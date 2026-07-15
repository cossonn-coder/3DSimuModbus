// =============================================================================
// POC D-001 — Comportement thread-safe reel de FluentModbus (JETABLE)
// =============================================================================
//
// Pourquoi ce fichier existe (cf. docs/dettes.md D-001, docs/sprints/sprint_01_brief.md) :
//   Avant de figer l'API des 3 briques C# (loader / datastore / serveur), on veut
//   OBSERVER comment le ModbusTcpServer de FluentModbus se comporte quand deux acteurs
//   touchent son buffer en meme temps :
//     - un thread de fond (ici : une horloge qui joue la future boucle de simulation)
//       ecrit le buffer toutes les 100 ms (heartbeat + quelques bits de retour),
//     - un client Modbus (testbench/io_scanner_sim.py, qui joue le M580) martele
//       FC3 (lecture zone ret) et FC16 (ecriture zone cmd) a cadence fixe.
//
// Hypothese a confirmer = "Arch A" (actee dans docs/memory.md) :
//   1. server.GetHoldingRegisters() + lock(server.Lock) permettent une copie atomique
//      de quelques mots, SANS tearing ni exception, meme sous scan repete ;
//   2. tenir le verrou le temps d'une copie de ~3 mots ne bloque pas le service Modbus ;
//   3. l'adressage est SANS decalage : le registre d'indice n correspond a %MWn.
//
// IMPORTANT : ce POC EST l'experience. Si l'API differe (noms de methodes, mode
// synchrone/asynchrone, signature du verrou), il ne compile pas / ne tourne pas comme
// prevu — et CE constat est justement le resultat D-001 a consigner dans
// docs/notes/NOTES_sprint_01.md, avant de figer la version exacte de FluentModbus.
//
// Ce fichier sera SUPPRIME une fois D-001 documente. Il ne reflete pas le style final
// des briques (adresses en dur ici = tolere pour un POC ; le code de prod resout tout
// depuis le pivot).

using System.Net;
using FluentModbus;

// --- Table d'echange = miroir du pivot machine_carrousel.json (bases figees Phase 0) --
//     cmd : %MW100 (1 mot)      ret : %MW200 (2 mots, heartbeat en mot 0)   unit_id = 1
const byte UnitId   = 1;     // pivot modbus.unit_id (le M580 scrutera cet identifiant)
const int CmdWord   = 100;   // %MW100 : mot de commandes (PLC -> sim, FC16)
const int HbWord    = 200;   // %MW200 : heartbeat (compteur +1 / 100 ms)
const int RetBits   = 201;   // %MW201 : S11/S12/S21/S22/B1/B2/KM1_AUX

const int BitCmdRun = 0;     // cmd %MW100 bit 0 = KM1.cmd_run
const int BitKm1Aux = 6;     // ret %MW201 bit 6 = KM1_AUX (retour de marche)
const int BitWitness = 0;    // ret %MW201 bit 0 (S11) : temoin qui clignote

// FluentModbus : serveur ASYNCHRONE par defaut => il traite les requetes client sur son
// propre thread. On se synchronise avec ce thread via l'objet server.Lock. C'est
// exactement ce que le POC valide : est-ce suffisant pour une copie coherente ?
using var server = new ModbusTcpServer();
// TROUVAILLE D-001 (unit id) : par defaut le ModbusTcpServer ne sert QUE l'unit 0 et
// FERME la connexion pour tout autre identifiant. Le pivot impose unit_id=1 => il faut
// declarer explicitement l'unite. Le buffer est alors PAR unite : GetHoldingRegisters(UnitId).
server.AddUnit(UnitId);
server.Start(IPAddress.Any);   // port 502 par defaut ; pare-feu Windows entrant TCP 502 requis
Console.WriteLine($"POC D-001 : serveur Modbus TCP sur 0.0.0.0:502, unit {UnitId} (Ctrl+C pour arreter).");
Console.WriteLine($"Cote client :  python testbench/io_scanner_sim.py --unit {UnitId} --run 1 --period 0.1");

// Arret propre sur Ctrl+C.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Boucle d'horloge = maquette de la future boucle de simulation. A chaque cycle 100 ms :
//   (1) snapshot debut de tick : lit le mot cmd,
//   (2) "cinematique" bidon : heartbeat++ ; KM1_AUX recopie cmd_run ; bit temoin clignote,
//   (3) publication fin de tick : ecrit les 2 mots ret.
// Tout l'acces au buffer se fait sous lock(server.Lock) => c'est l'hypothese Arch A.
ushort heartbeat = 0;
long tick = 0;

// TROUVAILLE D-001 : Span<short> est un *ref struct*. Le C# INTERDIT qu'un ref struct
// vive dans une methode async (il ne peut pas etre stocke dans la machine a etats que
// le compilateur genere autour d'un await). Comme la boucle ci-dessous est async
// (await Task.Delay), tout l'acces buffer est confine dans cette fonction SYNCHRONE.
// C'est representatif du runtime final : l'acces au buffer FluentModbus se fera dans
// _PhysicsProcess (synchrone), jamais dans du code async. A consigner dans les NOTES.
bool PumpOnce()
{
    lock (server.Lock)
    {
        // GetHoldingRegisters(UnitId) : buffer de CETTE unite, indexe par adresse
        // protocole. regs[n] <-> %MWn (aucun decalage).
        Span<short> regs = server.GetHoldingRegisters(UnitId);

        // TROUVAILLE D-001 (endianness) : le buffer natif est en LITTLE-endian (x86)
        // alors que le fil Modbus est BIG-endian. Un acces brut (regs[n]) fait lire au
        // client standard (pymodbus, M580) des octets INVERSES (0x0001 -> 0x0100).
        // FluentModbus fournit Get/SetBigEndian<T> pour presenter la valeur conforme.
        // On passe donc TOUJOURS par ces helpers au bord FluentModbus.

        // (1) Snapshot cmd (lecture big-endian conforme).
        ushort cmd = regs.GetBigEndian<ushort>(CmdWord);
        bool run = (cmd & (1 << BitCmdRun)) != 0;

        // (2) Cinematique minimale : juste de quoi voir bouger la trame cote client.
        heartbeat++;
        ushort ret = 0;
        if (run) ret |= (ushort)(1 << BitKm1Aux);            // KM1_AUX = echo de cmd_run
        if ((tick & 1) == 0) ret |= (ushort)(1 << BitWitness); // temoin qui clignote

        // (3) Publication ret (ecriture big-endian conforme).
        regs.SetBigEndian<ushort>(HbWord, heartbeat);
        regs.SetBigEndian<ushort>(RetBits, ret);
        return run;
    }
}

try
{
    while (!cts.IsCancellationRequested)
    {
        bool cmdRun = PumpOnce();

        // Trace legere (1 ligne / seconde) pour suivre le POC cote serveur.
        if (tick % 10 == 0)
            Console.WriteLine($"[tick {tick,6}] HB={heartbeat,5}  cmd_run={(cmdRun ? 1 : 0)}");

        tick++;
        await Task.Delay(100, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Sortie normale sur Ctrl+C.
}

Console.WriteLine("POC D-001 arrete.");

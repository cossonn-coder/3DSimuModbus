// =============================================================================
// ModbusServer — serveur Modbus TCP (FluentModbus) branche sur le datastore (Arch A)
// =============================================================================
//
// Role : encapsuler le ModbusTcpServer de FluentModbus et faire le PONT entre son
// buffer interne (le fil Modbus, vu par le M580) et le ModbusDataStore (la source de
// verite, cote simulation). C'est la derniere piece du transport Modbus : apres elle,
// la chaine FC3 (lecture retours) / FC16 (ecriture commandes) tourne bout-en-bout.
//
// --- Place dans l'architecture (Arch A, actee 2026-07-14) ------------------------------
// Le datastore est la SOURCE DE VERITE. Le buffer FluentModbus n'est qu'un detail PRIVE
// de cette classe, recopie <-> datastore UNE FOIS PAR TICK physique, sous server.Lock :
//     debut de tick : buffer cmd  -> datastore   (PullCommands)
//     [ ... la boucle de simulation (brique 4) lit cmd, calcule, prepare ret ... ]
//     fin de tick   : datastore ret -> buffer     (PushReturns)
// Latence commande -> retour mesuree au POC D-001 = 1 tick (conforme).
//
// --- Serveur PASSIF (pas d'horloge interne) --------------------------------------------
// ModbusServer n'a AUCUN timer. C'est le thread appelant (la boucle physique / le futur
// _PhysicsProcess, brique 4) qui rythme Pull puis Push. Une seule horloge dans le systeme
// (celle du tick physique) : pas de course entre deux cadences. Le thread propre de
// FluentModbus, lui, ne fait que servir les requetes TCP du client ; il ne touche NI le
// scene tree Godot NI le datastore.
//
// --- ModbusServer est le SEUL detenteur du format fil (endianness) ---------------------
// Le buffer natif de FluentModbus est en LITTLE-endian (x86) ; le fil Modbus est
// BIG-endian. Un acces brut (buffer[n]) presenterait au M580/pymodbus des octets INVERSES
// (0x1234 lu 0x3412). La traduction se fait ICI, REGISTRE PAR REGISTRE, via Get/SetBigEndian.
// Le datastore, lui, ne manipule que des mots en ordre hote (numeriques) : il reste
// endian-agnostique, tout comme la simulation. Cette classe est la frontiere unique.
//
// --- Contraintes POC D-001 re-imposees (non negociables) -------------------------------
//   1. server.AddUnit(unit_id) : sans ca, le serveur ne sert que l'unite 0 et FERME la
//      connexion pour tout autre identifiant. Le pivot impose unit_id=1.
//   2. Acces buffer SYNCHRONE : GetHoldingRegisters() rend un Span<short> (ref struct) qui
//      NE PEUT PAS vivre dans une methode async. Tout le pont Pull/Push est synchrone.
//   3. Toujours Get/SetBigEndian<ushort> (cf. bloc endianness ci-dessus).
//
// --- Aucune adresse en dur -------------------------------------------------------------
// Port, unit_id et bases de zones (%MW100/%MW200) sont TOUS resolus depuis le pivot au
// constructeur. Les tailles de zone viennent du datastore (lui-meme dimensionne par le
// pivot). Aucune constante d'adresse dans ce fichier.

using System.Net;
using CarrouselCore;
using FluentModbus;

namespace CarrouselServer;

/// <summary>
/// Serveur Modbus TCP branche sur le <see cref="ModbusDataStore"/> (Arch A).
/// Passif : le thread appelant declenche <see cref="PullCommands"/> en debut de tick et
/// <see cref="PushReturns"/> en fin de tick, sous <c>server.Lock</c>. Seul endroit qui
/// connait le format fil big-endian.
/// </summary>
public sealed class ModbusServer : IDisposable
{
    private readonly ModbusDataStore _store;
    private readonly ModbusTcpServer _server;

    // Parametres reseau et disposition memoire, figes au constructeur depuis le pivot.
    private readonly IPAddress _bind;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly int _cmdBase;   // adresse %MW absolue de la base de la zone cmd (100)
    private readonly int _retBase;   // adresse %MW absolue de la base de la zone ret (200)

    // Tailles des deux zones : tirees du datastore (lui-meme dimensionne par le pivot).
    // On les capture pour eviter d'appeler les proprietes dans les boucles chaudes Pull/Push.
    private readonly int _cmdCount;
    private readonly int _retCount;

    private bool _disposed;

    /// <summary>
    /// Resout port, unit_id et bases de zones depuis le pivot, garde la reference au datastore
    /// et cree le serveur FluentModbus (pas encore demarre : voir <see cref="Start"/>).
    /// </summary>
    /// <param name="bind">
    /// Interface d'ecoute. Par defaut <see cref="IPAddress.Any"/> : le M580 est un client
    /// DISTANT sur le LAN, le serveur doit ecouter sur toutes les interfaces (regle de
    /// pare-feu Windows entrante TCP <see cref="_port"/> prevue, cf. docs/memory.md). Passer
    /// <see cref="IPAddress.Loopback"/> pour un test local isole.
    /// </param>
    public ModbusServer(PivotModel pivot, ModbusDataStore store, IPAddress? bind = null)
    {
        ArgumentNullException.ThrowIfNull(pivot);
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _bind = bind ?? IPAddress.Any;
        _port = pivot.Port;
        _unitId = pivot.UnitId;

        // Bases absolues des zones : le pont slice le buffer serveur a partir de la base
        // %MW de chaque zone. Jamais de 100/200 en dur — tout vient du pivot.
        _cmdBase = pivot.GetZone("cmd").Base;
        _retBase = pivot.GetZone("ret").Base;

        // Tailles : le datastore est la reference (une seule source pour les longueurs).
        _cmdCount = store.CommandWordCount;
        _retCount = store.ReturnWordCount;

        // Serveur asynchrone par defaut : il traite les requetes client sur son propre thread.
        // On se synchronise avec ce thread via server.Lock dans Pull/Push.
        _server = new ModbusTcpServer();
    }

    /// <summary>
    /// Declare l'unite du pivot (contrainte POC 1) puis demarre l'ecoute TCP sur
    /// <c>bind:port</c> resolus du pivot. A appeler une fois, avant le premier Pull/Push
    /// (typiquement dans le <c>_Ready</c> du noeud Godot, brique 4).
    /// </summary>
    public void Start()
    {
        // AddUnit AVANT Start : le buffer est alloue par unite ; sans cette declaration le
        // serveur refermerait toute connexion ciblant unit_id=1.
        _server.AddUnit(_unitId);
        _server.Start(new IPEndPoint(_bind, _port));
    }

    /// <summary>
    /// Pull (debut de tick) : recopie la zone cmd du buffer serveur (fil, big-endian) vers le
    /// datastore. Synchrone et sous <c>server.Lock</c> : copie atomique vis-a-vis du thread
    /// FluentModbus, sans tearing.
    /// </summary>
    public void PullCommands()
    {
        // Ordre de verrous : server.Lock (externe) PUIS le verrou interne du datastore
        // (pris par WriteCommandsFromWire). Toujours dans ce sens, jamais l'inverse.
        lock (_server.Lock)
        {
            // Buffer de CETTE unite, indexe par adresse protocole : buffer[n] <-> %MWn.
            Span<short> buffer = _server.GetHoldingRegisters(_unitId);

            // stackalloc : zero allocation, taille minuscule (1 mot). On traduit fil -> hote
            // registre par registre (seul endroit qui connait le big-endian).
            Span<ushort> cmd = stackalloc ushort[_cmdCount];
            for (int i = 0; i < _cmdCount; i++)
                cmd[i] = buffer.GetBigEndian<ushort>(_cmdBase + i);

            _store.WriteCommandsFromWire(cmd);
        }
    }

    /// <summary>
    /// Push (fin de tick) : recopie la zone ret du datastore vers le buffer serveur (fil,
    /// big-endian). Synchrone et sous <c>server.Lock</c>. Le M580 lira ces mots au prochain
    /// scan FC3, en voyant toujours un jeu de retours coherent (publie d'un bloc).
    /// </summary>
    public void PushReturns()
    {
        lock (_server.Lock)
        {
            Span<short> buffer = _server.GetHoldingRegisters(_unitId);

            Span<ushort> ret = stackalloc ushort[_retCount];
            _store.CopyReturnsToWire(ret);

            // Traduction hote -> fil, registre par registre.
            for (int i = 0; i < _retCount; i++)
                buffer.SetBigEndian<ushort>(_retBase + i, ret[i]);
        }
    }

    /// <summary>Arret propre du serveur (Q3 : stop simple, pas de gestion d'arret sous charge).</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _server.Stop();
        _server.Dispose();
    }
}

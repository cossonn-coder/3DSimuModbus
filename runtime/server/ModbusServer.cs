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
using System.Net.Sockets;
using System.Threading;
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

    // Vrai apres un Start() reussi, faux avant (et si Start a echoue). Sert de source au
    // voyant sante (S3.2) : « le serveur ecoute-t-il vraiment ? ». Ecrit sur le thread
    // appelant (Start), lu sur le meme thread principal Godot : pas de partage inter-thread,
    // un bool simple suffit.
    private bool _isListening;

    // Horodatage (Ticks UTC) de la DERNIERE ecriture cmd recue d'un client (FC16), 0 si jamais.
    // Ecrit par le handler RegistersChanged qui fire sur le THREAD SERVEUR FluentModbus, lu par
    // le thread principal Godot -> acces obligatoirement atomique. Un `long` n'est pas garanti
    // atomique en lecture/ecriture sur plateforme 32 bits : on passe donc TOUJOURS par
    // Interlocked (Exchange a l'ecriture, Read a la lecture). Pas de DateTime partage non atomique.
    private long _lastClientWriteTicksUtc;

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

        // --- Detection d'activite PLC via l'event RegistersChanged (D-Q3) --------------------
        // FluentModbus 5.3.2 leve RegistersChanged a chaque ecriture de holding register par un
        // client (FC6/FC16), sur son thread serveur. Deux interrupteurs a armer :
        //   - EnableRaisingEvents : maitre general (defaut false) — sans lui, aucun event.
        //   - AlwaysRaiseChangedEvent : lever MEME si la valeur ecrite est identique a l'actuelle.
        //     CRUCIAL ici : l'I/O Scanner du M580 reecrit la zone cmd A CHAQUE SCAN, le plus
        //     souvent avec la MEME valeur. Sans ce drapeau, un carrousel a l'arret (cmd stable)
        //     ne genererait plus aucun event et le voyant « PLC actif » s'eteindrait a tort.
        // Seul un CLIENT peut declencher cet event : nos propres PushReturns ecrivent le buffer
        // via GetHoldingRegisters (acces direct, hors chemin requete) et ne le levent donc pas.
        // => tout RegistersChanged = un client a ecrit = activite PLC reelle. On horodate.
        _server.EnableRaisingEvents = true;
        _server.AlwaysRaiseChangedEvent = true;
        _server.RegistersChanged += OnRegistersChanged;
    }

    /// <summary>
    /// Handler appele sur le THREAD SERVEUR FluentModbus a chaque ecriture cmd par un client.
    /// On ne touche NI le scene tree Godot NI le datastore : on se contente d'horodater de
    /// facon atomique (Interlocked) la derniere manifestation du PLC. C'est la brique du voyant
    /// « PLC actif » lu cote Godot (S3.2).
    /// </summary>
    private void OnRegistersChanged(object? sender, RegistersChangedEventArgs e)
    {
        Interlocked.Exchange(ref _lastClientWriteTicksUtc, System.DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// Declare l'unite du pivot (contrainte POC 1) puis demarre l'ecoute TCP sur
    /// <c>bind:port</c> resolus du pivot. A appeler une fois, avant le premier Pull/Push
    /// (typiquement dans le <c>_Ready</c> du noeud Godot, brique 4).
    /// </summary>
    public void Start()
    {
        // AddUnit AVANT le premier Start : le buffer est alloue par unite ; sans cette
        // declaration le serveur refermerait toute connexion ciblant unit_id=1. Le spike S5.4
        // a montre que l'unite (et son buffer) SURVIT a un Stop()/Start() ulterieur : on ne la
        // redeclare donc PAS dans Reconnect (voir StartListening / Reconnect ci-dessous).
        _server.AddUnit(_unitId);

        StartListening();
    }

    /// <summary>
    /// Pre-vol du port + demarrage effectif de l'ecoute FluentModbus, PARTAGE par <see cref="Start"/>
    /// (premier demarrage) et <see cref="Reconnect"/> (re-armement apres <see cref="Disconnect"/>).
    /// Ne touche PAS a AddUnit : l'unite est declaree une seule fois par Start et persiste (spike S5.4).
    /// Positionne <see cref="_isListening"/> a true seulement si le bind reussit ; sinon leve.
    /// </summary>
    private void StartListening()
    {
        var endpoint = new IPEndPoint(_bind, _port);

        // --- Pre-vol du port (solde la dette D-013) -----------------------------------------
        // On lie brievement un TcpListener sur bind:port AVANT de demarrer FluentModbus. Si le
        // port est deja pris (reliquat SimHost, autre serveur Modbus...), ce bind leve une
        // SocketException que l'on traduit en ModbusServerException FR claire. Interet : la
        // detection est INDEPENDANTE de la lib (robuste quel que soit le comportement interne
        // de FluentModbus, synchrone ou non) — c'est le meme filet que demo_sprint_02.ps1 pose
        // deja en externe, ramene DANS l'app. Fenetre de course Stop()->Start() negligeable
        // pour un demonstrateur mono-poste, et FluentModbus est de toute facon enveloppe ci-dessous.
        // Ce meme pre-vol vaut aussi pour Reconnect (D-013 : un re-bind qui echoue est VISIBLE).
        var probe = new TcpListener(_bind, _port);
        try
        {
            probe.Start();
        }
        catch (SocketException ex)
        {
            throw new ModbusServerException(
                $"impossible d'ecouter sur {_bind}:{_port} — port deja utilise (SimHost reliquat ?)", ex);
        }
        finally
        {
            probe.Stop();
        }

        // Filet de securite : si malgre le pre-vol FluentModbus echouait a lier (course, autre
        // cause reseau), on enveloppe pareil plutot que de laisser fuiter une exception opaque.
        try
        {
            _server.Start(endpoint);
        }
        catch (SocketException ex)
        {
            throw new ModbusServerException(
                $"impossible d'ecouter sur {_bind}:{_port} — echec du bind FluentModbus", ex);
        }

        _isListening = true;
    }

    /// <summary>
    /// Coupure TCP volontaire (S5.4, defaut de comm « perte de l'esclave ») : arrete l'ecoute et
    /// ferme les connexions clients. L'I/O Scanner du M580 passe alors en DEFAUT DE SCRUTATION —
    /// distinct d'un simple gel de donnees (RetFrozen). <see cref="IsListening"/> repasse a false.
    /// Idempotent : deja deconnecte ⇒ no-op (pas de double Stop). La simulation, elle, continue
    /// de tourner cote scene (la 3D bouge) ; c'est <see cref="Reconnect"/> qui retablit le transport.
    /// </summary>
    public void Disconnect()
    {
        if (!_isListening)
            return;

        _isListening = false;
        _server.Stop();
    }

    /// <summary>
    /// Retablit l'ecoute apres un <see cref="Disconnect"/> (« Reparer » cote IHM). Le spike S5.4 a
    /// confirme qu'un ModbusTcpServer FluentModbus 5.3.2 se REARME sur la MEME instance : Stop()
    /// puis Start(endpoint) re-ouvrent le port, l'unite et son buffer sont conserves, un client se
    /// reconnecte et relit les registres — inutile de recreer l'instance ni de rappeler AddUnit.
    /// Idempotent : deja a l'ecoute ⇒ no-op. Peut lever <see cref="ModbusServerException"/> si le
    /// re-bind du port echoue (port repris entre-temps) : l'exception est REMONTEE (D-013), jamais
    /// avalee — l'appelant (HUD) l'affiche comme l'echec de bind initial.
    /// </summary>
    public void Reconnect()
    {
        if (_isListening)
            return;

        StartListening();
    }

    /// <summary>
    /// Vrai uniquement apres un <see cref="Start"/> reussi. Reste faux si le bind a echoue
    /// (l'exception a alors deja ete levee). Source du voyant sante cote Godot (S3.2).
    /// </summary>
    public bool IsListening => _isListening;

    /// <summary>
    /// Horodatage UTC de la derniere ecriture cmd (FC16) recue d'un client, ou <c>null</c> tant
    /// qu'aucune n'a eu lieu. Compose atomiquement (Interlocked.Read) le compteur de ticks
    /// alimente par <see cref="OnRegistersChanged"/> sur le thread serveur. Cote Godot, comparer
    /// a <c>DateTime.UtcNow</c> donne « le PLC s'est-il manifeste recemment ? ».
    /// </summary>
    public System.DateTime? LastClientWriteUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastClientWriteTicksUtc);
            return ticks == 0 ? null : new System.DateTime(ticks, System.DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Pull (debut de tick) : recopie la zone cmd du buffer serveur (fil, big-endian) vers le
    /// datastore. Synchrone et sous <c>server.Lock</c> : copie atomique vis-a-vis du thread
    /// FluentModbus, sans tearing.
    /// </summary>
    public void PullCommands()
    {
        // Garde S5.4 : hors ecoute (TCP coupe volontairement, ou bind jamais reussi), le buffer
        // FluentModbus n'est pas servable — GetHoldingRegisters sur un serveur arrete leverait.
        // On sort en no-op pour que StepSim (physique + heartbeat interne) continue de tourner
        // pendant la coupure sans exception : la 3D bouge, mais plus rien ne transite sur le fil.
        if (!_isListening)
            return;

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
        // Meme garde que PullCommands : hors ecoute, pas de buffer a alimenter. La sim a tout de
        // meme prepare ses retours (heartbeat interne inclus), simplement personne ne les lit —
        // coherent avec « perte de l'esclave » cote M580 (message pedagogique S5.4).
        if (!_isListening)
            return;

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
        _isListening = false;
        // Desabonnement symetrique de l'abonnement du constructeur : evite qu'un event tardif
        // ne touche un objet dispose (le handler est inoffensif, mais on reste propre).
        _server.RegistersChanged -= OnRegistersChanged;
        _server.Stop();
        _server.Dispose();
    }
}

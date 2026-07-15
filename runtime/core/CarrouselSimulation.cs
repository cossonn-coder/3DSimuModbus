// =============================================================================
// CarrouselSimulation — la boucle de simulation (« le cerveau »), brique 4
// =============================================================================
//
// Role : a chaque tick physique, transformer les COMMANDES du PLC (zone cmd) en RETOURS
// physiquement plausibles (zone ret), en faisant avancer la cinematique scriptee de `dt`.
// C'est la piece qui debloque la chaine reelle bout-en-bout : sans elle, le serveur ne
// renvoyait que ce qu'on lui donnait (transport nu). Objet C# PUR (CarrouselCore) : il ne
// touche NI le scene tree Godot NI le buffer serveur — seulement le ModbusDataStore, et
// seulement depuis le thread physique (frontiere Arch A, cf. docs/memory.md).
//
// --- Composition root --------------------------------------------------------------------
// Cette classe est le point ou le pivot (adresses + params) rencontre les modeles purs :
// au constructeur, elle lit les params machine et RESOUT une fois pour toutes les `Signal`
// (offsets + masques de bits) ; ensuite, chaque Tick ne fait que decoder/encoder via ces
// Signal. Les modeles purs (CylinderState, ConveyorState) ignorent tout du Modbus : ils ne
// manipulent que des grandeurs physiques. La traduction bit <-> etat vit ICI.
//
// --- Perimetre brique 4a -----------------------------------------------------------------
// 4a gere : heartbeat + verins (S11/S12/S21/S22) + retour convoyeur (KM1_AUX). Les bits de
// presence B1/B2 restent a 0 (pas encore de palettes) : c'est la brique 4b qui les remplira,
// de facon additive, en ajoutant un PalletSet et en etendant Tick. Aucun des 4 scenarios
// pytest full-chain ne depend de B1/B2 : 4a suffit a tous les debloquer.
//
// --- Sequence d'un Tick (D-e : reconstruction complete de ret) ---------------------------
//   1. snapshot des commandes (debut de tick)      -> coherence intra-scan
//   2. decodage des bits cmd (cmd_run, cmd_extend)
//   3. avancement de la cinematique de `dt`
//   4. increment heartbeat (rollover ushort naturel)
//   5. reconstruction d'un ushort[] ret NEUF, bits encodes un a un
//   6. publication d'un bloc (fin de tick)         -> le PLC voit toujours un jeu coherent

namespace CarrouselCore;

/// <summary>
/// Boucle de simulation cinematique (brique 4). <see cref="Tick"/> lit les commandes du datastore,
/// avance les modeles purs de <c>dt</c>, puis publie les retours (heartbeat + fins de course +
/// retour convoyeur). Pur, deterministe, sans dependance Godot.
/// </summary>
public sealed class CarrouselSimulation
{
    // Regroupe un verin (etat physique) avec les trois Signal qui le relient au bus : c'est le
    // « cablage » entre un modele pur et ses adresses Modbus, resolu une fois au constructeur.
    private sealed class CylinderUnit
    {
        public CylinderState State { get; }
        public Signal CmdExtend { get; }
        public Signal RetRetracted { get; }
        public Signal RetExtended { get; }

        private CylinderUnit(CylinderState state, Signal cmdExtend, Signal retRetracted, Signal retExtended)
        {
            State = state;
            CmdExtend = cmdExtend;
            RetRetracted = retRetracted;
            RetExtended = retExtended;
        }

        public static CylinderUnit FromPivot(PivotModel pivot, string compId)
        {
            var c = pivot.GetComponent(compId);
            // Les temps du pivot sont en MILLISECONDES ; les modeles raisonnent en SECONDES
            // (comme `dt`). Conversion unique ici, jamais de melange d'unites plus loin.
            var state = new CylinderState(
                travelTimeS: c.GetParam("travel_time_ms") / 1000.0,
                retractedThreshold: c.GetParam("retracted_threshold"),
                extendedThreshold: c.GetParam("extended_threshold"),
                blockThreshold: c.GetParam("block_threshold"));

            return new CylinderUnit(
                state,
                pivot.GetSignal(compId, "cmd_extend"),
                pivot.GetSignal(compId, "ret_retracted"),
                pivot.GetSignal(compId, "ret_extended"));
        }
    }

    private readonly Signal _heartbeatSig;

    private readonly Signal _cmdRun;
    private readonly Signal _km1Aux;
    private readonly ConveyorState _conveyor;

    private readonly CylinderUnit _cyl1;
    private readonly CylinderUnit _cyl2;

    // Compteur heartbeat : ushort => rollover a 65535 -> 0 automatique (spec : rollover libre).
    private ushort _heartbeat;

    /// <summary>
    /// Resout les signaux et params depuis le pivot, et construit les modeles purs. Aucune I/O,
    /// aucun tick : l'etat cinematique demarre au repos (verins rentres, convoyeur arrete).
    /// </summary>
    public CarrouselSimulation(PivotModel pivot)
    {
        ArgumentNullException.ThrowIfNull(pivot);

        _heartbeatSig = pivot.Heartbeat;

        var km1 = pivot.GetComponent("KM1");
        _cmdRun = pivot.GetSignal("KM1", "cmd_run");
        _km1Aux = pivot.GetSignal("KM1", "ret_running");
        _conveyor = new ConveyorState(feedbackDelayS: km1.GetParam("feedback_delay_ms") / 1000.0);

        _cyl1 = CylinderUnit.FromPivot(pivot, "cylinder_1");
        _cyl2 = CylinderUnit.FromPivot(pivot, "cylinder_2");
    }

    // --- Accces lecture seule a l'etat interne (pour la 3D brique 5 et les tests) ---
    // On expose les modeles : la 3D lira Position, les tests liront les seuils. Par convention
    // les appelants NE font que lire (ne rappellent pas Advance) : la seule horloge est le Tick.

    /// <summary>Verin YV1 (poste 90°).</summary>
    public CylinderState Cylinder1 => _cyl1.State;

    /// <summary>Verin YV2 (poste 270°).</summary>
    public CylinderState Cylinder2 => _cyl2.State;

    /// <summary>Retour de marche du convoyeur (contact KM1_AUX).</summary>
    public ConveyorState Conveyor => _conveyor;

    /// <summary>Valeur courante du compteur heartbeat (dernier publie).</summary>
    public ushort Heartbeat => _heartbeat;

    /// <summary>
    /// Un pas de simulation : snapshot cmd -> avance la cinematique de <paramref name="dtSeconds"/>
    /// -> incremente le heartbeat -> reconstruit et publie la zone ret.
    /// </summary>
    public void Tick(ModbusDataStore store, double dtSeconds)
    {
        ArgumentNullException.ThrowIfNull(store);

        // 1. Snapshot des commandes en debut de tick : la sim travaille sur une photo figee,
        //    insensible a une ecriture PLC qui arriverait en cours de calcul (coherence intra-scan).
        ushort[] cmd = store.SnapshotCommands();

        // 2. Decodage des bits de commande via les Signal (offset relatif + masque). Jamais
        //    d'adresse ni de masque en dur : tout passe par le pivot (decision D-b).
        bool run = _cmdRun.ReadBit(cmd[_cmdRun.WordRel]);
        bool extend1 = _cyl1.CmdExtend.ReadBit(cmd[_cyl1.CmdExtend.WordRel]);
        bool extend2 = _cyl2.CmdExtend.ReadBit(cmd[_cyl2.CmdExtend.WordRel]);

        // 3. Avancement de la cinematique scriptee de dt (chaque modele est autonome).
        _conveyor.Advance(dtSeconds, run);
        _cyl1.State.Advance(dtSeconds, extend1);
        _cyl2.State.Advance(dtSeconds, extend2);

        // 4. Heartbeat : +1 par tick, rollover ushort naturel (preuve de vie pour le PLC).
        _heartbeat++;

        // 5. Reconstruction COMPLETE de ret (D-e) : un tableau neuf, tous les bits repositionnes.
        //    Le heartbeat est un mot ENTIER (word 0) ; les TOR (word 1) sont ecrits bit a bit.
        ushort[] ret = new ushort[store.ReturnWordCount];
        ret[_heartbeatSig.WordRel] = _heartbeat;

        _km1Aux.WriteBit(ref ret[_km1Aux.WordRel], _conveyor.IsRunning);
        WriteCylinder(ret, _cyl1);
        WriteCylinder(ret, _cyl2);
        // B1/B2 : laisses a 0 en 4a (brique 4b les remplira).

        // 6. Publication d'un bloc en fin de tick : le PLC lira un jeu de retours coherent.
        store.PublishReturns(ret);
    }

    /// <summary>Encode les deux fins de course d'un verin dans la zone ret.</summary>
    private static void WriteCylinder(ushort[] ret, CylinderUnit u)
    {
        u.RetRetracted.WriteBit(ref ret[u.RetRetracted.WordRel], u.State.IsRetracted);
        u.RetExtended.WriteBit(ref ret[u.RetExtended.WordRel], u.State.IsExtended);
    }
}

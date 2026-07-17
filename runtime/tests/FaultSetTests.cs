// =============================================================================
// FaultSetTests — le coeur d'injection de defauts, teste sans Godot (sprint 5)
// =============================================================================
//
// Deux familles de tests :
//   1. FaultSet / FaultCatalog PURS : mutations, dispatch, catalogue (aucun pivot, aucune sim).
//   2. INJECTION dans CarrouselSimulation : on branche la vraie chaine (pivot -> datastore -> sim)
//      comme CarrouselSimulationTests, on force un defaut, on Tick, et on relit `ret` comme le
//      ferait le PLC. Preuve que le defaut se traduit en retours SANS forcer le datastore.

using CarrouselCore;
using Xunit;

namespace CarrouselCore.Tests;

public class FaultSetTests
{
    // --- Chargement du pivot reel (meme helper que CarrouselSimulationTests) ---

    private static readonly PivotModel Pivot = PivotModel.Load(PivotPath());

    private static string PivotPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "pivot", "machine_carrousel.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("pivot introuvable depuis " + AppContext.BaseDirectory);
    }

    private static void SetCmd(ModbusDataStore store, Signal sig, bool value)
    {
        ushort[] cmd = store.SnapshotCommands();
        sig.WriteBit(ref cmd[sig.WordRel], value);
        store.WriteCommandsFromWire(cmd);
    }

    private static bool GetRet(ModbusDataStore store, Signal sig)
    {
        Span<ushort> ret = new ushort[store.ReturnWordCount];
        store.CopyReturnsToWire(ret);
        return sig.ReadBit(ret[sig.WordRel]);
    }

    private static ushort GetRetWord(ModbusDataStore store, int wordRel)
    {
        Span<ushort> ret = new ushort[store.ReturnWordCount];
        store.CopyReturnsToWire(ret);
        return ret[wordRel];
    }

    // =====================================================================
    // 1. FaultSet pur : mutations, dispatch, lectures
    // =====================================================================

    [Fact]
    public void SetPhysical_None_efface_le_defaut()
    {
        var f = new FaultSet();
        f.SetPhysical("cylinder_1", PhysicalFault.CylinderStuckRetracted);
        Assert.Equal(PhysicalFault.CylinderStuckRetracted, f.GetPhysical("cylinder_1"));
        Assert.True(f.HasAnyFault("cylinder_1"));

        f.SetPhysical("cylinder_1", PhysicalFault.None);
        Assert.Equal(PhysicalFault.None, f.GetPhysical("cylinder_1"));
        Assert.False(f.HasAnyFault("cylinder_1"));
    }

    [Fact]
    public void SetSensorStuck_None_libere_le_signal()
    {
        var f = new FaultSet();
        f.SetSensorStuck("cylinder_1", "ret_extended", StuckMode.High);
        Assert.Equal(StuckMode.High, f.GetSensorStuck("cylinder_1", "ret_extended"));

        f.SetSensorStuck("cylinder_1", "ret_extended", StuckMode.None);
        Assert.Equal(StuckMode.None, f.GetSensorStuck("cylinder_1", "ret_extended"));
    }

    [Fact]
    public void ClearComponent_efface_physique_et_capteurs_du_composant_seulement()
    {
        var f = new FaultSet();
        f.SetPhysical("cylinder_1", PhysicalFault.CylinderStuckMidStroke);
        f.SetSensorStuck("cylinder_1", "ret_extended", StuckMode.Low);
        f.SetSensorStuck("cylinder_2", "ret_extended", StuckMode.High);   // autre composant : preserve

        f.ClearComponent("cylinder_1");

        Assert.False(f.HasAnyFault("cylinder_1"));
        Assert.Equal(PhysicalFault.None, f.GetPhysical("cylinder_1"));
        Assert.Equal(StuckMode.None, f.GetSensorStuck("cylinder_1", "ret_extended"));
        // cylinder_2 intact.
        Assert.Equal(StuckMode.High, f.GetSensorStuck("cylinder_2", "ret_extended"));
    }

    [Fact]
    public void Apply_dispatch_les_trois_familles()
    {
        var f = new FaultSet();

        f.Apply(new FaultCommand(FaultKind.Physical, "conveyor", Physical: PhysicalFault.ConveyorSlip));
        Assert.Equal(PhysicalFault.ConveyorSlip, f.GetPhysical("conveyor"));

        f.Apply(new FaultCommand(FaultKind.SensorStuck, "presence_station_1", SignalName: "ret_active", Stuck: StuckMode.High));
        Assert.Equal(StuckMode.High, f.GetSensorStuck("presence_station_1", "ret_active"));

        f.Apply(new FaultCommand(FaultKind.Repair, "conveyor"));
        Assert.Equal(PhysicalFault.None, f.GetPhysical("conveyor"));
    }

    [Fact]
    public void Apply_SensorStuck_sans_signal_echoue()
    {
        var f = new FaultSet();
        Assert.Throws<ArgumentException>(
            () => f.Apply(new FaultCommand(FaultKind.SensorStuck, "cylinder_1", Stuck: StuckMode.High)));
    }

    [Fact]
    public void ActiveStucks_liste_les_capteurs_bloques()
    {
        var f = new FaultSet();
        f.SetSensorStuck("cylinder_1", "ret_extended", StuckMode.High);
        f.SetSensorStuck("presence_station_1", "ret_active", StuckMode.Low);

        var stucks = f.ActiveStucks;
        Assert.Equal(2, stucks.Count);
        Assert.Contains(("cylinder_1", "ret_extended", StuckMode.High), stucks);
        Assert.Contains(("presence_station_1", "ret_active", StuckMode.Low), stucks);
    }

    // =====================================================================
    // 2. FaultCatalog : quels defauts pour quel composant
    // =====================================================================

    [Fact]
    public void Catalog_cylindre_2_physiques_plus_stuck_par_S11_S12()
    {
        var cmds = FaultCatalog.ApplicableTo(Pivot.GetComponent("cylinder_1"));

        // 2 physiques + (ret_retracted Low/High) + (ret_extended Low/High) = 6, aucun Repair.
        Assert.Equal(6, cmds.Count);
        Assert.DoesNotContain(cmds, c => c.Kind == FaultKind.Repair);
        Assert.Contains(cmds, c => c.Kind == FaultKind.Physical && c.Physical == PhysicalFault.CylinderStuckRetracted);
        Assert.Contains(cmds, c => c.Kind == FaultKind.Physical && c.Physical == PhysicalFault.CylinderStuckMidStroke);
        Assert.Contains(cmds, c => c.Kind == FaultKind.SensorStuck && c.SignalName == "ret_retracted" && c.Stuck == StuckMode.Low);
        Assert.Contains(cmds, c => c.Kind == FaultKind.SensorStuck && c.SignalName == "ret_retracted" && c.Stuck == StuckMode.High);
        Assert.Contains(cmds, c => c.Kind == FaultKind.SensorStuck && c.SignalName == "ret_extended" && c.Stuck == StuckMode.Low);
        Assert.Contains(cmds, c => c.Kind == FaultKind.SensorStuck && c.SignalName == "ret_extended" && c.Stuck == StuckMode.High);
    }

    [Fact]
    public void Catalog_capteur_presence_aucun_physique_stuck_sur_ret_active()
    {
        var cmds = FaultCatalog.ApplicableTo(Pivot.GetComponent("presence_station_1"));

        Assert.Equal(2, cmds.Count);
        Assert.DoesNotContain(cmds, c => c.Kind == FaultKind.Physical);
        Assert.Contains(cmds, c => c.SignalName == "ret_active" && c.Stuck == StuckMode.Low);
        Assert.Contains(cmds, c => c.SignalName == "ret_active" && c.Stuck == StuckMode.High);
    }

    [Fact]
    public void Catalog_convoyeur_patine_plus_stuck_sur_ret_running()
    {
        var cmds = FaultCatalog.ApplicableTo(Pivot.GetComponent("conveyor"));

        // 1 physique (patine) + ret_running Low/High = 3. cmd_run est en zone `cmd` : exclu.
        Assert.Equal(3, cmds.Count);
        Assert.Contains(cmds, c => c.Kind == FaultKind.Physical && c.Physical == PhysicalFault.ConveyorSlip);
        Assert.Contains(cmds, c => c.SignalName == "ret_running" && c.Stuck == StuckMode.Low);
        Assert.Contains(cmds, c => c.SignalName == "ret_running" && c.Stuck == StuckMode.High);
        Assert.DoesNotContain(cmds, c => c.SignalName == "cmd_run");
    }

    // =====================================================================
    // 3. Injection dans la simulation : defaut physique
    // =====================================================================

    [Fact]
    public void CylinderStuckRetracted_le_verin_ne_sort_jamais()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var s11 = Pivot.GetSignalByTag("S11");   // retracted
        var s12 = Pivot.GetSignalByTag("S12");   // extended

        sim.Faults.SetPhysical("cylinder_1", PhysicalFault.CylinderStuckRetracted);
        SetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend"), true);   // commande SORTIE

        // Malgre la commande, le verin reste rentre : S11=1, S12=0, meme apres largement travel_time.
        for (int i = 0; i < 10; i++) sim.Tick(store, 0.1);

        Assert.True(GetRet(store, s11));
        Assert.False(GetRet(store, s12));
    }

    [Fact]
    public void CylinderStuckMidStroke_gele_la_position_entre_deux_ticks()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        SetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend"), true);

        // Sortie partielle (0.2 s sur 0.5 s de course -> ~0.4), SANS defaut.
        sim.Tick(store, 0.1);
        sim.Tick(store, 0.1);
        double frozen = sim.Cylinder1.Position;
        Assert.InRange(frozen, 0.05, 0.95);   // bien a mi-course, ni rentre ni sorti

        // On coince le verin : la position ne bouge plus, ni sortie ni rentree, quelle que soit la commande.
        sim.Faults.SetPhysical("cylinder_1", PhysicalFault.CylinderStuckMidStroke);
        for (int i = 0; i < 10; i++) sim.Tick(store, 0.1);
        Assert.Equal(frozen, sim.Cylinder1.Position, precision: 9);

        // Commande retiree : toujours gele (ni rappel ressort).
        SetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend"), false);
        for (int i = 0; i < 10; i++) sim.Tick(store, 0.1);
        Assert.Equal(frozen, sim.Cylinder1.Position, precision: 9);
    }

    [Fact]
    public void ConveyorSlip_km1_aux_reste_a_1_mais_palettes_et_B1_figes()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var aux = Pivot.GetSignalByTag("KM1_AUX");

        sim.Faults.SetPhysical("conveyor", PhysicalFault.ConveyorSlip);
        SetCmd(store, Pivot.GetSignal("KM1", "cmd_run"), true);

        // Sans defaut, 44 ticks amenent une palette au poste 90° (B1=1). Avec patinage : rien ne bouge.
        double[] before = sim.Pallets.AnglesDeg.ToArray();
        for (int i = 0; i < 44; i++) sim.Tick(store, 0.1);

        Assert.True(GetRet(store, aux));   // le contacteur confirme la marche (KM1_AUX=1)
        Assert.False(GetRet(store, Pivot.GetSignal("B1", "ret_active")));   // aucune palette arrivee
        Assert.False(GetRet(store, Pivot.GetSignal("B2", "ret_active")));
        // Palettes strictement immobiles.
        double[] after = sim.Pallets.AnglesDeg.ToArray();
        for (int i = 0; i < before.Length; i++)
            Assert.Equal(before[i], after[i], precision: 9);
    }

    // =====================================================================
    // 4. Injection : capteur-bloque (masque APRES encodage)
    // =====================================================================

    [Fact]
    public void SensorStuck_High_force_S12_a_1_sans_verin_sorti()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var s12 = Pivot.GetSignalByTag("S12");

        sim.Faults.SetSensorStuck("cylinder_1", "ret_extended", StuckMode.High);
        sim.Tick(store, 0.1);   // aucun mouvement : S12 nominal serait 0

        Assert.True(GetRet(store, s12));
    }

    [Fact]
    public void SensorStuck_Low_force_S12_a_0_verin_pourtant_sorti()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var s12 = Pivot.GetSignalByTag("S12");

        // Verin reellement sorti (S12 nominal = 1), puis on bloque le capteur a 0.
        SetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend"), true);
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);
        Assert.True(GetRet(store, s12));

        sim.Faults.SetSensorStuck("cylinder_1", "ret_extended", StuckMode.Low);
        sim.Tick(store, 0.1);
        Assert.False(GetRet(store, s12));
    }

    [Fact]
    public void SensorStuck_High_sur_B1_au_repos()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);

        sim.Faults.SetSensorStuck("presence_station_1", "ret_active", StuckMode.High);
        sim.Tick(store, 0.1);   // convoyeur arrete : B1 nominal = 0

        Assert.True(GetRet(store, Pivot.GetSignal("B1", "ret_active")));
    }

    [Fact]
    public void SensorStuck_High_sur_ret_running_km1_aux_colle_sans_commande()
    {
        // « KM1_AUX collé » : pas un cas special, juste le capteur-bloque du signal ret_running.
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var aux = Pivot.GetSignalByTag("KM1_AUX");

        sim.Faults.SetSensorStuck("conveyor", "ret_running", StuckMode.High);
        sim.Tick(store, 0.1);   // aucune commande de marche : KM1_AUX nominal = 0

        Assert.True(GetRet(store, aux));
    }

    // =====================================================================
    // 5. Injection : gel `ret` (RetFrozen)
    // =====================================================================

    [Fact]
    public void RetFrozen_gele_heartbeat_et_bits_ret_sur_plusieurs_ticks()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);

        // Quelques ticks nominaux, puis on note l'image `ret` publiee.
        for (int i = 0; i < 3; i++) sim.Tick(store, 0.1);
        ushort word0 = GetRetWord(store, 0);   // heartbeat
        ushort word1 = GetRetWord(store, 1);   // TOR
        ushort hb = sim.Heartbeat;

        sim.Faults.RetFrozen = true;
        for (int i = 0; i < 5; i++) sim.Tick(store, 0.1);

        // Le datastore conserve strictement les derniers `ret` : heartbeat fige, TOR figes.
        Assert.Equal(word0, GetRetWord(store, 0));
        Assert.Equal(word1, GetRetWord(store, 1));
        Assert.Equal(hb, sim.Heartbeat);   // le compteur interne n'avance pas non plus
    }

    [Fact]
    public void RetFrozen_leve_republie_le_heartbeat_qui_reprend()
    {
        // Le gel est reversible : une fois RetFrozen remis a false, le heartbeat reprend son cours.
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        sim.Tick(store, 0.1);
        ushort before = sim.Heartbeat;

        sim.Faults.RetFrozen = true;
        for (int i = 0; i < 5; i++) sim.Tick(store, 0.1);
        Assert.Equal(before, sim.Heartbeat);

        sim.Faults.RetFrozen = false;
        sim.Tick(store, 0.1);
        Assert.Equal((ushort)(before + 1), sim.Heartbeat);
    }

    // =====================================================================
    // 6. Reparation : retour au nominal
    // =====================================================================

    [Fact]
    public void ClearComponent_restaure_le_comportement_nominal_du_verin()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var s12 = Pivot.GetSignalByTag("S12");

        // Verin coince rentre : ne sort pas malgre la commande.
        sim.Faults.SetPhysical("cylinder_1", PhysicalFault.CylinderStuckRetracted);
        SetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend"), true);
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);
        Assert.False(GetRet(store, s12));

        // Reparation : le verin repart et atteint la fin de course sortie.
        sim.Faults.ClearComponent("cylinder_1");
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);
        Assert.True(GetRet(store, s12));
    }

    [Fact]
    public void Sans_aucun_defaut_le_comportement_reste_nominal()
    {
        // Garde-fou explicite : un FaultSet vide ne doit RIEN changer (les 4 pytest full-chain
        // reposent sur ce nominal). On rejoue le scenario verin de reference.
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var s11 = Pivot.GetSignalByTag("S11");
        var s12 = Pivot.GetSignalByTag("S12");

        Assert.False(sim.Faults.HasAnyFault("cylinder_1"));

        SetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend"), true);
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);
        Assert.True(GetRet(store, s12));
        Assert.False(GetRet(store, s11));
    }
}

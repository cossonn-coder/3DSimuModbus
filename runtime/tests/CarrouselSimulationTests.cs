// =============================================================================
// CarrouselSimulationTests — la boucle de sim testee de bout en bout, sans Godot
// =============================================================================
//
// Ces tests branchent la VRAIE chaine C# (pivot reel -> datastore -> simulation) mais SANS
// reseau ni Godot : on ecrit les commandes directement dans le datastore (comme le ferait le
// serveur apres un FC16), on Tick, puis on relit ret (comme le ferait un FC3). C'est le miroir
// in-process des 4 scenarios pytest full-chain — memes comportements, sans le port 502.

using CarrouselCore;
using Xunit;

namespace CarrouselCore.Tests;

public class CarrouselSimulationTests
{
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

    // Ecrit un bit de commande dans le datastore, comme le ferait le serveur apres un FC16.
    private static void SetCmd(ModbusDataStore store, Signal sig, bool value)
    {
        // On passe par le pont serveur (WriteCommandsFromWire) sur une copie du mot courant :
        // il n'y a qu'un mot de commande, on le reconstruit a partir du bit voulu.
        ushort[] cmd = store.SnapshotCommands();
        sig.WriteBit(ref cmd[sig.WordRel], value);
        store.WriteCommandsFromWire(cmd);
    }

    // Relit un bit de retour, comme le ferait le PLC apres un FC3.
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

    [Fact]
    public void Heartbeat_incremente_a_chaque_tick()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);

        sim.Tick(store, 0.1);
        ushort h1 = GetRetWord(store, Pivot.Heartbeat.WordRel);
        sim.Tick(store, 0.1);
        ushort h2 = GetRetWord(store, Pivot.Heartbeat.WordRel);

        Assert.Equal(1, h1);
        Assert.Equal(2, h2);
    }

    [Fact]
    public void Heartbeat_rollover_65535()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);

        // 65535 ticks -> heartbeat = 65535 ; le 65536e doit revenir a 0 (rollover ushort).
        for (int i = 0; i < 65535; i++) sim.Tick(store, 0.1);
        Assert.Equal((ushort)65535, sim.Heartbeat);
        sim.Tick(store, 0.1);
        Assert.Equal((ushort)0, sim.Heartbeat);
        Assert.Equal(0, GetRetWord(store, Pivot.Heartbeat.WordRel));
    }

    [Fact]
    public void Convoyeur_colle_km1_aux_apres_le_delai()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var run = Pivot.GetSignal("KM1", "cmd_run");
        var aux = Pivot.GetSignalByTag("KM1_AUX");

        SetCmd(store, run, true);
        sim.Tick(store, 0.1);   // > feedback_delay (0.05 s)
        Assert.True(GetRet(store, aux));

        SetCmd(store, run, false);
        sim.Tick(store, 0.1);
        Assert.False(GetRet(store, aux));
    }

    [Theory]
    [InlineData("cylinder_1", "S12", "S11")]
    [InlineData("cylinder_2", "S22", "S21")]
    public void Verin_sortie_puis_rappel(string comp, string extendedTag, string retractedTag)
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var extend = Pivot.GetSignal(comp, "cmd_extend");
        var fcExt = Pivot.GetSignalByTag(extendedTag);
        var fcRet = Pivot.GetSignalByTag(retractedTag);

        // Sortie : 6 ticks de 0.1 s > travel_time (0.5 s) -> fin de course sortie.
        SetCmd(store, extend, true);
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);
        Assert.True(GetRet(store, fcExt));
        Assert.False(GetRet(store, fcRet));

        // Rappel ressort : commande retiree, meme duree -> fin de course rentree.
        SetCmd(store, extend, false);
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);
        Assert.True(GetRet(store, fcRet));
        Assert.False(GetRet(store, fcExt));
    }

    [Fact]
    public void Presence_b1_b2_fausses_au_repos()
    {
        // Convoyeur a l'arret : les palettes restent a 0/120/240, aucune n'est dans une fenetre
        // de poste (90/270) -> B1 et B2 a 0. (En 4a ce test verrouillait « pas encore de palettes » ;
        // en 4b il verrouille le comportement REEL au repos.)
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        sim.Tick(store, 0.1);
        Assert.False(GetRet(store, Pivot.GetSignal("B1", "ret_active")));
        Assert.False(GetRet(store, Pivot.GetSignal("B2", "ret_active")));
    }

    [Fact]
    public void B1_passe_a_1_quand_une_palette_atteint_le_poste_90()
    {
        // Convoyeur en marche, aucun verin : la palette partie de 0° tourne (20°/s) et entre dans
        // la fenetre du poste 90° (±4°) apres ~44 ticks (pos ~= 88°). B2 reste a 0 (rien pres de 270).
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        SetCmd(store, Pivot.GetSignal("KM1", "cmd_run"), true);

        for (int i = 0; i < 44; i++) sim.Tick(store, 0.1);

        Assert.True(GetRet(store, Pivot.GetSignal("B1", "ret_active")));
        Assert.False(GetRet(store, Pivot.GetSignal("B2", "ret_active")));
    }

    [Fact]
    public void YV1_sorti_bloque_une_palette_au_poste_et_maintient_B1()
    {
        // Convoyeur en marche + YV1 sorti : une palette vient buter au poste 90° et s'y park.
        // B1 doit etre a 1 et le RESTER (la palette bloquee reste dans la fenetre), tour apres tour.
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        SetCmd(store, Pivot.GetSignal("KM1", "cmd_run"), true);
        SetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend"), true);

        // Assez de ticks pour : sortir YV1 (0.5 s), amener une palette au poste, puis tourner encore.
        for (int i = 0; i < 120; i++) sim.Tick(store, 0.1);
        Assert.True(GetRet(store, Pivot.GetSignal("B1", "ret_active")));

        // Une palette est bien immobilisee a ~90° (blocage effectif, pas un simple passage).
        var angles = sim.Pallets.AnglesDeg;
        Assert.Contains(angles, a => Math.Abs(a - 90.0) < 1.0);
    }

    [Fact]
    public void Deux_verins_independants_dans_le_meme_mot()
    {
        // S11..S22 partagent le mot ret 1 : sortir YV1 ne doit pas perturber les bits de YV2.
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        SetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend"), true);
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);

        Assert.True(GetRet(store, Pivot.GetSignalByTag("S12")));    // YV1 sorti
        Assert.True(GetRet(store, Pivot.GetSignalByTag("S21")));    // YV2 toujours rentre
        Assert.False(GetRet(store, Pivot.GetSignalByTag("S22")));   // YV2 pas sorti
    }

    // =====================================================================
    // Forcage des commandes (sprint 6) : substitution de la valeur EFFECTIVE d'un bit `cmd`
    // en tete de Tick, sans jamais ecrire le datastore. Miroir cote `cmd` du masque capteur.
    // =====================================================================

    // Relit un bit de commande DANS LE DATASTORE (photo courante), pour prouver que le forcage
    // ne l'a pas modifie : le forcage vit dans la copie snapshot du Tick, pas dans le transport.
    private static bool GetCmd(ModbusDataStore store, Signal sig)
    {
        ushort[] cmd = store.SnapshotCommands();
        return sig.ReadBit(cmd[sig.WordRel]);
    }

    [Fact]
    public void Forcage_sans_PLC_le_verin_sort_alors_que_cmd_est_a_0()
    {
        // Aucune commande PLC (cmd_extend reste a 0 dans le datastore) : le forcage a 1 pilote seul.
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var s12 = Pivot.GetSignalByTag("S12");

        sim.Forces.SetForce("cylinder_1", "cmd_extend", ForceMode.ForceHigh);
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);

        Assert.True(GetRet(store, s12));                 // tige sortie : le forcage a commande la sortie
        Assert.False(GetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend")));  // datastore cmd intact (0)
    }

    [Fact]
    public void Forcage_contre_PLC_le_verin_sort_malgre_cmd_a_0()
    {
        // Le PLC ecrit explicitement cmd_extend=0 ; le forcage a 1 gagne. Le datastore reste a 0.
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var s12 = Pivot.GetSignalByTag("S12");
        var extend = Pivot.GetSignal("cylinder_1", "cmd_extend");

        SetCmd(store, extend, false);
        sim.Forces.SetForce("cylinder_1", "cmd_extend", ForceMode.ForceHigh);
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);

        Assert.True(GetRet(store, s12));       // tige sortie malgre la commande PLC a 0
        Assert.False(GetCmd(store, extend));   // le datastore `cmd` n'a PAS ete modifie
    }

    [Fact]
    public void Forcage_a_0_contre_PLC_le_verin_reste_rentre()
    {
        // Le PLC commande la sortie (cmd_extend=1), mais le forcage a 0 neutralise la commande.
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var s11 = Pivot.GetSignalByTag("S11");
        var s12 = Pivot.GetSignalByTag("S12");
        var extend = Pivot.GetSignal("cylinder_1", "cmd_extend");

        SetCmd(store, extend, true);
        sim.Forces.SetForce("cylinder_1", "cmd_extend", ForceMode.ForceLow);
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);

        Assert.True(GetRet(store, s11));      // tige rentree : le forcage a 0 a gagne
        Assert.False(GetRet(store, s12));
        Assert.True(GetCmd(store, extend));   // datastore `cmd` toujours a 1 (PLC), non modifie
    }

    [Fact]
    public void Forcage_KM1_AUX_suit_la_commande_effective_sans_PLC()
    {
        // Forcer cmd_run a 1 (PLC a 0) : la commande effective vue par le convoyeur passe a 1, donc
        // KM1_AUX colle apres feedback_delay — sans que le PLC ait commande la marche.
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var aux = Pivot.GetSignalByTag("KM1_AUX");

        sim.Forces.SetForce("conveyor", "cmd_run", ForceMode.ForceHigh);
        sim.Tick(store, 0.1);   // > feedback_delay (0.05 s)

        Assert.True(GetRet(store, aux));
        Assert.False(GetCmd(store, Pivot.GetSignal("KM1", "cmd_run")));  // datastore cmd intact (0)
    }

    [Fact]
    public void Composition_forcage_puis_defaut_le_defaut_physique_gagne()
    {
        // Ordre des couches : forcage `cmd` (tete) -> defaut physique. Forcer cmd_extend=1 rend la
        // commande effective vraie, mais CylinderStuckRetracted force la commande a faux dans
        // AdvanceCylinder -> la tige reste rentree. Determinisme de la composition prouve.
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var s12 = Pivot.GetSignalByTag("S12");

        sim.Forces.SetForce("cylinder_1", "cmd_extend", ForceMode.ForceHigh);
        sim.Faults.SetPhysical("cylinder_1", PhysicalFault.CylinderStuckRetracted);
        for (int i = 0; i < 6; i++) sim.Tick(store, 0.1);

        Assert.False(GetRet(store, s12));   // le defaut physique gagne sur la commande forcee
    }

    // =====================================================================
    // Defaut BlockerIneffective (sprint 6) : la tige sort normalement (S12=1) mais le poste est
    // exclu du blocage -> les palettes traversent. Contraste avec le blocage nominal.
    // =====================================================================

    [Fact]
    public void BlockerIneffective_la_palette_traverse_la_tige_pourtant_levee()
    {
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        var s12 = Pivot.GetSignalByTag("S12");

        SetCmd(store, Pivot.GetSignal("KM1", "cmd_run"), true);
        SetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend"), true);
        sim.Faults.SetPhysical("cylinder_1", PhysicalFault.BlockerIneffective);

        // La tige sort NORMALEMENT (le defaut ne touche pas AdvanceCylinder) : on tourne assez pour
        // sortir YV1 puis amener une palette dans la fenetre du poste 90°. B1 monte au passage.
        bool sawB1High = false;
        for (int i = 0; i < 120; i++)
        {
            sim.Tick(store, 0.1);
            if (GetRet(store, Pivot.GetSignal("B1", "ret_active"))) sawB1High = true;
        }

        Assert.True(GetRet(store, s12));    // S12=1 : la tige est bien sortie (cinematique nominale)
        Assert.True(sawB1High);             // une palette est bien passee dans la fenetre (B est monte)
        // ... mais AUCUNE palette n'est restee parquee a 90° : elles ont traverse la tige levee.
        Assert.DoesNotContain(sim.Pallets.AnglesDeg, a => Math.Abs(a - 90.0) < 1.0);
    }

    [Fact]
    public void Sans_BlockerIneffective_la_palette_reste_bloquee_non_regression()
    {
        // Meme scenario SANS le defaut : le blocage nominal tient (une palette se park a ~90°).
        var store = new ModbusDataStore(Pivot);
        var sim = new CarrouselSimulation(Pivot);
        SetCmd(store, Pivot.GetSignal("KM1", "cmd_run"), true);
        SetCmd(store, Pivot.GetSignal("cylinder_1", "cmd_extend"), true);

        for (int i = 0; i < 120; i++) sim.Tick(store, 0.1);

        Assert.True(GetRet(store, Pivot.GetSignal("B1", "ret_active")));
        Assert.Contains(sim.Pallets.AnglesDeg, a => Math.Abs(a - 90.0) < 1.0);
    }
}

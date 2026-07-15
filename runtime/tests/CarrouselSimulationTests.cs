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
}

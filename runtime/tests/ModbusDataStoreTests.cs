// =============================================================================
// ModbusDataStoreTests — contrat du datastore Modbus (brique 2)
// =============================================================================
//
// Le datastore est la source de verite d'Arch A : ces tests verrouillent les proprietes
// dont depend toute la chaine (brique 3 serveur, brique 4 simulation) :
//   1. tailles derivees du pivot (pas de constante en dur) ;
//   2. SnapshotCommands rend une COPIE (isolation de la simulation) ;
//   3. PublishReturns remplace la zone ret de facon atomique et defensive ;
//   4. round-trip fidele wire <-> store (pull puis push redonne les memes mots) ;
//   5. toute longueur invalide echoue CLAIREMENT (code defensif sur entree externe).
//
// Aucune dependance Godot ni serveur reseau : logique pure, testee hors Godot (dette D-006).

using CarrouselCore;
using Xunit;

namespace CarrouselCore.Tests;

public class ModbusDataStoreTests
{
    // Pivot reel partage (immuable apres Load) : cmd = 1 mot, ret = 2 mots.
    private static readonly PivotModel Mapping = PivotModel.Load(PivotPath());

    // Localisation du pivot en remontant depuis l'assembly de test (meme recette que
    // PivotModelTests : robuste quel que soit le cwd).
    private static string PivotPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "pivot", "machine_carrousel.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("pivot/machine_carrousel.json introuvable en remontant depuis " + AppContext.BaseDirectory);
    }

    private static ModbusDataStore NewStore() => new(Mapping);

    // =====================================================================
    // 1. Tailles derivees du pivot
    // =====================================================================

    [Fact]
    public void Tailles_tirees_du_pivot()
    {
        var store = NewStore();
        Assert.Equal(1, store.CommandWordCount);   // zone cmd, size_words = 1
        Assert.Equal(2, store.ReturnWordCount);    // zone ret, size_words = 2
    }

    // =====================================================================
    // 2. SnapshotCommands rend une copie isolee
    // =====================================================================

    [Fact]
    public void Snapshot_est_une_copie_defensive()
    {
        var store = NewStore();
        store.WriteCommandsFromWire(new ushort[] { 0x00FF });

        var snap = store.SnapshotCommands();
        Assert.Equal(new ushort[] { 0x00FF }, snap);

        // Muter le tableau rendu ne doit PAS toucher l'etat interne.
        snap[0] = 0xDEAD;
        Assert.Equal(new ushort[] { 0x00FF }, store.SnapshotCommands());
    }

    [Fact]
    public void Snapshot_reflete_la_derniere_ecriture()
    {
        var store = NewStore();
        Assert.Equal(new ushort[] { 0 }, store.SnapshotCommands());   // etat initial : zero

        store.WriteCommandsFromWire(new ushort[] { 0x0001 });
        Assert.Equal(new ushort[] { 0x0001 }, store.SnapshotCommands());
    }

    // =====================================================================
    // 3. PublishReturns : remplacement atomique + defensif
    // =====================================================================

    [Fact]
    public void Publish_remplace_la_zone_ret()
    {
        var store = NewStore();
        store.PublishReturns(new ushort[] { 0x1234, 0x5678 });

        var wire = new ushort[2];
        store.CopyReturnsToWire(wire);
        Assert.Equal(new ushort[] { 0x1234, 0x5678 }, wire);
    }

    [Fact]
    public void Publish_copie_le_contenu_pas_la_reference()
    {
        var store = NewStore();
        var ret = new ushort[] { 0x0001, 0x0002 };
        store.PublishReturns(ret);

        // Muter le tableau source apres publication ne doit pas alterer le store.
        ret[0] = 0xFFFF;

        var wire = new ushort[2];
        store.CopyReturnsToWire(wire);
        Assert.Equal(new ushort[] { 0x0001, 0x0002 }, wire);
    }

    [Fact]
    public void Publish_longueur_invalide_echoue()
    {
        var store = NewStore();
        var ex = Assert.Throws<ArgumentException>(() => store.PublishReturns(new ushort[] { 1 }));   // 1 mot au lieu de 2
        Assert.Contains("attendus", ex.Message);
    }

    [Fact]
    public void Publish_null_echoue()
    {
        var store = NewStore();
        Assert.Throws<ArgumentNullException>(() => store.PublishReturns(null!));
    }

    // =====================================================================
    // 4. Round-trip fidele wire <-> store
    // =====================================================================

    [Fact]
    public void RoundTrip_commandes_pull_puis_snapshot()
    {
        var store = NewStore();
        store.WriteCommandsFromWire(new ushort[] { 0b0000_0111 });   // cmd_run + 2 verins
        Assert.Equal(new ushort[] { 0b0000_0111 }, store.SnapshotCommands());
    }

    [Fact]
    public void RoundTrip_retours_publish_puis_push()
    {
        var store = NewStore();
        var published = new ushort[] { 0x00AB, 0b0100_0001 };   // heartbeat + quelques bits ret
        store.PublishReturns(published);

        var wire = new ushort[2];
        store.CopyReturnsToWire(wire);
        Assert.Equal(published, wire);
    }

    // =====================================================================
    // 5. Longueurs invalides cote pont serveur
    // =====================================================================

    [Fact]
    public void WriteCommandsFromWire_longueur_invalide_echoue()
    {
        var store = NewStore();
        var ex = Assert.Throws<ArgumentException>(
            () => store.WriteCommandsFromWire(new ushort[] { 1, 2 }));   // 2 mots au lieu de 1
        Assert.Contains("cmd", ex.Message);
    }

    [Fact]
    public void CopyReturnsToWire_longueur_invalide_echoue()
    {
        var store = NewStore();
        var ex = Assert.Throws<ArgumentException>(
            () => store.CopyReturnsToWire(new ushort[3]));   // 3 mots au lieu de 2
        Assert.Contains("ret", ex.Message);
    }
}

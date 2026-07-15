// =============================================================================
// PivotModelTests — miroir xUnit de testbench/test_pivot_mapping.py
// =============================================================================
//
// Ces tests verrouillent le MEME contrat que la suite pytest cote Python, sur le MEME
// pivot reel (pivot/machine_carrousel.json, schema 0.2.0). Les deux loaders (Python et
// C#) doivent resoudre exactement les memes adresses absolues : asserter ici les memes
// %MW/bit que dans test_pivot_mapping.py est notre garantie pratique de parite entre les
// deux implementations (cf. en-tete de PivotModel.cs). Un diff canonique formel Python <->
// C# reste un point de backlog (l'emetteur canonique Python n'existe pas encore).
//
// Deux familles de tests :
//   1. Contrat sur le pivot reel (zones, heartbeat, adresses des signaux).
//   2. Robustesse : un pivot malforme doit lever PivotException CLAIREMENT (code defensif).

using CarrouselCore;
using Xunit;

namespace CarrouselCore.Tests;

public class PivotModelTests
{
    // Le pivot reel est charge une fois et partage : il est immuable apres Load.
    private static readonly PivotModel Mapping = PivotModel.Load(PivotPath());

    // --- Localisation du pivot : on remonte depuis le dossier de l'assembly de test
    //     jusqu'a trouver pivot/machine_carrousel.json. Robuste quel que soit le cwd
    //     (equivalent du DEFAULT_PIVOT_PATH = parents[1] cote Python). ---
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

    // =====================================================================
    // 1. Contrat sur le pivot reel
    // =====================================================================

    [Fact]
    public void Zones_et_bases()
    {
        Assert.Equal(new[] { "cmd", "ret" }, Mapping.Zones.Keys.OrderBy(k => k).ToArray());
        Assert.Equal((100, 1), (Mapping.GetZone("cmd").Base, Mapping.GetZone("cmd").SizeWords));
        Assert.Equal((200, 2), (Mapping.GetZone("ret").Base, Mapping.GetZone("ret").SizeWords));
    }

    [Fact]
    public void Port_et_unit_id_du_pivot()
    {
        // Parametres reseau tires du pivot (contrat), jamais devines.
        Assert.Equal(502, Mapping.Port);
        Assert.Equal((byte)1, Mapping.UnitId);
    }

    [Fact]
    public void Heartbeat_mot0_de_ret()
    {
        var hb = Mapping.Heartbeat;
        Assert.Equal("ret", hb.Zone);
        Assert.Equal(0, hb.WordRel);
        Assert.Equal(200, hb.AbsWord);
        Assert.False(hb.IsTor);
    }

    [Fact]
    public void Commande_convoyeur()
    {
        var run = Mapping.GetSignal("KM1", "cmd_run");   // lookup par tag composant
        Assert.Equal(("cmd", 100, 0), (run.Zone, run.AbsWord, run.Bit));
    }

    [Fact]
    public void Retour_marche_moteur_km1_aux()
    {
        var aux = Mapping.GetSignalByTag("KM1_AUX");
        Assert.Equal(("ret", 201, 6), (aux.Zone, aux.AbsWord, aux.Bit));
        // KM1_AUX est bien le ret_running du composant conveyor (ex-B3 supprime).
        Assert.Equal(aux, Mapping.GetSignal("conveyor", "ret_running"));
    }

    [Theory]
    [InlineData("S11", 201, 0)]
    [InlineData("S12", 201, 1)]
    [InlineData("S21", 201, 2)]
    [InlineData("S22", 201, 3)]
    public void Fins_de_course_verins(string tag, int absWord, int bit)
    {
        var s = Mapping.GetSignalByTag(tag);
        Assert.Equal((absWord, bit), (s.AbsWord, s.Bit));
        Assert.Equal("ret", s.Zone);
    }

    [Fact]
    public void Presences_palette()
    {
        var b1 = Mapping.GetSignal("B1", "ret_active");
        var b2 = Mapping.GetSignal("B2", "ret_active");
        Assert.Equal((201, 4), (b1.AbsWord, b1.Bit));
        Assert.Equal((201, 5), (b2.AbsWord, b2.Bit));
    }

    [Fact]
    public void Lecture_bit_depuis_mot()
    {
        var s11 = Mapping.GetSignalByTag("S11");           // bit 0
        var aux = Mapping.GetSignalByTag("KM1_AUX");       // bit 6
        const ushort word = 0b0100_0001;                   // bits 0 et 6 a 1
        Assert.True(s11.ReadBit(word));
        Assert.True(aux.ReadBit(word));
        Assert.False(Mapping.GetSignalByTag("S12").ReadBit(word));
    }

    [Fact]
    public void Bases_surchargeables()
    {
        // Recaler une zone (adressage memoire existant) ne casse pas le mapping.
        var m = PivotModel.Load(PivotPath(), new Dictionary<string, int> { ["cmd"] = 300, ["ret"] = 400 });
        Assert.Equal(300, m.GetZone("cmd").Base);
        Assert.Equal(300, m.GetSignal("KM1", "cmd_run").AbsWord);
        Assert.Equal(400, m.Heartbeat.AbsWord);
        Assert.Equal(401, m.GetSignalByTag("KM1_AUX").AbsWord);
    }

    [Fact]
    public void Aucun_hors_borne()
    {
        foreach (var sig in Mapping.AllSignals)
        {
            var z = Mapping.GetZone(sig.Zone);
            Assert.InRange(sig.WordRel, 0, z.SizeWords - 1);
            if (sig.IsTor)
                Assert.InRange(sig.Bit!.Value, 0, 15);
        }
    }

    // =====================================================================
    // 2. Robustesse : un pivot malforme doit echouer CLAIREMENT
    // =====================================================================

    // Pivot minimal valide, servant de base a muter pour chaque cas d'erreur.
    private const string MinimalPivot = """
    {
      "modbus": {
        "port": 502,
        "unit_id": 1,
        "zones": {
          "cmd": { "base_mw": 100, "size_words": 1 },
          "ret": { "base_mw": 200, "size_words": 2 }
        },
        "heartbeat": { "zone": "ret", "word": 0 }
      },
      "components": [
        { "id": "c", "tag": "KM1", "type": "conveyor_circular",
          "signals": { "cmd_run": { "zone": "cmd", "word": 0, "bit": 0 } } }
      ]
    }
    """;

    // Ecrit un pivot temporaire et renvoie son chemin (nettoye par l'OS ; on ne pollue
    // pas le depot). Chaque test compose son propre JSON malforme.
    private static string WriteTemp(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"pivot_test_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Json_invalide_echoue()
    {
        Assert.Throws<PivotException>(() => PivotModel.Load(WriteTemp("{ pas du json")));
    }

    [Fact]
    public void Bit_hors_borne_echoue()
    {
        // bit 16 hors [0..15]
        string json = MinimalPivot.Replace("\"bit\": 0", "\"bit\": 16");
        var ex = Assert.Throws<PivotException>(() => PivotModel.Load(WriteTemp(json)));
        Assert.Contains("bit", ex.Message);
    }

    [Fact]
    public void Word_hors_zone_echoue()
    {
        // word 5 dans la zone cmd (size_words = 1)
        string json = MinimalPivot.Replace("\"word\": 0, \"bit\": 0", "\"word\": 5, \"bit\": 0");
        var ex = Assert.Throws<PivotException>(() => PivotModel.Load(WriteTemp(json)));
        Assert.Contains("hors zone", ex.Message);
    }

    [Fact]
    public void Conflit_adresse_echoue()
    {
        // Deux signaux sur cmd %MW100 bit 0.
        string json = MinimalPivot.Replace(
            "\"cmd_run\": { \"zone\": \"cmd\", \"word\": 0, \"bit\": 0 }",
            "\"cmd_run\": { \"zone\": \"cmd\", \"word\": 0, \"bit\": 0 }, "
            + "\"cmd_dup\": { \"zone\": \"cmd\", \"word\": 0, \"bit\": 0 }");
        var ex = Assert.Throws<PivotException>(() => PivotModel.Load(WriteTemp(json)));
        Assert.Contains("onflit", ex.Message);   // "Conflit" / "conflit"
    }

    [Fact]
    public void Port_absent_echoue()
    {
        // Retire "port": 502 du pivot minimal : le parse strict doit lever clairement.
        string json = MinimalPivot.Replace("\"port\": 502,", "");
        var ex = Assert.Throws<PivotException>(() => PivotModel.Load(WriteTemp(json)));
        Assert.Contains("port", ex.Message);
    }

    [Fact]
    public void Unit_id_absent_echoue()
    {
        string json = MinimalPivot.Replace("\"unit_id\": 1,", "");
        var ex = Assert.Throws<PivotException>(() => PivotModel.Load(WriteTemp(json)));
        Assert.Contains("unit_id", ex.Message);
    }

    [Fact]
    public void Heartbeat_absent_echoue()
    {
        // Meme pivot minimal mais sans la section heartbeat (litteral dedie : plus robuste
        // qu'un replace sur whitespace).
        const string noHeartbeat = """
        {
          "modbus": {
            "port": 502,
            "unit_id": 1,
            "zones": {
              "cmd": { "base_mw": 100, "size_words": 1 },
              "ret": { "base_mw": 200, "size_words": 2 }
            }
          },
          "components": [
            { "id": "c", "tag": "KM1", "type": "conveyor_circular",
              "signals": { "cmd_run": { "zone": "cmd", "word": 0, "bit": 0 } } }
          ]
        }
        """;
        var ex = Assert.Throws<PivotException>(() => PivotModel.Load(WriteTemp(noHeartbeat)));
        Assert.Contains("heartbeat", ex.Message);
    }
}

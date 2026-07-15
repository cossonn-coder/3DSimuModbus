// =============================================================================
// PivotModel — chargement et resolution du modele pivot Modbus (miroir C# du
// testbench/pivot_loader.py)
// =============================================================================
//
// Role : lire pivot/machine_carrousel.json (contrat central) et resoudre, pour chaque
// signal, son adresse Modbus ABSOLUE a partir de la base de sa zone (base_mw) et de son
// offset relatif (word). Le pivot exprime les adresses de facon RELATIVE {zone, word, bit}
// pour qu'on puisse recaler une zone entiere (changer base_mw) sans reconstruire le
// mapping ; le runtime et les tests, eux, ont besoin de l'adresse ABSOLUE %MW.
//
// Ce fichier est le PENDANT C# de pivot_loader.py. Les deux DOIVENT produire la meme
// table resolue : c'est verifie par un diff formel (ToCanonical() ici vs le meme format
// emis cote Python). Toute divergence est un bug de l'un des deux loaders.
//
// Convention (figee en Phase 0) :
//   - %MWn cote Control Expert = holding register d'adresse protocole n. Aucun decalage.
//   - Zone cmd (PLC -> sim, FC16) et zone ret (sim -> PLC, FC3), chacune avec sa base
//     base_mw et sa taille size_words.
//   - Signaux TOR : {zone, word, bit} (bit 0..15), avec un tag optionnel (S11, KM1_AUX...).
//   - Heartbeat : compteur mot entier (modbus.heartbeat, zone ret, word).
//
// Code DEFENSIF (entree externe) : tout JSON malforme ou incoherent leve PivotException
// avec un message clair (adresse hors zone, doublon, champ manquant).

using System.Text.Json;

namespace CarrouselCore;

/// <summary>Pivot malforme ou incoherent. On echoue clairement (jamais de mapping partiel).</summary>
public sealed class PivotException : Exception
{
    public PivotException(string message) : base(message) { }
    public PivotException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Disposition resolue d'une zone Modbus (base absolue + taille).</summary>
public readonly record struct ZoneLayout(string Name, int Base, int SizeWords, string Description);

/// <summary>Un signal resolu, pret a etre interroge sur le bus (adresse absolue calculee).</summary>
public readonly record struct Signal(
    string ComponentId,   // id du composant proprietaire (ou "_heartbeat")
    string Name,          // cle du signal dans le composant (cmd_run, ret_extended...)
    string? Tag,          // repere schema (S11, KM1_AUX...) si defini, sinon null
    string Zone,          // "cmd" ou "ret"
    bool IsTor,           // true = bit dans un mot ; false = mot entier (compteur)
    int WordRel,          // offset mot relatif a la base de la zone
    int? Bit,             // position 0..15 si TOR, sinon null
    int AbsWord)          // adresse %MW absolue resolue (base + WordRel)
{
    /// <summary>Extrait l'etat de ce bit TOR dans la valeur d'un mot 16 bits.</summary>
    public bool ReadBit(ushort wordValue)
    {
        if (!IsTor || Bit is null)
            throw new PivotException($"{Name} n'est pas un signal TOR");
        return ((wordValue >> Bit.Value) & 0x1) != 0;
    }
}

/// <summary>Un composant du pivot (conveyor, cylinder, sensor...) avec ses signaux resolus.</summary>
public sealed class Component
{
    public string Id { get; }
    public string Tag { get; }
    public string Type { get; }
    public IReadOnlyDictionary<string, Signal> Signals { get; }

    public Component(string id, string tag, string type, IReadOnlyDictionary<string, Signal> signals)
    {
        Id = id;
        Tag = tag;
        Type = type;
        Signals = signals;
    }
}

/// <summary>Vue resolue du pivot : zones, composants, index par tag, heartbeat.</summary>
public sealed class PivotModel
{
    public IReadOnlyDictionary<string, ZoneLayout> Zones { get; }
    public IReadOnlyDictionary<string, Component> Components { get; }   // cle = id composant
    public IReadOnlyDictionary<string, Component> ByTag { get; }        // cle = tag composant (KM1, YV1...)
    public IReadOnlyDictionary<string, Signal> SignalsByTag { get; }    // cle = tag signal (S11, KM1_AUX...)
    public IReadOnlyList<Signal> AllSignals { get; }
    public Signal Heartbeat { get; }

    // Parametres reseau du serveur Modbus, tires du pivot (jamais en dur cote code) :
    //   - Port : port d'ecoute TCP (502 dans le pivot V1) ;
    //   - UnitId : identifiant d'unite Modbus que le M580 scrutera (1 dans le pivot V1).
    // Consommes par ModbusServer (brique 3) : server.AddUnit(UnitId) + ecoute sur Port.
    public int Port { get; }
    public byte UnitId { get; }

    private PivotModel(
        IReadOnlyDictionary<string, ZoneLayout> zones,
        IReadOnlyDictionary<string, Component> components,
        IReadOnlyDictionary<string, Component> byTag,
        IReadOnlyDictionary<string, Signal> signalsByTag,
        IReadOnlyList<Signal> allSignals,
        Signal heartbeat,
        int port,
        byte unitId)
    {
        Zones = zones;
        Components = components;
        ByTag = byTag;
        SignalsByTag = signalsByTag;
        AllSignals = allSignals;
        Heartbeat = heartbeat;
        Port = port;
        UnitId = unitId;
    }

    // --- Accesseurs (resolvent depuis le pivot ; jamais d'adresse absolue en dur ailleurs) ---

    public ZoneLayout GetZone(string name)
        => Zones.TryGetValue(name, out var z) ? z : throw new PivotException($"Zone inconnue : {name}");

    public Component GetComponent(string key)
    {
        if (Components.TryGetValue(key, out var c)) return c;
        if (ByTag.TryGetValue(key, out var byTag)) return byTag;
        throw new PivotException($"Composant inconnu : {key}");
    }

    public Signal GetSignal(string componentKey, string signalName)
    {
        var comp = GetComponent(componentKey);
        return comp.Signals.TryGetValue(signalName, out var s)
            ? s
            : throw new PivotException($"{comp.Id} n'a pas de signal {signalName}");
    }

    public Signal GetSignalByTag(string tag)
        => SignalsByTag.TryGetValue(tag, out var s) ? s : throw new PivotException($"Signal de tag inconnu : {tag}");

    // --- Chargement ---

    /// <summary>
    /// Charge le pivot et resout toutes les adresses absolues.
    /// <paramref name="baseOverrides"/> permet de recaler une zone (ex. {"cmd": 300}) sans
    /// toucher au pivot — utile si l'automaticien doit caser les zones dans un adressage existant.
    /// </summary>
    public static PivotModel Load(string path, IReadOnlyDictionary<string, int>? baseOverrides = null)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new PivotException($"Pivot introuvable : {path}", e);
        }

        using JsonDocument doc = ParseOrThrow(text, path);
        JsonElement root = doc.RootElement;

        // modbus.zones est obligatoire : sans lui, aucune resolution possible.
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("modbus", out var modbus)
            || modbus.ValueKind != JsonValueKind.Object
            || !modbus.TryGetProperty("zones", out var zonesEl)
            || zonesEl.ValueKind != JsonValueKind.Object)
        {
            throw new PivotException("Section 'modbus.zones' absente du pivot");
        }

        var zones = ResolveZones(zonesEl, baseOverrides);

        // Port et unit_id : parametres reseau REQUIS. Le pivot est le contrat ; ces valeurs
        // ne se devinent jamais en silence (on pilote un automate reel — un mauvais port ou
        // un mauvais unit_id = connexion fermee cote M580, panne silencieuse). Absence ou
        // valeur hors plage => echec clair, au meme titre que les autres validations pivot.
        if (!TryGetInt(modbus, "port", out int port))
            throw new PivotException("Section 'modbus.port' absente ou invalide");
        if (port < 1 || port > 65535)
            throw new PivotException($"modbus.port hors plage [1..65535] : {port}");
        if (!TryGetInt(modbus, "unit_id", out int unitId))
            throw new PivotException("Section 'modbus.unit_id' absente ou invalide");
        if (unitId < 1 || unitId > 247)   // 0 = broadcast, 248..255 reserves : hors usage serveur
            throw new PivotException($"modbus.unit_id hors plage [1..247] : {unitId}");

        // Anti-collision : un bit TOR est unique ; un mot compteur reserve le mot entier.
        var occupiedBits = new HashSet<(string Zone, int Word, int Bit)>();
        var occupiedWords = new HashSet<(string Zone, int Word)>();

        // Heartbeat = mot entier dans la zone ret. Resolu et reserve en premier.
        var heartbeat = ResolveHeartbeat(modbus, zones);
        CheckBounds(heartbeat, zones);
        occupiedWords.Add((heartbeat.Zone, heartbeat.AbsWord));

        var components = new Dictionary<string, Component>();
        var byTag = new Dictionary<string, Component>();
        var signalsByTag = new Dictionary<string, Signal>();
        var allSignals = new List<Signal> { heartbeat };

        if (root.TryGetProperty("components", out var compsEl) && compsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var compRaw in compsEl.EnumerateArray())
            {
                string compId = GetString(compRaw, "id", "");
                string compTag = GetString(compRaw, "tag", compId);
                var sigs = new Dictionary<string, Signal>();

                if (compRaw.TryGetProperty("signals", out var sigsEl) && sigsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in sigsEl.EnumerateObject())
                    {
                        var sig = ResolveSignal(compId, prop.Name, prop.Value, zones);
                        CheckBounds(sig, zones);
                        CheckCollision(sig, occupiedBits, occupiedWords);
                        sigs[prop.Name] = sig;
                        allSignals.Add(sig);
                        if (sig.Tag is not null)
                        {
                            if (!signalsByTag.TryAdd(sig.Tag, sig))
                                throw new PivotException($"Tag de signal duplique : {sig.Tag}");
                        }
                    }
                }

                var comp = new Component(compId, compTag, GetString(compRaw, "type", ""), sigs);
                components[compId] = comp;
                byTag[compTag] = comp;
            }
        }

        // NB : les params machine (speed_deg_per_s, travel_time_ms, seuils...) ne sont PAS
        // parses ici — ils ne servent qu'a la boucle de simulation (brique ulterieure). On
        // les ajoutera quand cette brique en aura besoin (simplicite d'abord).

        if (allSignals.Count <= 1)
            throw new PivotException("Aucun signal de composant resolu depuis le pivot");

        return new PivotModel(zones, components, byTag, signalsByTag, allSignals, heartbeat, port, (byte)unitId);
    }

    private static JsonDocument ParseOrThrow(string text, string path)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException e)
        {
            throw new PivotException($"Pivot JSON invalide ({path}) : {e.Message}", e);
        }
    }

    private static Dictionary<string, ZoneLayout> ResolveZones(
        JsonElement zonesEl, IReadOnlyDictionary<string, int>? overrides)
    {
        var outp = new Dictionary<string, ZoneLayout>();
        foreach (var prop in zonesEl.EnumerateObject())
        {
            var z = prop.Value;
            int baseMw;
            if (overrides is not null && overrides.TryGetValue(prop.Name, out var ov))
                baseMw = ov;
            else if (TryGetInt(z, "base_mw", out var b))
                baseMw = b;
            else
                throw new PivotException($"Zone '{prop.Name}' sans 'base_mw' ni override");

            if (!TryGetInt(z, "size_words", out var size))
                throw new PivotException($"Zone '{prop.Name}' sans 'size_words'");

            outp[prop.Name] = new ZoneLayout(prop.Name, baseMw, size, GetString(z, "description", ""));
        }
        return outp;
    }

    private static Signal ResolveHeartbeat(JsonElement modbus, Dictionary<string, ZoneLayout> zones)
    {
        if (!modbus.TryGetProperty("heartbeat", out var hb) || hb.ValueKind != JsonValueKind.Object)
            throw new PivotException("Section 'modbus.heartbeat' absente ou invalide");

        string zone = GetString(hb, "zone", "");
        if (!zones.ContainsKey(zone))
            throw new PivotException($"heartbeat : zone '{zone}' inconnue");
        if (!TryGetInt(hb, "word", out var word))
            throw new PivotException("heartbeat : champ 'word' manquant");

        return new Signal("_heartbeat", "heartbeat", "HEARTBEAT", zone,
                          IsTor: false, WordRel: word, Bit: null, AbsWord: zones[zone].Base + word);
    }

    private static Signal ResolveSignal(string compId, string name, JsonElement spec, Dictionary<string, ZoneLayout> zones)
    {
        string zone = GetString(spec, "zone", "");
        if (!zones.ContainsKey(zone))
            throw new PivotException($"{compId}.{name} : zone '{zone}' inconnue");
        if (!TryGetInt(spec, "word", out var word))
            throw new PivotException($"{compId}.{name} : champ 'word' manquant");

        int? bit = TryGetInt(spec, "bit", out var b) ? b : null;   // pas de bit => mot entier
        string? tag = spec.TryGetProperty("tag", out var tEl) && tEl.ValueKind == JsonValueKind.String
            ? tEl.GetString()
            : null;

        return new Signal(compId, name, tag, zone,
                          IsTor: bit is not null, WordRel: word, Bit: bit, AbsWord: zones[zone].Base + word);
    }

    private static void CheckBounds(Signal sig, Dictionary<string, ZoneLayout> zones)
    {
        var zone = zones[sig.Zone];
        if (sig.WordRel < 0 || sig.WordRel >= zone.SizeWords)
            throw new PivotException(
                $"{sig.ComponentId}.{sig.Name} : word {sig.WordRel} hors zone {sig.Zone} (size_words {zone.SizeWords})");
        if (sig.IsTor && (sig.Bit!.Value < 0 || sig.Bit.Value > 15))
            throw new PivotException($"{sig.ComponentId}.{sig.Name} : bit {sig.Bit} hors [0..15]");
    }

    private static void CheckCollision(
        Signal sig, HashSet<(string Zone, int Word, int Bit)> bits, HashSet<(string Zone, int Word)> words)
    {
        if (sig.IsTor)
        {
            var key = (sig.Zone, sig.AbsWord, sig.Bit!.Value);
            if (!bits.Add(key))
                throw new PivotException($"Conflit d'adresse TOR : {sig.Zone} %MW{sig.AbsWord} bit {sig.Bit}");
            if (words.Contains((sig.Zone, sig.AbsWord)))
                throw new PivotException(
                    $"{sig.ComponentId}.{sig.Name} : TOR sur un mot deja reserve (compteur) {sig.Zone} %MW{sig.AbsWord}");
        }
        else
        {
            var wkey = (sig.Zone, sig.AbsWord);
            bool clashBits = bits.Any(k => k.Zone == sig.Zone && k.Word == sig.AbsWord);
            if (!words.Add(wkey) || clashBits)
                throw new PivotException($"{sig.ComponentId}.{sig.Name} : mot {sig.Zone} %MW{sig.AbsWord} deja occupe");
        }
    }

    // --- Helpers JSON defensifs ---

    private static bool TryGetInt(JsonElement obj, string name, out int value)
    {
        value = 0;
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out value))
        {
            return true;
        }
        return false;
    }

    private static string GetString(JsonElement obj, string name, string fallback)
        => obj.ValueKind == JsonValueKind.Object
           && obj.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : fallback;

    // --- Diagnostic : table resolue au format canonique (pour diff vs pivot_loader.py) ---

    /// <summary>
    /// Emet la table resolue en lignes canoniques triees, pour comparaison formelle avec la
    /// sortie equivalente du loader Python. Format stable, une ligne par zone puis par signal.
    /// </summary>
    public string ToCanonical()
    {
        var lines = new List<string>();
        foreach (var z in Zones.Values)
            lines.Add($"zone {z.Name} base={z.Base} size={z.SizeWords}");
        foreach (var s in AllSignals)
        {
            string pos = s.IsTor ? $"bit{s.Bit}" : "word";
            string tag = s.Tag ?? "-";
            lines.Add($"{s.ComponentId}.{s.Name} zone={s.Zone} abs=%MW{s.AbsWord} pos={pos} tag={tag}");
        }
        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines);
    }
}

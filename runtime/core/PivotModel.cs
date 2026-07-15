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

/// <summary>
/// Cinematique des palettes, resolue depuis le bloc <c>kinematics</c> du pivot (brique 4b).
/// Purement descriptive : le PivotModel transporte les nombres, c'est <c>PalletSet</c> qui les
/// anime. <c>Ccw</c> vient de <c>kinematics.path.direction</c> (sens de rotation du convoyeur).
/// </summary>
public readonly record struct KinematicsInfo(
    int PalletCount,                    // nombre de palettes (kinematics.pallets.count)
    double[] InitialPositionsDeg,       // positions angulaires de depart, normalisees [0..360)
    double MinGapDeg,                   // ecart angulaire minimal en accumulation
    bool Ccw);                          // true = rotation trigonometrique (angle croissant)

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

    /// <summary>
    /// Ecrit l'etat de ce bit TOR dans un mot 16 bits POSSEDE par l'appelant (passe par ref) :
    /// met le bit a 1 si <paramref name="value"/>, a 0 sinon, sans toucher aux autres bits du mot.
    /// Symetrique de <see cref="ReadBit"/> : la simulation reconstruit chaque mot de la zone `ret`
    /// en positionnant les bits de ses retours (S11, KM1_AUX...) un par un via cette methode.
    /// </summary>
    public void WriteBit(ref ushort word, bool value)
    {
        if (!IsTor || Bit is null)
            throw new PivotException($"{Name} n'est pas un signal TOR");
        ushort mask = (ushort)(1 << Bit.Value);
        if (value)
            word |= mask;                       // set : OU avec le masque
        else
            word = (ushort)(word & ~mask);      // clear : ET avec le complement (cast : ~ushort promeut en int)
    }
}

/// <summary>Un composant du pivot (conveyor, cylinder, sensor...) avec ses signaux resolus.</summary>
public sealed class Component
{
    public string Id { get; }
    public string Tag { get; }
    public string Type { get; }
    public IReadOnlyDictionary<string, Signal> Signals { get; }

    // Parametres machine numeriques du composant (bloc "params" du pivot), resolus en scalaires
    // `double` : vitesse convoyeur, temps de course verin, seuils de capteurs, fenetre de presence...
    // Sac GENERIQUE (cle -> valeur) : le PivotModel ne connait pas la SEMANTIQUE machine, il ne fait
    // que transporter des nombres nommes ; c'est la simulation (brique 4) qui sait quel param lire
    // pour quel modele. Les champs non numeriques (monostable: bool, size_m: tableau...) sont ignores
    // ici — ils relevent d'autres briques (rendu 3D) ou d'un comportement deja acte.
    public IReadOnlyDictionary<string, double> Params { get; }

    public Component(string id, string tag, string type,
                     IReadOnlyDictionary<string, Signal> signals,
                     IReadOnlyDictionary<string, double> parameters)
    {
        Id = id;
        Tag = tag;
        Type = type;
        Signals = signals;
        Params = parameters;
    }

    /// <summary>
    /// Lit un parametre machine numerique du composant. DEFENSIF : un param attendu mais absent du
    /// pivot est une incoherence de contrat (pas un cas nominal) — on echoue clairement plutot que
    /// de retomber sur une valeur par defaut silencieuse qui fausserait la cinematique.
    /// </summary>
    public double GetParam(string name)
        => Params.TryGetValue(name, out var v)
            ? v
            : throw new PivotException($"{Id} n'a pas de parametre '{name}'");
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

    // Cinematique palettes (brique 4b). Optionnelle au chargement : les pivots minimaux de test
    // (mapping Modbus seul) n'ont pas de bloc `kinematics`. On ne le rend obligatoire qu'au POINT
    // D'USAGE — un consommateur qui la demande sur un pivot sans palettes obtient une erreur claire,
    // plutot qu'un chargement qui echouerait pour des tests qui ne s'interessent qu'aux adresses.
    private readonly KinematicsInfo? _kinematics;

    /// <summary>
    /// Cinematique des palettes (positions initiales, min_gap, sens). Leve <see cref="PivotException"/>
    /// si le pivot n'a pas de bloc <c>kinematics.pallets</c> (contrat incomplet pour la simulation 4b).
    /// </summary>
    public KinematicsInfo Kinematics =>
        _kinematics ?? throw new PivotException("Section 'kinematics.pallets' absente du pivot");

    // Parametres reseau du serveur Modbus, tires du pivot (jamais en dur cote code) :
    //   - Port : port d'ecoute TCP (502 dans le pivot V1) ;
    //   - UnitId : identifiant d'unite Modbus que le M580 scrutera (1 dans le pivot V1).
    // Consommes par ModbusServer (brique 3) : server.AddUnit(UnitId) + ecoute sur Port.
    public int Port { get; }
    public byte UnitId { get; }

    // Cadence du tick physique, en millisecondes, tiree de modbus.heartbeat.period_ms (100 ms
    // au pivot V1). C'est le rythme auquel l'hote (SimHost / _PhysicsProcess Godot) enchaine
    // Pull -> Tick -> Push, donc aussi la cadence d'increment du heartbeat. Optionnel dans le
    // pivot : defaut 100 ms si absent (evite d'imposer le champ aux pivots minimaux de test).
    public int HeartbeatPeriodMs { get; }

    private PivotModel(
        IReadOnlyDictionary<string, ZoneLayout> zones,
        IReadOnlyDictionary<string, Component> components,
        IReadOnlyDictionary<string, Component> byTag,
        IReadOnlyDictionary<string, Signal> signalsByTag,
        IReadOnlyList<Signal> allSignals,
        Signal heartbeat,
        int heartbeatPeriodMs,
        int port,
        byte unitId,
        KinematicsInfo? kinematics)
    {
        Zones = zones;
        Components = components;
        ByTag = byTag;
        SignalsByTag = signalsByTag;
        AllSignals = allSignals;
        Heartbeat = heartbeat;
        HeartbeatPeriodMs = heartbeatPeriodMs;
        Port = port;
        UnitId = unitId;
        _kinematics = kinematics;
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

        // Cadence du tick (heartbeat.period_ms) : optionnelle, defaut 100 ms. Defensif si presente :
        // une periode nulle/negative n'a pas de sens (rythme d'une boucle temps reel).
        int heartbeatPeriodMs = 100;
        if (modbus.TryGetProperty("heartbeat", out var hbEl)
            && TryGetInt(hbEl, "period_ms", out int periodMs))
        {
            if (periodMs <= 0)
                throw new PivotException($"modbus.heartbeat.period_ms doit etre > 0 : {periodMs}");
            heartbeatPeriodMs = periodMs;
        }

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

                var prms = ResolveParams(compRaw);
                var comp = new Component(compId, compTag, GetString(compRaw, "type", ""), sigs, prms);
                components[compId] = comp;
                byTag[compTag] = comp;
            }
        }

        // Les params machine (speed_deg_per_s, travel_time_ms, seuils...) sont desormais parses de
        // facon ADDITIVE (brique 4a, decision D-d) : un seul point de verite pour le pivot, pas de
        // second parseur. On ne lit que les scalaires numeriques ; la cinematique (brique 4) sait
        // quel param s'applique a quel modele. Les resolutions d'adresses ci-dessus sont inchangees.

        if (allSignals.Count <= 1)
            throw new PivotException("Aucun signal de composant resolu depuis le pivot");

        // Cinematique palettes (brique 4b) : parse ADDITIF, optionnel au chargement (cf. _kinematics).
        // Present-mais-malforme => PivotException ; absent => null (rendu obligatoire a l'usage seul).
        var kinematics = ResolveKinematics(root);

        return new PivotModel(zones, components, byTag, signalsByTag, allSignals, heartbeat, heartbeatPeriodMs, port, (byte)unitId, kinematics);
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

    /// <summary>
    /// Extrait les params machine numeriques d'un composant (bloc "params"). Ne retient que les
    /// scalaires nombres (double) ; ignore silencieusement bool/tableau/objet (ex. monostable,
    /// size_m) qui ne concernent pas la cinematique. Bloc "params" absent = dictionnaire vide
    /// (un capteur sans param est legitime) — c'est GetParam qui echoue si un param requis manque.
    /// </summary>
    private static Dictionary<string, double> ResolveParams(JsonElement compRaw)
    {
        var outp = new Dictionary<string, double>();
        if (compRaw.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in p.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out var d))
                    outp[prop.Name] = d;
            }
        }
        return outp;
    }

    /// <summary>
    /// Resout le bloc cinematique (<c>kinematics.pallets</c> + <c>kinematics.path.direction</c>).
    /// Retourne <c>null</c> si la section <c>kinematics</c> est absente (pivot minimal de mapping
    /// Modbus) ; leve <see cref="PivotException"/> si elle est PRESENTE mais incoherente. DEFENSIF :
    /// on pilote une simulation reelle, une cinematique bancale doit echouer tot et clairement.
    /// </summary>
    private static KinematicsInfo? ResolveKinematics(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("kinematics", out var kin)
            || kin.ValueKind != JsonValueKind.Object)
            return null;   // pivot sans palettes : legitime (mapping Modbus seul, fixtures de test)

        // Sens de rotation : kinematics.path.direction, "ccw" (trigonometrique) ou "cw".
        if (!kin.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.Object)
            throw new PivotException("kinematics.path absent");
        string dir = GetString(path, "direction", "");
        bool ccw = dir switch
        {
            "ccw" => true,
            "cw" => false,
            _ => throw new PivotException($"kinematics.path.direction doit etre 'ccw' ou 'cw' : '{dir}'"),
        };

        // Bloc pallets : count, initial_positions_deg, min_gap_deg.
        if (!kin.TryGetProperty("pallets", out var pal) || pal.ValueKind != JsonValueKind.Object)
            throw new PivotException("kinematics.pallets absent");
        if (!TryGetInt(pal, "count", out int count) || count <= 0)
            throw new PivotException("kinematics.pallets.count absent ou <= 0");
        if (!TryGetDouble(pal, "min_gap_deg", out double minGap) || minGap < 0)
            throw new PivotException("kinematics.pallets.min_gap_deg absent ou negatif");

        double[] positions = ReadDoubleArray(pal, "initial_positions_deg");
        if (positions.Length != count)
            throw new PivotException(
                $"kinematics.pallets : {positions.Length} initial_positions_deg pour count={count}");

        // Invariant geometrique : N palettes espacees d'au moins min_gap doivent tenir sur 360°,
        // sinon l'accumulation deadlocke (place impossible). On echoue plutot que de tourner en rond.
        if (count * minGap > 360.0)
            throw new PivotException(
                $"kinematics.pallets : {count} palettes x min_gap {minGap}° > 360° (placement impossible)");

        // Normalisation defensive des positions dans [0..360) : on replie des angles hors plage
        // (ex. -90, 450) plutot que d'imposer une contrainte de saisie au redacteur du pivot.
        for (int i = 0; i < positions.Length; i++)
            positions[i] = ((positions[i] % 360.0) + 360.0) % 360.0;

        return new KinematicsInfo(count, positions, minGap, ccw);
    }

    private static double[] ReadDoubleArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new PivotException($"{name} absent ou n'est pas un tableau");
        var outp = new List<double>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Number || !el.TryGetDouble(out var d))
                throw new PivotException($"{name} : valeur non numerique");
            outp.Add(d);
        }
        return outp.ToArray();
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

    private static bool TryGetDouble(JsonElement obj, string name, out double value)
    {
        value = 0;
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetDouble(out value))
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

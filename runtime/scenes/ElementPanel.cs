// =============================================================================
// ElementPanel — panneau lateral 2D des elements du pivot (S4.2)
// =============================================================================
//
// Role : remplacer les etiquettes 3D flottantes (rejet UX du sprint 3) par un PANNEAU
// LATERAL 2D lisible qui liste TOUS les composants du pivot, une ligne chacun, avec leur
// mapping %MW decode et leur etat physique. Il relie aussi le tableau a la 3D : survoler
// une ligne demande a la scene d'eclairer l'element correspondant (sens ligne -> 3D).
//
// --- Generique via le pivot (invariant CLAUDE.md, §0bis vision long terme) ---------------
// Le peuplement des lignes est PILOTE PAR LE PIVOT : on itere `pivot.Components.Values`
// (ordre d'insertion = ordre du tableau JSON), une ligne par composant, sans supposer le
// nombre de verins/capteurs ni coder d'adresse %MW en dur. Les adresses affichees sont
// DECODEES depuis les Signal resolus (`AbsWord`/`Bit`) : si l'automaticien recale une zone
// (base_mw), le panneau suit sans toucher ce fichier. Ce panneau vaut donc pour N machines,
// pas seulement le carrousel.
//
// --- Hub de decodage (relocalisation depuis CommandChainLabels, supprime en S4.2) --------
// Ce fichier reprend le role de « hub de decodage » qu'avait CommandChainLabels : il expose
// les trois booleens de retour (Km1Aux/B1Present/B2Present) que CarrouselScene lit pour la
// COLORATION D'ETAT 3D (anneau vert, fenetres capteurs allumees). Le decodage pivot vit
// desormais a UN seul endroit (ici), plus dans un helper d'etiquettes 3D.
//
// --- Frontiere Arch A (thread-safety) --------------------------------------------------
// Glue Godot pure. Update tourne sur le THREAD PRINCIPAL Godot, a BASSE cadence (~6 Hz,
// pilote par CarrouselScene). Il ne recoit que des COPIES verrouillees (snapshots cmd/ret)
// et lit la position des verins via un delegue fourni par la scene. LECTURE SEULE stricte :
// aucune ecriture cmd, aucun acces au datastore interne, aucune traversee de thread.
//
// --- Panneau ANCRE (piege D-Q4 : tenir en Maximized et en 1080p) -----------------------
// Contrairement a HealthHud (positionne en pixels fixes), ce panneau s'ANCRE au bord droit
// de l'ecran sur toute la hauteur (preset RightWide) : il ne suppose aucune resolution. Son
// `Control` racine est en `MouseFilter = Stop`, tout comme chaque ligne : il CONSOMME les
// events souris au-dessus de lui, si bien qu'un drag demarre sur le panneau n'orbite pas la
// camera (qui lit dans _UnhandledInput, non atteint quand la GUI consomme l'event).

using System.Collections.Generic;
using System.Globalization;
using CarrouselCore;
using Godot;

// Godot expose aussi un type `Signal` (signaux du moteur) : on leve l'ambiguite en fixant
// `Signal` = le signal Modbus resolu du pivot, seul sens utile dans ce fichier.
using Signal = CarrouselCore.Signal;

/// <summary>
/// Panneau lateral 2D lecture seule listant les composants du pivot (5 colonnes :
/// Repere | Type | Etat | cmd %MW=bit | ret %MW=bit). Peuple depuis <c>components[]</c> dans
/// l'ordre du pivot, adresses decodees. Relie le tableau a la 3D au survol (ligne -> element)
/// et expose l'etat de retour decode pour la coloration d'etat pilotee par la scene.
/// </summary>
public partial class ElementPanel : CanvasLayer
{
    // Largeur du panneau (px) : mobilier d'ecran hors pivot (comme la camera ou le HealthHud).
    // Le panneau s'ancre au bord droit sur TOUTE la hauteur ; seule la largeur est fixee, et
    // elle est calibree pour loger la colonne la plus longue (retours nommes S11/S12 + adresse).
    private const int PanelWidth = 620;

    // Largeurs de colonnes (px), memes valeurs pour l'entete et chaque ligne : c'est ce qui
    // ALIGNE les colonnes verticalement (chaque cellule est un Label a largeur minimale fixe).
    private static readonly int[] ColWidths = { 46, 84, 132, 116, 210 };
    private static readonly string[] Headers = { "Repère", "Type", "État", "cmd %MW=bit", "ret %MW=bit" };

    // Abreviations d'affichage des types du pivot (lisibilite). Cle = valeur `Component.Type`
    // (convention pivot, pas un id carrousel) ; type inconnu => affiche tel quel. Purement cosmetique.
    private static readonly Dictionary<string, string> TypeLabels = new()
    {
        ["conveyor_circular"] = "convoyeur",
        ["cylinder_blocker"] = "vérin",
        ["sensor_presence"] = "capteur",
    };

    // --- Etat de retour decode, EXPOSE pour la coloration d'etat 3D (cf. CarrouselScene) -----
    // Reprend le role de CommandChainLabels : la scene teinte l'anneau quand Km1Aux=1 et
    // allume les fenetres capteurs quand Bi=1. Ces trois accesseurs sont l'interface (specifique
    // au carrousel, comme les materiaux _ringMat/_sensorMat) entre le decodage et la coloration.
    public bool Km1Aux { get; private set; }
    public bool B1Present { get; private set; }
    public bool B2Present { get; private set; }

    // Signaux de retour resolus une fois au Build pour alimenter les trois booleens ci-dessus.
    // Resolution DEFENSIVE (nullable) : un pivot futur sans ces composants n'empeche pas le
    // panneau de se construire — les booleens restent simplement faux (coloration au repos).
    private Signal? _sigKm1Aux, _sigB1, _sigB2;

    // Delegue fourni par la scene pour obtenir la position 0..1 d'un verin par son id pivot.
    // La simulation n'expose les verins que par accesseurs fixes (Cylinder1/2) ; la scene, qui
    // connait deja ce routage (BuildCylinder), le referme dans ce delegue. Le panneau reste
    // ainsi GENERIQUE : il itere les composants et demande « la position de cet id » sans jamais
    // coder « 2 verins ». Retourne 0 pour un id non route (composant non verin).
    private System.Func<string, double> _cylinderPositionById = _ => 0.0;

    // Callback vers la scene au survol d'une ligne (sens ligne -> element 3D). Injecte au Build.
    private System.Action<string, bool> _onRowHover = static (_, _) => { };

    // Styleboxes partagees des lignes : transparente au repos, teintee en surbrillance. On ne
    // recree rien par survol, on echange juste l'override « panel » de la ligne concernee.
    private readonly StyleBox _rowIdle = new StyleBoxEmpty();
    private readonly StyleBoxFlat _rowHighlight = new() { BgColor = new Color(0.30f, 0.55f, 0.85f, 0.45f) };

    // Une ligne d'interface par composant : son conteneur (pour la surbrillance) + les trois
    // cellules dynamiques (etat/cmd/ret) rafraichies a chaque Update. Repere et Type sont statiques.
    private sealed class Row
    {
        public required Component Comp;
        public required PanelContainer Container;
        public required Label Etat, Cmd, Ret;
    }

    // Lignes indexees par id composant (pour HighlightRow) + liste ordonnee (pour Update dans
    // l'ordre du pivot). Les deux vues partagent les memes objets Row.
    private readonly Dictionary<string, Row> _rowsById = new();
    private readonly List<Row> _rows = new();

    /// <summary>
    /// Resout les signaux de coloration et cree une ligne par composant du pivot (ordre du pivot).
    /// A appeler une fois, apres le chargement du pivot et la construction de la geometrie.
    /// </summary>
    /// <param name="pivot">Modele pivot resolu (source des reperes, types et adresses).</param>
    /// <param name="onRowHover">Appele (id, on) quand la souris entre/sort d'une ligne (ligne -> 3D).</param>
    /// <param name="cylinderPositionById">Position 0..1 d'un verin par son id pivot (routage de la scene).</param>
    public void Build(PivotModel pivot, System.Action<string, bool> onRowHover,
                      System.Func<string, double> cylinderPositionById)
    {
        _onRowHover = onRowHover;
        _cylinderPositionById = cylinderPositionById;

        // Signaux de retour pour la coloration d'etat, resolus DEFENSIVEMENT (voir champ _sigKm1Aux).
        _sigKm1Aux = TryResolve(pivot, "conveyor", "ret_running");
        _sigB1 = TryResolve(pivot, "presence_station_1", "ret_active");
        _sigB2 = TryResolve(pivot, "presence_station_2", "ret_active");

        // --- Fond ANCRE au bord droit, pleine hauteur (piege D-Q4 : aucune resolution en dur) ---
        // Preset RightWide = ancre a droite sur toute la hauteur ; OffsetLeft = -PanelWidth fixe la
        // largeur. MouseFilter = Stop => le fond consomme les clics (la camera n'orbite pas dessus).
        var bg = new Panel { Name = "panel_bg", MouseFilter = Control.MouseFilterEnum.Stop };
        bg.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        bg.OffsetLeft = -PanelWidth;
        bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.62f) });
        AddChild(bg);

        // Marge interieure + pile verticale des lignes. Les conteneurs heritent du MouseFilter Stop
        // par defaut : les zones « vides » entre lignes consomment aussi les clics (camera protegee).
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 8);
        bg.AddChild(margin);

        var list = new VBoxContainer();
        margin.AddChild(list);

        // Entete : une ligne de cinq cellules aux memes largeurs que les lignes de donnees.
        list.AddChild(MakeHeaderRow());

        // Une ligne par composant, dans l'ordre d'insertion (= ordre du tableau JSON du pivot).
        foreach (var comp in pivot.Components.Values)
        {
            var row = MakeRow(comp);
            list.AddChild(row.Container);
            _rows.Add(row);
            _rowsById[comp.Id] = row;
        }

        // Trace deterministe pour le smoke-test headless : nombre de lignes = nombre de composants.
        GD.Print($"[panel] rows={_rows.Count}");
    }

    /// <summary>
    /// Recompose les cellules dynamiques (etat physique + cmd/ret decodes) de chaque ligne a partir
    /// de snapshots verrouilles des zones cmd/ret. Met aussi a jour les booleens de coloration.
    /// Appelee a BASSE cadence par CarrouselScene, sur le thread principal. LECTURE SEULE.
    /// </summary>
    /// <param name="cmd">Copie de la zone cmd (mots relatifs a sa base), indexee par WordRel.</param>
    /// <param name="ret">Copie de la zone ret (mots relatifs a sa base), indexee par WordRel.</param>
    public void Update(ushort[] cmd, ushort[] ret)
    {
        foreach (var row in _rows)
        {
            row.Etat.Text = PhysicalState(row.Comp, cmd, ret);
            row.Cmd.Text = ZoneAddresses(row.Comp, "cmd", cmd);
            row.Ret.Text = ZoneAddresses(row.Comp, "ret", ret);
        }

        // Booleens exposes pour la coloration (Signal? => faux si le signal n'existe pas).
        Km1Aux = _sigKm1Aux is Signal s0 && s0.ReadBit(ret[s0.WordRel]);
        B1Present = _sigB1 is Signal s1 && s1.ReadBit(ret[s1.WordRel]);
        B2Present = _sigB2 is Signal s2 && s2.ReadBit(ret[s2.WordRel]);
    }

    /// <summary>
    /// Surligne/dé-surligne la ligne d'un composant (echange de stylebox). Appelee par la scene :
    /// c'est le point d'entree du sens 3D -> ligne (branche une 2e source en S4.3) et du sens
    /// ligne -> 3D (via <c>SetHover</c> qui, en plus de l'emission, rappelle cette methode).
    /// </summary>
    public void HighlightRow(string componentId, bool on)
    {
        if (_rowsById.TryGetValue(componentId, out var row))
            row.Container.AddThemeStyleboxOverride("panel", on ? _rowHighlight : _rowIdle);
    }

    // --- Decodage des cellules (pilote par le pivot, dispatch par type) --------------------

    /// <summary>
    /// Etat physique lisible d'un composant, dispatch par <see cref="Component.Type"/> (convention
    /// pivot, generique). Reprend la logique de CommandChainLabels : convoyeur = marche/arret,
    /// verin = pourcentage de tige + phase, capteur = presence. Type inconnu => tiret.
    /// </summary>
    private string PhysicalState(Component comp, ushort[] cmd, ushort[] ret)
    {
        switch (comp.Type)
        {
            case "conveyor_circular":
            {
                // Marche = contact de retour KM1_AUX ferme ; vitesse affichee depuis le param pivot.
                bool run = ReadNamed(comp, "ret_running", cmd, ret);
                double speed = comp.Params.TryGetValue("speed_deg_per_s", out var sp) ? sp : 0.0;
                return run
                    ? string.Format(CultureInfo.InvariantCulture, "marche {0:0.#}°/s", speed)
                    : "arrêt";
            }
            case "cylinder_blocker":
            {
                // % depuis la position sim (routee par id) ; phase deduite des butees et de la commande.
                bool ext = ReadNamed(comp, "cmd_extend", cmd, ret);
                int pct = (int)System.Math.Round(_cylinderPositionById(comp.Id) * 100.0);
                string phase = pct >= 98 ? "sorti"
                             : pct <= 2 ? "rentré"
                             : ext ? "sort" : "rentre";
                return $"tige {pct}% ({phase})";
            }
            case "sensor_presence":
            {
                bool present = ReadNamed(comp, "ret_active", cmd, ret);
                return present ? "palette présente" : "poste libre";
            }
            default:
                return "—";
        }
    }

    /// <summary>
    /// Adresses %MW decodees d'un composant pour une zone donnee (« cmd » ou « ret »), au format
    /// automaticien <c>[tag ]%MW&lt;mot&gt;.&lt;bit&gt;={0|1}</c>. Chaque retour nomme (S11/S12,
    /// KM1_AUX...) est prefixe de son tag. Tiret si le composant n'a aucun signal TOR dans la zone.
    /// </summary>
    private static string ZoneAddresses(Component comp, string zone, ushort[] words)
    {
        var parts = new List<string>();
        foreach (var s in comp.Signals.Values)
        {
            if (s.Zone != zone || !s.IsTor)
                continue;
            string prefix = s.Tag is null ? "" : s.Tag + " ";
            parts.Add($"{prefix}%MW{s.AbsWord}.{s.Bit}={(s.ReadBit(words[s.WordRel]) ? "1" : "0")}");
        }
        return parts.Count == 0 ? "—" : string.Join("  ", parts);
    }

    // Lit un bit nomme d'un composant depuis la zone qui le porte (cmd/ret). Absent => faux (le
    // composant n'a pas ce signal : cas normal, ex. un capteur sans commande). Aucune exception.
    private static bool ReadNamed(Component comp, string name, ushort[] cmd, ushort[] ret)
    {
        if (!comp.Signals.TryGetValue(name, out var s) || !s.IsTor)
            return false;
        return s.ReadBit((s.Zone == "cmd" ? cmd : ret)[s.WordRel]);
    }

    // Resout un signal (compId, name) sans lever : retourne null si le composant ou le signal
    // manque. Sert a resoudre defensivement les signaux de coloration (interface carrousel).
    private static Signal? TryResolve(PivotModel pivot, string compId, string signalName)
        => pivot.Components.TryGetValue(compId, out var c) && c.Signals.TryGetValue(signalName, out var s)
            ? s
            : null;

    // --- Construction de l'interface -------------------------------------------------------

    // Ligne d'entete : cinq Labels en gras a largeur fixe (pas de survol, pas de conteneur teinte).
    private HBoxContainer MakeHeaderRow()
    {
        var hbox = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        for (int i = 0; i < Headers.Length; i++)
        {
            var lbl = MakeCell(Headers[i], ColWidths[i]);
            lbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 1f));
            hbox.AddChild(lbl);
        }
        return hbox;
    }

    // Ligne de donnees : un PanelContainer (surbrillance + capture du survol) contenant cinq
    // cellules. Repere/Type sont figes ici ; Etat/cmd/ret sont memorisees pour Update.
    private Row MakeRow(Component comp)
    {
        // Conteneur de ligne : MouseFilter Stop pour recevoir mouse_entered/exited ET consommer le
        // clic (la camera n'orbite pas quand on part d'une ligne). Stylebox transparente au repos.
        var container = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop };
        container.AddThemeStyleboxOverride("panel", _rowIdle);

        var hbox = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        string typeLabel = TypeLabels.TryGetValue(comp.Type, out var t) ? t : comp.Type;

        var repere = MakeCell(comp.Tag, ColWidths[0]);
        var type = MakeCell(typeLabel, ColWidths[1]);
        var etat = MakeCell("…", ColWidths[2]);
        var cmdCell = MakeCell("…", ColWidths[3]);
        var retCell = MakeCell("…", ColWidths[4]);
        foreach (var c in new[] { repere, type, etat, cmdCell, retCell })
            hbox.AddChild(c);
        container.AddChild(hbox);

        // Survol : les signaux Godot mouse_entered/mouse_exited du conteneur declenchent le sens
        // ligne -> 3D. On capture l'id du composant dans la lambda (chaque ligne son propre id).
        string id = comp.Id;
        container.MouseEntered += () => _onRowHover(id, true);
        container.MouseExited += () => _onRowHover(id, false);

        return new Row { Comp = comp, Container = container, Etat = etat, Cmd = cmdCell, Ret = retCell };
    }

    // Une cellule = un Label a largeur minimale fixe (alignement des colonnes), texte tronque si
    // trop long (ClipText) plutot que d'elargir la colonne. MouseFilter Ignore pour laisser le
    // survol atteindre le conteneur de ligne (sinon le Label capterait l'event a sa place).
    private static Label MakeCell(string text, int width)
    {
        return new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, 0),
            ClipText = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }
}

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
    // Largeur INITIALE du panneau (px) : mobilier d'ecran hors pivot (comme la camera ou le
    // HealthHud). Le panneau s'ancre au bord droit sur TOUTE la hauteur. La largeur n'est plus
    // figee : une poignee sur le bord gauche la fait varier au drag (cf. _panelWidth / _handle).
    private const int DefaultPanelWidth = 620;

    // Bornes de la largeur redimensionnable (px). Le plancher garde le panneau lisible ; le
    // plafond reel est recalcule au drag depuis la largeur de la fenetre (jamais de resolution en dur).
    private const int MinPanelWidth = 240;

    // Largeur COURANTE du panneau, pilotee par la poignee de redimensionnement. Sert de valeur
    // de reference partout (OffsetLeft du fond = -_panelWidth). En float pour un drag fluide.
    private float _panelWidth = DefaultPanelWidth;

    // Largeurs de colonnes (px) : PLANCHERS d'alignement. Les colonnes s'ajustent au contenu le
    // plus long rencontre (cf. AutoFitColumns) mais ne descendent jamais sous ces valeurs, ce qui
    // garde un tableau tabulaire meme quand les cellules sont courtes.
    // S5.3 : 6e colonne « Défaut » (libellé du/des défaut(s) actif(s) de l'élément, « — » si aucun).
    // Un MenuButton s'ajoute EN PLUS en bout de ligne (hors colonne mesuree) pour declencher/reparer.
    private static readonly int[] ColWidths = { 46, 84, 132, 116, 210, 180 };
    private static readonly string[] Headers = { "Repère", "Type", "État", "cmd %MW=bit", "ret %MW=bit", "Défaut" };

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

    // Callback vers la scene au CLIC d'une ligne (sens ligne -> selection). Injecte au Build (S5.2).
    // La scene y route sa source unique SetSelected : clic de ligne et clic 3D convergent donc.
    private System.Action<string> _onRowClick = static _ => { };

    // --- Injection de defauts (S5.3) : deux delegues fournis par la scene (comme _onRowHover) -------
    // _onFault : appele quand l'utilisateur choisit une entree du menu d'une ligne. La scene y route
    // _sim.Faults.Apply(cmd) — c'est le PREMIER endroit ou l'IHM ECRIT dans la sim (meme thread
    // principal, aucune traversee de thread). _faultLabelById : rend le libelle « Défaut » d'un
    // composant (« — » si aucun, ou masque par le mode aveugle), interroge a chaque Update et au
    // peuplement du menu (pour savoir s'il faut y ajouter « Réparer »). Le contenu du menu, lui,
    // vient de FaultCatalog (generique par type + signaux ret TOR), jamais d'une liste carrousel.
    private System.Action<FaultCommand> _onFault = static _ => { };
    private System.Func<string, string> _faultLabelById = static _ => "—";

    // Indicateur « MODE AVEUGLE » (S5.3) : rappel visuel que le marquage des defauts est masque alors
    // que les defauts restent REELLEMENT injectes (sinon on s'y perd). Bascule par SetBlindMode.
    private Label _blindIndicator = null!;

    // --- Encart « conséquence du défaut » (demande Nico) : SOUS le tableau, dans le meme panneau -----
    // Explique EN CLAIR la consequence de chaque defaut actif, chaque bloc nomme par le repere de son
    // element (« relie a l'element mis en panne »), pour rendre l'effet OBSERVABLE en demo. Il n'a
    // aucune source propre : la SCENE le nourrit via RefreshFaults (elle seule connait _sim.Faults et
    // l'etat du serveur). Masque quand aucun defaut OU en mode aveugle (meme regle que le badge de la
    // colonne « Défaut »). Corps en AutowrapMode : le texte s'ajuste a la largeur du panneau
    // redimensionnable, sans jamais declencher de defilement horizontal.
    private PanelContainer _faultEncart = null!;
    private Label _faultEncartBody = null!;

    // Styleboxes partagees des lignes : transparente au repos, teintee en survol, teintee (cyan) en
    // selection. On ne recree rien par frame, on echange juste l'override « panel » de la ligne. La
    // selection (S5.2) prime visuellement sur le survol : les deux etats se composent dans RefreshRowStyle.
    private readonly StyleBox _rowIdle = new StyleBoxEmpty();
    private readonly StyleBoxFlat _rowHighlight = new() { BgColor = new Color(0.30f, 0.55f, 0.85f, 0.45f) };
    private readonly StyleBoxFlat _rowSelect = new() { BgColor = new Color(0.15f, 0.75f, 0.85f, 0.55f) };

    // Etat de survol / selection au niveau du panneau (S5.2), miroir de celui de la scene : chacun au
    // plus un id (pointeur unique ; selection unique). RefreshRowStyle arbitre lequel s'affiche.
    private string? _hoveredRowId;
    private string? _selectedRowId;

    // Une ligne d'interface par composant : son conteneur (pour la surbrillance) + les trois
    // cellules dynamiques (etat/cmd/ret) rafraichies a chaque Update. Repere et Type sont statiques.
    // Cells = les 5 cellules dans l'ordre des colonnes, pour l'ajustement de largeur (AutoFitColumns).
    private sealed class Row
    {
        public required Component Comp;
        public required PanelContainer Container;
        public required Label Etat, Cmd, Ret;
        public required Label[] Cells;

        // S5.3 : cellule « Défaut » (libelle du/des defaut(s) actif(s), rafraichie a chaque Update)
        // + MenuButton de declenchement/reparation + liste des commandes du popup (index -> commande,
        // reconstruite a chaque ouverture pour refleter l'etat courant, cf. PopulateFaultMenu).
        public required Label Defaut;
        public required MenuButton Menu;
        public required List<FaultCommand> MenuCommands;
    }

    // Lignes indexees par id composant (pour HighlightRow) + liste ordonnee (pour Update dans
    // l'ordre du pivot). Les deux vues partagent les memes objets Row.
    private readonly Dictionary<string, Row> _rowsById = new();
    private readonly List<Row> _rows = new();

    // Cellules d'entete (memes colonnes que les lignes), mesurees comme les autres dans AutoFitColumns.
    private Label[] _headerCells = System.Array.Empty<Label>();

    // Largeurs de colonnes COURANTES (grow-only) : partent des planchers ColWidths et ne font que
    // croitre vers le contenu le plus large vu. Grow-only = pas de tremblement de largeur quand un
    // texte d'etat court remplace un texte long d'une frame a l'autre.
    private readonly int[] _colWidths = (int[])ColWidths.Clone();

    // Fond ancre + poignee de redimensionnement, memorises pour le drag. _resizing = drag en cours.
    private Panel _bg = null!;
    private bool _resizing;

    /// <summary>
    /// Resout les signaux de coloration et cree une ligne par composant du pivot (ordre du pivot).
    /// A appeler une fois, apres le chargement du pivot et la construction de la geometrie.
    /// </summary>
    /// <param name="pivot">Modele pivot resolu (source des reperes, types et adresses).</param>
    /// <param name="onRowHover">Appele (id, on) quand la souris entre/sort d'une ligne (ligne -> 3D).</param>
    /// <param name="onRowClick">Appele (id) au clic gauche d'une ligne (ligne -> selection).</param>
    /// <param name="cylinderPositionById">Position 0..1 d'un verin par son id pivot (routage de la scene).</param>
    /// <param name="onFault">Appele avec la commande choisie dans le menu d'une ligne (S5.3, ligne -> sim).</param>
    /// <param name="faultLabelById">Libelle « Défaut » d'un composant (« — » si aucun / masque aveugle).</param>
    public void Build(PivotModel pivot, System.Action<string, bool> onRowHover,
                      System.Action<string> onRowClick,
                      System.Func<string, double> cylinderPositionById,
                      System.Action<FaultCommand> onFault,
                      System.Func<string, string> faultLabelById)
    {
        _onRowHover = onRowHover;
        _onRowClick = onRowClick;
        _cylinderPositionById = cylinderPositionById;
        _onFault = onFault;
        _faultLabelById = faultLabelById;

        // Signaux de retour pour la coloration d'etat, resolus DEFENSIVEMENT (voir champ _sigKm1Aux).
        _sigKm1Aux = TryResolve(pivot, "conveyor", "ret_running");
        _sigB1 = TryResolve(pivot, "presence_station_1", "ret_active");
        _sigB2 = TryResolve(pivot, "presence_station_2", "ret_active");

        // --- Fond ANCRE au bord droit, pleine hauteur (piege D-Q4 : aucune resolution en dur) ---
        // Preset RightWide = ancre a droite sur toute la hauteur ; OffsetLeft = -_panelWidth fixe la
        // largeur (variable au drag). MouseFilter = Stop => le fond consomme les clics (la camera
        // n'orbite pas dessus).
        _bg = new Panel { Name = "panel_bg", MouseFilter = Control.MouseFilterEnum.Stop };
        _bg.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        _bg.OffsetLeft = -_panelWidth;
        _bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.62f) });
        AddChild(_bg);

        // Marge interieure. Les conteneurs heritent du MouseFilter Stop par defaut : les zones
        // « vides » consomment aussi les clics (camera protegee).
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, 8);
        _bg.AddChild(margin);

        // Colonne verticale : le TABLEAU (extensible) en haut, l'ENCART conséquence en bas. Le tableau
        // prend toute la hauteur restante (ExpandFill sur son scroll) ; l'encart se pose SOUS lui, dans
        // le meme fond ancre. C'est ce qui satisfait « en dessous du tableau, dans le meme encart ».
        var column = new VBoxContainer();
        margin.AddChild(column);

        // --- Zone defilante (H + V) : rend lisibles les colonnes plus larges que le panneau -------
        // ScrollContainer en mode Auto : la barre horizontale apparait des qu'une ligne depasse la
        // largeur visible (colonne ret nommee, etat long...), la verticale des que les lignes
        // depassent la hauteur (utile quand la machine generee aura beaucoup de composants). L'entete
        // vit DANS la zone defilante, avec les lignes : elle scrolle horizontalement avec elles, ce
        // qui garde les colonnes alignees sous leur titre. ExpandFill : le tableau absorbe la hauteur,
        // laissant l'encart conséquence se poser en bas.
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddChild(scroll);

        var list = new VBoxContainer();
        scroll.AddChild(list);

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

        // Encart « conséquence du défaut », ajoute SOUS le tableau dans la meme colonne (cf. _faultEncart).
        BuildFaultEncart(column);

        // --- Poignee de redimensionnement (bord GAUCHE du panneau) -----------------------------
        // Bande fine ancree sur toute la hauteur du bord gauche, curseur « redimensionnement
        // horizontal ». Ajoutee EN DERNIER (donc au-dessus de la marge) pour capter le drag sur ses
        // 6 px. MouseFilter Stop => elle consomme le clic (ni scroll, ni orbite camera). Le drag est
        // traite dans OnHandleGuiInput, qui deplace OffsetLeft du fond.
        var handle = new Control
        {
            Name = "resize_handle",
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Hsize,
        };
        handle.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        handle.OffsetRight = 6;
        handle.GuiInput += OnHandleGuiInput;
        _bg.AddChild(handle);

        // --- Indicateur « MODE AVEUGLE » (S5.3) ------------------------------------------------
        // Bandeau ancre en HAUT-CENTRE de l'ecran (donc visible quelle que soit la largeur du
        // panneau), cache par defaut. SetBlindMode le montre/masque. MouseFilter Ignore : il ne
        // capte aucun clic (purement indicatif). Rouge attenue pour rester lisible sans alarmer.
        _blindIndicator = new Label
        {
            Text = "MODE AVEUGLE",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _blindIndicator.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.30f));
        _blindIndicator.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        AddChild(_blindIndicator);

        // Trace deterministe pour le smoke-test headless : nombre de lignes = nombre de composants.
        GD.Print($"[panel] rows={_rows.Count}");
    }

    // --- Redimensionnement du panneau au drag de la poignee gauche -----------------------------

    /// <summary>
    /// Gere le glisser de la poignee gauche : bouton gauche enfonce = debut du drag, relache = fin ;
    /// chaque mouvement ajuste la largeur du panneau (glisser vers la gauche = elargir). La largeur
    /// est bornee entre <see cref="MinPanelWidth"/> et la largeur de la fenetre moins une marge, sans
    /// jamais coder de resolution en dur. Applique la nouvelle largeur via l'OffsetLeft du fond.
    /// </summary>
    private void OnHandleGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb)
            _resizing = mb.Pressed;
        else if (@event is InputEventMouseMotion mm && _resizing)
        {
            // Relative.X < 0 quand on glisse vers la gauche => on SOUSTRAIT pour elargir a gauche.
            float max = _bg.GetViewportRect().Size.X - 40f;
            _panelWidth = Mathf.Clamp(_panelWidth - mm.Relative.X, MinPanelWidth, max);
            _bg.OffsetLeft = -_panelWidth;
        }
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
            // S5.3 : la colonne « Défaut » est un MIROIR de l'etat de defauts de la sim (source de
            // verite cote scene) ; le mode aveugle y renvoie « — » sans lever le defaut reel.
            row.Defaut.Text = _faultLabelById(row.Comp.Id);
        }

        // Reajuste les largeurs de colonnes au contenu (aucune cellule tronquee, colonnes alignees).
        AutoFitColumns();

        // Booleens exposes pour la coloration (Signal? => faux si le signal n'existe pas).
        Km1Aux = _sigKm1Aux is Signal s0 && s0.ReadBit(ret[s0.WordRel]);
        B1Present = _sigB1 is Signal s1 && s1.ReadBit(ret[s1.WordRel]);
        B2Present = _sigB2 is Signal s2 && s2.ReadBit(ret[s2.WordRel]);
    }

    /// <summary>
    /// Surligne/dé-surligne (SURVOL) la ligne d'un composant. Appelee par la scene : c'est le point
    /// d'entree du sens 3D -> ligne et du sens ligne -> 3D (via <c>SetHover</c>). Ne fait plus qu'un
    /// EFFET DIRECT : elle met a jour l'etat de survol du panneau puis delegue le rendu a
    /// <see cref="RefreshRowStyle"/>, qui compose survol et selection (la selection prime).
    /// </summary>
    public void HighlightRow(string componentId, bool on)
    {
        if (on)
            _hoveredRowId = componentId;
        else if (_hoveredRowId == componentId)
            _hoveredRowId = null;

        RefreshRowStyle(componentId);
    }

    /// <summary>
    /// Selectionne/deselectionne une ligne (S5.2). <c>null</c> = deselection. Symetrique de
    /// <see cref="HighlightRow"/> pour la SELECTION persistante : la scene y route sa source unique
    /// (clic 3D ou clic de ligne). On rafraichit l'ANCIENNE et la NOUVELLE ligne pour que l'ancienne
    /// retombe au survol/repos et la nouvelle prenne la stylebox de selection.
    /// </summary>
    public void SelectRow(string? componentId)
    {
        string? previous = _selectedRowId;
        _selectedRowId = componentId;

        if (previous is not null)
            RefreshRowStyle(previous);
        if (componentId is not null)
            RefreshRowStyle(componentId);
    }

    /// <summary>
    /// Resout la stylebox d'une ligne par PRIORITE (S5.2), miroir de <c>RefreshEmission</c> cote 3D :
    /// selectionnee (cyan) prime sur survolee (bleu), qui prime sur repos (transparente). Seul endroit
    /// qui ECRIT l'override « panel » d'une ligne ; survol et selection ne font que muter leur etat.
    /// </summary>
    private void RefreshRowStyle(string componentId)
    {
        if (!_rowsById.TryGetValue(componentId, out var row))
            return;

        StyleBox style = _selectedRowId == componentId ? _rowSelect
                       : _hoveredRowId == componentId ? _rowHighlight
                       : _rowIdle;
        row.Container.AddThemeStyleboxOverride("panel", style);
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

    // --- Ajustement des largeurs de colonnes (grow-only, aligne toutes les lignes) ------------

    /// <summary>
    /// Ajuste la largeur minimale de chaque colonne au contenu le plus large (entete comprise),
    /// puis applique cette meme largeur a toutes les cellules de la colonne — c'est ce qui GARDE les
    /// colonnes ALIGNEES d'une ligne a l'autre une fois <c>ClipText</c> retire. Grow-only : les
    /// largeurs ne descendent jamais sous ce qu'on a deja vu (ni sous <see cref="ColWidths"/>), pour
    /// eviter tout tremblement quand un texte court remplace un texte long. Ce qui deborde du panneau
    /// devient accessible via la barre de defilement horizontale.
    /// </summary>
    private void AutoFitColumns()
    {
        for (int c = 0; c < _colWidths.Length; c++)
        {
            int want = _colWidths[c];
            if (c < _headerCells.Length)
                want = System.Math.Max(want, MeasureTextWidth(_headerCells[c]));
            foreach (var row in _rows)
                want = System.Math.Max(want, MeasureTextWidth(row.Cells[c]));

            if (want == _colWidths[c])
                continue; // deja a la bonne largeur : rien a toucher (pas d'invalidation de layout)

            _colWidths[c] = want;
            if (c < _headerCells.Length)
                _headerCells[c].CustomMinimumSize = new Vector2(want, 0);
            foreach (var row in _rows)
                row.Cells[c].CustomMinimumSize = new Vector2(want, 0);
        }
    }

    // Largeur pixel du texte d'une cellule, mesuree directement sur la police du theme (independant
    // de toute passe de layout, donc fiable des le premier Update). +4 px de garde pour ne pas raser
    // le dernier glyphe. Police absente (theme pas encore pret) => 0, la colonne garde son plancher.
    private static int MeasureTextWidth(Label lbl)
    {
        var font = lbl.GetThemeFont("font");
        if (font is null)
            return 0;
        int fontSize = lbl.GetThemeFontSize("font_size");
        float w = font.GetStringSize(lbl.Text, HorizontalAlignment.Left, -1, fontSize).X;
        return Mathf.CeilToInt(w) + 4;
    }

    // --- Construction de l'interface -------------------------------------------------------

    // Ligne d'entete : cinq Labels en gras a largeur fixe (pas de survol, pas de conteneur teinte).
    // Les cellules sont memorisees dans _headerCells pour etre mesurees comme les lignes (alignement).
    private HBoxContainer MakeHeaderRow()
    {
        var hbox = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _headerCells = new Label[Headers.Length];
        for (int i = 0; i < Headers.Length; i++)
        {
            var lbl = MakeCell(Headers[i], ColWidths[i]);
            lbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.85f, 1f));
            hbox.AddChild(lbl);
            _headerCells[i] = lbl;
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
        var defautCell = MakeCell("—", ColWidths[5]);   // S5.3 : libelle du/des defaut(s) actif(s)
        var cells = new[] { repere, type, etat, cmdCell, retCell, defautCell };
        foreach (var c in cells)
            hbox.AddChild(c);

        // --- Menu de defauts (S5.3) : un MenuButton en bout de ligne ----------------------------
        // Son popup est peuple A CHAQUE OUVERTURE (signal AboutToPopup) depuis FaultCatalog (generique
        // par type + signaux ret TOR) plus une entree « Réparer » si un defaut est actif. On memorise
        // dans menuCommands la commande de chaque item (dans l'ordre d'insertion) : IndexPressed nous
        // rend l'INDICE du choix, qu'on retraduit en FaultCommand transmise a la scene via _onFault.
        // MouseFilter Stop : le bouton consomme son clic (ni scroll, ni orbite camera, ni selection ligne).
        var menu = new MenuButton { Text = "▾", MouseFilter = Control.MouseFilterEnum.Stop };
        var popup = menu.GetPopup();
        var menuCommands = new List<FaultCommand>();
        popup.AboutToPopup += () => PopulateFaultMenu(comp, popup, menuCommands);
        popup.IndexPressed += index =>
        {
            if (index >= 0 && index < menuCommands.Count)
                _onFault(menuCommands[(int)index]);
        };
        hbox.AddChild(menu);

        container.AddChild(hbox);

        // Survol : les signaux Godot mouse_entered/mouse_exited du conteneur declenchent le sens
        // ligne -> 3D. On capture l'id du composant dans la lambda (chaque ligne son propre id).
        string id = comp.Id;
        container.MouseEntered += () => _onRowHover(id, true);
        container.MouseExited += () => _onRowHover(id, false);

        // Clic (S5.2) : le contencur (MouseFilter=Stop) recoit ses events GUI et les CONSOMME avant
        // _UnhandledInput -> pas de double-selection cote 3D. On selectionne au RELACHE du bouton
        // gauche (symetrie avec le clic 3D de la scene, lui aussi au relache), via la source unique.
        container.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
                _onRowClick(id);
        };

        return new Row
        {
            Comp = comp, Container = container, Etat = etat, Cmd = cmdCell, Ret = retCell, Cells = cells,
            Defaut = defautCell, Menu = menu, MenuCommands = menuCommands,
        };
    }

    // --- Menu de defauts (S5.3) : peuplement + API d'ouverture/masquage ------------------------

    /// <summary>
    /// (Re)peuple le popup d'une ligne A CHAQUE ouverture : d'abord les modes applicables du
    /// composant (<see cref="FaultCatalog.ApplicableTo"/> — physique du type + capteur-bloque de
    /// chaque signal `ret` TOR, 100 % generique), puis une entree « Réparer » SEULEMENT si un defaut
    /// est actif (label != « — »). On reconstruit en parallele <paramref name="commands"/> pour que
    /// l'indice de l'item choisi (IndexPressed) retrouve exactement sa commande. Pas de separateur :
    /// un separateur compterait comme un item et decalerait les indices.
    /// </summary>
    private void PopulateFaultMenu(Component comp, PopupMenu popup, List<FaultCommand> commands)
    {
        popup.Clear();
        commands.Clear();

        foreach (var cmd in FaultCatalog.ApplicableTo(comp))
        {
            popup.AddItem(FaultCommandLabel(cmd, comp));
            commands.Add(cmd);
        }

        // « Réparer » n'est pas emis par le catalogue : l'UI l'ajoute quand l'element porte un defaut.
        if (_faultLabelById(comp.Id) != "—")
        {
            var repair = new FaultCommand(FaultKind.Repair, comp.Id);
            popup.AddItem(FaultCommandLabel(repair, comp));
            commands.Add(repair);
        }
    }

    /// <summary>Ouvre le popup de defauts d'une ligne (touche « F »/« Espace » cote scene sur la selection).</summary>
    public void OpenFaultMenu(string componentId)
    {
        if (_rowsById.TryGetValue(componentId, out var row))
            row.Menu.ShowPopup();
    }

    /// <summary>Montre/masque l'indicateur « MODE AVEUGLE » (le defaut reste injecte ; seul l'indice visuel change).</summary>
    public void SetBlindMode(bool on) => _blindIndicator.Visible = on;

    // --- Encart « conséquence du défaut » (demande Nico) : construction + mise a jour --------------

    /// <summary>
    /// Construit l'encart « conséquence du défaut » (masque par defaut) et l'ajoute SOUS le tableau,
    /// dans la meme colonne. Un titre fixe colore + un corps <c>AutowrapMode</c> alimente par
    /// <see cref="RefreshFaults"/>. Fond rouge attenue pour se distinguer du tableau sans alarmer.
    /// </summary>
    private void BuildFaultEncart(VBoxContainer column)
    {
        // MouseFilter Ignore partout : purement indicatif, il ne capte aucun clic (ni selection, ni
        // orbite camera). Le fond du panneau (_bg, MouseFilter Stop) protege deja la camera dessous.
        _faultEncart = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _faultEncart.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.32f, 0.06f, 0.05f, 0.90f),
            ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _faultEncart.AddChild(box);

        var title = new Label { Text = "CONSÉQUENCE DU DÉFAUT", MouseFilter = Control.MouseFilterEnum.Ignore };
        title.AddThemeColorOverride("font_color", new Color(1f, 0.70f, 0.55f));
        box.AddChild(title);

        // Corps en AutowrapMode Word : le texte s'enroule a la largeur de la colonne (= largeur du
        // panneau), donc pas de defilement horizontal ; sa hauteur suit le contenu (1 a N defauts).
        _faultEncartBody = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _faultEncartBody.AddThemeColorOverride("font_color", new Color(1f, 0.90f, 0.85f));
        box.AddChild(_faultEncartBody);

        column.AddChild(_faultEncart);
    }

    /// <summary>
    /// Met a jour l'encart « conséquence du défaut » depuis la liste (titre, explication) fournie par
    /// la SCENE — elle seule connait <c>_sim.Faults</c> et l'etat serveur, et applique deja le mode
    /// aveugle (liste vide s'il est actif). Liste vide => encart masque. Un bloc par defaut : le titre
    /// « <repère> — <libellé> » puis l'explication indentee, blocs separes par une ligne vide.
    /// </summary>
    public void RefreshFaults(System.Collections.Generic.IReadOnlyList<(string Heading, string Body)> entries)
    {
        if (entries.Count == 0)
        {
            _faultEncart.Visible = false;
            return;
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0)
                sb.Append("\n\n");
            sb.Append(entries[i].Heading).Append('\n').Append("  ").Append(entries[i].Body);
        }
        _faultEncartBody.Text = sb.ToString();
        _faultEncart.Visible = true;
    }

    /// <summary>
    /// Libelle FR d'une commande de defaut (presentation, esprit <see cref="TypeLabels"/>) : mappe le
    /// <see cref="FaultCommand"/> vers un texte lisible par l'automaticien. Reutilise par le menu (cote
    /// panneau) ET par la colonne « Défaut » (cote scene, qui agrege les defauts actifs) — un seul
    /// endroit ou vit la traduction. Type/mode inconnu => libelle brut (defensif, jamais d'exception).
    /// </summary>
    public static string FaultCommandLabel(FaultCommand cmd, Component comp)
    {
        switch (cmd.Kind)
        {
            case FaultKind.Repair:
                return "réparer";
            case FaultKind.Physical:
                return cmd.Physical switch
                {
                    PhysicalFault.CylinderStuckRetracted => "vérin : ne sort pas",
                    PhysicalFault.CylinderStuckMidStroke => "vérin : coincé mi-course",
                    PhysicalFault.ConveyorSlip => "convoyeur : patine",
                    _ => cmd.Physical.ToString(),
                };
            case FaultKind.SensorStuck:
                string label = SignalLabel(comp, cmd.SignalName);
                return cmd.Stuck switch
                {
                    StuckMode.Low => $"{label} bloqué à 0",
                    StuckMode.High => $"{label} bloqué à 1",
                    _ => cmd.ToString(),
                };
            default:
                return cmd.ToString();
        }
    }

    // --- Textes pedagogiques de l'encart « conséquence du défaut » (demande Nico) -----------------
    // Comme FaultCommandLabel, ces textes vivent ICI (un seul endroit ou vit la presentation FR) et
    // sont GENERIQUES PAR MODE de defaut, jamais par id carrousel (§0bis : ils survivront a une machine
    // generee depuis un DWG inconnu). La scene appelle FaultEntry / CommFaultEntry pour composer la
    // liste (titre, explication) qu'elle pousse au panneau via RefreshFaults.

    /// <summary>Defaut de communication global (non lie a un element) : deux modes du sprint 5.</summary>
    public enum CommFaultKind { RetFrozen, TcpCut }

    /// <summary>
    /// (Titre, explication) d'un defaut PAR ELEMENT : le titre relie le defaut a son repere
    /// (« <repère> — <libellé> », <see cref="FaultCommandLabel"/>), l'explication en decrit la
    /// consequence pour le PLC (<see cref="FaultExplanation"/>).
    /// </summary>
    public static (string Heading, string Body) FaultEntry(FaultCommand cmd, Component comp)
        => ($"{comp.Tag} — {FaultCommandLabel(cmd, comp)}", FaultExplanation(cmd, comp));

    /// <summary>(Titre, explication) d'un defaut de COMMUNICATION global (gel des retours / coupure TCP).</summary>
    public static (string Heading, string Body) CommFaultEntry(CommFaultKind kind) => kind switch
    {
        CommFaultKind.RetFrozen => ("Communication — gel des retours",
            "La simulation tourne toujours (la 3D bouge) mais ne publie plus aucun mot ret, heartbeat compris. " +
            "Le PLC voit une image figée alors que la machine évolue : un watchdog doit détecter l'absence de heartbeat."),
        CommFaultKind.TcpCut => ("Communication — coupure TCP",
            "Le serveur Modbus a fermé le port 502 : l'esclave disparaît de l'I/O Scanner. " +
            "Le M580 tombe en défaut de scrutation jusqu'à la reconnexion (re-bind du port 502)."),
        _ => ("Communication", ""),
    };

    /// <summary>
    /// Explication pedagogique de la CONSEQUENCE d'un defaut par element, generique par mode. Le
    /// repere du signal bloque est resolu via <see cref="SignalLabel"/> (S11/S12, KM1_AUX...). Mode
    /// inconnu => texte neutre (defensif, jamais d'exception).
    /// </summary>
    public static string FaultExplanation(FaultCommand cmd, Component comp)
    {
        switch (cmd.Kind)
        {
            case FaultKind.Physical:
                return cmd.Physical switch
                {
                    PhysicalFault.CylinderStuckRetracted =>
                        "La tige reste rentrée malgré la commande d'extension : le fin de course « sorti » ne passe " +
                        "jamais à 1. Le PLC voit une commande sans confirmation de fin de course (défaut de course).",
                    PhysicalFault.CylinderStuckMidStroke =>
                        "La tige se fige à mi-course : ni le fin de course « rentré » ni « sorti » n'est à 1. " +
                        "Le PLC ne sait plus où est le vérin (position indéterminée, aucune butée confirmée).",
                    PhysicalFault.ConveyorSlip =>
                        "KM1_AUX confirme la marche mais les palettes n'avancent plus et B1/B2 n'évoluent pas. " +
                        "Sans codeur de mouvement, le PLC ne détecte pas le glissement : marche commandée ≠ mouvement réel.",
                    _ => "Comportement physique dégradé de l'élément.",
                };
            case FaultKind.SensorStuck:
            {
                string label = SignalLabel(comp, cmd.SignalName);
                return cmd.Stuck switch
                {
                    StuckMode.Low =>
                        $"Le retour {label} est forcé à 0 quel que soit l'état réel : le PLC le lit en permanence " +
                        "inactif. Un capteur muet peut masquer une présence réelle ou une fin de course pourtant atteinte.",
                    StuckMode.High =>
                        $"Le retour {label} est forcé à 1 quel que soit l'état réel : le PLC le lit en permanence " +
                        "actif. Un capteur collé peut simuler une présence absente ou une fin de course jamais atteinte.",
                    _ => $"Le retour {label} est bloqué à une valeur fixe.",
                };
            }
            default:
                return "";
        }
    }

    // Repere d'un signal pour l'affichage : son tag schema (S11, KM1_AUX...) si defini, sinon son nom
    // brut. Defensif : composant/signal absent => on retombe sur le nom (jamais d'exception).
    private static string SignalLabel(Component comp, string? signalName)
    {
        if (signalName is null)
            return "signal";
        if (comp.Signals.TryGetValue(signalName, out var s) && s.Tag is not null)
            return s.Tag;
        return signalName;
    }

    // Une cellule = un Label a largeur minimale fixe (le plancher d'alignement). PAS de ClipText :
    // une cellule trop longue ELARGIT sa colonne (via AutoFitColumns) au lieu d'etre tronquee, et le
    // debordement du panneau se lit a la barre de defilement horizontale. MouseFilter Ignore pour
    // laisser le survol atteindre le conteneur de ligne (sinon le Label capterait l'event a sa place).
    private static Label MakeCell(string text, int width)
    {
        return new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }
}

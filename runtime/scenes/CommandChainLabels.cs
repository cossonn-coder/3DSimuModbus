// =============================================================================
// CommandChainLabels — etiquettes 3D « cmd -> physique -> ret » par element (S3.3)
// =============================================================================
//
// Role : rendre la CHAINE DE COMMANDE lisible d'un coup d'oeil, ancree sur chaque
// element 3D. Pour chaque composant (convoyeur, verins, capteurs) on cree UN Label3D
// billboard (oriente camera) qui affiche, sur une ligne compacte :
//
//     <repere>  <cmd nom %MWx.y=b>  ->  <etat physique>  ->  <ret nom %MWa.b=b>
//
// C'est le pendant VISUEL et OBSERVABLE de la brique 5 : la meme information que le
// M580 echange sur le bus, mais posee sur la maquette. Objet de GLUE Godot pure.
//
// --- Decodage PILOTE PAR LE PIVOT (invariant CLAUDE.md) --------------------------------
// Aucune adresse %MW n'est ecrite en dur ici. Chaque Signal est resolu une fois au Build
// depuis le pivot (repere `tag` + adresse absolue `AbsWord` + `Bit`), puis Update ne fait
// que decoder les mots d'un snapshot via Signal.ReadBit. Si l'automaticien recale une zone
// (base_mw), les etiquettes suivent sans toucher a ce fichier.
//
// --- Frontiere Arch A (thread-safety) --------------------------------------------------
// Ce helper ne touche JAMAIS le datastore ni le thread serveur : CarrouselScene lui passe,
// sur le THREAD PRINCIPAL Godot et a BASSE cadence, des COPIES (snapshots verrouilles) des
// zones cmd/ret + la simulation en lecture seule. On lit, on n'ecrit rien (aucune commande).
//
// --- Cadence & lisibilite (point dur de l'amorce) --------------------------------------
// Le texte est recompose a ~5-10 Hz (piloté par CarrouselScene), jamais a 60 Hz : un texte
// qui change 60 fois/s scintille. Les Label3D sont en mode billboard (toujours face camera)
// avec un contour sombre pour rester lisibles sur la geometrie claire, quel que soit l'angle.

using System.Globalization;
using CarrouselCore;
using Godot;

// Godot expose aussi un type `Signal` (signaux du moteur) : on leve l'ambiguite en fixant
// `Signal` = le signal Modbus resolu du pivot, seul sens utile dans ce fichier.
using Signal = CarrouselCore.Signal;

/// <summary>
/// Fabrique et met a jour un <see cref="Label3D"/> par element de la chaine de commande
/// (KM1, YV1, YV2, B1, B2). Le texte « cmd -> physique -> ret » est decode des snapshots
/// via les <see cref="Signal"/> du pivot ; les booleens de retour sont exposes pour la
/// coloration d'etat pilotee par <see cref="CarrouselScene"/>. Lecture seule, glue Godot.
/// </summary>
public sealed class CommandChainLabels
{
    // --- Reglages COSMETIQUES des etiquettes (mobilier d'ecran hors pivot, comme la camera) ---
    // Aucune de ces valeurs n'est une adresse Modbus ni un temps de simulation : ce sont des
    // tailles de rendu. PixelSize petit + FontSize modere = texte compact ; le contour noir le
    // detache de la maquette claire. Billboard : l'etiquette pivote toujours vers la camera.
    private const float LabelPixelSize = 0.0022f;
    private const int LabelFontSize = 48;
    private const int LabelOutline = 20;

    // Signaux resolus du pivot (adresses + masques figes au Build). Jamais d'adresse en dur.
    private Signal _cmdRun, _km1Aux;
    private Signal _cmdExt1, _s11, _s12;
    private Signal _cmdExt2, _s21, _s22;
    private Signal _b1Active, _b2Active;

    // Reperes (tags) des composants, lus du pivot pour l'affichage — jamais devines.
    private string _km1Tag = "", _yv1Tag = "", _yv2Tag = "", _b1Tag = "", _b2Tag = "";

    // Vitesse du convoyeur (deg/s) pour l'etat physique « marche N°/s » — vient du pivot.
    private double _speedDegPerS;

    // Les cinq etiquettes, creees au Build et rafraichies a chaque Update.
    private Label3D _km1 = null!, _yv1 = null!, _yv2 = null!, _b1 = null!, _b2 = null!;

    // --- Etat de retour decode, EXPOSE pour la coloration cote CarrouselScene ---------------
    // Mis a jour a chaque Update ; CarrouselScene s'en sert pour teinter l'anneau (KM1_AUX) et
    // allumer les fenetres capteurs (B1/B2). Evite de re-resoudre ces signaux ailleurs : le
    // decodage pivot vit a UN seul endroit (ici).
    public bool Km1Aux { get; private set; }
    public bool B1Present { get; private set; }
    public bool B2Present { get; private set; }

    /// <summary>
    /// Resout les signaux depuis le pivot et cree un <see cref="Label3D"/> ancre au-dessus de
    /// chaque element. Les ancres sont les noeuds 3D deja construits par CarrouselScene (anneau,
    /// verins, fenetres capteurs) : le Label3D devient leur enfant et suit donc l'element.
    /// </summary>
    /// <param name="pivot">Modele pivot resolu (source des reperes et adresses).</param>
    /// <param name="km1Anchor">Noeud du convoyeur (anneau) — ancre de l'etiquette KM1.</param>
    /// <param name="yv1Anchor">Noeud du verin 1 — ancre de l'etiquette YV1.</param>
    /// <param name="yv2Anchor">Noeud du verin 2 — ancre de l'etiquette YV2.</param>
    /// <param name="b1Anchor">Noeud de la fenetre capteur 1 — ancre de l'etiquette B1.</param>
    /// <param name="b2Anchor">Noeud de la fenetre capteur 2 — ancre de l'etiquette B2.</param>
    public void Build(PivotModel pivot, Node3D km1Anchor, Node3D yv1Anchor, Node3D yv2Anchor,
                      Node3D b1Anchor, Node3D b2Anchor)
    {
        // Convoyeur (KM1) : commande de marche + contact auxiliaire de retour.
        var km1 = pivot.GetComponent("conveyor");
        _km1Tag = km1.Tag;
        _cmdRun = pivot.GetSignal("conveyor", "cmd_run");
        _km1Aux = pivot.GetSignal("conveyor", "ret_running");
        _speedDegPerS = km1.GetParam("speed_deg_per_s");

        // Verins (YV1/YV2) : commande d'extension + deux fins de course (rentre/sorti).
        _yv1Tag = pivot.GetComponent("cylinder_1").Tag;
        _cmdExt1 = pivot.GetSignal("cylinder_1", "cmd_extend");
        _s11 = pivot.GetSignal("cylinder_1", "ret_retracted");
        _s12 = pivot.GetSignal("cylinder_1", "ret_extended");

        _yv2Tag = pivot.GetComponent("cylinder_2").Tag;
        _cmdExt2 = pivot.GetSignal("cylinder_2", "cmd_extend");
        _s21 = pivot.GetSignal("cylinder_2", "ret_retracted");
        _s22 = pivot.GetSignal("cylinder_2", "ret_extended");

        // Capteurs (B1/B2) : pas de commande, un seul bit de retour de presence.
        _b1Tag = pivot.GetComponent("presence_station_1").Tag;
        _b1Active = pivot.GetSignal("presence_station_1", "ret_active");
        _b2Tag = pivot.GetComponent("presence_station_2").Tag;
        _b2Active = pivot.GetSignal("presence_station_2", "ret_active");

        // Creation des etiquettes, ancrees a des hauteurs LOCALES etagees pour ne pas se
        // superposer (KM1 haut au centre ; verins et capteurs plus bas sur leur poste).
        _km1 = MakeLabel(km1Anchor, 1.05f);
        _yv1 = MakeLabel(yv1Anchor, 0.55f);
        _yv2 = MakeLabel(yv2Anchor, 0.55f);
        _b1 = MakeLabel(b1Anchor, 0.45f);
        _b2 = MakeLabel(b2Anchor, 0.45f);
    }

    /// <summary>
    /// Recompose le texte de chaque etiquette a partir des snapshots decodes (cmd/ret) et de
    /// l'etat physique lu sur la simulation. Appelee a BASSE cadence par CarrouselScene sur le
    /// thread principal. Met aussi a jour les booleens exposes pour la coloration.
    /// </summary>
    /// <param name="cmd">Copie de la zone cmd (mots relatifs a sa base), indexee par WordRel.</param>
    /// <param name="ret">Copie de la zone ret (mots relatifs a sa base), indexee par WordRel.</param>
    /// <param name="sim">Simulation en lecture seule (position des tiges, marche convoyeur).</param>
    public void Update(ushort[] cmd, ushort[] ret, CarrouselSimulation sim)
    {
        // --- Convoyeur KM1 : cmd_run -> marche -> KM1_AUX -------------------------------------
        bool run = _cmdRun.ReadBit(cmd[_cmdRun.WordRel]);
        Km1Aux = _km1Aux.ReadBit(ret[_km1Aux.WordRel]);
        string km1Phys = Km1Aux
            ? string.Format(CultureInfo.InvariantCulture, "marche {0:0.#}°/s", _speedDegPerS)
            : "arrêt";
        _km1.Text = $"{_km1Tag}  cmd_run {Addr(_cmdRun)}={Bit(run)}  →  {km1Phys}"
                    + $"  →  {_km1Aux.Tag} {Addr(_km1Aux)}={Bit(Km1Aux)}";

        // --- Verins YV1/YV2 : cmd_extend -> tige N% (phase) -> S11/S12 ------------------------
        _yv1.Text = CylinderLine(_yv1Tag, cmd, ret, sim.Cylinder1.Position, _cmdExt1, _s11, _s12);
        _yv2.Text = CylinderLine(_yv2Tag, cmd, ret, sim.Cylinder2.Position, _cmdExt2, _s21, _s22);

        // --- Capteurs B1/B2 : (pas de cmd) -> presence -> Bi ---------------------------------
        B1Present = _b1Active.ReadBit(ret[_b1Active.WordRel]);
        B2Present = _b2Active.ReadBit(ret[_b2Active.WordRel]);
        _b1.Text = SensorLine(_b1Tag, _b1Active, B1Present);
        _b2.Text = SensorLine(_b2Tag, _b2Active, B2Present);
    }

    // --- Composition des lignes de texte ---------------------------------------------------

    /// <summary>
    /// Ligne d'un verin : commande d'extension, etat physique de la tige (pourcentage + phase
    /// deduite de la position et de la commande), puis les deux fins de course. La phase (sort /
    /// rentre / sorti / rentré) rend le mouvement lisible sans afficher de vitesse instantanee.
    /// </summary>
    private string CylinderLine(string tag, ushort[] cmd, ushort[] ret, double position,
                                Signal cmdExtend, Signal retRetracted, Signal retExtended)
    {
        bool ext = cmdExtend.ReadBit(cmd[cmdExtend.WordRel]);
        bool s1 = retRetracted.ReadBit(ret[retRetracted.WordRel]);
        bool s2 = retExtended.ReadBit(ret[retExtended.WordRel]);
        int pct = (int)System.Math.Round(position * 100.0);

        // Phase : les butees priment (98 %/2 % = fins de course reelles) ; entre les deux, le
        // sens vient de la commande (electrovanne alimentee = la tige va vers la sortie).
        string phase = pct >= 98 ? "sorti"
                     : pct <= 2 ? "rentré"
                     : ext ? "sort" : "rentre";

        return $"{tag}  cmd_extend {Addr(cmdExtend)}={Bit(ext)}  →  tige {pct}% ({phase})"
             + $"  →  {retRetracted.Tag} {Addr(retRetracted)}={Bit(s1)}"
             + $"  {retExtended.Tag} {Addr(retExtended)}={Bit(s2)}";
    }

    /// <summary>
    /// Ligne d'un capteur de presence : pas de commande (— ), etat physique (palette presente
    /// ou poste libre) puis le bit de retour. Le signal ret_active n'a pas de tag propre dans le
    /// pivot : on affiche donc le repere du COMPOSANT (B1/B2) devant l'adresse.
    /// </summary>
    private static string SensorLine(string tag, Signal retActive, bool present)
    {
        string phys = present ? "palette présente" : "poste libre";
        return $"{tag}   —  →  {phys}  →  {tag} {Addr(retActive)}={Bit(present)}";
    }

    // --- Helpers de formatage --------------------------------------------------------------

    // Adresse %MW absolue d'un signal TOR, au format automaticien « %MW<mot>.<bit> ». L'adresse
    // vient de la resolution pivot (AbsWord = base_mw + word) : aucune valeur en dur.
    private static string Addr(Signal s) => $"%MW{s.AbsWord}.{s.Bit}";

    // Un bit rendu « 1 » / « 0 » (langage de l'automaticien, comme sur un pupitre).
    private static string Bit(bool b) => b ? "1" : "0";

    /// <summary>
    /// Cree un <see cref="Label3D"/> billboard lisible, enfant de <paramref name="anchor"/>, pose
    /// a la hauteur locale <paramref name="localY"/> au-dessus de l'element. Reglages purement
    /// cosmetiques (cf. constantes en tete) : contour sombre + orientation camera.
    /// </summary>
    private static Label3D MakeLabel(Node3D anchor, float localY)
    {
        var label = new Label3D
        {
            Name = "chain_label",
            Position = new Vector3(0, localY, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,   // toujours face camera
            PixelSize = LabelPixelSize,
            FontSize = LabelFontSize,
            OutlineSize = LabelOutline,
            Modulate = new Color(1f, 1f, 1f),
            OutlineModulate = new Color(0f, 0f, 0f),
            // NoDepthTest : l'etiquette reste lisible meme si un element passe devant (elle
            // « flotte » au-dessus de la maquette, comme une annotation d'IHM).
            NoDepthTest = true,
            Text = "…",
        };
        anchor.AddChild(label);
        return label;
    }
}

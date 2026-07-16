// =============================================================================
// CarrouselScene — generation procedurale de la maquette 3D depuis le pivot (brique 5)
// =============================================================================
//
// Role : a `_Ready`, LIRE le pivot (contrat central) et CONSTRUIRE toute la geometrie de la
// maquette en primitives Godot, sans aucun asset externe ni aucune coordonnee en dur. C'est le
// SEUL fichier du projet qui depend de Godot ET du core a la fois : il fait le pont entre le
// modele resolu (PivotModel) et le scene tree.
//
// --- Perimetre : geometrie statique (brique 5) + hote Modbus vivant (S2.1) ----------------
// La construction geometrique (BuildConveyor/BuildCylinder/BuildPallet/BuildSensorWindow) reste
// STATIQUE : les palettes sont posees a leurs positions INITIALES (kinematics.initial_positions_deg),
// la geometrie n'est pas encore pilotee par la simulation. Ce que S2.1  AJOUTE, de facon additive :
// la scene demarre a _Ready la chaine Modbus (datastore + simulation + serveur, comme SimHost) et la
// fait vivre dans _PhysicsProcess (heartbeat qui bat, retours qui repondent aux commandes du M580).
// Le REFLET VISUEL de cet etat simule (bouger les tiges et les palettes) est le sous-sprint S2.2 :
// c'est pour lui qu'on nomme deja chaque noeud d'apres son `id` pivot (les verins exposent un enfant
// « rod » adressable) — le binding de S2.2 les retrouvera sans retoucher les builders ci-dessous.
//
// --- Repere (fige a l'archi de la brique, cf. NOTES_sprint_01 §5) ---------------------------
// Sol = plan (X,Z) de Godot, Y = hauteur. Un angle theta du pivot (degres, 0 sur +X, CCW vu de
// dessus) se projette sur le cercle par :   x = cx + r*cos(theta) ,  z = cz - r*sin(theta).
// Le signe « moins » sur z rend le sens CCW correct vu du dessus (camera qui regarde -Y) avec X
// vers la droite et Z vers l'observateur. Postes : 90° -> +X=0,Z=-r (fond) ; 270° -> Z=+r (devant).
// Cette meme rotation (RotateY(theta)) aligne les palettes et les fenetres capteurs sur le rayon.
//
// --- Pourquoi du CSG pour l'anneau ---------------------------------------------------------
// Aucun mesh primitif Godot ne donne un anneau PLAT (couronne) : TorusMesh est un tube (donut) et
// ne respecte pas height_m. On soustrait donc un cylindre interieur d'un cylindre exterieur
// (CsgCombiner3D) : 2 primitives, une soustraction — simple, et fidele aux 3 cotes du pivot
// (inner_radius_m / outer_radius_m / height_m). Partout ailleurs, un mesh primitif suffit (D-b).

using System.Globalization;
using System.IO;
using System.Net;
using CarrouselCore;
using CarrouselServer;
using Godot;

/// <summary>
/// Scene racine de la maquette. <see cref="_Ready"/> charge le pivot et genere l'anneau du
/// convoyeur, les deux verins bloqueurs (statiques, rentres), les trois palettes et les volumes
/// semi-transparents des capteurs de presence. Tout vient du pivot ; rien n'est code en dur.
/// </summary>
public partial class CarrouselScene : Node3D
{
    // Hauteurs/rayons purement graphiques des verins (hors pivot : le pivot ne decrit que la course
    // et l'angle du poste). Un fut vertical qui abrite la tige ; la tige coulisse dedans.
    private const float BodyRadius = 0.05f;
    private const float BodyHeight = 0.20f;
    private const float RodRadius = 0.025f;
    private const float SensorHeight = 0.15f;   // hauteur du volume translucide d'un capteur

    // --- Hote Modbus vivant (S2.1) -------------------------------------------------------------
    // Meme chaine que SimHost (brique 4a), mais cadencee par _PhysicsProcess au lieu d'un
    // Stopwatch. Ces trois objets sont du CORE PUR (aucune dependance Godot) : le datastore est la
    // source de verite, la simulation le « cerveau », le serveur le pont Modbus TCP. On les cree a
    // _Ready depuis le pivot deja charge, puis on les fait vivre au rythme du moteur.
    private ModbusDataStore _store = null!;
    private CarrouselSimulation _sim = null!;
    private ModbusServer _server = null!;

    // --- Etat de sante du serveur (S3.2) -------------------------------------------------------
    // Vrai si _server.Start() a echoue (port 502 deja occupe). Dans ce cas on N'ENTRE PAS dans la
    // boucle Modbus (_PhysicsProcess sort tot) : inutile d'avancer la simulation puisque personne
    // ne lira les retours, et la maquette figee est desormais EXPLIQUEE par le bandeau rouge du
    // HealthHud (solde D-013). Le message porte le texte de la ModbusServerException pour le bandeau.
    private bool _serverFailed;
    private string _serverFailureMessage = "";

    // Accumulateur du pas fixe (voir _PhysicsProcess). En secondes.
    private double _accumulator;

    // Periode du tick physique en SECONDES, tiree du pivot (heartbeat.period_ms / 1000). Ce n'est
    // pas une vraie constante (elle depend du pivot), d'ou un champ readonly resolu a _Ready.
    private double _periodS;

    // Garde-fou anti-spirale de la mort : au plus MaxCatchup ticks de rattrapage par frame. Si le
    // moteur prend du retard (frame longue), on rattrape un peu puis on lache le surplus plutot que
    // de boucler indefiniment (ce qui figerait la frame ET le heartbeat).
    private const int MaxCatchup = 5;

    // Interface d'ecoute du serveur. Defaut IPAddress.Any (le M580 est un client DISTANT sur le
    // LAN) ; true => IPAddress.Loopback pour un test local isole sans exposer le port 502. Lu par
    // Godot AVANT _Ready (valeur figee dans la .tscn ou l'inspecteur), d'ou l'attribut [Export].
    [Export] public bool BindLoopback { get; set; } = false;

    // Sonde de diagnostic (S2.2) : quand true, chaque frame physique imprime l'etat anime (heartbeat,
    // altitude des tiges, angles des palettes). Sert UNIQUEMENT au smoke-test headless a observer le
    // mouvement sans oeil humain ; false par defaut (aucun bruit en usage normal). Lu par Godot avant
    // _Ready (valeur figee dans la .tscn), d'ou [Export].
    [Export] public bool DiagAnim { get; set; } = false;

    // --- Reflet visuel (S2.2) : references capturees au build ------------------------------------
    // On memorise a _Ready les noeuds que la simulation anime, pour n'appeler AUCUN GetNode par frame
    // (ApplyToScene tourne 60 fois/s). Les tiges bougent en +Y (course du verin), les palettes se
    // repositionnent sur le cercle. Chaque tige est routee par id pivot vers le meme indice que la sim
    // (Cylinder1/Cylinder2), car l'ordre d'iteration du dictionnaire de composants n'est pas garanti.
    private MeshInstance3D _rod1 = null!;
    private MeshInstance3D _rod2 = null!;
    private float _restY1, _restY2;     // altitude de la tige RENTREE (Position.Y initial, pos 0)
    private float _stroke1, _stroke2;   // course stroke_m : deplacement +Y a pleine extension (pos 1)

    // Palettes + parametres du cercle (radius/center/y), memorises pour rejouer OnCircle chaque frame
    // avec EXACTEMENT le repere de la brique 5 (coherence de repere garantie).
    private MeshInstance3D[] _pallets = null!;
    private float _palletRadius;
    private Vector3 _palletCenter;
    private float _palletY;

    // --- Chaine de commande par element (S3.3) : etiquettes 3D + coloration d'etat ------------
    // Etiquettes « cmd -> physique -> ret » ancrees sur chaque element (helper de glue pure).
    // Nourries a BASSE cadence (~6 Hz) par _Process, jamais a 60 Hz : le texte ne doit pas
    // scintiller. Le decodage pivot (adresses %MW) vit entierement dans ce helper — pas ici.
    private CommandChainLabels _labels = null!;
    private double _labelAccumulator;
    private const double LabelRefreshPeriodS = 0.15;   // ~6-7 Hz : lisible, sans scintillement

    // Ancres (noeuds 3D) au-dessus desquels poser les etiquettes, capturees au build pour ne
    // faire AUCUN GetNode par rafraichissement. Anneau du convoyeur, verins, fenetres capteurs.
    private Node3D _ringNode = null!;
    private Node3D _cyl1Node = null!;
    private Node3D _cyl2Node = null!;
    private MeshInstance3D _sensor1Node = null!;
    private MeshInstance3D _sensor2Node = null!;

    // Materiaux a colorer selon l'etat (references reutilisees, on ne modifie que leur AlbedoColor,
    // cf. amorce S3.3). Tige teintee par degrade selon la course ; anneau teinte quand KM1_AUX=1 ;
    // fenetre capteur allumee quand Bi=1. Capturees au build (l'anneau/les fenetres ne l'etaient pas).
    private StandardMaterial3D _rodMat1 = null!;
    private StandardMaterial3D _rodMat2 = null!;
    private StandardMaterial3D _ringMat = null!;
    private StandardMaterial3D _sensorMat1 = null!;
    private StandardMaterial3D _sensorMat2 = null!;

    // Couleurs repos / actif (mobilier de rendu hors pivot, comme la camera). Repos = couleurs
    // deja posees par les builders ; actif = variante saturee qui « allume » l'element sous etat.
    private static readonly Color RodRest = new(0.78f, 0.78f, 0.82f);
    private static readonly Color RodActive = new(0.95f, 0.75f, 0.20f);   // ambre : tige qui sort
    private static readonly Color RingRest = new(0.55f, 0.55f, 0.58f);
    private static readonly Color RingActive = new(0.30f, 0.72f, 0.38f);  // vert : convoyeur en marche
    private static readonly Color SensorRest = new(0.20f, 0.60f, 1.00f, 0.35f);
    private static readonly Color SensorActive = new(0.30f, 1.00f, 0.45f, 0.70f);   // vert allume : palette presente

    public override void _Ready()
    {
        // Chargement defensif : PivotModel.Load leve PivotException (message clair) si le contrat
        // est absent ou incoherent. On laisse l'exception remonter — Godot la journalise ; mieux
        // vaut une scene qui refuse de se construire qu'une maquette a la geometrie fausse.
        var pivot = PivotModel.Load(FindPivot());
        var k = pivot.Kinematics;
        var center = new Vector3((float)k.Center[0], (float)k.Center[1], (float)k.Center[2]);

        // 1. Anneau du convoyeur (unique composant de type conveyor_circular). Renvoie l'altitude
        //    de sa face SUPERIEURE : toutes les autres pieces se posent dessus.
        var conveyor = FindSingleByType(pivot, "conveyor_circular");
        float innerR = (float)conveyor.GetRender("inner_radius_m");
        float outerR = (float)conveyor.GetRender("outer_radius_m");
        float ringTopY = BuildConveyor(conveyor, center, innerR, outerR);

        // 2. Verins bloqueurs (autant que le pivot en declare). Corps + tige, tige RENTREE.
        int cylinders = 0;
        foreach (var c in FindByType(pivot, "cylinder_blocker"))
        {
            BuildCylinder(c, center, (float)k.RadiusM, ringTopY);
            cylinders++;
        }

        // 3. Palettes : boites posees sur l'anneau a leurs positions initiales (animees en S2.2).
        var size = new Vector3((float)k.PalletSizeM[0], (float)k.PalletSizeM[1], (float)k.PalletSizeM[2]);
        // On memorise ici les invariants du cercle (radius/center/altitude) une seule fois : ApplyToScene
        // (S2.2) rejouera OnCircle chaque frame avec ces memes valeurs -> repere identique a la brique 5.
        _pallets = new MeshInstance3D[k.InitialPositionsDeg.Length];
        _palletRadius = (float)k.RadiusM;
        _palletCenter = center;
        _palletY = ringTopY + size.Y / 2f;   // meme altitude que BuildPallet (boite posee sur l'anneau)
        for (int i = 0; i < k.InitialPositionsDeg.Length; i++)
            BuildPallet(i, k.InitialPositionsDeg[i], (float)k.RadiusM, center, ringTopY, size);

        // 4. Zones capteurs B1/B2 : volumes semi-transparents materialisant window_deg autour du poste.
        int sensors = 0;
        float ringWidth = outerR - innerR;
        foreach (var s in FindByType(pivot, "sensor_presence"))
        {
            BuildSensorWindow(s, center, (float)k.RadiusM, ringWidth, ringTopY);
            sensors++;
        }

        // Recensement deterministe (une ligne) : sert de reference au smoke-test headless.
        GD.Print($"[carrousel] ring=1 cylinders={cylinders} pallets={k.InitialPositionsDeg.Length} sensors={sensors}");

        // Camera + lumiere pour l'inspection : mobilier de scene HORS contrat pivot (Q4 du brief).
        AddPresentation(center, (float)k.RadiusM);

        // --- Demarrage de l'hote Modbus (S2.1) -------------------------------------------------
        // On reutilise le MEME `pivot` deja charge (une seule lecture du contrat). Ordre calque sur
        // SimHost : datastore -> simulation -> serveur -> Start(). Le serveur ecoute sur son propre
        // thread ; c'est _PhysicsProcess (thread principal Godot) qui rythmera Pull/Tick/Push.
        _store = new ModbusDataStore(pivot);
        _sim = new CarrouselSimulation(pivot);
        _periodS = pivot.HeartbeatPeriodMs / 1000.0;

        IPAddress bind = BindLoopback ? IPAddress.Loopback : IPAddress.Any;
        _server = new ModbusServer(pivot, _store, bind);

        // Demarrage ENVELOPPE (S3.2) : un echec de bind (port occupe) ne doit plus planter la scene
        // en silence (dette D-013). On catche specifiquement ModbusServerException, on memorise
        // l'etat + le message, et on journalise. La scene reste construite (geometrie visible) et le
        // bandeau rouge du HealthHud dira pourquoi elle ne bouge pas. Aucun autre type d'exception
        // n'est avale : une PivotException ou une erreur systeme doit continuer de remonter.
        try
        {
            _server.Start();
        }
        catch (ModbusServerException ex)
        {
            _serverFailed = true;
            _serverFailureMessage = ex.Message;
            GD.PrintErr($"[carrousel] serveur Modbus KO : {ex.Message}");
        }

        // --- Overlay sante (S3.2) : HealthHud cree par code, meme patron qu'AddPresentation --------
        // Lecture seule : il recoit des references (serveur + simulation) et se rafraichit tout seul
        // a basse cadence sur le thread principal Godot. AddChild declenche son _Ready de facon
        // synchrone -> ses noeuds existent quand on appelle Configure/ShowBindFailure juste apres.
        var hud = new HealthHud();
        AddChild(hud);
        hud.Configure(_server, _sim, pivot.Port);
        if (_serverFailed)
            hud.ShowBindFailure(_serverFailureMessage);

        // --- Chaine de commande par element (S3.3) : etiquettes 3D « cmd -> physique -> ret » ----
        // Construites APRES la geometrie (les ancres/materiaux sont capturees) et apres le pivot.
        // Lecture seule : le helper resout les adresses %MW du pivot et pose un Label3D billboard
        // sur chaque element ; _Process les nourrit ensuite a basse cadence (avec la coloration).
        _labels = new CommandChainLabels();
        _labels.Build(pivot, _ringNode, _cyl1Node, _cyl2Node, _sensor1Node, _sensor2Node);
    }

    /// <summary>
    /// Coeur temps reel de la scene-hote : a chaque frame physique, on avance la simulation par pas
    /// FIXE de <see cref="_periodS"/> (deterministe, heartbeat a cadence exacte), en decouplant le
    /// rythme du moteur (delta variable) de celui de la cinematique. L'accumulateur encaisse l'ecart.
    ///
    /// Deux garde-fous contre la « spirale de la mort » (une frame longue qui en genere d'autres) :
    ///   1. clamp de l'accumulateur a MaxCatchup * periode : on ne memorise jamais plus de retard
    ///      qu'on ne peut en rattraper ;
    ///   2. guard `ticks &lt; MaxCatchup` : au plus MaxCatchup pas par frame, le surplus est abandonne.
    /// Sans eux, un pic de charge pourrait figer le thread principal (et donc le heartbeat vu du M580).
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        // Garde S3.2 : si le serveur n'a pas pu ecouter (bind echoue), la chaine Modbus est morte.
        // On ne fait pas StepSim (rien a publier, aucun client) ; la maquette reste figee, ce qui est
        // desormais EXPLIQUE par le bandeau rouge du HealthHud (qui, lui, tourne sur son propre _Process).
        if (_serverFailed)
            return;

        _accumulator = System.Math.Min(_accumulator + delta, MaxCatchup * _periodS);

        int ticks = 0;
        while (_accumulator >= _periodS && ticks < MaxCatchup)
        {
            StepSim(_periodS);
            _accumulator -= _periodS;
            ticks++;
        }

        // Reflet visuel de l'etat simule (tiges/palettes) : stub en S2.1, corps livre en S2.2.
        ApplyToScene();
    }

    /// <summary>
    /// Rafraichissement VISUEL a BASSE cadence (~6 Hz) de la chaine de commande par element (S3.3) :
    /// texte des etiquettes + coloration d'etat. Distinct de _PhysicsProcess (60 Hz, deterministe) :
    /// le texte et les couleurs n'ont pas besoin du 60 Hz et scintilleraient a ce rythme. Lecture
    /// seule stricte (frontiere Arch A) : on ne lit que des COPIES verrouillees (SnapshotCommands /
    /// SnapshotReturns) et l'etat deja publie par la simulation ; aucune ecriture cmd, aucun acces
    /// au datastore interne. On rafraichit meme sur echec de bind : les etiquettes montrent alors un
    /// etat au repos (cmd=0), coherent avec le bandeau rouge du HealthHud (la maquette est figee).
    /// </summary>
    public override void _Process(double delta)
    {
        _labelAccumulator += delta;
        if (_labelAccumulator < LabelRefreshPeriodS)
            return;
        _labelAccumulator = 0;

        // Snapshots atomiques des deux zones (thread principal) : la meme photo alimente le texte
        // ET la coloration, pour un affichage coherent d'un rafraichissement a l'autre.
        ushort[] cmd = _store.SnapshotCommands();
        ushort[] ret = _store.SnapshotReturns();
        _labels.Update(cmd, ret, _sim);

        // Coloration d'etat (reutilisation des materiaux deja poses, on ne touche que AlbedoColor) :
        //   - tige : degrade repos->actif selon la course reelle (0..1) du verin ;
        //   - anneau : teinte « en marche » quand le contact KM1_AUX est ferme ;
        //   - fenetre capteur : « allumee » quand son bit de presence Bi vaut 1.
        // Les booleens KM1_AUX / B1 / B2 sont deja decodes par _labels (decodage pivot centralise).
        _rodMat1.AlbedoColor = RodRest.Lerp(RodActive, (float)_sim.Cylinder1.Position);
        _rodMat2.AlbedoColor = RodRest.Lerp(RodActive, (float)_sim.Cylinder2.Position);
        _ringMat.AlbedoColor = _labels.Km1Aux ? RingActive : RingRest;
        _sensorMat1.AlbedoColor = _labels.B1Present ? SensorActive : SensorRest;
        _sensorMat2.AlbedoColor = _labels.B2Present ? SensorActive : SensorRest;
    }

    /// <summary>
    /// Un pas de simulation, exactement l'enchainement de SimHost : on aspire les commandes du fil
    /// (Pull, sous server.Lock), on avance la cinematique de <paramref name="dt"/>, puis on publie
    /// les retours sur le fil (Push, sous server.Lock). Frontiere Arch A : ce code tourne sur le
    /// thread principal Godot, seul autorise a toucher datastore ET scene tree ; le thread propre du
    /// serveur ne fait que servir le TCP. Volontairement NON factorise avec SimHost (2 sites, dette B).
    /// </summary>
    private void StepSim(double dt)
    {
        _server.PullCommands();
        _sim.Tick(_store, dt);
        _server.PushReturns();
    }

    /// <summary>
    /// Applique l'etat de la simulation a la geometrie 3D (position des tiges de verin, des palettes).
    /// Mapping LECTURE SEULE : on ne lit que l'etat DEJA publie par <see cref="_sim"/> (thread principal,
    /// frontiere Arch A), jamais le datastore. Snap 10 Hz : recopie directe de l'etat courant, sans
    /// interpolation (decision D-d ; l'interpolation prev/current est ajoutable en V2 sans toucher la sim).
    /// </summary>
    private void ApplyToScene()
    {
        // --- Tiges : translation sur +Y proportionnelle a la position 0..1 du verin ---------------
        // Position=0 -> tige RENTREE (restY, ressort au repos) ; Position=1 -> tige SORTIE (restY+stroke).
        // On ne touche QUE l'altitude : x/z de la tige (locale, au centre du fut) restent inchanges.
        _rod1.Position = new Vector3(_rod1.Position.X, _restY1 + (float)_sim.Cylinder1.Position * _stroke1, _rod1.Position.Z);
        _rod2.Position = new Vector3(_rod2.Position.X, _restY2 + (float)_sim.Cylinder2.Position * _stroke2, _rod2.Position.Z);

        // --- Palettes : reposition sur le cercle via le MEME helper que la brique 5 (repere garanti) ---
        // AnglesDeg est dans l'ordre des indices (meme indice qu'a la construction). RotationDegrees
        // aligne cosmetiquement la face de la boite sur le rayon (RotateY(theta), comme au build).
        var angles = _sim.Pallets.AnglesDeg;
        for (int i = 0; i < _pallets.Length; i++)
        {
            double angle = angles[i];
            _pallets[i].Position = OnCircle(angle, _palletRadius, _palletCenter, _palletY);
            _pallets[i].RotationDegrees = new Vector3(0, (float)angle, 0);
        }

        // Sonde de diagnostic (headless) : une ligne par frame, cadre invariant (jamais de virgule
        // decimale francaise) pour que le smoke-test parse Y et angles de façon fiable. Muette par defaut.
        if (DiagAnim)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(CultureInfo.InvariantCulture, "[anim] hb={0} rod1Y={1:F5} rod2Y={2:F5} pallets=",
                _sim.Heartbeat, _rod1.Position.Y, _rod2.Position.Y);
            for (int i = 0; i < angles.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F3}", angles[i]);
            }
            GD.Print(sb.ToString());
        }
    }

    /// <summary>
    /// Liberation propre a la sortie de l'arbre : on dispose le serveur pour rendre le port 502.
    /// Symetrique du Start() de _Ready ; sans ca, un relancement echouerait (port encore occupe).
    /// </summary>
    public override void _ExitTree()
    {
        // Sur echec de bind (S3.2), le serveur FluentModbus n'a jamais demarre (le pre-vol a leve
        // avant Start) : le pre-vol TcpListener a deja ete relache dans son finally, il n'y a donc
        // aucun port a rendre, et tenter d'arreter un serveur non demarre serait inutile voire fragile.
        // On ne dispose donc que le serveur reellement lance.
        if (!_serverFailed)
            _server.Dispose();
    }

    /// <summary>
    /// Ajoute une camera ORBITALE libre (style CAO, S4.1) et une lumiere directionnelle pour « voir »
    /// la maquette. Ces noeuds ne sont PAS pilotes par le pivot : seul le CADRAGE INITIAL de la camera
    /// se deduit du pivot (center/radius passes a FrameFrom), ensuite elle est libre. La camera vit
    /// entierement dans <see cref="OrbitCamera"/> (input souris/clavier + F11). Non comptes dans le
    /// recensement du smoke-test. La lumiere, elle, reste inchangee.
    /// </summary>
    private void AddPresentation(Vector3 center, float radius)
    {
        // Camera orbitale : le gimbal se cadre depuis le pivot (point d'interet = centre, distance
        // deduite du rayon), puis l'utilisateur navigue librement. AddChild declenche son _Ready
        // (creation de la Camera3D enfant) de facon synchrone -> FrameFrom peut agir juste apres.
        var cam = new OrbitCamera { Name = "OrbitCamera" };
        AddChild(cam);
        cam.FrameFrom(center, radius);

        var light = new DirectionalLight3D { Name = "SunLight" };
        AddChild(light);
        // Lumiere en plongee diagonale (cible decalee du centre pour donner un angle d'eclairage).
        light.LookAtFromPosition(center + new Vector3(0, 5, 0), center + new Vector3(1, 0, 1), Vector3.Up);
    }

    // --- Construction des pieces ---------------------------------------------------------------

    /// <summary>
    /// Anneau plat (couronne) construit par soustraction CSG. Centre a <paramref name="center"/> ;
    /// retourne l'altitude de sa face superieure (center.Y + height/2), point d'appui des palettes.
    /// </summary>
    private float BuildConveyor(Component conveyor, Vector3 center, float innerR, float outerR)
    {
        float height = (float)conveyor.GetRender("height_m");

        var ring = new CsgCombiner3D { Name = conveyor.Id, Position = center };

        // Cylindre exterieur (plein), puis cylindre interieur soustrait pour percer le trou. Le
        // cylindre soustrait est un peu plus haut pour trancher net (pas de face residuelle).
        var outer = new CsgCylinder3D
        {
            Radius = outerR,
            Height = height,
            Sides = 64,
            Material = SolidMat(RingRest),
            Operation = CsgShape3D.OperationEnum.Union,
        };
        var inner = new CsgCylinder3D
        {
            Radius = innerR,
            Height = height + 0.02f,
            Sides = 64,
            Operation = CsgShape3D.OperationEnum.Subtraction,
        };
        ring.AddChild(outer);
        ring.AddChild(inner);
        AddChild(ring);

        // S3.3 : memoriser l'anneau (ancre de l'etiquette KM1) et son materiau (a teinter quand
        // le contact KM1_AUX est ferme). On reutilise le materiau deja pose, on ne recree rien.
        _ringNode = ring;
        _ringMat = (StandardMaterial3D)outer.Material;

        return center.Y + height / 2f;
    }

    /// <summary>
    /// Verin bloqueur : un noeud parent (nomme d'apres l'id pivot) place au poste sur le cercle,
    /// avec deux enfants — <c>body</c> (le fut vertical) et <c>rod</c> (la tige, position RENTREE).
    /// L'extension (sprint 3) consistera a translater <c>rod</c> de +stroke_m sur l'axe Y.
    /// </summary>
    private void BuildCylinder(Component cyl, Vector3 center, float radius, float ringTopY)
    {
        double angle = cyl.GetParam("station_angle_deg");
        float stroke = (float)cyl.GetParam("stroke_m");

        var node = new Node3D { Name = cyl.Id, Position = OnCircle(angle, radius, center, ringTopY) };

        var body = new MeshInstance3D
        {
            Name = "body",
            Mesh = new CylinderMesh { TopRadius = BodyRadius, BottomRadius = BodyRadius, Height = BodyHeight },
            Position = new Vector3(0, BodyHeight / 2f, 0),   // pose sur l'anneau
            MaterialOverride = SolidMat(new Color(0.30f, 0.30f, 0.33f)),
        };

        // Tige RENTREE : sommet affleurant le haut du fut, donc entierement contenue dans le corps.
        // Centre = BodyHeight - stroke/2 (le sommet est a BodyHeight). Extension future : Position.Y += stroke.
        var rod = new MeshInstance3D
        {
            Name = "rod",
            Mesh = new CylinderMesh { TopRadius = RodRadius, BottomRadius = RodRadius, Height = stroke },
            Position = new Vector3(0, BodyHeight - stroke / 2f, 0),
            MaterialOverride = SolidMat(new Color(0.78f, 0.78f, 0.82f)),
        };

        node.AddChild(body);
        node.AddChild(rod);
        AddChild(node);

        // S2.2 : memoriser la tige et sa course pour l'animer chaque frame (zero GetNode a l'execution).
        // Routage explicite par id pivot : on garantit que _rod1/_rod2 correspondent bien a
        // Cylinder1/Cylinder2 de la simulation, independamment de l'ordre d'iteration du dictionnaire.
        // S3.3 (additif) : on capture aussi le noeud du verin (ancre de l'etiquette) et le materiau
        // de la tige (a teinter par degrade selon la course) — memes indices, meme routage par id.
        if (cyl.Id == "cylinder_1")
        {
            _rod1 = rod; _restY1 = rod.Position.Y; _stroke1 = stroke;
            _cyl1Node = node; _rodMat1 = (StandardMaterial3D)rod.MaterialOverride;
        }
        else if (cyl.Id == "cylinder_2")
        {
            _rod2 = rod; _restY2 = rod.Position.Y; _stroke2 = stroke;
            _cyl2Node = node; _rodMat2 = (StandardMaterial3D)rod.MaterialOverride;
        }
    }

    /// <summary>Palette : boite posee sur l'anneau, orientee sur le rayon (alignement cosmetique).</summary>
    private void BuildPallet(int index, double angleDeg, float radius, Vector3 center, float ringTopY, Vector3 size)
    {
        float y = ringTopY + size.Y / 2f;   // la boite repose sur l'anneau
        var pallet = new MeshInstance3D
        {
            Name = $"pallet_{index}",
            Mesh = new BoxMesh { Size = size },
            Position = OnCircle(angleDeg, radius, center, y),
            RotationDegrees = new Vector3(0, (float)angleDeg, 0),   // face alignee au rayon (RotateY(theta))
            MaterialOverride = SolidMat(new Color(0.80f, 0.55f, 0.25f)),
        };
        AddChild(pallet);
        _pallets[index] = pallet;   // S2.2 : ref capturee pour animer la palette sans GetNode par frame
    }

    /// <summary>
    /// Volume semi-transparent materialisant la fenetre <c>window_deg</c> d'un capteur autour de son
    /// poste : une boite fine dont la largeur tangentielle vaut la corde de l'arc, la profondeur la
    /// largeur de l'anneau. Purement indicatif (le capteur reel est le bit B1/B2 cote datastore).
    /// </summary>
    private void BuildSensorWindow(Component sensor, Vector3 center, float radius, float ringWidth, float ringTopY)
    {
        double angle = sensor.GetParam("station_angle_deg");
        double windowDeg = sensor.GetParam("window_deg");

        // Corde de l'arc de `windowDeg` au rayon du cercle : largeur tangentielle du volume.
        float chord = 2f * radius * (float)System.Math.Sin(Mathf.DegToRad(windowDeg / 2.0));
        float y = ringTopY + SensorHeight / 2f;

        var window = new MeshInstance3D
        {
            Name = sensor.Id,
            // X = profondeur radiale (largeur anneau), Y = hauteur, Z = largeur tangentielle (corde).
            // RotateY(theta) envoie le X local sur le rayon et le Z local sur la tangente (cf. en-tete).
            Mesh = new BoxMesh { Size = new Vector3(ringWidth, SensorHeight, chord) },
            Position = OnCircle(angle, radius, center, y),
            RotationDegrees = new Vector3(0, (float)angle, 0),
            MaterialOverride = GlassMat(SensorRest),
        };
        AddChild(window);

        // S3.3 : capturer le noeud (ancre de l'etiquette Bi) et son materiau (a « allumer » quand
        // le bit de presence est a 1). Routage par id pivot, comme pour les verins/tiges.
        if (sensor.Id == "presence_station_1")
        {
            _sensor1Node = window; _sensorMat1 = (StandardMaterial3D)window.MaterialOverride;
        }
        else if (sensor.Id == "presence_station_2")
        {
            _sensor2Node = window; _sensorMat2 = (StandardMaterial3D)window.MaterialOverride;
        }
    }

    // --- Helpers -------------------------------------------------------------------------------

    // Projette un angle (deg) sur le cercle de rayon r centre en `center`, a l'altitude y.
    // x = cx + r*cos(theta) ; z = cz - r*sin(theta) (voir l'en-tete : convention CCW vu de dessus).
    private static Vector3 OnCircle(double angleDeg, double radius, Vector3 center, float y)
    {
        double rad = Mathf.DegToRad(angleDeg);
        float x = center.X + (float)(radius * System.Math.Cos(rad));
        float z = center.Z - (float)(radius * System.Math.Sin(rad));
        return new Vector3(x, y, z);
    }

    private static System.Collections.Generic.IEnumerable<Component> FindByType(PivotModel pivot, string type)
    {
        foreach (var c in pivot.Components.Values)
            if (c.Type == type)
                yield return c;
    }

    private static Component FindSingleByType(PivotModel pivot, string type)
    {
        Component? found = null;
        foreach (var c in pivot.Components.Values)
        {
            if (c.Type != type) continue;
            if (found is not null)
                throw new PivotException($"un seul composant de type '{type}' attendu (brique 5)");
            found = c;
        }
        return found ?? throw new PivotException($"aucun composant de type '{type}' dans le pivot");
    }

    private static StandardMaterial3D SolidMat(Color c) => new() { AlbedoColor = c };

    private static StandardMaterial3D GlassMat(Color c) => new()
    {
        AlbedoColor = c,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    // Localise pivot/machine_carrousel.json en remontant depuis le dossier du projet Godot
    // (res:// = runtime/). Robuste en editeur comme en --headless, quel que soit l'emplacement des
    // binaires .NET. Non prevu pour un export packe (la maquette de demo tourne dans l'arbo source).
    private static string FindPivot()
    {
        var dir = new DirectoryInfo(ProjectSettings.GlobalizePath("res://"));
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "pivot", "machine_carrousel.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            "pivot/machine_carrousel.json introuvable depuis " + ProjectSettings.GlobalizePath("res://"));
    }
}

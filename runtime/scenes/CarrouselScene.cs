// =============================================================================
// CarrouselScene — generation procedurale de la maquette 3D depuis le pivot (brique 5)
// =============================================================================
//
// Role : a `_Ready`, LIRE le pivot (contrat central) et CONSTRUIRE toute la geometrie de la
// maquette en primitives Godot, sans aucun asset externe ni aucune coordonnee en dur. C'est le
// SEUL fichier du projet qui depend de Godot ET du core a la fois : il fait le pont entre le
// modele resolu (PivotModel) et le scene tree.
//
// --- Perimetre brique 5 : STATIQUE ---------------------------------------------------------
// On affiche la machine a sa place ; on ne l'anime PAS. Aucune liaison au ModbusDataStore ni a
// CarrouselSimulation, aucun _PhysicsProcess. Les palettes sont posees a leurs positions INITIALES
// (kinematics.initial_positions_deg), pas au fil d'une simulation. La cinematique VISUELLE (lier la
// 3D a la sim) est le sprint 3 : c'est pour elle qu'on nomme deja chaque noeud d'apres son `id`
// pivot (les verins exposent un enfant « rod » adressable) — le binding futur les retrouvera sans
// retoucher cette scene.
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

using System.IO;
using CarrouselCore;
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

        // 3. Palettes : boites posees sur l'anneau a leurs positions initiales (statique ce sprint).
        var size = new Vector3((float)k.PalletSizeM[0], (float)k.PalletSizeM[1], (float)k.PalletSizeM[2]);
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
    }

    /// <summary>
    /// Ajoute une camera et une lumiere directionnelle pour pouvoir « voir » la maquette. Ces noeuds
    /// ne sont PAS pilotes par le pivot ; on les cree par code car <c>LookAt</c> garantit un cadrage
    /// correct sans transformation ecrite a la main. Non comptes dans le recensement du smoke-test.
    /// </summary>
    private void AddPresentation(Vector3 center, float radius)
    {
        var cam = new Camera3D { Name = "InspectionCamera", Current = true };
        AddChild(cam);
        float back = radius * 2.2f + 1f;
        cam.LookAtFromPosition(center + new Vector3(0, radius * 2f + 1f, back), center, Vector3.Up);

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
            Material = SolidMat(new Color(0.55f, 0.55f, 0.58f)),
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
            MaterialOverride = GlassMat(new Color(0.20f, 0.60f, 1.0f, 0.35f)),
        };
        AddChild(window);
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

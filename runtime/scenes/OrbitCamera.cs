// =============================================================================
// OrbitCamera — camera orbitale style visionneuse CAO (S4.1)
// =============================================================================
//
// Role : remplacer la camera FIGEE de la maquette (LookAtFromPosition immobile) par une
// camera LIBRE que l'utilisateur pilote a la souris, comme dans un viewer CAO. Generique :
// aucune coordonnee carrousel en dur. Le SEUL lien au pivot est le cadrage initial recu via
// FrameFrom(center, radius) ; ensuite la camera vit sa vie. Ce mecanisme vaut donc pour N
// machines futures (cf. CLAUDE.md §0bis), pas seulement pour le carrousel.
//
// --- Modele GIMBAL (pourquoi ce montage) --------------------------------------------------
// Ce Node3D est place AU POINT D'INTERET (le centre de la machine). Sa rotation porte le
// yaw (autour de Y) et le pitch (autour de X). La Camera3D est un ENFANT recule de `distance`
// sur l'axe local +Z : une camera Godot regarde son -Z, donc posee en (0,0,distance) elle
// fixe l'origine du gimbal (le point d'interet). Consequence directe :
//   - Orbite = tourner le gimbal (yaw/pitch)         -> la camera decrit une sphere autour du point.
//   - Pan    = translater le gimbal dans le plan camera -> le point d'interet glisse, l'orbite suit.
//   - Zoom   = changer `distance`                     -> la camera avance/recule sur son axe.
// Ce montage evite toute trigonometrie manuelle : Godot compose les transforms parent->enfant.
//
// --- Ordre d'Euler (piege) ----------------------------------------------------------------
// Godot compose les angles d'un Vector3 rotation en ordre YXZ : Basis = R_y(yaw)*R_x(pitch)*R_z.
// Applique a la camera enfant en (0,0,distance), un pitch NEGATIF leve la camera au-dessus du
// plan et la fait PLONGER sur la maquette. D'ou la borne pitch dans [-89°, -5°] : la camera
// pique toujours vers le bas, jamais sous le sol (elle ne passe pas la ligne d'horizon).
//
// --- Frontiere Arch A / perimetre ---------------------------------------------------------
// Glue Godot pure : ce noeud ne touche NI le datastore, NI la simulation, NI le heartbeat.
// Pas de moteur physique ni de collision : c'est un transform scripte deterministe. L'entree
// est lue dans _UnhandledInput (et non _Input) pour qu'un Control d'UI (panneau lateral livre
// en S4.2, MouseFilter=Stop) puisse CONSOMMER l'event avant nous : la camera n'orbitera pas
// quand le curseur agit sur l'UI. A ce sous-sprint l'UI n'existe pas encore ; la non-
// interference sera revalidee en S4.2.

using Godot;

/// <summary>
/// Camera orbitale (montage gimbal) pilotee a la souris facon CAO : orbite au bouton du
/// milieu, pan a Shift+milieu, zoom a la molette (vitesse proportionnelle a la distance).
/// Cadrage initial deduit du pivot via <see cref="FrameFrom"/> ; ensuite libre. Cree par code
/// depuis <see cref="CarrouselScene"/>. Touche F11 : bascule fenetre maximisee / plein ecran.
/// </summary>
public partial class OrbitCamera : Node3D
{
    // --- Reglages cosmetiques (mobilier de rendu HORS pivot, comme l'ancienne camera) --------
    // Sensibilites en unites "par pixel de deplacement souris". Valeurs de confort, ajustables
    // a l'oeil ; elles ne decrivent aucune realite physique de la machine.
    private const float OrbitSensitivity = 0.4f;    // degres de rotation par pixel (orbite)
    private const float PanSensitivity = 0.0015f;   // fraction de `distance` par pixel (pan)
    private const float ZoomStep = 0.12f;           // fraction de `distance` gagnee/perdue par cran de molette

    // Bornes du pitch : la camera plonge toujours vers la maquette (valeurs negatives), jamais
    // a plat ni sous le sol. -89° = quasi au-dessus, -5° = rasant sans franchir l'horizon.
    private const float PitchMinDeg = -89f;
    private const float PitchMaxDeg = -5f;

    // --- Etat courant du gimbal ---------------------------------------------------------------
    private float _yawDeg;      // rotation autour de Y (azimut) — libre, rollover naturel
    private float _pitchDeg;    // rotation autour de X (elevation) — clampee [PitchMin, PitchMax]
    private float _distance;    // recul de la camera enfant sur +Z — clampe [_distanceMin, _distanceMax]

    // Bornes de distance deduites du rayon de la machine au cadrage (cf. FrameFrom). Le zoom
    // reduit la distance mais ne FRANCHIT jamais le point d'interet (clamp >= _distanceMin) :
    // pas de traversee de la maquette. _distanceMax evite de se perdre a l'infini.
    private float _distanceMin;
    private float _distanceMax;

    // Vrai tant que le bouton du milieu est maintenu : on n'orbite/pan que pendant ce drag.
    private bool _dragging;

    // La camera enfant, recreee/positionnee a chaque changement de distance.
    private Camera3D _camera = null!;

    public override void _Ready()
    {
        // Camera enfant posee sur +Z local : une camera Godot regarde son -Z, donc depuis
        // (0,0,distance) elle fixe l'origine du gimbal (le point d'interet). Current=true la
        // rend active (elle remplace l'ancienne camera fixe supprimee dans AddPresentation).
        _camera = new Camera3D { Name = "OrbitCamera3D", Current = true };
        AddChild(_camera);
        ApplyTransform();
    }

    /// <summary>
    /// Cadrage initial deduit du pivot : place le point d'interet au centre de la machine et
    /// choisit une distance/pitch de depart confortables a partir du rayon. C'est l'UNIQUE
    /// dependance de la camera au pivot ; apres cet appel elle est entierement libre. Les
    /// facteurs (2.5, 0.3, 8) sont cosmetiques (mobilier hors pivot), pas des grandeurs machine.
    /// </summary>
    public void FrameFrom(Vector3 center, float radius)
    {
        // Le gimbal se pose AU centre : orbite et pan partent donc du coeur de la machine.
        Position = center;

        // Bornes de distance mises a l'echelle du rayon : une petite machine se regarde de pres,
        // une grande de loin, sans reglage manuel. min ~ 0.3R (assez pres sans traverser),
        // max ~ 8R (vue d'ensemble large). Garde-fou : radius peut etre absurde (pivot exotique).
        float r = Mathf.Max(radius, 0.001f);
        _distanceMin = r * 0.3f;
        _distanceMax = r * 8f;

        // Vue 3/4 plongeante par defaut : lisible d'emblee, sans etre au-dessus a la verticale.
        _yawDeg = 30f;
        _pitchDeg = -35f;
        _distance = Mathf.Clamp(r * 2.5f, _distanceMin, _distanceMax);

        // Si _Ready a deja cree la camera, on applique tout de suite ; sinon _Ready le fera.
        if (_camera is not null)
            ApplyTransform();
    }

    /// <summary>
    /// Entree souris/clavier. Lue dans _UnhandledInput (et non _Input) : un Control d'UI qui
    /// consomme l'event (S4.2) passe AVANT ce handler, donc la camera ne reagit pas au-dessus
    /// de l'UI. On marque l'event traite (SetInputAsHandled) quand on l'a reellement consomme,
    /// pour ne pas le laisser propager plus loin.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        // --- F11 : bascule maximisee <-> plein ecran sans bordure ------------------------------
        // On alterne selon le mode courant du DisplayServer (pas d'etat local a maintenir, donc
        // pas de desync si l'OS change le mode). Fullscreen = plein ecran exclusif borderless.
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
        {
            var mode = DisplayServer.WindowGetMode();
            DisplayServer.WindowSetMode(mode == DisplayServer.WindowMode.Fullscreen
                ? DisplayServer.WindowMode.Maximized
                : DisplayServer.WindowMode.Fullscreen);
            GetViewport().SetInputAsHandled();
            return;
        }

        // --- Bouton du milieu : molette (zoom) + suivi de l'appui (orbite/pan) -----------------
        if (@event is InputEventMouseButton mb)
        {
            switch (mb.ButtonIndex)
            {
                // Zoom MULTIPLICATIF : la distance varie d'une FRACTION d'elle-meme, donc les pas
                // sont petits de pres et grands de loin (vitesse ∝ distance, sans reptation). Clamp
                // >= _distanceMin : le zoom avant ne franchit jamais le point d'interet.
                case MouseButton.WheelUp:
                    if (mb.Pressed) { SetDistance(_distance * (1f - ZoomStep)); GetViewport().SetInputAsHandled(); }
                    break;
                case MouseButton.WheelDown:
                    if (mb.Pressed) { SetDistance(_distance * (1f + ZoomStep)); GetViewport().SetInputAsHandled(); }
                    break;
                // Bouton du milieu maintenu => on entre en mode drag (orbite ou pan selon Shift).
                case MouseButton.Middle:
                    _dragging = mb.Pressed;
                    GetViewport().SetInputAsHandled();
                    break;
            }
            return;
        }

        // --- Deplacement souris pendant le drag : orbite, ou pan si Shift enfonce ---------------
        if (@event is InputEventMouseMotion motion && _dragging)
        {
            if (motion.ShiftPressed)
                Pan(motion.Relative);
            else
                Orbit(motion.Relative);
            GetViewport().SetInputAsHandled();
        }
    }

    // Orbite : le deplacement souris tourne le gimbal. Signes choisis pour un ressenti "je fais
    // tourner l'objet" : glisser a droite fait tourner la scene vers la gauche (yaw negatif).
    // Le pitch est clampe pour rester une plongee (jamais sous le sol).
    private void Orbit(Vector2 mouseDelta)
    {
        _yawDeg -= mouseDelta.X * OrbitSensitivity;
        _pitchDeg = Mathf.Clamp(_pitchDeg - mouseDelta.Y * OrbitSensitivity, PitchMinDeg, PitchMaxDeg);
        ApplyTransform();
    }

    // Pan : on translate le POINT D'INTERET (le gimbal) dans le plan de la camera. On lit les
    // axes right/up de la camera dans le repere MONDE pour que le glissement suive l'ecran quelle
    // que soit l'orientation courante. Amplitude ∝ distance : de pres petits pas, de loin grands.
    private void Pan(Vector2 mouseDelta)
    {
        Basis camBasis = _camera.GlobalTransform.Basis;
        Vector3 right = camBasis.X;   // axe horizontal ecran (monde)
        Vector3 up = camBasis.Y;      // axe vertical ecran (monde)
        float amount = _distance * PanSensitivity;
        // Glisser a droite (delta.X>0) doit "pousser" la scene a droite => camera vers la gauche
        // => point d'interet vers la gauche (-right). Glisser vers le bas (delta.Y>0) => +up.
        Position += (-right * mouseDelta.X + up * mouseDelta.Y) * amount;
        // Pas d'ApplyTransform : on n'a bouge que la position du gimbal, la rotation est inchangee.
    }

    // Applique distance clampee puis repositionne la camera enfant sur son axe +Z local.
    private void SetDistance(float distance)
    {
        _distance = Mathf.Clamp(distance, _distanceMin, _distanceMax);
        _camera.Position = new Vector3(0f, 0f, _distance);
    }

    // Recompose la transform du gimbal (yaw/pitch) et le recul de la camera enfant.
    private void ApplyTransform()
    {
        RotationDegrees = new Vector3(_pitchDeg, _yawDeg, 0f);
        _camera.Position = new Vector3(0f, 0f, _distance);
    }
}

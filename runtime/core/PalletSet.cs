// =============================================================================
// PalletSet — cinematique scriptee des palettes sur le carrousel (brique 4b)
// =============================================================================
//
// Role : faire tourner N palettes sur un cercle avec le convoyeur, les ARRETER derriere un
// verin engage (blocage a un poste), et les faire s'ACCUMULER les unes derriere les autres en
// respectant un ecart minimal (min_gap). Expose la presence d'une palette dans la fenetre d'un
// capteur (B1/B2). Objet C# PUR (comme CylinderState/ConveyorState) : `dt` injecte, aucune
// horloge interne, aucune dependance Godot ni pivot -> testable en isolation (xUnit).
//
// --- Espace « sens de marche » (le truc qui evite tous les cas particuliers) --------------
// Une palette avance dans le sens de rotation. Pour ccw (trigonometrique), avancer = angle
// CROISSANT ; pour cw, avancer = angle DECROISSANT. Plutot que dupliquer toute la logique selon
// le sens, on travaille en interne dans un repere `phi` ou avancer est TOUJOURS croissant :
//   - ccw : phi = angle (identite) ;
//   - cw  : phi = 360 - angle (reflexion).
// La reflexion est une INVOLUTION (sa propre inverse) et preserve les distances angulaires, donc
// on l'applique dans les deux sens (angle->phi a l'entree, phi->angle a la sortie). Resultat : un
// seul chemin de calcul, forward-only, et le sens du pivot est honore par une conversion aux bords.
//
// --- Accumulation : relaxation iterative par ecart mod 360 (wrap-safe) --------------------
// A chaque tick, chaque palette VEUT avancer de `move = speed*dt`. Deux contraintes la freinent :
//   1. BLOCAGE : si l'arc qu'elle parcourt contient un poste bloque (verin engage), elle s'arrete
//      pile au poste. Un poste bloque = un obstacle fixe ; un verin engage agit donc comme une
//      « palette virtuelle arretee » au poste (exactement le point dur n°3 de l'amorce).
//   2. ACCUMULATION : elle ne peut pas se rapprocher a moins de `min_gap` de la palette DEVANT.
// La contrainte 2 est exprimee en ECART sur le cercle : `gap = (phi_devant - phi_ici) mod 360`
// doit rester >= min_gap. Formuler l'ecart en `mod 360` fait DISPARAITRE la couture 0°/360° : pas
// de tri fragile, pas de branche « la palette devant est de l'autre cote de 0 ». On relaxe en
// quelques passes (= nombre de palettes) : un arret de tete se propage de proche en proche vers la
// queue. Les cibles ne font que DECROITRE et sont bornees -> convergence garantie ; 3 palettes = trivial.
//
// --- Ce qui n'est PAS modelise (dettes assumees) -----------------------------------------
//   - D-002 : collision laterale. Une palette qui a DEJA franchi le poste avant que le verin
//     n'engage n'est pas rappelee en arriere (l'arc parcouru ne contient plus le poste) -> elle
//     poursuit. Conforme a la dette existante.
//   - D-003 : pas de rampe. Une palette part/s'arrete instantanement (vitesse constante `move`).

namespace CarrouselCore;

/// <summary>
/// Ensemble de palettes tournant sur un cercle : rotation avec le convoyeur, blocage aux postes
/// (verin engage), accumulation a <c>min_gap</c>, presence dans la fenetre d'un capteur. Modele
/// pur, deterministe, testable hors Godot.
/// </summary>
public sealed class PalletSet
{
    // Positions en espace « sens de marche » (avancer = phi croissant), normalisees dans [0..360).
    private readonly double[] _phi;
    private readonly double _minGap;   // ecart angulaire minimal entre palettes (accumulation)
    private readonly bool _ccw;        // sens de rotation (pilote la conversion angle <-> phi)

    // Nombre de passes de relaxation par tick. = nombre de palettes : suffit a propager un arret
    // de tete jusqu'a la derniere palette de la chaine, quel que soit l'ordre (y compris a la couture).
    private readonly int _passes;

    /// <param name="initialAnglesDeg">Positions angulaires de depart (deg), repliees dans [0..360).</param>
    /// <param name="minGapDeg">Ecart minimal entre deux palettes en accumulation (deg).</param>
    /// <param name="ccw">true = rotation trigonometrique (angle croissant).</param>
    public PalletSet(double[] initialAnglesDeg, double minGapDeg, bool ccw)
    {
        ArgumentNullException.ThrowIfNull(initialAnglesDeg);
        // Defensif (entree issue du pivot) : au moins une palette, ecart mini non negatif.
        if (initialAnglesDeg.Length == 0)
            throw new ArgumentException("au moins une palette attendue", nameof(initialAnglesDeg));
        if (minGapDeg < 0)
            throw new ArgumentOutOfRangeException(nameof(minGapDeg), minGapDeg, "min_gap doit etre >= 0");

        _minGap = minGapDeg;
        _ccw = ccw;
        _phi = new double[initialAnglesDeg.Length];
        for (int i = 0; i < _phi.Length; i++)
            _phi[i] = Flip(Norm(initialAnglesDeg[i]));   // angle -> espace de marche
        _passes = _phi.Length;
    }

    /// <summary>
    /// Positions angulaires courantes des palettes (deg, [0..360)), dans l'ordre des palettes
    /// (meme indice qu'a la construction). Lue par la 3D (brique 5) et les tests.
    /// </summary>
    public IReadOnlyList<double> AnglesDeg
    {
        get
        {
            var outp = new double[_phi.Length];
            for (int i = 0; i < _phi.Length; i++)
                outp[i] = Flip(_phi[i]);   // espace de marche -> angle (Flip est sa propre inverse)
            return outp;
        }
    }

    /// <summary>
    /// Avance les palettes de <paramref name="dt"/> secondes si le convoyeur tourne, en appliquant
    /// blocage (postes de <paramref name="blockedAnglesDeg"/>) puis accumulation a <c>min_gap</c>.
    /// </summary>
    /// <param name="conveyorRunning">Retour de marche du convoyeur (KM1_AUX) : rien ne bouge si false.</param>
    /// <param name="speedDegPerS">Vitesse angulaire du convoyeur (deg/s), tiree du pivot.</param>
    /// <param name="blockedAnglesDeg">Angles des postes ou un verin est ENGAGE (obstacles fixes).</param>
    public void Advance(double dt, bool conveyorRunning, double speedDegPerS, IReadOnlyList<double> blockedAnglesDeg)
    {
        if (dt < 0)
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "dt ne peut pas etre negatif");
        ArgumentNullException.ThrowIfNull(blockedAnglesDeg);

        // Convoyeur a l'arret (ou vitesse nulle) => palettes figees : rien a recalculer.
        double move = (conveyorRunning && speedDegPerS > 0) ? speedDegPerS * dt : 0.0;
        if (move <= 0.0)
            return;

        int n = _phi.Length;

        // Postes bloques convertis une fois en espace de marche (obstacles fixes de la contrainte 1).
        double[] blocked = new double[blockedAnglesDeg.Count];
        for (int b = 0; b < blocked.Length; b++)
            blocked[b] = Flip(Norm(blockedAnglesDeg[b]));

        // --- Etape 1 : cible libre de chaque palette, clampee au premier poste bloque atteint ---
        // `target[i]` peut depasser 360 (phi + move) : on ne normalise qu'a la toute fin. Les ecarts
        // etant calcules en mod 360, cela n'a aucune incidence sur l'accumulation.
        double[] target = new double[n];
        for (int i = 0; i < n; i++)
        {
            double allowed = move;   // avance permise, reduite au poste bloque le plus proche devant
            foreach (double s in blocked)
            {
                // Distance vers l'avant jusqu'au poste. == 0 si on est PILE au poste (deja bloque).
                // ~360 si on vient de le franchir (arc parcouru ne le contient plus -> pas de blocage, cf. D-002).
                double d = Norm(s - _phi[i]);
                if (d <= move)
                    allowed = Math.Min(allowed, d);
            }
            target[i] = _phi[i] + allowed;
        }

        // --- Etape 2 : accumulation. Ordre cyclique par phi croissant pour trouver le voisin DEVANT.
        // L'ordre ne change jamais (les palettes ne se croisent pas), on le recalcule par surete.
        int[] order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        Array.Sort(order, (a, b) => _phi[a].CompareTo(_phi[b]));

        int[] aheadOf = new int[n];   // aheadOf[i] = indice de la palette immediatement devant i
        for (int k = 0; k < n; k++)
            aheadOf[order[k]] = order[(k + 1) % n];

        // Relaxation : on repousse chaque palette pour garder gap >= min_gap avec celle de devant.
        // On balaie de la tete (phi le plus grand) vers la queue ; _passes balayages garantissent
        // la propagation meme si la chaine franchit la couture 0°/360°.
        for (int pass = 0; pass < _passes; pass++)
        {
            for (int k = n - 1; k >= 0; k--)
            {
                int i = order[k];
                int j = aheadOf[i];
                if (j == i)
                    continue;   // une seule palette : pas de contrainte d'accumulation

                double gap = Norm(target[j] - target[i]);
                if (gap < _minGap)
                {
                    target[i] -= (_minGap - gap);
                    if (target[i] < _phi[i])
                        target[i] = _phi[i];   // jamais reculer : au pire on reste sur place
                }
            }
        }

        // --- Etape 3 : publication des nouvelles positions (repliees dans [0..360)) ---
        for (int i = 0; i < n; i++)
            _phi[i] = Norm(target[i]);
    }

    /// <summary>
    /// Vrai si au moins une palette est dans la fenetre <paramref name="windowDeg"/> (centree)
    /// autour du poste <paramref name="stationAngleDeg"/> — modelise un capteur de presence (B1/B2).
    /// </summary>
    public bool PresentAt(double stationAngleDeg, double windowDeg)
    {
        double half = windowDeg / 2.0;
        // La distance angulaire est invariante par la reflexion Flip : on compare en espace de
        // marche (station convertie), inutile de repasser chaque palette en angle reel.
        double s = Flip(Norm(stationAngleDeg));
        foreach (double phi in _phi)
        {
            if (AngularDistance(phi, s) <= half)
                return true;
        }
        return false;
    }

    // --- Helpers geometriques (espace de marche, normalisation, distance) ---

    // Conversion angle <-> espace de marche. Identite pour ccw ; reflexion 360-a pour cw. INVOLUTION
    // (sa propre inverse), d'ou son usage dans les deux sens. Voir l'en-tete pour le pourquoi.
    private double Flip(double a) => _ccw ? a : Norm(360.0 - a);

    // Replie un angle (ou un ecart) dans [0..360). Gere les valeurs negatives (ex. -40 -> 320).
    private static double Norm(double a) => ((a % 360.0) + 360.0) % 360.0;

    // Distance angulaire minimale entre deux angles (le plus court des deux arcs), dans [0..180].
    private static double AngularDistance(double a, double b)
    {
        double d = Norm(a - b);
        return Math.Min(d, 360.0 - d);
    }
}

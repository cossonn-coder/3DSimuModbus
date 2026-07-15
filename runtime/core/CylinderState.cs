// =============================================================================
// CylinderState — cinematique scriptee d'un verin bloqueur MONOSTABLE (YV1/YV2)
// =============================================================================
//
// Role : modeliser la position d'un verin monostable au fil du temps, sans moteur physique
// (decision actee : cinematique scriptee deterministe, cf. docs/memory.md). Objet C# PUR :
// aucune dependance Godot ni pivot, tous les parametres sont injectes au constructeur et le
// pas de temps `dt` est passe a chaque avancement. C'est ce qui le rend testable en isolation
// (xUnit, valeurs a la main) — pilier de la strategie de test du projet.
//
// --- Modele : position 0..1 interpolee lineairement --------------------------------------
// La position va de 0 (verin RENTRE, ressort au repos) a 1 (verin SORTI). Un verin monostable
// n'a qu'une commande stable : bit=1 -> l'electrovanne pousse la tige vers 1 ; bit=0 -> plus
// d'alimentation, le RESSORT ramene la tige vers 0. Dans les deux sens la tige parcourt toute
// la course en `travel_time_ms` : la vitesse est donc constante = 1 / travel_time.
//
// --- Inversion en cours de course = GRATUITE ---------------------------------------------
// A chaque tick on se contente d'aller VERS la cible (1 si commande, 0 sinon) depuis la
// position COURANTE, a vitesse constante, en clampant dans [0..1]. Si la commande s'inverse a
// mi-course, la cible change et la tige repart du point ou elle etait : aucun cas special, pas
// de saut. C'est exactement le comportement « repart du point courant » exige par memory.md.
//
// --- Capteurs a seuils (fins de course + engagement) -------------------------------------
// Les fins de course reels ne se declenchent qu'aux extremites, pas des qu'on quitte le repos :
//   - IsRetracted (S11/S21) : pos < retracted_threshold (2 %)  -> tige rentree
//   - IsExtended  (S12/S22) : pos > extended_threshold  (98 %) -> tige sortie
//   - IsEngaged             : pos > block_threshold      (10 %) -> assez sortie pour BLOQUER une
//     palette. Utilise par la brique 4b (accumulation) ; expose des maintenant, cout nul.

namespace CarrouselCore;

/// <summary>
/// Etat d'un verin bloqueur monostable : position <c>0..1</c> avancee a vitesse constante vers la
/// commande, avec seuils de fins de course. Modele pur, deterministe, testable hors Godot.
/// </summary>
public sealed class CylinderState
{
    // Parametres figes au constructeur (tires du pivot par la simulation, jamais en dur ici).
    private readonly double _travelTimeS;   // duree d'une course complete 0<->1, en SECONDES
    private readonly double _retracted;     // seuil « rentre » (fraction de course)
    private readonly double _extended;      // seuil « sorti »
    private readonly double _block;         // seuil « engage » (bloque une palette)

    // Etat mutable : la position courante de la tige. Init a 0 = verin rentre (ressort au repos),
    // etat physique de depart d'un monostable non alimente.
    private double _position;

    /// <param name="travelTimeS">Temps d'une course complete, en secondes (ex. 0.5 pour 500 ms).</param>
    public CylinderState(double travelTimeS, double retractedThreshold, double extendedThreshold, double blockThreshold)
    {
        // Defensif : le temps de course vient du pivot (entree externe). Zero ou negatif = division
        // impossible / cinematique absurde -> on echoue tot plutot que de propager un NaN/Inf.
        if (travelTimeS <= 0)
            throw new ArgumentOutOfRangeException(nameof(travelTimeS), travelTimeS, "travel_time doit etre > 0");

        _travelTimeS = travelTimeS;
        _retracted = retractedThreshold;
        _extended = extendedThreshold;
        _block = blockThreshold;
        _position = 0.0;
    }

    /// <summary>Position courante de la tige, dans [0..1] (0 = rentre, 1 = sorti).</summary>
    public double Position => _position;

    /// <summary>Fin de course RENTRE (S11/S21) : la tige est sous le seuil bas.</summary>
    public bool IsRetracted => _position < _retracted;

    /// <summary>Fin de course SORTI (S12/S22) : la tige a depasse le seuil haut.</summary>
    public bool IsExtended => _position > _extended;

    /// <summary>Verin assez sorti pour bloquer une palette (utilise par la brique 4b).</summary>
    public bool IsEngaged => _position > _block;

    /// <summary>
    /// Avance la tige de <paramref name="dt"/> secondes vers sa cible : 1 si <paramref name="cmdExtend"/>
    /// (electrovanne alimentee), 0 sinon (rappel ressort). Vitesse constante, position clampee.
    /// </summary>
    public void Advance(double dt, bool cmdExtend)
    {
        if (dt < 0)
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "dt ne peut pas etre negatif");

        double target = cmdExtend ? 1.0 : 0.0;
        double step = dt / _travelTimeS;   // fraction de course franchie pendant dt

        // On se rapproche de la cible sans jamais la depasser (clamp via Min/Max) : c'est ce clamp
        // qui gere a la fois l'arrivee en butee ET l'inversion mi-course, sans branche dediee.
        if (_position < target)
            _position = Math.Min(target, _position + step);
        else if (_position > target)
            _position = Math.Max(target, _position - step);
    }
}

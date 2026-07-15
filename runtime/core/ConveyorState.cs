// =============================================================================
// ConveyorState — retour de marche du convoyeur (contact auxiliaire KM1_AUX)
// =============================================================================
//
// Role (brique 4a) : modeliser UNIQUEMENT le retour de marche du convoyeur, c'est-a-dire le
// contact auxiliaire KM1_AUX (signal `ret_running`). Le mouvement effectif des palettes est le
// sujet de la brique 4b : ici on ne gere que la temporisation electrique du contacteur.
//
// --- Ce que represente KM1_AUX -----------------------------------------------------------
// KM1 est le contacteur de puissance du moteur. Sa bobine, une fois commandee (cmd_run), met un
// court instant a fermer ses contacts (temps de « pick-up ») : le contact auxiliaire KM1_AUX ne
// recopie donc l'etat de la commande qu'apres `feedback_delay_ms` (50 ms au pivot). C'est ce
// petit retard que le PLC observe entre « je commande la marche » et « le moteur confirme tourner ».
//
// --- Modele : suiveur retarde symetrique -------------------------------------------------
// On accumule le temps pendant lequel la sortie DIFFERE de la commande ; des que ce temps atteint
// le delai, la sortie bascule sur la commande. Symetrique (fermeture ET ouverture temporisees de
// la meme duree) : c'est le modele le plus simple qui satisfait la spec « recopie apres delai »,
// sans distinguer pick-up et drop-out (simplicite d'abord — la dette D-003 note deja qu'on ne
// modelise pas les rampes moteur ; ici on ne modelise que le contacteur).
//
// Objet C# PUR : `dt` injecte, aucune horloge interne, aucune dependance Godot/pivot -> testable
// en isolation.

namespace CarrouselCore;

/// <summary>
/// Retour de marche du convoyeur : recopie retardee de la commande (contact auxiliaire KM1_AUX).
/// Modele pur, deterministe, testable hors Godot.
/// </summary>
public sealed class ConveyorState
{
    private readonly double _delayS;   // temporisation de recopie du contacteur, en SECONDES

    private bool _running;             // etat courant du contact auxiliaire (la sortie KM1_AUX)
    private double _pending;           // temps ecoule pendant lequel la commande differe de la sortie

    /// <param name="feedbackDelayS">Delai de recopie du contacteur, en secondes (ex. 0.05 pour 50 ms).</param>
    public ConveyorState(double feedbackDelayS)
    {
        // Defensif : un delai negatif n'a pas de sens physique (entree issue du pivot). Zero est
        // admis (recopie quasi immediate, basculee au tick suivant).
        if (feedbackDelayS < 0)
            throw new ArgumentOutOfRangeException(nameof(feedbackDelayS), feedbackDelayS, "feedback_delay ne peut pas etre negatif");

        _delayS = feedbackDelayS;
        _running = false;   // etat initial : moteur a l'arret, contact ouvert
        _pending = 0.0;
    }

    /// <summary>Etat du contact auxiliaire KM1_AUX : true = le convoyeur confirme tourner.</summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Avance la temporisation de <paramref name="dt"/> secondes. Tant que <paramref name="cmdRun"/>
    /// differe de la sortie, on accumule ; au bout de <c>feedback_delay</c>, la sortie recopie la commande.
    /// </summary>
    public void Advance(double dt, bool cmdRun)
    {
        if (dt < 0)
            throw new ArgumentOutOfRangeException(nameof(dt), dt, "dt ne peut pas etre negatif");

        if (cmdRun == _running)
        {
            // Sortie deja alignee sur la commande : rien a temporiser, on remet le compteur a zero
            // (un a-coup bref de la commande qui revient a son etat n'entame pas le delai).
            _pending = 0.0;
        }
        else
        {
            _pending += dt;
            if (_pending >= _delayS)
            {
                _running = cmdRun;
                _pending = 0.0;
            }
        }
    }
}

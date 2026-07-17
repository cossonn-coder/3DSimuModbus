// =============================================================================
// ForceSet — modele de FORCAGE des signaux `cmd` dans la simulation pure (sprint 6)
// =============================================================================
//
// Role : porter, hors de toute logique Godot ou Modbus, la LISTE des signaux de
// commande (`cmd` TOR) dont l'operateur substitue la valeur EFFECTIVE depuis l'IHM —
// pour piloter la machine SANS PLC (cmd a 0 -> forcer a 1 = commander) et MALGRE le
// PLC (le M580 ecrit cmd=0 -> forcer a 1 gagne). C'est le miroir cote `cmd` de FaultSet
// (qui, lui, masque les retours `ret`).
//
// --- Pourquoi une classe SEPAREE de FaultSet (et non une extension) -----------------------
// Forcage != defaut, au sens de l'automaticien (Control Expert) : un forcage est une
// substitution DELIBEREE d'entree/sortie pour la mise au point, pas une panne simulee.
// Les garder distincts preserve l'invariant « FaultSet vide = nominal strict » intact, et
// laisse un code strictement symetrique (ActiveForces <-> ActiveStucks). ForceSet est
// expose a cote de FaultSet (`_sim.Forces` / `_sim.Faults`).
//
// --- Masque a la LECTURE, jamais d'ecriture datastore -------------------------------------
// Le Tick applique le forcage sur la COPIE snapshot des commandes (SnapshotCommands rend une
// copie defensive), en TETE de tick et AVANT tout decodage : tout le pipeline (run/extend,
// physique, KM1_AUX) voit la commande effective. On ne force JAMAIS le datastore `cmd` lui-meme
// (invariant identique au masque capteur `ret` de FaultSet) : le M580 continue d'ecrire `cmd`,
// la sim substitue la valeur forcee a la lecture seulement.
//
// --- Pourquoi PAS de verrou (meme justification que FaultSet) -----------------------------
// ForceSet n'est mute QUE par l'IHM (thread principal Godot) et lu QUE par le Tick physique,
// qui tourne sur ce meme thread principal (_PhysicsProcess). Aucun thread serveur n'y touche :
// un objet mutable nu suffit, un verrou serait du bruit.

namespace CarrouselCore;

/// <summary>Mode de forcage d'un signal `cmd` TOR : Auto (nominal, valeur PLC) / force a 0 / force a 1.</summary>
public enum ForceMode { Auto, ForceLow, ForceHigh }

/// <summary>
/// Commande de forcage emise par l'IHM (dispatch dans <see cref="ForceSet.Apply"/>).
/// <c>Auto</c> efface le forcage du signal (retour a la valeur PLC).
/// </summary>
public readonly record struct ForceCommand(string ComponentId, string SignalName, ForceMode Mode);

/// <summary>
/// Ensemble mutable des forcages actifs, partage entre l'IHM (qui le mute) et la boucle de
/// simulation (qui le lit en tete de Tick). Objet pur, sans dependance Godot ni Modbus.
/// </summary>
public sealed class ForceSet
{
    // Forcage courant par (composant, nom de signal). Absent => Auto (nominal). Meme regle que
    // FaultSet : on ne stocke JAMAIS Auto (SetForce(Auto) efface l'entree), ce qui garde
    // HasAnyForce trivial et « ForceSet vide = nominal strict » (aucune substitution). La cle
    // inclut le composant car deux composants peuvent avoir un signal de meme nom (cmd_extend
    // sur cylinder_1 et cylinder_2).
    private readonly Dictionary<(string ComponentId, string SignalName), ForceMode> _forces = new();

    // --- Mutations (appelees directement par les tests, ou via Apply depuis l'UI) ---

    /// <summary>Fixe (ou efface si <see cref="ForceMode.Auto"/>) le forcage d'un signal `cmd` TOR.</summary>
    public void SetForce(string componentId, string signalName, ForceMode mode)
    {
        if (mode == ForceMode.Auto)
            _forces.Remove((componentId, signalName));
        else
            _forces[(componentId, signalName)] = mode;
    }

    /// <summary>Dispatch d'une commande UI de forcage vers la mutation correspondante.</summary>
    public void Apply(ForceCommand cmd) => SetForce(cmd.ComponentId, cmd.SignalName, cmd.Mode);

    /// <summary>« Reparer » l'element : efface tous les forcages de ce composant (retour Auto).</summary>
    public void ClearComponent(string componentId)
    {
        // Materialiser la liste des cles AVANT de supprimer evite de muter le dictionnaire
        // pendant son enumeration.
        var toRemove = _forces.Keys.Where(k => k.ComponentId == componentId).ToList();
        foreach (var k in toRemove)
            _forces.Remove(k);
    }

    // --- Lectures (interrogees par le Tick et l'affichage) ---

    /// <summary>Forcage courant d'un signal (<see cref="ForceMode.Auto"/> si non force).</summary>
    public ForceMode GetForce(string componentId, string signalName)
        => _forces.TryGetValue((componentId, signalName), out var m) ? m : ForceMode.Auto;

    /// <summary>Vrai si l'element porte au moins un forcage (marqueur panneau, sprint S6.2).</summary>
    public bool HasAnyForce(string componentId)
        => _forces.Keys.Any(k => k.ComponentId == componentId);

    /// <summary>
    /// Liste des forcages actifs, consommee par le Tick pour substituer les bits `cmd` AVANT
    /// decodage (miroir de <see cref="FaultSet.ActiveStucks"/>, mais cote `cmd`). Materialisee a
    /// la demande (poignee de forcages : cout negligeable).
    /// </summary>
    public IReadOnlyList<(string ComponentId, string SignalName, ForceMode Mode)> ActiveForces
        => _forces.Select(kv => (kv.Key.ComponentId, kv.Key.SignalName, kv.Value)).ToList();
}

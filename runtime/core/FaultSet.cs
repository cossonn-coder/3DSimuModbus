// =============================================================================
// FaultSet — modele de defauts injectables dans la simulation pure (sprint 5)
// =============================================================================
//
// Role : porter, hors de toute logique Godot ou Modbus, la LISTE des defauts que
// l'operateur force depuis l'IHM pour eprouver le M580. La simulation lit ce set en
// tete de chaque Tick et en fait decouler les mots `ret` — SANS jamais forcer un mot
// du datastore (invariant D-016 : le masque vit dans la sim, pas dans le transport).
//
// --- Trois familles de defauts, toutes generiques (pensees « machine inconnue ») -----------
//   1. PHYSIQUE (par composant) : le modele cinematique lui-meme derape. Un verin coince a
//      mi-course, un verin qui ne sort plus (reste rentre), un convoyeur qui patine (KM1
//      confirme tourner mais les palettes n'avancent pas). Ce sont les SEULS cas ou l'on
//      connait le type du composant — encapsule dans FaultCatalog, pas ici.
//   2. CAPTEUR-BLOQUE (par signal) : n'importe quel bit `ret` TOR est force a 0 (Low) ou 1
//      (High) APRES l'encodage nominal. Couvre S11/S12/S21/S22, B1/B2, et ret_running
//      (KM1_AUX « colle ») — aucun de ces cas n'est special : c'est le meme mecanisme.
//   3. GEL `ret` (RetFrozen) : la sim continue de tourner (la 3D bouge), mais plus AUCUN mot
//      `ret` n'est publie, heartbeat compris. Le PLC voit une image figee (detection de sim
//      morte). Global, tout-ou-rien (jamais un heartbeat fige avec des bits vivants).
//
// --- Pourquoi PAS de verrou (contrairement au datastore) ---------------------------------
// Le datastore est touche par DEUX threads (serveur Modbus + physique), d'ou son verrou.
// FaultSet, lui, n'est mute QUE par l'IHM (thread principal Godot) et lu QUE par le Tick
// physique — or dans ce projet Tick tourne sur ce meme thread principal (_PhysicsProcess).
// Aucun thread serveur n'y touche : un objet mutable nu suffit, un verrou serait du bruit.

namespace CarrouselCore;

/// <summary>Defaut physique applicable a un composant selon son type (cinematique qui derape).</summary>
public enum PhysicalFault
{
    None,
    CylinderStuckRetracted,   // verin qui ne sort plus : reste rentre quelle que soit la commande
    CylinderStuckMidStroke,   // verin coince : position gelee (ni sortie ni rentree)
    ConveyorSlip,             // convoyeur qui patine : KM1_AUX confirme, mais palettes figees
}

/// <summary>Mode d'un capteur bloque : force le bit `ret` a 0 (Low) ou a 1 (High).</summary>
public enum StuckMode { None, Low, High }

/// <summary>Famille d'une action de defaut cote UI (dispatch dans <see cref="FaultSet.Apply"/>).</summary>
public enum FaultKind { Physical, SensorStuck, Repair }

/// <summary>
/// Descripteur d'une action de defaut declenchee par l'IHM (menu). C'est l'unite que
/// <see cref="FaultCatalog.ApplicableTo"/> enumere et que <see cref="FaultSet.Apply"/> dispatche.
/// <c>Repair</c> efface tous les defauts de l'element (physique + capteurs bloques).
/// </summary>
public readonly record struct FaultCommand(
    FaultKind Kind, string ComponentId,
    PhysicalFault Physical = PhysicalFault.None,
    string? SignalName = null, StuckMode Stuck = StuckMode.None);

/// <summary>
/// Ensemble mutable des defauts actifs, partage entre l'IHM (qui le mute) et la boucle de
/// simulation (qui le lit en tete de Tick). Objet pur, sans dependance Godot ni Modbus.
/// </summary>
public sealed class FaultSet
{
    // Defaut physique courant par composant. Absent => None (nominal). On ne stocke JAMAIS
    // None : SetPhysical(None) efface l'entree, ce qui garde HasAnyFault trivial et le nominal
    // strictement identique a « aucune entree » (preuve : les 4 pytest restent verts).
    private readonly Dictionary<string, PhysicalFault> _physical = new();

    // Capteurs bloques, indexes par (composant, nom de signal). Meme regle : Low/High seulement,
    // jamais None (SetSensorStuck(None) efface). La cle inclut le composant car deux composants
    // peuvent avoir un signal de meme nom (ex. ret_active sur B1 et B2).
    private readonly Dictionary<(string ComponentId, string SignalName), StuckMode> _stucks = new();

    /// <summary>Gel global de la zone `ret` (heartbeat inclus) : la sim tourne, plus rien n'est publie.</summary>
    public bool RetFrozen { get; set; }

    // --- Mutations (appelees directement par les tests, ou via Apply depuis l'UI) ---

    /// <summary>Fixe (ou efface si <see cref="PhysicalFault.None"/>) le defaut physique d'un composant.</summary>
    public void SetPhysical(string componentId, PhysicalFault fault)
    {
        if (fault == PhysicalFault.None)
            _physical.Remove(componentId);
        else
            _physical[componentId] = fault;
    }

    /// <summary>Bloque (ou libere si <see cref="StuckMode.None"/>) un signal `ret` TOR a 0/1.</summary>
    public void SetSensorStuck(string componentId, string signalName, StuckMode mode)
    {
        if (mode == StuckMode.None)
            _stucks.Remove((componentId, signalName));
        else
            _stucks[(componentId, signalName)] = mode;
    }

    /// <summary>« Reparer » l'element : efface son defaut physique ET tous ses capteurs bloques.</summary>
    public void ClearComponent(string componentId)
    {
        _physical.Remove(componentId);
        // On retire toutes les cles de stuck appartenant a ce composant. Materialiser la liste
        // AVANT de supprimer evite de muter le dictionnaire pendant son enumeration.
        var toRemove = _stucks.Keys.Where(k => k.ComponentId == componentId).ToList();
        foreach (var k in toRemove)
            _stucks.Remove(k);
    }

    /// <summary>Dispatch d'une commande UI vers la mutation correspondante.</summary>
    public void Apply(FaultCommand cmd)
    {
        switch (cmd.Kind)
        {
            case FaultKind.Physical:
                SetPhysical(cmd.ComponentId, cmd.Physical);
                break;
            case FaultKind.SensorStuck:
                // SignalName est requis pour un capteur bloque : un cmd malforme (UI incoherente)
                // echoue clairement plutot que de bloquer un signal fantome sans effet visible.
                if (cmd.SignalName is null)
                    throw new ArgumentException("SensorStuck exige un SignalName", nameof(cmd));
                SetSensorStuck(cmd.ComponentId, cmd.SignalName, cmd.Stuck);
                break;
            case FaultKind.Repair:
                ClearComponent(cmd.ComponentId);
                break;
        }
    }

    // --- Lectures (interrogees par le Tick et l'affichage) ---

    /// <summary>Defaut physique courant d'un composant (<see cref="PhysicalFault.None"/> si aucun).</summary>
    public PhysicalFault GetPhysical(string componentId)
        => _physical.TryGetValue(componentId, out var f) ? f : PhysicalFault.None;

    /// <summary>Mode de blocage courant d'un signal (<see cref="StuckMode.None"/> si non bloque).</summary>
    public StuckMode GetSensorStuck(string componentId, string signalName)
        => _stucks.TryGetValue((componentId, signalName), out var m) ? m : StuckMode.None;

    /// <summary>
    /// Liste des capteurs bloques actifs, consommee par le Tick pour masquer les bits `ret`
    /// APRES encodage. Materialisee a la demande (poignee de defauts : cout negligeable).
    /// </summary>
    public IReadOnlyList<(string ComponentId, string SignalName, StuckMode Mode)> ActiveStucks
        => _stucks.Select(kv => (kv.Key.ComponentId, kv.Key.SignalName, kv.Value)).ToList();

    /// <summary>Vrai si l'element porte au moins un defaut (marqueur 3D / badge, sprint S5.3).</summary>
    public bool HasAnyFault(string componentId)
        => _physical.ContainsKey(componentId)
           || _stucks.Keys.Any(k => k.ComponentId == componentId);
}

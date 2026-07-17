// =============================================================================
// FaultCatalog — quels defauts sont applicables a un composant donne (sprint 5)
// =============================================================================
//
// Role : produire, pour un composant du pivot, la liste des defauts que l'IHM peut lui
// proposer (le menu « Defaut » d'une ligne). C'est le SEUL endroit du sprint 5 qui connait
// le TYPE d'un composant — et c'est volontaire : la generecite du projet (« survivra-t-il a
// une machine generee depuis un DWG inconnu ? ») exige de concentrer ici, et nulle part
// ailleurs, la table type -> defauts physiques. Tout le reste (FaultSet, injection dans Tick)
// est agnostique du type.
//
// --- Deux briques, dont une 100 % generique -----------------------------------------------
//   1. PHYSIQUE (specifique au type) : une petite table figee. Un type inconnu => aucune
//      physique proposee (on n'invente pas de cinematique pour une machine qu'on ne connait
//      pas), mais le composant reste eligible au capteur-bloque ci-dessous.
//   2. CAPTEUR-BLOQUE (generique) : pour CHAQUE signal `ret` TOR du composant, on emet deux
//      commandes (bloque a 0, bloque a 1). Aucune connaissance de type : cela marche pour
//      S11/S12 d'un verin, B1/B2 d'un capteur, ret_running d'un convoyeur, ou n'importe quel
//      bit `ret` d'un composant futur jamais vu. « KM1_AUX colle » n'est PAS un cas special :
//      c'est simplement le capteur-bloque du signal ret_running du convoyeur.
//
// Repair n'est PAS emis ici : l'UI l'ajoute elle-meme au menu quand HasAnyFault(comp) est vrai.

namespace CarrouselCore;

/// <summary>
/// Catalogue des defauts applicables a un composant, deduits de son type (physique) et de ses
/// signaux `ret` TOR (capteur-bloque). Statique et pur : une projection du pivot, sans etat.
/// </summary>
public static class FaultCatalog
{
    /// <summary>
    /// Defauts proposables pour <paramref name="comp"/> : d'abord les defauts physiques du type,
    /// puis, pour chaque signal `ret` TOR, un blocage Low et un blocage High. N'inclut pas Repair.
    /// </summary>
    public static IReadOnlyList<FaultCommand> ApplicableTo(Component comp)
    {
        ArgumentNullException.ThrowIfNull(comp);

        var outp = new List<FaultCommand>();

        // 1. Physique, par type (seule connaissance type-specifique du sprint).
        foreach (var f in PhysicalFaultsForType(comp.Type))
            outp.Add(new FaultCommand(FaultKind.Physical, comp.Id, Physical: f));

        // 2. Capteur-bloque, generique : Low puis High pour chaque bit `ret` TOR du composant.
        //    On ordonne les signaux par nom pour une sortie deterministe (menu stable d'un run
        //    a l'autre) : IReadOnlyDictionary n'a pas d'ordre garanti.
        foreach (var sig in comp.Signals.Values
                     .Where(s => s.IsTor && s.Zone == "ret")
                     .OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            outp.Add(new FaultCommand(FaultKind.SensorStuck, comp.Id, SignalName: sig.Name, Stuck: StuckMode.Low));
            outp.Add(new FaultCommand(FaultKind.SensorStuck, comp.Id, SignalName: sig.Name, Stuck: StuckMode.High));
        }

        return outp;
    }

    /// <summary>
    /// Table type -> defauts physiques. Un type absent de la table (capteur, ou composant futur
    /// inconnu) renvoie une sequence vide : pas de physique inventee, mais le capteur-bloque reste.
    /// </summary>
    private static IEnumerable<PhysicalFault> PhysicalFaultsForType(string type) => type switch
    {
        "cylinder_blocker" => new[] { PhysicalFault.CylinderStuckRetracted, PhysicalFault.CylinderStuckMidStroke, PhysicalFault.BlockerIneffective },
        "conveyor_circular" => new[] { PhysicalFault.ConveyorSlip },
        _ => Array.Empty<PhysicalFault>(),
    };
}

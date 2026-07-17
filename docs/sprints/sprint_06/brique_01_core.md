# S6.1 — Cœur : `BlockerIneffective` + forçage `ForceSet` (headless / xUnit)

> **Cold-start** : lire `CLAUDE.md` + `docs/sprints/sprint_06/00_etat.md` + cette amorce suffit.
> Sous-sprint **fonctionnel/headless** : que du `CarrouselCore` pur + tests xUnit. Aucune ligne Godot,
> aucun fichier de scène. Banc core **re-figé** (nouveaux cas). Les 4 pytest full-chain **inchangés**.

## Objectif

Deux ajouts au cœur pur, tous deux **génériques via le pivot**, tous deux **inactifs = nominal strict** :

1. **`BlockerIneffective`** — nouveau défaut physique du vérin : la tige **sort normalement**
   (S12=1, cinématique nominale), mais le poste est **exclu du blocage** → les palettes traversent
   une tige levée. Signature PLC : « je crois bloquer, B1 se libère quand même ».
2. **Forçage** — nouvelle classe pure **`ForceSet`** (miroir de `FaultSet`) : par signal `cmd` TOR,
   un mode `Auto` / `ForceLow` / `ForceHigh`. Appliqué **en tête de `Tick`**, après
   `SnapshotCommands`, sur la **copie snapshot** (jamais le datastore). Le M580 continue d'écrire
   `cmd` ; la sim **substitue** la valeur forcée à la lecture.

## Contrat d'API visé

### `runtime/core/ForceSet.cs` (fichier NEUF, calqué sur `FaultSet.cs`)

```csharp
namespace CarrouselCore;

/// <summary>Mode de forçage d'un signal `cmd` TOR : Auto (nominal) / forcé à 0 / forcé à 1.</summary>
public enum ForceMode { Auto, ForceLow, ForceHigh }

/// <summary>Commande de forçage émise par l'IHM (dispatch dans ForceSet.Apply).</summary>
public readonly record struct ForceCommand(string ComponentId, string SignalName, ForceMode Mode);

public sealed class ForceSet
{
    // Indexé par (composant, nom de signal). Auto = absence d'entrée (SetForce(Auto) efface).
    // Même règle que FaultSet : on ne stocke JAMAIS l'état nominal → « ForceSet vide = nominal ».
    public void SetForce(string componentId, string signalName, ForceMode mode);   // Auto ⇒ Remove
    public void Apply(ForceCommand cmd);                                            // dispatch UI
    public ForceMode GetForce(string componentId, string signalName);              // Auto si absent
    public void ClearComponent(string componentId);                                // « réparer » les forçages d'un élément
    public bool HasAnyForce(string componentId);                                   // marquage panneau
    // Consommé par le Tick pour masquer les bits `cmd` AVANT décodage (miroir de FaultSet.ActiveStucks) :
    public IReadOnlyList<(string ComponentId, string SignalName, ForceMode Mode)> ActiveForces { get; }
}
```

Pas de verrou (même justification que `FaultSet` : muté par l'IHM sur le thread principal, lu par le
Tick sur ce même thread ; aucun thread serveur n'y touche).

### `runtime/core/FaultSet.cs` — ajout d'un mode physique

```csharp
public enum PhysicalFault
{
    None,
    CylinderStuckRetracted,
    CylinderStuckMidStroke,
    ConveyorSlip,
    BlockerIneffective,   // NOUVEAU : la tige sort (S12=1) mais ne retient plus les palettes
}
```

### `runtime/core/FaultCatalog.cs` — proposer le nouveau mode

Dans `PhysicalFaultsForType`, ligne `"cylinder_blocker"` : ajouter `PhysicalFault.BlockerIneffective`
à la liste. C'est le **seul** endroit qui connaît le type (invariant §0bis respecté).

### `runtime/core/CarrouselSimulation.cs` — trois greffes chirurgicales

1. **Exposer `ForceSet`** (comme `Faults`) :
   ```csharp
   public ForceSet Forces { get; } = new();
   ```
2. **Index `_cmdTorSignals`** (miroir exact de `_retTorSignals`, construit dans le même `foreach` du
   constructeur, condition `sig.IsTor && sig.Zone == "cmd"`) : `(compId, sigName) -> Signal`.
3. **Application du forçage en tête de `Tick`**, juste après `ushort[] cmd = store.SnapshotCommands();`
   et **avant** le décodage `run`/`extend1`/`extend2` :
   ```csharp
   // Forçage (S6.1) : on réécrit les bits `cmd` forcés dans la COPIE snapshot (jamais le datastore,
   // invariant identique au masque capteur `ret`). Symétrique de Faults.ActiveStucks, mais côté cmd
   // et AVANT décodage : tout le pipeline (run/extend/physique/KM1_AUX) voit la commande EFFECTIVE.
   foreach (var (compId, sigName, mode) in Forces.ActiveForces)
       if (_cmdTorSignals.TryGetValue((compId, sigName), out var sig))
           sig.WriteBit(ref cmd[sig.WordRel], mode == ForceMode.ForceHigh);
   ```
4. **`BlockerIneffective` dans `CollectBlockedStations`** : un vérin engagé n'ajoute son poste que si
   ce défaut n'est PAS actif :
   ```csharp
   if (_cyl1.State.IsEngaged && Faults.GetPhysical(_cyl1.Id) != PhysicalFault.BlockerIneffective)
       blocked.Add(_cyl1.StationAngleDeg);
   // idem _cyl2
   ```
   `AdvanceCylinder` reste **nominal** pour ce défaut (la tige sort, S11/S12 nominaux) : ne PAS ajouter
   de branche dans `AdvanceCylinder` — seule l'exclusion du blocage change.

## Décisions pré-tranchées (ne pas ré-instruire)

- **`ForceSet` séparé** de `FaultSet` (pas d'extension). Voir `00_etat.md`.
- **Masque à la lecture uniquement** — aucune écriture `cmd`/`ret` du datastore. `SnapshotCommands()`
  renvoie une **copie défensive** : la muter est sûr et n'affecte pas le fil Modbus.
- **Ordre des couches** : forçage `cmd` (tête) → défaut physique → masque capteur `ret` (fin). Ne pas
  réordonner. Un YV1 forcé à 1 + « ne sort pas » ⇒ `AdvanceCylinder` force la commande effective à
  faux (le défaut gagne) → tige rentrée. **Tester ce cas** (déterminisme de la composition).
- **KM1_AUX** suit la commande effective : forcer `cmd_run` fait suivre `ConveyorState` puis KM1_AUX.

## Definition of Done (cochable)

- [x] `ForceSet.cs` créé, commentaires pédagogiques FR (rôle, pourquoi pas de verrou, masque lecture).
- [x] `BlockerIneffective` ajouté à l'enum, au catalogue (cylinder_blocker), et à `CollectBlockedStations`.
- [x] `Forces` exposé + `_cmdTorSignals` construit + application en tête de `Tick`.
- [x] Tests xUnit ajoutés (voir ci-dessous) ; `dotnet test` **vert** (121/121).
- [x] Banc core **re-figé** : nouveau témoin = 109 + 12 = **121** (5 cas ForceSet purs + 7 cas
      injection sim forçage/BlockerIneffective ; le test catalogue cylindre passe de 2 à 3 physiques,
      renommé, count 6→7). Consigné pour `memory.md`/NOTES (clôture).
- [x] `pytest testbench/test_modbus_chain.py` **inchangé** (non relancé ici : aucun runtime n'écoute
      sur 502 ; forçage + BlockerIneffective inactifs ⇒ nominal strict, garanti par les cas « vide »).

## Tests xUnit à écrire (`runtime/tests/`)

`ForceSetTests.cs` (neuf) — miroir de `FaultSetTests.cs` :
- `SetForce(Auto)` efface l'entrée ; `GetForce` rend `Auto` par défaut ; `HasAnyForce` cohérent.
- `Apply(ForceCommand)` dispatche ; `ClearComponent` efface tous les forçages d'un composant.
- `ActiveForces` liste bien les forçages actifs.

Dans `CarrouselSimulationTests.cs` (ajouts) :
- **Forçage sans PLC** : `cmd` datastore à 0, forcer `cylinder_1/cmd_extend` à `ForceHigh` → après
  assez de ticks, S12 (`ret_extended`) = 1 et la tige sort. Prouve « piloter sans PLC ».
- **Forçage contre PLC** : `cmd` datastore met `cmd_extend`=0 (PLC), forcer à `ForceHigh` → la tige
  sort quand même. Vérifier que le **datastore `cmd` n'est pas modifié** (lecture directe inchangée).
- **Forçage à 0 contre PLC** : PLC commande 1, forcer `ForceLow` → tige reste rentrée.
- **KM1_AUX sous forçage** : forcer `conveyor/cmd_run` à `ForceHigh`, PLC à 0 → KM1_AUX passe à 1
  après `feedback_delay_ms`.
- **Composition forçage × défaut** : forcer `cmd_extend`=1 + `CylinderStuckRetracted` actif → tige
  rentrée (le défaut physique gagne), S12=0. Déterminisme prouvé.
- **`BlockerIneffective`** : commander le vérin à sortir jusqu'à S12=1, injecter `BlockerIneffective`,
  faire tourner le convoyeur → une palette **traverse** le poste (l'angle du poste n'est plus dans les
  postes bloqués ; B monte puis redescend au lieu de rester actif). Cas complémentaire : sans le
  défaut, la palette **reste bloquée** (non-régression du blocage nominal).

## Vérif autosuffisante (prouver le vert sans contexte externe)

```
dotnet test runtime/tests/CarrouselCore.Tests.csproj      # tous verts, nouveau total affiché
cd testbench && pytest test_modbus_chain.py -q            # 4 verts, INCHANGÉ
```
Le nouveau total core = compte affiché par `dotnet test` ; le noter (re-figeage prévu et justifié).

## Ce qu'il NE faut PAS faire

- **Aucune écriture** du datastore `cmd`/`ret` (ni `store.Wire...`, ni `PublishReturns` détourné).
- Ne pas fusionner `ForceSet` dans `FaultSet`, ni forcer via `FaultSet`.
- Ne pas toucher au pivot JSON, ni à l'ordre des mots, ni à la cadence 10 Hz.
- Pas de branche `BlockerIneffective` dans `AdvanceCylinder` (la tige sort normalement).
- Pas de Godot ici (ni scène, ni panneau) — c'est S6.2.
- Ne pas coder d'id carrousel en dur : tout passe par les Signal résolus du pivot.

## DÉPENDANCES / FICHIERS TOUCHÉS

- **Dépendances** : aucune (premier sous-sprint).
- **Fichiers** : `runtime/core/ForceSet.cs` (neuf), `runtime/core/FaultSet.cs`,
  `runtime/core/FaultCatalog.cs`, `runtime/core/CarrouselSimulation.cs`,
  `runtime/tests/ForceSetTests.cs` (neuf), `runtime/tests/CarrouselSimulationTests.cs`.

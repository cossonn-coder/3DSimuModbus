# S5.1 — Cœur pur : `FaultSet` + `FaultCatalog` + injection dans `CarrouselSimulation`

> Sous-sprint **headless** (aucun Godot). Reprise à froid : `CLAUDE.md` + `00_etat.md` + cette amorce.

## Objectif
Introduire le modèle de défauts dans la **sim pure** (`CarrouselCore`) et l'appliquer dans
`Tick`, de sorte que les mots `ret` reflètent le défaut **sans qu'aucun mot ne soit forcé au
datastore**. Trois familles : **physique** (vérin/convoyeur), **capteur-bloqué** (tout bit `ret`
TOR), **gel `ret`**. Défaut inactif ⇒ comportement **strictement nominal**.

## Fichiers touchés
- **Neuf** : `runtime/core/FaultSet.cs`, `runtime/core/FaultCatalog.cs`, `runtime/tests/FaultSetTests.cs`.
- **Modifié** : `runtime/core/CarrouselSimulation.cs` (expose `Faults`, applique dans `Tick`).
- Lire pour comprendre : `CarrouselSimulation.cs`, `CylinderState.cs`, `ConveyorState.cs`,
  `PalletSet.cs`, `ModbusDataStore.cs`, et le type `Signal`/`Component` dans `PivotModel.cs`.

## Contrat d'API visé (CarrouselCore, zéro Godot)

```csharp
public enum PhysicalFault { None, CylinderStuckRetracted, CylinderStuckMidStroke, ConveyorSlip }
public enum StuckMode { None, Low, High }   // capteur bloque a 0 / a 1

// Descripteur d'une action UI (menu). Repair = efface les defauts de l'element.
public enum FaultKind { Physical, SensorStuck, Repair }
public readonly record struct FaultCommand(
    FaultKind Kind, string ComponentId,
    PhysicalFault Physical = PhysicalFault.None,
    string? SignalName = null, StuckMode Stuck = StuckMode.None);

public sealed class FaultSet
{
    // Mutations (tests directs + UI via Apply)
    public void SetPhysical(string componentId, PhysicalFault fault);        // None => efface
    public void SetSensorStuck(string componentId, string signalName, StuckMode mode);
    public void ClearComponent(string componentId);                          // "reparer" l'element
    public void Apply(FaultCommand cmd);                                     // dispatch UI
    public bool RetFrozen { get; set; }                                      // gel ret (heartbeat inclus)

    // Lectures (Tick + affichage)
    public PhysicalFault GetPhysical(string componentId);
    public StuckMode GetSensorStuck(string componentId, string signalName);
    public IReadOnlyList<(string ComponentId, string SignalName, StuckMode Mode)> ActiveStucks { get; }
    public bool HasAnyFault(string componentId);                             // marqueur 3D / badge (S5.3)
}

// Modes applicables a un composant, deduits de son TYPE et de ses signaux ret TOR.
// Generique (survit a une machine inconnue) : la SEULE connaissance type-specifique.
public static class FaultCatalog
{
    // Physique par type + capteur-bloque (Low et High) pour CHAQUE signal ret TOR du composant.
    // N'inclut PAS Repair (l'UI l'ajoute si HasAnyFault).
    public static IReadOnlyList<FaultCommand> ApplicableTo(Component comp);
}
```

`CarrouselSimulation` : ajouter `public FaultSet Faults { get; } = new();` et l'appliquer dans `Tick`.

## Décisions pré-tranchées
- **Table type→physique** (dans `FaultCatalog`) : `cylinder_blocker` → {CylinderStuckRetracted,
  CylinderStuckMidStroke} ; `conveyor_circular` → {ConveyorSlip} ; `sensor_presence` → {} ;
  type inconnu → {} physique **mais** capteur-bloqué quand même sur ses bits `ret` TOR.
- **Capteur-bloqué générique** : `ApplicableTo` émet, pour chaque `Signal` de `comp.Signals`
  avec `IsTor && Zone=="ret"`, deux `FaultCommand` (Low, High). Couvre S11/S12/S21/S22, B1/B2,
  et `ret_running` (KM1_AUX) — « KM1_AUX collé » **n'est pas** un cas spécial.
- **Application dans `Tick`** (ordre) :
  1. lire `Faults` en tête (même thread, pas de snapshot spécial nécessaire) ;
  2. vérin : `CylinderStuckMidStroke` ⇒ **ne pas appeler `Advance`** (gèle la position courante) ;
     `CylinderStuckRetracted` ⇒ `Advance(dt, cmdExtend && false)` (commande effective forcée faux) ;
  3. convoyeur : `ConveyorSlip` ⇒ `_pallets.Advance(dt, conveyorRunning: false, …)` **mais**
     `_conveyor.Advance(...)` inchangé (KM1_AUX suit toujours la commande) ;
  4. encoder les vrais bits `ret` comme aujourd'hui ;
  5. **appliquer les `ActiveStucks`** : pour chaque (compId, sigName, mode), résoudre le `Signal`
     et forcer son bit dans `ret` (`Low`⇒0, `High`⇒1) **après** l'encodage ;
  6. si `RetFrozen` : **ne pas** incrémenter `_heartbeat`, **ne pas** `PublishReturns` (le
     datastore garde ses derniers `ret` — heartbeat figé). La physique (2-3) tourne quand même.
- **Résolution des signaux stuck** : `CarrouselSimulation` construit au constructeur un
  `Dictionary<(string compId,string name),Signal>` des signaux `ret` TOR (parcours de
  `pivot.Components`), pour l'étape 5. Ne pas garder tout le pivot ; juste cette table.
- **Id de composant** : ajouter `Id` à `CylinderUnit` (déjà connu via `FromPivot`) et mémoriser
  `_conveyorId = km1.Id` (= "conveyor") pour les lectures `GetPhysical`.
- `FaultSet` est un **objet mutable partagé** (l'UI le mute entre deux ticks, sur le même thread
  principal) : pas de verrou (contrairement au datastore, aucun thread serveur n'y touche).

## Definition of Done (cochable)
- [x] `FaultSet`, `FaultCommand`, `FaultCatalog` créés dans `CarrouselCore`, compilent.
- [x] `CarrouselSimulation.Faults` exposé ; `Tick` applique les 3 familles selon l'ordre ci-dessus.
- [x] **Nominal préservé** : sans aucun défaut, `Tick` produit exactement les mêmes `ret` qu'avant.
- [x] Tests xUnit (`FaultSetTests`) couvrant :
  - [x] vérin `CylinderStuckRetracted` : commande extend mais `S12` reste 0 / `S11` reste 1 ;
  - [x] vérin `CylinderStuckMidStroke` : position gelée entre 2 ticks (ni sortie ni rentrée) ;
  - [x] convoyeur `ConveyorSlip` : `KM1_AUX`=1 (marche confirmée) mais palettes/`B1`/`B2` figés ;
  - [x] capteur-bloqué `High`/`Low` sur `S12`, sur `B1`, sur `ret_running` (KM1_AUX collé) ;
  - [x] `RetFrozen` : heartbeat et bits `ret` du datastore inchangés sur plusieurs ticks ;
  - [x] `ClearComponent` / `SetPhysical(None)` restaurent le nominal ;
  - [x] `FaultCatalog.ApplicableTo` : cylinder → 2 physiques + stuck×2 par S11/S12 ; sensor → stuck×2
    sur ret_active ; conveyor → patine + stuck×2 sur ret_running.

## Banc attendu — **re-figé**
xUnit passe de **95** à **95 + N** (N = tests de défaut ajoutés). Raison : nouveaux scénarios de
défaut sur modèles purs (prévu par l'amorce, pas une régression). **4 pytest full-chain inchangés**
(aucun défaut actif dans les fixtures). Annoncer le nouveau total dans le rapport.

## Vérif autosuffisante
`dotnet test runtime/tests/CarrouselCore.Tests.csproj` → vert, total = 95+N.
`pytest testbench/test_modbus_chain.py` → 4 verts (inchangé).

## Ce qu'il NE faut PAS faire
- **Aucune** modification de `ModbusDataStore` ni de `ModbusServer` (le masque capteur reste
  dans la sim ; le datastore n'est jamais forcé — invariant D-016).
- Aucun déclenchement automatique/scénarisé de défaut (l'humain seul force ; « l'app sert »).
- Pas de gel partiel du heartbeat avec `ret` non figé (le gel est **tout ou rien** sur `ret`).
- Ne pas toucher au pivot, à l'Arch A, à la cadence 10 Hz.
- Ne pas introduire la **déconnexion TCP** ici (S5.4) : `RetFrozen` seul côté sim.

## Dépendances / validation manuelle
- **Dépendances** : aucune (premier sous-sprint).
- Pas de validation Godot ici (headless). La preuve est xUnit.

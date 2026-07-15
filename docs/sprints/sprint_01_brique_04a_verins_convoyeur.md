# Sprint 1 · Brique 4a — Vérins + convoyeur (KM1_AUX) + heartbeat  ✅ LIVRÉE 2026-07-15

> ✅ **Livrée** : 63 verts C# (60 core + 3 serveur) + **4 scénarios pytest full-chain verts**.
> Détails : `docs/journal.md` (2026-07-15) et `NOTES_sprint_01.md §4`.

> **But de ce fichier** : reprendre la brique **4a** à froid. Issue du **re-découpage** de la
> brique 4 (décidé à l'archi le 2026-07-15) : 4a = le tronçon qui **débloque les 4 scénarios
> pytest full-chain**, 4b = palettes/accumulation/présence (voir `sprint_01_brique_04b_palettes.md`).
> Contexte : `CLAUDE.md` · décisions : `docs/memory.md` · pivot : `pivot/machine_carrousel.json`
> · loader : `runtime/core/PivotModel.cs` · datastore : `runtime/core/ModbusDataStore.cs`
> · serveur : `runtime/server/ModbusServer.cs`.
> Entrée précédente : brique 3 (serveur FluentModbus ↔ datastore, commit `b34524e`).

## Où on en est (attendu à l'ouverture de 4a)

- Briques 1-3 livrées : loader + datastore + serveur FluentModbus. Transport FC3/FC16 +
  endianness big-endian prouvés **in-process**. **34 verts** xUnit (31 core + 3 intégration serveur).
- **4 scénarios pytest full-chain TOUJOURS EN SKIP** (`testbench/test_modbus_chain.py`) :
  heartbeat, KM1_AUX, vérin_1 (S11/S12), vérin_2 (S21/S22). **Aucun ne teste les palettes/B1/B2.**
  → C'est **exactement** ce que 4a doit débloquer.
- Le pivot **ne change pas** : tous les params machine y sont déjà (Phase 0). 4a **étend le loader**
  pour les lire (additif, **D-d**), sans toucher aux résolutions d'adresses.

## Objectif de 4a

Le « cerveau » minimal : heartbeat + vérins monostables + recopie contacteur convoyeur. À chaque tick :
1. `cmd = store.SnapshotCommands()` ;
2. `Advance(dt)` des modèles purs (vérins, convoyeur) ;
3. incrément heartbeat ;
4. reconstruction complète de `ushort[] ret` + `store.PublishReturns(ret)`.

Le câblage temporel (qui appelle le tick) est fait par un **hôte headless `SimHost`** (console .NET
pur, **zéro Godot**) : `PullCommands → sim.Tick → PushReturns` cadencé ~100 ms. pytest s'y connecte
sur le 502. Le nœud Godot `_PhysicsProcess` (brique 5) réutilisera ces **3 mêmes appels**.

## Ce que 4a modélise (depuis le pivot, jamais en dur)

- **Vérins `YV1`/`YV2` (monostables)** : position `0→1`, vitesse constante `1/travel_time_ms`.
  `cmd_extend=1` → va vers 1 ; `=0` → va vers 0 (rappel ressort). **Inversion mi-course = gratuite**
  (on va toujours vers la cible depuis le point courant, clampé [0,1] — aucun cas spécial).
  Capteurs à seuils : `ret_retracted` (S11/S21) si pos `< retracted_threshold` (0.02) ;
  `ret_extended` (S12/S22) si pos `> extended_threshold` (0.98). `IsEngaged` (pos `> block_threshold`
  0.10) exposé pour 4b, non utilisé en 4a.
- **Convoyeur `KM1`** : `KM1_AUX` (`ret_running`) = **recopie de `cmd_run` après `feedback_delay_ms`**
  (50 ms). En 4a, le convoyeur ne modélise **que** ce retour contacteur (le mouvement des palettes = 4b).
- **Heartbeat** : mot 0 de `ret`, `+1` par tick, **rollover `ushort` naturel** (65535 → 0).
- **B1/B2** : laissés à **0** en 4a (pas encore de palettes) — sans impact sur les 4 pytest.

## Contrat d'API (validé à l'archi)

```csharp
// PivotModel.cs — additif
public void Signal.WriteBit(ref ushort word, bool value);      // symétrique de ReadBit ; PivotException si non-TOR
public IReadOnlyDictionary<string,double> Component.Params;    // params numériques scalaires du composant
public double Component.GetParam(string name);                 // défensif : PivotException si absent

// Modèles purs (CarrouselCore, zéro Godot, dt injecté)
public sealed class CylinderState {
    CylinderState(double travelTimeS, double retracted, double extended, double block);
    void   Advance(double dt, bool cmdExtend);
    double Position { get; }  bool IsRetracted { get; }  bool IsExtended { get; }  bool IsEngaged { get; }
}
public sealed class ConveyorState {
    ConveyorState(double feedbackDelayS);
    void Advance(double dt, bool cmdRun);
    bool IsRunning { get; }
}

// Composition root (CarrouselCore)
public sealed class CarrouselSimulation {
    CarrouselSimulation(PivotModel pivot);
    void Tick(ModbusDataStore store, double dtSeconds);   // snapshot→advance→heartbeat→encode ret→publish
    // + accès lecture seule à l'état interne (positions vérins) pour la 3D (brique 5) et les tests
}
```

## Décisions de design (tranchées à l'archi 2026-07-15)

- **D-a** `dt` injecté, pas d'horloge interne (fonction pure du temps).
- **D-c** cinématique = petits modèles purs composés (`CylinderState`, `ConveyorState`), testables seuls.
- **D-d** extension **additive** de `PivotModel` (`Component.Params`), pas de second parseur.
- **D-e** reconstruction complète de `ret` chaque tick, heartbeat inclus, puis `PublishReturns`.
- **Q1 tranchée** : `Signal.WriteBit(ref ushort, bool)` ajouté (symétrique de `ReadBit`), additif.
- **Q3 tranchée** : re-découpage 4a/4b acté ; 4a porte les 4 pytest full-chain.
- **SimHost tranché** : nouvel hôte console headless pour débloquer pytest sans Godot.
- **Params tranché** : sac générique `double` (la sim extrait par nom), plus simple + additif.

## Definition of Done (4a)

- [x] `Signal.WriteBit` + `Component.Params`/`GetParam` — **34 verts intacts** + nouveaux tests params verts.
- [x] `CylinderState` : sortie après `travel_time`, rappel ressort, **inversion mi-course propre**.
- [x] `ConveyorState` : `KM1_AUX` recopie `cmd_run` après `feedback_delay_ms`.
- [x] `CarrouselSimulation.Tick` : encode S11/S12/S21/S22 + KM1_AUX + heartbeat dans `ret` ; B1/B2 = 0.
- [x] Heartbeat +1/tick, rollover 65535 propre.
- [x] `SimHost` écoute sur 502 et cadence le tick ~100 ms.
- [x] **Les 4 scénarios `testbench` full-chain FC3/FC16 verts** (lancer `SimHost` puis `pytest -v`).
- [x] Points de design dans `NOTES_sprint_01.md §4`. Orchestration à jour ; ce brief coché.

## Ordre de travail

1. `PivotModel.cs` : `Signal.WriteBit` + `Component.Params`/`GetParam` → tests additifs verts.
2. `CylinderState.cs` + `ConveyorState.cs` → tests xUnit isolés verts.
3. `CarrouselSimulation.cs` → test xUnit (bits dans `ret`, heartbeat, rollover).
4. `runtime/simhost/` (projet console) → build + écoute 502.
5. Lancer `SimHost`, `pytest -v` → **4 scénarios verts**.
6. `NOTES §4` + orchestration. Brique suivante : **4b** (`sprint_01_brique_04b_palettes.md`).

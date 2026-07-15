# Sprint 2 · Sous-sprint S2.1 — La scène devient hôte Modbus (boucle + garde-fous, sans visuel)

> **Amorce autosuffisante** (reprise à froid après `/clear`). Archi figée le 2026-07-15.
> Overview du sprint : `sprint_02_cinematique_visuelle.md`. Contexte : `CLAUDE.md` ·
> décisions : `docs/memory.md` · pivot : `pivot/machine_carrousel.json`.

## Objectif

Faire de `CarrouselScene` un **hôte Modbus vivant** — le même enchaînement que `SimHost`,
mais piloté par `_PhysicsProcess` — **sans toucher au visuel**. À la fin de ce sous-sprint, la
scène lancée en `--headless` se comporte exactement comme `SimHost` vue du réseau : heartbeat qui
bat, retours qui répondent aux commandes. Le mapping 3D (tiges/palettes) est le sous-sprint **S2.2**.

## Contrat d'API visé (`CarrouselScene`, extension additive)

- Champs : `ModbusDataStore _store`, `CarrouselSimulation _sim`, `ModbusServer _server`,
  `double _accumulator` ; constantes `periodS` (= `pivot.HeartbeatPeriodMs/1000`), `MaxCatchup` (=5).
- `[Export] bool BindLoopback = false` : défaut → `IPAddress.Any` (M580 distant) ; `true` →
  `IPAddress.Loopback` (test local sans exposer 502). Lu par Godot avant `_Ready`.
- `_Ready()` : **après** la construction géométrique (brique 5, intacte), instancier
  `store/sim/server` depuis le pivot déjà chargé, puis `server.Start()` (bind selon `BindLoopback`).
- `_PhysicsProcess(double delta)` : accumulateur à pas fixe (voir squelette overview) —
  `_accumulator = min(_accumulator + delta, MaxCatchup*periodS)` ; `while _accumulator >= periodS
  && ticks < MaxCatchup { StepSim(periodS); _accumulator -= periodS; ticks++ }` ; puis
  `ApplyToScene()`.
- `StepSim(double dt)` **privé** : `server.PullCommands()` → `_sim.Tick(_store, dt)` →
  `server.PushReturns()`. Parenté `SimHost` lisible, **sans** abstraire (2 sites, cf. dette B).
- `ApplyToScene()` **privé** : **stub vide ce sous-sprint** (corps livré en S2.2).
- `_ExitTree()` : `_server.Dispose()` (libère le port 502).

## Décisions pré-tranchées (détail dans l'overview D-a..D-f)

- `dt` **fixe** = `periodS` (déterministe) ; heartbeat à 10 Hz exact.
- Garde-fous heartbeat : **(1)** `project.godot` → `run/low_processor_mode=false` (versionné) ;
  **(2)** anti-spirale clamp accumulateur **+** guard `ticks < MaxCatchup`.
- Frontière Arch A : `_PhysicsProcess` = thread principal Godot = seul à toucher datastore **et**
  scene tree ; serveur sur son thread, `Pull/Push` sous `server.Lock` (contrainte POC D-001).

## Questions ouvertes résiduelles

Aucune (toutes figées à la conception). Signaler tout écart constaté au build.

## Definition of Done (cochable)

- [x] `project.godot` : `run/low_processor_mode=false` présent dans `[application]`.
- [x] `_Ready` démarre le serveur (bind selon `BindLoopback`), `_ExitTree` le dispose.
- [x] `_PhysicsProcess` : accumulateur pas-fixe + clamp + guard ; `StepSim` = pull→tick→push.
- [x] `ApplyToScene` existe en **stub vide** (aucun accès scene tree encore).
- [x] Builders brique 5 **intacts** (relecture du diff).
- [x] `SimHost` **non modifié**.
- [x] Vérif de bout en bout (ci-dessous) **verte en session** — contribue à solder D-010.

## Vérif autosuffisante (Godot 4.6 mono **disponible en session** — cf. mémoire `godot-executable`)

Exécutable headless : `C:\Users\Nicol\Documents\Godot_v4.6-stable_mono_win64\Godot_v4.6-stable_mono_win64_console.exe --headless --path runtime`.

1. `dotnet build` de l'assembly Godot → **0 erreur** (usage API 4.6 + lien core validés).
2. `dotnet test` (core + serveur) → **non régressé**.
3. **Scène ≡ `SimHost`** : arrêter `SimHost` ; lancer la **scène** en `--headless` avec
   `BindLoopback=true` (loopback, 502) ; rejouer `pytest testbench/test_modbus_chain.py -v` contre
   cet hôte → **4 passed** (heartbeat +1/100 ms, `cmd_run`→KM1_AUX, `cmd_extend`→S12, rappel ressort).
   Prouve la boucle `_PhysicsProcess` + garde-fous heartbeat en conditions réelles.
4. Non-régression : relancer `SimHost`, les 4 pytest full-chain restent verts contre lui aussi.

## Dépendances

**Aucune** — premier sous-sprint du sprint 2.

## Fichiers touchés

- `runtime/project.godot`
- `runtime/scenes/CarrouselScene.cs`

> ⚠ Partage `CarrouselScene.cs` avec **S2.2** → les deux sous-sprints sont **séquentiels** (S2.1 puis S2.2).

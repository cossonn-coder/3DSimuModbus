# Sprint 2 · Sous-sprint S2.2 — Animation visuelle (ApplyToScene) + smoke-test

> **Amorce autosuffisante** (reprise à froid après `/clear`). Archi figée le 2026-07-15.
> Overview du sprint : `sprint_02_cinematique_visuelle.md`. Contexte : `CLAUDE.md` ·
> décisions : `docs/memory.md` · pivot : `pivot/machine_carrousel.json`.

## Où on en est (à l'ouverture de S2.2)

- **S2.1 livré** : `CarrouselScene` est déjà un hôte Modbus (boucle `_PhysicsProcess`/`StepSim`,
  serveur, garde-fous). `ApplyToScene()` existe mais est un **stub vide**. La scène passe les 4
  pytest full-chain en headless (≡ `SimHost`), **sans aucun mouvement visuel**.
- S2.2 = donner vie à `ApplyToScene()` : recopier l'état de la sim sur les transforms, et **solder
  D-010** (le smoke-test headless jamais exécuté depuis la brique 5).

## Objectif

Recopier, à chaque frame, l'état lu sur `_sim` vers les nœuds construits en brique 5 (snap 10 Hz).
Aucune modification de la boucle S2.1 ni du core.

## Contrat d'API visé (`CarrouselScene`, extension additive)

- **Capture des refs au build** (dans `BuildCylinder`/`BuildPallet`, sans changer leur géométrie) :
  `_rod1`, `_rod2` (nœuds `rod`) + pour chacun `restY` (leur `Position.Y` initial) et `stroke` ;
  `_pallets[]` (les `MeshInstance3D` des palettes, dans l'ordre des indices).
- **`ApplyToScene()`** (corps, remplace le stub) — mapping **lecture seule** de `_sim` :
  - Tiges : `_rod_i.Position = new Vector3(x, restY_i + (float)Cylinder_i.Position * stroke_i, z)`
    (translation +Y ; `x`/`z` inchangés). `Cylinder1`/`Cylinder2` exposent `.Position` (0..1).
  - Palettes : pour `i`, `angle = _sim.Pallets.AnglesDeg[i]` →
    `_pallets[i].Position = OnCircle(angle, radius, center, y)` (**même** helper que brique 5) et
    `_pallets[i].RotationDegrees = new Vector3(0, (float)angle, 0)` (alignement cosmétique).
  - `radius`/`center`/`y` : réutiliser les valeurs déjà calculées en `_Ready` (les mémoriser au
    build si besoin, comme les refs).

## Décisions pré-tranchées (détail dans l'overview D-d)

- **Snap 10 Hz** : recopie directe de l'état courant, pas d'interpolation (non-lock-in : ajoutable
  en V2 via snapshots prev/current, sans toucher la sim). Adossé D-003.
- Réutiliser `OnCircle` (repère brique 5) → cohérence de repère **garantie** (CCW, postes 90°/270°).
- Aucun accès datastore ici : `ApplyToScene` ne lit que l'état **déjà publié** par `_sim` (thread
  principal, cf. Arch A).

## Questions ouvertes résiduelles

Aucune. Signaler tout écart de repère constaté à la validation visuelle.

## Definition of Done (cochable)

- [x] Refs `_rod1/_rod2` (+`restY`/`stroke`) et `_pallets[]` capturées au build (zéro `GetNode` par frame).
- [x] `ApplyToScene()` recopie tiges (translation +Y) et palettes (`OnCircle` + `RotationDegrees`).
- [x] Builders brique 5 **intacts** hormis la capture des refs (relecture du diff).
- [x] Core, serveur, `SimHost`, boucle S2.1 **non modifiés**.
- [x] `smoke_anim.ps1` écrit et **vert** (rod1Y 0.125→0.275, pallets 0,120,240→90,50,70, heartbeat 0→211).
- [x] Les 4 pytest full-chain restent verts (non-régression de S2.1) — 4 passed contre SimHost.

## Vérif autosuffisante (Godot 4.6 mono **disponible en session** — cf. mémoire `godot-executable`)

Exécutable headless : `C:\Users\Nicol\Documents\Godot_v4.6-stable_mono_win64\Godot_v4.6-stable_mono_win64_console.exe --headless --path runtime [--quit-after N]`. `smoke_anim.ps1` peut se modeler sur `smoke_scene.ps1` (brique 5).

1. `dotnet build` assembly Godot → **0 erreur**.
2. **Smoke-test headless** `runtime/scripts/smoke_anim.ps1` : lance la scène `--headless`, force via un
   petit client Modbus `cmd_run=1` puis `cmd_extend=1` (YV1), tourne N frames, puis **asserte** :
   la tige `rod` de `cylinder_1` a monté (Y a augmenté), au moins une palette a avancé (angle
   changé), heartbeat incrémenté. Sortie 0 + assertions vertes. **Solde D-010** (couvre aussi le
   recensement statique brique 5 : `ring=1 cylinders=2 pallets=3 sensors=2`).
3. **Validation visuelle** (éditeur/desktop) : rotation **CCW** vue de dessus, postes 90° (fond) /
   270° (devant), tige qui **monte** à l'extension et redescend au rappel ressort, palettes qui
   **s'accumulent** derrière un vérin engagé (`min_gap` respecté).

## Dépendances

**S2.1** (scène-hôte Modbus). Partage `CarrouselScene.cs` → **séquentiel** après S2.1.

## Fichiers touchés

- `runtime/scenes/CarrouselScene.cs`
- `runtime/scripts/smoke_anim.ps1`

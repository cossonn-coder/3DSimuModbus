# Sprint 1 · Brique 5 — Scène 3D statique (génération procédurale depuis le pivot)

> **But de ce fichier** : reprendre la brique 5 **à froid**. Rédigé **pendant la conception** du
> sprint (convention 2026-07-15) : **provisoire**, à re-valider à l'étape archi quand son tour vient.
> Contexte : `CLAUDE.md` · décisions : `docs/memory.md` · pivot : `pivot/machine_carrousel.json`.

## Où on en est (attendu à l'ouverture de la brique 5)

- Briques 1-4 livrées : la chaîne Modbus tourne bout-en-bout, la cinématique produit des retours
  plausibles (vérins, palettes, capteurs, heartbeat). Tout ça **sans aucun visuel**.
- Brique 5 = **la maquette 3D**, mais **statique** pour ce sprint : afficher la géométrie à sa place,
  pas encore l'animer. L'animation (lier les positions au datastore/à la sim) est **sprint 3**
  (`docs/backlog.md`, Phase 1bis). Objectif ici : voir la machine, valider les proportions du pivot.

## Objectif de la brique

Générer **procéduralement** la scène Godot 4 depuis `machine_carrousel.json` (aucun asset externe
en V1) :
- **Convoyeur** : anneau (`render.kind = ring`, `inner_radius_m` 1.3 / `outer_radius_m` 1.7, `height_m` 0.05).
- **2 vérins bloqueurs** aux postes 90° et 270° : corps + tige (positions/course depuis `station_angle_deg`,
  `stroke_m`). Tige en position **rentrée** (statique).
- **3 palettes** : boîtes `size_m` [0.4, 0.1, 0.4] aux `initial_positions_deg` [0, 120, 240] sur le
  cercle (`radius_m` 1.5, centre, sens `ccw`).
- **Zones capteurs** (`B1`/`B2`) : volumes **semi-transparents** matérialisant `window_deg` autour des postes.

C'est **la seule brique qui dépend de Godot** : pas de test « hors Godot ». Validation = **smoke-test
headless** + inspection visuelle.

## Décisions de design pré-tranchées (provisoires)

- **D-a — Tout est construit en C# à `_Ready`** depuis le `PivotModel` : positions, rayons, tailles
  viennent du pivot. Aucune coordonnée en dur dans la scène. *Raison* : le pivot est le contrat central ;
  la 3D doit refléter n'importe quel pivot valide.
- **D-b — Géométrie primitive uniquement** (`CylinderMesh`, `BoxMesh`, `TorusMesh`/anneau construit,
  `MeshInstance3D`). Pas d'import, pas de `CSG` complexe si un mesh primitif suffit. *Raison* : « simplicité
  d'abord », zéro asset (memory.md).
- **D-c — Statique ce sprint.** Aucune liaison au datastore ni `_PhysicsProcess` visuel ici. *Raison* :
  découpe nette avec la cinématique visuelle (sprint 3). On ne mélange pas « voir la machine » et « animer la machine ».
- **D-d — Conversion repère** : le pivot est en mètres, angles en degrés, cercle dans le plan
  (X,Z) probablement (sol horizontal Godot). **À figer à l'archi** : mapping angle→(x,z), sens `ccw`,
  origine 0°. *Reco* : `x = r·cos θ`, `z = −r·sin θ` (ccw vu de dessus), 0° sur +X — **à valider visuellement**.

## Questions ouvertes (à trancher à l'archi de la brique)

1. **Repère et orientation** (D-d) : confirmer le plan du carrousel (sol X-Z) et le sens de `θ`.
   Impact direct sur la position des postes 90°/270° et des palettes. **À trancher tôt, c'est structurant.**
2. **Le futur binding sim→3D** (sprint 3) doit pouvoir réutiliser cette scène : prévoir des **nœuds
   nommés/adressables** (par `id`/`tag` du pivot) pour que l'animation les retrouve. **Reco : nommer chaque
   nœud d'après l'`id` du composant** (`cylinder_1`, `pallet_0`…) dès maintenant, sans coût.
3. **Smoke-test headless** : Godot 4 permet `--headless --quit-after N` pour vérifier que la scène se
   construit sans erreur. **Reco : un script de smoke-test** (« la scène charge, N nœuds attendus créés »)
   plutôt qu'une validation purement visuelle manuelle.
4. **Caméra/éclairage minimal** pour l'inspection : hors périmètre strict, mais nécessaire pour « voir ».
   **Reco : caméra + lumière par défaut simples**, non paramétrées par le pivot.

## Definition of Done (brique 5) — provisoire

- [x] Scène générée **procéduralement** depuis le pivot à `_Ready` (aucune coordonnée en dur).
- [x] Anneau convoyeur + 2 vérins (corps+tige, rentrée) + 3 palettes aux bonnes positions + zones B1/B2 semi-transparentes.
- [x] Nœuds nommés d'après les `id` du pivot (prépare le binding sprint 3).
- [~] Smoke-test headless : script `runtime/scripts/smoke_scene.ps1` **écrit** ; **non exécuté** (Godot absent du poste) → **D-010**. Assembly compile.
- [x] Repère angle→(x,z) figé et documenté dans `NOTES_sprint_01.md §6` (le §5 était déjà pris par la brique 4b).
- [x] Orchestration à jour ; ce brief coché. **Clôture du sprint 1** faite (2026-07-15).

## Ordre de travail

1. **Archi avant code** : figer le repère (Q1), le nommage des nœuds (Q2), le mode de smoke-test (Q3).
2. Générer le script de scène C# (fichier par fichier) : anneau → vérins → palettes → zones capteurs.
3. Smoke-test headless vert + capture visuelle.
4. `NOTES §5` + clôture sprint 1 (`/sprint`) : journal, memory, dettes, backlog, réorganisation.

> Après cette brique, le sprint 1 est **complet côté runtime statique**. Restent, hors sprint 1 :
> le **diff canonique formel Python↔C#** des loaders (backlog Phase 1) et la **validation M580 réelle**
> (Phase 4). La cinématique **visuelle** (animer la 3D depuis la sim) est le **sprint 3**.

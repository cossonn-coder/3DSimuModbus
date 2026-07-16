# Sprint 2 — Cinématique visuelle (animer la 3D depuis la simulation)

> **But de ce fichier** : reprendre le sprint 2 **à froid** (après `/clear`). Rédigé pendant la
> conception (2026-07-15), **archi validée** avec Nico — ce n'est plus provisoire.
> Contexte : `CLAUDE.md` · décisions : `docs/memory.md` · pivot : `pivot/machine_carrousel.json`.

## Où on en est (à l'ouverture du sprint 2)

- Sprint 1 clos : la chaîne Modbus tourne bout-en-bout (`SimHost` headless, 4 pytest full-chain verts),
  la cinématique produit des retours plausibles (vérins, palettes, capteurs, heartbeat), et la
  **maquette 3D existe mais est STATIQUE** (brique 5 : `runtime/scenes/CarrouselScene.cs`).
- **Renumérotation** : l'ex-« sprint 3 » (cinématique visuelle) devient le **sprint 2** — la 3D
  statique, initialement prévue en sprint 2, a été livrée en sprint 1 (brique 5).
- **Prérequis matériel confirmé** : poste avec **Godot 4.6 .NET** disponible → le sprint est
  **validable end-to-end** et **solde la dette D-010** (smoke-test brique 5 jamais lancé).

## Objectif du sprint

Faire **vivre** la maquette : à chaque frame physique Godot, rejouer la boucle Modbus
(`PullCommands → Tick → PushReturns`, patron `SimHost`) **cadencée sur `period_ms`**, puis
recopier l'état de la simulation sur les transforms des nœuds construits en brique 5 :
- **tiges de vérin** : `rod.Position.Y = restY + Cylinder_i.Position × stroke_i` (translation +Y) ;
- **palettes** : `OnCircle(Pallets.AnglesDeg[i], radius, center, y)` + `RotationDegrees.Y` (mêmes
  helpers/repère que la brique 5 → cohérence garantie).

C'est de la **glue Godot pure** : non testable en xUnit (frontière D-006). Validation = smoke-test
anim headless + inspection visuelle.

## Décomposition en sous-sprints (orchestrés par `/sprint open 02`)

Coupe nette **fonctionnel/headless vs présentation/visuel** (même philosophie que sprint 1 :
4a fonctionnel / 5 visuel). Les deux partagent `CarrouselScene.cs` → **séquentiels**.

| Sous-sprint | Amorce | Contenu | Fichiers | Dépend de |
|---|---|---|---|---|
| **S2.1 — Scène-hôte Modbus** | `sprint_02_brique_01_scene_hote.md` | Boucle `_PhysicsProcess`/`StepSim` + serveur + `BindLoopback` + garde-fous ; `ApplyToScene` = stub vide | `project.godot`, `CarrouselScene.cs` | — |
| **S2.2 — Animation + smoke** | `sprint_02_brique_02_animation.md` | Refs au build + `ApplyToScene` (mapping tiges/palettes) ; solde **D-010** | `CarrouselScene.cs`, `smoke_anim.ps1` | S2.1 |

**Vérif de bout en bout** : S2.1 prouve que la scène ≡ `SimHost` (4 pytest full-chain en headless) ;
S2.2 prouve le rendu (smoke anim + visuel CCW). Chaque sous-sprint est confié à un **sous-agent en
contexte vierge** (cold-start depuis son amorce), enchaînés sans interruption par l'orchestrateur.

## Contrat pivot

**Aucune évolution.** Tout est déjà dans `machine_carrousel.json` depuis la Phase 0 :
`heartbeat.period_ms` (100), `stroke_m` (0.15), `speed_deg_per_s` (20), géométrie `kinematics`/`render`,
`modbus.port`/`unit_id`. La règle « le pivot d'abord » est honorée par la négative.

## Contrats consommés (lecture seule — rien à ajouter au core)

| Besoin | Source | Type |
|---|---|---|
| Cadence tick | `heartbeat.period_ms = 100` (pivot) | int |
| Position tiges | `Cylinder1.Position`, `Cylinder2.Position` | double 0..1 |
| Course tige | `cylinder_*.params.stroke_m = 0.15` (pivot) | double |
| Angles palettes | `Pallets.AnglesDeg` | `IReadOnlyList<double>` |
| Vitesse palettes | `KM1.params.speed_deg_per_s = 20` (pivot) | double |
| État convoyeur | `Conveyor.IsRunning` | bool |
| Heartbeat | `Heartbeat` (incrémenté dans `Tick`) | ushort |
| Port / unit_id | `modbus.port` / `modbus.unit_id` (pivot) | int |
| Géométrie | `kinematics.*` + `render.*` (déjà consommés brique 5) | — |

## Squelette de la boucle (référence, pas l'implémentation finale)

```
_PhysicsProcess(delta):
    _accumulator = min(_accumulator + delta, MaxCatchup * periodS)   // anti-spirale (clamp)
    ticks = 0
    while _accumulator >= periodS and ticks < MaxCatchup:            // anti-spirale (guard)
        StepSim(periodS)          // pull -> sim.Tick(store, periodS) -> push, sous server.Lock
        _accumulator -= periodS
        ticks += 1
    ApplyToScene()                // chaque frame : recopie l'etat courant (snap 10 Hz)
```

## Décisions de design (validées avec Nico, 2026-07-15)

- **D-a — Hôte = `CarrouselScene` étendu.** C'est déjà le seul pont Godot↔core ; il détient toutes
  les constantes géométriques et tous les nœuds cibles. On y ajoute le runtime (datastore + sim +
  serveur + boucle). Les builders de la brique 5 restent **intacts** (modif chirurgicale). Un nœud
  `CarrouselRuntime` séparé forcerait à repasser pivot + refs et à gérer l'ordre `_Ready` → zéro
  bénéfice à ce stade.
- **D-b — Corps de boucle isolé dans `StepSim()`** (privé) : `PullCommands → sim.Tick(store, periodS)
  → PushReturns`. Garde la parenté avec `SimHost` lisible **sans abstraire** (on n'abstrait pas pour
  2 sites — le core ne peut pas voir le serveur ni Godot). La duplication du câblage runtime avec
  `SimHost` est assumée (candidate **D-011** si un 3ᵉ hôte apparaît).
- **D-c — Boucle à pas fixe (accumulateur).** Le heartbeat est incrémenté *dans* `Tick` et le pivot
  impose `period_ms = 100` → `Tick` **doit** tourner à 10 Hz, pas à 60. `_PhysicsProcess(delta)` :
  `accumulator += delta` ; tant que `accumulator ≥ periodS` → `StepSim()` avec **`dt = periodS` FIXE**
  (déterministe) ; `ApplyToScene()` chaque frame. Divergence avec `SimHost` (`dt` réel) → **NOTES
  seulement**, on ne touche pas `SimHost` (harmonisation éventuelle = pas fixe côté SimHost, hors
  périmètre).
- **D-d — Rendu par « snap » 10 Hz.** `ApplyToScene` recopie l'état courant tel quel. Simple,
  correction fonctionnelle prioritaire pour une démo automaticien. **Non-lock-in** : l'interpolation
  60 Hz (snapshots prev/current lerpés) est purement présentation, ajoutable en V2 sans toucher la
  sim. Adossé **D-003** (« pas de rampe »).
- **D-e — Serveur, bind configurable.** Propriété exportée `[Export] bool BindLoopback = false`
  (miroir du flag `--any` de `SimHost`, choisi **sans recompiler**, lu par Godot avant `_Ready`) :
  défaut → `IPAddress.Any` (cible = M580 distant ; règle pare-feu Windows TCP 502 déjà prévue,
  `memory.md`) ; `true` → `IPAddress.Loopback` pour tester en local sans exposer le port 502.
  `server.Start()` en `_Ready`, `server.Dispose()` en `_ExitTree` (libère le port proprement).
  Frontière Arch A : `_PhysicsProcess` = thread principal Godot = le seul à toucher datastore **et**
  scene tree ; le serveur écoute sur son thread, `Pull/Push` sous `server.Lock` (contrainte POC
  D-001, déjà imposée à `ModbusServer`). *Effet de bord attendu* : défaut `false` = `Any` =
  dialogue pare-feu Windows au 1er lancement.
- **D-f — Garde-fou anti-gel du heartbeat.** Le M580 détecte une sim figée via l'incrément du
  heartbeat ; or celui-ci vit dans `_PhysicsProcess`. Deux protections :
  1. `run/low_processor_mode=false` **explicite** dans `project.godot` (défaut déjà `false`, mais on
     le verrouille) — sinon la boucle ne tourne que sur événement d'entrée et le heartbeat gèle.
  2. **Anti-spirale (deux protections, ceinture+bretelles)** : clamp `accumulator = min(accumulator +
     delta, MaxCatchup×periodS)` **et** guard `ticks < MaxCatchup` dans le `while` (ex. `MaxCatchup = 5`).
     Le clamp borne déjà le nombre de ticks ; le guard reste comme filet si un refactor futur touche
     la boucle. Évite un **burst** de heartbeat après un stall / breakpoint / fenêtre débloquée.
  *Résiduel à surveiller* (candidate **D-011/D-012**) : throttling d'une fenêtre non-focus non
  entièrement maîtrisable côté OS. Repli possible en V2 si constaté : sim sur **thread dédié** (façon
  SimHost intégré) + verrou sur l'état visuel lu par `ApplyToScene` — **écarté ce sprint** (casse la
  propriété « même thread », ajoute un lock, complexité non justifiée pour une démo).

## Points durs / incertitudes

1. **Validation = Godot obligatoire** (glue pure). Levé par le prérequis matériel confirmé (Q6).
2. **Gel du heartbeat** (D-f) — traité par settings + clamp ; résiduel throttling fenêtre à observer.
3. **Steppiness du snap** (D-d) — tige de vérin visible par ~5 paliers ; jugé acceptable pour la démo,
   interpolation différée sans coût de lock-in.

## Plan de livraison fichier par fichier (Nico code en SSH mobile)

| # | Fichier | Action | Validation |
|---|---|---|---|
| 1 | `runtime/project.godot` | Ajouter `run/low_processor_mode=false` (section `[application]`). | Relecture ; scène tourne en continu. |
| 2 | `runtime/scenes/CarrouselScene.cs` | Étendre : champs runtime + `[Export] bool BindLoopback` + refs capturées au build (`_rod1/_rod2` + `restY`/`stroke`, `_pallets[]`) ; `_Ready` (start serveur, bind selon `BindLoopback`) ; `_PhysicsProcess` (accumulateur pas-fixe + clamp + guard) ; `StepSim()` ; `ApplyToScene()` ; `_ExitTree` (dispose). **Builders brique 5 intacts.** | `dotnet build` assembly Godot **compile** (0 erreur). Relecture : builders non modifiés. |
| 3 | `runtime/scripts/smoke_anim.ps1` | Godot `--headless` : forcer `cmd_run`/`cmd_extend` via un petit client, tourner N frames, asserter qu'une tige a bougé et une palette a avancé, heartbeat incrémenté. | Sortie 0 + assertions vertes. **Solde D-010** (+ smoke statique brique 5). |
| 4 | Validation manuelle | Lancer la scène, piloter les bits (io_scanner_sim / pytest en parallèle). | Rotation **CCW**, postes 90°/270°, tige monte à l'extension, palettes s'arrêtent derrière un vérin engagé, heartbeat vivant. |
| 5 | `docs/notes/NOTES_sprint_02.md` + `/sprint` | Clôture : journal, memory, dettes, backlog. | — |

## Definition of Done

- [ ] `_PhysicsProcess` rejoue `StepSim()` à pas fixe `periodS`, `ApplyToScene` chaque frame (snap).
- [ ] Tiges de vérin et palettes suivent l'état de la sim (mêmes helpers/repère que la brique 5).
- [ ] Serveur `IPAddress.Any` démarré en `_Ready`, disposé en `_ExitTree`.
- [ ] Garde-fous heartbeat : `low_processor_mode=false` versionné + clamp accumulateur + guard `ticks`.
- [ ] Smoke-test anim headless vert → **D-010 soldée** ; assembly compile.
- [ ] Validation visuelle CCW + postes + extension tige + accumulation OK.
- [ ] `SimHost` **inchangé** ; les 4 pytest full-chain restent verts.
- [ ] NOTES sprint 2 + orchestration à jour ; cette amorce cochée.

## Ordre de travail

1. Fichier 1 (`project.godot`) → fichier 2 (`CarrouselScene`) → fichier 3 (smoke) → validation → NOTES.
2. Archi déjà figée : on passe directement à la génération fichier par fichier sur feu vert.

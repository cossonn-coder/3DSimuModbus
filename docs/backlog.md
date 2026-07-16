# backlog.md — Phases et tâches

Statuts : ✅ fait · 🔄 en cours · ⏳ à venir · 🧊 gelé (post-démonstrateur)

## Phase 0 — Modèle pivot ✅
- ✅ Spec fonctionnelle carrousel validée
- ✅ Table Modbus validée (2 zones, 2 lignes I/O Scanner FC3/FC16)
- ✅ `pivot/machine_carrousel.json` v0.2
- ✅ CLAUDE.md + orchestration + commandes

## Phase 1 — Chaîne Modbus de bout en bout 🔄
Brief : `docs/sprints/sprint_01/overview.md`
- ✅ Sprint 1 : datastore thread-safe + serveur Modbus + heartbeat + banc de test
  Python (émulateur I/O Scanner) + pytest **+ maquette 3D statique** (clos 2026-07-15)
  - ✅ Brique 1 : loader pivot C# (`PivotModel`) + class library `CarrouselCore` +
    xUnit `runtime/tests` (17 verts, symétrie avec pytest) — D-006 concrétisée
  - ✅ Brique 2 : `ModbusDataStore` (C# pur, `ushort[]` cmd/ret + verrou) + tests xUnit
    (28 verts au total) — transport de mots bruts, tailles tirées du pivot, snapshot copie
    défensive, publish/wire atomiques et défensifs. Amorce cochée.
  - ✅ Brique 3 : serveur FluentModbus branché (Arch A) + tests d'intégration in-process
    (34 verts au total) — pont big-endian, Pull/Push sous server.Lock. Amorce cochée.
  - ✅ Brique 4a : boucle de simulation vérins + convoyeur + heartbeat (cinématique scriptée
    pure : `CylinderState`, `ConveyorState`, `CarrouselSimulation`) + hôte headless `SimHost`.
    **63 verts** + les **4 scénarios pytest full-chain débloqués**. Amorce cochée.
    — amorce : `docs/sprints/sprint_01/brique_04a_verins_convoyeur.md`
  - ✅ Brique 4b : palettes + accumulation `min_gap_deg` + présence B1/B2 (`PalletSet`,
    modèle pur ; accumulation `mod 360` en espace « sens de marche », D-008 non créée). **82 verts**.
    — amorce : `docs/sprints/sprint_01/brique_04b_palettes.md`
  - ✅ Brique 5 : scène 3D **statique** procédurale depuis le pivot (anneau CSG, 2 vérins,
    3 palettes, fenêtres capteurs). Extension core additive (option A). **Core 87 verts**, assembly
    Godot compile ; smoke-test headless + conformité visuelle à confirmer (**D-010**).
    — amorce : `docs/sprints/sprint_01/brique_05_scene3d.md`
  - ⏳ Diff **canonique formel** Python↔C# des loaders (émetteur canonique côté Python
    à écrire ; `ToCanonical()` déjà présent côté C#) — parité par assertions communes
    en attendant *(reporté hors sprint 1)*
- ⏳ Validation croisée avec le M580 réel (2 lignes de scan dans Control Expert) *(Phase 4)*

## Phase 1bis — Cinématique 3D ✅
> **Note (2026-07-15)** : la **scène 3D statique**, initialement prévue en sprint 2, a été livrée
> en **sprint 1 (brique 5)** ; le **sprint 2** a donc porté la **cinématique visuelle** (ex-sprint 3).
- ✅ Sprint 2 : cinématique visuelle — la 3D est animée depuis la sim (clos 2026-07-15, validé Nico).
  `_PhysicsProcess` rejoue `PullCommands → Tick → PushReturns` (patron `SimHost`) à **pas fixe**
  (accumulateur, `Tick` 10 Hz), puis `ApplyToScene` recopie l'état sur les transforms (snap 10 Hz).
  Décomposé en 2 sous-sprints séquentiels (partageaient `CarrouselScene.cs`) :
  - ✅ S2.1 : scène-hôte Modbus (boucle `_PhysicsProcess`/`StepSim` + serveur + garde-fous heartbeat ;
    `ApplyToScene` stub). Build 0 erreur, 90 tests, scène ≡ SimHost (4 pytest). — `31ac4d5`
  - ✅ S2.2 : animation `ApplyToScene` (tiges +Y, palettes `OnCircle`) + `smoke_anim.ps1` vert ;
    **D-010 soldée**. — `8dc0c22`
  - ✅ `demo_sprint_02.ps1` (démo visuelle guidée, pré-vol port 502) → validation visuelle Nico. — `d4b2d71`
- ⏳ Reste éventuel : diff canonique Python↔C#, polish visuel, IHM debug minimale.

## Phase 1ter — Durcir le démonstrateur ✅
> Brief : `docs/sprints/sprint_03/overview.md`. Robustesse (échecs bruyants) + traçabilité de
> la chaîne de commande **par élément 3D**, avant le M580 réel. Lecture seule, pivot non touché.
- ✅ Sprint 3 : 3 sous-sprints séquentiels (orchestrés par `/sprint open 03`, clos 2026-07-16 ;
  validation visuelle Nico restante — F5 + `demo_sprint_03.ps1`). Banc **95 verts**.
  - [x] S3.1 — Backend santé (`ModbusServer` : bind visible + repro D-013, `IsListening`,
    `LastClientWriteUtc`, `SnapshotReturns`) — headless/xUnit. Banc re-figé 90→95. — `6ae64d5`
  - [x] S3.2 — Santé visible (bandeau bind + panneau santé ; **solde D-013**) — visuel. — `30e4f2b`
  - [x] S3.3 — Chaîne par élément + coloration + `demo_sprint_03.ps1` — visuel/observable. — `f1523f2`

## Phase 1quater — Ergonomie d'utilisation ✅
> Brief : `docs/sprints/sprint_04/overview.md`. Priorité n°1 de Nico (générique à N machines, cf.
> `CLAUDE.md` §0bis). Navigation 3D libre + panneau des éléments + surbrillance croisée + plein écran.
> **Lecture seule**, pivot inchangé, Arch A intacte, **banc inchangé** (95 + 4 pytest). Solde la partie
> **navigation** de D-015 (la vitesse réglable reste reportée).
- ✅ Sprint 4 : 4 sous-sprints séquentiels (orchestrés par `/sprint open 04`, clos 2026-07-16 ;
  validation visuelle Nico restante — F5 + `demo_sprint_04.ps1`). Banc **95 verts** (inchangé), build
  Godot 0 erreur, smoke `panel rows=5`. Dette née : **D-018** (nœuds vestigiaux). **D-015 nav soldée** :
  - [x] S4.1 — Présentation : caméra orbitale (CAO) + plein écran/résolution (`project.godot`) + F11. — `1912c6b`
    — amorce : `docs/sprints/sprint_04/brique_01_presentation.md`
  - [x] S4.2 — Panneau latéral (5 colonnes, peuplé du pivot) + relocalisation du décodage + dépose
    `CommandChainLabels` + coloration préservée + survol ligne→3D. — `37b9b7c`
    — amorce : `docs/sprints/sprint_04/brique_02_panneau.md`
  - [x] S4.3 — Picking 3D→panneau (Area3D + surbrillance symétrique). — `cc8aa9e`
    — amorce : `docs/sprints/sprint_04/brique_03_picking.md`
  - [x] S4.4 — `demo_sprint_04.ps1` + sortie observable (re-montre D-013). — `e33ed9c`
    — amorce : `docs/sprints/sprint_04/brique_04_demo.md`

## Phase 4 — Intégration M580 réelle ⏳
> Piste **parallèle et indépendante** (la chaîne Modbus est prête ; le collègue automaticien clone le
> repo et déroule). Numérotée « Phase 4 » — à ne pas confondre avec le **sprint 4** (ergonomie ci-dessus).
- ⏳ Campagne avec le programme PLC de l'automaticien, mesure de latence scan↔simulation, IHM minimale
  (état des mots, forçage basique de debug — rejoint D-016)

## Phases 2-3 — Pipeline d'extraction Python 🧊 (après démonstrateur)
- 🧊 DWG → DXF (ezdxf) → géométrie 2D → volumes basiques
- 🧊 Schéma électrique PDF → OCR/CV → composants + repères → mapping Modbus proposé
- 🧊 Mécanisme de relecture/correction de l'extraction
- 🧊 JSON Schema de validation du pivot (rembourse D-005)

## Phase 5 — IHM automaticien 🧊
- 🧊 Édition du mapping, diagnostic, forçage, visualisation des échanges en direct
  (à spécifier avec l'automaticien après le démonstrateur)

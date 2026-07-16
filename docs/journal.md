# journal.md — Chronologie du projet

Règle : une entrée par sprint (ou par session de conception significative), rédigée
en fin de sprint via `/sprint`. Format : date, objectif, ce qui a été fait, ce qui a
surpris, décisions prises (reportées dans memory.md), état des tests.

---

## 2026-07-16 — Ouverture Sprint 3 : durcir le démonstrateur (robustesse + traçabilité)

**Contexte** : sprints 1 & 2 clos, démonstrateur 3D vivant validé à l'œil. Conception du sprint 3
figée le 2026-07-16 (`docs/sprints/sprint_03/`, décisions D-Q1..D-Q4 + D-arch). Deux manques pour
une démo solide devant l'automaticien : robustesse muette (bind 502 silencieux, **D-013**) et
illisibilité (rien ne trace la chaîne %MW ↔ physique à l'écran).

**Objectif** : rendre les échecs **bruyants** et la chaîne de commande **lisible par élément 3D**,
avant le M580 réel (Phase 4). **Lecture seule** (zéro écriture `cmd`, forçage → D-016), **pivot non
touché**, **Arch A intacte**.

**Orchestration** (`/sprint open 03`, séquentiel strict autonome, un sous-agent cold-start/sous-sprint) :
- **S3.1** — Backend santé (headless/xUnit) : bind visible + test qui reproduit D-013, `IsListening`,
  `LastClientWriteUtc`, `SnapshotReturns`. Disjoint.
- **S3.2** — Santé visible : bandeau bind + panneau santé ; **solde D-013**. Partage `CarrouselScene.cs`.
- **S3.3** — Chaîne par élément (étiquettes %MW + coloration d'état) + `demo_sprint_03.ps1`. Partage
  `CarrouselScene.cs` → **S3.2 avant S3.3**.

**État banc à l'ouverture** : **90 tests verts** (87 core + 3 serveur), 4 pytest full-chain verts.
S3.1 re-figera le banc (nouveaux témoins, total annoncé).

**Résultat (clôture 2026-07-16)** — sprint **livré**, 3 sous-sprints verts et commités, DoD atteinte
(hors validation visuelle Nico) :
- **S3.1** (`6ae64d5`) : `ModbusServer` durci — `ModbusServerException` (type dédié), `IsListening`,
  `LastClientWriteUtc` (`Interlocked`), `ModbusDataStore.SnapshotReturns()`. Banc **re-figé 90 → 95**
  (89 core + 6 serveur, +5 témoins santé). **Points durs tranchés empiriquement** : bind occupé =
  échec **synchrone** (`SocketException`) → pré-vol `TcpListener` + try/catch (ceinture-bretelles) ;
  `RegistersChanged` **fiable** avec `AlwaysRaiseChangedEvent=true` (fire même sur FC16 valeur
  identique, indispensable pour l'I/O Scanner) → pas de repli, pas de dette.
- **S3.2** (`30e4f2b`) : `HealthHud` (neuf) — bandeau rouge d'échec de bind (**solde D-013**) +
  panneau santé (serveur / heartbeat / activité PLC), lecture seule ~5 Hz sur le thread principal.
  `CarrouselScene` : try/catch `ModbusServerException`, garde `_serverFailed`, `_ExitTree`
  conditionnel. Banc **inchangé (95)**, build Godot 0 erreur, `smoke_anim.ps1` + 4 pytest verts.
- **S3.3** (`f1523f2`) : `CommandChainLabels` (neuf) — étiquettes 3D `cmd %MW → physique → ret %MW`
  par élément **décodées du pivot** (jamais d'adresse en dur) + coloration d'état (tige/anneau/
  fenêtres, matériaux réutilisés) rafraîchies ~6 Hz. `demo_sprint_03.ps1` (neuf). Banc **inchangé (95)**.

**Ce qui a surpris** : le bind occupé s'est révélé **synchrone** (on craignait un échec silencieux) —
tranché par une sonde jetable avant d'écrire le correctif. Et `RegistersChanged` a bien un mode
« lève même sans changement de valeur », sans quoi un PLC présent mais stable aurait paru déconnecté.

**Décisions actées** (voir `memory.md`) : `ModbusServerException` + pré-vol/catch ; activité PLC via
`RegistersChanged`/`AlwaysRaiseChangedEvent` + `LastClientWriteUtc` (Interlocked, Arch A) ;
`SnapshotReturns` ; étiquettes %MW décodées du pivot + coloration par mutation d'`AlbedoColor` ;
QCM D-Q1..D-Q4. **Dettes** : **D-013 soldée**. Consignées hors périmètre (déjà au sprint de
conception) : D-015 (nav 3D + vitesse), D-016 (édition in-app / forçage), D-017 (injection de défauts).

**Validation manuelle restante (Nico)** : F5 → lire les étiquettes %MW + voir les couleurs suivre
l'état ; occuper le port 502 pour voir le bandeau rouge ; dérouler `demo_sprint_03.ps1` ; puis M580 réel
(Phase 4). **⚠ Toujours aucun remote git** → commits locaux, push impossible.

**Suite** : Phase 4 = intégration M580 réelle. Ergonomie démo (D-015) et édition in-app (D-016) /
injection de défauts (D-017) = sprints dédiés à concevoir.

---

## 2026-07-15 — Ouverture Sprint 2 : cinématique visuelle (animer la 3D depuis la sim)

**Contexte** : sprint 1 clos (chaîne Modbus bout-en-bout + maquette 3D **statique**). Archi du sprint 2
figée à la conception (`sprint_02_cinematique_visuelle.md`, décisions D-a..D-f). Prérequis matériel
confirmé : **Godot 4.6 .NET disponible en session** → le sprint est validable end-to-end et **solde
D-010** (smoke-test brique 5 jamais lancé).

**Objectif** : faire vivre la maquette. `_PhysicsProcess` rejoue la boucle Modbus (`PullCommands →
Tick → PushReturns`, patron `SimHost`) à **pas fixe** (accumulateur, `Tick` à 10 Hz), puis
`ApplyToScene` recopie l'état sim sur les transforms (snap 10 Hz). Glue Godot pure.

**Orchestration** (`/sprint open 02`, séquentiel strict autonome, un sous-agent cold-start/sous-sprint) :
- **S2.1** — scène-hôte Modbus (boucle + serveur + garde-fous ; `ApplyToScene` = stub vide).
- **S2.2** — animation `ApplyToScene` (mapping tiges/palettes) + `smoke_anim.ps1` ; **solde D-010**.
Les deux partagent `CarrouselScene.cs` → **séquentiels** (S2.1 puis S2.2).

**État tests à l'ouverture** : core 87 verts + serveur, 4 pytest full-chain verts (contre `SimHost`).

**Résultat (clôture 2026-07-15)** — sprint **livré et validé end-to-end**, DoD atteinte :
- **S2.1** (commit `31ac4d5`) : `CarrouselScene` devenue hôte Modbus. Build **0 erreur**, **90 tests**
  (87 core + 3 serveur), **scène ≡ SimHost** (4 pytest full-chain verts contre la scène en loopback:502),
  non-régression SimHost 4/4. Écart nécessaire : `ProjectReference` → `CarrouselServer` ajouté au
  `.csproj` Godot (anticipé dans l'en-tête de `CarrouselServer.csproj`).
- **S2.2** (commit `8dc0c22`) : `ApplyToScene` animée (tiges +Y, palettes `OnCircle` + rotation).
  `smoke_anim.ps1` **vert** (rod1 Y 0,125→0,275 m ; palettes 0/120/240 → 90/50/70 = accumulation
  correcte ; heartbeat 0→211). 4 pytest toujours verts.
- **Validation visuelle** (éditeur, `demo_sprint_02.ps1`, commit `d4b2d71`) : rotation **CCW**, postes
  90°/270°, tige qui monte à l'extension et redescend au rappel ressort, accumulation derrière un vérin
  engagé — **confirmée à l'œil par Nico**. → **D-010 soldée** (headless + humain).

**Ce qui a surpris** : à la validation, la maquette restait figée alors que le heartbeat vivait.
Cause = **deux serveurs sur le port 502** (un `SimHost` reliquat des vérifs + la scène) ; le
`server.Start()` de la scène avait **échoué en silence**. D'où le pré-vol du port dans
`demo_sprint_02.ps1` et la dette **D-013** (échec de bind non signalé à l'écran).

**Décisions actées** (voir `memory.md`) : boucle à pas fixe + accumulateur ; garde-fous heartbeat
(`low_processor_mode=false` + clamp + guard) ; snap 10 Hz (D-d) ; `BindLoopback` exporté ; **script
de démo visuelle guidée à livrer à chaque sprint**.

**Dettes** : **D-010 soldée**. Nouvelles : **D-011** (duplication `StepSim`↔`SimHost`, assumée),
**D-012** (throttling fenêtre non-focus, à surveiller), **D-013** (bind 502 silencieux).

**Suite** : reste hors sprint (diff canonique Python↔C#, polish visuel, IHM debug) ; Phase 4 =
intégration M580 réelle. **⚠ Toujours aucun remote git** → 3 commits locaux, push impossible.

---

## 2026-07-15 — Clôture Sprint 1 (brique 5 livrée, chaîne Modbus + maquette statique complètes)

**Contexte** : dernière brique du sprint 1 — la scène 3D statique, première scène Godot du projet.
Amorce : `sprint_01_brique_05_scene3d.md`.

**Archi (validée avant code)** — le pivot ne livrait pas encore la géométrie de rendu (bloc `render`
convoyeur, `size_m`/`radius_m`/`center` de `kinematics`). Deux options : **(A)** étendre le loader,
**(B)** relire le JSON dans Godot. Tranché **A** (un seul parseur du pivot, le codebase refuse un
second loader). Autres décisions actées avec Nico : **vérin vertical**, repère `x=cx+r·cosθ /
z=cz−r·sinθ` (0° sur +X, CCW vu de dessus), nœuds nommés par `id` pivot (tige = enfant `rod`).

**Livré** :
- `runtime/core/PivotModel.cs` : extension **additive** — `KinematicsInfo` + `RadiusM`/`Center`/
  `PalletSizeM` ; `Component.Render` + `GetRender` (`ResolveParams` généralisé en `ResolveNumericMap`).
  `radius_m`/`size_m` requis dès que `kinematics` est présent. `ToCanonical` inchangé → **parité
  formelle Python↔C# non affectée**.
- `runtime/scenes/CarrouselScene.cs` (nouveau) : builder procédural à `_Ready` — anneau **CSG**
  (aucun primitif ne fait de couronne plate), 2 vérins (corps + tige rentrée, `rod` translatable),
  3 palettes aux positions initiales, fenêtres capteurs translucides ; caméra/lumière par `LookAt`.
- `runtime/scenes/main.tscn` + `project.godot` (`run/main_scene`) ; `DemonstrateurCarrousel.csproj`
  (`ProjectReference` → core) ; `runtime/scripts/smoke_scene.ps1` (smoke-test headless) ; NOTES §6.

**Résultat** : **core 82 → 87 verts** (+5 : géométrie rendu + render + robustesse). **Assembly Godot
compile** (0 erreur : usage API 4.6 + lien core validés). Server + SimHost non régressés. **DoD sprint 1
atteinte** hormis la validation M580 réelle (Phase 4, pas de matériel) et le smoke-test headless
lui-même (Godot absent du poste — **D-010**).

**Ce qui reste (hors sprint 1, reporté)** : diff **canonique formel** Python↔C# (backlog Phase 1) ;
cinématique **visuelle** (animer la 3D depuis la sim) = **sprint 3** ; conformité visuelle de la scène
à confirmer au 1er lancement Godot.

**Dettes nouvelles** : **D-009** (`render.kind` non consommé, convoyeur câblé « anneau » en dur, V1
mono-convoyeur — cosmétique) ; **D-010** (smoke-test headless non exécuté ici — à surveiller).

**⚠ Périmètre sprint 2 à revoir** : le backlog Phase 1bis prévoyait la 3D statique **en sprint 2** ;
elle est faite (sprint 1, brique 5). Le sprint 2 doit être **re-conçu** (candidat naturel : la
cinématique visuelle, ex-sprint 3). → objet de la **conception après `/clear`**.

**⚠ Commit** : commit local `5228326` ; **toujours pas de remote** (`git remote -v` vide) → push impossible.

---

## 2026-07-15 — Sprint 1, brique 4b : palettes (rotation, accumulation, présence B1/B2)

**Objectif** : ajouter, de façon additive, le mouvement des palettes, leur blocage/accumulation
derrière un vérin engagé, et remplir B1/B2. Lever le point dur annoncé (accumulation circulaire).

**Fait** :
- `runtime/core/PivotModel.cs` : parse additif `Kinematics` (count, positions initiales, `min_gap`,
  sens). Optionnel au `Load` (fixtures de mapping), obligatoire à l'usage (accès défensif).
- `runtime/core/PalletSet.cs` (nouveau) : modèle pur. Rotation, blocage, accumulation, présence.
- `runtime/core/CarrouselSimulation.cs` : extension `Tick` — postes bloqués (vérin engagé) →
  `pallets.Advance` (rotation pilotée par `Conveyor.IsRunning`) → écriture B1/B2 via `WriteBit`.
- Tests : `PalletSetTests` (nouveau, 14), ajouts `PivotModelTests` (6) et `CarrouselSimulationTests` (2).

**Ce qui a surpris (dans le bon sens)** : le point dur « accumulation circulaire » (incertitude
HAUTE) s'est **dissous** par la formulation, comme l'inversion mi-course du vérin en 4a. Deux idées :
(1) **espace « sens de marche »** — repère où avancer = angle croissant, réflexion involutive pour
`cw`, un seul chemin de code ; (2) **écart en `mod 360`** — efface la couture 0°/360°, rend le
blocage vérin trivial (obstacle = un angle de plus). Relaxation itérative (`count` passes) pour la
chaîne. Résultat : **aucune simplification**, donc **D-008 non créée**.

**Décisions** (reportées `memory.md`) : algo d'accumulation acté ; rotation palettes pilotée par
KM1_AUX (moteur confirmé), pas la commande brute ; vitesse = `KM1.speed_deg_per_s`.

**État des tests** : **82 core + 3 serveur** verts (`dotnet test`, les 60 core d'avant intacts).
**4 pytest full-chain restent verts** (SimHost relancé → `4 passed`). D-002/D-003 inchangées.

**Suite** : brique 5 — scène 3D Godot (`sprint_01_brique_05_scene3d.md`).

---

## 2026-07 — Phase 0 : conception et modèle pivot

**Objectif** : figer le contrat central (JSON pivot) et l'organisation du projet.

**Fait** :
- Spec fonctionnelle du carrousel validée (3 palettes, 2 postes de blocage,
  vérins monostables 500 ms, accumulation, retour de marche KM1).
- Table Modbus validée : cmd %MW100 (1 mot), ret %MW200 (2 mots, heartbeat en mot 0),
  deux lignes d'I/O Scanner FC3/FC16.
- `pivot/machine_carrousel.json` v0.2 écrit et validé.
- CLAUDE.md, commandes Claude Code et fichiers d'orchestration créés.

**Décisions** : voir memory.md (toutes reportées).

**Prochaine étape** : Sprint 1 — chaîne Modbus de bout en bout
(datastore thread-safe + serveur + banc de test Python émulant l'I/O Scanner),
avant toute 3D. Brief : `docs/sprints/sprint_01_brief.md`.

---

## 2026-07-14 — Ouverture Sprint 1 : chaîne Modbus de bout en bout

**Objectif** : prouver la chaîne Modbus bout-en-bout (client Python jouant le M580, puis
M580 réel → maquette 3D statique) avant tout pipeline d'extraction ou IHM.
Brief : `docs/sprints/sprint_01_brief.md`.

**État à l'ouverture** :
- Testbench Python amorcé : loader pivot + `test_pivot_mapping.py` — **17 verts / 4 skip**
  (les 4 skip = scénarios chaîne, en attente d'un serveur qui écoute sur :502).
  `io_scanner_sim.py` écrit, non encore validé contre un serveur réel.
- Runtime Godot : squelette seul (`.csproj`, `project.godot`), aucun code métier.
- Fichiers d'orchestration en place.

**Premier point dur** : D-001 (comportement thread-safe réel de FluentModbus) — POC à
lever **avant** de figer le pont datastore ↔ serveur.

**Ordre de travail** : POC D-001 → datastore + loader C# (tests hors Godot) → serveur +
heartbeat (validé au testbench) → boucle de simulation → scène 3D → validation M580 réel.

---

## 2026-07-14 — POC D-001 concluant (FluentModbus validé, Arch A confirmée)

**Contexte** : lever le point dur n°1 avant de figer l'API des briques C#.

**Action** : harnais jetable `runtime/poc/` (vrai `ModbusTcpServer` + horloge 100 ms)
martelé par `io_scanner_sim.py` en loopback (FC3/FC16).

**Résultat** : chaîne validée end-to-end à l'unit 1 — heartbeat propre, `cmd_run=1` →
`KM1_AUX=1` en 1 tick, aucune corruption sous scan répété. **Arch A confirmée.**
Trois contraintes FluentModbus découvertes et résolues (accès buffer synchrone ;
`AddUnit(unit_id)` ; `Get/SetBigEndian<T>` obligatoires) — détails dans
`docs/notes/NOTES_sprint_01.md`, décisions dans `memory.md`. **D-001 soldée.**
Effet de bord : testbench corrigé pour l'API pymodbus 3.14 (`device_id=`), dette **D-007**.
FluentModbus figé à **5.3.2**.

**Suite** : brique 1 (`PivotModel` C#) + scaffolding class library `core` (décision D-006).

---

## 2026-07-15 — Brique 1 close : `PivotModel` C# testé hors Godot (D-006 concrétisée)

**Contexte** : `runtime/core/PivotModel.cs` (miroir C# de `pivot_loader.py`) et la class
library `CarrouselCore` étaient écrits et compilaient, mais sans tests — or c'est
précisément le test hors moteur qui justifie D-006.

**Action** : projet xUnit `runtime/tests/` (`ProjectReference` → `CarrouselCore`,
aucune dépendance Godot ; chemin `tests/` déjà réservé par le `Compile Remove` de
`DemonstrateurCarrousel.csproj`, donc l'assembly Godot ne compile pas ces fichiers). `PivotModelTests.cs` reprend **cas pour cas** la suite pytest
`test_pivot_mapping.py` : mêmes adresses %MW/bit attendues sur le **même pivot réel**
(zones, heartbeat mot 0, KM1_AUX %MW201.6, S11..S22, B1/B2, bases surchargeables) +
robustesse (JSON invalide, bit/word hors borne, conflit, heartbeat absent).

**Résultat** : **17 verts / 0 skip** (`dotnet test`), symétrie exacte avec les 17 verts
Python. Les deux loaders résolvent des adresses identiques sur le pivot réel → parité
pratique Python/C# établie. `.gitignore` runtime couvre bien `bin/`+`obj/` (vérifié en
dry-run : seuls les fichiers source seraient suivis).

**Reste** : le diff **canonique formel** Python↔C# (évoqué dans l'en-tête de
`PivotModel.cs`, `ToCanonical()` côté C#) n'est pas encore outillé — l'émetteur canonique
manque côté Python. Consigné au backlog Phase 1. La parité par assertions communes suffit
pour l'instant (simplicité d'abord).

**Suite** : brique 2 — `ModbusDataStore` (objet C# pur, `ushort[]` cmd/ret + verrou),
même projet `core`, tests dans `runtime/tests`. Puis brique 3 (serveur FluentModbus branché,
Arch A) validée au testbench Python.

**Convention actée ce jour** : chaque brique reçoit une **amorce autosuffisante** dans
`docs/sprints/` (reprise à froid après `/clear`). Amorce brique 2 rédigée :
`docs/sprints/sprint_01_brique_02_datastore.md` (contrat d'API, 5 décisions pré-tranchées,
3 questions ouvertes, DoD).

---

## 2026-07-15 — Brique 2 close : `ModbusDataStore` (source de vérité d'Arch A)

**Contexte** : après le loader (brique 1), la pièce centrale d'Arch A — le tampon des mots
d'échange entre thread serveur et thread physique. Amorce : `sprint_01_brique_02_datastore.md`.

**Archi validée avant code** : contrat de l'amorce confirmé, 3 questions ouvertes tranchées
selon les recommandations — snapshot `ushort[]` **brut** (pas de struct décodé), pont serveur
en **`Span<ushort>`** (zéro alloc, aligné sur `GetHoldingRegisters`), **pas** d'accès direct
au heartbeat (la sim reconstruit tout le `ret` puis publie).

**Fait** : `runtime/core/ModbusDataStore.cs` — objet C# pur (`ushort[]` cmd/ret + verrou),
zéro dépendance Godot. **Transport de mots bruts** : aucun décodage bit, aucun heartbeat,
aucune adresse absolue (tailles tirées de `PivotModel.GetZone(...).SizeWords`). API :
`SnapshotCommands` (copie défensive), `PublishReturns` (remplacement atomique + défensif),
`WriteCommandsFromWire`/`CopyReturnsToWire` (pont serveur, spans dimensionnés à la zone,
longueur vérifiée). `runtime/tests/ModbusDataStoreTests.cs` (11 cas).

**Résultat** : **28 verts / 0 échec** (`dotnet test`). Deux propriétés fines verrouillées
par test : snapshot = *copie* (pas la référence interne) et publish = *recopie du contenu*
(pas la référence fournie) — évitent deux fuites d'abstraction classiques. Tous les points
de design justifiés pédagogiquement dans `NOTES_sprint_01.md §2`.

**Décisions** : pas de nouvelle entrée `memory.md` (Arch A et le pattern datastore y étaient
déjà actés le 2026-07-14) ; les choix de design de la brique sont dans les NOTES et l'amorce
cochée. Aucune dette nouvelle.

**Suite** : **brique 3** — serveur FluentModbus branché sur ce datastore (pull `cmd` début de
tick / push `ret` fin de tick, sous `server.Lock`), validé au testbench Python (io_scanner_sim).

---

## 2026-07-15 — Brique 3 close : `ModbusServer` (pont FluentModbus ↔ datastore)

**Contexte** : dernière pièce du transport Modbus. Amorce : `sprint_01_brique_03_serveur.md`.

**Archi validée avant code** — deux questions structurantes tranchées :
- **Assembly (Q4)** : `ModbusServer` va dans un **projet dédié** `runtime/server/CarrouselServer.csproj`
  (classlib → `CarrouselCore` + FluentModbus 5.3.2). `CarrouselCore` reste **pur** (D-006) ;
  option A (FluentModbus dans le core) et B (dans l'assembly Godot, non testable) écartées.
- **Validation (Q2)** : test d'intégration **in-process** (reco 2a) — vrai serveur sur loopback
  + vrai `ModbusTcpClient` FluentModbus dans le même `dotnet test`. La full-chain Python (4 skips)
  reste pour la brique 4.

**Fait** :
- `PivotModel` expose `Port`/`UnitId`, **parse strict** (échec clair si absent) — décision **D-f**
  demandée par l'utilisateur (pas de repli 502/1 : le pivot est le contrat, ces valeurs réseau ne
  se devinent pas). Les 2 fixtures de test minimales reçoivent `port`/`unit_id` explicites +
  2 tests de robustesse (champ absent).
- `runtime/server/ModbusServer.cs` : les 3 contraintes POC ré-imposées (`AddUnit(unit_id)`, accès
  buffer **synchrone**, `Get/SetBigEndian<ushort>` **registre par registre**). Port/unit_id/bases
  résolus du pivot. Serveur **passif** : `PullCommands`/`PushReturns` séparés, appelés par le thread
  appelant sous `server.Lock`. Bind défaut `IPAddress.Any` (M580 distant).
- `runtime/server.tests/ModbusServerTests.cs` (3 cas : transport FC16→pull, publish→push→FC3,
  endianness big-endian explicite via client little/big). Port **éphémère libre** en test (pas 502).
- `DemonstrateurCarrousel.csproj` : `server/` et `server.tests/` retirés du glob SDK Godot.

**Résultat** : **34 verts / 0 échec** (`dotnet test` : 31 core + 3 intégration serveur).
Endianness big-endian du fil prouvée in-process (client little-endian lit `0x3412` là où le
serveur a écrit `0x1234`). Points de design justifiés dans `NOTES_sprint_01.md §3`.

**Décisions** : `memory.md` inchangé (Arch A + contraintes FluentModbus déjà actées le
2026-07-14) ; **D-f** (parse strict port/unit_id) consignée aux NOTES §3 et à l'amorce cochée.
Aucune dette nouvelle.

**Suite** : **brique 4** — boucle de simulation (cinématique scriptée déterministe + heartbeat),
qui remplira le `ret` et débloquera la validation full-chain FC3/FC16 du testbench Python.
Amorce : `sprint_01_brique_04_simulation.md`.

---

## 2026-07-15 — Sprint 1, brique 4a : boucle de simulation (vérins + convoyeur + heartbeat)

**Archi (validée avant code)** : re-découpage brique 4 → **4a** (heartbeat + vérins + KM1_AUX,
porte les 4 pytest full-chain) et **4b** (palettes/accumulation/présence). Les deux sous-amorces
rédigées d'avance (convention 2026-07-15). Reco archi suivies en bloc par Nico.

**Livré** :
- `runtime/core/PivotModel.cs` : `Signal.WriteBit` (symétrique de `ReadBit`), `Component.Params`
  + `GetParam` (sac générique `double`, additif D-d), `HeartbeatPeriodMs` (cadence tick, défaut 100).
  **Le pivot JSON n'a pas changé** — tous les params y étaient depuis la Phase 0.
- `runtime/core/CylinderState.cs` : vérin monostable 0→1, vitesse constante, inversion mi-course
  gérée par le clamp (aucune branche dédiée). Seuils S11/S12 + IsEngaged (pour 4b).
- `runtime/core/ConveyorState.cs` : recopie retardée KM1_AUX (suiveur temporisé symétrique).
- `runtime/core/CarrouselSimulation.cs` : composition root. `Tick` = snapshot cmd → advance →
  heartbeat (rollover ushort) → reconstruction complète de `ret` (D-e) → publish. B1/B2 à 0 (4b).
- `runtime/simhost/` (nouveau projet console) : hôte headless Pull→Tick→Push cadencé, écoute 502.
  Débloque pytest sans Godot ; patron du futur `_PhysicsProcess`.
- Tests : `CylinderStateTests`, `ConveyorStateTests`, `CarrouselSimulationTests` + ajouts params/
  WriteBit/cadence dans `PivotModelTests`.
- `DemonstrateurCarrousel.csproj` : `simhost/` retiré du glob SDK Godot (sinon double entry point).

**Résultat** : **63 verts / 0 échec** (60 core + 3 intégration serveur ; +29 vs 34, originaux intacts).
**4 scénarios pytest full-chain PASSENT** (`SimHost` en écoute, `pytest test_modbus_chain.py -v`
→ `4 passed`). Heartbeat, KM1_AUX (recopie après délai), YV1/YV2 (sortie + rappel ressort) validés
bout-en-bout FC3/FC16.

**Décisions/dettes** : `memory.md` amendé (params + cadence tirés du pivot, découpage 4a/4b).
Aucune dette nouvelle en 4a. D-008 (simplification accumulation) reste candidate pour 4b.
Points de design → `NOTES_sprint_01.md §4`. Amorces 4a rédigée+cochée, 4b prête.

**Suite** : **brique 4b** — palettes, accumulation `min_gap_deg`, présence B1/B2.
Amorce : `sprint_01_brique_04b_palettes.md`.

**⚠ Commit** : pas de remote configuré (`git remote -v` vide) — commit local uniquement, push impossible.

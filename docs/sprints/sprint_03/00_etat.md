# Sprint 03 — « Durcir le démonstrateur » — état de sprint

> Carnet vivant : conception **figée** le 2026-07-16 (archi validée avec Nico). Tenu à jour par
> chaque sous-sprint pendant l'exécution. Reprise à froid : `CLAUDE.md` + ce fichier + l'amorce.

## Où on en est
Sprint 3 **CLOS** (2026-07-16). 3 sous-sprints livrés, verts et **commités** ; NOTES + journal +
memory + dettes + backlog à jour. **Reste : validation visuelle Nico** (F5 + `demo_sprint_03.ps1`).
Sprints 1 & 2 clos avant. Banc **95 verts** de bout en bout ; build Godot 0 erreur ; 4 pytest full-chain verts.

**Avancement S3.x (tous CLOS, commités) :**
- **S3.1 Backend santé — CLOS** (`6ae64d5`). Banc re-figé **90 → 95**. DoD + points durs ci-dessous.
- **S3.2 Santé visible — CLOS** (`30e4f2b`). HUD lecture seule (bandeau échec bind + panneau santé).
  Banc **inchangé (95)**. **D-013 soldée** (visibilité). Détails ci-dessous.
- **S3.3 Chaîne par élément + démo — CLOS** (`f1523f2`). Étiquettes 3D `cmd %MW → physique → ret %MW`
  par élément (KM1/YV1/YV2/B1/B2) décodées du pivot + coloration d'état + `demo_sprint_03.ps1`.
  Banc **inchangé (95)**. Détails ci-dessous.

### S3.1 — DoD cochée
- [x] `ModbusServerException` (type dédié) créée — `runtime/server/ModbusServerException.cs`.
- [x] Test de repro D-013 : 2ᵉ `Start()` sur le même port → `Assert.Throws<ModbusServerException>`. **Vert.**
- [x] `Start()` : échec bind → `ModbusServerException` (message FR + bind:port) ; succès → `IsListening == true`.
      Nominal inchangé (les 3 tests transport/endianness restent verts).
- [x] `LastClientWriteUtc` : non-`null` après FC16 réel, thread-safe (`Interlocked`).
- [x] `ModbusDataStore.SnapshotReturns()` : copie défensive sous verrou + 2 tests.
- [x] `dotnet test` **vert** : core 87→89, serveur 3→6, **total 95**.
- [ ] 4 pytest full-chain — **non lancés** (hors périmètre S3.1 par consigne orchestrateur ; nominal
      Modbus inchangé, additif seulement).

### S3.1 — Points durs tranchés (empirique)
- **Bind occupé = SYNCHRONE** : `ModbusTcpServer.Start()` (FluentModbus 5.3.2) lève une
  `SocketException` synchrone sur port occupé (vérifié par probe jetable). Implémentation retenue :
  **pré-vol `TcpListener`** (détection indépendante de la lib, filet type `demo_sprint_02.ps1` ramené
  dans l'app) **+** try/catch autour de `_server.Start()` (ceinture-bretelles). Les deux enveloppent
  en `ModbusServerException`.
- **`RegistersChanged` fiable** : oui. FluentModbus 5.3.2 expose `EnableRaisingEvents` +
  **`AlwaysRaiseChangedEvent`** ; armés tous deux → l'event fire sur FC16 **même valeur identique**
  (indispensable pour l'I/O Scanner qui réécrit `cmd` à l'identique). **Pas de repli, pas de dette.**
  Test dédié : 2ᵉ écriture même valeur → horodatage avance.

### S3.2 — DoD cochée
- [x] Port 502 occupé → **bandeau rouge visible** + message `ModbusServerException` + `GD.PrintErr`.
      Scène non plantée, `_PhysicsProcess` sort tôt (garde `_serverFailed`). → **D-013 SOLDÉE** (visibilité).
- [x] Port libre → pas de bandeau ; panneau santé `serveur : à l'écoute :502`, heartbeat qui défile,
      activité PLC via `LastClientWriteUtc` (fiabilisée S3.1 — pas de `n/d`).
- [x] `[Export] bool ShowHud` masque/affiche le panneau santé ; le bandeau d'erreur reste.
- [x] Build assembly Godot **0 erreur**.
- [x] `smoke_anim.ps1` **vert** (scène boote avec HUD, cinématique intacte, aucune SCRIPT ERROR) ;
      **4 pytest full-chain vert** (SimHost loopback:502). Comportement Modbus **inchangé**.
- [x] Aucune écriture `cmd` ; boucle `StepSim` inchangée (hors garde `_serverFailed`).
- Fichiers : `runtime/scenes/CarrouselScene.cs` (try/catch Start, garde, création HUD, `_ExitTree`
  gardé sur échec) + `runtime/scenes/HealthHud.cs` (**neuf**).
- Design imprévu tranché : `_ExitTree` ne dispose le serveur **que si** le bind a réussi (sur échec,
  FluentModbus n'a jamais démarré ; le pré-vol `TcpListener` a déjà relâché le port dans son finally).

### S3.3 — DoD cochée
- [x] Étiquette 3D `Label3D` billboard par élément (KM1, YV1, YV2, B1, B2), texte `cmd %MWx.y →
      physique → ret %MWa.b` **décodé du pivot** (`Signal.AbsWord/.Bit`, jamais d'adresse en dur).
- [x] Coloration d'état : tige teintée par dégradé (repos→ambre, selon `Position`), anneau vert si
      `KM1_AUX`=1, fenêtre capteur allumée verte si `Bi`=1. Réutilise les matériaux déjà posés
      (mut. `AlbedoColor`), refs anneau/fenêtres capturées au build.
- [x] Rafraîchissement **basse cadence ~6 Hz** (`_Process` + accumulateur, `LabelRefreshPeriodS=0.15`),
      pas 60 Hz (anti-scintillement). Snapshots `SnapshotCommands()/SnapshotReturns()` (thread principal,
      Arch A). **Zéro écriture `cmd`.** Décodage centralisé dans `CommandChainLabels` (booléens
      `Km1Aux/B1Present/B2Present` exposés pour la coloration).
- [x] `demo_sprint_03.ps1` : pré-vol port 502 (calqué demo_02), 6 phases guidées annonçant étiquette +
      couleur à regarder, **ASCII pur**, parse PS OK.
- [x] Build Godot **0 erreur** ; `smoke_anim.ps1` **vert** ; **4 pytest full-chain verts** (scène
      headless sur :502). Banc **inchangé (95)**.
- Fichiers : `runtime/scenes/CarrouselScene.cs` (capture refs matériaux/nœuds anneau+fenêtres,
  création + alimentation labels, `_Process` coloration) + `runtime/scenes/CommandChainLabels.cs`
  (**neuf**) + `runtime/scripts/demo_sprint_03.ps1` (**neuf**).
- Points durs tranchés : `Label3D` billboard + `NoDepthTest=true` + contour noir → lisible sous tout
  angle ; texte **une ligne/élément** (compact) à `PixelSize=0.0022`, hauteurs locales étagées
  (KM1 1.05, vérins 0.55, capteurs 0.45) pour éviter la superposition. Ambiguïté de type `Signal`
  (Godot.Signal vs CarrouselCore.Signal) levée par alias `using Signal = CarrouselCore.Signal;`.
- **Validation manuelle restante (Nico)** : F5 → lire les étiquettes %MW + voir les couleurs suivre
  l'état ; dérouler `demo_sprint_03.ps1` ; confirmer lisibilité de la chaîne à l'œil (+ M580 réel).

## Objectif
Rendre le démonstrateur **robuste** (échecs bruyants) et **lisible** (chaîne de commande tracée
**par élément 3D**) avant le M580 réel (Phase 4). **Lecture seule**, **pivot non touché**, Arch A intacte.

## Décisions clés (QCM 2026-07-16)
- **D-Q1** — Cœur = HUD lecture seule + bind visible.
- **D-Q2** — Forçage **reporté** (→ D-016) ; zéro écriture `cmd`.
- **D-Q3** — Voyant santé **dérivé de nous** (heartbeat + dernière écriture `cmd`) ; vérifier
  `RegistersChanged` tôt, repli sinon ; ne jamais promettre plus que le certain.
- **D-Q4** — « Par élément » = **étiquette texte** (lien Modbus %MW) **+ coloration d'état**.
- **D-arch** — Couture headless (S3.1) / visuel (S3.2, S3.3).
- **D-013** — Test qui **reproduit** le bind occupé écrit **avant** le correctif (CLAUDE.md §5).

## Carte des sous-sprints (séquentiels)
| # | Amorce | Nature | Fichiers | Dépend |
|---|---|---|---|---|
| **S3.1** Backend santé | `brique_01_backend_sante.md` | headless/xUnit | `ModbusServer.cs`, `ModbusServerException.cs`(neuf), `ModbusDataStore.cs`, `server.tests/`, `tests/` | — |
| **S3.2** Santé visible | `brique_02_sante_visible.md` | visuel | `CarrouselScene.cs`, `HealthHud.cs`(neuf) | S3.1 |
| **S3.3** Chaîne par élément + démo | `brique_03_chaine_commande.md` | visuel / **observable** | `CarrouselScene.cs`, `CommandChainLabels.cs`(neuf), `demo_sprint_03.ps1`(neuf) | S3.1, S3.2 |
> S3.2 & S3.3 partagent `CarrouselScene.cs` → **S3.2 avant S3.3**. S3.1 disjoint.

## Banc
S3.1 : `dotnet test` **re-figé 90 → 95** (+5 témoins : repro bind D-013, `IsListening`, activité PLC,
2× `SnapshotReturns`). **Fait.** Deux projets à lancer séparément (pas de .sln).
S3.2/S3.3 : glue lecture seule → `smoke_anim.ps1` + 4 pytest full-chain **inchangés/verts**.

## Dettes
- **D-013 : SOLDÉE** (S3.2, 2026-07-16) — détectable S3.1 (`ModbusServerException`), désormais
  **visible** S3.2 (bandeau rouge + `GD.PrintErr`). À reporter dans `docs/dettes.md` à la clôture.
- Consignées hors périmètre (dettes.md) : **D-015** (nav 3D + vitesse), **D-016** (édition/branchement
  in-app), **D-017** (simulation de cas non nominaux / éléments défaillants).
- Non touchées : D-012, D-005, D-011.

## Points durs (dans les amorces)
- Bind occupé synchrone vs silencieux → tranché par le test de repro (S3.1).
- `RegistersChanged` fiable ? → mini-vérif en tête de S3.1, repli documenté.

## REPRISE
1. **Sprint 3 CLOS et commité** (`6ae64d5` S3.1, `30e4f2b` S3.2, `f1523f2` S3.3). NOTES pédagogiques
   rédigées (`NOTES.md`), journal/memory/dettes/backlog à jour. Rien ne reste à orchestrer sur ce sprint.
2. **Seule action ouverte : validation visuelle Nico** — F5 sur la scène pour lire les étiquettes %MW +
   voir les couleurs suivre l'état ; occuper le port 502 (`SimHost`) pour vérifier le **bandeau rouge** ;
   dérouler `runtime/scripts/demo_sprint_03.ps1`. Puis confrontation **M580 réel** (Phase 4).
3. Rappel banc (témoin figé **95**) : `dotnet test runtime/tests/CarrouselCore.Tests.csproj` (89) **puis**
   `dotnet test runtime/server.tests/CarrouselServer.Tests.csproj` (6). Pas de .sln (deux projets séparés).
   Non-régression visuelle/Modbus : `runtime/scripts/smoke_anim.ps1` (vert) + `pytest
   testbench/test_modbus_chain.py` (4 verts, scène headless sur :502).
4. **Suite projet** : Phase 4 (M580 réel). Sprints dédiés à concevoir : D-015 (nav 3D + vitesse),
   D-016 (édition in-app / forçage), D-017 (injection de défauts).

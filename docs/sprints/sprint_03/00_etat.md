# Sprint 03 — « Durcir le démonstrateur » — état de sprint

> Carnet vivant : conception **figée** le 2026-07-16 (archi validée avec Nico). Tenu à jour par
> chaque sous-sprint pendant l'exécution. Reprise à froid : `CLAUDE.md` + ce fichier + l'amorce.

## Où on en est
Conception **close**, sprint **en cours d'exécution**. 3 sous-sprints, amorces rédigées.
Sprints 1 & 2 clos (démonstrateur 3D animé depuis la sim via Modbus, validé à l'œil).

**Avancement S3.x :**
- **S3.1 Backend santé — FAIT** (2026-07-16, non commité, en attente orchestrateur). Banc re-figé
  **90 → 95**. Détails ci-dessous (DoD + points durs tranchés).
- **S3.2 Santé visible — FAIT** (2026-07-16, non commité, en attente orchestrateur). HUD lecture
  seule (bandeau échec bind + panneau santé). Banc **inchangé (95)**. **D-013 soldée** (visibilité).
  Détails ci-dessous.
- S3.3 Chaîne par élément + démo — à faire (prochain).

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
1. Relire `CLAUDE.md`, ce fichier, l'`overview.md`, et l'amorce du sous-sprint courant.
2. **Sous-sprint courant : S3.3** (chaîne par élément + démo). S3.1 **et** S3.2 sont FAITS (voir
   avancement ci-dessus), changements sur disque **non commités** — l'orchestrateur commit avant/après.
   S3.3 partage `CarrouselScene.cs` avec S3.2 → rester chirurgical (ne pas défaire la garde `_serverFailed`
   ni la création du HUD).
3. Exécution : `/sprint open 03` (séquentiel strict, un sous-agent cold-start par sous-sprint,
   autonome jusqu'au vert). Ordre restant : **S3.3**. Nico reprend au rapport final.
4. Rappel banc S3.1 (témoin figé) : `dotnet test runtime/tests/CarrouselCore.Tests.csproj` (89) **puis**
   `dotnet test runtime/server.tests/CarrouselServer.Tests.csproj` (6) = **95 verts**. Pas de .sln.

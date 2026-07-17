# Sprint 05 « Injection de défauts » — état de sprint

> Carnet vivant, tenu à jour par chaque sous-sprint. Reprise à froid = `CLAUDE.md` + ce fichier
> (+ l'amorce du sous-sprint courant). Conception figée le **2026-07-17** (`/conception`).

## Où on en est
**S5.1 + S5.2 + S5.3 + S5.4 livrées.** Reste S5.5.
Banc vert : **xUnit core 109** (INCHANGÉ) + **serveur 10** (**re-figé 6 → 10**, +4 : déconnexion/
reconnexion TCP) = **119 au total**. Build Godot **0 erreur**. Les 4 pytest full-chain **skippent**
tant qu'aucun runtime Godot n'écoute sur 502 (état nominal, S5.4 n'y touche pas).

## Intention
Forcer depuis l'IHM un défaut **physique ou de comm** par élément, pour éprouver le M580 — **sans
jamais écrire un mot Modbus**. Défaut dans la **sim pure**, `ret` en découlent, générique **par
type**. Pivot inchangé, Arch A intacte, boucle 10 Hz intacte. Solde **D-017** et **D-018**, prépare **D-016**.

## Décisions clés (QCM)
- **D-Q1** coupure comm = gel `ret` (heartbeat inclus) **+ déconnexion TCP réelle** (2 modes).
- **D-Q2** granularité = physique vérin/convoyeur **+ capteur-bloqué 0/1 générique sur tout bit `ret`
  TOR** (S11/S12/S21/S22, B1/B2, KM1_AUX). « KM1_AUX collé » = capteur-bloqué sur `ret_running`.
- **D-Q3** visibilité = marquage visible **+ mode aveugle** (touche `B`).
- **D-Q4** entrée = **souris + clavier** (`[`/`]` cycler, `R` réparer, `F` menu, `B` aveugle ; éviter `F11`).
- **D-Q5** menu = **MenuButton par ligne + colonne « Défaut »** (entrée réutilisée par D-016).

## Cœur pur (S5.1) — l'API que les autres consomment
`FaultSet` (physique par composant / capteur-bloqué par signal / `RetFrozen`), `FaultCommand`,
`FaultCatalog.ApplicableTo(Component)`. `CarrouselSimulation.Faults` appliqué en tête de `Tick` :
vérin coincé ⇒ pas d'`Advance` ; ne sort pas ⇒ commande forcée faux ; patine ⇒ palettes figées,
KM1_AUX intact ; capteur-bloqué ⇒ masque du bit **après** encodage ; `RetFrozen` ⇒ ni heartbeat ni
publication. **Défaut inactif = nominal ⇒ 4 pytest inchangés.**

## Carte des sous-sprints (5, ordre d'orchestration)
1. [x] **S5.1** `brique_01_faultset.md` — cœur pur (headless/xUnit). **Banc re-figé** (89 → 109, +20). Indépendant. **LIVRÉE**.
2. [x] **S5.2** `brique_02_selection.md` — sélection clic 3D↔ligne + clavier + **D-018 soldée** (visuel). Banc inchangé (115). **LIVRÉE**.
3. [x] **S5.3** `brique_03_menu_defauts.md` — menu par ligne + marquage 3D/badge + mode aveugle (visuel). Banc inchangé (115). **LIVRÉE**.
4. [x] **S5.4** `brique_04_coupure_comm.md` — déconnexion TCP + contrôle comm global (backend+UI). **Banc serveur re-figé** (6 → 10, +4). **LIVRÉE**.
5. **S5.5** `brique_05_demo.md` — `demo_sprint_05.ps1` + sortie observable. Banc intact.

S5.2→S5.3→S5.4 partagent `CarrouselScene.cs` ⇒ **strictement séquentiels**. S5.1 d'abord, S5.5 dernier.

## Points durs
- **S5.4 / FluentModbus — TRANCHÉ** : le spike (test jetable in-process) montre qu'un `ModbusTcpServer`
  5.3.2 **se réarme sur la MÊME instance** après `Stop()` (port ré-ouvert, unité+buffer conservés, client
  reconnectable, `AddUnit` inutile). Donc `Disconnect()=Stop()`, `Reconnect()=Start()` **sans recréation**
  (voir `docs/memory.md`, entrée 2026-07-17). « Réparer » = re-bind 502 (D-013), remonté à l'UI (bandeau).
- **Incohérence 3D/PLC assumée** pendant la coupure (sim animée, `ret` figé / TCP coupé) — message pédagogique voulu.

## Reste à faire
Orchestrer **S5.5** (dernier : `demo_sprint_05.ps1` + sortie observable, banc intact).

## REPRISE
**S5.1 + S5.2 + S5.3 + S5.4 livrées, non committées** (l'orchestrateur commit).
**S5.4 — fichiers modifiés (3)** : `runtime/server/ModbusServer.cs`, `runtime/server.tests/ModbusServerTests.cs`,
`runtime/scenes/HealthHud.cs`. **`CarrouselScene.cs` NON touché** (les no-op serveur suffisent : `StepSim`
tourne inchangé pendant la coupure ; le HUD a déjà `_server` et `_sim` via `Configure`).
- `ModbusServer` : `Start()` refactoré → extrait `StartListening()` privé (pré-vol `TcpListener` + `_server.Start`),
  partagé avec `Reconnect()`. Nouvelles : `Disconnect()` (`Stop()`, idempotent, `IsListening=false`),
  `Reconnect()` (`StartListening()`, idempotent, peut lever `ModbusServerException` remontée). `AddUnit`
  reste dans `Start()` seul (unité conservée, spike). `PullCommands`/`PushReturns` : garde `if (!_isListening) return;`.
- `ModbusServerTests` : +4 (déconnexion coupe l'écoute/reconnexion rétablit ; idempotence ; Pull/Push hors
  écoute ne lèvent pas ; bout-en-bout client perd la connexion puis se reconnecte après réparation). **Serveur 6 → 10**.
- `HealthHud` : le HUD **pilote** désormais serveur + `Faults` (toujours pas datastore/scene tree, Arch A
  tenue). `BuildCommControls()` : panneau toujours visible sous le panneau santé, 2 `CheckButton` toggles
  (« Geler retours » → `_sim.Faults.RetFrozen` ; « Couper TCP » → `Disconnect`/`Reconnect`). Reconnexion
  échouée → `ShowBindFailure` + `SetPressedNoSignal(true)` (bouton reste « coupé »). `CommStatusLine()` :
  4e ligne du panneau (`nominal`/`TCP coupé`/`ret figé`/cumul/`serveur KO`) ; panneau santé +20 px de haut.
- Banc **core 109 inchangé**, **serveur re-figé 6 → 10** = **119**, build Godot 0 erreur. **Validation manuelle
  F5 + `io_scanner_sim.py` requise** (voir DoD/Vérif de l'amorge) : couper TCP → le client perd la connexion ;
  réparer → il rescanne ; geler retours → heartbeat figé côté client, TCP vivant.
Prochaine action : `/sprint open 05` continue sur **S5.5** (démo + sortie observable, dernier sous-sprint).

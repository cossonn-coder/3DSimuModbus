# Sprint 05 « Injection de défauts » — état de sprint

> Carnet vivant, tenu à jour par chaque sous-sprint. Reprise à froid = `CLAUDE.md` + ce fichier
> (+ l'amorce du sous-sprint courant). Conception figée le **2026-07-17** (`/conception`).

## Où on en est
**S5.1 + S5.2 livrées.** Reste S5.3 → S5.5.
Banc vert : **xUnit core 109** + **serveur 6** = **115 au total, INCHANGÉ** en S5.2 (glue Godot
lecture seule, zéro modif core/serveur). Build Godot **0 erreur**.

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
3. **S5.3** `brique_03_menu_defauts.md` — menu par ligne + marquage 3D/badge + mode aveugle (visuel). Banc inchangé.
4. **S5.4** `brique_04_coupure_comm.md` — déconnexion TCP + contrôle comm global (backend+UI). **Banc serveur re-figé**.
5. **S5.5** `brique_05_demo.md` — `demo_sprint_05.ps1` + sortie observable. Banc intact.

S5.2→S5.3→S5.4 partagent `CarrouselScene.cs` ⇒ **strictement séquentiels**. S5.1 d'abord, S5.5 dernier.

## Points durs
- **S5.4 / FluentModbus** : réutilisabilité de `ModbusTcpServer` après `Stop()` (spike avant code ;
  sinon recréer + réarmer `AddUnit`/`RegistersChanged`). « Réparer » = re-bind 502 (D-013), remonté à l'UI.
- **Incohérence 3D/PLC assumée** pendant la coupure (sim animée, `ret` figé) — message pédagogique voulu.

## Reste à faire
Orchestrer S5.3 → S5.5 (S5.3/S5.4 séquentiels sur `CarrouselScene.cs`, S5.5 dernier).
S5.3 s'appuie sur la sélection persistante posée par S5.2 (`SetSelected`/`_selectedId` côté scène,
`SelectRow` côté panneau) pour cibler ses menus/marquages, et ajoute la priorité **défaut (rouge) >
sélection > survol** dans `RefreshEmission`/`RefreshRowStyle` déjà en place.

## REPRISE
**S5.1 + S5.2 livrées, non committées** (l'orchestrateur commit).
**S5.2 — fichiers modifiés (2)** : `runtime/scenes/CarrouselScene.cs` et `runtime/scenes/ElementPanel.cs`.
- `CarrouselScene` : état `_hoveredId`/`_selectedId`/`_componentIds` ; `SetHover` devient stateful ;
  nouvelles `SetSelected(string?)` (source unique symétrique), `RefreshEmission(string?)` (résolveur
  par priorité sélection cyan > survol bleu > repos), `_UnhandledInput` (clic gauche relâché →
  `SetSelected(_hoveredId)` ; `]`/`[` → `CycleSelection` dans l'ordre du pivot), `CycleSelection`.
  **D-018 soldée** : 5 champs de nœuds vestigiaux + 5 assignations supprimés.
- `ElementPanel` : délégué `_onRowClick` (param ajouté à `Build`) ; `HighlightRow` devient stateful ;
  nouvelles `SelectRow(string?)` + `RefreshRowStyle` (stylebox cyan `_rowSelect` prime sur survol) ;
  capture du clic gauche par ligne via `GuiInput`.
- Banc **inchangé** (core 109, serveur 6), build Godot 0 erreur. **Validation manuelle F5 requise** (voir
  DoD de l'amorce) : non exécutable en headless (picking souris + styleboxes).
Prochaine action : `/sprint open 05` continue sur **S5.3** (menu de défauts par ligne).

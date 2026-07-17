# Sprint 05 « Injection de défauts » — état de sprint

> Carnet vivant, tenu à jour par chaque sous-sprint. Reprise à froid = `CLAUDE.md` + ce fichier
> (+ l'amorce du sous-sprint courant). Conception figée le **2026-07-17** (`/conception`).

## Où on en est
**S5.1 + S5.2 + S5.3 livrées.** Reste S5.4 → S5.5.
Banc vert : **xUnit core 109** + **serveur 6** = **115 au total, INCHANGÉ** en S5.3 (glue Godot
qui pilote la sim sur le thread principal, zéro modif core/serveur). Build Godot **0 erreur**.

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
4. **S5.4** `brique_04_coupure_comm.md` — déconnexion TCP + contrôle comm global (backend+UI). **Banc serveur re-figé**.
5. **S5.5** `brique_05_demo.md` — `demo_sprint_05.ps1` + sortie observable. Banc intact.

S5.2→S5.3→S5.4 partagent `CarrouselScene.cs` ⇒ **strictement séquentiels**. S5.1 d'abord, S5.5 dernier.

## Points durs
- **S5.4 / FluentModbus** : réutilisabilité de `ModbusTcpServer` après `Stop()` (spike avant code ;
  sinon recréer + réarmer `AddUnit`/`RegistersChanged`). « Réparer » = re-bind 502 (D-013), remonté à l'UI.
- **Incohérence 3D/PLC assumée** pendant la coupure (sim animée, `ret` figé) — message pédagogique voulu.

## Reste à faire
Orchestrer S5.4 → S5.5 (S5.4 séquentiel sur `CarrouselScene.cs` après S5.3, S5.5 dernier).
S5.4 réutilise l'entrée « menu par ligne » posée par S5.3 (`_onFault`/`OnFault` → `_sim.Faults.Apply`)
et le contrôle comm global s'ajoutera au-dessus (déconnexion TCP + gel `ret`, prépare D-016).

## REPRISE
**S5.1 + S5.2 + S5.3 livrées, non committées** (l'orchestrateur commit).
**S5.3 — fichiers modifiés (2)** : `runtime/scenes/CarrouselScene.cs` et `runtime/scenes/ElementPanel.cs`.
- `ElementPanel` : 6e colonne « Défaut » (`ColWidths`/`Headers` étendus) ; chaque ligne gagne une
  cellule `Defaut` + un `MenuButton` en bout de ligne (popup peuplé à l'ouverture via `AboutToPopup`
  depuis `FaultCatalog.ApplicableTo` + « Réparer » si un défaut est actif ; `IndexPressed` → `_onFault`).
  `Build` gagne 2 délégués (`onFault`, `faultLabelById`) ; `Update` remplit la colonne via `_faultLabelById`.
  Nouvelles : `PopulateFaultMenu`, `OpenFaultMenu(id)`, `SetBlindMode(bool)`, statique
  `FaultCommandLabel(cmd, comp)` (mapping FR, source unique) + `SignalLabel`. Indicateur `_blindIndicator`
  « MODE AVEUGLE » ancré haut-centre.
- `CarrouselScene` : état `_blindMode` + lookup `_components` ; `RefreshEmission` gagne la priorité
  **défaut (rouge) > sélection (cyan) > survol (bleu)**, ignorée en mode aveugle. `_UnhandledInput`
  gagne `B` (aveugle), `R` (réparer la sélection), `F`/`Espace` (ouvrir le menu). Nouvelles : `OnFault`
  (→ `_sim.Faults.Apply` — 1re écriture IHM→sim, thread principal), `FaultLabelById` (agrège
  physique+stucks, « — » en aveugle), `ToggleBlindMode`. Constantes `FaultEmission`/`FaultEnergy`.
- Banc **inchangé** (core 109, serveur 6 = 115), build Godot 0 erreur. **Validation manuelle F5 requise**
  (voir DoD de l'amorce) : non exécutable en headless (MenuButton/picking/styleboxes/émission).
Prochaine action : `/sprint open 05` continue sur **S5.4** (coupure comm : déconnexion TCP + gel `ret`).

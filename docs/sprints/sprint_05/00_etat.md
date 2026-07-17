# Sprint 05 « Injection de défauts » — état de sprint

> Carnet vivant, tenu à jour par chaque sous-sprint. Reprise à froid = `CLAUDE.md` + ce fichier
> (+ l'amorce du sous-sprint courant). Conception figée le **2026-07-17** (`/conception`).

## Où on en est
Phase B **figée** : `overview.md` + 5 amorces autosuffisantes rédigées. Prêt pour `/sprint open 05`.
Aucune implémentation encore livrée.

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
1. **S5.1** `brique_01_faultset.md` — cœur pur (headless/xUnit). **Banc re-figé** (+scénarios défaut). Indépendant.
2. **S5.2** `brique_02_selection.md` — sélection clic 3D↔ligne + clavier + **solde D-018** (visuel). Banc inchangé.
3. **S5.3** `brique_03_menu_defauts.md` — menu par ligne + marquage 3D/badge + mode aveugle (visuel). Banc inchangé.
4. **S5.4** `brique_04_coupure_comm.md` — déconnexion TCP + contrôle comm global (backend+UI). **Banc serveur re-figé**.
5. **S5.5** `brique_05_demo.md` — `demo_sprint_05.ps1` + sortie observable. Banc intact.

S5.2→S5.3→S5.4 partagent `CarrouselScene.cs` ⇒ **strictement séquentiels**. S5.1 d'abord, S5.5 dernier.

## Points durs
- **S5.4 / FluentModbus** : réutilisabilité de `ModbusTcpServer` après `Stop()` (spike avant code ;
  sinon recréer + réarmer `AddUnit`/`RegistersChanged`). « Réparer » = re-bind 502 (D-013), remonté à l'UI.
- **Incohérence 3D/PLC assumée** pendant la coupure (sim animée, `ret` figé) — message pédagogique voulu.

## Reste à faire
Exécuter `/sprint open 05` (orchestration séquentielle autonome des 5 sous-sprints).

## REPRISE
Conception terminée et committée. Prochaine action : `/clear` puis `/sprint open 05`. Chaque
sous-sprint met à jour ce carnet (case cochée + total de banc) au fil de sa livraison.

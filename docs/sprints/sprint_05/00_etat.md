# Sprint 05 « Injection de défauts » — état de sprint

> Carnet vivant, tenu à jour par chaque sous-sprint. Reprise à froid = `CLAUDE.md` + ce fichier
> (+ l'amorce du sous-sprint courant). Conception figée le **2026-07-17** (`/conception`).

## Où on en est
**S5.1 livrée** (cœur pur d'injection de défauts). Reste S5.2 → S5.5.
Banc vert : **xUnit core 109** (89 → 109, +20 scénarios de défaut) + **serveur 6 inchangé** = 115 au total.

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
Orchestrer S5.2 → S5.5 (séquentiels sur `CarrouselScene.cs`, sauf S5.5 dernier).

## REPRISE
**S5.1 livrée, non committée** (l'orchestrateur commit). Fichiers neufs :
`runtime/core/FaultSet.cs`, `runtime/core/FaultCatalog.cs`, `runtime/tests/FaultSetTests.cs`.
Modifié : `runtime/core/CarrouselSimulation.cs` (expose `Faults`, applique les 3 familles dans `Tick` :
verin coincé/rentré, convoyeur patine, capteur-bloqué masqué après encodage, `RetFrozen` = ni heartbeat
ni publication). Banc : core 109 vert, serveur 6 vert. API consommée par S5.2/S5.3 (menu `FaultCatalog`,
mutation `FaultSet.Apply`, marqueur `HasAnyFault`) et S5.4 (`RetFrozen` côté sim, la déconnexion TCP
restant à faire). Prochaine action : `/sprint open 05` continue sur **S5.2**.

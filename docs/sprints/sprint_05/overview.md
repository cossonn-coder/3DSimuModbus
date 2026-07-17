# Sprint 05 « Injection de défauts » — overview

> Index des sous-sprints. Conception figée le 2026-07-17 (`/conception`, QCM D-Q1..D-Q5).
> Reprise à froid d'un sous-agent : `CLAUDE.md` + `00_etat.md` + son amorce suffisent.

## Intention
Forcer depuis l'IHM un état de défaillance **physique ou de comm** par élément, pour éprouver
le programme M580 face aux défauts — **sans jamais écrire un mot Modbus** (ni `cmd` ni `ret`).
Le défaut vit dans la **sim pure** (`CarrouselCore`), les mots `ret` en découlent. Générique
**par type** de composant (jamais d'id carrousel en dur). Solde **D-017**, prépare **D-016**,
solde **D-018**. **Pivot inchangé**, Arch A intacte, boucle 10 Hz intacte.

## Décisions figées (QCM 2026-07-17)
- **D-Q1** Coupure comm = **gel `ret` (heartbeat inclus)** + **déconnexion TCP réelle** (2 modes).
- **D-Q2** = physique vérin/convoyeur **+ capteur-bloqué 0/1 générique sur tout bit `ret` TOR**
  (S11/S12/S21/S22, B1/B2, KM1_AUX). « KM1_AUX collé » = capteur-bloqué sur `ret_running`.
- **D-Q3** = marquage visible par défaut **+ interrupteur « mode aveugle »**.
- **D-Q4** = **souris + raccourcis clavier**.
- **D-Q5** = **MenuButton par ligne + colonne « Défaut »** (entrée réutilisée par D-016).

## Modèle de défauts (rappel — détaillé en S5.1)
| Famille | Application | Où |
|---|---|---|
| Physique | vérin coincé ⇒ pas d'`Advance` ; ne sort pas ⇒ commande effective faux ; convoyeur patine ⇒ palettes figées, KM1_AUX intact | modèles purs via `Tick` |
| Capteur-bloqué | masque du bit **après** encodage (jamais au datastore) | `CarrouselSimulation` encode |
| Gel `ret` | ni `_heartbeat++` ni `PublishReturns` | `Tick` |
| Déconnexion TCP | `ModbusServer` ferme la socket (I/O Scanner en défaut de scrutation) | S5.4, transport |

**Défaut inactif = comportement nominal ⇒ 4 pytest full-chain inchangés.**

## Sous-sprints (5 bornés — cf. [[budget-tokens-sous-sprints]])
| # | Amorce | Nature | Dépend de |
|---|---|---|---|
| S5.1 | `brique_01_faultset.md` — cœur pur (`FaultSet`+`FaultCatalog`+injection sim) | headless/xUnit | — |
| S5.2 | `brique_02_selection.md` — sélection clic 3D↔ligne + clavier + solde D-018 | visuel | — (S5.1 conseillé pour cohérence, pas requis) |
| S5.3 | `brique_03_menu_defauts.md` — menu par ligne + marquage 3D/badge + mode aveugle | visuel | S5.1, S5.2 |
| S5.4 | `brique_04_coupure_comm.md` — déconnexion TCP + contrôle comm global | backend+UI | S5.1, S5.3 |
| S5.5 | `brique_05_demo.md` — `demo_sprint_05.ps1` + sortie observable | observable | S5.1..S5.4 |

**Ordre d'orchestration** : S5.1 → S5.2 → S5.3 → S5.4 → S5.5. S5.2/S5.3/S5.4 partagent
`CarrouselScene.cs` ⇒ strictement séquentiels.

## Banc attendu (global)
- xUnit **re-figé** en S5.1 (ajoute les scénarios de défaut sur modèles purs) puis en S5.4
  (banc serveur : déconnexion/reconnexion). **Inchangé** en S5.2/S5.3/S5.5.
- **4 pytest full-chain inchangés** sur tout le sprint (défaut inactif = nominal).
- Build Godot 0 erreur à chaque sous-sprint visuel ; smoke (`ring/cylinders/pallets/sensors`,
  `panel rows`) préservé.

## Points durs
- **S5.4 / FluentModbus** : réutilisabilité de `ModbusTcpServer` après `Stop()` à vérifier
  (sinon recréer + réarmer `AddUnit`/`RegistersChanged`) ; « réparer » = re-bind 502 (D-013).
- **Incohérence assumée** pendant la coupure : 3D animée alors que panneau/PLC voient `ret`
  figé — message pédagogique voulu.

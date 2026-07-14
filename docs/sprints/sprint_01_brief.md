# Sprint 1 — Chaine Modbus de bout en bout (runtime Godot minimal)

> Brief **autosuffisant** : lisible seul, sans avoir a relire tout l'historique.
> Contexte complet dans `CLAUDE.md` ; decisions dans `docs/memory.md` ; contrat dans
> `pivot/machine_carrousel.json`.

## Objectif du sprint

Prouver que la **chaine Modbus fonctionne de bout en bout** avant d'investir dans le
pipeline Python (DWG/PDF) ou une IHM. A la fin du sprint, un client Modbus (le
testbench Python jouant le M580, puis le M580 reel) pilote une maquette 3D statique de
carrousel a palettes et lit ses retours, via une table d'echange conforme au pivot.

**Ce sprint ne cherche PAS** : le realisme mecanique, l'ergonomie, l'ingestion des
fichiers sources. Uniquement le squelette runtime + le dialogue Modbus.

## Rappel machine (pivot schema 0.2.0)

Carrousel a palettes : 1 convoyeur circulaire (rayon 1.5 m, 3 palettes, moteur `KM1`,
20 deg/s), 2 postes de blocage a **90 deg** et **270 deg** avec verins bloqueurs
**monostables** (`YV1`, `YV2`, course 0.15 m, `travel_time_ms`=500), capteurs de
presence palette (`B1`, `B2`, fenetre 8 deg), retour marche moteur (`KM1_AUX`, ex-`B3`).
Cinematique **scriptee** (pas de physique) ; palettes = positions angulaires avec
accumulation (`min_gap_deg`=20).

## Rappel Modbus (contrat fige en Phase 0)

- Godot = **serveur** Modbus TCP (FluentModbus, port 502, hote Windows).
- M580 = **client**, scrute via I/O Scanner : **FC3** lit la zone `ret`, **FC16** ecrit
  la zone `cmd`. Pas de FC23 en V1.
- `%MWn` = holding register protocole `n`, **aucun decalage**.
- 2 zones a base configurable : `cmd` base `%MW100` (1 mot), `ret` base `%MW200` (2 mots).
- Adressage **relatif** `{zone, word, bit}` — jamais d'adresse absolue en dur.
- **Heartbeat** = mot 0 de `ret` (`%MW200`), +1 toutes les 100 ms.

### Table d'echange cible

**Zone `cmd` (PLC → sim, FC16, base %MW100)**
| %MW | Bit | Signal | Signification |
|-----|-----|--------|---------------|
| 100 | 0 | KM1.cmd_run | marche convoyeur |
| 100 | 1 | YV1.cmd_extend | verin 1 sorti (0 = rappel ressort) |
| 100 | 2 | YV2.cmd_extend | verin 2 sorti (0 = rappel ressort) |

**Zone `ret` (sim → PLC, FC3, base %MW200)**
| %MW | Bit | Tag | Signification |
|-----|-----|-----|---------------|
| 200 | — | HEARTBEAT | compteur +1 / 100 ms, rollover 16 bits |
| 201 | 0 | S11 | verin 1 rentre |
| 201 | 1 | S12 | verin 1 sorti |
| 201 | 2 | S21 | verin 2 rentre |
| 201 | 3 | S22 | verin 2 sorti |
| 201 | 4 | B1 | presence palette poste 1 (90 deg) |
| 201 | 5 | B2 | presence palette poste 2 (270 deg) |
| 201 | 6 | KM1_AUX | retour marche moteur convoyeur |

## Livrables du sprint

### Runtime Godot (`runtime/`)
1. **Loader pivot** — DTO C# + parsing defensif de `machine_carrousel.json`
   (echoue clairement si mot/bit hors zone, doublon d'adresse, champ manquant).
2. **Datastore Modbus** — objet **C# pur** (`ushort[]` cmd + `ushort[]` ret + verrou),
   **sans dependance Godot**. API : `SnapshotCommands()`, `PublishReturns(...)`,
   accesseurs bit/mot resolus depuis base + offset.
3. **Serveur FluentModbus** — TCP port 502, branche sur le datastore, thread dedie qui
   **ne touche jamais le scene tree**.
4. **Boucle de simulation** — a chaque tick physique : snapshot des commandes →
   cinematique scriptee (convoyeur en rotation continue si `KM1`, bloqueurs interpoles
   sur `travel_time_ms`, capteurs a seuils) → increment heartbeat → publication des
   retours.
5. **Scene 3D statique** — convoyeur + 2 bloqueurs + palettes, positions issues du pivot.

### Testbench Python (`testbench/`)
6. **Loader pivot Python** — resout les adresses **absolues** (`base + word`, `bit`) a
   partir du JSON, pour ne jamais coder une adresse en dur dans les tests.
7. **Simulateur d'I/O Scanner** — client Modbus (pymodbus) qui joue le M580 : ecrit
   `cmd` en FC16, lit `ret` en FC3, a cadence fixe.
8. **Suite pytest** — scenarios de validation de la chaine (voir criteres ci-dessous).

## Point dur a lever tot (D-001)

Avant de figer le pont datastore ↔ serveur, faire un **POC isole** : serveur
FluentModbus + client pymodbus qui martele FC3/FC16 sous charge, pour observer le
comportement thread-safe reel (buffer interne, verrou, hooks). Comparer a NModbus si
blocage. Documenter dans `NOTES_sprint_01.md`.

## Ordre de travail conseille

1. Valider l'**architecture** des 3 briques C# (loader / datastore / serveur) — cf.
   regle « archi avant code ». Livraison **fichier par fichier**.
2. POC FluentModbus (D-001).
3. Datastore + loader + tests unitaires (testables **hors Godot**).
4. Serveur Modbus branche + heartbeat, valide au testbench Python (sans 3D).
5. Boucle de simulation + cinematique scriptee.
6. Scene 3D statique.
7. Validation M580 reel.

## Definition of Done

- [ ] `machine_carrousel.json` charge sans erreur ; un JSON malforme echoue proprement.
- [x] Testbench : loader pivot + tests unitaires du contrat (17 verts, 4 skip).
- [ ] `cmd_run=1` (FC16) → heartbeat progresse et `KM1_AUX` passe a 1 apres
      `feedback_delay_ms` ; `cmd_run=0` → `KM1_AUX` retombe.
- [ ] `cylinder_1.cmd_extend=1` → apres `travel_time_ms`, `S12`=1 et `S11`=0 ;
      `=0` → rappel ressort (monostable) : `S11`=1, `S12`=0. Idem verin 2 (`S21`/`S22`).
- [ ] `B1`/`B2` refletent la presence palette selon la position simulee.
- [ ] Heartbeat incremente ~10x/s, rollover propre a 65535.
- [ ] POC D-001 documente ; strategie thread-safe retenue ecrite dans les NOTES.
- [ ] Scene 3D statique affiche convoyeur (anneau) + 2 bloqueurs + 3 palettes.
- [ ] (Si dispo) M580 reel scrute l'app via I/O Scanner 2 lignes, echanges coherents.
- [ ] `docs/notes/NOTES_sprint_01.md` redige ; journal/backlog/dettes a jour.

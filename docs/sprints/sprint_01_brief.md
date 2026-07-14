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

## Rappel machine (pivot v0.2)

Carrousel a palettes : 1 convoyeur circulaire (moteur `KM1`), 2 verins bloqueurs
**monostables** (`YV1`, `YV2`), capteurs de presence (`B1`, `B2`), retour marche
moteur (`KM1_AUX`, ex-`B3`). Cinematique **scriptee** (pas de physique).

## Rappel Modbus (contrat fige en Phase 0)

- Godot = **serveur** Modbus TCP (FluentModbus, port 502, hote Windows).
- M580 = **client**, scrute via I/O Scanner : **FC3** lit la zone `ret`, **FC16** ecrit
  la zone `cmd`. Pas de FC23 en V1.
- `%MWn` = holding register protocole `n`, **aucun decalage**.
- 2 zones, base `%MW` configurable (`MW_CMD_BASE`=0, `MW_RET_BASE`=100 par defaut).
- Adressage **relatif** `{zone, word, bit}` — jamais d'adresse absolue en dur.
- **Heartbeat** = mot 0 de `ret`, +1 toutes les 100 ms.

### Table d'echange cible (bases par defaut)

**Zone `cmd` (PLC → sim, FC16, base %MW0)**
| Mot | Bit | Repere | Signification |
|-----|-----|--------|---------------|
| 0 | 0 | KM1 | marche convoyeur |
| 0 | 1 | YV1 | bloqueur 1 sorti (0 = rappel ressort) |
| 0 | 2 | YV2 | bloqueur 2 sorti (0 = rappel ressort) |
| 1 | — | SPD_CMD | consigne vitesse convoyeur (0.1 deg/s) |

**Zone `ret` (sim → PLC, FC3, base %MW100)**
| Mot | Bit | Repere | Signification |
|-----|-----|--------|---------------|
| 0 | — | HB_RET | heartbeat (+1 / 100 ms) |
| 1 | 0 | B1 | presence palette bloqueur 1 |
| 1 | 1 | B2 | presence palette bloqueur 2 |
| 1 | 2 | KM1_AUX | retour marche moteur convoyeur |
| 2 | — | POS_RET | position angulaire convoyeur (0.1 deg) |
| 3 | — | CNT_RET | compteur palettes |

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
- [ ] Le testbench Python ecrit `KM1=1` (FC16) → le heartbeat progresse, `KM1_AUX`
      passe a 1, `POS_RET` augmente ; `KM1=0` → `POS_RET` se fige.
- [ ] `YV1=1` (FC16) → apres `travel_time_ms`, l'etat bloqueur reflete la sortie ;
      `YV1=0` → rappel ressort (monostable) verifie.
- [ ] `B1`/`B2` refletent la presence palette selon la position simulee.
- [ ] Heartbeat incremente ~10x/s, rollover propre a 65535.
- [ ] POC D-001 documente ; strategie thread-safe retenue ecrite dans les NOTES.
- [ ] Scene 3D statique affiche convoyeur + bloqueurs + palettes.
- [ ] (Si dispo) M580 reel scrute l'app via I/O Scanner 2 lignes, echanges coherents.
- [ ] `docs/notes/NOTES_sprint_01.md` redige ; journal/backlog/dettes a jour.

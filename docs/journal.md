# journal.md — Chronologie du projet

Règle : une entrée par sprint (ou par session de conception significative), rédigée
en fin de sprint via `/sprint`. Format : date, objectif, ce qui a été fait, ce qui a
surpris, décisions prises (reportées dans memory.md), état des tests.

---

## 2026-07 — Phase 0 : conception et modèle pivot

**Objectif** : figer le contrat central (JSON pivot) et l'organisation du projet.

**Fait** :
- Spec fonctionnelle du carrousel validée (3 palettes, 2 postes de blocage,
  vérins monostables 500 ms, accumulation, retour de marche KM1).
- Table Modbus validée : cmd %MW100 (1 mot), ret %MW200 (2 mots, heartbeat en mot 0),
  deux lignes d'I/O Scanner FC3/FC16.
- `pivot/machine_carrousel.json` v0.2 écrit et validé.
- CLAUDE.md, commandes Claude Code et fichiers d'orchestration créés.

**Décisions** : voir memory.md (toutes reportées).

**Prochaine étape** : Sprint 1 — chaîne Modbus de bout en bout
(datastore thread-safe + serveur + banc de test Python émulant l'I/O Scanner),
avant toute 3D. Brief : `docs/sprints/sprint_01_brief.md`.

---

## 2026-07-14 — Ouverture Sprint 1 : chaîne Modbus de bout en bout

**Objectif** : prouver la chaîne Modbus bout-en-bout (client Python jouant le M580, puis
M580 réel → maquette 3D statique) avant tout pipeline d'extraction ou IHM.
Brief : `docs/sprints/sprint_01_brief.md`.

**État à l'ouverture** :
- Testbench Python amorcé : loader pivot + `test_pivot_mapping.py` — **17 verts / 4 skip**
  (les 4 skip = scénarios chaîne, en attente d'un serveur qui écoute sur :502).
  `io_scanner_sim.py` écrit, non encore validé contre un serveur réel.
- Runtime Godot : squelette seul (`.csproj`, `project.godot`), aucun code métier.
- Fichiers d'orchestration en place.

**Premier point dur** : D-001 (comportement thread-safe réel de FluentModbus) — POC à
lever **avant** de figer le pont datastore ↔ serveur.

**Ordre de travail** : POC D-001 → datastore + loader C# (tests hors Godot) → serveur +
heartbeat (validé au testbench) → boucle de simulation → scène 3D → validation M580 réel.

---

## 2026-07-14 — POC D-001 concluant (FluentModbus validé, Arch A confirmée)

**Contexte** : lever le point dur n°1 avant de figer l'API des briques C#.

**Action** : harnais jetable `runtime/poc/` (vrai `ModbusTcpServer` + horloge 100 ms)
martelé par `io_scanner_sim.py` en loopback (FC3/FC16).

**Résultat** : chaîne validée end-to-end à l'unit 1 — heartbeat propre, `cmd_run=1` →
`KM1_AUX=1` en 1 tick, aucune corruption sous scan répété. **Arch A confirmée.**
Trois contraintes FluentModbus découvertes et résolues (accès buffer synchrone ;
`AddUnit(unit_id)` ; `Get/SetBigEndian<T>` obligatoires) — détails dans
`docs/notes/NOTES_sprint_01.md`, décisions dans `memory.md`. **D-001 soldée.**
Effet de bord : testbench corrigé pour l'API pymodbus 3.14 (`device_id=`), dette **D-007**.
FluentModbus figé à **5.3.2**.

**Suite** : brique 1 (`PivotModel` C#) + scaffolding class library `core` (décision D-006).

---

## 2026-07-15 — Brique 1 close : `PivotModel` C# testé hors Godot (D-006 concrétisée)

**Contexte** : `runtime/core/PivotModel.cs` (miroir C# de `pivot_loader.py`) et la class
library `CarrouselCore` étaient écrits et compilaient, mais sans tests — or c'est
précisément le test hors moteur qui justifie D-006.

**Action** : projet xUnit `runtime/tests/` (`ProjectReference` → `CarrouselCore`,
aucune dépendance Godot ; chemin `tests/` déjà réservé par le `Compile Remove` de
`DemonstrateurCarrousel.csproj`, donc l'assembly Godot ne compile pas ces fichiers). `PivotModelTests.cs` reprend **cas pour cas** la suite pytest
`test_pivot_mapping.py` : mêmes adresses %MW/bit attendues sur le **même pivot réel**
(zones, heartbeat mot 0, KM1_AUX %MW201.6, S11..S22, B1/B2, bases surchargeables) +
robustesse (JSON invalide, bit/word hors borne, conflit, heartbeat absent).

**Résultat** : **17 verts / 0 skip** (`dotnet test`), symétrie exacte avec les 17 verts
Python. Les deux loaders résolvent des adresses identiques sur le pivot réel → parité
pratique Python/C# établie. `.gitignore` runtime couvre bien `bin/`+`obj/` (vérifié en
dry-run : seuls les fichiers source seraient suivis).

**Reste** : le diff **canonique formel** Python↔C# (évoqué dans l'en-tête de
`PivotModel.cs`, `ToCanonical()` côté C#) n'est pas encore outillé — l'émetteur canonique
manque côté Python. Consigné au backlog Phase 1. La parité par assertions communes suffit
pour l'instant (simplicité d'abord).

**Suite** : brique 2 — `ModbusDataStore` (objet C# pur, `ushort[]` cmd/ret + verrou),
même projet `core`, tests dans `runtime/tests`. Puis brique 3 (serveur FluentModbus branché,
Arch A) validée au testbench Python.

**Convention actée ce jour** : chaque brique reçoit une **amorce autosuffisante** dans
`docs/sprints/` (reprise à froid après `/clear`). Amorce brique 2 rédigée :
`docs/sprints/sprint_01_brique_02_datastore.md` (contrat d'API, 5 décisions pré-tranchées,
3 questions ouvertes, DoD).

---

## 2026-07-15 — Brique 2 close : `ModbusDataStore` (source de vérité d'Arch A)

**Contexte** : après le loader (brique 1), la pièce centrale d'Arch A — le tampon des mots
d'échange entre thread serveur et thread physique. Amorce : `sprint_01_brique_02_datastore.md`.

**Archi validée avant code** : contrat de l'amorce confirmé, 3 questions ouvertes tranchées
selon les recommandations — snapshot `ushort[]` **brut** (pas de struct décodé), pont serveur
en **`Span<ushort>`** (zéro alloc, aligné sur `GetHoldingRegisters`), **pas** d'accès direct
au heartbeat (la sim reconstruit tout le `ret` puis publie).

**Fait** : `runtime/core/ModbusDataStore.cs` — objet C# pur (`ushort[]` cmd/ret + verrou),
zéro dépendance Godot. **Transport de mots bruts** : aucun décodage bit, aucun heartbeat,
aucune adresse absolue (tailles tirées de `PivotModel.GetZone(...).SizeWords`). API :
`SnapshotCommands` (copie défensive), `PublishReturns` (remplacement atomique + défensif),
`WriteCommandsFromWire`/`CopyReturnsToWire` (pont serveur, spans dimensionnés à la zone,
longueur vérifiée). `runtime/tests/ModbusDataStoreTests.cs` (11 cas).

**Résultat** : **28 verts / 0 échec** (`dotnet test`). Deux propriétés fines verrouillées
par test : snapshot = *copie* (pas la référence interne) et publish = *recopie du contenu*
(pas la référence fournie) — évitent deux fuites d'abstraction classiques. Tous les points
de design justifiés pédagogiquement dans `NOTES_sprint_01.md §2`.

**Décisions** : pas de nouvelle entrée `memory.md` (Arch A et le pattern datastore y étaient
déjà actés le 2026-07-14) ; les choix de design de la brique sont dans les NOTES et l'amorce
cochée. Aucune dette nouvelle.

**Suite** : **brique 3** — serveur FluentModbus branché sur ce datastore (pull `cmd` début de
tick / push `ret` fin de tick, sous `server.Lock`), validé au testbench Python (io_scanner_sim).

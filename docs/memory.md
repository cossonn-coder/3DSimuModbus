# memory.md — Décisions actées (source de vérité)

Règle : toute décision validée par l'équipe (utilisateur + automaticien) est consignée
ici avec sa date. En cas de contradiction entre un fichier et memory.md, memory.md
fait foi et la contradiction doit être signalée.

## Architecture

| Date | Décision |
|---|---|
| 2026-07 | Deux temps : pipeline hors-ligne Python (Phases 2-3, plus tard) / runtime Godot 4 C# autonome. Le pont entre les deux est **uniquement** le JSON pivot. |
| 2026-07 | V1 = Phases 0+1+4 avec JSON pivot **écrit à la main** (`pivot/machine_carrousel.json`). Le pipeline d'extraction (DWG, PDF) vient après. Le JSON manuel est le contrat que le pipeline devra produire. |
| 2026-07 | Godot = **serveur Modbus TCP** (FluentModbus pressenti, à confirmer au sprint 1), M580 = client via I/O Scanner. Aucune logique READ_VAR/WRITE_VAR côté PLC. |
| 2026-07 | Hôte runtime : **Windows**, port **502**, unit ID 1. Prévoir règle de pare-feu Windows entrante TCP 502. |

## Modbus

| Date | Décision |
|---|---|
| 2026-07 | **Deux lignes d'I/O Scanner** : lecture FC3 sur zone `ret`, écriture FC16 sur zone `cmd`. Pas de ligne combinée FC23 en V1. |
| 2026-07 | `%MWn` = holding register d'adresse protocole `n`. Aucun décalage. |
| 2026-07 | Zones à base configurable : `cmd` base %MW100 (1 mot), `ret` base %MW200 (2 mots). Adresses relatives `{zone, word, bit}` dans le JSON. |
| 2026-07 | Heartbeat : mot 0 zone `ret`, +1 toutes les 100 ms, rollover 16 bits libre. |
| 2026-07 | Tout en 16 bits en V1 (pas de sujet d'ordre de mots). TOR packés 16/mot. |
| 2026-07-14 | **FluentModbus 5.3.2 figé** (POC D-001 concluant). Trois règles imposées à `ModbusServer` : (1) déclarer l'unité du pivot via `server.AddUnit(unit_id)` — le défaut ne sert que l'unit 0 et ferme la connexion sinon ; (2) accéder au buffer par `server.GetHoldingRegisters(unit_id)` **toujours via `Get/SetBigEndian<T>`** (buffer natif little-endian ≠ fil Modbus big-endian, sinon octets inversés côté M580/pymodbus) ; (3) l'accès buffer se fait dans une méthode **synchrone** (un `Span<short>` ne peut pas vivre dans du code `async`). Latence commande→retour mesurée = **1 tick** (conforme Arch A). |

## Machine et simulation

| Date | Décision |
|---|---|
| 2026-07 | Machine V1 : carrousel circulaire, 3 palettes, 2 postes de blocage (90° et 270°), vérins **monostables** (bit=1 → sort, bit=0 → rentre par ressort). |
| 2026-07 | **Pas de moteur physique** : cinématique scriptée déterministe. Vérin = position 0→1 interpolée sur `travel_time_ms` (500 ms), capteurs à seuils (rentré <2 %, sorti >98 %). Inversion en cours de course : repart du point courant. |
| 2026-07 | Palette bloquée si vérin engagé (>10 %) à son poste. **Accumulation** : une palette s'arrête derrière une palette arrêtée (écart mini 20°). |
| 2026-07 | Signaux retours : S11/S12/S21/S22 (fins de course vérins), B1/B2 (présence palette aux postes), KM1_AUX (retour de marche convoyeur, recopie de la commande après 50 ms). Pas de capteur de zone de chargement. |
| 2026-07 | 3D générée **procéduralement** depuis le JSON (anneau, boîtes palettes, vérins corps+tige, zones capteurs semi-transparentes). Aucun asset externe en V1. |

## Thread-safety (pattern imposé)

| Date | Décision |
|---|---|
| 2026-07 | Datastore = objet C# pur (`ushort[]` + verrou), zéro dépendance Godot. Thread serveur Modbus ne touche jamais le scene tree. Simulation : snapshot des commandes en début de tick physique, publication des retours en fin de tick. |
| 2026-07-14 | **Arch A actée** (sprint 1, `/conception`) : le `ModbusDataStore` est la **source de vérité** ; le buffer interne de FluentModbus est un détail **privé** de `ModbusServer`, recopié ↔ datastore **à chaque tick physique** sous `server.Lock` (pull `cmd` en début de tick, push `ret` en fin). Le thread serveur ne touche **ni** le scene tree **ni** le datastore : seul le thread physique accède au datastore. Datastore et FluentModbus restent découplés (repli mini-serveur D-001 = une seule classe à réécrire). À réviser **uniquement** si le POC D-001 révèle un vrai problème de latence/contention. |
| 2026-07-15 | **Brique 4 scindée en 4a/4b** (archi `/conception`) : 4a = heartbeat + vérins (S11/S12/S21/S22) + retour convoyeur (KM1_AUX) → porte **les 4 scénarios pytest full-chain** ; 4b = palettes/accumulation/présence B1/B2 (isole l'incertitude du modèle d'accumulation circulaire). Cinématique = **petits modèles purs composés** (`CylinderState`, `ConveyorState`, puis `PalletSet`), `dt` injecté, zéro Godot, testables isolément. L'inversion mi-course du vérin est gérée par le clamp (aucune branche dédiée). |
| 2026-07-15 | **Params machine tirés du pivot** (extension additive `PivotModel`, D-d) : `Component.Params` (sac générique `double`) + `GetParam`, et `HeartbeatPeriodMs` (cadence tick, `heartbeat.period_ms`, défaut 100). Le pivot JSON **n'a pas changé** (tout y était depuis la Phase 0). `Signal.WriteBit` ajouté (symétrique de `ReadBit`). Hôte headless **`SimHost`** (`runtime/simhost/`, console .NET pure) débloque pytest sans Godot : boucle `PullCommands → sim.Tick → PushReturns`, patron du futur `_PhysicsProcess`. |

## Méthode de travail

| Date | Décision |
|---|---|
| 2026-07 | Architecture présentée et validée avant génération de code ; livraison fichier par fichier (SSH mobile). |
| 2026-07 | Commentaires pédagogiques en français (par bloc/fonction, le « pourquoi ») + `NOTES_sprint_XX.md` détaillé par sprint. Les commentaires pédagogiques ne comptent jamais comme dette de refactor. |
| 2026-07 | pytest côté Python ; logique C# testable hors Godot autant que possible. |
| 2026-07-15 | **Chaque brique a son amorce autosuffisante** (`docs/sprints/sprint_XX_brique_YY_*.md`) : contrat d'API visé, décisions de design pré-tranchées + questions ouvertes, DoD, vérif. But : permettre une reprise **à froid** (après `/clear`) sans relire l'historique. Le fichier est coché à sa livraison. |
| 2026-07-15 | **Toutes les amorces d'un sprint sont rédigées dès la conception (au moment du découpage)**, pas au fil de l'eau brique par brique. Quand un sprint est découpé en briques/tronçons, **chaque** sous-sprint reçoit son amorce **immédiatement** (les briques lointaines sont plus provisoires et re-validées à l'archi le moment venu, mais elles existent). Précise l'entrée précédente. Amorces sprint 1 rédigées : briques 2, 3, 4 (scindée en **4a/4b** le 2026-07-15), 5. |

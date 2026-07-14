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

## Méthode de travail

| Date | Décision |
|---|---|
| 2026-07 | Architecture présentée et validée avant génération de code ; livraison fichier par fichier (SSH mobile). |
| 2026-07 | Commentaires pédagogiques en français (par bloc/fonction, le « pourquoi ») + `NOTES_sprint_XX.md` détaillé par sprint. Les commentaires pédagogiques ne comptent jamais comme dette de refactor. |
| 2026-07 | pytest côté Python ; logique C# testable hors Godot autant que possible. |

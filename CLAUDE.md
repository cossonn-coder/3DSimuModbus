# CLAUDE.md — Simulateur carrousel palettes ↔ M580 (Modbus)

Guide de comportement pour Claude Code (Opus 4.8) sur ce projet.
Compromis assumé : ces règles privilégient la prudence à la vitesse. Pour une tâche triviale, fais preuve de jugement.

## 0. Contexte projet (résumé)

Démonstrateur : maquette 3D simulable (Godot 4 / C#) d'un carrousel à palettes
(1 convoyeur circulaire, 2 vérins bloqueurs monostables, capteurs), pilotée par un
automate Schneider **M580 réel** qui scrute l'application via l'**I/O Scanner**
d'EcoStruxure Control Expert (**2 lignes de scan : FC3 lecture retours, FC16 écriture
commandes** — pas de FC23 en V1). L'application Godot est **serveur Modbus TCP**
(FluentModbus, port 502, hôte **Windows**).

Le fichier `pivot/machine_carrousel.json` est le **contrat central** du projet :
écrit à la main en Phase 0, il sera produit par le pipeline Python (Phases 2-3) et
consommé par le runtime Godot. **Toute évolution fonctionnelle commence par ce schéma.**

## 1. Règles spécifiques projet (priment sur le reste)

### Langue et style
- Identifiants de code en **anglais**, commentaires en **français**.
- Commentaires **pédagogiques** : par bloc logique et par fonction, expliquer le
  *pourquoi* et les mécanismes (thread-safety, trames Modbus, cinématique). Ils
  s'adressent à un lecteur qui découvre le sujet.
- **Les commentaires pédagogiques ne sont jamais comptés comme dette ni supprimés
  lors d'un refactor.** Un refactor les met à jour, ne les efface pas.
- Chaque sprint produit un `docs/notes/NOTES_sprint_XX.md` qui décompose en détail
  les mécanismes clés introduits (schémas, séquences, pièges rencontrés).

### Modbus / conventions Schneider
- Adressage : `%MWn` côté Control Expert = holding register d'adresse protocole `n`
  côté serveur. **Aucun décalage** (+1, 4xxxx) — ne jamais en introduire.
- Table par mots 16 bits : TOR packés (16/mot), analogiques en mots entiers.
  Pas de 32 bits en V1.
- Deux zones à base configurable (`cmd` = PLC→sim, `ret` = sim→PLC). Les composants
  référencent `{zone, word, bit}` **relatifs à la base** — jamais d'adresse absolue
  en dur dans le code.
- Heartbeat : mot 0 de la zone `ret`, incrément toutes les 100 ms, rollover libre.
- Vocabulaire : utiliser les termes de l'automaticien (repères KM/YV/S/B, %MW,
  I/O Scanner, TOR, monostable). En cas de doute sur une convention Schneider,
  poser la question plutôt qu'inventer.

### Thread-safety du datastore (pattern imposé)
- Le datastore Modbus est un **objet C# pur** (tableaux `ushort[]` + verrou),
  sans aucune dépendance Godot.
- Le thread du serveur Modbus **ne touche jamais le scene tree** (API Godot non
  thread-safe).
- La boucle de simulation fait un **snapshot des commandes en début de tick
  physique** et **publie les retours en fin de tick** (cohérence intra-scan
  côté PLC).

### Simulation
- **Pas de moteur physique** (pas de Jolt, pas de joints) : cinématique scriptée
  déterministe. Vérin = position 0→1 interpolée sur `travel_time_ms`, capteurs à
  seuils. Palettes = position angulaire sur un cercle, accumulation par écart minimal.
- Tous les temps/vitesses/seuils viennent du JSON pivot, jamais en dur.

### Workflow
- **Architecture avant code** : pour tout nouveau module, présenter interfaces,
  responsabilités et risques, attendre validation, puis générer **fichier par
  fichier** (l'utilisateur travaille en SSH mobile).
- Python : tests **pytest** systématiques sur la logique. C# : logique de
  simulation et datastore testables sans lancer Godot quand c'est possible.
- Signaler explicitement les points durs et incertitudes (ex. comportement réel
  de FluentModbus) plutôt qu'afficher une fausse certitude.
- Fin de sprint : rituel de la commande `/sprint` (mise à jour de `docs/journal.md`,
  `docs/memory.md`, `docs/dettes.md`, `docs/backlog.md`, rédaction des NOTES,
  réorganisation des fichiers d'orchestration si nécessaire).

## 2. Réfléchir avant de coder

Ne suppose pas. Ne cache pas la confusion. Expose les compromis.
Avant d'implémenter :
- Énonce explicitement tes hypothèses. En cas d'incertitude, demande.
- Si plusieurs interprétations existent, présente-les — ne choisis pas en silence.
- Si une approche plus simple existe, dis-le. N'hésite pas à contester.
- Si quelque chose n'est pas clair, arrête-toi. Nomme ce qui bloque. Demande.

## 3. La simplicité d'abord

Le minimum de code qui résout le problème. Rien de spéculatif.
- Pas de fonctionnalité au-delà de la demande.
- Pas d'abstraction pour du code à usage unique.
- Pas de « flexibilité » ou « configurabilité » non demandée
  (exception : ce que le JSON pivot paramètre déjà).
- Pas de gestion d'erreur pour des scénarios impossibles — mais le code reste
  **défensif** sur les entrées externes (JSON pivot, trames Modbus, fichiers).
- 200 lignes qui pourraient en faire 50 : réécris.
Test : « un ingénieur senior dirait-il que c'est sur-conçu ? » Si oui, simplifie.
(Les commentaires pédagogiques ne comptent pas dans la longueur.)

## 4. Modifications chirurgicales

Ne touche que le nécessaire. Ne nettoie que ton propre désordre.
- N'« améliore » pas le code, les commentaires ou le formatage adjacents.
- Ne refactore pas ce qui n'est pas cassé.
- Respecte le style existant, même si tu ferais autrement.
- Code mort préexistant : signale-le dans `docs/dettes.md`, ne le supprime pas.
- Si tes modifications rendent des imports/variables/fonctions orphelins,
  supprime-les.
Test : chaque ligne modifiée doit se rattacher directement à la demande.

## 5. Exécution pilotée par les objectifs

Définis les critères de succès. Boucle jusqu'à vérification.
- « Ajouter la validation » → « écrire les tests des entrées invalides, les faire passer »
- « Corriger le bug » → « écrire un test qui le reproduit, le faire passer »
- « Refactorer X » → « tests verts avant et après »
Pour les tâches multi-étapes, énonce un plan bref :
1. [Étape] → vérification : [contrôle]
2. [Étape] → vérification : [contrôle]

Ces règles fonctionnent si : moins de changements inutiles dans les diffs, moins de
réécritures pour sur-complexité, et des questions de clarification **avant**
l'implémentation plutôt qu'après les erreurs.

## 6. Arborescence de référence

```
/
├── CLAUDE.md                  ← ce fichier
├── .claude/commands/          ← /conception /sprint /audit /refactor /test_suite
├── docs/
│   ├── memory.md              ← décisions actées (source de vérité)
│   ├── journal.md             ← chronologie des sprints
│   ├── dettes.md              ← dettes techniques et simplifications assumées
│   ├── backlog.md             ← phases et tâches à venir
│   ├── notes/                 ← NOTES_sprint_XX.md pédagogiques
│   └── sprints/               ← briefs de sprint autosuffisants
├── pivot/machine_carrousel.json  ← contrat central
├── testbench/                 ← Python : émulateur I/O Scanner + pytest
└── runtime/                   ← projet Godot 4 C# (serveur Modbus + simulation + 3D)
```

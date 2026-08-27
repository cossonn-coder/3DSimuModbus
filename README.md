# 3DSimuModbus

Simulateur 3D de machines industrielles piloté par un automate Schneider M580 via Modbus TCP.

Le démonstrateur actuel simule un **carrousel à palettes** (convoyeur circulaire, 2 vérins bloqueurs monostables, capteurs de présence) dans une maquette 3D générée procéduralement depuis un fichier JSON pivot. L'application Godot 4 agit comme **serveur Modbus TCP** ; l'automate M580 réel la scrute via l'**I/O Scanner** de Control Expert (2 lignes de scan : FC3 lecture retours, FC16 écriture commandes).

## Vision

Le carrousel n'est pas le produit — c'est le véhicule de validation. La cible est un outil capable de **générer automatiquement** une simulation 3D connectée en Modbus à un automate réel, à partir de :

- un modèle 3D de la machine,
- un fichier DWG (implantation),
- un schéma électrique PDF,
- tout document complémentaire nécessaire.

Le pipeline envisagé : **extraction Python → pivot JSON → runtime Godot**. Le pivot JSON est le pont entre les deux mondes : écrit à la main aujourd'hui, il sera produit par le pipeline d'extraction demain. Chaque fonctionnalité est pensée « générique via le pivot », jamais spécifique au carrousel.

## Architecture

```
                 ┌──────────────────────────────────┐
                 │          M580 réel               │
                 │   (Control Expert / I/O Scanner) │
                 └──────┬───────────────┬───────────┘
                   FC16 │ (commandes)   │ FC3 (retours)
                        ▼               ▲
                 ┌──────────────────────────────────┐
                 │     Serveur Modbus TCP (:502)    │
                 │        FluentModbus 5.3.2        │
                 ├──────────────────────────────────┤
                 │       ModbusDataStore             │
                 │   (ushort[] cmd/ret + verrou)     │
                 │    ← source de vérité Arch A →    │
                 ├──────────────────────────────────┤
                 │    CarrouselSimulation            │
                 │  (cinématique scriptée, 10 Hz)    │
                 ├──────────────────────────────────┤
                 │       Godot 4 / C#               │
                 │  (scène 3D procédurale, HUD,     │
                 │   caméra orbitale, panneau)       │
                 └──────────────────────────────────┘
                              ▲
                              │
                 ┌────────────┴─────────────┐
                 │   pivot/machine_carrousel │
                 │         .json            │
                 │   (contrat central)      │
                 └──────────────────────────┘
```

**Principes clés :**

- **Arch A** — Le `ModbusDataStore` est la source de vérité. Le thread du serveur Modbus ne touche jamais le scene tree Godot. La boucle de simulation fait un snapshot des commandes en début de tick physique et publie les retours en fin de tick.
- **Cinématique déterministe** — Pas de moteur physique (pas de Jolt, pas de joints). Vérin = position 0→1 interpolée sur `travel_time_ms`, capteurs à seuils. Tous les paramètres viennent du JSON pivot.
- **Adressage Modbus** — `%MWn` côté Control Expert = holding register d'adresse protocole `n`. Aucun décalage. Zones à base configurable, adresses relatives `{zone, word, bit}` dans le pivot.

## Stack technique

| Composant | Technologie |
|---|---|
| Runtime 3D | Godot 4.6 (.NET / C#) |
| Serveur Modbus | FluentModbus 5.3.2 (NuGet) |
| Logique de simulation | C# pur (class library `CarrouselCore`, testable sans Godot) |
| Banc de test | Python 3 — pytest, pymodbus ≥3.7 |
| Pivot | JSON (v0.2, écrit à la main en Phase 0) |
| Hôte | Windows, port TCP 502, unit ID 1 |

## Structure du dépôt

```
/
├── CLAUDE.md                     # Invariants et règles de conception
├── pivot/
│   └── machine_carrousel.json    # Contrat central (le pivot)
├── runtime/
│   ├── core/                     # CarrouselCore (class library .NET pure)
│   │   ├── PivotModel.cs         #   Loader du pivot JSON
│   │   ├── ModbusDataStore.cs    #   Tampon cmd/ret thread-safe
│   │   ├── CylinderState.cs      #   Cinématique vérin monostable
│   │   ├── ConveyorState.cs      #   Retour convoyeur (KM1_AUX)
│   │   ├── PalletSet.cs          #   Rotation, accumulation, présence
│   │   └── CarrouselSimulation.cs#   Composition root
│   ├── server/                   # CarrouselServer (FluentModbus)
│   │   └── ModbusServer.cs       #   Pont datastore ↔ buffer Modbus
│   ├── simhost/                  # Hôte console headless (boucle sim sans Godot)
│   ├── scenes/                   # Scène Godot (CarrouselScene, OrbitCamera, etc.)
│   ├── tests/                    # Tests xUnit du core
│   ├── server.tests/             # Tests d'intégration du serveur
│   └── scripts/                  # Scripts de démo (demo_sprint_NN.ps1)
├── testbench/                    # Python : émulateur I/O Scanner + pytest
│   ├── io_scanner_sim.py         #   Simulateur de l'I/O Scanner M580
│   ├── pivot_loader.py           #   Loader Python du pivot (symétrie C#)
│   ├── test_pivot_mapping.py     #   Tests de mapping (17 cas)
│   └── test_modbus_chain.py      #   Tests chaîne complète FC3/FC16 (4 scénarios)
└── docs/
    ├── memory.md                 # Décisions actées (source de vérité)
    ├── journal.md                # Chronologie des sprints
    ├── dettes.md                 # Dettes techniques
    ├── backlog.md                # Phases et tâches à venir
    └── sprints/                  # Un dossier par sprint (amorces, NOTES, overview)
```

## Prérequis

- **Windows** (hôte du runtime Godot)
- **Godot 4.6 .NET** (avec le SDK .NET 8+)
- **Python 3** avec `pymodbus >= 3.7` et `pytest` (pour le banc de test)
- **Port TCP 502** libre (ou adapter `modbus.port` dans le pivot)
- Règle de pare-feu Windows entrante autorisant TCP 502

Pour le banc de test Python :

```bash
cd testbench
pip install -r requirements.txt
```

## Lancement

### Scène Godot (mode normal — attente du M580)

Ouvrir le projet `runtime/` dans Godot 4.6 .NET, puis F5. La scène démarre le serveur Modbus sur le port 502 et attend les trames de l'I/O Scanner.

### Démo guidée (sans M580, avec le simulateur Python)

Chaque sprint livre un script PowerShell de démo qui joue le rôle du M580 via `io_scanner_sim.py` :

```powershell
# Lancer d'abord la scène Godot (F5), puis dans un terminal :
cd runtime/scripts
.\demo_sprint_04.ps1
```

Le script vérifie que le port 502 est bien occupé par la scène, puis enchaîne les scénarios en décrivant ce qu'il faut observer dans la 3D.

### Hôte headless (SimHost — sans Godot)

Pour exécuter la simulation sans interface graphique (utile pour les tests automatisés) :

```bash
cd runtime/simhost
dotnet run
```

### Tests

```bash
# Tests C# (core + serveur) — 95 verts attendus
cd runtime
dotnet test

# Tests Python (chaîne Modbus complète) — nécessite SimHost ou scène en écoute sur :502
cd testbench
pytest -v
```

## Machine simulée (carrousel V1)

Le pivot `machine_carrousel.json` décrit un carrousel circulaire à 3 palettes avec 2 postes de blocage :

| Repère | Type | Description | Commande | Retours |
|---|---|---|---|---|
| KM1 | Convoyeur circulaire | Rotation CCW, 20°/s | `%MW100.0` | KM1_AUX `%MW201.6` |
| YV1 | Vérin bloqueur (90°) | Monostable, 500 ms | `%MW100.1` | S11 `%MW201.0`, S12 `%MW201.1` |
| YV2 | Vérin bloqueur (270°) | Monostable, 500 ms | `%MW100.2` | S21 `%MW201.2`, S22 `%MW201.3` |
| B1 | Capteur présence (90°) | Fenêtre ±8° | — | `%MW201.4` |
| B2 | Capteur présence (270°) | Fenêtre ±8° | — | `%MW201.5` |

Heartbeat : `%MW200` (mot 0 zone `ret`), incrémenté toutes les 100 ms, rollover 16 bits libre.

## Configuration I/O Scanner (Control Expert)

Deux lignes de scan à configurer dans l'I/O Scanner du M580 :

| Ligne | Fonction | Adresse serveur | Zone PLC | Taille |
|---|---|---|---|---|
| Lecture (FC3) | Retours sim → PLC | Base `%MW200` | 2 mots | Holding registers 200–201 |
| Écriture (FC16) | Commandes PLC → sim | Base `%MW100` | 1 mot | Holding register 100 |

Paramètres réseau : IP de la machine Windows, port 502, unit ID 1.

## État d'avancement

### Phases terminées

- **Phase 0** — Modèle pivot écrit et validé
- **Phase 1** — Chaîne Modbus bout en bout (datastore, serveur, simulation, 3D statique)
- **Phase 1bis** — Cinématique 3D (animation depuis la simulation, boucle 10 Hz)
- **Phase 1ter** — Robustesse (échecs bruyants, traçabilité par élément)
- **Phase 1quater** — Ergonomie (caméra orbitale, panneau des éléments, surbrillance croisée, plein écran)

### Prochaines étapes

- **Phase 4** — Intégration M580 réelle (campagne avec le programme PLC de l'automaticien)
- **Phases 2–3** — Pipeline d'extraction Python (DWG → géométrie, PDF schéma → composants → pivot)
- **Phase 5** — IHM automaticien (édition du mapping, forçage, injection de défauts)

### Banc de tests

95 tests C# (xUnit) + 4 scénarios pytest chaîne complète. Build Godot 0 erreur.

## Conventions

- **Identifiants** en anglais, **commentaires** en français (pédagogiques, expliquent le *pourquoi*).
- Vocabulaire Schneider obligatoire : repères KM/YV/S/B, `%MW`, I/O Scanner, TOR, monostable.
- Chaque sprint produit un `NOTES.md` détaillé et un script de démo `demo_sprint_NN.ps1`.
- Le pivot JSON est le contrat central — toute évolution fonctionnelle commence par ce fichier.

## Licence

Projet privé.

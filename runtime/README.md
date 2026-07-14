# runtime/ — Projet Godot 4 (C#)

Runtime **autonome** du demonstrateur : charge le pivot JSON, construit la scene 3D,
execute la simulation cinematique scriptee et fait office de **serveur Modbus TCP**
scrute par le M580 (via l'I/O Scanner de Control Expert).

> Etat : squelette de projet pose (`project.godot`, `.csproj`, `.gitignore`).
> Le **code des 3 briques n'est pas encore genere** : conformement a la regle
> « architecture avant code » (`CLAUDE.md`), il passe d'abord par `/conception` pour
> validation des interfaces, puis livraison **fichier par fichier**.

## Prerequis

- Godot 4.3 **.NET** (build Mono/C#).
- .NET SDK 8.
- Hote **Windows** (cible du projet).

## Architecture cible (a valider via `/conception` avant generation)

Trois briques a frontieres nettes. **Regle de thread-safety imposee** : le datastore
est un objet C# pur ; le thread du serveur Modbus ne touche **jamais** le scene tree.

```
   M580 (client, I/O Scanner)
        │  FC3 (lit ret) / FC16 (ecrit cmd), port 502
        ▼
┌─────────────────────┐      thread serveur (FluentModbus)
│  ModbusServer       │  ── ne touche jamais le scene tree ──┐
│  (FluentModbus)     │                                      │
└─────────┬───────────┘                                      │
          │ lit/ecrit sous verrou                            │
          ▼                                                  │
┌─────────────────────┐   objet C# PUR (ushort[] cmd/ret + verrou), sans dep. Godot
│  ModbusDataStore    │                                      │
│  Snapshot / Publish │                                      │
└─────────┬───────────┘                                      │
          │ snapshot cmd (debut tick) / publish ret (fin tick)
          ▼                                                  │
┌─────────────────────┐   thread Godot (_PhysicsProcess)     │
│  SimulationRoot     │  ◄───────────────────────────────────┘
│  cinematique scriptee: convoyeur (rotation continue),
│  bloqueurs YV1/YV2 (interpolation travel_time_ms),
│  capteurs a seuils, heartbeat +1/100 ms
│  + scene 3D (Convoyeur, Bloqueur1/2, palettes)
└─────────────────────┘
```

### Briques prevues

1. **`PivotModel` / loader** — DTO C# + parsing defensif de
   `../pivot/machine_carrousel.json` (echec clair si adresse hors zone, conflit,
   champ manquant). Miroir C# du `pivot_loader.py` cote testbench.
2. **`ModbusDataStore`** — objet C# pur : `ushort[]` pour `cmd` et `ret`, verrou,
   `SnapshotCommands()` (copie coherente pour le tick), `PublishReturns(...)`,
   accesseurs bit/mot resolus depuis base + offset. **Aucune dependance Godot** →
   testable hors moteur.
3. **`ModbusServer`** — enveloppe FluentModbus (TCP 502) branchee sur le datastore,
   thread dedie. Strategie de synchro figee **apres le POC D-001**.
4. **`SimulationRoot`** (Node Godot) — orchestre le tick : snapshot cmd → cinematique
   → heartbeat → publish ret ; porte la scene 3D statique.

## Points ouverts avant code

- **D-001** : comportement thread-safe reel de FluentModbus (buffer interne, `Lock`,
  hooks de requete). POC isole a mener en premier (voir `docs/dettes.md` et
  `docs/sprints/sprint_01_brief.md`).
- Choix : datastore comme source de verite recopiee vers le buffer FluentModbus, ou
  usage direct du buffer FluentModbus sous son propre verrou → a trancher au POC.

## Lancer (une fois la scene ajoutee)

```bash
# Build C# + ouverture dans l'editeur Godot 4 .NET, puis F5.
# Le serveur Modbus ecoutera sur 0.0.0.0:502 ; valider avec testbench/ :
#   python -m pytest ../testbench/test_modbus_chain.py -v
```

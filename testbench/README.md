# testbench/ — Simulateur d'I/O Scanner + tests (Python)

Outil Python **hors-ligne** qui joue le role du **M580** face au serveur Modbus du
runtime Godot. Sert a valider la chaine Modbus de bout en bout (Sprint 1) sans dependre
de la disponibilite de l'automate reel.

> Rappel topologie : Godot = **serveur** Modbus TCP (FluentModbus, port 502).
> Le M580 (ici simule) est **client** : **FC3** pour lire les retours (`ret`),
> **FC16** pour ecrire les commandes (`cmd`). Pas de FC23 en V1.

## Contenu

| Fichier | Role |
|---|---|
| `pivot_loader.py` | Charge `pivot/machine_carrousel.json`, resout les adresses **absolues** `%MW` (base + offset). Aucune adresse en dur ailleurs. |
| `conftest.py` | Fixtures pytest : `mapping` (table resolue), `modbus_client` (skip si serveur absent). |
| `test_pivot_mapping.py` | Tests **unitaires** du contrat (zones, heartbeat, B3→KM1_AUX, conflits, JSON malforme). Tournent sans serveur. |
| `test_modbus_chain.py` | Tests d'**integration** de la chaine (DoD Sprint 1). Skippent si le runtime Godot n'ecoute pas. |
| `io_scanner_sim.py` | Utilitaire manuel : scrute le serveur a cadence fixe, force des commandes, decode les retours. |

## Installation

```bash
cd testbench
python -m venv .venv && . .venv/bin/activate    # ou .venv\Scripts\activate sous Windows
pip install -r requirements.txt
```

## Lancer les tests

```bash
# Tests unitaires (aucun serveur requis) — passent tout de suite :
python -m pytest test_pivot_mapping.py -v

# Chaine complete (necessite le runtime Godot en ecoute sur :502) :
python -m pytest test_modbus_chain.py -v
# -> skippent proprement si aucun serveur ne repond
```

Cible configurable par variables d'environnement :
`MODBUS_HOST` (defaut 127.0.0.1), `MODBUS_PORT` (502), `MODBUS_UNIT` (1).

## Inspecter la table resolue (pratique en SSH)

```bash
python pivot_loader.py         # affiche zones + signaux avec leurs adresses %MW
```

## Scruter le serveur a la main

```bash
python io_scanner_sim.py --km1 1 --period 0.1     # met KM1=1 puis scrute a 10 Hz
python io_scanner_sim.py --yv1 1 --cycles 20      # sort le bloqueur 1, 20 scans
```

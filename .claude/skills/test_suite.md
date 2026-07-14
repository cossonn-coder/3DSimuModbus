---
description: Construire / faire tourner la suite de tests (pytest testbench + logique C#)
---

Gere la suite de tests : **$ARGUMENTS** (ex. `run`, `add <cas>`, `cover <module>`).

Philosophie (Nico vient du HIL / pytest / Robot Framework) : des tests **lisibles**,
en **assertions explicites**, avec **fixtures** claires. On ne code jamais une adresse
Modbus en dur : on la resout depuis le pivot JSON.

### `run`
- Lance `pytest` dans `testbench/` (`python -m pytest -v`).
- Rapporte : total, passes, echoues, skippes. Les tests d'integration Modbus qui
  requierent un serveur Godot en ecoute doivent **skipper proprement** s'il est absent
  (pas d'echec du a l'environnement).
- En cas d'echec, montre la sortie reelle (pas de reformulation optimiste).

### `add <cas>`
- Ajoute un cas de test au bon fichier `testbench/test_*.py`.
- Resout les adresses via le loader pivot (`pivot_loader`), jamais en dur.
- Structure : Arrange (etat commande via FC16) / Act (cadence de scan) / Assert
  (retours lus via FC3).

### `cover <module>`
- Analyse la couverture logique du module cible et propose les cas manquants
  (bornes d'adresses, rollover heartbeat, rappel ressort monostable, seuils capteurs,
  JSON malforme...).

Cote C# : privilegie une logique (datastore, cinematique) **testable hors Godot** ;
signale ce qui ne peut se tester qu'en lancant le runtime.

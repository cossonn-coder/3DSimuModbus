# dettes.md — Dettes techniques et simplifications assumées

Règle : chaque entrée a un identifiant, une date, une criticité (bloquant / à
surveiller / cosmétique) et une condition de remboursement. On n'y range PAS les
commentaires pédagogiques (jamais considérés comme dette).

| ID | Date | Criticité | Description | Remboursement |
|---|---|---|---|---|
| D-001 | 2026-07 | À surveiller | FluentModbus pressenti mais **comportement réel non vérifié** (support serveur FC3/FC16, modèle de threading, verrou d'accès au datastore, compatibilité .NET de Godot 4). | Test de validation en tout début de sprint 1 ; repli = mini-serveur Modbus maison (FC3/FC16, ~300 lignes testables). |
| D-002 | 2026-07 | Cosmétique | Collision latérale non simulée : si un vérin sort pendant qu'une palette franchit exactement le point d'arrêt, la palette n'est bloquée que si son bord avant n'a pas franchi le point. | V2 si besoin réel constaté par l'automaticien. |
| D-003 | 2026-07 | Cosmétique | Convoyeur sans rampe (arrêt/démarrage instantanés des palettes). `feedback_delay_ms` ne modélise que le contacteur. | V2 si les temporisations PLC l'exigent. |
| D-004 | 2026-07 | À surveiller | FC23 (ligne RD+WR combinée) non supporté/testé en V1 — choix assumé : deux lignes de scan. | Si l'automaticien veut une ligne combinée : valider FC23 de la lib ou l'implémenter. |
| D-005 | 2026-07 | À surveiller | Pas de validation de schéma JSON formalisée (JSON Schema) : le chargeur Godot doit être défensif à la main en V1. | Introduire un JSON Schema quand le pipeline Python (Phases 2-3) produira le fichier. |

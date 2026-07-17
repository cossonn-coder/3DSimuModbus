# S5.5 — `demo_sprint_05.ps1` + sortie observable

> Sous-sprint **observable** (clôture). Reprise à froid : `CLAUDE.md` + `00_etat.md` + cette amorce.
> Règle permanente : le dernier sous-sprint livre une **sortie observable** (scène animée + démo
> guidée), banc **intact**.

## Objectif
Livrer `runtime/scripts/demo_sprint_05.ps1` : une **démo visuelle guidée** qui joue le M580 (via
`testbench/io_scanner_sim.py`) et enchaîne **automatiquement** les scénarios de défaut du sprint,
en annonçant à chaque phase **ce qu'il faut regarder** dans la 3D et **comment le PLC (io_scanner)
réagit** — pour que Nico et l'automaticien valident à l'œil, sans retaper les commandes.

## Fichiers touchés
- **Neuf** : `runtime/scripts/demo_sprint_05.ps1`.
- Lire pour comprendre : `runtime/scripts/demo_sprint_04.ps1` (patron : pré-vol 502, phases guidées,
  ASCII pur PowerShell 5.1), `testbench/io_scanner_sim.py` (émulateur I/O Scanner : commandes dispo).

## Contrat / structure visée (calquée sur `demo_sprint_04.ps1`)
- **Pré-vol obligatoire** du port 502 : refuser de démarrer si un `SimHost` reliquat occupe le port,
  ou si la scène Godot n'est pas lancée (rappeler à Nico de faire F5 d'abord). ASCII pur.
- Le script **guide** (bannières de phase + pauses), l'injection des défauts se fait **dans l'IHM**
  (clic/menu) : le script **dit quoi cliquer** et **quoi observer**, pendant que `io_scanner_sim.py`
  scrute et **affiche les retours** que le PLC voit (fins de course, présence, KM1_AUX, heartbeat).
  → la démo montre la **corrélation** « défaut injecté à l'écran ↔ ce que le M580 détecte ».

## Phases guidées (au moins ces scénarios — un par famille)
1. **Nominal** : convoyeur en marche, palettes tournent, vérins commandés → S12/S22 confirment,
   heartbeat défile. (Référence avant défauts.)
2. **Vérin ne sort pas** (YV1) : commander l'extension, observer la tige qui **ne monte pas** et
   `S12` qui **ne vient jamais** (le PLC voit « commande sans confirmation fin de course »).
3. **Vérin coincé mi-course** (YV2) : déclencher pendant l'extension → tige **figée** à mi-hauteur,
   ni `S21` ni `S22` (état incohérent détectable).
4. **Capteur bloqué** : `S12` bloqué à 1 (fin de course « sortie » collée alors que la tige est
   rentrée) **ou** `B1` bloqué à 1 (présence permanente) — le PLC lit un capteur menteur.
5. **Convoyeur patine** : `KM1_AUX`=1 (marche confirmée) mais **palettes immobiles** et `B1`/`B2`
   inchangés (glissement/perte d'entraînement détectable sans codeur).
6. **Gel des retours** : heartbeat **figé** côté io_scanner alors que la 3D continue de bouger
   (watchdog PLC).
7. **Coupure TCP** : io_scanner **perd la connexion** (défaut de scrutation) ; « Réparer » → il
   rescanne.
8. **Mode aveugle** (bref) : rejouer un défaut avec le marquage masqué — montrer que la sim se
   comporte anormalement **sans indice visuel** (test de diagnostic à l'aveugle).

Chaque phase : bannière « CE QU'IL FAUT REGARDER » + ce que io_scanner doit afficher. Pauses
lisibles (le rythme laisse le temps d'observer). Réparer entre les phases pour repartir du nominal.

## Definition of Done (cochable)
- [ ] `demo_sprint_05.ps1` s'exécute sous PowerShell 5.1, ASCII pur, pré-vol 502 opérationnel.
- [ ] Les 8 phases s'enchaînent avec bannières claires ; `io_scanner_sim.py` tourne en parallèle
  et montre les retours vus par le PLC.
- [ ] La démo se termine proprement (défauts réparés, io_scanner arrêté, port relâché).
- [ ] Scène Godot **intacte** (aucune régression visuelle) ; banc **intact**.

## Banc attendu — **inchangé / intact**
Aucune modification de code de production : xUnit **95+N** (core) et **serveur +M** inchangés,
**4 pytest inchangés**, build Godot 0 erreur. Seul un script de démo est ajouté.

## Vérif autosuffisante
- Lancer la scène (F5), puis `./runtime/scripts/demo_sprint_05.ps1` → les phases se déroulent,
  io_scanner affiche des retours cohérents avec chaque défaut.
- Validation finale Nico à l'œil (corrélation 3D ↔ retours PLC pour chaque famille de défaut).

## Ce qu'il NE faut PAS faire
- Ne pas automatiser l'injection des défauts en contournant l'IHM (la démo **valide l'IHM**) :
  le script guide, l'humain clique (ou utilise les raccourcis clavier). Exception tolérée : les
  **commandes** (marche/extension) viennent d'`io_scanner_sim.py` comme aux démos précédentes.
- Ne pas toucher au code de production (core/serveur/scènes) : ce sous-sprint est **script seul**.
- Pas de dépendance non-ASCII, pas d'appel bloquant interactif (`Read-Host`) sans issue.

## Dépendances / validation manuelle
- **Dépendances** : **S5.1..S5.4** (toutes les capacités que la démo exerce).
- Validation manuelle : Nico, F5 + exécution du script (voir Vérif).

# S6.3 — Sortie observable : `demo_sprint_06.ps1` (démo guidée)

> **Cold-start** : lire `CLAUDE.md` + `docs/sprints/sprint_06/00_etat.md` + cette amorce suffit.
> Sous-sprint **observable** (règle permanente : tout sprint finit par une sortie démontrable).
> Dépend de l'UI de **S6.2**. Banc **inchangé** (aucun code C# touché). Modèle de référence :
> `runtime/scripts/demo_sprint_05.ps1` (structure `Invoke-Phase` / `Wait-Go` / pré-vol 502, ASCII pur).

## Objectif

Une démo guidée qui prouve les **deux histoires** du forçage, sans que le script clique à ta place
(la démo VALIDE l'IHM) :

1. **Pilotage IHM pur (sans PLC)** : des phases où **io_scanner n'est PAS lancé** (personne n'écrit
   `cmd`). Tu forces depuis l'IHM (KM1 run, YV1, YV2) et la machine bouge : le forçage **est** le
   pilote. On regarde la 3D + l'écart cmd (`PLC=0 → forcé 1`).
2. **Forçage malgré le PLC (avec scan)** : des phases où **io_scanner écrit `cmd`** à une valeur, et
   le forçage la **surclasse** à la lecture. On regarde : l'écart cmd, la 3D qui suit le forçage, et
   surtout **KM1_AUX=1 vu par le PLC sans qu'il l'ait commandé** (forcer KM1 run pendant que le
   scanner commande `--run 0`).

Ajouter une phase **`BlockerIneffective`** : forcer/commander YV1 à sortir (S12=1), injecter le défaut,
faire tourner le convoyeur → une palette **traverse** la tige levée (B1 monte puis retombe).

## Structure attendue (calquée sur demo_sprint_05.ps1)

- Même **pré-vol 502** (refuse si rien n'écoute, si c'est SimHost, avertit si pas Godot).
- Même `param(...)` (`-PyHost`, `-Port`, `-Prep`) et même mode interactif/auto (`Wait-Go`).
- Réutiliser `Invoke-Phase`. Pour les phases **sans PLC**, **ne pas** lancer io_scanner : soit un
  helper `Invoke-PhaseNoScan` (affiche consignes + pause, PAS d'appel Python), soit un flag sur
  `Invoke-Phase` qui saute l'appel `& $py`. Le pré-vol 502 reste requis (la **scène** tient le port ;
  simplement aucun M580 ne scrute pendant ces phases).
- ASCII pur (Windows PowerShell 5.1, pas d'accents ni de tirets longs).
- Raccourcis IHM à rappeler en tête (sprint 6) :
  ```
  A / Z : cycler la selection (AZERTY)      G : ouvrir le menu FORCAGE de la selection
  F     : menu DEFAUT                        R : reparer les defauts de la selection
  B     : mode aveugle                       clic 3D / ligne : selectionner
  menu Forcage d'une ligne : Auto / forcer a 0 / forcer a 1  (par signal cmd)
  ```

## Séquence de phases proposée (à ajuster si besoin)

1. **PILOTAGE SANS PLC — convoyeur** (no-scan) : force `KM1 run` à 1 (menu Forçage de KM1). Regarder :
   l'anneau **tourne**, les palettes avancent, KM1_AUX passe à 1 (écart cmd `PLC=0 → forcé 1`) — alors
   qu'**aucun M580 ne scrute**. C'est le forçage qui pilote.
2. **PILOTAGE SANS PLC — vérins** (no-scan) : force YV1 puis YV2 à 1 → les tiges **sortent**, S12/S22
   suivent. Puis Auto → elles rentrent. Prouve le pilotage bit par bit sans PLC.
3. **FORÇAGE MALGRÉ LE PLC** (scan `--run 0 --yv1 0 --yv2 0`) : garder YV1 forcé à 1. Regarder : le PLC
   commande tout à 0, mais YV1 **reste sortie** ; la cellule cmd montre `PLC=0 → forcé 1`.
4. **KM1_AUX NON COMMANDÉ** (scan `--run 0`) : forcer `KM1 run` à 1. Regarder : côté console PLC,
   **KM1_AUX=1** alors que le scanner commande la marche à 0 → « marche forcée localement, détectable ».
5. **FORÇAGE À 0 CONTRE LE PLC** (scan `--run 1 --yv1 1`) : forcer YV1 à **0**. Le PLC commande la
   sortie, mais la tige **reste rentrée** (S12=0) → le forçage à 0 masque la commande PLC.
6. **COMPOSITION FORÇAGE × DÉFAUT** (scan) : forcer YV1 à 1 **et** injecter « vérin : ne sort pas ».
   Regarder : la tige reste rentrée (le défaut physique **gagne** sur la commande forcée), S12=0 —
   comportement déterministe documenté.
7. **BLOQUEUR INEFFICACE** (scan `--run 1 --yv1 1`) : YV1 sort (S12=1), injecter « bloqueur inefficace ».
   Regarder : la tige est **levée** mais une palette **traverse** le poste 90° (B1 monte puis retombe) —
   « je crois bloquer, B1 se libère quand même ».
8. **RETOUR AU NOMINAL** : Auto sur tous les forçages, R sur les défauts, `--run 0 ...` → machine propre.

## Definition of Done (cochable)

- [x] `runtime/scripts/demo_sprint_06.ps1` créé, ASCII pur (confirmé), pré-vol 502, mode interactif/auto.
- [x] Phases **sans io_scanner** (P1-2 : pilotage IHM pur, `-NoScan`) **et** phases **avec scan** (P3
      forçage gagne, P4 KM1_AUX non commandé, P5 forçage à 0, P6 composition, P7 bloqueur inefficace).
- [x] Phase `BlockerIneffective` présente (P7).
- [x] Le script **ne modifie aucun code C#** (vérifié `git status`) ; banc **inchangé** ; build Godot **inchangé**.
- [x] Clôture : rappel de remettre tous les forçages à Auto et de réparer les défauts.

## Vérif autosuffisante

- Lint/exécution à blanc : lancer le script **sans** scène (`powershell -File runtime/scripts/demo_sprint_06.ps1`)
  → il doit **refuser proprement** (pré-vol : « rien n'écoute sur 502 ») et sortir code ≠ 0, sans erreur
  de parseur PowerShell (preuve que l'ASCII/here-strings sont sains).
- Validation complète = **F5 Godot + lancer la démo** (visuel, à valider par Nico).

## Ce qu'il NE faut PAS faire

- Ne pas cliquer à la place de l'utilisateur (la démo guide ; l'humain force/injecte dans l'IHM).
- Pas d'accents ni de caractères multi-octets (casse le parseur PS 5.1 en Windows-1252).
- Ne toucher à aucun `.cs` (sinon régression de banc/scope).
- Ne pas lancer Godot depuis le script (l'utilisateur fait F5 lui-même, comme demo_sprint_05).

## DÉPENDANCES / FICHIERS TOUCHÉS

- **Dépendances** : **S6.2** (colonne Forçage, écart cmd, touche `G`, cyclage `A`/`Z`).
- **Fichiers** : `runtime/scripts/demo_sprint_06.ps1` (neuf).

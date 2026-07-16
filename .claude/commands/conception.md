---
description: Cadrer et valider l'architecture AVANT d'ecrire du code, puis la figer en sous-sprints prets pour /sprint open
---

Tu vas cadrer l'architecture de : **$ARGUMENTS**.

Tu **concois avec Nico, tu n'executes pas**. Ce skill couvre l'amont : concevoir en
conversation -> durcir -> figer en amorces autosuffisantes. La realisation est le metier
de `/sprint open` (aval) ; tu t'arretes ou il commence.

Regles d'or (cf. `CLAUDE.md`) : **architecture avant code**, **le pivot JSON d'abord**,
**ne genere aucun fichier d'implementation** ici. Livrable = la **decomposition en
sous-sprints autosuffisants** (phase B), que `/sprint open` orchestrera via des sous-agents.

## 0bis. Carnet de bord vivant `00_etat.md` — autonomie contexte

Une conception longue fait gonfler le contexte ; une auto-compaction peut tomber en plein
milieu. Pour ne jamais perdre le fil : **des le 1er echange**, tiens un etat vivant sur
disque `docs/sprints/sprint_<NN>/00_etat.md`, mis a jour a **chaque decision arretee**. Il
contient : phase courante, **decisions arretees** (une ligne + pourquoi), **questions
ouvertes**, cout estime, **section REPRISE** (quoi relire + prochaine etape precise).

- **Reprise a froid** (apres auto-compaction ou `/clear`) : relire `CLAUDE.md` + ce
  `00_etat.md` suffit a continuer sans perte. L'auto-compaction devient un filet sans
  consequence — tu ne demandes JAMAIS de lancer `/compact` ni de « relire un HANDOFF ».
- **Nuance** avec « ne creer un fichier que sur OK » : `00_etat.md` est un carnet (il
  persiste ce qui est DEJA tranche), pas l'artefact fige. Les amorces et le doc de
  conception restent la phase B.

## A. Concevoir et challenger (DIALOGUE — ne fige rien)

Aucun fichier cree en phase A, **sauf** `00_etat.md`. Sortie = critique structuree a
l'ecran + arbitrages a remonter.

1. **Relire le contrat.** Ouvre `pivot/machine_carrousel.json`, `docs/memory.md`,
   `docs/dettes.md`, `docs/backlog.md`. Verifie si le besoin **impacte le pivot** : si oui,
   propose d'abord l'evolution du pivot (le pivot change avant le code). Au besoin, lis le
   code source de verite pour confirmer ce qui existe REELLEMENT (une fonction, un registre,
   un etat) avant de t'appuyer dessus.
2. **Presenter l'architecture**, sans code :
   - responsabilite du module en une phrase
   - interfaces publiques (signatures, entrees/sorties, types)
   - dependances et frontieres (ce qu'il ne fait PAS)
   - C# runtime : ou passe la frontiere thread serveur / scene tree / datastore
   - Python : structure des tests pytest envisages
3. **Challenger** (pas de fausse certitude) :
   - **Hypotheses metier** — realisme du modele (physique carrousel, timing capteurs/verins,
     semantique Modbus/Schneider). Une hypothese fausse se signale.
   - **Trous et couplages** — effets oublies, couplages non vus. Tout ce que la mecanique
     neuve touche (simulation <-> vue 3D <-> couche Modbus <-> contrat pivot) doit etre relie.
   - **Invariants** — toute proposition qui viole un invariant `CLAUDE.md` est un drapeau
     rouge (app qui « decide » au lieu de servir, divergence de mapping, FC23 non decide,
     indeterminisme).
   - **Cout chiffre** — perf, complexite, risque d'integration automate. Budget indicatif par
     sous-sprint ; une idee couteuse se concoit econome ou se reporte.
4. **Points durs et incertitudes** : nomme-les, rattache-les a une dette existante
   (`docs/dettes.md`) ou propose-en une nouvelle.
5. **Figer les decisions ouvertes.** Arbitrages en **QCM** : max **3 questions par tour**,
   chacune avec avant-propos pedagogique + **ta recommandation** (option « (Recommande) »
   placee en premier). Attends les reponses. Boucle si plus de 3 points restent ouverts.
   **Reporte chaque arbitrage tranche aussitot dans `00_etat.md`.**

Pour un sujet hors de l'expertise de Nico : ne balance pas des questions brutes — produis
un court doc a etudier (mecanismes, options, ta reco), puis laisse decider.

## B. Figer (UNIQUEMENT sur accord explicite : « OK / vas-y »)

Un fichier a la fois (SSH mobile), sauf synchro coherente demandee.

1. **Doc de conception / evolution pivot** si pertinent : reflete la conception arbitree
   (pas la version challengee).
2. **Decomposer en SOUS-SPRINTS autosuffisants** (souvent 2-5, chacun verifiable seul ; ne
   **sur-decoupe pas** : chaque sous-sprint a froid repaie son amorcage). Coupe selon des
   lignes nettes (ex. fonctionnel/headless vs presentation/visuel).
   **REGLE PERMANENTE** : le dernier sous-sprint est une **sortie observable** (scene Godot
   animee + `demo_sprint_<NN>.ps1`, banc intact). On ne differe jamais le rendu ou la
   demonstrabilite en dette separee. Un sprint sans sortie observable est incomplet.
3. **Un dossier par sprint** `docs/sprints/sprint_<NN>/` (regroupe TOUT le sprint, pour
   navigation humaine facile) : **une amorce par sous-sprint**
   `docs/sprints/sprint_<NN>/brique_<MM>_<slug>.md`, plus un **overview**
   `docs/sprints/sprint_<NN>/overview.md` qui les indexe. Chaque amorce autosuffisante
   contient :
   - objectif ; **contrat d'API vise** ; decisions pre-tranchees + questions residuelles ;
   - **Definition of Done cochable** ; **verif autosuffisante** (comment prouver le vert sans
     contexte externe) ;
   - **banc attendu** : `dotnet test` + `pytest testbench/test_modbus_chain.py` **inchange**,
     OU **re-fige** (nouveau temoin + raison, prevu par l'amorce ; sinon c'est une regression) ;
   - **ce qu'il NE faut PAS faire** ; points de validation manuelle eventuels (F5 Godot / test
     sur M580 reel) ;
   - **DEPENDANCES** (quels sous-sprints precedent) et **FICHIERS TOUCHES** (deux sous-sprints
     partageant un fichier sont forcement **sequentiels**).
   Test decisif : un sous-agent qui demarre **a froid** doit pouvoir livrer le sous-sprint en
   lisant seulement `CLAUDE.md` + `00_etat.md` + son amorce.
4. **Finalise `00_etat.md`** en etat de sprint (~20 lignes : ou on en est, decisions cles,
   carte des sous-sprints, ce qui reste, section REPRISE) — desormais tenu a jour par chaque
   sous-sprint pendant l'execution.

## C. Livrer

1. **git** — verifie l'arbre AVANT. S'il est sale hors de ton travail, previens ; ne stash
   pas de toi-meme.
2. **Commit** (doc + amorces + `00_etat.md`), message style projet en HEREDOC. Ajoute les
   fichiers nommement (pas de `git add -A` aveugle), jamais `--no-verify`. Push si un remote
   existe (cf. `memory.md` : commit + push autonomes) ; sinon commit local et signale-le.
3. Mets a jour **dette** / **journal** si une decision notable a ete prise.
4. **Message final** : donne le message exact a ecrire apres `/clear` :
   > Conception figee, sprint `<NN>` pret (N sous-sprints, banc <inchange / re-fige au
   > sous-sprint X, annonce>). Tu peux `/clear`, puis lance : `/sprint open <NN>`

## Ce que ce skill ne fait JAMAIS

- **Implementer** — aucun code au-dela d'une lecture de verification (c'est le metier de
  `/sprint open`).
- **Figer sans accord** — la phase B ne demarre pas tant que la conception n'est pas validee.
- **Sur-decouper** — autant de sous-sprints que necessaire, sans morcellement gratuit.
- **Re-figer le banc en douce** — tout re-figeage doit etre prevu et justifie par une amorce.

Aligne le vocabulaire sur Control Expert / EcoStruxure (KM/YV/S/B, %MW, I/O Scanner, TOR,
monostable). En cas de doute sur une convention Schneider : pose la question.

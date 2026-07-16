---
description: Ouvrir (orchestrer en autonomie) ou cloturer un sprint (brief, notes, journal, memory, dettes, backlog)
---

Gere le cycle de vie d'un sprint : **$ARGUMENTS** (ex. `open 03`, `close 03`, `status`).

Un **dossier par sprint** regroupe tout : `docs/sprints/sprint_<NN>/` contient les amorces
`brique_<MM>_*.md`, l'`overview.md`, le carnet vivant `00_etat.md` et, a la cloture,
`NOTES.md`. Docs projet transverses : `docs/journal.md`, `docs/memory.md`, `docs/dettes.md`,
`docs/backlog.md`.

Le **banc de verification** du projet (le golden a preserver) : `dotnet test` (core +
serveur) et, quand un serveur ecoute sur :502, `pytest testbench/test_modbus_chain.py`.
Un sous-sprint **preserve le banc** OU le **re-fige de facon annoncee** (prevue par son
amorce). Un re-figeage non prevu par l'amorce est une **regression**, pas un fait acquis.

### Si `open <NN>`

L'architecture et la decomposition ont deja ete produites par `/conception` dans
`docs/sprints/sprint_<NN>/` (amorces `brique_<MM>_*.md` + `overview.md` + `00_etat.md`).
`open` **ne (re)concoit rien** : il **orchestre la realisation en autonomie**, puis enchaine
la cloture.

**0. Preconditions — verifie TOUT avant de lancer le moindre sous-agent.** Si une seule
echoue : **stop net**, explique, ne lance rien.
- **git a jour** (`git status`). S'il est sale hors de ce qui va etre produit, previens et
  demande — ne commit/stash pas de toi-meme.
- Le dossier `docs/sprints/sprint_<NN>/` existe avec ses **amorces** et son `00_etat.md`.
  Sinon : « la conception n'est pas figee, lance `/conception <sujet>` d'abord ». `open`
  **execute**, il ne concoit jamais.
- Lis `00_etat.md` (section REPRISE + marqueurs) pour etablir la **liste ordonnee des
  sous-sprints RESTANTS**. Si tous sont clos : dis-le, ne lance rien.

**1. Ordonnancer.** Trie par DEPENDANCES. Deux sous-sprints partageant un FICHIER TOUCHE
sont **sequentiels**. Passe les items concernes du backlog en `[~]`, ajoute une entree
d'ouverture au journal. **Annonce la liste** des sous-sprints que tu vas enchainer.

**2. Boucle orchestrateur — SEQUENTIELLE STRICTE, AUTONOME.** Le temoin passe par
`00_etat.md`. **Jamais deux sous-agents en parallele. Jamais de pause de validation.** Pour
chaque sous-sprint restant, dans l'ordre :

  **a. Lance UN sous-agent** (Agent, `general-purpose`), **contexte VIERGE** (cold-start).
  Prompt autosuffisant :
  > Tu travailles sur le projet 3DSimuModbus (`C:\Users\Nicol\ops\3DSimuModbus`). Lis
  > d'abord `CLAUDE.md`, puis `docs/sprints/sprint_<NN>/00_etat.md`, puis l'amorce
  > `docs/sprints/sprint_<NN>/brique_<MM>_*.md`. **Implemente UNIQUEMENT ce sous-sprint**,
  > rien d'autre, en respectant tous les invariants du projet (Arch A, thread-safety,
  > pivot = source de verite, determinisme, langue, commentaires pedagogiques jamais
  > effaces). Avant de finir, OBLIGATOIRE :
  > - Lance le **banc** pertinent (`dotnet test` ; si le sous-sprint touche la chaine
  >   full-chain, `pytest testbench/test_modbus_chain.py` contre un serveur qui ecoute).
  >   Note s'il est **inchange** ou **re-fige** (nouveau temoin + raison). Un re-figeage
  >   n'est legitime QUE si l'amorce le prevoit ; sinon corrige, ne fige pas.
  > - Coche la DoD de l'amorce. Mets a jour `docs/sprints/sprint_<NN>/00_etat.md`
  >   (avancement + REPRISE).
  > - **NE COMMIT PAS, ne push pas.** Laisse les changements, l'orchestrateur commit.
  > - Termine en purgeant ton contexte (`/clear`).
  > Rends un rapport < 200 mots : (1) ce que tu as fait, (2) banc inchange / re-fige+raison,
  > (3) build vert/rouge/non concerne, (4) fichiers touches, (5) validations manuelles
  > requises (F5 / M580 reel), (6) tout blocage ou decision de design imprevue.

  **b. Verifie (trust but verify).** `git diff --stat` ; **relance le banc toi-meme** si le
  rapport est ambigu. Le rapport dit ce que le sous-agent COMPTAIT faire, pas forcement ce
  qui est sur le disque.

  **c. Decide.**
  - **Sain** (banc inchange OU re-figeage prevu ; build vert ou non concerne ; pas de
    blocage) -> **commit** (message style projet, HEREDOC, fichiers nommement, jamais
    `--no-verify` ni `git add -A` aveugle ; push si remote), puis sous-sprint suivant.
  - **Echec bloquant** -> tu **t'arretes** (voir plus bas). C'est le **seul** cas
    d'interruption.

**3. Cloture auto-enchainee** (tous les sous-sprints verts). Enchaine sans rendre la main :
deroule la section **`close <NN>`** ci-dessous (NOTES, journal, memory, dettes, backlog,
DoD). C'est **la** que Nico reprend, au rapport final consolide : sous-sprints faits, temoin
de banc final, dettes nees, **liste precise des validations manuelles** (F5 / M580 reel), ce
qui reste.

**Echec bloquant = seule cause d'arret anticipe.** Stoppe (ne commit pas de casse, ne lance
pas le suivant) si : le banc casse de facon **non prevue** par l'amorce ; un sous-sprint
re-fige le banc sans que l'amorce le prevoie ; `dotnet build` reste **rouge** non resolu ;
le sous-agent signale un **blocage** ou une **decision de design imprevue** non tranchee par
l'amorce. Laisse l'etat tel quel (sain commite, casse non commite), livre un **rapport**
clair (ou ca bloque, quoi decider/corriger).

### Si `close <NN>`

1. **Notes pedagogiques** : redige `docs/sprints/sprint_<NN>/NOTES.md` — decompose les
   mecanismes cles introduits (thread-safety, trames Modbus, cinematique), avec
   schemas/sequences et pieges rencontres. Public : quelqu'un qui decouvre le sujet.
2. **Journal** : entree de cloture (Contexte / Action / Resultat / Suite).
3. **Memory** : ajoute les decisions actees durant le sprint (nouvelles entrees, ne pas
   reecrire les anciennes).
4. **Dettes** : mets a jour les statuts, ajoute les dettes nees pendant le sprint.
5. **Backlog** : coche `[x]` ce qui est fait, reporte le reste.
6. Verifie la DoD de chaque sous-sprint ; liste ce qui reste non satisfait.
7. Finalise `docs/sprints/sprint_<NN>/00_etat.md` (etat clos + REPRISE pointant vers la suite).

### Si `status`

Synthetise depuis `docs/sprints/sprint_<NN>/00_etat.md` : sprint courant, sous-sprints livres /
restants, DoD atteinte / restante, banc (vert / re-fige), dettes ouvertes bloquantes,
prochaine action recommandee.

Ne supprime jamais les commentaires pedagogiques du code lors des mises a jour.

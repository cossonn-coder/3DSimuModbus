---
description: Ouvrir ou cloturer un sprint (brief, notes, journal, memory, dettes, backlog)
---

Gere le cycle de vie d'un sprint : **$ARGUMENTS** (ex. `open 02`, `close 01`, `status`).

Fichiers d'orchestration concernes : `docs/journal.md`, `docs/memory.md`,
`docs/dettes.md`, `docs/backlog.md`, `docs/sprints/`, `docs/notes/`.

### Si `open <NN>`
1. Verifie l'etat du backlog et des dettes ouvertes pertinentes.
2. Redige `docs/sprints/sprint_<NN>_brief.md` **autosuffisant** : objectif, rappel
   machine + Modbus utile, livrables, point durs (dettes liees), ordre de travail,
   Definition of Done cochable.
3. Passe les items concernes du backlog en `[~]`. Ajoute une entree d'ouverture au
   journal.

### Si `close <NN>`
1. **Notes pedagogiques** : redige `docs/notes/NOTES_sprint_<NN>.md` — decompose les
   mecanismes cles introduits (thread-safety, trames Modbus, cinematique), avec
   schemas/sequences et pieges rencontres. Public : quelqu'un qui decouvre le sujet.
2. **Journal** : entree de cloture (Contexte / Action / Resultat / Suite).
3. **Memory** : ajoute les decisions actees durant le sprint (nouvelles entrees, ne
   pas reecrire les anciennes).
4. **Dettes** : mets a jour les statuts, ajoute les dettes nees pendant le sprint.
5. **Backlog** : coche `[x]` ce qui est fait, reporte le reste.
6. Verifie la Definition of Done du brief ; liste ce qui reste non satisfait.

### Si `status`
Synthetise : sprint courant, DoD atteinte / restante, dettes ouvertes bloquantes,
prochaine action recommandee.

Ne supprime jamais les commentaires pedagogiques du code lors des mises a jour.

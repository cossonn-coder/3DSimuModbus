---
description: Refactor cible avec garde-fous (tests verts avant/apres, modifs chirurgicales)
---

Refactore de facon **chirurgicale** : **$ARGUMENTS**.

Cadre impose par `CLAUDE.md` (sections « modifications chirurgicales » et « objectifs ») :

1. **Etat de depart vert.** Identifie/execute les tests couvrant la zone
   (`pytest` cote testbench). Si aucun test ne la couvre, ecris-en un qui capture le
   comportement actuel **avant** de toucher au code. Note le resultat.
2. **Perimetre minimal.** Ne touche que le necessaire. N'« ameliore » pas le code
   adjacent, ne reformate pas, ne renomme pas au-dela du besoin. Respecte le style
   existant meme si tu ferais autrement.
3. **Commentaires pedagogiques = intouchables.** Un refactor les **met a jour**, ne les
   supprime jamais et ne les compte pas comme dette.
4. **Contrat stable.** Le pivot JSON et les signatures publiques ne changent pas sans
   validation explicite. Si le refactor impose de toucher le pivot, arrete-toi et
   propose d'abord l'evolution du pivot.
5. **Etat d'arrivee vert.** Rejoue les memes tests : ils doivent passer a l'identique.
   Nettoie les imports/variables/fonctions rendus orphelins par tes modifications.
6. **Livraison fichier par fichier** ; resume le diff conceptuel (avant/apres) sans
   noyer sous le detail.

Termine par : tests verts avant = X, apres = X ; ce qui a change ; ce qui n'a
volontairement pas ete touche.

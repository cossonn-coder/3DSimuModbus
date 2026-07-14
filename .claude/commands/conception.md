---
description: Cadrer et valider l'architecture d'un module AVANT d'ecrire du code
---

Tu vas cadrer l'architecture d'un module ou d'une fonctionnalite : **$ARGUMENTS**.

Regle d'or du projet (cf. `CLAUDE.md`) : **architecture avant code**, et **le modele
pivot JSON d'abord**. Ne genere aucun fichier d'implementation dans cette commande.

Deroule :

1. **Relire le contrat.** Ouvre `pivot/machine_carrousel.json`, `docs/memory.md` et
   `docs/backlog.md`. Verifie si le besoin impacte le pivot. Si oui, propose d'abord
   l'evolution du pivot (le pivot change avant le code).
2. **Presenter l'architecture**, sans code :
   - responsabilite du module en une phrase
   - interfaces publiques (signatures, entrees/sorties, types)
   - dependances et frontieres (ce qu'il ne fait PAS)
   - pour du C# runtime : ou passe la frontiere thread serveur / scene tree / datastore
   - pour du Python : structure des tests pytest envisages
3. **Nommer les points durs et incertitudes** explicitement (pas de fausse certitude).
   Rattache-les a une dette existante (`docs/dettes.md`) ou propose-en une nouvelle.
4. **Plan de livraison fichier par fichier** (Nico code en SSH mobile) : liste ordonnee
   des fichiers, avec pour chacun le critere de validation.
5. **Attendre la validation** avant toute generation. Termine par les questions ouvertes
   a trancher.

Aligne le vocabulaire sur Control Expert / EcoStruxure (KM/YV/S/B, %MW, I/O Scanner,
TOR, monostable). En cas de doute sur une convention Schneider : pose la question.

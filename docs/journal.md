# journal.md — Chronologie du projet

Règle : une entrée par sprint (ou par session de conception significative), rédigée
en fin de sprint via `/sprint`. Format : date, objectif, ce qui a été fait, ce qui a
surpris, décisions prises (reportées dans memory.md), état des tests.

---

## 2026-07 — Phase 0 : conception et modèle pivot

**Objectif** : figer le contrat central (JSON pivot) et l'organisation du projet.

**Fait** :
- Spec fonctionnelle du carrousel validée (3 palettes, 2 postes de blocage,
  vérins monostables 500 ms, accumulation, retour de marche KM1).
- Table Modbus validée : cmd %MW100 (1 mot), ret %MW200 (2 mots, heartbeat en mot 0),
  deux lignes d'I/O Scanner FC3/FC16.
- `pivot/machine_carrousel.json` v0.2 écrit et validé.
- CLAUDE.md, commandes Claude Code et fichiers d'orchestration créés.

**Décisions** : voir memory.md (toutes reportées).

**Prochaine étape** : Sprint 1 — chaîne Modbus de bout en bout
(datastore thread-safe + serveur + banc de test Python émulant l'I/O Scanner),
avant toute 3D. Brief : `docs/sprints/sprint_01_brief.md`.

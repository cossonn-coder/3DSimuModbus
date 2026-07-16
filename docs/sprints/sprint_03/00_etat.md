# Sprint 03 — « Durcir le démonstrateur » — état de sprint

> Carnet vivant : conception **figée** le 2026-07-16 (archi validée avec Nico). Tenu à jour par
> chaque sous-sprint pendant l'exécution. Reprise à froid : `CLAUDE.md` + ce fichier + l'amorce.

## Où on en est
Conception **close**, sprint **prêt pour `/sprint open 03`**. 3 sous-sprints, amorces rédigées.
Sprints 1 & 2 clos (démonstrateur 3D animé depuis la sim via Modbus, validé à l'œil).

## Objectif
Rendre le démonstrateur **robuste** (échecs bruyants) et **lisible** (chaîne de commande tracée
**par élément 3D**) avant le M580 réel (Phase 4). **Lecture seule**, **pivot non touché**, Arch A intacte.

## Décisions clés (QCM 2026-07-16)
- **D-Q1** — Cœur = HUD lecture seule + bind visible.
- **D-Q2** — Forçage **reporté** (→ D-016) ; zéro écriture `cmd`.
- **D-Q3** — Voyant santé **dérivé de nous** (heartbeat + dernière écriture `cmd`) ; vérifier
  `RegistersChanged` tôt, repli sinon ; ne jamais promettre plus que le certain.
- **D-Q4** — « Par élément » = **étiquette texte** (lien Modbus %MW) **+ coloration d'état**.
- **D-arch** — Couture headless (S3.1) / visuel (S3.2, S3.3).
- **D-013** — Test qui **reproduit** le bind occupé écrit **avant** le correctif (CLAUDE.md §5).

## Carte des sous-sprints (séquentiels)
| # | Amorce | Nature | Fichiers | Dépend |
|---|---|---|---|---|
| **S3.1** Backend santé | `brique_01_backend_sante.md` | headless/xUnit | `ModbusServer.cs`, `ModbusServerException.cs`(neuf), `ModbusDataStore.cs`, `server.tests/`, `tests/` | — |
| **S3.2** Santé visible | `brique_02_sante_visible.md` | visuel | `CarrouselScene.cs`, `HealthHud.cs`(neuf) | S3.1 |
| **S3.3** Chaîne par élément + démo | `brique_03_chaine_commande.md` | visuel / **observable** | `CarrouselScene.cs`, `CommandChainLabels.cs`(neuf), `demo_sprint_03.ps1`(neuf) | S3.1, S3.2 |
> S3.2 & S3.3 partagent `CarrouselScene.cs` → **S3.2 avant S3.3**. S3.1 disjoint.

## Banc
S3.1 : `dotnet test` **re-figé** (nouveaux témoins ; réf. actuelle **90**, annoncer le total).
S3.2/S3.3 : glue lecture seule → `smoke_anim.ps1` + 4 pytest full-chain **inchangés/verts**.

## Dettes
- **D-013** : soldée en fin de **S3.2** (détectable S3.1, visible S3.2).
- Consignées hors périmètre (dettes.md) : **D-015** (nav 3D + vitesse), **D-016** (édition/branchement
  in-app), **D-017** (simulation de cas non nominaux / éléments défaillants).
- Non touchées : D-012, D-005, D-011.

## Points durs (dans les amorces)
- Bind occupé synchrone vs silencieux → tranché par le test de repro (S3.1).
- `RegistersChanged` fiable ? → mini-vérif en tête de S3.1, repli documenté.

## REPRISE
1. Relire `CLAUDE.md`, ce fichier, l'`overview.md`, et l'amorce du sous-sprint courant.
2. Exécution : `/sprint open 03` (séquentiel strict, un sous-agent cold-start par sous-sprint,
   autonome jusqu'au vert). Ordre : **S3.1 → S3.2 → S3.3**. Nico reprend au rapport final.

# Sprint 06 — « Forçage de debug » · overview

> Conception figée le 2026-07-18 (`/conception`, QCM tranchés avec Nico). Ce fichier indexe les
> sous-sprints et porte la **synthèse d'architecture** arbitrée. Détail par sous-sprint : les
> amorces `brique_0N_*.md`. Carnet de bord : `00_etat.md`.

## Intention

Forcer depuis l'IHM la valeur **effective** d'un signal de commande (zone `cmd` : KM1 run, YV1,
YV2) — pour **piloter la machine sans PLC** et pour forcer **malgré** le PLC — **sans jamais écrire
un mot du datastore** (ni `ret`, ni `cmd`). Le forçage est un **masque à la lecture** appliqué en
tête de `Tick` sur la copie snapshot des commandes ; le M580 continue d'écrire `cmd` à chaque scan,
la simulation **substitue** la valeur forcée à la lecture. Bonus : un nouveau défaut physique
`BlockerIneffective` (tige levée mais poste exclu du blocage → les palettes traversent).

Rejoint le **volet forçage de D-016** (le volet édition/catalogue reste reporté).

## Décisions arbitrées (QCM 2026-07-18)

- **D6-Q1** — **Mécanisme unique** (masque à la lecture). Aucune écriture datastore. Insensible à la
  réécriture de `cmd` par l'I/O Scanner par construction. Couvre aussi le pilotage sans PLC (personne
  n'écrit `cmd` → mots à 0 → forcer à 1 = piloter). Pas de mode « PLC absent » séparé.
- **D6-Q2** — **Colonne « Forçage » dédiée** dans le panneau (7ᵉ colonne), un MenuButton par signal
  `cmd` TOR (Auto / forcé 0 / forcé 1), peuplé à l'ouverture (générique, jamais une liste carrousel).
  Distincte de la colonne « Défaut » → marquage séparé par construction. Touche `G` = menu forçage de
  la sélection (comme `F` pour les défauts).
- **D6-Q3** — **Écart PLC/effectif inline dans la cellule `cmd`**, couleur distincte (convention
  « variable forcée » Control Expert), ex. `YV1 %MW100.1 : PLC=0 → forcé 1`. Pas de badge+tooltip.
- **Cœur = classe `ForceSet`** (miroir de `FaultSet`), pas une extension de FaultSet : forçage ≠
  défaut au sens Control Expert, et l'invariant « FaultSet vide = nominal » reste intact.
- **Marquage forçage = panneau seulement** (colonne + écart cmd). Pas de nouvelle couleur d'émission
  3D : le canal émission reste défaut(rouge) > sélection(cyan) > survol(bleu). En 3D, l'effet du
  forçage se VOIT dans le mouvement (vérin qui sort, convoyeur qui tourne).
- **Pipeline déterministe à 3 couches** : forçage `cmd` (tête de Tick) → défaut physique
  (AdvanceCylinder / ConveyorSlip) → masque capteur `ret` (fin de Tick). Un YV1 forcé à 1 avec « ne
  sort pas » actif → tige rentrée (le défaut physique gagne sur la commande forcée).
- **KM1_AUX sous forçage** : recopie la commande **effective** → forcer KM1 fait passer KM1_AUX à 1
  après `feedback_delay_ms` sans ordre PLC (cohérent, documenté NOTES).
- **`BlockerIneffective`** : `PhysicalFault` sur `cylinder_blocker` ; tige sort (S12=1, avance
  nominale), poste **exclu de `CollectBlockedStations`**. Marquage 3D = rouge défaut standard.
- **Clavier AZERTY (Nico)** : cyclage de sélection `[`/`]` → **`A` (précédent) / `Z` (suivant)**
  (touches directes sur AZERTY ; `[`/`]` exigeaient AltGr). Corrigé en S6.2.

## Invariants tenus

Pivot **inchangé** · Arch A intacte (mutation IHM→sim sur le thread principal ; le thread serveur ne
touche ni datastore ni scene tree) · boucle **10 Hz** intacte · **aucune écriture** `ret`/`cmd`
datastore · « forçable » = tout signal de zone `cmd` déclaré au pivot (générique, jamais un id
carrousel) · aucune adresse Modbus en dur · commentaires pédagogiques en français.

## Carte des sous-sprints (séquentiels stricts)

| # | Amorce | Nature | Fichiers principaux | Banc |
|---|---|---|---|---|
| **S6.1** | `brique_01_core.md` | core / headless | `FaultCatalog.cs`, `CarrouselSimulation.cs`, **`ForceSet.cs`** (neuf), tests xUnit | **re-figé** (core ↑) |
| **S6.2** | `brique_02_ui.md` | visuel | `ElementPanel.cs`, `CarrouselScene.cs` | inchangé |
| **S6.3** | `brique_03_demo.md` | observable | `runtime/scripts/demo_sprint_06.ps1` (neuf) | inchangé |

**DÉPENDANCES** : S6.2 dépend de l'API `ForceSet`/`BlockerIneffective` de S6.1 · S6.3 dépend de l'UI
de S6.2. **Aucun fichier partagé** entre les trois → chaîne séquentielle nette, pas de conflit.

## Definition of Done du sprint

- [ ] `BlockerIneffective` + `ForceSet` livrés, testés xUnit ; banc core **re-figé** (nouveau témoin
  documenté), les 4 pytest full-chain **inchangés** (forçage inactif + BlockerIneffective inactif =
  nominal strict).
- [ ] Colonne « Forçage » + écart cmd + touche `G` + correction AZERTY `A`/`Z` ; build Godot 0 erreur.
- [ ] `demo_sprint_06.ps1` : phases **sans** io_scanner (pilotage IHM pur) puis **avec** scan (le
  forçage gagne, écart visible, KM1_AUX=1 non commandé). Pré-vol 502.
- [ ] `docs/sprints/sprint_06/NOTES.md` (à la clôture) : mécanisme masque-lecture, pipeline 3 couches,
  KM1_AUX sous forçage, piège clavier AZERTY.

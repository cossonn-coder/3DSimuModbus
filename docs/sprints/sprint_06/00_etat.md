# 00_etat — Sprint 06 « Forçage de debug » (carnet vivant)

> Tenu à jour à **chaque décision arrêtée**. Reprise à froid = relire `CLAUDE.md` + ce fichier.

## Phase courante
**EXÉCUTION TERMINÉE (2026-07-18).** Les 3 sous-sprints sont livrés, vérifiés et committés
(S6.1 `0fb75ea`, S6.2 `8005d7b`, S6.3 à committer). Prêt pour la clôture (`/sprint close 06`).

## Carte des sous-sprints (séquentiels stricts, aucun fichier partagé)
- **S6.1** `brique_01_core.md` — [core/headless] `BlockerIneffective` + `ForceSet` + application Tick.
  Banc core **re-figé 109 → 121** (+12). **LIVRÉ (2026-07-17)**, DoD cochée, build 0 erreur.
- **S6.2** `brique_02_ui.md` — [visuel] colonne Forçage + écart cmd + touche `G` + AZERTY `A`/`Z`.
  Banc inchangé. Dépend de S6.1. **LIVRÉ (2026-07-17)**, DoD cochée, build Godot 0 erreur.
- **S6.3** `brique_03_demo.md` — [observable] `demo_sprint_06.ps1` (8 phases : 2 sans PLC + 5 avec scan
  + retour nominal). **LIVRÉ (2026-07-18)** : exécution à blanc = refus propre du pré-vol 502 (exit 1,
  zéro erreur parseur), ASCII pur confirmé, aucun `.cs` touché. Dépend de S6.2. Banc inchangé.

## Intention (rappel amorce)
Forcer depuis l'IHM la valeur **effective** d'un signal de commande (`cmd` : KM1 run, YV1, YV2)
pour piloter la machine **sans PLC** et **malgré** le PLC — sans jamais écrire un mot `ret`, ni
même écrire le datastore `cmd` (masque **à la lecture**, post-snapshot). + brique 1 : défaut
`BlockerIneffective` (tige levée mais poste exclu du blocage → palettes traversent).

## Décisions arrêtées
- **Q1 (tranché 2026-07-18)** : **mécanisme unique** (masque à la lecture). Aucune écriture datastore
  (ni `ret` ni `cmd`). Couvre aussi le pilotage sans PLC (cmd à 0 → forcer à 1 = piloter).
- **Q2 (tranché 2026-07-18)** : **colonne « Forçage » dédiée** dans le panneau, un MenuButton par
  signal `cmd` (Auto / forcé 0 / forcé 1), peuplé à l'ouverture depuis les signaux `cmd` TOR
  (générique). Distincte de la colonne « Défaut ». + raccourci clavier (menu forçage de la sélection).
- **Q3 (tranché 2026-07-18)** : **écart inline dans la cellule `cmd`**, couleur distincte
  (`PLC=0 → forcé 1`). Le panneau lit la valeur PLC via son snapshot cmd, la valeur forcée via un
  délégué `ForceModeBySignal` (symétrique de `faultLabelById`).
- **Clavier AZERTY (Nico 2026-07-18)** : le cyclage de sélection passe de `[`/`]` (inaccessibles sans
  AltGr sur AZERTY) à **`A` = précédent / `Z` = suivant**. Correction intégrée à **S6.2**. Touche
  d'ouverture du menu forçage : **`G`** (reco, adjacente à `F` = menu défaut ; contestable). Pas de
  conflit avec B/R/F/Espace. ⚠️ **Piège à vérifier en S6.2** : `key.Keycode` dépend du layout — confirmer
  sur AZERTY que `Key.A`/`Key.Z`/`Key.G` répondent aux touches marquées (sinon utiliser `KeyLabel`).
- Pré-tranché (reco phase A, contestable) :
  - **Cœur forçage = nouvelle classe `ForceSet`** (miroir de `FaultSet`), pas une extension de
    FaultSet. Pourquoi : forçage ≠ défaut (convention Control Expert), garde le « défaut inactif =
    nominal » de FaultSet intact, code symétrique. Exposée `_sim.Forces` à côté de `_sim.Faults`.
  - **Application** : après `SnapshotCommands` en tête de `Tick`, on réécrit les bits `cmd` forcés
    dans la **copie snapshot** (jamais le datastore) via un index `_cmdTorSignals` (miroir de
    `_retTorSignals`). Symétrique du masque capteur (qui, lui, agit sur `ret` après encodage).
  - **Ordre des couches (déterministe)** : forçage `cmd` (tête de Tick) → défaut physique
    (AdvanceCylinder / ConveyorSlip) → masque capteur `ret` (fin de Tick). Un YV1 forcé à 1 avec
    « ne sort pas » actif → la tige reste rentrée (le défaut physique gagne sur la commande forcée).
  - **KM1_AUX sous forçage** : recopie la commande **effective** → forcer KM1 run=1 fait passer
    KM1_AUX à 1 après feedback_delay, sans que le PLC l'ait commandé (cohérent, à documenter NOTES).
  - **`BlockerIneffective`** : nouveau `PhysicalFault` sur `cylinder_blocker`. La tige sort
    normalement (S12=1), mais `CollectBlockedStations` **exclut** ce poste → palettes traversent.
    Marquage 3D = **rouge défaut standard** (c'est un défaut ; la signature = palettes qui filent).
  - **Marquage forçage** = **panneau seulement** (colonne dédiée + écart dans la cellule `cmd`), PAS
    de nouvelle couleur d'émission 3D (le canal émission reste défaut/sélection/survol). En 3D,
    l'effet du forçage se VOIT dans le mouvement (vérin qui sort, convoyeur qui tourne).

## Questions ouvertes
- Aucune bloquante. Résiduel contestable : touche `G` pour le menu forçage (confirmable à l'usage).

## Coût estimé
Faible. Core additif (une classe + ~1 défaut + application symétrique). UI = extension du panneau
existant (colonne + délégué, patron rôdé S5.3). Aucun changement pivot, Arch A / boucle 10 Hz intactes.

## Banc (état de départ)
Core 109 + serveur 10 = **119** ; 4 pytest full-chain verts. S6.1 relève le compte core (nouveaux
cas) → nouveau témoin. S6.2/S6.3 : inchangé. Forçage inactif + BlockerIneffective inactif = nominal.

## REPRISE
**Banc core inchangé après S6.2 : 121 verts** (aucun code core touché, confirmé `dotnet test`). Build
Godot 0 erreur / 0 avert. (`dotnet build runtime/DemonstrateurCarrousel.csproj` ; l'éditeur headless
`--build-solutions` reste lent sur l'import d'assets → non concluant, mais la compilation C# est verte).
Fichiers S6.2 touchés (2, aucun partagé avec S6.1) : `runtime/scenes/ElementPanel.cs`,
`runtime/scenes/CarrouselScene.cs`. Livré : 7e colonne « Forçage » (MenuButton Auto/0/1 par signal
`cmd` TOR, peuplé à l'ouverture, `—` pour capteur), cellule `cmd` force-aware (`PLC=x → forcé y` +
teinte magenta), délégués `OnForce`/`ForceModeBySignal` (écriture IHM→`_sim.Forces`, thread principal,
Arch A), touche `G` = menu forçage sélection, cyclage AZERTY `A`/`Z` (ex-`[`/`]`). `RefreshEmission`
inchangé (forçage NON peint en 3D). `R` inchangé (dé-forçage via entrée Auto du menu).

**À valider manuellement (F5, non fait ici — pas de session Godot interactive)** : forçage YV1 sans PLC
(tige sort, cmd `PLC=0 → forcé 1`), forçage contre PLC (`io_scanner_sim.py --run 0 --yv1 0` → tige reste
sortie), retour Auto (teinte disparaît), et surtout le **piège AZERTY** : confirmer que `A`/`Z`/`G`
(Keycode, dépendant du layout) répondent aux touches physiquement marquées ; sinon basculer sur `KeyLabel`.

**Prochain : S6.3** (`brique_03_demo.md`, `demo_sprint_06.ps1`). Banc inchangé attendu.

### Rappel S6.1 (référence)
Fichiers S6.1 : `runtime/core/ForceSet.cs` (neuf), `FaultSet.cs`, `FaultCatalog.cs`,
`CarrouselSimulation.cs`, `runtime/tests/ForceSetTests.cs` (neuf), `CarrouselSimulationTests.cs`,
`FaultSetTests.cs`. Témoin 109 → 121 (+12). Serveur 10 inchangé → total C# = 131. 4 pytest full-chain.

Conception close. Pour exécuter : `/sprint open 06` (orchestration séquentielle S6.1 → S6.2 → S6.3,
un sous-agent en contexte vierge par sous-sprint). Chaque sous-agent lit `CLAUDE.md` + ce fichier +
son amorce `brique_0N_*.md`. Sources de vérité code (déjà repérées) : `CarrouselSimulation.cs`
(Tick, SnapshotCommands, CollectBlockedStations, AdvanceCylinder), `FaultSet.cs`/`FaultCatalog.cs`
(patron à mirrorer), `ElementPanel.cs` (menu/colonnes/délégués), `CarrouselScene.cs` (OnFault,
RefreshEmission, délégués, clavier). Banc de départ : core 109 + serveur 10 = 119 ; 4 pytest full-chain.

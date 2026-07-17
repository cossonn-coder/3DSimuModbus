# S5.3 — Menu de défauts par ligne + marquage 3D/badge + mode aveugle

> Sous-sprint **visuel** (Godot). Reprise à froid : `CLAUDE.md` + `00_etat.md` + cette amorce.
> **Premier sous-sprint où l'IHM écrit dans la sim** (via `sim.Faults`, même thread principal).

## Objectif
Permettre de **déclencher / réparer** un défaut par élément depuis le panneau (MenuButton par
ligne + colonne « Défaut »), câblé au `FaultSet` (S5.1). Marquer visiblement un défaut actif
(émission **rouge** 3D + badge panneau), avec un **mode aveugle** qui masque ce marquage.

## Fichiers touchés
- `runtime/scenes/ElementPanel.cs` (colonne « Défaut », MenuButton par ligne, indicateur aveugle).
- `runtime/scenes/CarrouselScene.cs` (câblage menu→`sim.Faults`, marqueur émission rouge, touches
  réparer/aveugle, refresh du badge).
- Lire pour comprendre : ces deux fichiers + `FaultSet`/`FaultCatalog`/`FaultCommand` (`CarrouselCore`).

## Contrat d'API visé
- `ElementPanel.Build(...)` gagne deux délégués (comme `onRowHover`/`cylinderPositionById` en S4.2) :
  - `System.Action<FaultCommand> onFault` : appelé quand l'utilisateur choisit une entrée du menu ;
  - `System.Func<string, string> faultLabelById` : rend le libellé « Défaut » d'un composant
    (« — » si aucun) pour la colonne, interrogé à chaque `Update`.
- Colonne **« Défaut »** ajoutée (6e) : `Headers`/`ColWidths` étendus ; chaque ligne a une cellule
  `Défaut` + un **`MenuButton`** en bout de ligne. `Update` renseigne la cellule via `faultLabelById`.
- Le `PopupMenu` du `MenuButton` est peuplé depuis **`FaultCatalog.ApplicableTo(comp)`** (générique)
  + une entrée **« Réparer »** si `faultLabelById(id) != "—"`. Chaque item, à l'activation, appelle
  `onFault(cmd)` avec le `FaultCommand` correspondant (Repair pour « Réparer »).
  - **Libellés** (présentation, dans le panneau — esprit `TypeLabels`) : mapper `FaultCommand`→FR,
    ex. `CylinderStuckRetracted`→« vérin : ne sort pas », `CylinderStuckMidStroke`→« vérin : coincé
    mi-course », `ConveyorSlip`→« convoyeur : patine », `SensorStuck Low`→« {tag} bloqué à 0 »,
    `High`→« {tag} bloqué à 1 » (tag du signal, sinon nom). Type/mode inconnu → libellé brut.
- `CarrouselScene` :
  - handler `OnFault(FaultCommand cmd)` → `_sim.Faults.Apply(cmd)` puis `RefreshEmission(cmd.ComponentId)`.
  - `faultLabelById(id)` → décrit le défaut actif de `id` via `_sim.Faults` (physique et/ou stucks).
  - `RefreshEmission` (introduit en S5.2) étendu : **priorité défaut(rouge) > sélection(cyan) >
    survol(bleu)** ; « défaut » = `_sim.Faults.HasAnyFault(id) && !_blindMode`.
  - **Mode aveugle** : `private bool _blindMode;` ; touche **`B`** bascule ; quand actif, le marquage
    **3D rouge** ET le **badge panneau** sont masqués (la colonne « Défaut » affiche « — » et
    l'émission ignore l'état défaut). Le défaut **reste réellement injecté** (la sim se comporte
    anormalement) : seul l'**indice visuel** disparaît. Un petit indicateur « MODE AVEUGLE » visible
    (dans le panneau ou coin d'écran) rappelle que le masquage est actif (sinon on s'y perd).
  - Touche **`R`** : `_sim.Faults.ClearComponent(_selectedId)` (réparer l'élément sélectionné) +
    refresh. Touche **`F`** (ou `Space`) : ouvre le `PopupMenu` de la ligne sélectionnée.
    Toutes dans `_UnhandledInput`, `SetInputAsHandled`, sans collision avec `F11`/`[`/`]`.

## Décisions pré-tranchées
- **Générique** : le contenu du menu vient de `FaultCatalog` (type + signaux `ret` TOR), jamais
  d'une liste carrousel en dur. Un composant d'un type inconnu offre quand même ses capteur-bloqué.
- **Badge/colonne** : le libellé « Défaut » agrège les défauts actifs de l'élément (physique +
  chaque capteur bloqué), séparés par «  ». « — » si aucun.
- **Marqueur 3D rouge** : réutilise le matériau d'émission déjà enregistré (`_highlightMat[id]`),
  couleur rouge + énergie via le résolveur `RefreshEmission`. Aucun nouveau mesh. `Update` du
  panneau (~6 Hz) suffit à rafraîchir badge + émission des éléments faultés (ou refresh à chaque
  `Apply`/toggle).
- **Le masque aveugle n'affecte JAMAIS** le bandeau d'échec du `HealthHud` (invariant : une panne
  serveur ne se cache pas) — hors périmètre ici, mais à ne pas régresser.

## Definition of Done (cochable)
- [x] Chaque ligne du panneau a un MenuButton listant les modes applicables à son type + « Réparer ».
  *(peuplé à l'ouverture via `AboutToPopup` depuis `FaultCatalog.ApplicableTo` + « Réparer » si défaut actif ; F5 pour l'œil.)*
- [x] Déclencher un mode injecte le défaut (comportement sim visible : ex. « vérin ne sort pas » ⇒
  la tige ne monte plus ; « capteur B1 bloqué à 1 » ⇒ fenêtre B1 allumée en permanence).
  *(câblé `IndexPressed`→`_onFault`→`_sim.Faults.Apply` ; effet sim déjà couvert par S5.1, à confirmer à l'œil en F5.)*
- [x] Colonne « Défaut » + marqueur 3D rouge apparaissent sur l'élément faulté ; « Réparer »
  (menu ou touche `R`) rétablit le nominal. *(colonne via `FaultLabelById` ; rouge via `RefreshEmission`, priorité défaut.)*
- [x] Touche `B` bascule le mode aveugle : marquages masqués, **défaut toujours actif** ;
  indicateur « MODE AVEUGLE » visible. *(`ToggleBlindMode` + `SetBlindMode` ; `FaultLabelById` renvoie « — » ; émission ignore le défaut.)*
- [x] Smoke inchangé (`rows` reste à 5 lignes ; colonnes = 6 mais le smoke ne lit que `[panel] rows=5`,
  donc **inchangé**).

## Banc attendu — **inchangé**
Glue Godot lecture/écriture-sim **sur le thread principal**, zéro modif core/serveur : xUnit
**95+N** inchangé, **4 pytest inchangés**, build Godot **0 erreur**. La logique de défaut est
déjà testée en S5.1 ; ici on ne teste pas la sim, on la pilote.

## Vérif autosuffisante
- Build Godot 0 erreur ; smoke headless (recensement) inchangé.
- Validation manuelle Nico (F5) : déclencher chaque mode sur chaque type, observer l'effet 3D +
  la colonne + le marqueur ; réparer ; basculer le mode aveugle et vérifier que la sim reste
  anormale sans indice visuel.

## Ce qu'il NE faut PAS faire
- Ne pas modifier le core ni le serveur (le modèle de défaut est figé en S5.1). Aucune écriture
  `cmd`/`ret` au datastore.
- Ne pas introduire la coupure comm ici (gel `ret` / TCP) — c'est **S5.4** (contrôle global).
- Pas d'auto-déclenchement de défaut. Ne pas masquer le bandeau d'échec serveur avec le mode aveugle.

## Dépendances / validation manuelle
- **Dépendances** : **S5.1** (FaultSet/Catalog/Command) et **S5.2** (sélection + `RefreshEmission`).
  Partage `CarrouselScene.cs`/`ElementPanel.cs` avec S5.2/S5.4 ⇒ séquentiel après S5.2.
- Validation manuelle : F5 Godot (voir DoD).

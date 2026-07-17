# S5.2 — Sélection au clic (3D↔ligne, souris + clavier) + solde D-018

> Sous-sprint **visuel** (Godot). Reprise à froid : `CLAUDE.md` + `00_etat.md` + cette amorce.
> **Lecture seule de la sim** : ce sous-sprint n'injecte aucun défaut (c'est S5.3). Il pose la
> **sélection persistante** que S5.3 utilisera pour cibler ses menus/marquages.

## Objectif
Ajouter une **sélection persistante** d'élément (distincte du survol S4.3), symétrique :
clic sur l'élément **3D** ou sur sa **ligne** du panneau → même sélection (source unique
`SetSelected`, patron de `SetHover`). Cyclage au clavier. Visuel de sélection distinct du survol.
**Solder D-018** au passage (fichier touché).

## Fichiers touchés
- `runtime/scenes/CarrouselScene.cs` (sélection + visuel + input clavier + **solde D-018**).
- `runtime/scenes/ElementPanel.cs` (clic de ligne → sélection ; surbrillance de ligne sélectionnée).
- Lire pour comprendre : ces deux fichiers + `OrbitCamera.cs` (pipeline d'input `_UnhandledInput`).

## Contrat d'API visé
- `CarrouselScene` : `private string? _selectedId;` + `private void SetSelected(string? id)`
  (source unique, symétrique de `SetHover`). `SetSelected(null)` = désélection.
- Refactor de la surbrillance : remplacer l'écriture directe de `EmissionEnergyMultiplier` dans
  `SetHover` par un **résolveur par priorité** `RefreshEmission(string id)` qui lit l'état combiné
  de l'élément (`_hoveredId==id`, `_selectedId==id`) et pose **couleur + énergie** d'émission :
  - sélectionné → émission **cyan** (`HighlightSelectEmission`), énergie `SelectEnergy` ;
  - survolé (non sélectionné) → émission **bleue** existante, énergie `HighlightEnergy` ;
  - sinon → énergie 0.
  (S5.3 ajoutera la priorité **défaut (rouge) > sélection > survol** dans ce même résolveur.)
  `SetHover` et `SetSelected` appellent `RefreshEmission(id)` sur l'ancien et le nouvel id.
- `ElementPanel` :
  - `HighlightRow` reste (survol). Ajouter une notion de **ligne sélectionnée** : `SelectRow(string? id)`
    qui applique une stylebox de sélection (distincte de `_rowHighlight`) à la ligne, l'enlève à
    l'ancienne. Le survol et la sélection se composent (une ligne peut être les deux ; la sélection
    prime visuellement).
  - Clic de ligne → sélection : le `PanelContainer` de chaque ligne émet déjà `MouseEntered/Exited`
    (survol) ; ajouter la capture du **clic gauche** (via `GuiInput` sur le container, ou un signal)
    → appelle un délégué `_onRowClick(id)` injecté au `Build` (comme `_onRowHover`).
- Câblage `CarrouselScene` : passe `SetSelected` comme `_onRowClick` au `Build` du panneau, et
  appelle `_panel.SelectRow(id)` depuis `SetSelected` (symétrie ligne↔3D garantie par la source unique).

## Décisions pré-tranchées
- **Clic 3D** : réutiliser le suivi de survol. Maintenir `_hoveredId` (mis à jour par `SetHover`).
  Dans `CarrouselScene._UnhandledInput`, sur **clic gauche relâché non consommé** : `SetSelected(_hoveredId)`
  (si `_hoveredId==null`, clic dans le vide ⇒ **désélection**). Pas de câblage `input_event` par
  Area3D (générique, une seule source). Marquer l'event `SetInputAsHandled` quand on sélectionne.
  ⚠ L'UI (`MouseFilter=Stop`) consomme déjà ses clics avant `_UnhandledInput` (le clic de ligne
  passe donc par le panneau, pas par la 3D) — pas de double-sélection.
- **Clavier** (souris + clavier, D-Q4) — ce sous-sprint ne pose QUE le cyclage de sélection :
  `]` = élément suivant, `[` = précédent, dans l'**ordre du pivot** (`pivot.Components.Values`).
  Éviter `Tab` (focus GUI Godot) et `F11` (déjà pris par `OrbitCamera`). Cyclage circulaire ;
  depuis « aucune sélection », `]` prend le premier. Les touches de défaut (réparer/aveugle/menu)
  sont **S5.3**, pas ici.
- **Visuel** : sélection = émission cyan (3D) + stylebox de ligne dédiée (panneau). Distinct et
  lisible par-dessus le survol bleu. Aucune couleur d'**état** (albédo) touchée (D-arch préservé).
- **Solde D-018** : supprimer les 5 champs `_ringNode`, `_cyl1Node`, `_cyl2Node`, `_sensor1Node`,
  `_sensor2Node` **et leurs assignations** dans les builders. Vérifier qu'aucune lecture ne subsiste
  (grep). Rien d'autre n'est « nettoyé » au passage (CLAUDE.md §4).

## Definition of Done (cochable)
- [x] Clic gauche sur un élément 3D le sélectionne ; clic sur sa ligne aussi ; les deux donnent
  le **même** état (émission cyan + ligne sélectionnée). *Implémenté via source unique `SetSelected` ;
  à valider en F5 (interaction souris/picking non testable en headless).*
- [x] Clic dans le vide désélectionne. `]`/`[` cyclent la sélection dans l'ordre du pivot.
  *Implémenté (`_UnhandledInput` + `CycleSelection`) ; à valider en F5.*
- [x] Le survol (S4.3) fonctionne toujours ; survol et sélection se composent sans perte de couleur
  d'état (albédo intact). *Résolveurs par priorité `RefreshEmission`/`RefreshRowStyle` ; à valider en F5.*
- [x] D-018 soldée : 5 champs + assignations supprimés, build 0 erreur, aucun usage orphelin (grep = 0).
- [x] Smoke inchangé : `ring=1 cylinders=2 pallets=3 sensors=2`, `panel rows=5` (vérifié headless).

## Banc attendu — **inchangé**
xUnit **95+N** (total de S5.1) inchangé, **4 pytest inchangés** : ce sous-sprint est de la **glue
Godot lecture seule**, zéro modif core/serveur. Build Godot **0 erreur**. Annoncer « banc inchangé ».

## Vérif autosuffisante
- Build : `dotnet build` du projet Godot → 0 erreur.
- Smoke headless existant (`smoke_scene.ps1` / `smoke_anim.ps1`) → recensement inchangé.
- Validation manuelle Nico (F5) : sélectionner au clic 3D et à la ligne, cycler au clavier,
  vérifier la symétrie et que rien ne « clignote » ni ne perd sa couleur d'état.

## Ce qu'il NE faut PAS faire
- Aucune injection de défaut, aucun menu (S5.3). Aucune écriture `cmd`/`ret`. Aucun accès datastore
  hors les snapshots lecture seule déjà en place.
- Ne pas retoucher `OrbitCamera` (juste cohabiter dans le pipeline d'input).
- Ne pas modifier le core ni le serveur (banc doit rester inchangé).

## Dépendances / validation manuelle
- **Dépendances** : aucune stricte (S5.1 conseillé mergé avant pour éviter un rebase du core,
  mais S5.2 ne référence pas `FaultSet`). Partage `CarrouselScene.cs` avec S5.3/S5.4 ⇒ séquencer.
- Validation manuelle : F5 Godot (voir DoD).

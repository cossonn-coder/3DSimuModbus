# S6.2 — UI du forçage : colonne « Forçage » + écart cmd + clavier (visuel)

> **Cold-start** : lire `CLAUDE.md` + `docs/sprints/sprint_06/00_etat.md` + cette amorce suffit.
> Sous-sprint **visuel** : glue Godot lecture seule sur le thread principal + branchement des délégués.
> Dépend de l'API de **S6.1** (`ForceSet`, `Forces`, `ForceCommand`, `ForceMode`, `BlockerIneffective`).
> Banc **inchangé** (aucune modif core). Build Godot 0 erreur + smoke inchangé.

## Objectif

Rendre le forçage **pilotable et lisible** depuis le panneau, et corriger le clavier AZERTY :

1. **Colonne « Forçage »** (7ᵉ) : un MenuButton par ligne ayant ≥1 signal `cmd` TOR ; le popup offre,
   pour chaque signal `cmd`, **Auto / forcer à 0 / forcer à 1** (peuplé à l'ouverture, générique).
2. **Écart PLC/effectif** affiché **inline dans la cellule `cmd`**, en couleur distincte, quand un
   signal de la ligne est forcé (ex. `YV1 %MW100.1 : PLC=0 → forcé 1`).
3. **Marquage forçage = panneau seulement** (colonne + cellule cmd teintée). **Pas** de nouvelle
   couleur d'émission 3D — le canal émission reste défaut>sélection>survol. L'effet du forçage se
   VOIT dans le mouvement 3D.
4. **Clavier** : touche **`G`** ouvre le menu forçage de la sélection (comme `F` pour les défauts) ;
   **correction AZERTY** du cyclage de sélection `[`/`]` → **`A` (précédent) / `Z` (suivant)**.

## Contrat d'API visé

### `runtime/scenes/ElementPanel.cs`

- **Signature `Build` étendue** — ajouter deux délégués (patron identique à `onFault`/`faultLabelById`) :
  ```csharp
  public void Build(PivotModel pivot, System.Action<string,bool> onRowHover,
                    System.Action<string> onRowClick,
                    System.Func<string,double> cylinderPositionById,
                    System.Action<FaultCommand> onFault,
                    System.Func<string,string> faultLabelById,
                    System.Action<ForceCommand> onForce,                 // NEUF : menu forçage → sim
                    System.Func<string,string,ForceMode> forceModeBySignal)  // NEUF : mode courant d'un signal
  ```
- **7ᵉ colonne** « Forçage » : ajouter à `Headers` et `ColWidths` (le reste du tableau — `AutoFitColumns`,
  `Cells`, header — s'adapte automatiquement puisqu'il itère sur les tableaux).
- **MenuButton de forçage par ligne** : dans `MakeRow`, sur les lignes ayant ≥1 signal `cmd` TOR,
  ajouter un `MenuButton` (dans la nouvelle colonne). Popup peuplé à l'ouverture (`AboutToPopup`) par
  un `PopulateForceMenu(comp, popup, commands)` symétrique de `PopulateFaultMenu` : pour chaque signal
  `cmd` TOR (ordre `OrderBy(Name)` déterministe), trois items `Auto <label>` / `forcer <label> à 0` /
  `forcer <label> à 1`, chacun mémorisé comme `ForceCommand`. `IndexPressed` → `_onForce(cmd)`. Une
  ligne sans signal `cmd` TOR (capteurs) n'a **pas** de MenuButton (cellule « — »).
- **Écart dans la cellule `cmd`** : la construction de la cellule `cmd` (aujourd'hui `ZoneAddresses`)
  devient **force-aware**. Pour chaque signal `cmd` TOR, si `forceModeBySignal(comp.Id, sig.Name)` ≠
  `Auto`, afficher `TAG %MWx.y : PLC=<bit datastore> → forcé <0|1>` ; sinon le format actuel. Quand la
  ligne porte au moins un forçage, **teinter la cellule `cmd`** (override `font_color` distinct —
  convention « variable forcée » ; couleur ni rouge défaut ni vert état, ex. **magenta/orange clair**).
  Simplicité : teinter toute la cellule cmd suffit (pas de RichTextLabel).
- **`OpenForceMenu(string componentId)`** : symétrique de `OpenFaultMenu` (touche `G`).
- **Libellés** : réutiliser `SignalLabel(comp, name)` pour le repère du signal. Ajouter une petite
  fonction de libellé de `ForceCommand` (esprit `FaultCommandLabel`), source unique de la traduction FR.

### `runtime/scenes/CarrouselScene.cs`

- **Deux délégués** passés à `_panel.Build(...)` :
  ```csharp
  private void OnForce(ForceCommand cmd)
  {
      _sim.Forces.Apply(cmd);          // 1re écriture IHM→forçage, thread principal (Arch A)
      _panel.RefreshFaults(BuildFaultEntries());  // retour immédiat de l'encart si pertinent
      // PAS de RefreshEmission ici : le forçage ne peint pas le canal émission 3D.
  }
  private ForceMode ForceModeBySignal(string compId, string name) => _sim.Forces.GetForce(compId, name);
  ```
- **Clavier** dans `_UnhandledInput` :
  - Remplacer `Key.Bracketright` → **`Key.Z`** (`CycleSelection(+1)`, suivant) et `Key.Bracketleft`
    → **`Key.A`** (`CycleSelection(-1)`, précédent). Conserver `key.Keycode` (dépendant du **layout**,
    donc `Key.A`/`Key.Z` visent bien les touches marquées A/Z sur AZERTY).
  - Ajouter `case Key.G:` → `if (_selectedId is not null) { _panel.OpenForceMenu(_selectedId); SetInputAsHandled(); }`.
  - Mettre à jour le commentaire pédagogique du bloc clavier (touches `A`/`Z`/`G`, plus `B`/`R`/`F`/`Espace`).

## Décisions pré-tranchées (ne pas ré-instruire)

- **Forçage NON dans le canal émission 3D** : marquage panneau seulement (colonne + cellule cmd).
- **`R` reste « réparer les défauts »** (physique + capteurs bloqués). Le forçage se lève via l'entrée
  **Auto** du menu forçage (action délibérée ↔ dé-forçage délibéré). Ne pas faire lever le forçage par `R`.
- **Écart inline** dans la cellule cmd (pas de badge/tooltip). Teinte de cellule = signalement distinct.
- **`ForceModeBySignal`** est la seule source de la valeur forcée pour le panneau (le panneau ne connaît
  ni `_sim` ni le modèle de forçage : uniquement ce pont, comme `faultLabelById`).

## Definition of Done (cochable)

- [x] Colonne « Forçage » + MenuButton par ligne à signal `cmd` ; Auto/0/1 par signal, peuplé à l'ouverture.
- [x] Cellule `cmd` force-aware (PLC → forcé) + teinte distincte quand forçage actif.
- [x] `OnForce`/`ForceModeBySignal` branchés ; forçage écrit dans `_sim.Forces` sur le thread principal.
- [x] Touche `G` ouvre le menu forçage de la sélection ; cyclage `A`/`Z` (plus `[`/`]`).
- [x] Build Godot **0 erreur** (dotnet build du .csproj, 0 avert.) ; banc **inchangé** (121). Smoke headless
      `[panel] rows=5` : code de trace `[panel] rows={_rows.Count}` inchangé → à confirmer par F5/headless (piège AZERTY).

## Vérif autosuffisante

```
dotnet build runtime/                               # 0 erreur
```
Puis **validation manuelle F5** (voir ci-dessous) — cette brique est visuelle.

### Validation manuelle (F5 Godot)
- Sélectionner YV1, ouvrir le menu Forçage (bouton **ou touche `G`**), « forcer à 1 » **sans PLC** →
  la tige **sort** ; la cellule cmd de YV1 affiche `PLC=0 → forcé 1` en couleur distincte.
- Lancer `io_scanner_sim.py --run 0 --yv1 0` (le PLC commande 0) → la tige **reste sortie** (le
  forçage gagne) ; l'écart cmd reste visible.
- Forcer `Auto` → la ligne revient au nominal, la teinte disparaît.
- **Piège AZERTY à confirmer** : `A`/`Z` cyclent bien la sélection, `G` ouvre le menu forçage (touches
  physiquement marquées sur le clavier AZERTY). Si un décalage apparaît, basculer sur `KeyLabel`.

## Ce qu'il NE faut PAS faire

- Aucune modif du `CarrouselCore` (sinon c'est S6.1 ou une régression du banc).
- Ne pas peindre le forçage dans le canal émission 3D (`RefreshEmission` inchangé).
- Ne pas retirer/renommer les touches `B`/`R`/`F`/`Espace` existantes.
- Ne pas coder d'id carrousel ni d'adresse %MW en dur (tout via les Signal résolus / délégués).
- Ne pas casser le patron « le panneau ne connaît que des délégués » (aucun accès à `_sim`).

## DÉPENDANCES / FICHIERS TOUCHÉS

- **Dépendances** : **S6.1** (API `ForceSet`/`Forces`/`ForceCommand`/`ForceMode`, `BlockerIneffective`).
- **Fichiers** : `runtime/scenes/ElementPanel.cs`, `runtime/scenes/CarrouselScene.cs`.
  (Aucun fichier partagé avec S6.1 → séquentiel par dépendance d'API, pas par conflit.)

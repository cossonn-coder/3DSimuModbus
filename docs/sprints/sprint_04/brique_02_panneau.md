# Amorce S4.2 — Panneau latéral des éléments (+ dépose des Label3D, survol ligne → 3D)

> **Cold-start** : lire `CLAUDE.md` (dont §0bis) + `docs/sprints/sprint_04/00_etat.md` + cette amorce.
> Sous-sprint **2/4**. **Dépend de : S4.1** (caméra en place). **Séquentiel** avec S4.1/S4.3 (fichier
> partagé `CarrouselScene.cs`). C'est le **cœur** du sprint (retour UX du sprint 3).

## Objectif

Remplacer les étiquettes 3D flottantes (illisibles, rejet UX du sprint 3) par un **panneau latéral
2D** qui liste **tous les éléments du pivot** avec leur mapping %MW décodé, et **relie le tableau à la
3D au survol** (sens **ligne → élément 3D** ici ; le sens inverse est S4.3). La **coloration d'état 3D
est conservée**. Générique : le panneau se peuple depuis `components[]`, **rien de carrousel en dur**.

## Contexte code (déjà en place)

- `runtime/scenes/CommandChainLabels.cs` : helper S3.3 qui (a) crée un `Label3D` billboard par élément,
  (b) **décode le pivot** (`Signal.AbsWord/.Bit`, tags, phase vérin, marche convoyeur, présence),
  (c) **expose `Km1Aux/B1Present/B2Present`** consommés par la coloration. → **à SUPPRIMER**, mais son
  **décodage doit déménager dans le panneau** (c'est le nouveau hub de décodage).
- `runtime/scenes/CarrouselScene.cs` :
  - `_Ready` construit `_labels = new CommandChainLabels(); _labels.Build(pivot, _ringNode, _cyl1Node,
    _cyl2Node, _sensor1Node, _sensor2Node);` → à remplacer par la création du panneau.
  - `_Process` (~ligne 282, cadence ~6 Hz via `_labelAccumulator`/`LabelRefreshPeriodS`) fait
    `_labels.Update(cmd, ret, _sim)` **puis la coloration d'état** qui lit `_labels.Km1Aux`,
    `_labels.B1Present`, `_labels.B2Present` et `_sim.Cylinder1/2.Position`. → la coloration **reste**,
    mais lit désormais l'état exposé par le **panneau**.
  - Matériaux d'état capturés au build : `_ringMat`, `_rodMat1/2`, `_sensorMat1/2`. Nœuds :
    `_ringNode`, `_cyl1Node`, `_cyl2Node`, `_sensor1Node`, `_sensor2Node`.
- `runtime/scenes/HealthHud.cs` : **patron** du panneau (CanvasLayer + Panel/Label, lecture seule,
  accumulateur ~5 Hz sur `_Process`). ⚠ MAIS `HealthHud` positionne en **pixels fixes** — le nouveau
  panneau doit **s'ancrer** (cf. pièges).
- Core lecture seule disponible : `Signal.ReadBit/AbsWord/Bit/WordRel/Tag`, `Component.Type/Tag`,
  `pivot.Components`, `pivot.GetSignal(...)`, `sim.Cylinder1/2.State.Position`, `sim.Conveyor`,
  `store.SnapshotCommands()/SnapshotReturns()`.

## Contrat d'API visé

**Nouveau fichier `runtime/scenes/ElementPanel.cs`** — `CanvasLayer` portant un `Control` **ancré**,
lecture seule :

```csharp
public partial class ElementPanel : CanvasLayer
{
    // Résout les signaux + crée une ligne par composant du pivot (ordre du pivot).
    // onRowHover(id, on) : appelé quand la souris entre/sort d'une ligne (sens ligne → 3D).
    public void Build(PivotModel pivot, System.Action<string, bool> onRowHover);

    // Recompose les cellules (état physique + cmd/ret) depuis des snapshots verrouillés + la sim.
    public void Update(ushort[] cmd, ushort[] ret, CarrouselSimulation sim);

    // Surligne/dé-surligne visuellement la ligne d'un composant (appelé par la scène : survol 3D → ligne).
    public void HighlightRow(string componentId, bool on);

    // État décodé exposé pour la coloration d'état 3D (reprend le rôle de CommandChainLabels).
    public bool Km1Aux { get; }
    public bool B1Present { get; }
    public bool B2Present { get; }
}
```

**Modifs `CarrouselScene.cs`** :
- `_Ready` : remplacer la création des labels par `_panel = new ElementPanel(); AddChild(_panel);
  _panel.Build(pivot, SetHover);`.
- Nouveau `private void SetHover(string id, bool on)` : (a) allume/éteint l'**émission** du matériau
  d'état de `id` ; (b) `_panel.HighlightRow(id, on)`. **Source unique de vérité** de la surbrillance
  (S4.3 branchera une 2ᵉ source dessus).
- Construire un `Dictionary<string, StandardMaterial3D> _highlightMat` (id → matériau d'état) **de
  façon additive** dans les builders existants (`BuildConveyor` → `conveyor.Id`, `BuildCylinder` →
  `cyl.Id`, `BuildSensorWindow` → `sensor.Id`). Réutilise les matériaux déjà capturés — n'en recrée pas.
- `_Process` : `_panel.Update(cmd, ret, _sim)` à la place de `_labels.Update(...)`, puis la coloration
  d'état **inchangée** mais lisant `_panel.Km1Aux/B1Present/B2Present` (au lieu de `_labels.*`).
- Retirer les champs `_labels`, les ancres devenues inutiles **uniquement si** plus référencées
  (⚠ `_ringNode`/`_cyl1Node`/… restent utiles à S4.3 pour le picking — **les garder**).

**Suppression** : `runtime/scenes/CommandChainLabels.cs` (retirer du disque ; aucune référence
résiduelle). Vérifier qu'aucun autre fichier ne l'importe (seul `CarrouselScene` le référence).

## Colonnes (D-Q3) — 5 colonnes, ordre du pivot

`Repère | Type | État physique | cmd %MW=bit | ret %MW=bit`

- **Repère** = `Component.Tag` (KM1, YV1, B1…).
- **Type** = `Component.Type` (conveyor / cylinder / sensor — abrégé lisible).
- **État physique** = décodé, **dispatch par type** (reprendre la logique de `CommandChainLabels`) :
  - convoyeur : `marche N°/s` (si KM1_AUX) / `arrêt` — vitesse depuis `params.speed_deg_per_s` ;
  - vérin : `tige P% (phase)` — phase depuis S11/S12/`cmd_extend` (rentré/sort/sorti/rentre). Le **%**
    vient de la position sim ; comme la sim expose `Cylinder1/2` par accesseurs fixes, la scène fournit
    au panneau le moyen d'obtenir la position par `id` (routage déjà connu de `BuildCylinder`) — **le
    panneau reste générique** (il itère les composants, ne code pas « 2 vérins ») ;
  - capteur : `palette présente` / `poste libre` (bit `ret_active`).
- **cmd %MW=bit** = adresse(s) de commande décodées (`%MW{AbsWord}.{Bit}={0|1}`), `—` si le composant
  n'a pas de `cmd` (capteurs).
- **ret %MW=bit** = adresse(s) de retour décodées ; pour les vérins, **nommer S11/S12** (via
  `Signal.Tag`) devant chaque adresse (cf. preview D-Q3).

**Peuplement générique** : itérer `pivot.Components.Values` (ordre d'insertion = ordre du tableau JSON
du pivot en pratique). Une ligne par composant. **Aucune supposition** sur le nombre de vérins/capteurs.

## Décisions pré-tranchées (ne pas ré-arbitrer)

- **D-arch — Émission ≠ albédo** : la surbrillance utilise le **canal émission** du matériau
  (`EmissionEnabled`, `Emission`, `EmissionEnergyMultiplier`). L'état continue de piloter `AlbedoColor`
  à ~6 Hz dans `_Process` → **aucune couleur d'état perdue** au retour du survol (les deux canaux ne
  se marchent pas dessus).
- **Panneau ancré** : `Control` en ancrage bord d'écran (ex. bord droit, hauteur ~plein, largeur
  fixe raisonnable) — **pas** de `Position/Size` en pixels codés en dur comme `HealthHud`. Doit tenir
  en Maximized (S4.1) et en 1080p. `MouseFilter = Stop` sur le panneau et ses lignes (voir piège).
- **Cadence** : réutiliser l'accumulateur `_Process` existant (~5-6 Hz) — pas de rafraîchissement 60 Hz
  (scintillement inutile).
- **Coloration d'état conservée à l'identique** : tige (Lerp ambre selon `Cylinder.Position`), anneau
  (vert si `Km1Aux`), fenêtres (vert si `Bi`). Seule la **source** des booléens change (panneau).

## Points durs / pièges (déjà instruits)

- **Conflit souris caméra ↔ panneau** : le panneau `Control` en `MouseFilter=Stop` **consomme** les
  events souris au-dessus de lui ; la caméra (S4.1) lit dans `_UnhandledInput` → un drag démarré sur
  le panneau **n'orbite pas**. **Vérifier ce comportement à ce sous-sprint** (la caméra existe déjà).
- **Survol de ligne** : utiliser les signaux Godot `mouse_entered` / `mouse_exited` d'un `Control` de
  ligne (ex. un `Panel`/`PanelContainer` par ligne, `MouseFilter=Stop`) → appeler `onRowHover(id, on)`.
- **Ordre du pivot** : `Components.Values` suit l'ordre d'insertion (ordre JSON) en pratique ; ne pas
  trier autrement (D-Q3 = ordre du pivot). Ne **pas** ajouter d'accesseur ordonné au core (banc figé).
- **Ne pas casser la coloration** : après la bascule labels → panneau, revérifier que anneau/tige/
  fenêtres changent bien de couleur selon l'état (c'est la régression la plus facile à introduire).

## Ce qu'il NE faut PAS faire

- **Aucune écriture `cmd`** (lecture seule stricte ; forçage = D-016, sprint 5).
- **Pas de picking 3D → panneau** ici (Area3D/collision = S4.3). Ce sous-sprint fait **seulement**
  ligne → 3D via `SetHover` ; `HighlightRow` est déjà livrée mais sa 2ᵉ source arrive en S4.3.
- **Ne pas** modifier le core, le datastore, la boucle Modbus, le heartbeat, la caméra (S4.1).
- **Ne pas** supprimer `_ringNode/_cyl1Node/_cyl2Node/_sensor1Node/_sensor2Node` (utiles à S4.3).
- Pas de nouvelle traversée de threads : tout sur le thread principal, snapshots verrouillés.

## Definition of Done (cochable)

- [ ] `ElementPanel.cs` créé : panneau **ancré**, **une ligne par composant du pivot**, 5 colonnes,
      adresses %MW **décodées** (jamais en dur), état physique correct par type.
- [ ] `CommandChainLabels.cs` **supprimé** ; plus aucun `Label3D` de chaîne dans la scène.
- [ ] **Coloration d'état préservée** (anneau/tige/fenêtres suivent l'état), alimentée par le panneau.
- [ ] Survol d'une ligne → l'élément 3D correspondant **s'éclaire** (émission), et se **rééteint** en
      sortie de survol, **sans perdre** sa couleur d'état.
- [ ] `SetHover` centralise la surbrillance (émission + `HighlightRow`) ; `_highlightMat` (id→matériau)
      construit additivement dans les builders.
- [ ] Build Godot **0 erreur** ; banc **inchangé** : `dotnet test` = **95**, 4 pytest verts.
- [ ] Smoke headless : recensement inchangé **+** trace du nombre de lignes panneau = nombre de
      composants du pivot (ex. `panel_rows=5`).

## Vérif autosuffisante

1. `dotnet test` → **95 passed** (zéro core touché).
2. Build Godot **0 erreur** ; `grep` : aucune référence résiduelle à `CommandChainLabels`.
3. `pytest testbench/test_modbus_chain.py` (hôte à l'écoute) → 4 verts — **inchangé**.
4. Smoke headless de scène (mode `--headless` + sonde) : `panel_rows` = nombre de `components`.
5. **Validation manuelle (Nico, F5)** : plus d'étiquettes flottantes ; panneau latéral lisible listant
   KM1/YV1/YV2/B1/B2 avec leurs %MW ; en animant (io_scanner) les colonnes bougent ; **survoler une
   ligne éclaire l'élément 3D** puis s'éteint ; l'anneau/les tiges/les fenêtres **gardent** leur
   coloration d'état.

## Banc attendu

`dotnet test` **inchangé (95)** + 4 pytest full-chain **inchangés/verts**. Glue Godot pure, zéro core.

## Fichiers touchés

- `runtime/scenes/ElementPanel.cs` — **créé**.
- `runtime/scenes/CarrouselScene.cs` — `_Ready` (panneau à la place des labels), `_Process`
  (coloration alimentée par le panneau), `SetHover`, `_highlightMat` (builders, additif).
- `runtime/scenes/CommandChainLabels.cs` — **supprimé**.
- (option) `runtime/scripts/smoke_scene.ps1` ou `smoke_anim.ps1` — ajouter la sonde `panel_rows` si
  la vérif headless l'exige (sans casser les recensements existants).

# NOTES pédagogiques — Sprint 4 « Ergonomie d'utilisation »

> Public : quelqu'un qui découvre Godot et la 3D interactive. On décompose les **mécanismes
> clés** introduits ce sprint — caméra orbitale, picking 3D, surbrillance sans perte d'état,
> panneau ancré peuplé du pivot — avec les pièges rencontrés. Rien de spécifique au carrousel :
> tout vaut pour **N machines** générées depuis un pivot (cf. `CLAUDE.md` §0bis).

Contenu livré : `OrbitCamera.cs` (neuf), `ElementPanel.cs` (neuf), `demo_sprint_04.ps1` (neuf),
suppression de `CommandChainLabels.cs`, modifications de `CarrouselScene.cs` et `project.godot`.
**Banc inchangé sur tout le sprint** : 95 tests (89 core + 6 serveur), build Godot 0 erreur, smoke
headless `ring=1 cylinders=2 pallets=3 sensors=2` + `panel rows=5`. **Zéro modification du core** :
tout le sprint est de la **glue Godot en lecture seule sur le thread principal** (Arch A intacte).

---

## 1. La caméra orbitale (S4.1) — le montage « gimbal »

### Le problème
Aux sprints 2-3, la caméra était **figée** : `AddPresentation` posait une `Camera3D` immobile qui
regardait le centre (`LookAt`). Pour une démo, on veut **tourner autour** de la machine, **zoomer**,
**se déplacer** — comme dans une visionneuse CAO à laquelle un automaticien est habitué.

### L'idée : séparer « où je regarde » de « comment je regarde »
On ne bouge **pas** la caméra directement. On construit un **montage à deux étages** (un *gimbal*,
comme une nacelle articulée) :

```
    OrbitCamera (Node3D)          <- le PIVOT : positionné sur la cible (centre machine),
        |                            on le fait tourner (yaw autour de Y, pitch autour de X)
        +-- Camera3D (enfant)     <- RECULÉE de `distance` sur +Z LOCAL du pivot
```

- **Orbiter** = faire tourner le **pivot** (yaw/pitch). La caméra, étant son enfant reculée sur +Z,
  décrit alors un arc de cercle autour de la cible **sans qu'on ait à calculer la moindre position
  sur une sphère** : l'arbre de scène fait la trigonométrie pour nous.
- **Zoomer** = rapprocher/éloigner la caméra de son parent (changer `distance`, sa position locale Z).
- **Paner** = déplacer le **pivot** dans son propre plan (droite/haut locaux) → la cible glisse.

C'est le truc central : *une hiérarchie de nœuds bien choisie remplace des maths de caméra*.

### Zoom proportionnel à la distance (D-Q1)
Un zoom à pas **constant** est pénible : de loin il rampe, de près il traverse la machine d'un cran.
On rend donc le pas **multiplicatif** : chaque cran de molette multiplie `distance` par un facteur
(ex. ×0.9 / ×1.1). Conséquence : le déplacement **absolu** est grand quand on est loin, petit quand
on est près. Puis on **borne** : `distance` clampée dans `[0.3·R, 8·R]` (R = rayon du pivot) — pas de
traversée du centre, pas de fuite à l'infini.

### Bornes qui protègent le confort
- **Pitch** clampé (ici `[-89°, -5°]`) : on ne passe **jamais sous le sol** ni au zénith exact (au
  pôle, le yaw devient ambigu — le fameux *gimbal lock*).
- **Cadrage initial tiré du pivot** : `FrameFrom(center, radius)` place le pivot sur `path.center` et
  choisit une `distance` de départ qui **cadre** la machine. **Seule dépendance au pivot** ; ensuite
  la caméra est libre. → générique : une autre machine se cadre toute seule depuis **son** pivot.

### Piège n°1 : la souris de la caméra vs l'UI
Si la caméra lisait la souris dans `_Input`, un drag **commencé sur le panneau** ferait aussi
orbiter la scène. Solution en **deux temps** :
1. La caméra lit son entrée dans **`_UnhandledInput`** (pas `_Input`) : elle ne voit que les events
   que **personne n'a consommés** avant.
2. Le panneau (`Control`) est en **`MouseFilter = Stop`** : il **consomme** tout clic qui le touche.

Résultat : sur le panneau → l'UI mange l'event, la caméra ne le voit pas. Ailleurs → l'event
« tombe » jusqu'à `_UnhandledInput` et la caméra l'utilise. Zéro if, zéro test de zone : c'est le
**pipeline d'input** de Godot qui arbitre.

---

## 2. Le picking 3D (S4.3) — survoler un objet de la scène

### Ce qu'est une `Area3D`
Une `Area3D` est un **volume immatériel** : aucune collision solide, aucune force — juste un
**capteur**. Quand le curseur passe dessus, elle émet les signaux `mouse_entered` / `mouse_exited`.
On lui greffe une `CollisionShape3D` (la forme du capteur), enfant du nœud de l'élément (elle hérite
donc de sa position/rotation), et on branche :

```csharp
area.MouseEntered += () => SetHover(id, true);
area.MouseExited  += () => SetHover(id, false);
```

### Piège n°2 (à traiter EN PREMIER) : le picking est désactivé par défaut
En Godot 4, une `Area3D` **ne dira rien** tant que le viewport ne tire pas un **rayon de picking**
sous le curseur à chaque frame. Ce drapeau est **faux par défaut** :

```csharp
GetViewport().PhysicsObjectPicking = true;   // dans _Ready
```

Sans cette ligne, tout le sous-sprint semble « ne rien faire » alors que le code est correct. C'est
le piège classique — on l'active donc en tout premier.

### Formes approximatives assumées
On ne cherche pas la précision au pixel, juste « le curseur est-il sur cet élément ? » :
- **Anneau convoyeur** = un `CylinderShape3D` **plat** couvrant l'empreinte. L'anneau est un
  `CsgCombiner3D` ; **activer la collision du CSG serait coûteux et inutile**. Le survol se déclenche
  aussi au-dessus du trou central — sans conséquence (rien d'autre n'y est).
- **Vérin** = un cylindre englobant le fût **et** la course de la tige (hauteur `BodyHeight+stroke`,
  décalé de +Y/2) → l'élément reste survolable quelle que soit la position de la tige.
- **Capteur** = une `BoxShape3D` aux dimensions de la fenêtre.

### Choix de design : où poser le capteur (dette D-018)
L'amorce prévoyait d'attacher le picking via des références capturées (`_ringNode`, `_cyl1Node`…).
On a préféré le poser **dans les builders, sur le nœud local** de chaque composant, au moment où on
le construit. Avantage : **générique** — une `Area3D` par composant du pivot, **sans** coder « le
vérin n°1 », « le vérin n°2 » (cf. §0bis). Effet de bord : les 5 champs `_ringNode/_cyl*Node/...`
deviennent **vestigiaux** (assignés, jamais relus). Conformément à `CLAUDE.md` §4, on **signale**
(dette **D-018**) au lieu de supprimer (retrait = refactor hors périmètre S4.3).

---

## 3. Surbrillance sans perdre la couleur d'état — émission ≠ albédo (D-arch)

### Le conflit
La **couleur d'état** (sprint 3) est déjà peinte sur l'**albédo** des matériaux : tige ambre au
repos, anneau vert si le convoyeur tourne, fenêtre verte si présence. Si la **surbrillance de survol**
écrasait aussi l'albédo, on **perdrait** la couleur d'état au retour du survol.

### La solution : deux canaux distincts
Un `StandardMaterial3D` a plusieurs canaux indépendants. On répartit :

| Canal | Piloté par | Cadence |
|---|---|---|
| **Albédo** (`AlbedoColor`) | l'**état** physique | ~6 Hz (continu) |
| **Émission** (`EmissionEnabled`/`Emission`) | la **surbrillance** de survol | événementiel (on/off) |

L'émission « allume » le matériau **par-dessus** l'albédo sans le remplacer : quand le survol
s'éteint, l'albédo (= la couleur d'état) est **toujours là, intact**. Deux informations, deux canaux,
composition propre — jamais de « qui écrase qui ».

### État de surbrillance **partagé** = symétrie gratuite
La surbrillance a **deux sources** : survoler une **ligne** du panneau, ou survoler un **élément 3D**.
Les deux appellent la **même** méthode :

```csharp
SetHover(id, on) :  allume/éteint l'émission de _highlightMat[id]
                    ET appelle _panel.HighlightRow(id, on)
```

Puisque la logique est **écrite une seule fois** et que les deux entrées y convergent, la **symétrie
est garantie par construction** : survol ligne → 3D et survol 3D → ligne produisent **exactement** le
même rendu. Aucune duplication à maintenir synchrone.

```
  survol LIGNE  --\                          /--> glow EMISSION de l'element 3D
                    >--  SetHover(id, on)  --<
  survol 3D     --/                          \--> fond de la LIGNE du panneau
```

Le pointeur étant unique, **un seul survol est actif à la fois** : un simple booléen par id suffit,
pas de compteur de références (on ne sur-conçoit pas — cf. CLAUDE.md §3).

---

## 4. Le panneau des éléments (S4.2) — lire le pivot, pas le carrousel

### Peuplé du pivot, décodé du pivot
Le panneau crée **une ligne par composant** en itérant `Components.Values` (**ordre du pivot**), et
**décode** les adresses via `Signal.AbsWord` / `Signal.Bit` — **jamais** un `%MW` écrit en dur, jamais
« 2 vérins » codé en dur. Donne une autre machine → le panneau liste **ses** éléments et **ses**
adresses. Les 5 colonnes : `Repère | Type | État | cmd %MW=bit | ret %MW=bit`.

### La relocalisation du décodage (le vrai travail invisible)
Au sprint 3, les `Label3D` billboard collés aux éléments rendaient la scène **illisible** → on les
supprime (`CommandChainLabels.cs`). **Mais** ce fichier ne faisait pas qu'afficher : il **décodait**
`Km1Aux/B1Present/B2Present` depuis le datastore, et `CarrouselScene._Process` s'en servait pour la
**coloration d'état 3D**. Supprimer le fichier sans précaution **cassait la coloration**.

Solution : le **décodage déménage dans le panneau** (nouveau hub unique). Le panneau lit les snapshots
du datastore (`SnapshotCommands`/`SnapshotReturns`, qui verrouillent en interne — comme `HealthHud`),
expose l'état décodé, et `CarrouselScene._Process` le lit **de là** pour colorer. La coloration est
**préservée**, la source des booléens a juste changé.

### Panneau **ancré**, pas en pixels fixes (piège n°3)
`HealthHud` avait position/taille **en dur** — acceptable pour un petit bandeau. Le panneau, lui,
doit tenir en **Maximized** et en **plein écran 1080p**. On utilise donc des **ancrages** (collé au
bord droit, hauteur relative) plutôt que des coordonnées absolues : la mise en page **suit** la
taille de la fenêtre au lieu de se décaler.

### Un délégué pour rester générique
Le panneau doit afficher la **position 0..1** d'un vérin (« tige 100% »). Plutôt que de lui passer
tout l'objet simulation (couplage lourd, param `sim` inutilisé), on lui passe un **délégué**
`CylinderPositionById(id)` : « donne-moi la position de **cet id** ». Le routage `id → Cylinder1/2`
vit dans la scène (là où il est déjà connu) ; le panneau reste ignorant du carrousel.

---

## 5. Affichage plein écran (S4.1 / D-Q4)

Dans `project.godot` :
- **`window/size/mode = 2`** = **Maximized** au lancement (barre de titre visible → facile de placer
  une 2e fenêtre, ex. un terminal de démo, à côté).
- **1920×1080** de base + **stretch `canvas_items` / `expand`** : l'UI se met à l'échelle proprement
  quelle que soit la résolution réelle.
- **MSAA 3D 4×** (`msaa_3d = 2`) : anticrénelage qui lisse les bords de l'anneau CSG et des vérins.
- **F11** (géré dans `OrbitCamera._UnhandledInput`) bascule **Maximized ↔ Fullscreen borderless** :
  vrai plein écran à la demande, et **sortie de secours** garantie.

---

## 6. La démo observable (S4.4)

`demo_sprint_04.ps1` reprend l'ossature de `demo_sprint_03.ps1` : `param($PyHost, $Port, -Repeat)`,
**pré-vol du port 502** (refuse de tourner si la scène Godot n'écoute pas — évite de reproduire
**D-013** par accident), puis des **phases guidées à la voix**. La souris/caméra **ne s'automatise
pas** : le script **anime** la machine (il écrit `cmd` via `io_scanner_sim.py`, **jamais** `ret` — il
**joue le M580**) et **dit** à Nico quoi regarder (naviguer, lire le panneau, survoler). Contraintes :
**ASCII pur** (Windows PowerShell 5.1 lit les `.ps1` en Windows-1252), **pas** de
`$ErrorActionPreference='Stop'` (sinon le stderr d'`io_scanner` tant que rien n'écoute deviendrait
fatal). Vérifié : pré-vol à vide → « rien n'ecoute sur 502 », **exit 1, pas de stacktrace**.

---

## 7. Ce qui reste à valider à l'œil (Nico, F5)

Le picking, le survol et la caméra sont du **pur Godot** (peu testables en xUnit). Le smoke headless
vérifie le **minimum** (la scène se construit, le panneau est peuplé : `rows = nb composants`). Le
reste est **validation visuelle** guidée par `demo_sprint_04.ps1` :
- **S4.1** : Maximized au lancement ; orbite/pan/zoom souples ; jamais sous le sol ni traversée ;
  F11 aller-retour.
- **S4.2** : plus d'étiquettes 3D ; panneau droit lisible ; colonnes qui bougent quand ça anime ;
  survol ligne → élément 3D éclairé ; la coloration d'état **reste** ; drag sur le panneau n'orbite
  pas.
- **S4.3** : survol d'un élément 3D → sa ligne se surligne + glow ; **symétrie** identique dans les
  deux sens ; composition émission/**glass** (fenêtres semi-transparentes) OK ; couleur d'état jamais
  perdue.

---

## 8. Piège de processus rencontré (méthode)

Le sous-agent **S4.3 a été interrompu par la limite de session** après avoir écrit le code et lancé
le banc, mais **avant** de finaliser sa paperasse (carnet, DoD, dette D-018). L'orchestrateur a alors
**re-vérifié lui-même** (build + banc + smoke, tous verts) puis **complété la bookkeeping** avant de
commiter — plutôt que de relancer un sous-agent qui aurait risqué la même limite. Leçon : le **code
sur le disque** est le livrable ; quand un sous-agent tombe en fin de course, l'orchestrateur vérifie
et termine la queue mécanique lui-même. Trace conservée dans le commit S4.3 et le carnet `00_etat.md`.

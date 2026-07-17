# NOTES — Sprint 06 « Forçage de debug »

Notes pédagogiques : mécanismes clés introduits au sprint 6, pour quelqu'un qui découvre le projet.
Public visé : automaticien / développeur qui reprend le code sans l'avoir écrit.

---

## 1. Le problème : forcer une commande sans mentir au bus

Au sprint 5, on savait injecter des **défauts** (une panne physique, un capteur menteur) pour éprouver
le M580 — mais **sans jamais écrire un mot Modbus** : la sim reste seule maîtresse des retours `ret`.

Le sprint 6 ajoute le **forçage de commande** : l'opérateur veut fixer la valeur **effective** d'un
signal de la zone `cmd` (KM1 run, YV1, YV2) pour deux usages :

- **piloter la machine sans PLC** — utile en démo ou en mise au point quand aucun automate n'est
  branché : on fait tourner le convoyeur, sortir un vérin, à la souris ;
- **forcer malgré le PLC** — l'automate commande une valeur, on la surclasse localement (comme le
  « forçage de variable » de Control Expert).

La contrainte cardinale est la même qu'au sprint 5, poussée un cran plus loin : **aucune écriture du
datastore**, ni `ret`, ni **`cmd`**. Sinon on tricherait sur le contrat de transport.

---

## 2. Le mécanisme : un masque À LA LECTURE (le cœur du sprint)

### 2.1 Rappel du pipeline d'un `Tick`

```
Tick(store, dt):
  1.  cmd = store.SnapshotCommands()      <- COPIE DEFENSIVE de la zone cmd (le PLC l'a ecrite)
  1bis. [FORCAGE]  <<< NOUVEAU sprint 6 : on substitue les bits cmd forces DANS cette copie
  2.  decode run/extend1/extend2          <- lit la copie (donc voit la valeur EFFECTIVE)
  3.  avance la cinematique (verins, convoyeur, palettes)
  3ter. si RetFrozen -> return (on ne publie rien)
  4.  heartbeat++
  5.  encode ret (heartbeat, S11/S12/S21/S22, KM1_AUX, B1/B2)
  5bis. [MASQUE CAPTEUR]  <- defaut « capteur bloque » : force un bit ret APRES encodage (sprint 5)
  6.  store.PublishReturns(ret)
```

Le forçage est **le symétrique exact** du masque capteur du sprint 5, mais **de l'autre côté** du
pipeline :

| | Masque capteur (S5) | Forçage (S6) |
|---|---|---|
| zone | `ret` | `cmd` |
| moment | **après** encodage (étape 5bis) | **avant** décodage (étape 1bis) |
| effet | le PLC lit un retour menteur | la sim lit une commande substituée |
| écriture datastore | **jamais** | **jamais** |

### 2.2 Pourquoi « à la lecture » suffit, et pourquoi c'est robuste

`SnapshotCommands()` renvoie une **copie défensive** de la zone `cmd`. On mute cette copie, pas le
buffer partagé. Conséquence : le M580, qui réécrit **toute** la zone `cmd` à chaque scan via FC16,
ne peut **rien effacer** de notre forçage — au tick suivant, on re-substitue sur la copie fraîche.
Le forçage est donc **insensible par construction** à l'I/O Scanner (c'était le point dur historique,
identifié dès l'amorce du sprint 4). Aucun « mode PLC absent » séparé n'est nécessaire.

### 2.3 Un seul mécanisme couvre les deux usages

- **Sans PLC** : personne n'écrit `cmd` → la copie snapshot est à 0 → forcer un bit à 1 = le piloter.
- **Malgré le PLC** : le PLC écrit `cmd` → on substitue à la lecture → notre valeur gagne.

C'est le **même code**. La substitution ne « sait » pas s'il y a un PLC ou non ; elle écrase le bit
dans la copie, point. (Décision QCM D6-Q1 : mécanisme unique.)

### 2.4 Le code (extrait, `CarrouselSimulation.Tick`)

```csharp
ushort[] cmd = store.SnapshotCommands();          // copie defensive (le PLC l'a peut-etre ecrite)

foreach (var (compId, sigName, mode) in Forces.ActiveForces)
    if (_cmdTorSignals.TryGetValue((compId, sigName), out var sig))
        sig.WriteBit(ref cmd[sig.WordRel], mode == ForceMode.ForceHigh);   // substitution DANS la copie

bool run     = _cmdRun.ReadBit(cmd[_cmdRun.WordRel]);   // voit la valeur EFFECTIVE
// ...
```

`_cmdTorSignals` est l'**index générique** `(compId, sigName) -> Signal` construit une fois au
constructeur, miroir exact de `_retTorSignals`. On parcourt **tous** les composants du pivot et on
retient les signaux `cmd` TOR : aucune connaissance du carrousel, une machine future inconnue sera
forçable pareil (§0bis de `CLAUDE.md`).

---

## 3. `ForceSet` : pourquoi une classe séparée de `FaultSet`

`ForceSet` est un **jumeau** de `FaultSet` (même forme : dictionnaire muté par l'IHM, lu en tête de
`Tick`, **sans verrou** car un seul thread — le thread principal Godot — y touche). Mais c'est une
**classe distincte**, pas une extension. Raisons :

- **Sémantique Control Expert** : un forçage n'est **pas** un défaut. Un défaut = un état physique
  dégradé ; un forçage = une substitution volontaire d'entrée automate. Les mélanger brouillerait le
  vocabulaire de l'automaticien.
- **Invariant préservé** : `FaultSet` garantit « défaut inactif = nominal strict » (les 4 pytest
  full-chain en dépendent). En n'y touchant pas, on garde cette garantie intacte.
- **Marquage distinct** (voir §5) : forçage et défaut ne se peignent pas au même endroit.

`ForceMode { Auto, ForceLow, ForceHigh }` ; `Auto` = **absence d'entrée** (`SetForce(Auto)` efface),
donc « ForceSet vide = nominal » est trivial et testable.

---

## 4. Composition des couches (le piège du déterminisme)

Quand on force **et** qu'un défaut est actif sur le même élément, il faut un ordre **déterministe**.
Les trois couches s'appliquent dans cet ordre, chacune pouvant surclasser la précédente :

```
   [1] FORCAGE cmd  (tete de Tick)        substitue le bit de commande
        |
        v
   [2] DEFAUT PHYSIQUE  (AdvanceCylinder / ConveyorSlip)   detourne la cinematique
        |
        v
   [3] MASQUE CAPTEUR ret  (fin de Tick)  force le bit retour
```

**Exemple clé (testé)** : forcer YV1 = 1 (« sors ! ») **avec** le défaut « vérin ne sort pas ».
- couche [1] : la commande effective d'extension devient 1 ;
- couche [2] : `AdvanceCylinder` voit le défaut `CylinderStuckRetracted` et **force la commande
  effective à faux** — la tige reste rentrée.
- Résultat : **le défaut physique gagne**. S12 reste à 0.

C'est physiquement juste : une commande, même forcée, ne peut pas faire bouger un actionneur cassé.
Le déterminisme est prouvé par un test xUnit (`CarrouselSimulationTests`) et rejoué en phase 6 de la
démo.

### 4.1 Cas particulier : KM1_AUX sous forçage

Le contact de retour KM1_AUX **recopie la commande effective** (physique), pas la commande PLC. Donc
forcer `cmd_run` à 1 fait passer KM1_AUX à 1 après `feedback_delay_ms` — **sans que le PLC ait
commandé la marche**. C'est cohérent et **voulu** : une marche forcée localement doit être
**détectable** par l'automaticien (KM1_AUX=1 alors qu'il commande `--run 0` = signature d'un forçage
en local). Démontré en phase 4.

---

## 5. `BlockerIneffective` : un défaut à forte signature, minuscule en code

Nouveau `PhysicalFault` du vérin bloqueur. La subtilité : la tige **sort normalement** (S11/S12
nominaux, `AdvanceCylinder` **inchangé**) — visuellement, elle a l'air de bloquer. Mais le poste est
**exclu de `CollectBlockedStations`** :

```csharp
if (_cyl1.State.IsEngaged && Faults.GetPhysical(_cyl1.Id) != PhysicalFault.BlockerIneffective)
    blocked.Add(_cyl1.StationAngleDeg);
```

Un vérin engagé transforme normalement son poste en **obstacle fixe** pour les palettes (cf.
`PalletSet.Advance`). Ici, on **n'ajoute pas** ce poste aux obstacles → une palette **traverse** une
tige pourtant levée. Signature terrain : « **je crois bloquer, mais B1 se libère quand même** » — un
défaut mécanique réaliste (entraînement à friction qui ne retient plus), à forte valeur pédagogique
côté diagnostic PLC, pour **cinq lignes** de code.

C'est un **défaut** (pas un forçage) : marqué en **rouge** dans le canal émission 3D standard, comme
les autres défauts du sprint 5.

---

## 6. L'UI : deux concepts, deux marquages distincts

### 6.1 Colonne « Forçage » (7ᵉ colonne du panneau)

Chaque ligne ayant **au moins un signal `cmd` TOR** (KM1, YV1, YV2) reçoit un `MenuButton` dédié dans
la nouvelle colonne. Son popup, peuplé **à l'ouverture** (`AboutToPopup` → `PopulateForceMenu`), offre
pour chaque signal `cmd` : **Auto / forcer à 0 / forcer à 1**. Générique : les entrées viennent des
signaux `cmd` TOR du pivot, jamais d'une liste carrousel. Les lignes sans commande (capteurs) affichent
« — ».

Patron identique au menu « Défaut » du sprint 5 (`IndexPressed` → index retraduit en `ForceCommand`
via une liste mémorisée). Le panneau reste **générique** : il ne connaît ni `_sim` ni le modèle de
forçage, seulement deux délégués fournis par la scène — `onForce` (menu → `_sim.Forces.Apply`) et
`forceModeBySignal` (mode courant d'un signal, pour l'écart).

### 6.2 L'écart PLC / effectif, inline dans la cellule `cmd`

Quand un signal est forcé, sa cellule `cmd` n'affiche plus `%MW100.1=0` mais l'**écart** :
`YV1 %MW100.1 : PLC=0 → forcé 1`, et la cellule est **teintée magenta** (convention « variable
forcée », distincte du rouge défaut et du vert état). La valeur « PLC » vient du snapshot `cmd` du
datastore (ce que le PLC a réellement écrit) ; la valeur « forcé » vient du délégué. On lit d'un coup
d'œil **ce que le PLC croit commander** vs **ce que la sim exécute**.

### 6.3 Pourquoi le forçage n'est PAS dans le canal émission 3D

Le canal émission des matériaux 3D est déjà saturé : défaut (rouge) > sélection (cyan) > survol
(bleu). Y ajouter le forçage forcerait une priorité de plus et pourrait **masquer** un défaut sur un
élément à la fois forcé et faulté. On garde donc le forçage **au panneau seulement**. Et en 3D,
l'effet du forçage **se voit dans le mouvement** : un vérin qui sort, un anneau qui tourne — pas
besoin d'un halo pour ça.

---

## 7. Le piège clavier AZERTY (petit mais réel)

Le cyclage de sélection du sprint 5 utilisait `[` et `]`. Sur un clavier **AZERTY**, ces touches
n'existent pas en direct : il faut **AltGr** — inutilisable en pratique. On les a remplacées par des
**lettres**, touches directes : **`A` = précédent, `Z` = suivant** (adjacentes en haut à gauche sur
AZERTY, ordre spatial cohérent). Nouvelle touche **`G`** = ouvrir le menu forçage de la sélection
(comme `F` ouvre le menu défaut).

Point technique : Godot expose plusieurs champs pour une touche. On lit **`key.Keycode`**, qui est
**dépendant du layout** (le label imprimé sur la touche dans la disposition courante) — donc `Key.A`
vise bien la touche **marquée A** sur AZERTY. (Le champ `PhysicalKeycode`, lui, viserait la position
physique QWERTY et se serait trompé de touche.) Les touches existantes `B`/`R`/`F`/`Espace` sont déjà
des lettres directes, aucun conflit. **À reconfirmer visuellement à F5** — si un décalage apparaît,
basculer sur `KeyLabel`.

---

## 8. Ce que le sprint garantit

- **Zéro écriture datastore** (ni `ret` ni `cmd`) : le forçage vit dans `_sim.Forces`, agit sur la
  **copie** snapshot. Prouvé par un test qui vérifie que le datastore `cmd` reste inchangé sous forçage.
- **Générique via le pivot** : forçable = tout signal `cmd` TOR déclaré au pivot ; aucun id carrousel.
- **Inactif = nominal strict** : `ForceSet` vide + `BlockerIneffective` inactif ⇒ comportement
  identique à avant. Les 4 pytest full-chain restent verts (non impactés).
- **Banc** : core **re-figé 109 → 121** (+12 cas, prévu par l'amorce S6.1) ; serveur 10 inchangé ;
  build Godot 0 erreur ; démo `demo_sprint_06.ps1` parse OK (pré-vol refuse proprement).

---

## 9. Pièges rencontrés / à retenir

1. **Cellule hétérogène** : la 7ᵉ colonne est tantôt un `MenuButton` (ligne à commande) tantôt un
   `Label` « — » (ligne sans commande). `Row.Cells` a dû passer de `Label[]` à `Control[]`, et
   `AutoFitColumns` ne **mesure** que les `Label` (garde `is Label`) — sinon on tenterait de mesurer
   la largeur de texte d'un bouton.
2. **Deux boutons ▾ adjacents** : le ▾ « Forçage » (colonne 7, mesurée) et le ▾ « Défaut » (en fin de
   ligne, hors colonne, hérité de S5.3) se retrouvent côte à côte. C'est ce qui **préserve
   l'alignement en-têtes ↔ colonnes** ; le coût est cosmétique (le ▾ Défaut est détaché de son
   libellé). Consigné en **dette D-019** (cosmétique) pour arbitrage à F5.
3. **Ordre des couches** : ne jamais réordonner forçage → défaut → capteur. C'est ce qui rend la
   composition déterministe et testable.

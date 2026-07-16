# S3.3 — Chaîne de commande par élément (texte + couleur) + démo guidée

> **Amorce autosuffisante** (cold-start). Tu lis **seulement** : `CLAUDE.md` +
> `docs/sprints/sprint_03/00_etat.md` + ce fichier. Sous-sprint **visuel** (glue Godot pure) et
> **sortie observable** du sprint. **Dépend de S3.1 et S3.2.**

## Objectif

Rendre la **chaîne de commande lisible et compréhensible d'un coup d'œil**, **ancrée sur chaque
élément 3D** (demande explicite de Nico, 2026-07-16). Pour chaque composant, montrer :

```
commande Modbus (bit cmd + %MW)  ──►  état physique de l'élément  ──►  retour Modbus (bit ret + %MW)
```

…sous **deux formes complémentaires** (D-Q4) :
- **étiquette texte 3D** ancrée près de l'élément → porte le **lien Modbus explicite** ;
- **coloration d'état** de l'élément → porte l'**état instantané** à l'œil.

Plus une **démo guidée** `demo_sprint_03.ps1` qui joue le M580 et enchaîne les scénarios en
annonçant quoi regarder. **Lecture seule** (aucune écriture `cmd`).

## Contexte code (source de vérité)

- `runtime/scenes/CarrouselScene.cs` : builders `BuildConveyor` (anneau CSG, nommé `conveyor`),
  `BuildCylinder` (nœud nommé `cylinder_1`/`cylinder_2`, enfant `body` + `rod`), `BuildPallet`
  (`pallet_i`), `BuildSensorWindow` (nommé `presence_station_1`/`_2`, `GlassMat` translucide).
  Refs déjà capturées : `_rod1/_rod2`, `_pallets[]`. **Non capturés** : matériau de l'anneau, des
  fenêtres capteurs (à capturer ici pour les colorer).
- `_sim` expose : `Cylinder1.Position`/`Cylinder2.Position` (0..1), `Conveyor.IsRunning`,
  `Pallets.AnglesDeg`, `Heartbeat`.
- **Fourni par S3.1** : `ModbusDataStore.SnapshotReturns()` (+ `SnapshotCommands()` existant).
- **Décodage** : `PivotModel` résout chaque `Signal` en `{zone, word, bit}` (adresse %MW absolue =
  `zone.base_mw + word`) et fournit `Signal.ReadBit(motsZone)`. Lis `PivotModel.cs` pour la méthode
  exacte d'accès aux signals d'un `Component` (déjà utilisée par la sim). **Aucune adresse en dur.**

## Mapping concret à afficher (dérivé du pivot — pour référence)

| Élément | cmd (source) | état physique (source) | ret (source) | %MW attendus |
|---|---|---|---|---|
| **KM1** (convoyeur) | `cmd_run` = `_store.cmd`[w0.b0] | `Conveyor.IsRunning` (20°/s) | `KM1_AUX` = `ret`[w1.b6] | cmd %MW100.0 → ret %MW201.6 |
| **YV1** (vérin 1) | `cmd_extend` = `cmd`[w0.b1] | `Cylinder1.Position` (0..1) | `S11`=`ret`[w1.b0], `S12`=`ret`[w1.b1] | cmd %MW100.1 → ret %MW201.0/.1 |
| **YV2** (vérin 2) | `cmd_extend` = `cmd`[w0.b2] | `Cylinder2.Position` | `S21`=`ret`[w1.b2], `S22`=`ret`[w1.b3] | cmd %MW100.2 → ret %MW201.2/.3 |
| **B1** (capteur) | — (retour seul) | palette présente (poste 90°) | `B1`=`ret`[w1.b4] | ret %MW201.4 |
| **B2** (capteur) | — (retour seul) | palette présente (poste 270°) | `B2`=`ret`[w1.b5] | ret %MW201.5 |

**Ne code pas ces adresses en dur** : lis-les des `signals` du pivot (`tag` + `{zone, word, bit}`).
La table ci-dessus n'est qu'un **oracle de vérification** (ce que tu dois voir s'afficher).

Exemples de texte cible (une ligne par élément, compact) :
```
YV1  cmd_extend %MW100.1=1  →  tige 47% (sort)  →  S11 %MW201.0=0  S12 %MW201.1=0
KM1  cmd_run %MW100.0=1     →  marche 20°/s      →  KM1_AUX %MW201.6=1
B1   —                      →  palette présente  →  B1 %MW201.4=1
```

## Contrat d'API visé

### `CommandChainLabels` (runtime/scenes/CommandChainLabels.cs — **neuf**)
Helper qui **construit** un `Label3D` par composant (convoyeur, vérins, capteurs) et les **met à
jour**. Suggestion de forme (adapte librement, garde-le simple) :
- `void Build(PivotModel pivot, Node3D root, /* refs des nœuds/positions */)` — crée les `Label3D`
  (billboard, `Modulate`/`FontSize` lisibles), attachés au-dessus de chaque élément.
- `void Update(cmdWords, retWords, simState)` — recompose chaque `.Text` à partir des snapshots
  décodés (via les `signals` du pivot) et de l'état physique lu sur `_sim`.

### `CarrouselScene` (modif)
- Capturer au build les **matériaux à colorer** : tige (`_rod1/_rod2` → leur `MaterialOverride`),
  **anneau** convoyeur, **fenêtres capteurs**. Garder pour chacun une **couleur repos** et une
  **couleur active**.
- Créer le `CommandChainLabels` (par code, `AddChild`), le nourrir chaque rafraîchissement.
- Dans un `_Process` (ou en réutilisant la cadence basse du HUD S3.2), à **~5-10 Hz** :
  1. `cmd = _store.SnapshotCommands()` ; `ret = _store.SnapshotReturns()` (thread principal) ;
  2. `labels.Update(cmd, ret, _sim)` ;
  3. **coloration** : tige `YVi` teintée si `cmd_extend`=1 (ou dégradé selon `Position`) ; anneau
     teinté si `KM1_AUX`=1 ; fenêtre capteur `Bi` **allumée** si son bit `ret`=1, éteinte sinon.

### `demo_sprint_03.ps1` (runtime/scripts/ — **neuf**)
- **Pré-vol port 502** obligatoire (calqué sur `demo_sprint_02.ps1`, réf. `docs/memory.md`) : la
  **scène** doit être lancée (elle est le serveur sur 502) ; refuser proprement sinon.
- Joue le **M580** via `testbench/io_scanner_sim.py` (client FC3/FC16) et **enchaîne
  automatiquement** les scénarios, en **annonçant à chaque phase quoi regarder** (l'étiquette de tel
  élément, la couleur qui change). Scénario suggéré : démarrer KM1 → sortir YV1 (voir cmd→tige→S12,
  tige teintée, B1 s'allume, palette bloquée) → accumulation → rentrer YV1 (S11) → arrêter KM1.
- **ASCII pur**, PowerShell 5.1.

## Décisions pré-tranchées

- **`Label3D` billboard** (orienté caméra) = « sur chaque élément 3D » au sens propre. Cadence de
  rafraîchissement **basse** (~5-10 Hz) : le texte ne doit pas scintiller (le 60 Hz est inutile ici).
- **Décodage piloté par le pivot** : repères (`tag`) et adresses (`{zone, word, bit}` → %MW) viennent
  des `signals`. **Zéro adresse en dur** (invariant CLAUDE.md).
- **Coloration = réutilisation** des matériaux déjà posés (on modifie `AlbedoColor`) : capturer les
  refs manquantes (anneau, fenêtres) au build, garder repos/actif. Peu coûteux.
- **Lecture seule / Arch A** : tout se lit sur le **thread principal Godot** via snapshots
  verrouillés + `_sim`. On ne touche jamais le datastore depuis le thread serveur.
- **Redondance assumée avec S3.2** : le HUD santé (S3.2) montre serveur/heartbeat/activité ; ici on
  montre la **chaîne par élément**. Pas de « panneau mots bruts » séparé (les étiquettes le rendent
  redondant, cf. overview).

## Questions résiduelles (trancher en autonomie)

- Étiquette pour les **palettes** ? Elles n'ont pas de signal Modbus propre (elles *causent* B1/B2).
  **Reco** : pas d'étiquette Modbus sur les palettes (garder l'écran lisible) ; leur effet est déjà
  visible via B1/B2. À toi de juger si un libellé léger aide.
- Position/hauteur des `Label3D` : ajuste pour ne pas masquer la géométrie (au-dessus de l'élément).

## Definition of Done (cochable)

- [ ] Chaque élément (KM1, YV1, YV2, B1, B2) porte une **étiquette 3D** montrant `cmd → physique →
      ret` avec les **bons %MW** (conformes à l'oracle ci-dessus, mais **lus du pivot**).
- [ ] **Coloration d'état** cohérente : tige teintée sous commande, fenêtre capteur allumée quand
      B1/B2=1, anneau teinté quand KM1_AUX=1.
- [ ] Les valeurs **évoluent en direct** quand le M580 (ou `io_scanner_sim`) pilote les bits.
- [ ] `demo_sprint_03.ps1` : **pré-vol port** + scénarios guidés, **ASCII pur**, se déroule sans
      retaper de commandes ; Nico valide la lisibilité de la chaîne à l'œil.
- [ ] Build Godot **0 erreur** ; `smoke_anim.ps1` + **4 pytest full-chain** **inchangés/verts**.
- [ ] Aucune écriture `cmd` ; builders (brique 5) et animation (S2.2) **non régressés**.

## Vérif autosuffisante (prouver le vert)

```
# 1) Lancer la scène (serveur sur 502).
# 2) Dérouler la démo :
pwsh runtime/scripts/demo_sprint_03.ps1
#    -> observer : étiquettes cmd→physique→ret qui changent, couleurs qui basculent.
# 3) Non-régression :
dotnet build                              # 0 erreur
pwsh runtime/scripts/smoke_anim.ps1       # vert
pytest testbench/test_modbus_chain.py -v  # 4 passed
```

## Banc attendu

**Inchangé** : `dotnet test`, `smoke_anim.ps1`, 4 pytest restent verts (glue lecture seule, aucun
changement de comportement Modbus). `demo_sprint_03.ps1` est un **nouvel** outil de démo (pas un
test du banc).

## Ce qu'il NE faut PAS faire

- ❌ **Écrire dans `cmd`** / forçage (D-Q2 ; le forçage in-app est **D-016**, hors périmètre).
- ❌ Coder des **adresses %MW en dur** — tout vient des `signals` du pivot.
- ❌ Rafraîchir texte/couleur à 60 Hz (scintillement) — ~5-10 Hz.
- ❌ Casser l'animation S2.2 / les builders brique 5 / le HUD santé S3.2 — **additif, chirurgical**.
- ❌ Toucher au pivot, au backend (S3.1), à `SimHost`.
- ❌ Réintroduire un « panneau mots bruts » global (redondant avec les étiquettes).

## Validation manuelle (Nico, F5 + démo)

Lancer la scène puis `demo_sprint_03.ps1` : lire **sur chaque élément** la chaîne
`cmd %MWx.y → état physique → ret %MWa.b`, et voir les **couleurs** suivre l'état. Critère de
réussite = « la chaîne de commande est claire et facilement compréhensible » (exigence de Nico).

## DÉPENDANCES

- **S3.1** (`SnapshotReturns`), **S3.2** (partage `CarrouselScene.cs` ; ordre : **après** S3.2).

## FICHIERS TOUCHÉS

- `runtime/scenes/CarrouselScene.cs` (modif : capture refs matériaux anneau/fenêtres, création +
  alimentation des labels, coloration). **Partagé avec S3.2 → S3.3 passe APRÈS S3.2.**
- `runtime/scenes/CommandChainLabels.cs` (**neuf**).
- `runtime/scripts/demo_sprint_03.ps1` (**neuf**).

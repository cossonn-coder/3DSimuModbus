# Sprint 4 — Ergonomie d'utilisation (navigation 3D + panneau des éléments + plein écran)

> **But de ce fichier** : reprendre le sprint 4 **à froid** (après `/clear`). Rédigé pendant la
> conception (2026-07-16), **archi validée** avec Nico — ce n'est plus provisoire.
> Contexte : `CLAUDE.md` (dont le nouveau **§0bis Vision long terme**) · carnet vivant : `00_etat.md` ·
> décisions : `docs/memory.md` · dettes : `docs/dettes.md` · pivot : `pivot/machine_carrousel.json`.

## Où on en est (à l'ouverture du sprint 4)

- Sprints 1 → 3 clos, **validés à l'œil** par Nico. Le démonstrateur **vit** et est **robuste** :
  `CarrouselScene` est hôte Modbus (`_PhysicsProcess` rejoue `PullCommands → Tick → PushReturns` à
  pas fixe 10 Hz), la 3D est animée depuis la sim, les échecs de bind sont bruyants (bandeau rouge,
  D-013 soldée), la chaîne de commande est tracée par élément.
- **Retour UX majeur du sprint 3 (Nico, 2026-07-16)** : les `Label3D` billboard collés aux éléments
  (S3.3, `CommandChainLabels`) rendent la scène **illisible**. Décision : **les supprimer** au profit
  d'un **panneau latéral 2D** listant tous les éléments ; **au survol** on relie tableau ↔ 3D. La
  **coloration d'état 3D reste** (c'est l'étiquetage flottant qui dégage).
- **Ce qui manque pour une démo confortable** (priorité n°1 de Nico car générique à toutes les
  simulations futures, cf. §0bis) :
  1. **Caméra figée** : `CarrouselScene.AddPresentation` pose une `Camera3D` immobile (`LookAt` fixe) —
     pas d'orbite, pas de zoom, pas de pan (**D-015**, partie navigation).
  2. **Pas de vue d'ensemble lisible** des éléments et de leur mapping %MW (le retour UX ci-dessus).
  3. **Fenêtre petite** : `project.godot` est aux défauts Godot (fenêtré ~1152×648), pas de montée en
     résolution — Nico veut **plein écran + meilleure résolution** (nouveau besoin, 2026-07-16).

## Objectif du sprint

Rendre le démonstrateur **confortable à manipuler et à lire**, de façon **générique via le pivot** :
navigation 3D libre, panneau des éléments peuplé depuis `components[]`, surbrillance croisée
tableau ↔ 3D, et présentation plein écran. **Lecture seule** (aucune écriture `cmd` — le forçage
reste D-016, sprint 5). **Aucune évolution du pivot.** **Arch A intacte.** **Boucle à pas fixe
intacte** (le heartbeat 10 Hz que le M580 surveille ne bouge pas).

## Décomposition en sous-sprints (orchestrés par `/sprint open 04`)

Couture : **présentation/caméra** (S4.1) → **panneau + décodage** (S4.2) → **picking 3D** (S4.3) →
**démo/sortie observable** (S4.4). **S4.1, S4.2 et S4.3 partagent `CarrouselScene.cs` → séquentiels
stricts.**

| Sous-sprint | Amorce | Contenu | Fichiers | Dépend de |
|---|---|---|---|---|
| **S4.1 — Présentation** | `brique_01_presentation.md` | Caméra orbitale (orbite/pan/zoom style CAO, vitesse ∝ distance, bornes, cadrage initial pivot) + **plein écran/résolution** (`project.godot`) + bascule **F11**. | `OrbitCamera.cs` (neuf), `project.godot`, `CarrouselScene.cs` (`AddPresentation`) | — |
| **S4.2 — Panneau latéral** | `brique_02_panneau.md` | Panneau 2D **ancré**, 5 colonnes, **peuplé du pivot** ; **relocalisation du décodage** pivot ; **dépose `CommandChainLabels`** ; coloration d'état **préservée** ; **survol ligne → surbrillance 3D**. | `ElementPanel.cs` (neuf), `CarrouselScene.cs`, suppr. `CommandChainLabels.cs` | S4.1 |
| **S4.3 — Picking 3D → panneau** | `brique_03_picking.md` | `Area3D` + forme de collision par élément ; **état de surbrillance partagé** symétrique (survol élément 3D → surligne sa ligne + glow). | `CarrouselScene.cs` (builders), `ElementPanel.cs` | S4.1, S4.2 |
| **S4.4 — Démo + sortie observable** | `brique_04_demo.md` | `demo_sprint_04.ps1` guidé (navigation + panneau + surbrillance), re-montre D-013 au passage. **Sortie observable.** | `runtime/scripts/demo_sprint_04.ps1` (neuf) | S4.1, S4.2, S4.3 |

Chaque sous-sprint est confié à un **sous-agent en contexte vierge** (cold-start depuis `CLAUDE.md`
+ `00_etat.md` + son amorce), enchaînés sans interruption par l'orchestrateur.

## Contrat pivot

**Aucune évolution.** Le panneau se **peuple depuis `components[]`** et **décode les adresses** via
`Signal.AbsWord/.Bit` — jamais d'adresse %MW en dur. La caméra tire son **cadrage initial** de
`kinematics` (`path.center`, `path.radius_m`), puis est libre. La règle « le pivot d'abord » est
honorée par la négative (rien à changer), et la généricité §0bis est respectée : rien de spécifique
carrousel n'est codé en dur (ni « 2 vérins », ni « 2 capteurs »).

## Décisions de design (validées avec Nico, 2026-07-16 — QCM)

- **D-Q1 — Caméra style CAO.** Orbite = **bouton milieu** maintenu + drag ; pan = **Shift+milieu** ;
  zoom = **molette** (vitesse ∝ distance : pas de reptation de près, pas de traversée de la machine).
  Bornes : pitch clampé (jamais sous le sol), distance min/max. Cadrage initial depuis le pivot,
  ensuite libre. Familier à un automaticien habitué aux visionneuses CAO.
- **D-Q2 — Surbrillance BIDIRECTIONNELLE au survol.** Survol d'une **ligne** → l'**élément 3D**
  s'éclaire ; survol d'un **élément 3D** → sa **ligne** se surligne. Symétrique. Impose un **picking
  3D** (Area3D + collision + signaux `mouse_entered/exited` par élément) et un **état de surbrillance
  partagé** : les deux sources alimentent le même état, qui pilote à la fois l'**émission** 3D et le
  **fond de la ligne**. Tout au **survol** (pas de clic requis).
- **D-Q3 — Panneau 5 colonnes.** `Repère | Type | État physique | cmd %MW=bit | ret %MW=bit`
  (fins de course S11/S12 nommées côté ret), **tri = ordre du pivot**. Reprend le contenu des
  ex-étiquettes S3.3 dans un tableau lisible.
- **D-Q4 — Affichage plein écran + résolution.** Démarrage **Maximized** (`window/size/mode=2`, barre
  de titre visible → multi-fenêtre facile pour la démo à deux terminaux) ; **F11 bascule Maximized ↔
  Fullscreen borderless** (vrai plein écran à la demande + sortie de secours). Qualité : base
  **1920×1080**, **MSAA 3D 4×** (lisse l'anneau CSG et les vérins), stretch **canvas_items / expand**.
- **D-arch — Émission ≠ albédo pour la surbrillance.** L'état pilote déjà `AlbedoColor` (~6 Hz) ; la
  surbrillance pilote le **canal émission** (événementiel) → composition propre, **aucune couleur
  d'état perdue** au retour du survol (lève le point dur « surbrillance sans casser l'état »).
- **Exclu — Vitesse de simulation réglable** (2ᵉ moitié de D-015) : reportée. Régler la vitesse ≠
  dérégler le heartbeat 10 Hz que le M580 surveille ; non nécessaire à la navigation. Reste en D-015.

## Frontière Arch A (rien de neuf)

Tout le sprint est de la **glue Godot lecture seule** sur le **thread principal** :
- Le panneau lit l'état déjà publié par `_sim` (positions, marche) et des **snapshots** du datastore
  (`SnapshotCommands` / `SnapshotReturns`, qui verrouillent en interne) — comme `HealthHud`. Aucune
  écriture `cmd`, aucun accès au datastore interne, aucune traversée de threads nouvelle.
- La caméra et le picking sont du pur input/scene-tree Godot (thread principal). Le thread serveur
  reste intouché. **Aucune modification du core** → banc **inchangé**.

## Points durs / incertitudes (traités dans les amorces)

1. **Conflit souris caméra ↔ UI** (S4.1/S4.2) : le drag d'orbite ne doit pas se déclencher quand la
   souris est sur le panneau. Résolu par lecture d'entrée caméra dans **`_UnhandledInput`** + panneau
   `Control` en **`MouseFilter = Stop`** (l'UI consomme l'event avant qu'il atteigne la caméra).
2. **Relocalisation du décodage pivot** (S4.2) : la coloration d'état (`CarrouselScene._Process`)
   dépend aujourd'hui de `_labels.Km1Aux/B1Present/B2Present`, décodés par `CommandChainLabels`. En
   supprimant ce fichier, **le décodage doit déménager dans le panneau** (nouveau hub), qui expose
   l'état décodé ; `CarrouselScene` le lit pour la coloration. **Ne pas casser la coloration.**
3. **Composition émission/albédo** (S4.3) : vérifier à l'œil que l'émission « allume » bien les
   matériaux, y compris le **matériau *glass*** des fenêtres capteurs (semi-transparent). Point de
   validation manuelle.
4. **Picking sur l'anneau CSG** (S4.3) : le convoyeur est un `CsgCombiner3D`. Plutôt que d'activer la
   collision CSG, poser un `Area3D` avec une **forme de collision approximative** (disque/cylindre
   couvrant l'empreinte de l'anneau). Approximation assumée, suffisante pour le survol.
5. **Panneau ancré, pas en pixels fixes** (S4.2) : contrairement à `HealthHud` (position/taille en
   dur), l'`ElementPanel` doit utiliser des **ancrages** (bord d'écran, hauteur relative) pour tenir
   en Maximized/plein écran et en 1080p.
6. **Testabilité headless** : caméra, picking et survol sont du pur Godot (peu testable xUnit). Le
   smoke headless vérifie au minimum que la scène se construit avec le **panneau peuplé depuis le
   pivot** (recensement des lignes = recensement des composants). Le reste = validation visuelle
   guidée par `demo_sprint_04.ps1`.

## Banc de test attendu

- **Aucune modification du core** sur tout le sprint → **`dotnet test` inchangé (référence : 95)** et
  **4 pytest full-chain** (`testbench/test_modbus_chain.py`) **inchangés/verts** à chaque sous-sprint.
- Build assembly Godot **0 erreur**. Le smoke headless de scène (recensement + panneau peuplé) reste
  vert. **Tout re-figeage du banc serait une régression** (aucune amorce n'en prévoit).

## Definition of Done (sprint)

- [ ] S4.1 : orbite/pan/zoom au clavier-souris style CAO, vitesse ∝ distance, bornes respectées ;
      fenêtre **Maximized** au lancement, **F11** bascule plein écran ; MSAA 4× / 1080p ; build 0 erreur.
- [ ] S4.2 : panneau latéral **ancré** listant **tous** les composants du pivot (5 colonnes, adresses
      décodées) ; `CommandChainLabels` **supprimé** ; **coloration d'état préservée** ; survol ligne →
      élément 3D éclairé.
- [ ] S4.3 : survol d'un élément 3D → sa ligne se surligne + glow ; surbrillance **symétrique** et
      composée avec l'état (aucune couleur d'état perdue).
- [ ] S4.4 : `demo_sprint_04.ps1` (pré-vol port 502 + scénarios guidés navigation/panneau/survol) ;
      re-montre D-013 au passage.
- [ ] Build Godot 0 erreur ; `dotnet test` (95) + 4 pytest full-chain **inchangés/verts** ; smoke
      headless (panneau peuplé) vert.
- [ ] `00_etat.md` en état de sprint ; les 4 amorces cochées ; NOTES sprint 4 à la clôture.

## Ordre de travail

1. **S4.1** (présentation) → **S4.2** (panneau + décodage) → **S4.3** (picking) → **S4.4** (démo).
2. Archi figée : `/sprint open 04` orchestre en séquentiel strict, un sous-agent cold-start par
   sous-sprint, autonome jusqu'au vert. Nico reprend au rapport final (ou sur blocage).

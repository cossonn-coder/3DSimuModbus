# 00_etat.md — Sprint 04 « Ergonomie d'utilisation » (carnet de sprint)

> Reprise a froid : `CLAUDE.md` (dont le nouveau **§0bis Vision long terme**) + ce fichier suffisent.
> Conception **figee** le 2026-07-16. Tenu a jour par chaque sous-sprint pendant l'execution.

## Ou on en est
Conception close. **S4.1 Presentation LIVRE** (2026-07-16) : `OrbitCamera.cs` (neuf, gimbal CAO :
orbite=milieu, pan=Shift+milieu, zoom=molette ∝ distance, pitch clampe [-89,-5], distance bornee
du rayon), `AddPresentation` instancie la camera orbitale (lumiere conservee), `project.godot`
(Maximized/1920x1080/stretch canvas_items+expand/MSAA 4x), F11 gere dans `OrbitCamera._UnhandledInput`.

**S4.2 Panneau LIVRE** (2026-07-16) : `ElementPanel.cs` (neuf, `CanvasLayer` + `Control` ancre
RightWide, `MouseFilter=Stop`) : 5 colonnes `Repère|Type|État|cmd %MW=bit|ret %MW=bit`, une ligne
par composant du pivot (itere `Components.Values`, ordre pivot), adresses **decodees** via
`Signal.AbsWord/.Bit`, etat physique dispatche par `Component.Type`. Le **decodage a demenage** de
`CommandChainLabels` (SUPPRIME) vers le panneau, nouveau hub : il expose `Km1Aux/B1Present/B2Present`
que `CarrouselScene._Process` lit pour la coloration d'etat **preservee**. Survol ligne → `SetHover`
(scene) allume l'**emission** du materiau d'etat (D-arch : albedo jamais perdu) + `HighlightRow` ;
`_highlightMat` (id→materiau) rempli additivement dans les builders. Delegue `CylinderPositionById`
route la position verin par id (panneau reste generique). Banc **inchange** (89+6=95 verts), build
Godot **0 erreur/0 avert.**, smoke headless `[panel] rows=5`. Reste : S4.3 Picking, S4.4 Demo.

## Objectif
Ergonomie de demonstration **generique via le pivot** (vaut pour N machines, cf. §0bis) :
navigation 3D libre, panneau lateral des elements (mapping %MW decode), surbrillance croisee
tableau <-> 3D, presentation plein ecran. **Lecture seule**, pivot inchange, Arch A intacte,
boucle a pas fixe (heartbeat 10 Hz) intacte.

## Decisions cles (QCM 2026-07-16)
- **D-Q1 Camera CAO** : orbite = bouton MILIEU ; pan = Shift+MILIEU ; zoom = molette (vitesse ~ distance) ; bornes (pas sous le sol, zoom borne) ; cadrage initial du pivot.
- **D-Q2 Surbrillance BIDIRECTIONNELLE au survol** : ligne <-> element 3D, etat partage (`SetHover`), picking 3D (Area3D par element).
- **D-Q3 Panneau 5 colonnes** : `Repere | Type | Etat | cmd %MW=bit | ret %MW=bit`, ordre du pivot.
- **D-Q4 Affichage** : Maximized au lancement (`window/size/mode=2`) + **F11** -> plein ecran ; 1920x1080, MSAA 4x, stretch canvas_items/expand.
- **D-arch Emission != albedo** : surbrillance sur le canal emission -> l'etat (albedo) n'est jamais perdu.
- **Exclu** : vitesse de simulation reglable (reste D-015).

## Carte des sous-sprints (SEQUENTIELS — tous touchent CarrouselScene.cs)
- **S4.1 Presentation** (`brique_01_presentation.md`) : `OrbitCamera.cs` (neuf) + `project.godot` (plein ecran/MSAA) + F11. Dep : —.
- **S4.2 Panneau** (`brique_02_panneau.md`) : `ElementPanel.cs` (neuf, 5 col. ancrees, peuple du pivot) + relocalisation du decodage + **depose `CommandChainLabels.cs`** + coloration preservee + survol ligne->3D. Dep : S4.1.
- **S4.3 Picking** (`brique_03_picking.md`) : Area3D par element + surbrillance symetrique (survol 3D->ligne). Dep : S4.1, S4.2.
- **S4.4 Demo** (`brique_04_demo.md`) : `demo_sprint_04.ps1` (neuf) guide, re-montre D-013. Sortie observable. Dep : S4.1-S4.3.

## Points durs (instruits dans les amorces / overview)
Conflit souris camera<->UI (`_UnhandledInput` + MouseFilter=Stop) · relocalisation du decodage pivot
sans casser la coloration · composition emission/glass a valider a l'oeil · picking anneau CSG =
forme approximative · `GetViewport().PhysicsObjectPicking=true` requis (S4.3) · panneau **ancre**
(pas de pixels fixes) · ordre pivot via `Components.Values` (pas d'accesseur core -> banc fige).

## Banc
**Inchange sur tout le sprint** : `dotnet test` = **95** + 4 pytest full-chain verts + build Godot 0
erreur. Zero modif core. Tout re-figeage = **regression**. Smoke headless : `panel_rows` = nb composants.

## REPRISE
**S4.1 + S4.2 faits**, non commites (l'orchestrateur commit). Fichiers touches S4.2 :
`runtime/scenes/ElementPanel.cs` (neuf), `runtime/scenes/CarrouselScene.cs` (`_Ready` panneau au lieu
des labels + `SetHover` + `CylinderPositionById`, `_Process` coloration via `_panel`, `_highlightMat`
additif dans les 3 builders, champs `_panel`/`_panelAccumulator`), `runtime/scenes/CommandChainLabels.cs`
+ `.cs.uid` **SUPPRIMES**, `runtime/scripts/smoke_scene.ps1` (+probe `[panel] rows=5`).
Deviation de design (imprevu) : le contrat d'amorce montrait `Update(cmd, ret, sim)` et
`Build(pivot, onRowHover)` ; livre en `Update(cmd, ret)` + `Build(pivot, onRowHover, cylinderPositionById)`
— le panneau obtient la position verin par un **delegue** (routage id→Cylinder1/2 dans la scene, ou il
est deja connu), ce qui garde le panneau generique et evite un param `sim` inutilise. Note : les champs
`_ringNode/_cyl1Node/_cyl2Node/_sensor1Node/_sensor2Node` restent (utiles S4.3) — build reste 0 avert.
Validation manuelle Nico (F5, + M580 reel ou io_scanner) restante : plus d'etiquettes 3D ; panneau
lateral droit lisible (KM1/YV1/YV2/B1/B2 + %MW) ; colonnes qui bougent quand ca anime ; survol ligne
eclaire l'element 3D puis s'eteint ; anneau/tiges/fenetres **gardent** leur coloration d'etat ; drag
demarre sur le panneau n'orbite PAS la camera. Prochaine etape : **S4.3 Picking** (`brique_03_picking.md`,
depend de S4.1+S4.2 : branche une 2e source sur `SetHover`/`HighlightRow`, via Area3D par element).
NOTES.md a rediger a la cloture.

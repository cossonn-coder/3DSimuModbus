# 00_etat.md — Sprint 04 « Ergonomie d'utilisation » (carnet de sprint)

> Reprise a froid : `CLAUDE.md` (dont le nouveau **§0bis Vision long terme**) + ce fichier suffisent.
> Conception **figee** le 2026-07-16. Tenu a jour par chaque sous-sprint pendant l'execution.

## Ou on en est
Conception close. **S4.1 Presentation LIVRE** (2026-07-16) : `OrbitCamera.cs` (neuf, gimbal CAO :
orbite=milieu, pan=Shift+milieu, zoom=molette ∝ distance, pitch clampe [-89,-5], distance bornee
du rayon), `AddPresentation` instancie la camera orbitale (lumiere conservee), `project.godot`
(Maximized/1920x1080/stretch canvas_items+expand/MSAA 4x), F11 gere dans `OrbitCamera._UnhandledInput`.
Banc **inchange** (89+6=95 verts), build Godot **0 erreur**. Reste : S4.2 Panneau, S4.3 Picking, S4.4 Demo.

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
**S4.1 fait**, non commite (l'orchestrateur commit). Fichiers touches : `runtime/scenes/OrbitCamera.cs`
(neuf), `runtime/scenes/CarrouselScene.cs` (`AddPresentation` seul), `runtime/project.godot` (sections
[display]+[rendering]). Validation manuelle Nico (F5) restante : maquette maximisee, orbite/pan/zoom OK,
pas sous le sol ni traversee, F11 bascule. Prochaine etape : **S4.2 Panneau** (`brique_02_panneau.md`,
depend de S4.1). Chaque sous-sprint coche sa DoD et met a jour la section « Ou on en est ». NOTES.md a
rediger a la cloture.

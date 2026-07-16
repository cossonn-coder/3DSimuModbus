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

**S4.3 Picking LIVRE** (2026-07-16) : surbrillance rendue **symetrique**. `_Ready` active
`GetViewport().PhysicsObjectPicking = true` (sinon les Area3D restent muettes). Helper
`AttachHoverArea(anchor, id, shape, offset)` : cree une `Area3D` (`InputRayPickable`) + `CollisionShape3D`
enfant du nœud de l'element, et branche `MouseEntered/MouseExited` sur le **`SetHover` partage** (2e source,
meme rendu que le survol de ligne S4.2 — glow emission + fond de ligne). Formes approximatives : anneau =
`CylinderShape3D` plat (empreinte, collision CSG NON activee) ; verins = cylindre englobant fut+course
(offset +Y) ; capteurs = `BoxShape3D` de la fenetre. Choix de design : le picking est pose **dans les
builders sur le nœud local** de chaque composant (generique, §0bis) plutot que via `_ringNode/_cyl1Node/...`
que l'amorce prevoyait → ces 5 champs deviennent **vestigiaux** (assignes jamais relus) → **dette D-018**
(conserves, retrait = refactor hors perimetre). Banc **inchange** (95), build Godot **0 erreur/0 avert.**,
smoke headless `[carrousel] ring=1 cylinders=2 pallets=3 sensors=2` + `[panel] rows=5`. Reste : S4.4 Demo.
> Note process : le sous-agent S4.3 a ete **interrompu par la limite de session** apres avoir ecrit le
> code et lance le banc ; l'orchestrateur a **verifie lui-meme** (build + banc + smoke, tous verts) puis
> **finalise la bookkeeping** (D-018, DoD, ce carnet) avant de commiter.

**S4.4 Demo LIVRE** (2026-07-16) : `runtime/scripts/demo_sprint_04.ps1` (neuf) sur l'ossature de
`demo_sprint_03.ps1` : `param($PyHost,$Port,[switch]$Repeat)`, resolution arborescence + interpreteur,
**pre-vol port 502** (refuse si personne n'ecoute / si SimHost tient le port -> D-013). 5 phases guidees
a la voix ecrivant `cmd` via `io_scanner_sim.py` (jamais `ret`) : (1) navigation camera S4.1 (Maximized,
orbite/pan/zoom/F11), (2) panneau S4.2 (lignes + %MW, sortie/rentree YV1), (3) rappel ressort YV1
monostable, (4) surbrillance croisee S4.2+S4.3 (survol ligne<->3D, presence B1), (5) retour repos.
Rappel D-013 en cloture (zero code). ASCII pur, **pas** de `$ErrorActionPreference='Stop'`. Verifs :
parse PowerShell OK ; pre-vol a vide -> "rien n'ecoute sur 502", exit 1, **pas de stacktrace** ; banc
**inchange** (89+6=95). Ne lance PAS Godot, ne touche NI core NI scene. Sprint 4 **complet** (reste NOTES.md
a la cloture). Validation manuelle Nico : F5 + derouler la demo (navigation/panneau/surbrillance a l'oeil).

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

## ETAT : SPRINT 4 CLOS (2026-07-16)
Les **4 sous-sprints livres, commites et pousses** ; **cloture faite** (NOTES.md redige, journal/memory/
dettes/backlog a jour). Commits : S4.1 `1912c6b` · S4.2 `37b9b7c` · S4.3 `cc8aa9e` · S4.4 `e33ed9c`
(+ ouverture `ccab5a5`, + commit de cloture). Banc **inchange sur tout le sprint** : `dotnet test` = **95**
(89 core + 6 serveur), build Godot **0 erreur/0 avert.**, smoke headless `ring=1 cylinders=2 pallets=3
sensors=2` + `panel rows=5`. **Zero modif core.** Dette nee : **D-018** (5 champs de nœuds vestigiaux).
**D-015 partie navigation soldee** (vitesse reglable reste reportee). Reste **uniquement** la validation
visuelle Nico (F5 + `demo_sprint_04.ps1`) ci-dessous.

## REPRISE
Sprint 4 clos. **Prochaine etape : validation visuelle Nico** (checklist ci-dessous), puis piste
**Phase 4 (M580 reel)** — independante — OU un nouveau sprint via `/conception` (candidats backlog :
**D-016** edition in-app / **D-017** injection de defauts, tous deux post-demonstrateur).

Validation manuelle Nico (F5, + M580 reel ou io_scanner) restante, cumulee sur S4.1-S4.3 :
- **S4.1** : maquette Maximized au lancement ; orbite (milieu) / pan (Shift+milieu) / zoom (molette ∝
  distance) souples ; jamais sous le sol ni traversee de la machine ; F11 aller-retour plein ecran.
- **S4.2** : plus d'etiquettes 3D ; panneau lateral droit lisible (KM1/YV1/YV2/B1/B2 + %MW) ; colonnes
  qui bougent quand ca anime ; survol ligne eclaire l'element 3D puis s'eteint ; anneau/tiges/fenetres
  **gardent** leur coloration d'etat ; drag demarre sur le panneau n'orbite PAS la camera.
- **S4.3** : survol d'un **element 3D** (tige de verin, anneau, fenetre capteur) → sa **ligne** se
  surligne dans le panneau **et** l'element glow ; **symetrie** avec le survol de ligne (rendu
  identique) ; composition emission/**glass** (fenetres semi-transparentes) a verifier a l'oeil ; la
  coloration d'etat n'est jamais perdue au retour du survol.

- **S4.4** : derouler `demo_sprint_04.ps1` (F5 d'abord) ; suivre le guidage des 5 phases et confirmer a
  l'oeil navigation (souris/F11), panneau (%MW), surbrillance croisee ligne<->3D pendant que ca s'anime.

Prochaine etape : **cloture du sprint** (NOTES.md a rediger, /sprint close). Les 4 sous-sprints sont
livres. Dette nee ce sprint : **D-018** (champs de nœuds vestigiaux).

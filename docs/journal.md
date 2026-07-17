# journal.md — Chronologie du projet

Règle : une entrée par sprint (ou par session de conception significative), rédigée
en fin de sprint via `/sprint`. Format : date, objectif, ce qui a été fait, ce qui a
surpris, décisions prises (reportées dans memory.md), état des tests.

---

## 2026-07-18 — Ouverture sprint 6 « Forçage de debug » (`/sprint open 06`)

**Contexte** : conception figée (`f43a766`), 3 sous-sprints séquentiels stricts (aucun fichier
partagé) : S6.1 cœur/headless → S6.2 UI/visuel → S6.3 démo/observable. Témoin de banc de départ
vérifié par l'orchestrateur : **core 109 verts** (+ serveur 10 = 119). Orchestration autonome,
un sous-agent cold-start par sous-sprint, banc re-vérifié avant chaque commit.

**Objectif** : forcer depuis l'IHM la valeur **effective** d'un signal `cmd` (KM1/YV1/YV2) pour
piloter sans PLC et malgré le PLC, **sans jamais écrire un mot du datastore** (masque à la lecture).
+ défaut `BlockerIneffective` + correction clavier AZERTY (`[`/`]` → `A`/`Z`). Clôture à suivre.

---

## 2026-07-17 — Clôture sprint 5 « Injection de défauts » (`/sprint close 05`)

**Contexte** : les 5 sous-sprints orchestrés par `/sprint open 05` sont livrés, committés et
pushés (`8f3437d`, `9c4e9a3`, `39cf7af`, `f44eb24`, `7b9f62b`). Séquence tenue
S5.1 → S5.2 → S5.3 → S5.4 → S5.5, séquentielle stricte (S5.2/S5.3/S5.4 partagent
`CarrouselScene.cs`), un sous-agent cold-start par sous-sprint, banc re-vérifié par
l'orchestrateur avant chaque commit.

**Action** : capacité complète d'**injection de défauts** depuis l'IHM, **sans jamais écrire
un mot Modbus**.
- **S5.1** cœur pur : `FaultSet`/`FaultCatalog`/`FaultCommand` (`CarrouselCore`), injection en
  tête de `Tick` (vérin coincé/ne sort pas, convoyeur patine KM1_AUX intact, capteur masqué
  **après** encodage, `RetFrozen` = ni heartbeat ni publication). Aucun mot forcé au datastore.
- **S5.2** sélection persistante clic 3D↔ligne + clavier (`]`/`[`), priorité émission
  sélection>survol>repos, **D-018 soldée**.
- **S5.3** menu « Défaut » par ligne (peuplé de `FaultCatalog`), marquage rouge (priorité
  défaut>sélection>survol), colonne libellé, **mode aveugle `B`**, 1re écriture IHM→sim.
- **S5.4** coupure comm : `Disconnect()`/`Reconnect()` sur `ModbusServer` (spike concluant :
  réarmement direct, pas de recréation) + gel `ret` ; contrôles comm toujours visibles au HUD ;
  incohérence 3D/PLC pédagogique assumée.
- **S5.5** `demo_sprint_05.ps1` (8 phases guidées), script seul.

**Surprise / point dur** : le spike FluentModbus a tranché net (réarmement sur la même
instance après `Stop()`/`Start()`, unité+buffer conservés) — consigné dans `memory.md`.

**Tests** : banc **core 89 → 109** (S5.1, re-figeage prévu) + **serveur 6 → 10** (S5.4,
re-figeage prévu) = **119 verts**, inchangé en S5.2/S5.3/S5.5. Build Godot 0 erreur, script
démo PARSE OK. **4 pytest full-chain non impactés** (défaut inactif = nominal ; skippent tant
qu'aucun runtime n'écoute sur 502). **Validation visuelle Nico restante** (F5 +
`demo_sprint_05.ps1` : marquage 3D, sélection, mode aveugle, coupure/réparation comm).

**Dettes** : **D-017 soldée** (injection de défauts livrée) ; **D-018 soldée** (S5.2). D-016
(édition in-app) préparée par la colonne « Défaut » réutilisable. Aucune dette nouvelle.

**Suite** : validation visuelle Nico ; puis campagne M580 réelle (Phase 4) — la stack de
défauts est prête à éprouver le programme automate.

---

## 2026-07-17 — Ouverture sprint 5 « Injection de défauts » (`/sprint open 05`)

**Contexte** : conception figée (5 amorces + overview + 00_etat). Arbre git remis propre avant
lancement — `runtime/project.godot` (élagage éditeur Godot des commentaires pédagogiques S4.1,
dette D-014) restauré via `git checkout` sur choix de Nico, pour ne pas mêler ce diff aux commits
du sprint.

**Action** : orchestration séquentielle stricte et autonome des 5 sous-sprints
S5.1 → S5.2 → S5.3 → S5.4 → S5.5 (S5.2/S5.3/S5.4 partagent `CarrouselScene.cs`). Un sous-agent
cold-start par sous-sprint, banc vérifié + commit par l'orchestrateur entre chaque, puis clôture
auto-enchaînée. Entrée de clôture consolidée à la fin.

---

## 2026-07-17 — Conception sprint 5 « Injection de défauts » (`/conception`)

**Objectif** : cadrer l'architecture pour forcer depuis l'IHM un défaut **physique ou de comm**
par élément, afin d'éprouver le M580 face aux défauts — **sans jamais écrire un mot Modbus**.
Nourrit la campagne M580 réelle (Phase 4) qui démarre le même jour. Solde **D-017** ; prépare **D-016**.

**Fait** : phase A (dialogue + challenge), 2 tours de QCM tranchés, découpage figé en
**5 sous-sprints bornés** (< ~150K tokens/agent, cf. consigne budget tokens de Nico). Livrables
figés : `docs/sprints/sprint_05/overview.md`, `00_etat.md`, `brique_01_faultset.md` …
`brique_05_demo.md`. **Aucun code d'implémentation** (métier de `/sprint open`).

**Décisions clés** :
- Défaut dans la **sim pure** (`FaultSet`/`FaultCatalog`/`FaultCommand` en `CarrouselCore`),
  appliqué en tête de `Tick` ; masque capteur **après encodage**, jamais au datastore (invariant D-016).
- **D-Q1** coupure comm = gel `ret` (heartbeat inclus) **+ déconnexion TCP réelle** (2 modes ;
  la déconnexion touche `ModbusServer` → sous-sprint dédié + spike FluentModbus).
- **D-Q2** capteur-bloqué 0/1 **générique sur tout bit `ret` TOR** + physique vérin/convoyeur.
- **D-Q3** marquage visible + **mode aveugle** ; **D-Q4** souris + clavier ; **D-Q5** MenuButton par
  ligne + colonne « Défaut » (réutilisé par D-016).
- **D-018** soldée au passage en S5.2 (fichier `CarrouselScene.cs` touché).

**Surprise / point dur** : réutilisabilité de `ModbusTcpServer` après `Stop()` (FluentModbus 5.3.2)
à vérifier avant de coder la déconnexion TCP (S5.4). Incohérence 3D/PLC pendant la coupure = message
pédagogique assumé.

**Tests** : aucun changement (conception). Plan de banc : xUnit re-figé en S5.1 (scénarios défaut)
puis en S5.4 (déconnexion/reconnexion serveur) ; **4 pytest full-chain inchangés** sur tout le sprint.

**Note arbre git** : `runtime/project.godot` était déjà modifié (élagage Godot des commentaires,
dette D-014) — **hors périmètre conception**, non committé ici, signalé à Nico.

**Suite** : `/clear` puis `/sprint open 05`.

## 2026-07-16 — Conception Sprint 4 : ergonomie d'utilisation (navigation + panneau + plein écran)

**Contexte** : sprint 3 clos, `demo_sprint_03.ps1` déroulé avec succès (pré-vol 502 conforme,
étiquettes %MW + couleurs OK). **Retour UX majeur de Nico** : les `Label3D` billboard collés aux
éléments (S3.3) rendent la scène **illisible** → décision de les **supprimer** au profit d'un
**panneau latéral 2D** avec surbrillance croisée tableau ↔ 3D. Priorité n°1 affichée : **l'ergonomie
d'utilisation** (générique à toutes les simulations futures, cf. nouveau **§0bis** de `CLAUDE.md`).
Nouveau besoin exprimé en séance : **plein écran + meilleure résolution**. Remote git privé
`cossonn-coder/3DSimuModbus` en cours de création (les prochains sprints pousseront).

**Objectif du sprint** : navigation 3D libre + panneau des éléments (mapping %MW décodé) + surbrillance
croisée + présentation plein écran. **Lecture seule**, pivot inchangé, Arch A intacte, **boucle à pas
fixe (heartbeat 10 Hz) intacte**. Vitesse de sim réglable **exclue** (ne pas dérégler le heartbeat ;
reste D-015).

**Décisions (QCM 2026-07-16)** : D-Q1 caméra style CAO (orbite=milieu, pan=Shift+milieu, zoom=molette,
vitesse ∝ distance) ; D-Q2 surbrillance **bidirectionnelle au survol** (état partagé, picking 3D) ;
D-Q3 panneau **5 colonnes** (Repère|Type|État|cmd %MW|ret %MW, ordre pivot) ; D-Q4 **Maximized** +
**F11** plein écran, 1920×1080 / MSAA 4× ; D-arch **émission ≠ albédo** (surbrillance sans perte de la
couleur d'état). Décomposition **4 sous-sprints séquentiels** (partagent `CarrouselScene.cs`) :
S4.1 présentation (caméra + plein écran) → S4.2 panneau + relocalisation du décodage + **dépose
`CommandChainLabels`** → S4.3 picking 3D symétrique → S4.4 démo/sortie observable.

**Banc attendu** : **inchangé sur tout le sprint** — `dotnet test` = **95**, 4 pytest full-chain verts,
build Godot 0 erreur (glue Godot pure, **zéro modif core**). Tout re-figeage serait une régression.

**Livré (conception)** : `CLAUDE.md` §0bis (vision long terme) ; `docs/sprints/sprint_04/` (overview +
4 amorces autosuffisantes + `00_etat.md`) ; ce journal ; backlog + dette mis à jour.

**Suite** : `/sprint open 04` (séquentiel strict autonome, un sous-agent cold-start par sous-sprint).

---

## 2026-07-16 — Ouverture Sprint 4 (`/sprint open 04`)

**Contexte** : conception figée, git propre, baseline banc confirmée **95 verts** (89 core + 6 serveur)
avant tout lancement. Les 4 sous-sprints partagent `CarrouselScene.cs` → **orchestration séquentielle
stricte** : S4.1 → S4.2 → S4.3 → S4.4, un sous-agent cold-start autonome par item, commit orchestrateur
entre chaque. Banc à préserver inchangé (95 + 4 pytest full-chain + build Godot 0 erreur).

**Action** : items backlog S4.1–S4.4 passés en `[~]`. (Résultat consolidé rédigé à la clôture.)

---

## 2026-07-16 — Clôture Sprint 4 : ergonomie livrée (navigation + panneau + surbrillance + plein écran)

**Contexte** : orchestration `/sprint open 04` déroulée en **séquentiel strict autonome** (4 sous-agents
cold-start, un `/clear` chacun). Objectif : rendre le démonstrateur confortable à manipuler et à lire,
**générique via le pivot**, en **lecture seule** (aucune écriture `cmd`), pivot inchangé, Arch A intacte,
boucle à pas fixe (heartbeat 10 Hz) intacte.

**Action (ce qui a été fait)** :
- **S4.1** (`1912c6b`) — `OrbitCamera.cs` (neuf) : montage gimbal `Node3D`+`Camera3D`, orbite=milieu,
  pan=Shift+milieu, zoom=molette multiplicatif (∝ distance), pitch clampé [-89°,-5°], distance bornée
  du rayon, cadrage initial tiré du pivot (`path.center/radius_m`). `project.godot` : Maximized/1080p/
  MSAA 4×/stretch. F11 = Maximized↔Fullscreen. Conflit souris caméra↔UI réglé par `_UnhandledInput`.
- **S4.2** (`37b9b7c`) — `ElementPanel.cs` (neuf) : panneau **ancré** 5 colonnes, **peuplé de
  `Components.Values`** (ordre pivot), adresses **décodées** (`Signal.AbsWord/.Bit`). **Décodage
  relocalisé** de `CommandChainLabels` (**SUPPRIMÉ**) vers le panneau, qui expose l'état lu par
  `_Process` → **coloration préservée**. Survol ligne → émission 3D. Position vérin via **délégué**.
- **S4.3** (`cc8aa9e`) — picking 3D **symétrique** : `PhysicsObjectPicking=true` + `AttachHoverArea`
  (Area3D + forme approximative par élément) branché sur le **`SetHover` partagé** (2ᵉ source, même
  rendu). Émission ≠ albédo : couleur d'état jamais perdue.
- **S4.4** (`e33ed9c`) — `demo_sprint_04.ps1` (neuf) : pré-vol 502 + 5 phases guidées (navigation/
  panneau/surbrillance) écrivant `cmd` via `io_scanner_sim.py`, rappel D-013. ASCII pur.

**Ce qui a surpris** : (1) le vrai travail de S4.2 n'était pas le panneau mais la **relocalisation du
décodage** (supprimer `CommandChainLabels` cassait la coloration si on n'y prenait garde). (2) Choix de
design S4.3 : picking posé **dans les builders sur le nœud local** (plus générique, §0bis) plutôt que via
les refs prévues par l'amorce → 5 champs de nœuds **vestigiaux** → **dette D-018**. (3) **Piège de
process** : le sous-agent S4.3 a été **coupé par la limite de session** après le code+banc mais avant sa
paperasse ; l'orchestrateur a **re-vérifié** (build/banc/smoke verts) et **finalisé la bookkeeping**
lui-même avant commit, plutôt que de risquer un re-spawn contre la même limite.

**Résultat (banc)** : **inchangé sur tout le sprint** — `dotnet test` = **95** (89 core + 6 serveur),
build Godot **0 erreur/0 avertissement**, smoke headless `ring=1 cylinders=2 pallets=3 sensors=2` +
`panel rows=5`. **Zéro modification du core.** Aucun re-figeage (aucune régression). Décisions actées :
`docs/memory.md` (ligne Sprint 4). Dette née : **D-018** ; **D-015 partie navigation soldée**.

**Suite (validations manuelles Nico, F5 + `demo_sprint_04.ps1`)** : caméra orbite/pan/zoom + F11 ;
panneau lisible + colonnes animées + coloration d'état conservée ; **surbrillance croisée symétrique**
ligne↔3D, composition émission/**glass** capteurs. Puis campagne M580 réelle (Phase 4, piste
indépendante). Détails pédagogiques : `docs/sprints/sprint_04/NOTES.md`.

---

## 2026-07-16 — Ouverture Sprint 3 : durcir le démonstrateur (robustesse + traçabilité)

**Contexte** : sprints 1 & 2 clos, démonstrateur 3D vivant validé à l'œil. Conception du sprint 3
figée le 2026-07-16 (`docs/sprints/sprint_03/`, décisions D-Q1..D-Q4 + D-arch). Deux manques pour
une démo solide devant l'automaticien : robustesse muette (bind 502 silencieux, **D-013**) et
illisibilité (rien ne trace la chaîne %MW ↔ physique à l'écran).

**Objectif** : rendre les échecs **bruyants** et la chaîne de commande **lisible par élément 3D**,
avant le M580 réel (Phase 4). **Lecture seule** (zéro écriture `cmd`, forçage → D-016), **pivot non
touché**, **Arch A intacte**.

**Orchestration** (`/sprint open 03`, séquentiel strict autonome, un sous-agent cold-start/sous-sprint) :
- **S3.1** — Backend santé (headless/xUnit) : bind visible + test qui reproduit D-013, `IsListening`,
  `LastClientWriteUtc`, `SnapshotReturns`. Disjoint.
- **S3.2** — Santé visible : bandeau bind + panneau santé ; **solde D-013**. Partage `CarrouselScene.cs`.
- **S3.3** — Chaîne par élément (étiquettes %MW + coloration d'état) + `demo_sprint_03.ps1`. Partage
  `CarrouselScene.cs` → **S3.2 avant S3.3**.

**État banc à l'ouverture** : **90 tests verts** (87 core + 3 serveur), 4 pytest full-chain verts.
S3.1 re-figera le banc (nouveaux témoins, total annoncé).

**Résultat (clôture 2026-07-16)** — sprint **livré**, 3 sous-sprints verts et commités, DoD atteinte
(hors validation visuelle Nico) :
- **S3.1** (`6ae64d5`) : `ModbusServer` durci — `ModbusServerException` (type dédié), `IsListening`,
  `LastClientWriteUtc` (`Interlocked`), `ModbusDataStore.SnapshotReturns()`. Banc **re-figé 90 → 95**
  (89 core + 6 serveur, +5 témoins santé). **Points durs tranchés empiriquement** : bind occupé =
  échec **synchrone** (`SocketException`) → pré-vol `TcpListener` + try/catch (ceinture-bretelles) ;
  `RegistersChanged` **fiable** avec `AlwaysRaiseChangedEvent=true` (fire même sur FC16 valeur
  identique, indispensable pour l'I/O Scanner) → pas de repli, pas de dette.
- **S3.2** (`30e4f2b`) : `HealthHud` (neuf) — bandeau rouge d'échec de bind (**solde D-013**) +
  panneau santé (serveur / heartbeat / activité PLC), lecture seule ~5 Hz sur le thread principal.
  `CarrouselScene` : try/catch `ModbusServerException`, garde `_serverFailed`, `_ExitTree`
  conditionnel. Banc **inchangé (95)**, build Godot 0 erreur, `smoke_anim.ps1` + 4 pytest verts.
- **S3.3** (`f1523f2`) : `CommandChainLabels` (neuf) — étiquettes 3D `cmd %MW → physique → ret %MW`
  par élément **décodées du pivot** (jamais d'adresse en dur) + coloration d'état (tige/anneau/
  fenêtres, matériaux réutilisés) rafraîchies ~6 Hz. `demo_sprint_03.ps1` (neuf). Banc **inchangé (95)**.

**Ce qui a surpris** : le bind occupé s'est révélé **synchrone** (on craignait un échec silencieux) —
tranché par une sonde jetable avant d'écrire le correctif. Et `RegistersChanged` a bien un mode
« lève même sans changement de valeur », sans quoi un PLC présent mais stable aurait paru déconnecté.

**Décisions actées** (voir `memory.md`) : `ModbusServerException` + pré-vol/catch ; activité PLC via
`RegistersChanged`/`AlwaysRaiseChangedEvent` + `LastClientWriteUtc` (Interlocked, Arch A) ;
`SnapshotReturns` ; étiquettes %MW décodées du pivot + coloration par mutation d'`AlbedoColor` ;
QCM D-Q1..D-Q4. **Dettes** : **D-013 soldée**. Consignées hors périmètre (déjà au sprint de
conception) : D-015 (nav 3D + vitesse), D-016 (édition in-app / forçage), D-017 (injection de défauts).

**Validation manuelle restante (Nico)** : F5 → lire les étiquettes %MW + voir les couleurs suivre
l'état ; occuper le port 502 pour voir le bandeau rouge ; dérouler `demo_sprint_03.ps1` ; puis M580 réel
(Phase 4). **⚠ Toujours aucun remote git** → commits locaux, push impossible.

**Suite** : Phase 4 = intégration M580 réelle. Ergonomie démo (D-015) et édition in-app (D-016) /
injection de défauts (D-017) = sprints dédiés à concevoir.

---

## 2026-07-15 — Ouverture Sprint 2 : cinématique visuelle (animer la 3D depuis la sim)

**Contexte** : sprint 1 clos (chaîne Modbus bout-en-bout + maquette 3D **statique**). Archi du sprint 2
figée à la conception (`sprint_02_cinematique_visuelle.md`, décisions D-a..D-f). Prérequis matériel
confirmé : **Godot 4.6 .NET disponible en session** → le sprint est validable end-to-end et **solde
D-010** (smoke-test brique 5 jamais lancé).

**Objectif** : faire vivre la maquette. `_PhysicsProcess` rejoue la boucle Modbus (`PullCommands →
Tick → PushReturns`, patron `SimHost`) à **pas fixe** (accumulateur, `Tick` à 10 Hz), puis
`ApplyToScene` recopie l'état sim sur les transforms (snap 10 Hz). Glue Godot pure.

**Orchestration** (`/sprint open 02`, séquentiel strict autonome, un sous-agent cold-start/sous-sprint) :
- **S2.1** — scène-hôte Modbus (boucle + serveur + garde-fous ; `ApplyToScene` = stub vide).
- **S2.2** — animation `ApplyToScene` (mapping tiges/palettes) + `smoke_anim.ps1` ; **solde D-010**.
Les deux partagent `CarrouselScene.cs` → **séquentiels** (S2.1 puis S2.2).

**État tests à l'ouverture** : core 87 verts + serveur, 4 pytest full-chain verts (contre `SimHost`).

**Résultat (clôture 2026-07-15)** — sprint **livré et validé end-to-end**, DoD atteinte :
- **S2.1** (commit `31ac4d5`) : `CarrouselScene` devenue hôte Modbus. Build **0 erreur**, **90 tests**
  (87 core + 3 serveur), **scène ≡ SimHost** (4 pytest full-chain verts contre la scène en loopback:502),
  non-régression SimHost 4/4. Écart nécessaire : `ProjectReference` → `CarrouselServer` ajouté au
  `.csproj` Godot (anticipé dans l'en-tête de `CarrouselServer.csproj`).
- **S2.2** (commit `8dc0c22`) : `ApplyToScene` animée (tiges +Y, palettes `OnCircle` + rotation).
  `smoke_anim.ps1` **vert** (rod1 Y 0,125→0,275 m ; palettes 0/120/240 → 90/50/70 = accumulation
  correcte ; heartbeat 0→211). 4 pytest toujours verts.
- **Validation visuelle** (éditeur, `demo_sprint_02.ps1`, commit `d4b2d71`) : rotation **CCW**, postes
  90°/270°, tige qui monte à l'extension et redescend au rappel ressort, accumulation derrière un vérin
  engagé — **confirmée à l'œil par Nico**. → **D-010 soldée** (headless + humain).

**Ce qui a surpris** : à la validation, la maquette restait figée alors que le heartbeat vivait.
Cause = **deux serveurs sur le port 502** (un `SimHost` reliquat des vérifs + la scène) ; le
`server.Start()` de la scène avait **échoué en silence**. D'où le pré-vol du port dans
`demo_sprint_02.ps1` et la dette **D-013** (échec de bind non signalé à l'écran).

**Décisions actées** (voir `memory.md`) : boucle à pas fixe + accumulateur ; garde-fous heartbeat
(`low_processor_mode=false` + clamp + guard) ; snap 10 Hz (D-d) ; `BindLoopback` exporté ; **script
de démo visuelle guidée à livrer à chaque sprint**.

**Dettes** : **D-010 soldée**. Nouvelles : **D-011** (duplication `StepSim`↔`SimHost`, assumée),
**D-012** (throttling fenêtre non-focus, à surveiller), **D-013** (bind 502 silencieux).

**Suite** : reste hors sprint (diff canonique Python↔C#, polish visuel, IHM debug) ; Phase 4 =
intégration M580 réelle. **⚠ Toujours aucun remote git** → 3 commits locaux, push impossible.

---

## 2026-07-15 — Clôture Sprint 1 (brique 5 livrée, chaîne Modbus + maquette statique complètes)

**Contexte** : dernière brique du sprint 1 — la scène 3D statique, première scène Godot du projet.
Amorce : `sprint_01_brique_05_scene3d.md`.

**Archi (validée avant code)** — le pivot ne livrait pas encore la géométrie de rendu (bloc `render`
convoyeur, `size_m`/`radius_m`/`center` de `kinematics`). Deux options : **(A)** étendre le loader,
**(B)** relire le JSON dans Godot. Tranché **A** (un seul parseur du pivot, le codebase refuse un
second loader). Autres décisions actées avec Nico : **vérin vertical**, repère `x=cx+r·cosθ /
z=cz−r·sinθ` (0° sur +X, CCW vu de dessus), nœuds nommés par `id` pivot (tige = enfant `rod`).

**Livré** :
- `runtime/core/PivotModel.cs` : extension **additive** — `KinematicsInfo` + `RadiusM`/`Center`/
  `PalletSizeM` ; `Component.Render` + `GetRender` (`ResolveParams` généralisé en `ResolveNumericMap`).
  `radius_m`/`size_m` requis dès que `kinematics` est présent. `ToCanonical` inchangé → **parité
  formelle Python↔C# non affectée**.
- `runtime/scenes/CarrouselScene.cs` (nouveau) : builder procédural à `_Ready` — anneau **CSG**
  (aucun primitif ne fait de couronne plate), 2 vérins (corps + tige rentrée, `rod` translatable),
  3 palettes aux positions initiales, fenêtres capteurs translucides ; caméra/lumière par `LookAt`.
- `runtime/scenes/main.tscn` + `project.godot` (`run/main_scene`) ; `DemonstrateurCarrousel.csproj`
  (`ProjectReference` → core) ; `runtime/scripts/smoke_scene.ps1` (smoke-test headless) ; NOTES §6.

**Résultat** : **core 82 → 87 verts** (+5 : géométrie rendu + render + robustesse). **Assembly Godot
compile** (0 erreur : usage API 4.6 + lien core validés). Server + SimHost non régressés. **DoD sprint 1
atteinte** hormis la validation M580 réelle (Phase 4, pas de matériel) et le smoke-test headless
lui-même (Godot absent du poste — **D-010**).

**Ce qui reste (hors sprint 1, reporté)** : diff **canonique formel** Python↔C# (backlog Phase 1) ;
cinématique **visuelle** (animer la 3D depuis la sim) = **sprint 3** ; conformité visuelle de la scène
à confirmer au 1er lancement Godot.

**Dettes nouvelles** : **D-009** (`render.kind` non consommé, convoyeur câblé « anneau » en dur, V1
mono-convoyeur — cosmétique) ; **D-010** (smoke-test headless non exécuté ici — à surveiller).

**⚠ Périmètre sprint 2 à revoir** : le backlog Phase 1bis prévoyait la 3D statique **en sprint 2** ;
elle est faite (sprint 1, brique 5). Le sprint 2 doit être **re-conçu** (candidat naturel : la
cinématique visuelle, ex-sprint 3). → objet de la **conception après `/clear`**.

**⚠ Commit** : commit local `5228326` ; **toujours pas de remote** (`git remote -v` vide) → push impossible.

---

## 2026-07-15 — Sprint 1, brique 4b : palettes (rotation, accumulation, présence B1/B2)

**Objectif** : ajouter, de façon additive, le mouvement des palettes, leur blocage/accumulation
derrière un vérin engagé, et remplir B1/B2. Lever le point dur annoncé (accumulation circulaire).

**Fait** :
- `runtime/core/PivotModel.cs` : parse additif `Kinematics` (count, positions initiales, `min_gap`,
  sens). Optionnel au `Load` (fixtures de mapping), obligatoire à l'usage (accès défensif).
- `runtime/core/PalletSet.cs` (nouveau) : modèle pur. Rotation, blocage, accumulation, présence.
- `runtime/core/CarrouselSimulation.cs` : extension `Tick` — postes bloqués (vérin engagé) →
  `pallets.Advance` (rotation pilotée par `Conveyor.IsRunning`) → écriture B1/B2 via `WriteBit`.
- Tests : `PalletSetTests` (nouveau, 14), ajouts `PivotModelTests` (6) et `CarrouselSimulationTests` (2).

**Ce qui a surpris (dans le bon sens)** : le point dur « accumulation circulaire » (incertitude
HAUTE) s'est **dissous** par la formulation, comme l'inversion mi-course du vérin en 4a. Deux idées :
(1) **espace « sens de marche »** — repère où avancer = angle croissant, réflexion involutive pour
`cw`, un seul chemin de code ; (2) **écart en `mod 360`** — efface la couture 0°/360°, rend le
blocage vérin trivial (obstacle = un angle de plus). Relaxation itérative (`count` passes) pour la
chaîne. Résultat : **aucune simplification**, donc **D-008 non créée**.

**Décisions** (reportées `memory.md`) : algo d'accumulation acté ; rotation palettes pilotée par
KM1_AUX (moteur confirmé), pas la commande brute ; vitesse = `KM1.speed_deg_per_s`.

**État des tests** : **82 core + 3 serveur** verts (`dotnet test`, les 60 core d'avant intacts).
**4 pytest full-chain restent verts** (SimHost relancé → `4 passed`). D-002/D-003 inchangées.

**Suite** : brique 5 — scène 3D Godot (`sprint_01_brique_05_scene3d.md`).

---

## 2026-07 — Phase 0 : conception et modèle pivot

**Objectif** : figer le contrat central (JSON pivot) et l'organisation du projet.

**Fait** :
- Spec fonctionnelle du carrousel validée (3 palettes, 2 postes de blocage,
  vérins monostables 500 ms, accumulation, retour de marche KM1).
- Table Modbus validée : cmd %MW100 (1 mot), ret %MW200 (2 mots, heartbeat en mot 0),
  deux lignes d'I/O Scanner FC3/FC16.
- `pivot/machine_carrousel.json` v0.2 écrit et validé.
- CLAUDE.md, commandes Claude Code et fichiers d'orchestration créés.

**Décisions** : voir memory.md (toutes reportées).

**Prochaine étape** : Sprint 1 — chaîne Modbus de bout en bout
(datastore thread-safe + serveur + banc de test Python émulant l'I/O Scanner),
avant toute 3D. Brief : `docs/sprints/sprint_01_brief.md`.

---

## 2026-07-14 — Ouverture Sprint 1 : chaîne Modbus de bout en bout

**Objectif** : prouver la chaîne Modbus bout-en-bout (client Python jouant le M580, puis
M580 réel → maquette 3D statique) avant tout pipeline d'extraction ou IHM.
Brief : `docs/sprints/sprint_01_brief.md`.

**État à l'ouverture** :
- Testbench Python amorcé : loader pivot + `test_pivot_mapping.py` — **17 verts / 4 skip**
  (les 4 skip = scénarios chaîne, en attente d'un serveur qui écoute sur :502).
  `io_scanner_sim.py` écrit, non encore validé contre un serveur réel.
- Runtime Godot : squelette seul (`.csproj`, `project.godot`), aucun code métier.
- Fichiers d'orchestration en place.

**Premier point dur** : D-001 (comportement thread-safe réel de FluentModbus) — POC à
lever **avant** de figer le pont datastore ↔ serveur.

**Ordre de travail** : POC D-001 → datastore + loader C# (tests hors Godot) → serveur +
heartbeat (validé au testbench) → boucle de simulation → scène 3D → validation M580 réel.

---

## 2026-07-14 — POC D-001 concluant (FluentModbus validé, Arch A confirmée)

**Contexte** : lever le point dur n°1 avant de figer l'API des briques C#.

**Action** : harnais jetable `runtime/poc/` (vrai `ModbusTcpServer` + horloge 100 ms)
martelé par `io_scanner_sim.py` en loopback (FC3/FC16).

**Résultat** : chaîne validée end-to-end à l'unit 1 — heartbeat propre, `cmd_run=1` →
`KM1_AUX=1` en 1 tick, aucune corruption sous scan répété. **Arch A confirmée.**
Trois contraintes FluentModbus découvertes et résolues (accès buffer synchrone ;
`AddUnit(unit_id)` ; `Get/SetBigEndian<T>` obligatoires) — détails dans
`docs/notes/NOTES_sprint_01.md`, décisions dans `memory.md`. **D-001 soldée.**
Effet de bord : testbench corrigé pour l'API pymodbus 3.14 (`device_id=`), dette **D-007**.
FluentModbus figé à **5.3.2**.

**Suite** : brique 1 (`PivotModel` C#) + scaffolding class library `core` (décision D-006).

---

## 2026-07-15 — Brique 1 close : `PivotModel` C# testé hors Godot (D-006 concrétisée)

**Contexte** : `runtime/core/PivotModel.cs` (miroir C# de `pivot_loader.py`) et la class
library `CarrouselCore` étaient écrits et compilaient, mais sans tests — or c'est
précisément le test hors moteur qui justifie D-006.

**Action** : projet xUnit `runtime/tests/` (`ProjectReference` → `CarrouselCore`,
aucune dépendance Godot ; chemin `tests/` déjà réservé par le `Compile Remove` de
`DemonstrateurCarrousel.csproj`, donc l'assembly Godot ne compile pas ces fichiers). `PivotModelTests.cs` reprend **cas pour cas** la suite pytest
`test_pivot_mapping.py` : mêmes adresses %MW/bit attendues sur le **même pivot réel**
(zones, heartbeat mot 0, KM1_AUX %MW201.6, S11..S22, B1/B2, bases surchargeables) +
robustesse (JSON invalide, bit/word hors borne, conflit, heartbeat absent).

**Résultat** : **17 verts / 0 skip** (`dotnet test`), symétrie exacte avec les 17 verts
Python. Les deux loaders résolvent des adresses identiques sur le pivot réel → parité
pratique Python/C# établie. `.gitignore` runtime couvre bien `bin/`+`obj/` (vérifié en
dry-run : seuls les fichiers source seraient suivis).

**Reste** : le diff **canonique formel** Python↔C# (évoqué dans l'en-tête de
`PivotModel.cs`, `ToCanonical()` côté C#) n'est pas encore outillé — l'émetteur canonique
manque côté Python. Consigné au backlog Phase 1. La parité par assertions communes suffit
pour l'instant (simplicité d'abord).

**Suite** : brique 2 — `ModbusDataStore` (objet C# pur, `ushort[]` cmd/ret + verrou),
même projet `core`, tests dans `runtime/tests`. Puis brique 3 (serveur FluentModbus branché,
Arch A) validée au testbench Python.

**Convention actée ce jour** : chaque brique reçoit une **amorce autosuffisante** dans
`docs/sprints/` (reprise à froid après `/clear`). Amorce brique 2 rédigée :
`docs/sprints/sprint_01_brique_02_datastore.md` (contrat d'API, 5 décisions pré-tranchées,
3 questions ouvertes, DoD).

---

## 2026-07-15 — Brique 2 close : `ModbusDataStore` (source de vérité d'Arch A)

**Contexte** : après le loader (brique 1), la pièce centrale d'Arch A — le tampon des mots
d'échange entre thread serveur et thread physique. Amorce : `sprint_01_brique_02_datastore.md`.

**Archi validée avant code** : contrat de l'amorce confirmé, 3 questions ouvertes tranchées
selon les recommandations — snapshot `ushort[]` **brut** (pas de struct décodé), pont serveur
en **`Span<ushort>`** (zéro alloc, aligné sur `GetHoldingRegisters`), **pas** d'accès direct
au heartbeat (la sim reconstruit tout le `ret` puis publie).

**Fait** : `runtime/core/ModbusDataStore.cs` — objet C# pur (`ushort[]` cmd/ret + verrou),
zéro dépendance Godot. **Transport de mots bruts** : aucun décodage bit, aucun heartbeat,
aucune adresse absolue (tailles tirées de `PivotModel.GetZone(...).SizeWords`). API :
`SnapshotCommands` (copie défensive), `PublishReturns` (remplacement atomique + défensif),
`WriteCommandsFromWire`/`CopyReturnsToWire` (pont serveur, spans dimensionnés à la zone,
longueur vérifiée). `runtime/tests/ModbusDataStoreTests.cs` (11 cas).

**Résultat** : **28 verts / 0 échec** (`dotnet test`). Deux propriétés fines verrouillées
par test : snapshot = *copie* (pas la référence interne) et publish = *recopie du contenu*
(pas la référence fournie) — évitent deux fuites d'abstraction classiques. Tous les points
de design justifiés pédagogiquement dans `NOTES_sprint_01.md §2`.

**Décisions** : pas de nouvelle entrée `memory.md` (Arch A et le pattern datastore y étaient
déjà actés le 2026-07-14) ; les choix de design de la brique sont dans les NOTES et l'amorce
cochée. Aucune dette nouvelle.

**Suite** : **brique 3** — serveur FluentModbus branché sur ce datastore (pull `cmd` début de
tick / push `ret` fin de tick, sous `server.Lock`), validé au testbench Python (io_scanner_sim).

---

## 2026-07-15 — Brique 3 close : `ModbusServer` (pont FluentModbus ↔ datastore)

**Contexte** : dernière pièce du transport Modbus. Amorce : `sprint_01_brique_03_serveur.md`.

**Archi validée avant code** — deux questions structurantes tranchées :
- **Assembly (Q4)** : `ModbusServer` va dans un **projet dédié** `runtime/server/CarrouselServer.csproj`
  (classlib → `CarrouselCore` + FluentModbus 5.3.2). `CarrouselCore` reste **pur** (D-006) ;
  option A (FluentModbus dans le core) et B (dans l'assembly Godot, non testable) écartées.
- **Validation (Q2)** : test d'intégration **in-process** (reco 2a) — vrai serveur sur loopback
  + vrai `ModbusTcpClient` FluentModbus dans le même `dotnet test`. La full-chain Python (4 skips)
  reste pour la brique 4.

**Fait** :
- `PivotModel` expose `Port`/`UnitId`, **parse strict** (échec clair si absent) — décision **D-f**
  demandée par l'utilisateur (pas de repli 502/1 : le pivot est le contrat, ces valeurs réseau ne
  se devinent pas). Les 2 fixtures de test minimales reçoivent `port`/`unit_id` explicites +
  2 tests de robustesse (champ absent).
- `runtime/server/ModbusServer.cs` : les 3 contraintes POC ré-imposées (`AddUnit(unit_id)`, accès
  buffer **synchrone**, `Get/SetBigEndian<ushort>` **registre par registre**). Port/unit_id/bases
  résolus du pivot. Serveur **passif** : `PullCommands`/`PushReturns` séparés, appelés par le thread
  appelant sous `server.Lock`. Bind défaut `IPAddress.Any` (M580 distant).
- `runtime/server.tests/ModbusServerTests.cs` (3 cas : transport FC16→pull, publish→push→FC3,
  endianness big-endian explicite via client little/big). Port **éphémère libre** en test (pas 502).
- `DemonstrateurCarrousel.csproj` : `server/` et `server.tests/` retirés du glob SDK Godot.

**Résultat** : **34 verts / 0 échec** (`dotnet test` : 31 core + 3 intégration serveur).
Endianness big-endian du fil prouvée in-process (client little-endian lit `0x3412` là où le
serveur a écrit `0x1234`). Points de design justifiés dans `NOTES_sprint_01.md §3`.

**Décisions** : `memory.md` inchangé (Arch A + contraintes FluentModbus déjà actées le
2026-07-14) ; **D-f** (parse strict port/unit_id) consignée aux NOTES §3 et à l'amorce cochée.
Aucune dette nouvelle.

**Suite** : **brique 4** — boucle de simulation (cinématique scriptée déterministe + heartbeat),
qui remplira le `ret` et débloquera la validation full-chain FC3/FC16 du testbench Python.
Amorce : `sprint_01_brique_04_simulation.md`.

---

## 2026-07-15 — Sprint 1, brique 4a : boucle de simulation (vérins + convoyeur + heartbeat)

**Archi (validée avant code)** : re-découpage brique 4 → **4a** (heartbeat + vérins + KM1_AUX,
porte les 4 pytest full-chain) et **4b** (palettes/accumulation/présence). Les deux sous-amorces
rédigées d'avance (convention 2026-07-15). Reco archi suivies en bloc par Nico.

**Livré** :
- `runtime/core/PivotModel.cs` : `Signal.WriteBit` (symétrique de `ReadBit`), `Component.Params`
  + `GetParam` (sac générique `double`, additif D-d), `HeartbeatPeriodMs` (cadence tick, défaut 100).
  **Le pivot JSON n'a pas changé** — tous les params y étaient depuis la Phase 0.
- `runtime/core/CylinderState.cs` : vérin monostable 0→1, vitesse constante, inversion mi-course
  gérée par le clamp (aucune branche dédiée). Seuils S11/S12 + IsEngaged (pour 4b).
- `runtime/core/ConveyorState.cs` : recopie retardée KM1_AUX (suiveur temporisé symétrique).
- `runtime/core/CarrouselSimulation.cs` : composition root. `Tick` = snapshot cmd → advance →
  heartbeat (rollover ushort) → reconstruction complète de `ret` (D-e) → publish. B1/B2 à 0 (4b).
- `runtime/simhost/` (nouveau projet console) : hôte headless Pull→Tick→Push cadencé, écoute 502.
  Débloque pytest sans Godot ; patron du futur `_PhysicsProcess`.
- Tests : `CylinderStateTests`, `ConveyorStateTests`, `CarrouselSimulationTests` + ajouts params/
  WriteBit/cadence dans `PivotModelTests`.
- `DemonstrateurCarrousel.csproj` : `simhost/` retiré du glob SDK Godot (sinon double entry point).

**Résultat** : **63 verts / 0 échec** (60 core + 3 intégration serveur ; +29 vs 34, originaux intacts).
**4 scénarios pytest full-chain PASSENT** (`SimHost` en écoute, `pytest test_modbus_chain.py -v`
→ `4 passed`). Heartbeat, KM1_AUX (recopie après délai), YV1/YV2 (sortie + rappel ressort) validés
bout-en-bout FC3/FC16.

**Décisions/dettes** : `memory.md` amendé (params + cadence tirés du pivot, découpage 4a/4b).
Aucune dette nouvelle en 4a. D-008 (simplification accumulation) reste candidate pour 4b.
Points de design → `NOTES_sprint_01.md §4`. Amorces 4a rédigée+cochée, 4b prête.

**Suite** : **brique 4b** — palettes, accumulation `min_gap_deg`, présence B1/B2.
Amorce : `sprint_01_brique_04b_palettes.md`.

**⚠ Commit** : pas de remote configuré (`git remote -v` vide) — commit local uniquement, push impossible.

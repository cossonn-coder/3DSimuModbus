# Amorce de conception — Sprint 04 « Ergonomie d'utilisation »

> **Usage** : lancer `/conception docs/sprints/sprint_04/amorce_conception.md` (ce fichier
> est le $ARGUMENTS). Il fournit l'intention, les retours de validation du sprint 3, la
> priorisation des dettes arbitrée avec Nico, le périmètre proposé et les points durs
> anticipés. La phase A du skill part d'ici ; rien n'est figé tant que Nico n'a pas tranché
> les QCM.

---

## 1. Vision long terme du projet — À INSCRIRE DANS CLAUDE.md (phase B, action n°1)

Le bloc suivant doit être ajouté à `CLAUDE.md` (nouveau §0bis, juste après le §0
« Contexte projet ») pour que **chaque session et chaque sous-agent** l'ait en tête.
C'est une évolution documentaire, pas du code — elle se fait en phase B sur accord.

```markdown
## 0bis. Vision long terme (au-delà du démonstrateur)

Le carrousel n'est PAS le produit : c'est le véhicule de validation. La cible finale est
un outil qui, à partir d'un modèle 3D, d'un fichier DWG, d'un schéma électrique PDF et de
tout autre document nécessaire, GÉNÈRE automatiquement une simulation 3D connectée en
Modbus à un automate réel (pipeline Python → pivot JSON → runtime Godot). Le pivot JSON
est le pont : aujourd'hui écrit à la main, demain produit par le pipeline d'extraction
(Phases 2-3) et/ou par un éditeur in-app (Phase 5).

Conséquence pratique pour CHAQUE conception et CHAQUE sprint : toute fonctionnalité doit
être pensée « générique via le pivot » et non « spécifique au carrousel ». Avant de figer
un choix, se demander : « ce mécanisme survivra-t-il quand la machine sera générée depuis
un DWG et un schéma que je n'ai jamais vus ? ». La navigation 3D, le HUD, le forçage, le
catalogue d'éléments, l'injection de défauts : tout cela s'applique à N machines futures,
pas à une. Le carrousel sert à prouver ; la généricité par le pivot sert à durer.
```

## 2. Retours de validation du sprint 3 (Nico, 2026-07-16) — à reporter au journal

- ✅ `demo_sprint_03.ps1` déroulé avec succès.
- ✅ Pré-vol du port 502 vérifié en conditions réelles : script lancé avant F5 →
  « DEMO KO : rien n'écoute sur le port 502 » en rouge + consigne. Comportement conforme.
- ✅ F5 : étiquettes %MW lisibles et couleurs d'état fonctionnelles, MAIS…
- ⚠ **RETOUR UX MAJEUR (rejet partiel de S3.3)** : les étiquettes 3D collées aux éléments
  rendent la scène **illisible**. Décision de Nico : **supprimer les `Label3D` billboard**
  au profit d'un **panneau latéral** (UI 2D) listant tous les éléments ; au **survol d'une
  ligne du tableau, l'élément 3D correspondant entre en surbrillance**. La coloration
  d'état 3D (tige/anneau/fenêtres) est conservée — c'est l'étiquetage flottant qui dégage.
- ℹ Bandeau rouge D-013 : validé par tests (S3.1 xUnit + S3.2) mais pas revu à l'œil par
  Nico (le scénario « SimHost d'abord, F5 ensuite » n'a pas été rejoué). Non bloquant ;
  peut être re-montré au passage pendant ce sprint.
- ℹ Remote git : repo privé `cossonn-coder/3DSimuModbus` en cours de création — les
  prochains sprints poussent (`git push`) ; le collègue automaticien clone pour la Phase 4.

## 3. Priorisation des dettes (arbitrée avec Nico, 2026-07-16)

Axe directeur de Nico : **l'ergonomie d'utilisation est la priorité n°1**, car elle
s'appliquera à toutes les simulations futures (cf. §1), pas seulement au carrousel.

| Priorité | Sujet | Dettes | Sprint |
|---|---|---|---|
| **1 (ce sprint)** | Navigation 3D libre (orbite/pan/zoom à tout niveau) + panneau latéral éléments avec survol→surbrillance (retour S3.3) | D-015 (partie navigation) + retour UX §2 | **Sprint 4** |
| 2 | Commandes depuis le HUD → Modbus (« forçage ») | D-016 (partie forçage/écriture) | Sprint 5 (conception dédiée — point dur §5.1) |
| 3 | Catalogue d'éléments + ajout à la simulation + branchement automate (mapping produit depuis l'UI) | D-016 (complet) + réveille D-005 (JSON Schema) | Sprint 6 |
| 4 | Injection de défauts | D-017 | Après le forçage (mécanique commune) |
| Parallèle | **Campagne M580 réel (Phase 4)** — indépendante de tout ça, la chaîne est prête ; le collègue clone et déroule | — | Dès que le repo est cloné |
| Assumées | D-002, D-003, D-009, D-011, D-014 (cosmétiques) ; D-007 (épingler pymodbus) ; D-012 (surveiller) ; D-004 (2 lignes validées par l'usage) | — | Conditions de remboursement inchangées |

La **vitesse de simulation réglable** (2ᵉ moitié de D-015) est **exclue du sprint 4** :
elle porte un piège propre (ne pas dérégler le heartbeat 10 Hz que le M580 surveille —
découpler facteur visuel et pas de tick) et n'est pas nécessaire à la navigation. Elle
rejoint le sprint 5 ou un sprint ultérieur — à confirmer en phase A.

## 4. Périmètre proposé — Sprint 04 « Ergonomie d'utilisation »

### 4.1 Dans le périmètre
1. **Caméra orbitale** : rotation autour d'un point d'intérêt (drag souris), pan
   (translation du point d'intérêt), zoom molette **fonctionnel à tout niveau de zoom**
   (pas de zoom qui « traverse » la machine ni de vitesse constante ridicule de près —
   vitesse de pan/zoom proportionnelle à la distance). Bornes raisonnables (pas de caméra
   sous le sol). Générique : ne suppose RIEN du carrousel (centre/rayon initiaux tirés du
   pivot pour le cadrage de départ, ensuite libre).
2. **Panneau latéral des éléments** (UI Godot `Control`, lecture seule ce sprint) :
   une ligne par composant du pivot — tag (KM1, YV1…), état physique décodé, adresses
   `cmd %MW`/`ret %MW` (décodées du pivot via `Signal.AbsWord/.Bit`, jamais en dur).
   Reprend le CONTENU des étiquettes S3.3, dans un tableau.
3. **Survol → surbrillance** : hover d'une ligne du panneau ⇒ l'élément 3D correspondant
   est mis en évidence (ex. boost d'émission sur les matériaux déjà capturés au build) ;
   sortie du survol ⇒ retour à l'état normal. Le routage réutilise le nommage des nœuds
   par `id` pivot (acté brique 5).
4. **Dépose des `Label3D`** de S3.3 (`CommandChainLabels`) — remplacés par le panneau.
   La **coloration d'état 3D reste** (tige ambre, anneau vert, fenêtres capteurs).
5. Re-montrer au passage le bandeau D-013 à l'œil (SimHost puis F5) — validation
   opportuniste, zéro code.

### 4.2 Hors périmètre (NE PAS FAIRE)
- Aucune écriture vers la zone `cmd` (forçage = sprint 5, D-016). Panneau **lecture seule**.
- Pas de vitesse de simulation réglable (cf. §3).
- Pas de sélection/édition d'éléments, pas de catalogue (sprint 6).
- Pivot JSON inchangé ; Arch A intacte ; boucle à pas fixe intacte ; banc pytest full-chain
  inchangé.

### 4.3 Sortie observable (règle permanente du skill)
Scène animée avec navigation libre + panneau vivant, et `demo_sprint_04.ps1` : le script
anime la machine (io_scanner_sim) pendant que la voix du script guide Nico (« orbite autour
du poste 90°, zoome sur la tige YV1, survole la ligne S12 du panneau et regarde la
surbrillance »).

## 5. Points durs / incertitudes anticipés (à instruire en phase A)

1. **[Pour mémoire sprint 5, PAS ce sprint]** Forçage HUD vs I/O Scanner : le M580 réécrit
   TOUTE la zone `cmd` à chaque scan (50-100 ms) → un forçage HUD côté `cmd` serait écrasé
   au scan suivant. Pistes à instruire le moment venu : forcer côté `ret`/capteurs (recoupe
   D-017), mode « PLC absent » où le HUD joue l'automate, masque de forçage persistant
   appliqué après chaque pull. À NE PAS trancher au sprint 4 — consigné ici pour que la
   conception du panneau n'hypothèque pas ces options (ex. prévoir que le panneau puisse
   un jour porter des contrôles par ligne).
2. **Picking/hover inverse** : ce sprint fait tableau→3D (survol de ligne). Le sens
   3D→tableau (cliquer un élément dans la scène) est-il attendu aussi ? Coût modéré
   (raycast + zones de clic), à trancher en QCM.
3. **Surbrillance sans casser la coloration d'état** : l'émission/albedo est déjà mutée
   par l'état (S3.3). La surbrillance doit se composer avec (ex. canal émission distinct)
   sans « perdre » la couleur d'état au retour du survol. Les refs matériaux sont
   capturées au build — vérifier que la composition tient.
4. **Conflit souris caméra ↔ UI** : le drag d'orbite ne doit pas se déclencher quand la
   souris est sur le panneau (gestion `MouseFilter`/focus Godot). Classique mais à ne pas
   oublier dans la DoD.
5. **Testabilité headless** : la caméra et le hover sont du pur Godot (peu testable
   xUnit). Le smoke headless doit au minimum vérifier que la scène se construit avec le
   panneau peuplé depuis le pivot (recensement des lignes = recensement des composants).
   Le reste = validation visuelle guidée par `demo_sprint_04.ps1`. À cadrer dans les
   amorces (banc `dotnet test` + 4 pytest full-chain attendus INCHANGÉS).

## 6. Questions pressenties pour les QCM de phase A (à reformuler par /conception)

- Q1 — Contrôles caméra : schéma de souris (orbite au drag gauche ou droit ? pan au bouton
  du milieu ? touches clavier en complément ?) — reco à formuler avec les conventions des
  visionneuses 3D usuelles (type visionneuse CAO, familières à un automaticien).
- Q2 — Hover 3D→tableau (sens inverse) : inclus ce sprint ou reporté ?
- Q3 — Contenu exact des colonnes du panneau (tag / état / cmd / ret / … quoi d'autre ?)
  et tri (ordre du pivot ? par type ?).

## 7. Rappels d'invariants (CLAUDE.md) applicables ici

- Le scene tree n'est touché que par le thread principal ; le panneau LIT l'état exposé
  (comme `HealthHud`, ~5 Hz suffisent) — aucune nouvelle traversée de threads.
- Aucune adresse Modbus en dur : tout vient du pivot (`Signal.AbsWord/.Bit`).
- Générique via le pivot (cf. §1) : le panneau se peuple depuis `components[]`, la caméra
  se cadre depuis `kinematics` — rien de spécifique carrousel codé en dur.
- Commentaires pédagogiques en français (hors dette) ; NOTES du sprint ; amorces
  autosuffisantes avec DÉPENDANCES et FICHIERS TOUCHÉS (le panneau et la caméra touchent
  tous deux `CarrouselScene.cs` → probablement séquentiels, à confirmer au découpage).

# Amorce S4.3 — Picking 3D → panneau (surbrillance bidirectionnelle symétrique)

> **Cold-start** : lire `CLAUDE.md` (dont §0bis) + `docs/sprints/sprint_04/00_etat.md` + cette amorce.
> Sous-sprint **3/4**. **Dépend de : S4.1 + S4.2**. **Séquentiel** (fichier partagé `CarrouselScene.cs`).

## Objectif

Compléter la surbrillance dans le **sens inverse** : **survoler un élément 3D** dans la scène
**surligne sa ligne** dans le panneau **et** l'éclaire (glow), exactement comme le survol d'une ligne.
La surbrillance devient **bidirectionnelle et symétrique** : les deux sources (ligne, élément 3D)
alimentent le **même état** via le `SetHover` déjà en place (S4.2).

## Contexte code (déjà en place après S4.2)

- `CarrouselScene.SetHover(string id, bool on)` : **source unique** de la surbrillance — allume/éteint
  l'émission de `_highlightMat[id]` et appelle `_panel.HighlightRow(id, on)`. **On y branche une 2ᵉ
  source ici** (le picking 3D), sans rien changer d'autre.
- `_highlightMat` (id → matériau d'état) construit en S4.2.
- Nœuds d'ancrage capturés au build : `_ringNode` (CsgCombiner3D), `_cyl1Node`/`_cyl2Node` (Node3D
  parent body+rod), `_sensor1Node`/`_sensor2Node` (MeshInstance3D boîte). **Conservés** exprès pour ce
  sous-sprint.
- Caméra : `OrbitCamera` (S4.1) fournit un `Camera3D { Current = true }` — nécessaire au picking.

## Contrat d'API visé

Dans les **builders** de `CarrouselScene`, ajouter **de façon additive** un `Area3D` de picking par
élément, enfant du nœud de l'élément, avec une `CollisionShape3D` approximative, et connecter ses
signaux au `SetHover` :

```csharp
// Helper interne (glue Godot) : pose une zone de survol sur un élément et la relie à SetHover(id).
private void AttachHoverArea(Node3D anchor, string id, Shape3D shape);
//   -> crée Area3D { InputRayPickable = true } + CollisionShape3D { Shape = shape }
//   -> area.MouseEntered += () => SetHover(id, true);
//      area.MouseExited  += () => SetHover(id, false);
```

Appelé une fois par élément (convoyeur, vérins, capteurs) dans/juste après leur builder, avec une
forme adaptée (cf. ci-dessous). **Aucune nouvelle API publique** ; tout est privé à la scène.

## Formes de collision (approximations assumées)

- **Vérins** (`_cyl1Node/_cyl2Node`) : `CylinderShape3D` (ou `BoxShape3D`) englobant le fût + la tige.
- **Capteurs** (`_sensor1Node/_sensor2Node`) : `BoxShape3D` ~ la boîte de la fenêtre.
- **Anneau convoyeur** (`_ringNode`, CSG) : **ne pas** activer la collision CSG. Poser un
  `CylinderShape3D` **plat** couvrant l'empreinte de l'anneau (rayon ≈ outer, faible hauteur). Le
  survol se déclenche alors sur tout le disque de l'anneau — approximation **suffisante** pour relier
  KM1 au survol. Commenter l'approximation.

## Décisions pré-tranchées (ne pas ré-arbitrer)

- **D-Q2 — au survol, symétrique** : pas de clic. `mouse_entered`/`mouse_exited` → `SetHover(id, …)`.
- **Réutiliser `SetHover`** : ne pas dupliquer la logique de surbrillance. Les deux sources convergent
  vers la même méthode → glow 3D **et** fond de ligne pilotés à l'identique, quelle que soit la source.
- **Émission** (déjà actée S4.2) : le glow reste sur le **canal émission**, composé avec l'état albédo.

## Points durs / pièges (déjà instruits)

- **Physics object picking désactivé par défaut** : en Godot 4, un `Area3D` n'émet `mouse_entered`/
  `mouse_exited` que si le **picking physique du viewport** est actif. Activer
  `GetViewport().PhysicsObjectPicking = true` (dans `_Ready`) — sinon aucun survol 3D ne se déclenche
  et le sous-sprint semble « ne rien faire ». **Piège n°1, à traiter en premier.**
- **Panneau au-dessus de la 3D** : quand la souris est sur le panneau (Control `MouseFilter=Stop`),
  l'event ne descend pas au picking 3D → pas de double déclenchement. Cohérent avec S4.1/S4.2.
- **Double source / même id** : le pointeur est unique ; en pratique un seul survol actif à la fois.
  Un booléen par id suffit. Si un chevauchement transitoire apparaît (sortie 3D / entrée ligne au même
  instant), un léger clignotement est acceptable — **ne pas** sur-concevoir un compteur de références
  sauf si un vrai défaut visuel est observé.
- **Composition émission/glass** : re-vérifier que l'émission « allume » aussi le matériau *glass* des
  capteurs (semi-transparent). Point de validation manuelle.

## Ce qu'il NE faut PAS faire

- **Aucune écriture `cmd`** ; **aucun** clic-action (sélection/édition = sprint 6).
- **Ne pas** activer la collision CSG de l'anneau (forme approximative dédiée à la place).
- **Ne pas** toucher le core, la boucle Modbus, le heartbeat, la caméra (S4.1), le décodage panneau
  (S4.2) au-delà du branchement `SetHover`.
- **Ne pas** réintroduire de `Label3D`.

## Definition of Done (cochable)

- [x] `GetViewport().PhysicsObjectPicking = true` activé (dans `_Ready`).
- [x] Un `Area3D` + forme de collision approximative par élément (convoyeur/vérins/capteurs), relié à
      `SetHover(id, …)` via le helper `AttachHoverArea` (posé dans les builders sur le nœud local).
- [~] Survol d'un **élément 3D** → sa **ligne se surligne** dans le panneau **et** l'élément s'éclaire ;
      sortie de survol → retour à l'état normal **sans perdre** la couleur d'état. *(code en place ; rendu à confirmer F5)*
- [~] Symétrie vérifiée : survol ligne (S4.2) et survol 3D produisent le **même** rendu.
      *(même `SetHover` partagé → symétrie par construction ; à confirmer à l'œil F5)*
- [x] Build Godot **0 erreur** ; banc **inchangé** : `dotnet test` = **95** ; smoke headless `rows=5`.
      *(S4.3 ne touche pas la full-chain → pytest non requis)*

## Vérif autosuffisante

1. `dotnet test` → **95 passed** (zéro core).
2. Build Godot **0 erreur**.
3. `pytest testbench/test_modbus_chain.py` (hôte à l'écoute) → 4 verts — **inchangé**.
4. **Validation manuelle (Nico, F5)** : passer la souris sur la tige d'un vérin, sur l'anneau, sur une
   fenêtre capteur → la **ligne correspondante se surligne** dans le panneau **et** l'élément glow ;
   inversement, survoler la ligne éclaire l'élément (S4.2) — comportement **identique** dans les deux
   sens ; la coloration d'état n'est jamais perdue.

## Banc attendu

`dotnet test` **inchangé (95)** + 4 pytest full-chain **inchangés/verts**. Glue Godot pure, zéro core.

## Fichiers touchés

- `runtime/scenes/CarrouselScene.cs` — `_Ready` (`PhysicsObjectPicking`), builders (Area3D par
  élément via `AttachHoverArea`).
- `runtime/scenes/ElementPanel.cs` — au besoin, ajustement mineur de `HighlightRow` (rendu du surlignage
  de ligne) si non finalisé en S4.2. **Pas** de nouvelle API.

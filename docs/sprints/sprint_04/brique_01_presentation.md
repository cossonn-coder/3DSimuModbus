# Amorce S4.1 — Présentation : caméra orbitale + plein écran / résolution

> **Cold-start** : lire `CLAUDE.md` (dont §0bis) + `docs/sprints/sprint_04/00_etat.md` + cette amorce
> suffit. Sous-sprint **1/4** du sprint 4. **Dépend de : —** (premier). **Séquentiel** avec S4.2/S4.3
> (fichier partagé `CarrouselScene.cs`).

## Objectif

Remplacer la caméra **figée** de la maquette par une **caméra orbitale libre** (style visionneuse
CAO) et présenter la scène **en grand** (fenêtre maximisée + montée en résolution + anti-aliasing),
avec une bascule **F11** vers le vrai plein écran. Générique : **rien de spécifique carrousel** — le
cadrage initial se déduit du pivot, ensuite la caméra est libre.

## Contexte code (déjà en place)

- `runtime/scenes/CarrouselScene.cs` : `AddPresentation(Vector3 center, float radius)` (~ligne 381)
  crée aujourd'hui une `Camera3D { Current = true }` **immobile** via `LookAtFromPosition(...)`, plus
  une `DirectionalLight3D`. C'est ce **corps caméra** qu'on remplace ; **la lumière reste**.
- `center` et `radius` viennent déjà du pivot (`kinematics.path.center`, `kinematics.path.radius_m`),
  passés à `AddPresentation` dans `_Ready`. Les réutiliser pour le **cadrage initial**.
- `runtime/project.godot` : minimal (défauts Godot 4.6, pas de section `[display]` ni `[rendering]`).
  Note : `run/low_processor_mode=false` est le défaut (cf. D-014) — **ne pas y toucher**.

## Contrat d'API visé

**Nouveau fichier `runtime/scenes/OrbitCamera.cs`** — un `Camera3D` (ou un `Node3D` gimbal portant un
`Camera3D` enfant) autonome, piloté par la souris/clavier, sans aucune dépendance au pivot au-delà du
cadrage initial reçu à l'installation :

```csharp
public partial class OrbitCamera : Node3D   // gimbal au point d'intérêt ; Camera3D enfant à distance
{
    // Cadrage initial : point d'intérêt = centre pivot, distance déduite du rayon. Ensuite libre.
    public void FrameFrom(Vector3 center, float radius);
    // (input géré en interne dans _UnhandledInput ; aucune méthode par-frame exposée)
}
```

Modèle **gimbal** : ce `Node3D` est au **point d'intérêt** (yaw/pitch = sa rotation), la `Camera3D`
est un **enfant reculé** de `distance` sur son axe local −Z. Alors :
- **Orbite** = tourner le gimbal (yaw sur Y, pitch sur X).
- **Pan** = translater le point d'intérêt (le gimbal) dans le plan caméra.
- **Zoom** = changer `distance` (position locale de la caméra enfant).

`CarrouselScene.AddPresentation` : retirer la `Camera3D` fixe, instancier `OrbitCamera`, l'`AddChild`,
appeler `FrameFrom(center, radius)`. **Garder** la `DirectionalLight3D` telle quelle.

## Décisions pré-tranchées (QCM 2026-07-16 — ne pas ré-arbitrer)

- **D-Q1 — Schéma souris style CAO** :
  - **Orbite** = **bouton du milieu** maintenu + déplacement souris (yaw/pitch).
  - **Pan** = **Shift + bouton du milieu** + déplacement souris.
  - **Zoom** = **molette** (`WheelUp`/`WheelDown`).
- **Vitesse ∝ distance** : pan et zoom se déplacent proportionnellement à la distance courante
  (de près = petits pas, de loin = grands pas). **Pas de reptation** de près, **pas de traversée** de
  la machine (le zoom réduit la distance mais ne franchit pas le point d'intérêt : clamp `distance ≥
  distanceMin`).
- **Bornes** : `pitch` clampé (ex. −89°..−5°, la caméra **plonge** sur la maquette, **jamais sous le
  sol**) ; `distance ∈ [distanceMin, distanceMax]` déduits du `radius` (ex. `min ≈ radius*0.3`,
  `max ≈ radius*8`). Valeurs cosmétiques (mobilier hors pivot), à commenter comme telles.
- **Cadrage initial depuis le pivot** : point d'intérêt = `center`, distance initiale ≈ `radius*2.5`,
  pitch initial ≈ −35° (plongée douce). Générique : aucune coordonnée carrousel en dur.
- **D-Q4 — Affichage** (dans `project.godot`) :
  - `[display] window/size/mode=2` (**Maximized**, barre de titre visible).
  - `[display] window/size/viewport_width=1920`, `viewport_height=1080` (base de conception).
  - `[display] window/stretch/mode="canvas_items"`, `window/stretch/aspect="expand"`.
  - `[rendering] anti_aliasing/quality/msaa_3d=2` (**MSAA 4×** ; l'enum Godot : 0=off,1=2×,2=4×,3=8×).
- **F11 = bascule Maximized ↔ Fullscreen borderless** : géré dans `OrbitCamera._UnhandledInput`
  (ou un petit handler dédié) via `DisplayServer.WindowSetMode(...)` en alternant
  `WindowMode.Maximized` et `WindowMode.Fullscreen`.

## Points durs / pièges (déjà instruits)

- **Conflit souris caméra ↔ UI** : lire l'entrée dans **`_UnhandledInput`** (pas `_Input`). Ainsi,
  quand le panneau latéral (S4.2) consommera l'event (Control `MouseFilter=Stop`), la caméra ne
  déclenchera pas d'orbite. **À ce sous-sprint le panneau n'existe pas encore** : vérifier seulement
  que la caméra répond ; la non-interférence sera revalidée en S4.2.
- **Éditeur vs .tscn** : `main.tscn` peut contenir une caméra ? Non — la caméra est créée **par code**
  dans `AddPresentation` (aucune caméra dans `main.tscn`). Rester sur la création par code.
- **`viewport_width/height` + `mode=2`** : la fenêtre s'ouvre maximisée à la résolution de l'écran ;
  la base 1920×1080 sert au `stretch canvas_items` pour l'échelle des UI (utile en S4.2).

## Ce qu'il NE faut PAS faire

- **Pas de moteur physique** ni de collision pour la caméra (pur transform scripté).
- **Pas de vitesse de simulation réglable** (hors périmètre, reste D-015).
- **Ne pas toucher** au core, au datastore, à la boucle `_PhysicsProcess`, au heartbeat, à la
  `DirectionalLight3D`, ni à `run/low_processor_mode`.
- **Pas de sélection/picking d'éléments** ici (c'est S4.3).
- Pas de dépendance de la caméra au pivot au-delà du `FrameFrom(center, radius)` initial.

## Definition of Done (cochable)

- [ ] `OrbitCamera.cs` créé ; `AddPresentation` instancie la caméra orbitale (lumière conservée).
- [ ] Orbite (milieu), pan (Shift+milieu), zoom (molette) fonctionnels ; vitesse pan/zoom ∝ distance.
- [ ] Bornes respectées : caméra jamais sous le sol (pitch clampé), zoom borné (pas de traversée).
- [ ] `project.godot` : Maximized au lancement, base 1920×1080, MSAA 4×, stretch canvas_items/expand.
- [ ] **F11** bascule Maximized ↔ plein écran.
- [ ] Build assembly Godot **0 erreur**.
- [ ] Banc **inchangé** : `dotnet test` = **95**, 4 pytest full-chain verts, smoke scène vert.

## Vérif autosuffisante (prouver le vert sans contexte externe)

1. `dotnet test` à la racine `runtime/` → **95 passed** (aucune modif core attendue).
2. Build Godot headless (ou éditeur) → **0 erreur** de compilation.
3. `pytest testbench/test_modbus_chain.py` (avec un hôte à l'écoute, cf. `demo_*`/`SimHost`) → 4 verts
   — **inchangé** (la caméra ne touche pas la chaîne Modbus).
4. **Validation manuelle (Nico, F5)** : la maquette s'ouvre **maximisée** ; bouton milieu = orbite,
   Shift+milieu = pan, molette = zoom (souple de près comme de loin, sans passer sous le sol ni
   traverser la machine) ; **F11** passe en plein écran et revient.

## Banc attendu

`dotnet test` **inchangé (95)** + 4 pytest full-chain **inchangés/verts** (glue Godot pure, zéro core).
Tout re-figeage serait une **régression**.

## Fichiers touchés

- `runtime/scenes/OrbitCamera.cs` — **créé**.
- `runtime/scenes/CarrouselScene.cs` — `AddPresentation` (remplace la caméra fixe ; lumière conservée).
- `runtime/project.godot` — sections `[display]` + `[rendering]`.

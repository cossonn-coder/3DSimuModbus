# Sprint 1 · Brique 4b — Palettes : accumulation + présence B1/B2

> **But de ce fichier** : reprendre la brique **4b** à froid. Issue du re-découpage de la brique 4
> (2026-07-15). 4b = la partie **incertaine** (modèle d'accumulation circulaire), isolée du jalon
> de déblocage pytest (porté par 4a). Contexte : `CLAUDE.md` · décisions : `docs/memory.md` ·
> pivot : `pivot/machine_carrousel.json`.
> Entrée précédente : brique **4a** (`sprint_01_brique_04a_verins_convoyeur.md`) — vérins + convoyeur
> + heartbeat livrés, `CarrouselSimulation.Tick` encode déjà S/KM1_AUX/heartbeat, **B1/B2 à 0**.

## Où on en est (attendu à l'ouverture de 4b)

- 4a livrée : `CarrouselSimulation` tourne, 4 pytest full-chain verts, `SimHost` opérationnel.
- `CylinderState` expose déjà `IsEngaged` (pos > `block_threshold`) — **consommé ici** pour le blocage.
- `ConveyorState.IsRunning` déjà là — **consommé ici** pour faire tourner les palettes.
- Reste à faire : parser la cinématique palettes, modéliser leur avancement/accumulation, remplir B1/B2.

## Objectif de 4b

Ajouter le mouvement des palettes et la présence, de façon **additive** dans `CarrouselSimulation.Tick` :
- palettes tournent avec le convoyeur (`speed_deg_per_s`, sens `ccw`) tant que `KM1` tourne ;
- une palette est **bloquée** si un vérin est **engagé** (`IsEngaged`) à son poste (90°/270°) ;
- **accumulation** : une palette s'arrête derrière une palette arrêtée, écart mini `min_gap_deg` (20°) ;
- **B1/B2** vrais si une palette est dans `± window_deg/2` (fenêtre 8°) autour du poste.

## Ce que 4b modélise (depuis le pivot, jamais en dur)

- **Palettes** : `kinematics.pallets.{count=3, initial_positions_deg=[0,120,240], min_gap_deg=20}`,
  sens `kinematics.path.direction=ccw`.
- **Présence B1/B2** : `sensor.params.{station_angle_deg, window_deg}`.

## Contrat d'API (à re-valider à l'archi de 4b)

```csharp
// PivotModel.cs — additif (2e vague)
public KinematicsInfo PivotModel.Kinematics { get; }   // { int PalletCount; double[] InitialPositionsDeg; double MinGapDeg; string Direction; }

// Modèle pur (CarrouselCore)
public sealed class PalletSet {
    PalletSet(double[] initialAnglesDeg, double minGapDeg, bool ccw);
    void Advance(double dt, bool conveyorRunning, double speedDegPerS, IReadOnlyList<double> blockedAnglesDeg);
    IReadOnlyList<double> AnglesDeg { get; }
    bool PresentAt(double stationAngleDeg, double windowDeg);
}
```
`CarrouselSimulation.Tick` (extension additive) : après l'advance des vérins/convoyeur, calcule les
postes bloqués (vérin engagé) → `pallets.Advance(...)` → écrit B1/B2 via `Signal.WriteBit`.

## Point dur n°1 — accumulation circulaire (incertitude HAUTE)

**Reco de départ (Q2)** : positions triées + contrainte « ne pas dépasser (palette devant − min_gap) ».
**À PROTOTYPER AVANT DE FIGER** avec un test dédié « 3 palettes s'accumulent derrière YV1 sorti » :
1. **Couture 0°/360°** : tri angulaire sur un cercle — la « palette devant » peut être de l'autre côté de 0.
2. **3 palettes** : accumulation en chaîne — résolution **itérative** (une palette repousse la suivante)
   vs une seule passe. Prototyper les deux, garder la plus simple qui respecte la spec.
3. **Blocage vérin + accumulation simultanés** : un vérin engagé agit comme une « palette virtuelle
   arrêtée » au poste ; les vraies palettes s'accumulent derrière.
4. **Sens ccw** : l'ordre « devant/derrière » dépend du sens de rotation.

**Si simplification nécessaire** (ex. ignorer la couture, ou blocage strictement pairwise) →
**créer la dette D-008** dans `docs/dettes.md` et la documenter dans `NOTES §4`. Rappel dette
existante **D-002** (collision latérale non simulée) et **D-003** (pas de rampe, arrêt instantané).

## Definition of Done (4b)

- [ ] `PivotModel.Kinematics` parsé (additif, tests verts).
- [ ] `PalletSet` : rotation ccw à `speed_deg_per_s`, blocage si vérin engagé au poste.
- [ ] **Accumulation** `min_gap_deg` prototypée + test « 3 palettes derrière YV1 sorti » vert.
- [ ] B1/B2 sur `window_deg` (test : palette entre/sort de la fenêtre).
- [ ] `CarrouselSimulation.Tick` remplit B1/B2 ; les 34+ verts et 4 pytest full-chain **restent verts**.
- [ ] Dette D-008 créée si simplification. Points de design dans `NOTES §4`. Brief coché.

## Ordre de travail

1. **Archi avant code** : re-valider le contrat, arbitrer le modèle d'accumulation sur prototype.
2. `PivotModel.Kinematics` (additif) → tests verts.
3. `PalletSet` + **prototype accumulation** (test d'abord) → xUnit vert.
4. Extension `CarrouselSimulation.Tick` (B1/B2) → xUnit vert, non-régression pytest.
5. `NOTES §4` + orchestration. Brique suivante : **brique 5** (scène 3D, `sprint_01_brique_05_scene3d.md`).

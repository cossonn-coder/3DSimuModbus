# Sprint 1 · Brique 4 — Boucle de simulation (cinématique scriptée + heartbeat)

> **But de ce fichier** : reprendre la brique 4 **à froid**. Contrat visé, décisions pré-tranchées,
> questions ouvertes. Rédigé **pendant la conception** du sprint (convention 2026-07-15) : plus
> provisoire que la brique 3 — **à re-valider finement à l'étape archi**, quand son tour vient.
> Contexte : `CLAUDE.md` · décisions : `docs/memory.md` · pivot : `pivot/machine_carrousel.json`
> · datastore : `runtime/core/ModbusDataStore.cs` · serveur : brique 3.

## Où on en est (attendu à l'ouverture de la brique 4)

- Briques 1-3 livrées : loader + datastore + serveur FluentModbus branché, transport FC3/FC16
  validé (transport nu, sans cinématique réelle : le serveur pousse ce que la sim lui donne).
- Brique 4 = **le cerveau** : elle transforme les commandes en retours physiquement plausibles.
  C'est elle qui **débloque les 4 scénarios pytest en skip** (chaîne réelle bout-en-bout).

## Objectif de la brique

Un module de simulation **C# pur** (testable hors Godot) qui, **à chaque tick physique** :
1. `cmd = store.SnapshotCommands()` (snapshot début de tick) ;
2. fait avancer la **cinématique scriptée déterministe** de `dt` (pas de moteur physique) ;
3. **incrémente le heartbeat** ;
4. reconstruit tout le `ushort[] ret` et `store.PublishReturns(ret)` (publication fin de tick).

Le câblage temporel (qui appelle ce tick, et le pull/push du serveur autour) est fait par un nœud
Godot `_PhysicsProcess` (partie non-pure, mince). **La logique cinématique, elle, ne dépend pas de Godot.**

## Ce que la brique doit modéliser (depuis le pivot, jamais en dur)

Les **params machine** (`speed_deg_per_s`, `travel_time_ms`, seuils, `feedback_delay_ms`,
`station_angle_deg`, `window_deg`, `min_gap_deg`…) ne sont **pas encore parsés** par `PivotModel`
(cf. commentaire dans `PivotModel.cs` : « ajoutés quand une brique en aura besoin »). **La brique 4
étend le loader** pour les lire.

- **Vérins bloqueurs (`YV1`/`YV2`, monostables)** : position `0→1` interpolée linéairement sur
  `travel_time_ms`. `cmd_extend=1` → sort ; `cmd_extend=0` → rentre par **rappel ressort**.
  Inversion en cours de course : **repart du point courant** (pas de saut). Capteurs à seuils :
  `ret_retracted` (S11/S21) vrai si pos `< retracted_threshold` (0.02) ; `ret_extended` (S12/S22)
  vrai si pos `> extended_threshold` (0.98). « Engagé » (bloque une palette) si pos `> block_threshold` (0.10).
- **Convoyeur (`KM1`)** : rotation continue des palettes à `speed_deg_per_s` (20°/s) tant que
  `cmd_run=1`. `KM1_AUX` (`ret_running`) = **recopie de `cmd_run` après `feedback_delay_ms`** (50 ms,
  temps de fermeture du contacteur). Sens `ccw`.
- **Palettes (3, positions angulaires)** : avancent avec le convoyeur ; **bloquées** si un vérin est
  engagé à leur poste (90°/270°). **Accumulation** : une palette s'arrête derrière une palette arrêtée,
  écart mini `min_gap_deg` (20°). Accumulation par **écart angulaire minimal** (cf. CLAUDE.md).
- **Présence (`B1`/`B2`)** : vrai si une palette est dans `± window_deg/2` autour du poste (fenêtre 8°).
- **Heartbeat** : mot 0 de `ret`, `+1` par tick (à cadence 100 ms → ~10/s), **rollover 16 bits libre**.

## Contrat d'API proposé (très provisoire — à retravailler à l'archi de la brique)

```csharp
public sealed class CarrouselSimulation
{
    public CarrouselSimulation(PivotModel pivot);      // lit les params machine du pivot
    public void Tick(ModbusDataStore store, double dtSeconds);   // snapshot -> cinématique -> heartbeat -> publish
    // Accès lecture seule à l'état interne (positions vérins/palettes) pour la 3D (brique 5) et les tests.
}
```
Piste : découper l'état interne en petits modèles purs (`CylinderState`, `ConveyorState`,
`PalletSet`) chacun avec sa règle d'avancement — chacun **testable isolément** en pytest-like xUnit
(pas de Godot, `dt` injecté). Le heartbeat et l'encodage bit→`ret` restent dans `Tick`.

## Décisions de design pré-tranchées (provisoires)

- **D-a — `dt` injecté, pas d'horloge interne.** La sim est une **fonction pure du temps** : `Tick(store, dt)`.
  *Raison* : déterminisme total → tests reproductibles (on avance de `dt` fixes) et découplage de Godot.
- **D-b — L'encodage bit↔signal passe par les `Signal` du pivot** (`pivot.GetSignal(...).ReadBit/…`),
  jamais d'adresse ni de masque en dur. La sim lit `cmd[WordRel]` et écrit dans `ret[WordRel]` via les offsets résolus.
- **D-c — Cinématique = petits modèles purs composés**, pas un gros bloc. *Raison* : chaque règle
  (interpolation vérin, accumulation palettes) se teste seule ; « simplicité d'abord » à l'échelle du composant.
- **D-d — La brique 4 étend `PivotModel` pour lire les params** (au lieu d'un second parseur).
  *Raison* : un seul point de vérité pour le pivot. À faire de façon **additive** (ne pas casser les 28 verts).
- **D-e — Reconstruction complète de `ret` à chaque tick** puis `PublishReturns` (cohérence intra-scan,
  cf. datastore D-e). Le heartbeat est écrit dans ce même `ret`.

## Questions ouvertes (à trancher à l'archi de la brique)

1. **Encodage bit** : helper générique « écrire tel bit du signal S dans `ret[]` » à ajouter (au
   `Signal` ou à un petit `RetEncoder`) ? Le `Signal` a `ReadBit` mais pas `WriteBit`. **Reco : ajouter
   `Signal.WriteBit(ref ushort word, bool v)` (symétrique de `ReadBit`), additif.**
2. **Modèle d'accumulation des palettes** : calcul par tri angulaire + contrainte d'écart mini vs
   automate à états par palette ? **Reco : positions triées + « ne pas dépasser (palette devant − min_gap) »**,
   le plus simple qui respecte la spec. À prototyper avec un test dédié « 3 palettes s'accumulent derrière un vérin sorti ».
3. **Cette brique est-elle trop grosse pour un seul tronçon ?** Candidat au **re-découpage** en 4a
   (vérins + heartbeat, valide les scénarios S11..S22 + KM1_AUX) et 4b (palettes/accumulation/présence).
   **Reco : décider à l'archi** ; si oui, écrire **les deux sous-amorces** avant de coder (convention 2026-07-15).
4. **Source de `dt` réelle** : `_PhysicsProcess(delta)` de Godot fournit `delta`. Cadence cible 100 ms
   (heartbeat) — `Engine.PhysicsTicksPerSecond` à régler côté brique 5/câblage. **Hors périmètre logique pure.**

## Definition of Done (brique 4) — provisoire

- [ ] Params machine lus depuis le pivot (extension additive de `PivotModel`, 28 verts intacts).
- [ ] `CarrouselSimulation.Tick` : snapshot cmd → cinématique `dt` → heartbeat → publish ret.
- [ ] Vérins : `cmd_extend` → S12 après `travel_time_ms`, `=0` → S11 (rappel ressort) ; inversion mi-course propre.
- [ ] Convoyeur : `KM1_AUX` recopie `cmd_run` après `feedback_delay_ms`.
- [ ] Palettes : accumulation `min_gap_deg`, blocage si vérin engagé au poste ; `B1`/`B2` sur `window_deg`.
- [ ] Heartbeat ~10/s, rollover propre à 65535.
- [ ] Tests xUnit purs (`dt` injecté) verts **et** les 4 scénarios `testbench` full-chain FC3/FC16 débloqués.
- [ ] Points de design justifiés dans `NOTES_sprint_01.md §4`. Orchestration à jour ; brief coché.

## Ordre de travail

1. **Archi avant code** : re-valider ce contrat, trancher le re-découpage (Q3). Si re-découpage →
   rédiger les sous-amorces 4a/4b **d'abord**.
2. Étendre `PivotModel` (params) → tests additifs verts.
3. Générer les modèles purs + `CarrouselSimulation` (fichier par fichier) → tests xUnit verts.
4. Débloquer/valider les scénarios `testbench` full-chain.
5. Orchestration + `NOTES §4`. Brique suivante : **brique 5** (scène 3D statique),
   amorce `sprint_01_brique_05_scene3d.md`.

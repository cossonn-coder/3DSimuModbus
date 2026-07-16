# S3.2 — Santé visible : bandeau d'échec de bind + panneau santé (solde D-013)

> **Amorce autosuffisante** (cold-start). Tu lis **seulement** : `CLAUDE.md` +
> `docs/sprints/sprint_03/00_etat.md` + ce fichier. Sous-sprint **visuel** (glue Godot pure, non
> testable en xUnit — frontière D-006). **Dépend de S3.1** (API santé déjà livrée).

## Objectif

Rendre **visible à l'écran** la santé du démonstrateur, pour qu'un souci ne se traduise plus jamais
par une « maquette figée sans explication » (dette **D-013**, vécue en démo sprint 2) :
1. **Bandeau d'échec de bind** : si le serveur Modbus ne peut pas écouter (port 502 occupé),
   afficher un **message rouge explicite** — et **solder D-013**.
2. **Panneau santé** discret : heartbeat qui bat, état du serveur, **activité PLC** (dernière trame
   `cmd` reçue).

**Lecture seule.** Ce sous-sprint ne modifie **ni** la boucle Modbus, **ni** l'animation existante.

## Contexte code (source de vérité)

- `runtime/scenes/CarrouselScene.cs` : `_Ready()` charge le pivot, construit la géométrie, puis crée
  `_store`/`_sim`/`_server` et appelle **`_server.Start()` nu** (ligne ~163). `_PhysicsProcess`
  rejoue `StepSim` à pas fixe puis `ApplyToScene`. `_ExitTree` dispose le serveur.
- **Fourni par S3.1** (déjà livré) : `ModbusServer.IsListening`, `ModbusServer.LastClientWriteUtc`,
  `ModbusServerException`, `ModbusDataStore.SnapshotReturns()`.
- `_sim.Heartbeat` (ushort) est déjà exposé et lu par `ApplyToScene`.

## Contrat d'API visé

### `CarrouselScene._Ready` (modif chirurgicale)
- Entourer `_server.Start()` d'un `try { … } catch (ModbusServerException ex) { … }`.
  - Sur succès : comportement inchangé.
  - Sur échec : **ne pas planter la scène** ; mémoriser `_serverFailed = true` + le message ;
    **journaliser** (`GD.PrintErr`) ; **ne pas** entrer dans la boucle Modbus (garde en tête de
    `_PhysicsProcess` : si `_serverFailed`, on ne fait pas `StepSim` — inutile, le serveur n'écoute
    pas — mais on continue d'afficher le bandeau). L'animation 3D peut rester figée : c'est **voulu
    et désormais expliqué** par le bandeau.

### `HealthHud` (runtime/scenes/HealthHud.cs — **neuf**)
- `partial class HealthHud : CanvasLayer` (overlay 2D par-dessus la 3D).
- **Bandeau d'erreur** : un `Label`/`Panel` rouge en haut, **visible seulement** si le serveur a
  échoué ; texte = le message de `ModbusServerException`.
- **Panneau santé** (coin d'écran, discret) : `Label`(s) rafraîchis à **basse cadence** (~5 Hz, via
  un accumulateur sur `_Process(delta)` — **pas** 60 Hz, éviter le scintillement) :
  - `serveur : à l'écoute :502` **ou** `serveur : ÉCHEC (port occupé)` ;
  - `heartbeat : <n>` (doit défiler si la sim tourne) ;
  - `PLC : dernière trame il y a <X> ms` **ou** `PLC : aucune trame reçue` (à partir de
    `LastClientWriteUtc`). Si S3.1 n'a pas pu fiabiliser l'activité, afficher `PLC : n/d` — **ne pas
    mentir** (D-Q3).
- **Toggle** : `[Export] bool ShowHud { get; set; } = true` (le panneau santé ; le bandeau d'erreur
  s'affiche **quoi qu'il arrive** — une panne ne doit pas pouvoir être masquée).
- **Alimentation** : `HealthHud` reçoit des références lecture seule (le `ModbusServer` et le `_sim`,
  ou plus simplement `CarrouselScene` lui **pousse** les valeurs chaque rafraîchissement). Choisis le
  couplage le plus simple ; **aucune** lecture du datastore ici (santé = serveur + heartbeat).

## Décisions pré-tranchées

- **Instanciation** : `CarrouselScene._Ready` crée le `HealthHud` par code et l'`AddChild` (même
  patron que `AddPresentation` pour la caméra/lumière). Pas de `.tscn` séparé à éditer (SSH mobile).
- **Thread** : `HealthHud._Process` tourne sur le **thread principal Godot** → lit `_sim.Heartbeat`
  et `_server.IsListening/LastClientWriteUtc` sans verrou spécifique (ces propriétés sont thread-safe
  côté S3.1). **Frontière Arch A intacte.**
- **Le bandeau prime sur le rendu** : mieux vaut une maquette figée **avec** bandeau « serveur KO »
  qu'une maquette qui bouge en trompant sur son état. Solde D-013 par la **visibilité**.
- **Cadence** : rafraîchir le texte ~5 Hz suffit (lisibilité humaine), indépendamment du 60 Hz moteur.

## Definition of Done (cochable)

- [ ] Port 502 **occupé** au lancement → **bandeau rouge visible** + message clair + `GD.PrintErr`.
      La maquette n'est plus « figée sans raison ». → **D-013 SOLDÉE.**
- [ ] Port **libre** → pas de bandeau ; panneau santé affiche `serveur : à l'écoute`, un **heartbeat
      qui défile**, et l'activité PLC (ou `n/d` si non fiabilisée en S3.1).
- [ ] `[Export] bool ShowHud` masque/affiche le **panneau santé** (le bandeau d'erreur reste).
- [ ] Build assembly Godot **0 erreur**.
- [ ] `smoke_anim.ps1` **vert** et les **4 pytest full-chain** **verts** (comportement Modbus inchangé).
- [ ] Aucune écriture dans `cmd` ; boucle `_PhysicsProcess`/`StepSim` inchangée (hors la garde
      `_serverFailed`).

## Vérif autosuffisante (prouver le vert)

```
# 1) Cas nominal : lancer la scène (F5 ou --headless) SANS rien sur 502
#    -> panneau santé visible, heartbeat défile, pas de bandeau.
# 2) Cas D-013 : occuper 502 d'abord (lancer runtime/simhost), PUIS lancer la scène
#    -> bandeau rouge "serveur : ÉCHEC (port occupé)" visible. C'est la preuve du solde D-013.
# 3) Non-régression :
dotnet build   # assembly Godot 0 erreur
pwsh runtime/scripts/smoke_anim.ps1     # vert
pytest testbench/test_modbus_chain.py -v # 4 passed (SimHost à l'écoute)
```

## Banc attendu

**Inchangé** : `dotnet test` (le HUD n'est pas testé en xUnit), `smoke_anim.ps1`, 4 pytest — tout
reste vert. Aucune régression : la glue est lecture seule.

## Ce qu'il NE faut PAS faire

- ❌ Écrire dans `cmd` (aucun forçage — D-Q2 ; le forçage est D-016, hors périmètre).
- ❌ Afficher les **mots bruts** / la chaîne par élément ici — c'est **S3.3** (ne pas empiéter).
- ❌ Rafraîchir le texte à 60 Hz (scintillement) — basse cadence ~5 Hz.
- ❌ Prétendre « M580 connecté » si S3.1 n'a pas fiabilisé l'activité → afficher `n/d`.
- ❌ Toucher aux builders de géométrie (brique 5) ni à l'animation (S2.2) — modif chirurgicale.
- ❌ Toucher au pivot, au backend S3.1, ou à `SimHost`.

## Validation manuelle (Nico, F5)

- Lancer **sans** occupant sur 502 → panneau santé, heartbeat vivant.
- Lancer **avec** un `SimHost` déjà sur 502 → **bandeau d'échec bien visible** (le scénario qui avait
  piégé la démo sprint 2 est maintenant explicite).

## DÉPENDANCES

- **S3.1** (backend santé) : `IsListening`, `LastClientWriteUtc`, `ModbusServerException`.

## FICHIERS TOUCHÉS

- `runtime/scenes/CarrouselScene.cs` (modif : try/catch autour de `Start`, garde `_serverFailed`,
  création du `HealthHud`). **Partagé avec S3.3 → S3.2 passe AVANT S3.3.**
- `runtime/scenes/HealthHud.cs` (**neuf**).

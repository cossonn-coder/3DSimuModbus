# Sprint 3 — Durcir le démonstrateur (robustesse + traçabilité de la chaîne de commande)

> **But de ce fichier** : reprendre le sprint 3 **à froid** (après `/clear`). Rédigé pendant la
> conception (2026-07-16), **archi validée** avec Nico — ce n'est plus provisoire.
> Contexte : `CLAUDE.md` · carnet vivant : `00_etat.md` · décisions : `docs/memory.md` ·
> dettes : `docs/dettes.md` · pivot : `pivot/machine_carrousel.json`.

## Où on en est (à l'ouverture du sprint 3)

- Sprints 1 & 2 clos, **validés à l'œil** par Nico. Le démonstrateur **vit** : `CarrouselScene`
  est hôte Modbus (`_PhysicsProcess` rejoue `PullCommands → Tick → PushReturns` à pas fixe 10 Hz),
  la 3D est animée depuis la sim (tiges +Y, palettes sur le cercle), heartbeat à 10 Hz.
- **Ce qui manque pour une démo solide devant l'automaticien** :
  1. **Robustesse muette** : `CarrouselScene._Ready` fait `_server.Start()` **sans garde-fou**
     (ligne ~163). Si le port 502 est déjà pris (ex. un `SimHost` reliquat), la maquette se
     construit mais ne dialogue pas — **aucun message**, elle semble figée (**D-013**, vécu en
     démo sprint 2).
  2. **Illisibilité** : rien à l'écran n'explique la **chaîne de commande** (quel bit Modbus
     pilote quoi, quel retour en résulte). L'automaticien ne « voit » pas le lien %MW ↔ physique.

## Objectif du sprint

Rendre le démonstrateur **robuste** (les échecs deviennent bruyants) et **lisible** (la chaîne
`commande Modbus → état physique → retour Modbus` est tracée **sur chaque élément 3D**), avant la
confrontation au **M580 réel** (Phase 4). **Lecture seule** : le HUD n'écrit jamais dans `cmd`
(le forçage est reporté, cf. D-016). **Aucune évolution du pivot.** **Arch A intacte.**

## Décomposition en sous-sprints (orchestrés par `/sprint open 03`)

Couture nette **backend testable (headless) / présentation (visuel)**. S3.2 & S3.3 partagent
`CarrouselScene.cs` → **séquentiels**. S3.1 est disjoint et **testable seul** (xUnit).

| Sous-sprint | Amorce | Contenu | Fichiers | Dépend de |
|---|---|---|---|---|
| **S3.1 — Backend santé** | `brique_01_backend_sante.md` | `ModbusServer` : échec de bind **visible** (exception typée + test qui **reproduit** D-013), `IsListening`, `LastClientWriteUtc` (activité PLC) ; `ModbusDataStore.SnapshotReturns()`. **Zéro Godot.** | `ModbusServer.cs`, `ModbusDataStore.cs`, `server.tests/`, `tests/` | — |
| **S3.2 — Santé visible** | `brique_02_sante_visible.md` | Bandeau d'échec de bind (**solde D-013**) + panneau santé (heartbeat, état serveur, activité PLC). | `CarrouselScene.cs`, `HealthHud.cs` (neuf) | S3.1 |
| **S3.3 — Chaîne de commande par élément + démo** | `brique_03_chaine_commande.md` | Étiquettes 3D ancrées `cmd[%MW] → physique → ret[%MW]` (convoyeur/vérins/capteurs) **+ coloration d'état** + `demo_sprint_03.ps1`. **Sortie observable.** | `CarrouselScene.cs`, `CommandChainLabels.cs` (neuf), `demo_sprint_03.ps1` | S3.1, S3.2 |

Chaque sous-sprint est confié à un **sous-agent en contexte vierge** (cold-start depuis
`CLAUDE.md` + `00_etat.md` + son amorce), enchaînés sans interruption par l'orchestrateur.

## Contrat pivot

**Aucune évolution.** Tout ce dont le HUD a besoin est déjà dans `machine_carrousel.json` depuis
la Phase 0 : les `signals` de chaque composant (`{zone, word, bit}` + `tag` KM1/YV1/S11…), les
`params` physiques, les `render`/`kinematics`. Le HUD **décode via ces `signals`** — jamais
d'adresse %MW en dur. La règle « le pivot d'abord » est honorée par la négative.

## Décisions de design (validées avec Nico, 2026-07-16 — QCM)

- **D-Q1 — Cœur = HUD lecture seule + bind visible.** Maximum de valeur démontrable, risque
  maîtrisé, Arch A intacte, pivot non touché.
- **D-Q2 — Lecture seule en V1 ; forçage reporté** (mode « sans-PLC » exclusif → **D-016**). Zéro
  écrivain concurrent sur `cmd`, invariant « l'app sert, ne décide pas » sauf.
- **D-Q3 — Voyant santé dérivé de NOUS.** Vérifier `RegistersChanged` (FluentModbus) tôt ; repli
  garanti = heartbeat (vivant) + « dernière écriture `cmd` reçue il y a X ms ». Jamais promettre
  plus que le certain (un voyant « M580 connecté » fiable n'est pas garanti par la lib).
- **D-Q4 — « Sur chaque élément » = étiquette texte + coloration d'état.** Le **texte** porte le
  lien Modbus explicite (rep + bit `cmd`/%MW → état physique → bit `ret`/%MW) ; la **couleur** porte
  l'état instantané (tige teintée quand `cmd` active, fenêtre capteur allumée quand B1/B2=1, anneau
  selon KM1_AUX). Réutilise les matériaux déjà posés par les builders (peu coûteux).
- **D-arch — Couture headless/visuel.** Le backend (S3.1) *connaît et expose* sa santé, testable
  en xUnit (comme les briques 2/3). La présentation (S3.2/S3.3) ne fait que *lire et montrer*, sur
  le thread principal Godot (comme `ApplyToScene`). Fichiers disjoints S3.1 ↔ (S3.2/S3.3).

## Frontière Arch A (rien de neuf)

Le HUD lit sur le **thread principal Godot** (`_Process`/`_PhysicsProcess`) : l'état déjà publié
par `_sim` (heartbeat, positions) et des **snapshots** du datastore (`SnapshotCommands` /
`SnapshotReturns`, qui verrouillent en interne). Le thread propre du serveur reste intouché ; il ne
fait que servir le TCP et, via `RegistersChanged`, horodater la dernière écriture client (écriture
d'un simple compteur/timestamp, lue côté principal — thread-safe par `Interlocked`/`volatile`).

## Points durs / incertitudes (traités dans les amorces)

1. **Comportement réel du bind occupé** (D-013) : `_server.Start()` lève-t-il **synchroniquement**
   une `SocketException` (→ try/catch suffit) ou échoue-t-il en silence (→ pré-vol du port
   obligatoire) ? **Tranché par le test de reproduction en S3.1**, écrit **avant** le correctif.
2. **`RegistersChanged` fiable ?** L'event existe-t-il en FluentModbus 5.3.2 et fire-t-il sur FC16
   même si la valeur ne change pas ? **Mini-vérif en tête de S3.1** ; repli documenté sinon.
3. **Étiquettes 3D lisibles** (S3.3) : `Label3D` orienté caméra, cadence de rafraîchissement basse
   (~5-10 Hz) pour éviter le scintillement du texte ; format compact (une ligne par élément).

## Dettes

- **D-013** (bind silencieux) : *détectable* en S3.1, *visible* en S3.2 → **soldée en fin de S3.2**.
- **Hors périmètre, consignées** (2026-07-16) : **D-015** (nav 3D + vitesse réglable), **D-016**
  (édition/branchement in-app), **D-017** (simulation de cas non nominaux / éléments défaillants).
- **Restent ouvertes, non touchées** : D-012 (throttling), D-005 (JSON Schema), D-011 (dup StepSim).

## Banc de test attendu

- **S3.1** : `dotnet test` **re-figé** — nouveaux témoins serveur/core (bind occupé reproduit,
  activité, `SnapshotReturns`). Compte annoncé dans le rapport (référence actuelle : **90**). Les
  **4 pytest full-chain** (`testbench/test_modbus_chain.py`) restent **inchangés**.
- **S3.2 / S3.3** : glue Godot (lecture seule) → **aucun changement** de comportement Modbus :
  `smoke_anim.ps1` et les 4 pytest restent **verts**. Build assembly Godot **0 erreur**.

## Definition of Done (sprint)

- [ ] S3.1 : bind occupé → échec **clair** (exception typée, test de repro vert) ; `IsListening`,
      `LastClientWriteUtc`, `SnapshotReturns()` livrés + testés ; `dotnet test` vert (compte annoncé).
- [ ] S3.2 : port 502 occupé → **bandeau visible** + log clair (plus de maquette figée muette) →
      **D-013 soldée** ; panneau santé (heartbeat / serveur / activité PLC) à l'écran.
- [ ] S3.3 : chaque élément porte sa chaîne `cmd → physique → ret` (texte, %MW du pivot) **+
      coloration d'état** ; `demo_sprint_03.ps1` (pré-vol port + scénarios guidés) vert.
- [ ] Build Godot 0 erreur ; `smoke_anim.ps1` + 4 pytest full-chain inchangés/verts.
- [ ] `00_etat.md` en état de sprint ; les 3 amorces cochées ; NOTES sprint 3 à la clôture.

## Ordre de travail

1. **S3.1** (backend, testable) → **S3.2** (santé visible) → **S3.3** (chaîne par élément + démo).
2. Archi figée : `/sprint open 03` orchestre en séquentiel strict, un sous-agent cold-start par
   sous-sprint, autonome jusqu'au vert. Nico reprend au rapport final (ou sur blocage).

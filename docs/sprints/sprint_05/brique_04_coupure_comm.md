# S5.4 — Déconnexion TCP (`ModbusServer`) + contrôle comm global

> Sous-sprint **backend + UI**. Reprise à froid : `CLAUDE.md` + `00_etat.md` + cette amorce.
> Contient le **point dur FluentModbus** du sprint — spike de vérification obligatoire avant code.

## Objectif
Ajouter les **deux défauts de comm** globaux, déclenchables depuis le HUD :
1. **Gel `ret`** (déjà modélisé en S5.1 : `FaultSet.RetFrozen`) — brancher son **contrôle UI** ;
2. **Déconnexion TCP réelle** — `ModbusServer` ferme la socket : l'I/O Scanner du M580 passe en
   **défaut de scrutation** (perte de l'esclave), distinct du simple gel de données. « Réparer »
   rétablit l'écoute.

## Fichiers touchés
- `runtime/server/ModbusServer.cs` (méthodes `Disconnect()` / `Reconnect()`, état exposé).
- `runtime/server.tests/ModbusServerTests.cs` (déconnexion/reconnexion).
- `runtime/scenes/HealthHud.cs` (contrôles globaux : geler `ret`, couper TCP, réparer + affichage d'état).
- `runtime/scenes/CarrouselScene.cs` (garde `StepSim` quand déconnecté ; câblage HUD↔serveur/`sim.Faults`).
- Lire pour comprendre : `ModbusServer.cs`, `HealthHud.cs`, `CarrouselScene.cs`, `FaultSet` (`RetFrozen`).

## ⚠ Spike FluentModbus (à faire AVANT d'écrire la feature)
Vérifier le comportement réel de **FluentModbus 5.3.2** : une instance `ModbusTcpServer` supporte-
t-elle `Stop()` **puis** `Start(endpoint)` à nouveau (ré-écoute, clients reconnectables) ?
- **Si oui** : `Disconnect()` = `Stop()` ; `Reconnect()` = `Start()` (re-passer le pré-vol `TcpListener`
  + `AddUnit` si nécessaire).
- **Si non** (instance non réutilisable) : `Reconnect()` **recrée** un `ModbusTcpServer` neuf et
  **réarme** `EnableRaisingEvents`/`AlwaysRaiseChangedEvent`/`RegistersChanged` + `AddUnit(unitId)`,
  exactement comme le constructeur actuel (factoriser un `BuildServer()` privé).
Documenter le verdict dans le NOTES et dans `docs/memory.md` (comme les autres découvertes FluentModbus).

## Contrat d'API visé
- `ModbusServer` :
  - `public void Disconnect();` — ferme l'écoute/les connexions (I/O Scanner perd l'esclave).
    `IsListening` repasse à `false`. Idempotent (déjà déconnecté ⇒ no-op).
  - `public void Reconnect();` — ré-écoute (pré-vol 502 + `Start`/recréation selon le spike).
    Peut lever `ModbusServerException` si le re-bind échoue (port repris) — **remonté à l'UI**
    comme le bind initial (bandeau/rappel), pas avalé silencieusement (cf. D-013).
  - `PullCommands()`/`PushReturns()` : **no-op** si non à l'écoute (garde en tête, `if (!_isListening) return;`)
    pour que `StepSim` puisse continuer à tourner pendant la déconnexion sans lever sur
    `GetHoldingRegisters` d'un serveur arrêté. La sim (physique + heartbeat interne) tourne toujours.
- `CarrouselScene.StepSim` : inchangé dans sa structure ; les no-op serveur suffisent (pas besoin
  de garde supplémentaire). Vérifier qu'aucune exception ne fuit quand déconnecté.
- `HealthHud` : trois contrôles globaux (boutons) + affichage :
  - « Geler retours » (toggle) → `sim.Faults.RetFrozen = !RetFrozen` ;
  - « Couper TCP » (toggle) → `server.Disconnect()` / `server.Reconnect()` ;
  - état affiché : ligne comm indiquant `ret figé` / `TCP coupé` / `nominal`.
  - Le HUD reçoit les références nécessaires via `Configure` (déjà là pour `server`/`sim`) —
    étendre si besoin, mais rester **lecture seule sur le datastore** (le HUD pilote serveur et
    `Faults`, il ne touche jamais le datastore ni le scene tree 3D).

## Décisions pré-tranchées
- **Gel `ret`** est modélisé **côté sim** (S5.1) ; S5.4 n'ajoute que son **interrupteur UI** —
  ne pas ré-implémenter le gel dans le serveur.
- **Déconnexion TCP** est **transport** (serveur), pas un état de `FaultSet` : deux couches
  distinctes, chacune enforce son propre défaut.
- Pendant `TCP coupé` : la 3D continue d'animer (physique tourne), le heartbeat **interne**
  avance mais **n'est plus lu** par personne (aucun client) — cohérent avec « perte de l'esclave ».
  Pendant `ret figé` (TCP vivant) : le M580 lit toujours, mais des données figées + heartbeat
  immobile ⇒ il détecte via son watchdog. **Incohérence 3D/PLC assumée** (message pédagogique).
- Le **bandeau d'échec** (`ShowBindFailure`) et l'invariant « une panne serveur ne se cache pas »
  restent intacts ; le mode aveugle (S5.3) ne masque **pas** l'état comm du HUD.

## Definition of Done (cochable)
- [ ] Spike FluentModbus tranché et documenté (réutilisable ou recréation).
- [ ] `Disconnect()`/`Reconnect()` implémentés ; `Pull/Push` no-op hors écoute ; `StepSim` ne lève
  pas pendant la déconnexion.
- [ ] Bouton « Couper TCP » : un client (pymodbus/io_scanner_sim) **perd la connexion** puis
  **peut se reconnecter** après « Réparer ». Bouton « Geler retours » : `ret`/heartbeat figés
  côté client, TCP toujours vivant.
- [ ] Tests xUnit serveur : déconnexion coupe l'écoute (`IsListening==false`), reconnexion la
  rétablit (`IsListening==true`) ; un `Pull/Push` hors écoute ne lève pas.
- [ ] Build Godot 0 erreur ; smoke inchangé.

## Banc attendu — **re-figé (banc serveur)**
Le banc **serveur** (xUnit `server.tests`) passe de son total actuel à **+M** (tests
déconnexion/reconnexion). Raison : nouvelle capacité transport (prévue). **4 pytest full-chain
inchangés** (ils n'activent pas la coupure ; connexion nominale). Annoncer le nouveau total serveur.

## Vérif autosuffisante
- `dotnet test runtime/server.tests/…` → vert, total serveur +M.
- `dotnet test runtime/tests/…` → 95+N (inchangé depuis S5.1).
- `pytest testbench/test_modbus_chain.py` → 4 verts.
- Validation manuelle Nico (F5 + `io_scanner_sim.py`) : couper TCP → le client se plaint de perte
  de connexion ; réparer → il rescanne ; geler retours → heartbeat figé côté client, TCP vivant.

## Ce qu'il NE faut PAS faire
- Ne pas coder le gel `ret` dans le serveur (il est en S5.1, côté sim).
- Ne pas avaler une exception de re-bind (`Reconnect`) — la rendre visible (D-013).
- Ne pas toucher au format fil (big-endian), à l'Arch A, au pivot, à la cadence.
- Ne pas dégrader la détection d'activité PLC (`RegistersChanged`/`LastClientWriteUtc`) — la
  réarmer si le serveur est recréé.

## Dépendances / validation manuelle
- **Dépendances** : **S5.1** (`RetFrozen`), **S5.3** (partage `CarrouselScene.cs`) ⇒ séquentiel après.
- Validation manuelle : F5 Godot + `io_scanner_sim.py` (voir Vérif).

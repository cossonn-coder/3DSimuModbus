# Sprint 1 · Brique 3 — `ModbusServer` (serveur FluentModbus branché sur le datastore)

> **But de ce fichier** : reprendre la brique 3 **à froid** (après un `/clear`) sans relire
> l'historique. Il fige le contrat d'API visé, les décisions déjà tranchées (à re-valider à
> l'étape archi) et les questions ouvertes.
> Contexte global : `CLAUDE.md` · décisions : `docs/memory.md` · POC : `docs/notes/NOTES_sprint_01.md §1`
> · datastore : `runtime/core/ModbusDataStore.cs` (+ NOTES §2) · contrat data : `pivot/machine_carrousel.json`.

## Où on en est

- Briques 1 & 2 **livrées** : `PivotModel` (loader, résolution d'adresses) + `ModbusDataStore`
  (source de vérité, `ushort[]` cmd/ret + verrou), 28 verts xUnit.
- POC D-001 **soldé** : FluentModbus **5.3.2** figé, Arch A confirmée. Les 3 contraintes du
  POC sont à ré-imposer ici (voir plus bas).
- Brique 3 = **le serveur**, dernière pièce du transport Modbus. Après elle, la chaîne
  FC3/FC16 tourne bout-en-bout au testbench Python **sans 3D ni cinématique réelle**.

## Objectif de la brique

Une classe `ModbusServer` qui encapsule le `ModbusTcpServer` FluentModbus et le **branche sur
le `ModbusDataStore`** : elle **tire** (pull) la zone `cmd` du buffer serveur vers le datastore,
et **pousse** (push) la zone `ret` du datastore vers le buffer serveur. Le thread serveur
FluentModbus ne touche **ni** le scene tree **ni** le datastore : c'est le **thread appelant**
(le futur `_PhysicsProcess`, brique 4) qui déclenche pull/push, sous `server.Lock`.

## Rappel Arch A (déjà actée)

Datastore = source de vérité. Buffer FluentModbus = détail **privé** de `ModbusServer`, recopié
↔ datastore **une fois par tick** sous `server.Lock` : **pull `cmd` début de tick**, **push `ret`
fin de tick**. Latence commande→retour mesurée au POC = **1 tick** (conforme).

## Les 3 contraintes POC D-001 à ré-imposer (non négociables)

1. **Accès buffer synchrone.** `GetHoldingRegisters()` renvoie un `Span<short>` (ref struct) qui
   **ne peut pas** vivre dans une méthode `async`. Tout le pont pull/push est **synchrone**.
2. **`server.AddUnit(unit_id)` à l'init.** Le pivot impose `unit_id = 1` ; le serveur ne sert que
   l'unité 0 par défaut et **ferme la connexion** sinon. Buffer accédé via `GetHoldingRegisters(unit_id)`.
3. **Endianness : toujours `Get/SetBigEndian<ushort>`.** Le buffer natif est little-endian, le fil
   Modbus big-endian. **La traduction se fait ici, registre par registre.** Le datastore, lui, ne
   manipule que des mots en **ordre hôte** (numériques) : `ModbusServer` est le **seul** endroit
   qui connaît le format fil.

## Contrat d'API proposé (à VALIDER à l'étape archi)

```csharp
public sealed class ModbusServer : IDisposable
{
    // Récupère unit_id, port et les bases de zones depuis le pivot ; garde la réf au datastore.
    public ModbusServer(PivotModel pivot, ModbusDataStore store, IPAddress? bind = null);

    public void Start();          // AddUnit(unit_id) puis démarre l'écoute TCP (port du pivot)
    public void PullCommands();   // sous server.Lock : buffer cmd (BigEndian) -> store.WriteCommandsFromWire
    public void PushReturns();    // sous server.Lock : store.CopyReturnsToWire -> buffer ret (BigEndian)
    public void Dispose();        // arrêt propre du serveur
}
```

Cœur du pont (synchrone, per-registre pour l'endianness) :
```csharp
public void PullCommands()
{
    lock (_server.Lock)
    {
        Span<short> buffer = _server.GetHoldingRegisters(_unitId);
        Span<ushort> cmd = stackalloc ushort[_store.CommandWordCount];
        for (int i = 0; i < cmd.Length; i++)
            cmd[i] = buffer.GetBigEndian<ushort>(_cmdBase + i);   // fil -> hôte
        _store.WriteCommandsFromWire(cmd);
    }
}
public void PushReturns()
{
    lock (_server.Lock)
    {
        Span<short> buffer = _server.GetHoldingRegisters(_unitId);
        Span<ushort> ret = stackalloc ushort[_store.ReturnWordCount];
        _store.CopyReturnsToWire(ret);
        for (int i = 0; i < ret.Length; i++)
            buffer.SetBigEndian<ushort>(_retBase + i, ret[i]);    // hôte -> fil
    }
}
```
`_cmdBase = pivot.GetZone("cmd").Base` (100), `_retBase = pivot.GetZone("ret").Base` (200) —
**jamais** de constante en dur.

## Décisions de design pré-tranchées (à confirmer, pas à re-litiger)

- **D-a — `ModbusServer` est le seul détenteur du format fil.** Le datastore reste en ordre hôte ;
  la traduction big-endian ↔ hôte vit ici (contrainte POC 3). *Raison* : garde le datastore et la
  sim purs et endian-agnostiques.
- **D-b — Serveur passif (pas d'horloge interne).** Il **n'a pas** de timer : c'est le thread
  appelant (boucle de sim / `_PhysicsProcess`, brique 4) qui rythme pull/push. *Raison* : une seule
  horloge dans le système (celle du tick physique), pas de course entre deux cadences.
- **D-c — Bases/port/unit_id tirés du pivot** au constructeur (pas de magie). *Alternative écartée* :
  les câbler en dur → viole « aucune adresse absolue en dur ».
- **D-d — `stackalloc` pour le span de transfert.** Zéro allocation, tailles minuscules (1 et 2 mots),
  cohérent avec le contrat `Span` du datastore.
- **D-e — Pull et Push séparés** (deux méthodes) plutôt qu'un `Tick()` unique. *Raison* : Arch A veut
  **pull en début** de tick et **push en fin** de tick, avec la cinématique entre les deux. Les fusionner
  interdirait ce placement.

## Questions encore ouvertes (à trancher à l'archi)

1. **Bind address par défaut** : `IPAddress.Any` (toutes interfaces, pratique pour le M580 sur le LAN)
   ou `Loopback` (sûr par défaut, override explicite pour le réseau) ? **Reco : `Any`** (le pivot cible
   un M580 distant ; documenter la règle de pare-feu Windows TCP 502 déjà prévue dans `memory.md`).
2. **Comment valider la brique 3 SANS la boucle de sim (brique 4) ?** Trois options :
   - (a) **test d'intégration C# in-process** : `ModbusServer` + un client FluentModbus dans le même
     `dotnet test`, on écrit `cmd` via le client, on vérifie que `store` le reçoit après `PullCommands`,
     et qu'un `PushReturns` d'un `ret` connu est relu correctement. Rapide, déterministe, **hors réseau réel**.
   - (b) **runner headless jetable** (successeur du POC) martelé par `testbench/io_scanner_sim.py`.
   - (c) différer la validation full-chain à la brique 4 (qui fournira la vraie cinématique).
   **Reco : (a) pour le transport + endianness** (verrouille la brique isolément), **puis** la vraie
   validation full-chain FC3/FC16 arrive avec la brique 4 (débloque les 4 pytest en skip).
3. **Cycle de vie / `Dispose`** : suffit-il de `Stop()` le serveur, ou faut-il gérer un arrêt sous
   charge (client connecté) ? **Reco : `Dispose` = stop simple** ; robustesse d'arrêt sous charge à
   revoir seulement si le testbench le révèle.
4. **Où vit l'objet `ModbusServer` dans Godot ?** Créé/démarré dans `_Ready` d'un nœud autoload, pull/push
   appelés dans `_PhysicsProcess`. **Hors périmètre brique 3** (câblage Godot = brique 4/5) — juste
   s'assurer que l'API `Start/PullCommands/PushReturns/Dispose` s'y prête.

## Definition of Done (brique 3)

- [ ] `ModbusServer` dans `runtime/core/` (ou `runtime/` si dépendance FluentModbus incompatible avec
      `CarrouselCore` — **à vérifier** : `CarrouselCore` doit-il référencer FluentModbus, ou la brique
      serveur vit-elle dans l'assembly Godot ? cf. note ci-dessous).
- [ ] `AddUnit(unit_id)`, accès buffer **synchrone**, `Get/SetBigEndian<ushort>` — les 3 contraintes POC.
- [ ] Bases/port/unit_id résolus depuis le pivot (aucune constante en dur).
- [ ] `PullCommands`/`PushReturns` sous `server.Lock` ; round-trip client→store et store→client fidèle.
- [ ] Validation retenue (reco 2a) verte : test d'intégration transport + endianness.
- [ ] Points de design justifiés dans `NOTES_sprint_01.md §3`.
- [ ] Journal / backlog / (dettes si besoin) à jour ; ce brief coché.

> **Note d'architecture à trancher tôt** : `CarrouselCore` est aujourd'hui sans dépendance externe
> (pur, testable). FluentModbus est une dépendance NuGet lourde. **Décision à prendre à l'archi** :
> soit `ModbusServer` vit dans l'assembly **Godot** (`DemonstrateurCarrousel.csproj`, qui a déjà
> FluentModbus 5.3.2) et n'est pas testé en `CarrouselCore.Tests` ; soit on ajoute FluentModbus à
> `CarrouselCore` pour tester le serveur hors Godot. **Reco provisoire** : garder `CarrouselCore` pur
> et mettre `ModbusServer` + son test d'intégration dans un projet qui référence FluentModbus (assembly
> Godot, ou un petit projet d'intégration dédié). À valider selon la faisabilité du `dotnet test`.

## Ordre de travail

1. **Archi avant code** : valider le contrat ci-dessus + trancher les 4 questions ouvertes (surtout
   la localisation de l'assembly et le mode de validation) → attendre le go.
2. Générer `ModbusServer.cs` (**fichier par fichier**, SSH mobile).
3. Générer la validation retenue (test d'intégration transport/endianness) → boucler jusqu'au vert.
4. Mettre à jour l'orchestration + `NOTES_sprint_01.md §3`. Brique suivante : **brique 4** (boucle de
   simulation : cinématique scriptée + heartbeat), amorce `sprint_01_brique_04_simulation.md`.

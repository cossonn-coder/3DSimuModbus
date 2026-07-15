# NOTES pédagogiques — Sprint 01

Décomposition des mécanismes clés introduits au sprint 1. Public visé : quelqu'un qui
découvre Modbus, FluentModbus et le thread-safety d'un serveur de simulation.

> Ce document se remplit au fil du sprint et sera complété à la clôture (`/sprint close 01`).

---

## 1. POC D-001 — comportement thread-safe réel de FluentModbus

### Pourquoi un POC avant d'écrire les briques

La règle « architecture avant code » impose de lever le **point dur n°1** en premier :
on ne connaissait pas le comportement réel de FluentModbus comme *serveur* scruté en
continu. Plutôt que de figer l'API des 3 briques C# puis découvrir un problème, on a
écrit un **harnais jetable** (`runtime/poc/`) : un vrai `ModbusTcpServer` + une horloge
100 ms qui joue la future boucle de simulation, martelé par le client Python
`testbench/io_scanner_sim.py` (qui joue le M580).

### L'architecture validée (« Arch A »)

```
Thread serveur FluentModbus          Thread « simulation » (ici : boucle 100 ms)
────────────────────────────         ───────────────────────────────────────────
sert les requêtes FC3/FC16           à chaque tick, SOUS lock(server.Lock) :
sur SON buffer interne                 1. lit le mot cmd   (snapshot début de tick)
   ▲          │                         2. calcule les retours (cinématique)
   │ server.Lock (rendez-vous)          3. écrit les mots ret (publication fin de tick)
   └──────────┘
```

Le datastore (à venir, brique 2) sera la **source de vérité** ; le buffer FluentModbus
n'est qu'un détail interne de la brique serveur, recopié ↔ datastore une fois par tick.
Le thread serveur ne touche jamais le scene tree ni le datastore.

**Résultat** : stable sous scan FC3/FC16 répété, aucun crash, aucune valeur corrompue
(*tearing*). Tenir `server.Lock` le temps d'une copie de quelques mots est indolore.
Latence commande → retour mesurée = **1 tick** (le `KM1_AUX` colle au cycle suivant
l'écriture de `cmd_run`) — parfaitement acceptable devant la cadence de scan (≥ 100 ms).

### Les trois pièges découverts (et leur résolution)

Ces trois points sont maintenant des **contraintes imposées** à la future brique
`ModbusServer`. Ils viennent tous du fait que FluentModbus est optimisé pour du
FluentModbus↔FluentModbus, alors que nos clients (pymodbus, puis le **M580 Schneider**)
sont des implémentations Modbus *standard*.

**Piège 1 — `Span<short>` interdit dans une méthode `async`.**
`GetHoldingRegisters()` renvoie un `Span<short>` (un *ref struct* : vit sur la pile,
jamais sur le tas). Le compilateur **refuse** qu'il survive à un `await` (il ne peut pas
être stocké dans la machine à états générée). Le POC l'a heurté dès la 1ʳᵉ compilation.
→ *Résolution* : tout l'accès buffer se fait dans une **méthode synchrone**. Sans
conséquence pour le runtime : la boucle réelle vivra dans `_PhysicsProcess` (synchrone).

**Piège 2 — le serveur ne répond qu'à l'unit 0 par défaut.**
Le pivot impose `unit_id = 1`. Or `new ModbusTcpServer()` ne sert que l'unité 0 et
**ferme brutalement la connexion** pour toute autre (symptôme observé côté client :
`Connection unexpectedly closed`). → *Résolution* : `server.AddUnit(1)` à l'init, puis
accès au buffer de cette unité par `server.GetHoldingRegisters(1)`.

**Piège 3 — endianness : buffer natif little-endian ≠ fil Modbus big-endian.**
Modbus transporte les registres en **big-endian** (octet de poids fort en premier). Le
buffer interne de FluentModbus est en little-endian (natif x86). Un accès **brut**
(`regs[n] = 1`) fait donc lire au client une valeur aux **octets inversés** :

| Écrit côté serveur (brut) | Lu côté pymodbus/M580 |
|---|---|
| `0x0001` (bit 0) | `0x0100` = **256** ✗ |
| heartbeat croissant +1 | saute de +256 à chaque pas ✗ |

→ *Résolution* : **toujours** passer par les helpers `regs.SetBigEndian<T>(addr, val)` /
`regs.GetBigEndian<T>(addr)` fournis par FluentModbus (`SpanExtensions`). Après
correction, le client relit un heartbeat propre (`115, 116, 117…`) et les bits aux bonnes
positions.

### Effets de bord rencontrés (hors FluentModbus)

- **pymodbus 3.14** a supprimé le kwarg `slave=` (renommé `device_id=`). Le testbench,
  écrit contre l'ancienne API, cassait dès qu'un serveur répondait. Corrigé
  (`io_scanner_sim.py`, `test_modbus_chain.py`) → dette **D-007**.

### Ce que le POC fige pour la suite

- Version **FluentModbus 5.3.2** épinglée (`DemonstrateurCarrousel.csproj`).
- Recette `ModbusServer` : `AddUnit(unit_id)` → `GetHoldingRegisters(unit_id)` →
  `Get/SetBigEndian<ushort>` → accès **synchrone** sous `server.Lock`.
- Le fichier `runtime/poc/` est **jetable** : il sera supprimé une fois la brique
  serveur livrée (il ne reflète pas le style final — adresses en dur tolérées pour un POC).

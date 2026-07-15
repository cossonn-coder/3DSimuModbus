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

---

## 2. Brique 2 — le `ModbusDataStore` (source de vérité d'Arch A)

### À quoi sert cette pièce

Entre le **thread serveur** (qui parle Modbus au M580) et le **thread physique** (qui
calcule la cinématique), il faut un point de rendez-vous neutre pour les mots d'échange.
C'est le datastore : deux tableaux `ushort[]` (zone `cmd` = PLC→sim, zone `ret` = sim→PLC)
protégés par un verrou. En Arch A, **il est la source de vérité** ; le buffer interne de
FluentModbus n'en est qu'une recopie temporaire, rafraîchie une fois par tick.

```
        thread serveur (FluentModbus)                 thread physique (simulation)
        ─────────────────────────────                 ────────────────────────────
        sert FC3/FC16 sur SON buffer        début tick, sous server.Lock :
                                              WriteCommandsFromWire(cmdSlice)  [pull]
                                            ── puis, hors server.Lock : ──
                                              cmd = SnapshotCommands()
                                              …cinématique → ushort[] ret…
                                              PublishReturns(ret)
                                            fin tick, sous server.Lock :
                                              CopyReturnsToWire(retSlice)      [push]
```

### Le principe directeur : un **transport de mots bruts**, rien d'autre

Le choix le plus structurant est ce que le datastore **ne fait pas**. Il ne connaît ni les
bits (`cmd_run`, `S11`…), ni le heartbeat, ni la moindre règle machine. Il stocke et copie
des mots 16 bits, point. Le décodage bit↔signal reste au `PivotModel`
(`pivot.GetSignal("KM1","cmd_run").ReadBit(cmd[wordRel])`), et le heartbeat est reconstruit
par la simulation dans son `ushort[] ret` avant publication.

*Pourquoi ce partage ?* Un objet sans sémantique métier ni horloge est **générique**
(réutilisable si le mapping change) et **trivialement testable** (aucun mock, aucune
notion de temps). C'est la traduction directe de « simplicité d'abord » : le datastore
n'a qu'une responsabilité, être un tampon cohérent.

### Les décisions de design et leur justification

| Réf | Décision | Pourquoi (et alternative écartée) |
|---|---|---|
| **D-a** | Le datastore détient une référence au `PivotModel` | Il en tire `size_words` → **aucune taille en dur**. *Écarté* : passer les tailles à la main = duplication du contrat, risque de désynchronisation avec le pivot. |
| **D-b** | Tailles des tableaux = `size_words` des zones (cmd=1, ret=2) | Le pivot est le contrat central : les tailles en découlent, jamais l'inverse. |
| **D-c** | Le heartbeat **n'est pas** incrémenté ici | Le datastore reste un transport sans horloge. L'incrément appartient à la boucle de sim (brique 4), qui possède le tick. |
| **D-d** | Verrou interne conservé **même si** un seul thread y accède en Arch A | *Belt & suspenders* : le pattern imposé est `ushort[]` + verrou, et cela ouvre la porte à une lecture concurrente future (IHM debug) sans re-toucher la classe. Coût nul. |
| **D-e** | Grain snapshot/publish = **la zone entière** (copie `ushort[]`), pas signal par signal | Garantit la **cohérence intra-scan** : le PLC voit un jeu de retours figé par tick, jamais un état à moitié calculé. Se teste en une assertion. |

### Les trois questions ouvertes de l'amorce, tranchées

1. **`SnapshotCommands()` rend un `ushort[]` brut**, pas un struct décodé (`bool Run,
   Extend1…`). Le brut garde le datastore agnostique de la sémantique machine ; c'est la
   sim qui décode via les `Signal` du pivot. Un struct aurait couplé le tampon à *cette*
   machine précise.
2. **Le pont serveur prend des `Span<ushort>`** (`ReadOnlySpan` en pull, `Span` en push),
   pas des `ushort[]`. Deux gains : **zéro allocation** et **alignement exact** sur l'API
   buffer de FluentModbus (`GetHoldingRegisters` renvoie justement un `Span`). La brique 3
   pourra donc faire `store.WriteCommandsFromWire(buffer.Slice(base, size))` sans copie
   intermédiaire.
3. **Pas d'accès direct au mot heartbeat.** La sim reconstruit tout le `ushort[] ret` à
   chaque tick puis `PublishReturns`. Cohérent avec D-e (grain « zone entière ») et garde
   l'API minimale.

### Deux subtilités qui évitent des bugs

- **Snapshot = *copie*, pas la référence interne.** `SnapshotCommands()` renvoie
  `_cmd.Clone()`. Sans cela, la simulation muterait l'état interne du datastore en
  travaillant sur « son » tableau — fuite d'abstraction classique. Un test le verrouille :
  muter le tableau rendu ne change pas le store.
- **`PublishReturns` *recopie* le contenu** (`Array.Copy`) au lieu de garder la référence
  du tableau fourni. Sinon, la sim qui réutiliserait son buffer `ret` d'un tick à l'autre
  corromprait l'état publié. Test dédié : muter le tableau source après publication
  n'altère pas le store.

### Où s'arrête le datastore (frontière avec la brique 3)

Le datastore **ignore les adresses absolues** (%MW100/%MW200). Le découpage du buffer
FluentModbus par base de zone (`buffer.Slice(base, size)`) est l'affaire du **serveur**
(brique 3), qui connaît, lui, le buffer. Le datastore reçoit donc toujours un span **déjà
dimensionné à la zone**, et se contente d'en **vérifier la longueur** — code défensif :
toute longueur invalide (`PublishReturns`, `WriteCommandsFromWire`, `CopyReturnsToWire`)
lève une `ArgumentException` explicite plutôt qu'une copie partielle silencieuse.

*Sur le verrouillage* : aucun risque d'interblocage. En Arch A, le seul thread physique
prend `server.Lock` (externe) **puis** le verrou du datastore (interne) sur le pont, et ce
verrou interne n'est jamais pris dans l'autre ordre ailleurs. Il n'existe donc pas de cycle
possible.

**État** : `ModbusDataStore.cs` + `ModbusDataStoreTests.cs` livrés, **28 verts** au total
(`dotnet test`, dont 11 pour le datastore). Prochaine pièce : **brique 3**, le serveur
FluentModbus branché sur ce datastore (pull/push sous `server.Lock`), validé au testbench.

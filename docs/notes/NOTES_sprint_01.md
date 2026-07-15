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

---

## 3. Brique 3 — le `ModbusServer` (pont FluentModbus ↔ datastore)

### À quoi sert cette pièce

C'est la **dernière pièce du transport Modbus**. Elle encapsule le `ModbusTcpServer` de
FluentModbus et le **branche** sur le `ModbusDataStore` : elle **tire** (pull) la zone `cmd`
du buffer serveur vers le datastore, et **pousse** (push) la zone `ret` du datastore vers le
buffer. Après elle, la chaîne FC3/FC16 tourne bout-en-bout — il ne manque plus que la vraie
cinématique (brique 4) pour remplir le `ret`.

```
        M580 (client I/O Scanner)                ModbusServer (brique 3)          datastore
        ─────────────────────────                ───────────────────────         ─────────
        FC16 écrit %MW100  ─────────►  buffer cmd ──[PullCommands, server.Lock]──►  zone cmd
        FC3  lit  %MW200.. ◄─────────  buffer ret ◄─[PushReturns,  server.Lock]───  zone ret
                                       (big-endian, registre par registre)
```

### Décision structurante n°1 — où vit `ModbusServer` ? → **projet dédié `runtime/server/`**

`CarrouselCore` est **pur** par construction (D-006 : aucune dépendance tierce, testable hors
Godot). FluentModbus est un NuGet lourd. Trois options ont été pesées à l'archi :

| Option | Verdict |
|---|---|
| **A.** FluentModbus dans `CarrouselCore` | ❌ Détruit la pureté de `CarrouselCore` (D-006) : toute la logique loader/datastore hériterait d'une dépendance réseau. |
| **B.** `ModbusServer` dans l'assembly Godot (`DemonstrateurCarrousel`) | ❌ Plus testable en `dotnet test` (SDK Godot cible le moteur) → on perd la validation isolée. |
| **C.** Nouveau projet `runtime/server/CarrouselServer.csproj` (classlib net8.0 → `CarrouselCore` + FluentModbus 5.3.2) | ✅ **Retenu.** `CarrouselCore` reste pur ; le code fil-dépendant est isolé ; testable in-process. |

Résultat : `CarrouselServer` référence `CarrouselCore` + FluentModbus **5.3.2** (même version
figée que l'assembly Godot, pour éviter tout conflit au runtime). L'assembly Godot n'aura
qu'à référencer `CarrouselServer` (brique 4/5) au lieu de porter la logique serveur. Les
dossiers `server/` et `server.tests/` sont retirés du glob du SDK Godot
(`<Compile Remove="server/**/*.cs" />`), comme l'étaient déjà `poc/`, `core/`, `tests/`.

### Décision structurante n°2 — valider sans la boucle de sim → **test d'intégration in-process**

La brique 4 (cinématique) n'existe pas encore. Pour verrouiller la brique 3 **isolément**, on
démarre un vrai `ModbusServer` sur **loopback** et on le martèle avec un vrai
`ModbusTcpClient` FluentModbus dans le **même `dotnet test`** — pas de réseau externe, pas de
M580. On vérifie deux propriétés (`ModbusServerTests.cs`) :

1. **Transport** : ce que le client écrit en FC16 (%MW100) arrive au datastore après
   `PullCommands` ; ce que le datastore publie ressort au client en FC3 (%MW200..) après
   `PushReturns`.
2. **Endianness** (le cœur du risque) : le serveur sérialise le fil en **big-endian**. Un
   client big-endian retrouve la valeur à l'identique (`0x1234 → 0x1234`) ; un client
   **little-endian** lit les octets **inversés** (`0x1234 → 0x3412`) — preuve concrète que le
   format fil est bien big-endian, comme l'attend le M580 réel (déjà validé au POC D-001
   contre pymodbus).

> **Port de test.** On n'utilise **pas** le 502 du pivot (privilège/conflit/flakiness). Le
> test injecte un **port éphémère libre** (listener sur le port 0, relâché aussitôt) dans un
> pivot de test et écoute sur loopback. Le vrai M580 utilisera le port du pivot sur toutes les
> interfaces (`IPAddress.Any` par défaut en prod).

La **vraie** validation full-chain FC3/FC16 (qui débloquera les 4 pytest en skip du testbench
Python) arrive avec la brique 4 et sa cinématique réelle. Ici on prouve le **transport** et
l'**endianness**, ce qui suffit à figer la brique.

### La traduction big-endian se fait **registre par registre**, ici et nulle part ailleurs

C'est le point le plus subtil de la brique et la raison d'être de ce fichier comme
**frontière unique** du format fil (piège 3 du POC). Le buffer natif de FluentModbus est en
little-endian (x86) ; le fil Modbus est big-endian. `ModbusServer` traduit **mot par mot** :

```csharp
// pull : fil → hôte                         // push : hôte → fil
cmd[i] = buffer.GetBigEndian<ushort>(base+i); buffer.SetBigEndian<ushort>(base+i, ret[i]);
```

Le datastore, lui, ne manipule que des mots en **ordre hôte** (numériques) : il reste
endian-agnostique, tout comme la future cinématique. Aucune autre couche du système ne
connaît le big-endian. *Pourquoi registre par registre et pas un bloc ?* Les helpers
`Get/SetBigEndian<ushort>` opèrent sur **un** registre à une adresse donnée ; en V1 tout est
en 16 bits (pas de valeur 32 bits à cheval sur deux mots), donc une boucle triviale sur les 1
à 2 mots de chaque zone suffit — zéro allocation via `stackalloc`.

### Les décisions de design et leur justification

| Réf | Décision | Pourquoi (et alternative écartée) |
|---|---|---|
| **D-a** | `ModbusServer` est le **seul** détenteur du format fil | Garde datastore + sim purs et endian-agnostiques. *Écarté* : traduire côté datastore = fuite du détail fil dans la source de vérité. |
| **D-b** | Serveur **passif** (pas d'horloge interne) | Le thread appelant (`_PhysicsProcess`, brique 4) rythme pull/push. Une seule horloge = pas de course entre deux cadences. |
| **D-c** | Port, unit_id, bases **tirés du pivot** au constructeur | « Aucune adresse absolue en dur ». *Écarté* : câbler 502/1/100/200 = duplication du contrat. |
| **D-d** | `stackalloc` pour les spans de transfert | Zéro allocation, tailles minuscules (1 et 2 mots), aligné sur le contrat `Span` du datastore. |
| **D-e** | Pull et Push **séparés** (deux méthodes), pas un `Tick()` unique | Arch A veut pull en **début** de tick et push en **fin**, la cinématique **entre les deux**. Les fusionner interdirait ce placement. |
| **D-f** | `PivotModel` expose `Port`/`UnitId`, parse **strict** (échec si absent) | Le pivot est le contrat ; ces valeurs réseau ne se **devinent jamais** en silence (un mauvais port/unit_id = connexion fermée côté M580, panne muette). Les 4 fixtures de test minimales reçoivent `port`/`unit_id` explicites ; pas de repli 502/1 caché. |

### Les quatre questions ouvertes de l'amorce, tranchées

1. **Bind par défaut = `IPAddress.Any`** (et non `Loopback`) : le M580 est un client distant
   sur le LAN, le serveur doit écouter sur toutes les interfaces (règle de pare-feu Windows
   entrante TCP 502 déjà actée dans `memory.md`). Le test, lui, passe `Loopback` explicitement.
2. **Validation = test d'intégration in-process** (reco 2a, voir plus haut).
3. **`Dispose` = `Stop()` + `Dispose()` simple** : pas de gestion d'arrêt sous charge (client
   connecté). À revoir seulement si le testbench le révèle — non observé.
4. **Cycle de vie Godot** (`_Ready`/`_PhysicsProcess`) : **hors périmètre brique 3**. L'API
   `Start` / `PullCommands` / `PushReturns` / `Dispose` s'y prête ; le câblage est brique 4/5.

### Où s'arrête `AddUnit` et le verrouillage

`Start()` fait `AddUnit(unitId)` **avant** `Start(IPEndPoint)` (contrainte POC 2 : sans ça le
serveur ne sert que l'unit 0 et ferme la connexion). L'ordre de verrous est toujours
`server.Lock` (externe) **puis** le verrou interne du datastore (pris par
`WriteCommandsFromWire`/`CopyReturnsToWire`), jamais l'inverse — aucun cycle possible, donc
pas d'interblocage (cohérent avec le §2).

**État** : `PivotModel` (ajout `Port`/`UnitId`) + `runtime/server/ModbusServer.cs` +
`runtime/server.tests/ModbusServerTests.cs` livrés. **34 verts** au total (`dotnet test`) :
31 core (28 + `Port`/`UnitId`/champs manquants) + 3 intégration serveur (transport ×2,
endianness ×1). Prochaine pièce : **brique 4**, la boucle de simulation (cinématique scriptée
+ heartbeat), qui remplira le `ret` et débloquera la validation full-chain FC3/FC16 du
testbench Python.

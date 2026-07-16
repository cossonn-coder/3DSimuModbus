# S3.1 — Backend santé : bind visible + activité PLC + snapshot des retours

> **Amorce autosuffisante** (cold-start). Pour livrer ce sous-sprint tu lis **seulement** :
> `CLAUDE.md` + `docs/sprints/sprint_03/00_etat.md` + ce fichier. Sous-sprint **100 % headless**
> (xUnit, aucune dépendance Godot) — même monde de test que les briques 2 et 3 du sprint 1.

## Objectif

Donner au runtime les moyens de **connaître et exposer sa propre santé**, de façon **testable sans
Godot** :
1. **L'échec de bind ne doit plus être muet** (dette **D-013**) : si le port Modbus (502) est déjà
   pris, `ModbusServer.Start()` doit **échouer clairement** (exception typée, message FR), et non
   laisser croire que le serveur écoute.
2. Exposer **l'activité du client PLC** : « une trame `cmd` (FC16) a-t-elle été reçue récemment ? ».
3. Exposer **les mots `ret` publiés** en lecture (`SnapshotReturns`), symétrique de `SnapshotCommands`,
   pour que le HUD (S3.3) puisse afficher la trame réellement renvoyée au M580.

Ce sous-sprint **ne touche pas Godot** et **ne change pas le comportement Modbus nominal** : il
**ajoute** des points d'observation et **durcit** le démarrage.

## Contexte code (source de vérité)

- `runtime/server/ModbusServer.cs` : `Start()` = `_server.AddUnit(_unitId); _server.Start(new
  IPEndPoint(_bind, _port));` — **aucun try/catch, aucun état exposé**. `PullCommands()` recopie la
  zone `cmd` du buffer FluentModbus vers le datastore sous `server.Lock`. `Dispose()` = `Stop()` +
  `Dispose()`.
- `runtime/core/ModbusDataStore.cs` : `SnapshotCommands()` existe (copie défensive de `cmd` sous
  verrou). **Pas** de `SnapshotReturns()`. `PublishReturns` / `CopyReturnsToWire` existent.
- Tests : `runtime/server.tests/` (intégration in-process, vrai serveur + vrai client FluentModbus
  sur port **éphémère**), `runtime/tests/` (core pur). FluentModbus **figé à 5.3.2**.

## Contrat d'API visé (additif — ne casse aucune signature existante)

### `ModbusServer` (runtime/server/ModbusServer.cs)
- `bool IsListening { get; }` — `false` avant `Start()`, `true` après un `Start()` réussi.
- `Start()` — sur **échec de bind** (port occupé), lève **`ModbusServerException`** (nouveau type,
  message FR clair mentionnant `bind:port`, ex. « impossible d'écouter sur 0.0.0.0:502 — port déjà
  utilisé (SimHost reliquat ?) »). Sur succès, `IsListening = true`.
- `System.DateTime? LastClientWriteUtc { get; }` — horodatage UTC de la **dernière écriture `cmd`
  reçue d'un client** (FC16). `null` tant qu'aucune n'a eu lieu.

### `ModbusServerException` (nouveau, runtime/server/)
- `public sealed class ModbusServerException : System.Exception` — ctor `(string message,
  System.Exception? inner = null)`. But : un type **distinct** de `PivotException`, pour que
  l'appelant Godot (S3.2) le catche spécifiquement et affiche le bandeau.

### `ModbusDataStore` (runtime/core/ModbusDataStore.cs)
- `ushort[] SnapshotReturns()` — copie défensive de la zone `ret` **sous verrou** (strict miroir de
  `SnapshotCommands`). Longueur = `ReturnWordCount`.

## Décisions pré-tranchées

- **D-013, méthode imposée (CLAUDE.md §5)** : **écris d'abord un test qui reproduit** le bind
  occupé (démarre un `ModbusServer` sur un port éphémère P, puis un second sur **le même P**, et
  observe). Ce test **tranche le comportement réel** de FluentModbus 5.3.2 :
  - **Si** `_server.Start(endpoint)` lève **synchroniquement** une `SocketException` → il suffit de
    l'**envelopper** en `ModbusServerException` (try/catch dans `Start()`).
  - **Si** l'échec est **silencieux** (pas d'exception) → ajoute un **pré-vol du port** avant
    `_server.Start()` : tenter un `System.Net.Sockets.TcpListener(_bind, _port)` (Start puis Stop
    immédiat) ; si ça lève `SocketException` → `ModbusServerException`. (Ce pré-vol reproduit
    *dans l'app* ce que `demo_sprint_02.ps1` fait déjà en externe — robuste, indépendant de la lib.)
  Le test final **doit être vert** : le second démarrage se solde par un `ModbusServerException`
  attendu (`Assert.Throws`). Documente dans le NOTES lequel des deux cas s'est avéré.
- **Activité PLC — `RegistersChanged` d'abord, repli sinon** : vérifie **au début** si
  `ModbusTcpServer` (FluentModbus 5.3.2) expose un event de type `RegistersChanged` et s'il **fire
  sur FC16** (écriture holding registers), **même quand la valeur ne change pas** (le I/O Scanner
  réécrit `cmd` à chaque scan, valeur souvent identique). 
  - **Si oui** : abonne-toi, mets à jour `LastClientWriteUtc` dans le handler.
  - **Si non/incertain** : **repli** — mets à jour `LastClientWriteUtc` dans `PullCommands()`
    lorsqu'un client s'est manifesté. ⚠ `PullCommands` ne sait pas *distinguer* « client a écrit »
    de « rien reçu » au niveau buffer (le buffer garde la dernière valeur). Repli acceptable V1 :
    considérer « activité » dès qu'**au moins une connexion** a écrit une fois — si même ça n'est
    pas détectable proprement, **n'expose que ce qui est sûr** (`IsListening` + heartbeat côté sim)
    et **crée une dette** « voyant activité PLC non fiable » plutôt que d'inventer un signal faux.
  Principe directeur : **ne jamais promettre plus que le certain** (D-Q3).
- **Thread-safety du timestamp** : `RegistersChanged` fire sur le **thread serveur** ; la lecture se
  fait côté **thread principal Godot**. Stocke un `long _lastClientWriteTicksUtc` écrit via
  `System.Threading.Interlocked.Exchange` (ou `volatile long`), et compose `LastClientWriteUtc`
  (`null` si 0). Pas de `DateTime` partagé non atomique.
- **Horloge** : `System.DateTime.UtcNow` (runtime C# réel — la restriction « pas de `Date.now` » ne
  concerne QUE les scripts de workflow, pas le code produit).
- **`SnapshotReturns`** : copie **défensive** (jamais la référence interne), exactement comme
  `SnapshotCommands`. Aucune logique nouvelle, symétrie stricte.

## Questions résiduelles (trancher en autonomie, documenter le choix)

- Comportement exact du bind occupé (synchrone vs silencieux) → **le test le révèle** ; adapte
  `Start()` en conséquence (voir décision ci-dessus). Pas de blocage.
- Fiabilité de `RegistersChanged` → **mini-vérif** ; en cas de doute, repli + dette. Pas de blocage.

## Definition of Done (cochable)

- [ ] `ModbusServerException` (type dédié) créée.
- [ ] Test de **reproduction D-013** écrit **avant** le correctif : 2ᵉ `Start()` sur le même port →
      `Assert.Throws<ModbusServerException>`. **Vert.**
- [ ] `ModbusServer.Start()` : échec de bind → `ModbusServerException` (message FR clair) ; succès →
      `IsListening == true`. `Start()` nominal (port libre) **inchangé** fonctionnellement.
- [ ] `ModbusServer.LastClientWriteUtc` exposé : non-`null` après qu'un vrai client FluentModbus a
      écrit `cmd` (FC16) — via `RegistersChanged` **ou** repli documenté. Thread-safe (`Interlocked`/
      `volatile`).
- [ ] `ModbusDataStore.SnapshotReturns()` : copie défensive sous verrou + tests (copie ≠ référence ;
      reflète le dernier `PublishReturns`).
- [ ] `dotnet test` **vert**, nouveau compte **annoncé** dans le rapport (réf. actuelle : **90**).
- [ ] Les **4 pytest full-chain** (`testbench/test_modbus_chain.py`, contre `SimHost`) restent **verts**
      (rien du comportement nominal n'a changé).

## Vérif autosuffisante (prouver le vert sans contexte externe)

```
# depuis la racine du repo
dotnet test runtime/CarrouselCore.sln   # ou la solution/projets de test du repo → tout vert
# full-chain (SimHost doit écouter sur 502) :
#   lance SimHost (runtime/simhost) puis :
pytest testbench/test_modbus_chain.py -v   # -> 4 passed
```
Le test de repro D-013 est **la preuve** que l'échec de bind est désormais bruyant (il lève).

## Banc attendu

**Re-figé (prévu, justifié)** : `dotnet test` gagne des témoins (repro bind, activité, snapshot).
Annonce le nouveau total. **Aucun** changement attendu côté `pytest` (4 verts) ni côté `SimHost`.

## Ce qu'il NE faut PAS faire

- ❌ Toucher à Godot (aucun `using Godot`, aucun fichier `runtime/scenes/`).
- ❌ Écrire dans la zone `cmd` (le serveur reste **passif**, lecture seule vis-à-vis des commandes).
- ❌ Changer le comportement Modbus nominal (endianness, Pull/Push, Arch A) — **additif seulement**.
- ❌ Toucher au pivot.
- ❌ Inventer un signal « M580 connecté » non garanti par la lib — n'expose que le certain (D-Q3).
- ❌ Factoriser `StepSim`/`SimHost` (D-011 hors périmètre).

## Validation manuelle éventuelle

Aucune obligatoire (tout est xUnit). Optionnel : lancer deux `SimHost` sur 502 et vérifier que le
second signale l'échec — mais c'est déjà couvert par le test de repro.

## DÉPENDANCES

- **Aucune** (premier sous-sprint). Entrée = état de fin du sprint 2.

## FICHIERS TOUCHÉS

- `runtime/server/ModbusServer.cs` (modif : `Start`, `IsListening`, `LastClientWriteUtc`).
- `runtime/server/ModbusServerException.cs` (**neuf**).
- `runtime/core/ModbusDataStore.cs` (modif : `SnapshotReturns`).
- `runtime/server.tests/` (ajouts : repro bind, activité, `IsListening`).
- `runtime/tests/` (ajouts : `SnapshotReturns`).
- **Disjoint de S3.2/S3.3** (qui ne touchent que `runtime/scenes/`). → pas de conflit de fichier.

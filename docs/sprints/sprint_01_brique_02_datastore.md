# Sprint 1 · Brique 2 — `ModbusDataStore` (amorce autosuffisante)

> **But de ce fichier** : permettre de reprendre la brique 2 **à froid** (après un `/clear`),
> sans relire tout l'historique. Il fige le contrat d'API visé, les décisions de design
> **déjà tranchées** (à re-valider à l'étape archi) et les questions encore ouvertes.
> Contexte global : `CLAUDE.md` · décisions : `docs/memory.md` · contrat data : `pivot/machine_carrousel.json`.

## Où on en est

- Brique 1 **livrée** : `runtime/core/PivotModel.cs` (loader pivot, résolution d'adresses
  absolues) + tests xUnit `runtime/tests/` (17 verts). POC D-001 soldé (Arch A confirmée,
  FluentModbus 5.3.2 figé).
- Brique 2 = **le datastore**, pièce centrale d'Arch A. Vient **avant** le serveur (brique 3).

## Objectif de la brique

Un **datastore Modbus** : objet **C# pur** (`ushort[]` cmd + `ushort[]` ret + verrou),
**zéro dépendance Godot**, dans `runtime/core/` (assembly `CarrouselCore`). Il est la
**source de vérité** des mots d'échange (Arch A). Testé hors Godot dans `runtime/tests/`.

## Rappel Arch A (déjà actée — `docs/memory.md`, entrée 2026-07-14)

Le datastore est la source de vérité. Le buffer interne de FluentModbus est un **détail
privé** du futur `ModbusServer`, recopié ↔ datastore **à chaque tick physique** sous
`server.Lock` (**pull `cmd`** en début de tick, **push `ret`** en fin). Le thread serveur
ne touche **ni** le scene tree **ni** le datastore ; seul le **thread physique** accède au
datastore. Cohérence intra-scan côté PLC : **snapshot des commandes en début de tick,
publication des retours en fin de tick.**

## Contrat d'API proposé (à VALIDER à l'étape archi)

Datastore **générique** (transport de mots bruts) : le (dé)codage bit↔signal reste au
`PivotModel`/à la boucle de simulation, pas dans le datastore. Ça le garde pur et testable.

```csharp
public sealed class ModbusDataStore
{
    // Tailles des zones tirées du pivot (size_words) : cmd=1 mot, ret=2 mots ici.
    public ModbusDataStore(PivotModel pivot);

    // --- Côté simulation (thread physique) ---
    public ushort[] SnapshotCommands();        // copie atomique de la zone cmd (sous verrou)
    public void PublishReturns(ushort[] ret);  // remplace la zone ret d'un bloc (sous verrou) ;
                                               // longueur attendue = size_words(ret), sinon exception

    // --- Pont serveur (brique 3), appelé sous server.Lock ---
    public void WriteCommandsFromWire(ReadOnlySpan<ushort> words); // pull : wire cmd -> datastore
    public void CopyReturnsToWire(Span<ushort> words);             // push : datastore ret -> wire
}
```

Décodage dans la boucle de sim, sans adresse en dur (on passe par les `Signal` du pivot) :
```csharp
var cmd = store.SnapshotCommands();
bool run = pivot.GetSignal("KM1","cmd_run").ReadBit(cmd[/*WordRel*/0]);
// ... calcule les retours dans un ushort[] ret ...
store.PublishReturns(ret);
```

## Décisions de design pré-tranchées (justifiées — à confirmer, pas à re-litiger)

- **D-a — Le datastore détient une référence au `PivotModel`.** Il en tire la taille des
  zones (`size_words`) et évite toute adresse absolue en dur (règle CLAUDE.md). *Alternative
  écartée* : passer les tailles à la main → duplication du contrat, risque de désync.
- **D-b — Tailles des tableaux = `size_words`** des zones `cmd`/`ret` (ici 1 et 2).
- **D-c — Le heartbeat n'est PAS incrémenté par le datastore.** C'est la **boucle de
  simulation** (brique 4) qui l'incrémente et le publie via `PublishReturns`. Le datastore
  ne fait que stocker. *Raison* : le datastore reste un transport sans logique métier ni horloge.
- **D-d — Verrou interne conservé** (pattern imposé CLAUDE.md `ushort[]` + verrou) **même si**
  Arch A limite l'accès au seul thread physique. *Raison* : belt-and-suspenders + ouvre la
  porte à une lecture concurrente future (IHM debug) sans re-toucher le datastore. Coût nul.
- **D-e — Grain snapshot/publish = la zone entière** (copie `ushort[]`), pas signal par
  signal. *Raison* : garantit la cohérence intra-scan (le PLC voit un état de retours figé
  par tick) et se teste trivialement.

## Questions encore ouvertes (à trancher à l'archi)

1. `SnapshotCommands()` renvoie-t-il un `ushort[]` brut (proposé) ou un petit struct décodé
   (`bool Run, Extend1, Extend2`) ? Le brut garde le datastore générique ; le struct simplifie
   la boucle de sim mais couple le datastore à la sémantique machine. **Reco : brut.**
2. `WriteCommandsFromWire`/`CopyReturnsToWire` : `Span<ushort>` (proposé, zéro alloc, aligne
   sur l'accès buffer FluentModbus de la brique 3) vs `ushort[]`. **Reco : `Span`.**
3. Faut-il exposer un accès direct au mot heartbeat pour la brique 4, ou la sim reconstruit-elle
   tout le `ushort[] ret` à chaque tick puis `PublishReturns` ? **Reco : reconstruit + publie.**

## Definition of Done (brique 2)

- [ ] `ModbusDataStore` dans `runtime/core/`, **aucun `using Godot`**, compile dans `CarrouselCore`.
- [ ] Tailles de zones dérivées du `PivotModel` (pas de constante en dur).
- [ ] `SnapshotCommands()` renvoie une **copie** (muter le retour ne modifie pas le store).
- [ ] `PublishReturns` remplace la zone `ret` de façon atomique ; longueur invalide → exception claire.
- [ ] `WriteCommandsFromWire`/`CopyReturnsToWire` : round-trip fidèle wire↔store (pull puis push
      redonne les mêmes mots).
- [ ] Tests xUnit dans `runtime/tests/` (`ModbusDataStoreTests.cs`) : `dotnet test` **vert**.
- [ ] Journal / backlog / (dettes si besoin) à jour ; ce brief coché.

## Ordre de travail

1. **Archi avant code** : présenter l'interface ci-dessus + trancher les 3 questions ouvertes →
   attendre validation.
2. Générer `runtime/core/ModbusDataStore.cs` (**fichier par fichier**, SSH mobile).
3. Générer `runtime/tests/ModbusDataStoreTests.cs` → boucler jusqu'au vert.
4. Mettre à jour l'orchestration. Brique suivante : **brique 3** (serveur FluentModbus branché
   sur le datastore, validé au testbench Python).

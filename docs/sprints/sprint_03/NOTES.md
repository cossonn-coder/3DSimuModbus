# Sprint 3 — NOTES pédagogiques : durcir le démonstrateur

> Public : quelqu'un qui découvre le projet (Modbus, Godot, thread-safety). On explique le
> *pourquoi* des mécanismes introduits ce sprint, pas seulement le *quoi*. Trois thèmes :
> **rendre un échec bruyant**, **savoir si le PLC parle**, **tracer la chaîne à l'écran**.

---

## 1. Le problème de départ (ce qui piquait en démo)

Fin de sprint 2, on a vécu deux gênes en présentant la maquette :

1. **La maquette figée muette.** On lançait la scène, la 3D se construisait… mais rien ne
   bougeait. Le heartbeat vivait pourtant. Cause réelle : **un deuxième programme écoutait déjà
   sur le port 502** (un `SimHost` reliquat d'une vérif). Le `server.Start()` de la scène avait
   **échoué en silence** — aucun message. On croyait la sim cassée alors que c'était juste le port
   occupé. C'est la dette **D-013**.

2. **Personne ne « voit » le lien Modbus.** Devant l'automaticien, la 3D tourne joliment, mais
   rien à l'écran ne dit *quel bit %MW pilote quel vérin*, ni *quel retour en résulte*. Le lien
   entre le programme automate et la physique reste dans nos têtes.

Le sprint 3 attaque exactement ces deux points. **Aucune écriture Modbus nouvelle** : on ne fait
que **lire et montrer**. L'app reste « serveur qui sert, ne décide pas ».

---

## 2. Rendre l'échec de bind bruyant (S3.1 backend + S3.2 visible)

### 2.1 Qu'est-ce qu'un « bind » et pourquoi il échoue

Un serveur TCP doit **réserver** un port (ici 502) auprès de l'OS : c'est le *bind*. Deux
programmes ne peuvent pas réserver le même port en même temps — le second se fait jeter. La
question technique tranchée ce sprint : **comment le second l'apprend-il ?**

Deux mondes possibles :

- **Échec synchrone** : `Start()` lève tout de suite une exception → un simple `try/catch` suffit.
- **Échec silencieux/asynchrone** : `Start()` rend la main sans erreur, et l'échec n'apparaît que
  plus tard (ou jamais côté visible) → il faut **tester le port soi-même avant** (« pré-vol »).

On ne l'a pas deviné : on a écrit une **sonde jetable** (probe) qui ouvre deux serveurs sur le
même port et observe. Verdict pour **FluentModbus 5.3.2** : l'échec est **synchrone**
(`SocketException` levée immédiatement par `Start()`).

### 2.2 Ceinture **et** bretelles

Même si le `try/catch` suffirait, on garde **deux filets** :

```
        ┌─ pré-vol TcpListener sur (adresse, 502) ──┐   ← filet 1 : détection AVANT de lancer
        │   port libre ? on le relâche aussitôt.    │      la lib (indépendant de FluentModbus)
        │   port pris ? -> ModbusServerException     │
        └───────────────────────────────────────────┘
                          │ port libre
                          ▼
        ┌─ _server.Start() dans un try/catch ───────┐   ← filet 2 : au cas où la lib échoue
        │   SocketException -> ModbusServerException │      malgré le pré-vol (course, droits…)
        └───────────────────────────────────────────┘
```

Pourquoi le pré-vol en plus du catch ? Parce qu'il est **indépendant de la librairie** : c'est le
même filet que le pré-vol qu'on avait mis dans `demo_sprint_02.ps1`, ramené **dans l'application**.
Si un jour on change de lib Modbus, la détection tient toujours.

`ModbusServerException` est un **type d'exception dédié** (message en français, mentionne
`adresse:port`). L'intérêt d'un type à nous : la scène peut faire `catch (ModbusServerException)`
et distinguer « le port est pris » d'une panne quelconque, pour afficher le bon message.

### 2.3 Du backend silencieux au bandeau rouge (la couture headless/visuel)

C'est le patron **Arch A** appliqué à la santé : le **backend sait**, la **présentation montre**.

- **S3.1 (headless, testable en xUnit, zéro Godot)** : `ModbusServer` *expose* sa santé —
  `IsListening` (le bind a-t-il réussi ?) et lève `ModbusServerException` sinon. On peut tester
  tout ça **sans lancer Godot** (un test rejoue le bind occupé et vérifie l'exception — le fameux
  « test qui reproduit le bug **avant** le correctif », CLAUDE.md §5).

- **S3.2 (visuel, thread principal Godot)** : `CarrouselScene` entoure `Start()` d'un
  `try/catch (ModbusServerException)`. En cas d'échec : on **mémorise** l'état, on `GD.PrintErr`,
  et le `HealthHud` peint un **bandeau rouge non masquable**. La scène **ne plante pas** — elle
  s'affiche avec son message. Plus jamais de « figé muet ».

```
  502 occupé ──> Start() lève ──> scene catch ──> _serverFailed=true ──> HUD bandeau rouge
                                                       │
                                                       └─ garde en tête de _PhysicsProcess :
                                                          on ne rejoue pas la boucle Modbus
                                                          (le serveur n'existe pas)
```

**Piège rencontré** (décision imprévue) : `_ExitTree` disposait le serveur inconditionnellement.
Mais sur échec de bind, FluentModbus **n'a jamais démarré** → le disposer relançait une erreur à
la fermeture. Corrigé : on ne dispose **que si** le bind a réussi (`_serverFailed == false`). Le
pré-vol `TcpListener`, lui, relâche déjà son port dans son `finally`.

---

## 3. Savoir si le PLC parle vraiment (activité I/O Scanner)

Le heartbeat prouve que **nous** sommes vivants (on incrémente un mot toutes les 100 ms). Il ne
prouve **pas** que le M580 est là. Comment savoir qu'un client écrit réellement nos commandes ?

FluentModbus expose un événement **`RegistersChanged`** : la lib nous prévient quand un client
écrit un holding register (FC16). On s'y abonne et on **horodate** la dernière écriture reçue →
`LastClientWriteUtc`. Le HUD affiche « activité PLC il y a X ms ».

### 3.1 Le piège de l'I/O Scanner : il réécrit **la même valeur**

Subtilité Schneider importante : l'**I/O Scanner** réémet la ligne d'écriture `cmd` en boucle,
**même quand rien ne change** (le bit vaut toujours 1, il le réécrit quand même à chaque scan).
Or beaucoup de libs ne lèvent `Changed` que si la **valeur** change. Si c'était le cas ici, un PLC
présent mais « stable » aurait l'air **déconnecté** (plus aucun `Changed`).

Vérification faite : FluentModbus 5.3.2 a deux réglages —

- `EnableRaisingEvents` : active l'événement ;
- **`AlwaysRaiseChangedEvent = true`** : fait lever `Changed` sur **chaque FC16**, même si la
  valeur écrite est identique à l'ancienne.

Les deux armés → `LastClientWriteUtc` avance à chaque scan tant que le PLC parle. **Pas de repli,
pas de dette.** Un test dédié écrit deux fois la même valeur et vérifie que l'horodatage a avancé.

### 3.2 Thread-safety : deux threads, un timestamp

`LastClientWriteUtc` est **écrit par le thread du serveur** (celui qui reçoit le TCP) et **lu par
le thread principal Godot** (le HUD). Deux threads sur la même donnée → il faut protéger. Ici pas
besoin d'un gros verrou : un timestamp est une valeur simple, on utilise **`Interlocked`** (une
écriture/lecture atomique). C'est cohérent avec **Arch A** : le thread serveur **ne touche ni le
scene tree ni le datastore** — il ne fait qu'écrire un compteur atomique que le thread principal
lit. Aucune API Godot appelée hors du thread principal.

`ModbusDataStore.SnapshotReturns()` complète le tableau : c'est le **miroir** de
`SnapshotCommands()` (déjà là depuis le sprint 1). Il rend une **copie défensive sous verrou** de
la zone `ret` — le HUD peut lire l'état publié (heartbeat, fins de course, présences) **sans**
courir de risque avec le thread physique qui écrit au même moment.

---

## 4. Tracer la chaîne de commande **sur chaque élément** (S3.3)

Objectif : que l'automaticien **lise à l'écran**, collé à chaque pièce, la chaîne
`commande Modbus → état physique → retour Modbus`.

### 4.1 Étiquette texte : décoder %MW **depuis le pivot**, jamais en dur

Chaque élément (KM1 convoyeur, YV1/YV2 vérins, B1/B2 capteurs) porte un **`Label3D`** qui affiche
une ligne compacte :

```
   YV1 : cmd %MW100.0 -> tige sortie -> ret %MW200.1 (S12)
   └tag  └ bit qui pilote   └ physique   └ bit retour
```

**Règle d'or du projet honorée** : l'adresse `%MW100.0` **n'est jamais écrite en dur**. Elle est
**décodée du pivot** : chaque composant déclare ses `signals` (`{zone, word, bit}` + `tag`), et
`CommandChainLabels` calcule le `%MW` affiché via `Signal.AbsWord` / `Signal.Bit` (base de zone +
offset). Si demain on déplace la zone `cmd` dans le pivot, les étiquettes suivent **sans toucher au
code**. Le pivot reste la source de vérité — « le pivot d'abord », honoré jusque dans l'affichage.

**Piège de nommage** : Godot possède **son propre** type `Signal` (pour les signaux d'événements
Godot). Le nôtre est `CarrouselCore.Signal`. Collision de noms → on lève l'ambiguïté avec un alias
en tête de fichier : `using Signal = CarrouselCore.Signal;`.

### 4.2 Coloration d'état : le matériau **déjà posé**, muté

Le texte dit le *câblage* (statique). La **couleur** dit l'*état instantané* :

- tige de vérin **teintée** (dégradé repos → ambre) selon sa `Position` 0→1 ;
- **anneau** vert quand `KM1_AUX = 1` (convoyeur en marche) ;
- **fenêtre capteur** allumée verte quand `B1`/`B2 = 1` (palette présente).

On **réutilise les matériaux déjà créés par les builders** du sprint 1 (on mute leur
`AlbedoColor`) — pas de nouveaux nœuds, coût quasi nul. Les références aux matériaux (anneau,
fenêtres) sont **capturées une fois au build**, pas re-cherchées à chaque frame (`GetNode` par
frame = lent et fragile).

### 4.3 Basse cadence = texte lisible

Un `Label3D` rafraîchi à 60 Hz **scintille** (le texte se reconstruit trop vite). On rafraîchit
volontairement **à ~6 Hz** : un `_Process` avec un **accumulateur** (`LabelRefreshPeriodS = 0.15`)
ne recompose le texte que ~6 fois par seconde. Même patron d'accumulateur que la boucle physique
du sprint 2, mais pour la lisibilité au lieu du déterminisme.

Autres réglages de lisibilité : `Billboard` (l'étiquette fait **toujours face à la caméra**),
`NoDepthTest = true` (le texte n'est pas caché derrière la géométrie), **contour noir** (lisible
sur fond clair comme sombre), et des **hauteurs étagées** (KM1 haut, vérins mi-hauteur, capteurs
bas) pour que les étiquettes ne se **superposent** pas.

### 4.4 Le script de démo

`demo_sprint_03.ps1` reprend le rituel « un script de démo guidé par sprint » : **pré-vol du port
502** (refuse de tourner si un `SimHost` ou une scène squattent déjà le port — la leçon de D-013),
puis il **joue le M580** (via `io_scanner_sim.py`) en enchaînant des phases scénarisées, en
annonçant à chaque étape **quelle étiquette et quelle couleur regarder**. Nico valide **à l'œil**
sans retaper les commandes. ASCII pur (PowerShell 5.1).

---

## 5. Ce qui **n'a pas** changé (invariants tenus)

- **Pivot inchangé** : tout ce dont le HUD a besoin (`signals`, `tag`, bases de zone) y était
  depuis la Phase 0. On décode, on n'ajoute rien.
- **Comportement Modbus inchangé** : le banc reste à **95 verts** après S3.2 et S3.3 (glue lecture
  seule), et les **4 pytest full-chain** restent verts (la scène répond toujours pareil au PLC).
- **Arch A intacte** : le thread serveur ne touche que le datastore (et un timestamp atomique) ; le
  scene tree n'est modifié que sur le thread principal Godot.
- **Zéro écriture `cmd`** : le HUD lit, ne force rien. Le forçage est **reporté** (D-016).

## 6. Récapitulatif banc

| Étape | Banc `dotnet test` | Full-chain pytest | Build Godot |
|---|---|---|---|
| Ouverture | 90 (87 core + 3 serveur) | 4 verts | 0 erreur |
| Après S3.1 | **95** (89 core + 6 serveur) — *re-figé, +5 témoins santé* | 4 verts (inchangé) | 0 erreur |
| Après S3.2 | 95 (inchangé) | 4 verts | 0 erreur |
| Après S3.3 | 95 (inchangé) | 4 verts | 0 erreur |

Pas de .sln : les deux projets de test se lancent **séparément**.

## 7. Pièges à retenir pour la suite

1. **Un `Start()` peut mentir par le silence** — toujours vérifier *empiriquement* si un échec est
   synchrone avant de choisir try/catch vs pré-vol. Ici : sonde jetable, verdict, puis les deux.
2. **L'I/O Scanner réécrit à l'identique** — un détecteur d'activité qui n'écoute que les
   *changements de valeur* croira le PLC mort. `AlwaysRaiseChangedEvent` est indispensable.
3. **Godot a son propre `Signal`** — aliaser le nôtre pour éviter la collision.
4. **Ne pas disposer un serveur jamais démarré** — garder la libération conditionnelle à la réussite
   du bind.
5. **Un `Label3D` à 60 Hz scintille** — cadence basse + accumulateur, comme pour le tick physique.

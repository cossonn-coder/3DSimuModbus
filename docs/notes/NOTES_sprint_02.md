# NOTES pédagogiques — Sprint 02 (cinématique visuelle)

Décomposition des mécanismes clés introduits au sprint 2 : comment la maquette 3D **statique**
(sprint 1, brique 5) devient une maquette **animée**, pilotée en temps réel par la même chaîne
Modbus que `SimHost`. Public visé : quelqu'un qui découvre la boucle de jeu de Godot, le
découplage rythme-moteur / rythme-simulation, et la frontière de threads d'un serveur temps réel.

> Renumérotation : ce « sprint 2 » est l'ex-« sprint 3 » (cinématique visuelle). La 3D statique,
> initialement prévue en sprint 2, ayant été livrée en sprint 1 (brique 5), les sprints ont glissé.

---

## 1. Le problème central : deux horloges qui ne battent pas au même rythme

Godot appelle `_PhysicsProcess(delta)` à **60 Hz** par défaut (toutes les ~16,7 ms). Or le contrat
pivot impose un **heartbeat à 10 Hz** (`heartbeat.period_ms = 100`), et c'est la fonction `Tick` de
la simulation qui incrémente ce heartbeat. Si on appelait `Tick` à chaque frame, le heartbeat
monterait 6× trop vite — le M580 verrait une cadence fausse.

On a donc **deux horloges** :
- l'horloge du **moteur** (60 Hz, `delta` variable selon la charge machine) ;
- l'horloge de la **simulation** (10 Hz, pas fixe, déterministe).

Il faut les **découpler**. Le patron classique est l'**accumulateur à pas fixe**.

### L'accumulateur à pas fixe

```
_PhysicsProcess(delta):
    _accumulator += delta                       # on encaisse le temps réel écoulé
    while _accumulator >= periodS:              # tant qu'un « cran » de 100 ms est mûr…
        StepSim(periodS)                        #   …on avance la sim d'un pas FIXE
        _accumulator -= periodS
    ApplyToScene()                              # à CHAQUE frame : on reflète l'état courant
```

- `StepSim` reçoit **toujours** `dt = periodS` (100 ms), jamais le `delta` réel. La cinématique
  est donc **déterministe** : deux exécutions produisent la même trajectoire, indépendamment du
  framerate. Le heartbeat bat à 10 Hz exact.
- `ApplyToScene` tourne à **60 Hz** (chaque frame), mais ne fait que **recopier** l'état déjà
  calculé. À 60 Hz pour une sim à 10 Hz, on affiche 6 fois la même valeur entre deux ticks : c'est
  le **« snap »** (voir §3).
- À 60 Hz réels, `_accumulator` atteint `periodS` environ toutes les 6 frames → `StepSim` s'exécute
  ~1 fois toutes les 6 frames. Si une frame est longue (charge), l'accumulateur peut valoir 2-3
  crans d'un coup → la boucle `while` rattrape en plusieurs pas dans la même frame.

### Divergence assumée avec `SimHost`

`SimHost` (l'hôte console headless du sprint 1) avance la sim avec le **`dt` réel** mesuré par un
`Stopwatch`. `CarrouselScene` avance avec un **`dt` fixe**. Les deux sont légitimes :
- côté `SimHost`, on veut coller au temps mural (pas de rendu à cadencer) ;
- côté scène, on veut un heartbeat régulier **et** un rendu fluide → pas fixe + snap.

On n'a **pas** touché `SimHost` (modif chirurgicale). Une harmonisation éventuelle (pas fixe des
deux côtés) est hors périmètre.

---

## 2. La « spirale de la mort » et les deux garde-fous

Que se passe-t-il si une frame prend très longtemps (breakpoint, fenêtre gelée, pic de charge) ?
`delta` devient énorme → `_accumulator` accumule beaucoup de retard → la boucle `while` doit
exécuter des dizaines de `StepSim` pour rattraper → cette rafale rend **la frame suivante encore
plus longue** → l'accumulateur gonfle encore… C'est la **spirale de la mort** : le jeu se fige et,
ici, **le heartbeat part en rafale** (dizaines d'incréments d'un coup), ce que le M580 pourrait
mal interpréter.

Deux protections **ceinture + bretelles** :

```csharp
// 1. CLAMP : on ne mémorise jamais plus de retard qu'on ne peut rattraper.
_accumulator = Math.Min(_accumulator + delta, MaxCatchup * _periodS);

int ticks = 0;
// 2. GUARD : au plus MaxCatchup pas par frame ; le surplus est abandonné.
while (_accumulator >= _periodS && ticks < MaxCatchup)
{
    StepSim(_periodS);
    _accumulator -= _periodS;
    ticks++;
}
```

- Le **clamp** borne déjà l'accumulateur (donc le nombre de tours) : à lui seul il suffit.
- Le **guard** `ticks < MaxCatchup` est un filet redondant : si un refactor futur retire le clamp
  par mégarde, la boucle reste bornée. `MaxCatchup = 5` (rattrape jusqu'à 500 ms de retard, puis
  lâche le reste — mieux vaut un léger saut visuel qu'une frame figée).

### Le troisième garde-fou, invisible dans le code C# : `low_processor_mode`

Godot a un mode « économie » (`run/low_processor_mode`) où la boucle ne tourne **que sur événement
d'entrée** (souris, clavier). Dans ce mode, **sans interaction, `_PhysicsProcess` ne serait pas
appelé → le heartbeat gèlerait** alors même que le serveur répond encore. On a donc verrouillé
`run/low_processor_mode=false` **explicitement** dans `project.godot` (c'est déjà le défaut, mais on
le fige pour qu'un futur réglage ne casse pas la preuve de vie). *Résiduel connu* : l'OS peut
throttler une fenêtre **non focalisée** — non entièrement maîtrisable côté Godot (dette D-012).

---

## 3. Le mapping `ApplyToScene` : de l'état simulé aux transforms

`ApplyToScene` est de la **glue pure** : elle lit l'état exposé en lecture seule par `_sim` et le
recopie sur les nœuds construits en brique 5. **Rien de neuf n'est calculé** ; on reflète.

### Tiges de vérin — translation sur +Y

```csharp
_rod1.Position = new Vector3(_rod1.Position.X,
                             _restY1 + (float)_sim.Cylinder1.Position * _stroke1,
                             _rod1.Position.Z);
```

`Cylinder.Position` est un scalaire **0..1** (0 = rentré, 1 = sorti). On l'affine linéairement entre
`restY` (altitude de la tige rentrée, mémorisée au build) et `restY + stroke` (pleine extension,
`stroke_m = 0,15` du pivot). On ne touche **que** l'altitude ; `x`/`z` restent au centre du fût.

### Palettes — reposition sur le cercle via **le même helper** que la brique 5

```csharp
var angles = _sim.Pallets.AnglesDeg;               // ordre des indices = ordre de construction
_pallets[i].Position = OnCircle(angles[i], _palletRadius, _palletCenter, _palletY);
_pallets[i].RotationDegrees = new Vector3(0, (float)angles[i], 0);
```

Le point crucial : on réutilise **exactement** `OnCircle(...)` (le helper de la brique 5,
`x = cx + r·cosθ`, `z = cz − r·sinθ`) avec les **mêmes** `radius`/`center`/`y` mémorisés au build.
Résultat : **cohérence de repère garantie** — pas de risque qu'une palette animée « saute » par
rapport à sa position statique initiale, ni que le sens tourne à l'envers. Le sens **CCW vu de
dessus** et les postes (90° au fond, 270° devant) sont hérités sans recalcul.

### Pourquoi capturer les références **au build** (pas de `GetNode` par frame)

`ApplyToScene` tourne 60 fois par seconde. Un `GetNode("...")` par nœud et par frame serait une
recherche par chemin dans l'arbre à chaque appel. On mémorise donc `_rod1/_rod2` et `_pallets[]`
**une seule fois** dans les builders (`BuildCylinder`/`BuildPallet`), avec un **routage explicite
par `id` pivot** (`if (cyl.Id == "cylinder_1")`) : on garantit que `_rod1` correspond bien à
`Cylinder1` de la simulation, **indépendamment de l'ordre d'itération** du dictionnaire de
composants (qui n'est pas garanti en C#).

### Le « snap » 10 Hz (décision D-d) et sa steppiness

Comme `ApplyToScene` recopie l'état courant sans interpoler, la tige de vérin franchit sa course en
~5 paliers visibles (5 ticks sur les 500 ms de `travel_time_ms`). Jugé **acceptable pour une démo
automaticien** : la correction fonctionnelle prime. L'interpolation 60 Hz (lerp entre snapshot
`prev` et `current`) est **purement présentation** et ajoutable en V2 **sans toucher la sim** — d'où
« non-lock-in ». On ne l'a pas faite.

---

## 4. La frontière de threads (Arch A) — rien de neuf, mais il faut la garder en tête

`ModbusServer` écoute sur **son propre thread** (FluentModbus). `_PhysicsProcess` tourne sur le
**thread principal Godot** — le seul autorisé à toucher le scene tree (l'API Godot n'est pas
thread-safe). La règle d'or : **le thread serveur ne touche jamais le datastore ni la scène**, et le
thread principal est **le seul** à toucher le datastore.

`StepSim` respecte ça : `PullCommands()` et `PushReturns()` recopient le buffer FluentModbus ↔
datastore **sous `server.Lock`** (contrainte POC D-001), et `ApplyToScene` ne lit **que** l'état
déjà publié par `_sim` (jamais le datastore directement). C'est **exactement** l'enchaînement de
`SimHost`, volontairement **non factorisé** (2 sites seulement, le core ne peut voir ni le serveur
ni Godot — dette D-011 assumée si un 3ᵉ hôte apparaissait).

```
Thread serveur FluentModbus         Thread principal Godot (_PhysicsProcess)
───────────────────────────         ────────────────────────────────────────
sert FC3/FC16 sur son buffer        StepSim(periodS), sous server.Lock :
   ▲          │                       PullCommands() : fil → datastore
   │ server.Lock                      _sim.Tick(store, periodS)   (heartbeat, cinématique)
   └──────────┘                       PushReturns()  : datastore → fil
                                     ApplyToScene() : lit _sim (état publié) → transforms 3D
```

---

## 5. Le piège rencontré à la validation : **deux serveurs sur le port 502**

Symptôme observé : le client Python (`io_scanner_sim.py`) voyait un heartbeat vivant et `KM1_AUX`
réagir aux commandes — **mais la fenêtre Godot restait figée**, aucune palette ne bougeait.

Diagnostic : **deux processus écoutaient sur 502 en même temps** — un `SimHost` **reliquat** (lancé
pendant les vérifs des sous-agents) et la scène Godot. Le client tombait sur `SimHost` (le premier
lieur du port), qui simulait dans son coin **sans aucun rendu**, pendant que la scène Godot, dont le
`server.Start()` avait **échoué en silence** (port déjà pris), restait plantée sur sa géométrie
statique.

Deux leçons :
1. **Toujours vérifier qui tient 502 avant de lancer la scène.**
   ```powershell
   Get-NetTCPConnection -LocalPort 502 -State Listen |
     ForEach-Object { (Get-Process -Id $_.OwningProcess).ProcessName }
   ```
   Si ça affiche `SimHost` → `Stop-Process -Name SimHost -Force`, puis relancer la scène (F5).
2. **Le `server.Start()` de la scène avale l'échec sans le signaler à l'écran** (dette D-013) : pour
   une démo, un port déjà pris devrait être **visible** (log clair, voire bandeau), pas se traduire
   par une maquette figée sans explication.

C'est ce piège qui a motivé le pré-vol du script de démo (§6).

---

## 6. Outils de validation livrés

- **`runtime/scripts/smoke_anim.ps1`** — smoke-test **headless** : lance la scène en `--headless`
  (loopback + sonde `DiagAnim`), force `cmd_run=1` + `cmd_extend=1` via le client Modbus, tourne N
  frames, puis **asserte** sur la trace de la sonde que la tige a monté, qu'une palette a avancé, et
  que le heartbeat s'est incrémenté. Prouve **sans œil humain** que `ApplyToScene` reflète la sim.
  Solde la part automatisable de **D-010** (+ recensement statique brique 5).
- **`runtime/scripts/demo_sprint_02.ps1`** — démo **visuelle guidée** : joue le M580 et enchaîne
  automatiquement 6 phases (repos → convoyeur → blocage YV1 → rappel ressort → blocage YV2 → arrêt),
  en annonçant à chaque phase **ce qu'il faut regarder** dans la 3D. Pré-vol du port 502 intégré
  (refuse SimHost ou scène non lancée). C'est l'outil qui a servi à la **validation visuelle**
  (rotation CCW, postes, extension/rappel de tige, accumulation) — la part humaine de D-010.

### Points d'encodage PowerShell (rappel)

Les `.ps1` sont écrits en **ASCII pur** (pas d'accents ni de tirets longs) : Windows PowerShell 5.1
lit un `.ps1` sans BOM en Windows-1252, et un caractère multi-octet dans une chaîne casserait le
parseur. `Start-Process` a un quirk : sans lire `.Handle` juste après le lancement, `.ExitCode`
reste `null` après la sortie — d'où le `$null = $proc.Handle` dans le smoke.

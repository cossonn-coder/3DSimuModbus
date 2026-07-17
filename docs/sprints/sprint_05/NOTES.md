# Sprint 05 « Injection de défauts » — NOTES pédagogiques

> Public : quelqu'un qui découvre le projet. On explique **le pourquoi** et **les
> mécanismes** des cinq sous-sprints, avec les pièges rencontrés. Les identifiants de
> code sont en anglais, le raisonnement en français (convention projet).

## 0. L'idée en une phrase

On veut **fabriquer des pannes depuis l'IHM** — un vérin qui reste coincé, un capteur qui
ment, la communication qui tombe — pour **éprouver le programme de l'automate M580** face à
des situations non nominales. La contrainte cardinale : **on n'écrit jamais un mot Modbus à
la main**. Un défaut n'est pas une décision métier, c'est un **état physique forcé** ; la
simulation reste seule maîtresse des mots `ret` qu'elle publie. C'est le respect de
l'invariant « l'application **sert**, elle ne **décide** pas » (Arch A).

```
        IHM (souris/clavier)                  Simulation pure (CarrouselCore)
   ┌─────────────────────────┐        ┌──────────────────────────────────────┐
   │ menu "Défaut" par ligne │  ───►  │ FaultSet : quel élément, quel mode ?  │
   │ touches B / R / F / []  │        │                                       │
   └─────────────────────────┘        │  Tick() applique le défaut à la       │
                                       │  cinématique → les mots ret en        │
                                       │  DÉCOULENT (jamais forcés au datastore)│
                                       └──────────────────────────────────────┘
                                                        │
                                                        ▼  FC3 (lecture ret)
                                              M580 réel via I/O Scanner
```

Rien de tout cela n'est spécifique au carrousel : un défaut se déclare **par type** de
composant (`FaultCatalog.ApplicableTo`), jamais par un id codé en dur — c'est la généricité
« par le pivot » exigée par `CLAUDE.md` §0bis, qui survivra quand la machine sera générée
depuis un DWG inconnu.

---

## 1. S5.1 — Le cœur pur : où vit un défaut ?

### Le modèle de données

Trois façons de tomber en panne, réunies dans un unique `FaultSet` (objet C# pur, **zéro
Godot**, donc testable en xUnit sans lancer le moteur) :

| Famille | Porté par | Exemple |
|---|---|---|
| **Physique** | un **composant** (vérin, convoyeur) | vérin qui ne sort pas / coincé mi-course ; convoyeur qui patine |
| **Capteur-bloqué** | un **signal** `ret` TOR | S12 forcé à 1 alors que la tige est basse (capteur menteur) |
| **Gel `ret`** | la **table entière** (`RetFrozen`) | plus aucun retour publié, heartbeat compris |

Le choix de granularité mérite une explication : « KM1_AUX collé » ne se modélise pas comme
un défaut physique du convoyeur, mais comme un **capteur-bloqué générique sur le bit
`ret_running`**. Un seul mécanisme (« ce bit de retour ment ») couvre S11/S12/S21/S22,
B1/B2 **et** KM1_AUX — c'est plus générique que N défauts spécifiques.

### L'ordre d'application dans `Tick`

Le défaut s'applique **en tête du tick physique**, avant que la cinématique ne calcule
quoi que ce soit, sauf le masque capteur qui vient **après l'encodage** :

```
Tick(store, dt):
    snapshot des commandes cmd        ← comme d'habitude (cohérence intra-scan)
    ─── application des défauts physiques ───
    vérin "ne sort pas"   → la commande effective devient FAUX (il ne bougera pas)
    vérin "coincé"        → sa position est GELÉE (pas d'Advance) là où il en était
    convoyeur "patine"    → palettes figées, MAIS KM1_AUX reste le reflet de la commande
    ─── cinématique normale (vérins, palettes, heartbeat) ───
    ─── encodage des signaux ret ───
    capteur-bloqué        → on force le bit APRÈS l'encodage (0 ou 1)
    ─── publication ───
    si RetFrozen          → on NE publie RIEN et on N'incrémente PAS le heartbeat
```

**Piège subtil — le convoyeur qui patine.** Un vrai patinage mécanique, c'est le moteur qui
tourne (le contacteur KM1 est collé, donc **KM1_AUX = 1**) mais la charge qui n'avance pas.
Si on avait bêtement « arrêté le convoyeur », KM1_AUX serait tombé à 0 et l'automate aurait
vu un arrêt normal, pas une anomalie. On veut au contraire que **le PLC voie une incohérence
diagnostiquable** : « je commande la marche, KM1_AUX confirme la marche, mais les présences
B1/B2 ne changent jamais ». D'où : palettes figées **et** KM1_AUX intact.

**Piège capteur — pourquoi masquer *après* l'encodage ?** Parce qu'on ne doit **jamais**
écrire dans le datastore (ce serait « forcer un mot Modbus », interdit, et ça casserait
l'invariant D-016). La cinématique calcule le bit vrai, puis on le **remplace** au vol dans
la valeur encodée. Le datastore ne connaît que le résultat ; personne n'a « écrit un mot ».

### Preuve : défaut inactif = nominal

Le test décisif : un `FaultSet` vide doit produire **exactement** l'ancien comportement. Le
banc xUnit a été **re-figé 89 → 109** (+20 scénarios de défaut sur les modèles purs), et
les 89 tests antérieurs restent verts — donc les **4 pytest full-chain** (qui vérifient la
chaîne nominale de bout en bout) sont non impactés. C'est le garde-fou qui garantit qu'on a
ajouté une capacité **sans dégrader** l'existant.

---

## 2. S5.2 — Sélectionner un élément (et le piège émission ≠ albédo)

Avant d'injecter un défaut « sur un élément », il faut **désigner** cet élément. On
introduit une **sélection persistante**, distincte du **survol** (hover) déjà en place
depuis le sprint 4.

Le mécanisme clé, hérité de S4.3 et étendu ici : **l'état d'un composant peint son
`AlbedoColor`, la mise en évidence pilote son canal d'émission** (`Emission`). Les deux
canaux ne se marchent pas dessus. On résout la couleur d'émission par **priorité** :

```
sélection (cyan)  >  survol (bleu)  >  repos (émission nulle)
```

Ainsi un vérin sélectionné **puis** survolé reste cyan, et **aucune couleur d'état** (tige
ambre, anneau vert, fenêtre capteur) n'est perdue quand le survol s'en va — on ne fait que
moduler l'émission, jamais l'albédo. La symétrie 3D↔panneau est **garantie par
construction** : le clic 3D et le clic sur la ligne du panneau appellent le **même**
`SetSelected(id)` (source unique).

Détails d'entrée : clic gauche **relâché** → sélectionne l'élément survolé ; clic dans le
vide → désélection ; `]` / `[` cyclent les éléments **dans l'ordre du pivot**
(`Components.Keys`), jamais un ordre codé en dur.

**Au passage, la dette D-018 est soldée.** Le sprint 4 avait laissé cinq champs de nœuds 3D
vestigiaux (`_ringNode`, `_cyl1Node`…) : assignés mais jamais relus, parce que le picking
avait finalement été posé sur les nœuds locaux des builders (approche plus générique). Comme
S5.2 retouche `CarrouselScene.cs`, on en profite pour les retirer proprement (règle : on
solde une dette quand on est **déjà** dans le fichier).

---

## 3. S5.3 — Le menu de défauts, le marquage et le mode aveugle

### Un menu par ligne, peuplé du catalogue

Le panneau des éléments gagne une **6ᵉ colonne « Défaut »** et, sur chaque ligne, un
`MenuButton`. Le piège d'ergonomie : le contenu du menu **dépend du type** de l'élément
(un vérin n'a pas les mêmes modes de panne qu'un capteur). On peuple donc le popup
**à l'ouverture** (`AboutToPopup`) en interrogeant `FaultCatalog.ApplicableTo(component)`,
plus une entrée « Réparer » si un défaut est déjà actif. Le choix (`IndexPressed`) est
relayé à `CarrouselScene.OnFault` — **la première écriture IHM → sim** du projet, exécutée
sur le **thread principal** (jamais le thread serveur : Arch A tenue).

**Piège d'index.** On a délibérément **évité un séparateur** avant « Réparer » : un
`AddSeparator` décale les indices que renvoie `IndexPressed`, ce qui obligerait à un mapping
fragile. Simplicité d'abord.

### Marquage 3D et badge

Un élément en défaut passe en **rouge** — nouvelle priorité en tête du résolveur
d'émission :

```
défaut (rouge)  >  sélection (cyan)  >  survol (bleu)  >  repos
```

et la colonne « Défaut » du panneau affiche le libellé courant (mapping FR unique
`FaultCommandLabel`). L'opérateur voit d'un coup d'œil **quoi** est en panne et **comment**.

### Le mode aveugle (`B`)

Pourquoi une touche pour **cacher** ce qu'on vient de rendre visible ? Parce que la valeur
pédagogique du démonstrateur, c'est de montrer que **le programme automate doit détecter la
panne tout seul**, sans indice visuel. En mode aveugle, le rouge 3D et la colonne « Défaut »
disparaissent (un bandeau « MODE AVEUGLE » le rappelle), **mais la simulation reste tout
aussi anormale**. On teste alors le diagnostic du M580, pas l'œil de l'opérateur.

---

## 4. S5.4 — Couper la communication (le sous-sprint sensible)

Deux façons de « couper la comm », volontairement distinctes car elles produisent des
symptômes différents côté M580 :

| Mode | Ce qui se passe | Ce que voit l'I/O Scanner |
|---|---|---|
| **Gel `ret`** | on cesse de publier les retours et d'incrémenter le heartbeat ; **le TCP reste vivant** | la connexion tient, mais le **heartbeat est figé** → le PLC détecte un « simulateur muet » |
| **Déconnexion TCP** | le serveur Modbus **ferme sa socket** (`Stop()`) | l'esclave **disparaît** → défaut de scrutation franc |

### Le point dur : réarmer FluentModbus après `Stop()`

La grande inconnue du sprint : un `ModbusTcpServer` (FluentModbus 5.3.2) **survit-il** à un
`Stop()` suivi d'un `Start()` ? Faut-il recréer l'instance et redéclarer l'unité
(`AddUnit`) ? On a tranché **par un spike** (un test jetable in-process, avant d'écrire le
code de prod) plutôt qu'en devinant.

**Verdict empirique : le serveur se réarme sur la MÊME instance.** `Stop()` libère le port,
`Start(endpoint)` le ré-ouvre, **l'unité et son buffer sont conservés** (un client se
reconnecte et relit la dernière valeur publiée), **`AddUnit` est inutile à ré-armer**. D'où
un design minimal :

```
Disconnect()  = Stop()                 (idempotent, IsListening = false)
Reconnect()   = StartListening()       (idempotent, peut lever ModbusServerException)
StartListening() = pré-vol TcpListener + _server.Start   ← factorisé, partagé avec Start()
PullCommands / PushReturns : no-op si !IsListening        ← garde anti-crash
```

Le `StartListening()` **conserve le pré-vol `TcpListener`** hérité de D-013 : un re-bind qui
échoue (port repris entre-temps) reste **visible** (bandeau rouge remonté à l'UI), jamais
avalé silencieusement. « Réparer » la comm = simplement `Reconnect()`.

Le garde `if (!IsListening) return;` sur `PullCommands`/`PushReturns` est essentiel : il
empêche d'appeler `GetHoldingRegisters` sur un serveur arrêté (qui planterait), **tout en
laissant `StepSim` tourner** (physique + heartbeat interne continuent). C'est ce qui produit
l'**incohérence 3D/PLC assumée** :

> Pendant la coupure, la **3D continue de s'animer** (les palettes tournent, les vérins
> bougent) alors que le PLC voit une table **figée** ou un **esclave absent**. Ce n'est
> **pas un bug** : c'est précisément le message pédagogique — « la réalité physique et
> l'image qu'en a l'automate viennent de diverger ».

Côté UI, le `HealthHud` pilote désormais serveur **et** `Faults` (toujours sans toucher le
datastore ni le scene tree — Arch A). Les deux `CheckButton` (« Geler retours », « Couper
TCP ») sont **toujours visibles**, y compris quand le reste du HUD est masqué : une panne de
comm ne se cache pas. Une reconnexion échouée repasse le bouton en « coupé » sans émettre de
signal parasite (`SetPressedNoSignal`).

Banc serveur **re-figé 6 → 10** (+4 : l'écoute suit Disconnect/Reconnect, idempotence,
Pull/Push hors écoute sûrs, bout-en-bout perte→reconnexion). Le banc **core reste à 109**.

---

## 5. S5.5 — La démo guidée

`demo_sprint_05.ps1` (script **seul**, aucun code de prod touché) enchaîne **8 phases** en
jouant le M580 via `io_scanner_sim.py` et en affichant les retours pour corréler la 3D et la
table Modbus. Comme ses prédécesseurs : pré-vol du port 502 (refuse si un `SimHost` reliquat
tient le port, ou si rien n'écoute), ASCII pur, PowerShell 5.1.

Nouveauté utile : des **pauses de préparation décomptées** (non bloquantes, pas de
`Read-Host`) laissent à Nico le temps d'injecter ou de réparer un défaut dans l'IHM au bon
moment. Détail cinématique subtil intégré au script : le défaut « coincé mi-course » doit
être injecté **pendant** le scan (il gèle la tige à sa position **courante**) ; injecté à
l'arrêt, la tige serait déjà rentrée et « coincé » ne se verrait pas.

Parcours : nominal → vérin ne sort pas → coincé mi-course → capteur menteur → convoyeur qui
patine → gel des retours → coupure/réparation TCP → mode aveugle.

---

## 6. Ce qui reste à valider à la main (Nico)

Le banc automatisé prouve la **logique** (core 109 + serveur 10 = 119 verts) et le **build**
(0 erreur). Il ne peut **pas** prouver le rendu 3D ni le picking souris — cela reste une
**validation visuelle** :

1. Lancer la scène Godot (**F5**), puis dans un autre terminal
   `powershell -File runtime/scripts/demo_sprint_05.ps1`.
2. Pour chaque phase, injecter/réparer le défaut annoncé et vérifier la **corrélation
   3D ↔ retours PLC** : marquage rouge, effet cinématique attendu, colonne « Défaut ».
3. Vérifier la sélection (clic 3D ↔ ligne, cyan), le cyclage `]`/`[`, le mode aveugle `B`
   (rouge et colonne masqués, sim toujours anormale), la coupure TCP (l'`io_scanner` perd
   puis retrouve la connexion) et le gel des retours (heartbeat figé, 3D vivante).
4. Les **4 pytest full-chain** ne se valident qu'avec la scène à l'écoute sur 502 (sinon ils
   skippent) — validation manuelle également.

---

## 7. Récapitulatif des invariants tenus

- **Aucun mot Modbus forcé** : un défaut vit dans la sim pure ; les `ret` en découlent
  (capteur masqué **après** encodage, jamais au datastore). Invariant D-016 respecté.
- **Arch A** : le thread serveur ne touche ni datastore ni scene tree ; l'IHM écrit dans
  `Faults` sur le thread principal ; snapshot en début de tick, publication en fin.
- **Généricité par le pivot** : défauts déclarés **par type** (`FaultCatalog`), sélection et
  menu peuplés depuis `Components`, adresses `%MW` toujours décodées, jamais en dur.
- **Pivot inchangé**, boucle 10 Hz intacte, déterminisme préservé.
- **Banc** : core 89 → 109 (S5.1, prévu), serveur 6 → 10 (S5.4, prévu), inchangé ailleurs.

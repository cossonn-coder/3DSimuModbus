# Amorce de conception — Sprint 05 « Injection de défauts »

> **Usage** : lancer `/conception docs/sprints/sprint_05/amorce_conception.md` (ce fichier
> est le $ARGUMENTS). Intention, retours du sprint 4, priorisation arbitrée avec Nico
> (2026-07-17), périmètre proposé et points durs anticipés. La phase A part d'ici ; rien
> n'est figé tant que Nico n'a pas tranché les QCM.

---

## 1. Retours de validation du sprint 4 — à reporter au journal

- [ ] `demo_sprint_04.ps1` déroulé — **CONFIRMé CONFORME PAR NICO** (validation visuelle très bonne).
- ℹ **Contexte nouveau (2026-07-17)** : la **campagne M580 réelle (Phase 4) démarre
  aujourd'hui** avec l'automaticien. Ce sprint est conçu pour la **nourrir** (éprouver le
  programme PLC face aux défauts), jamais pour la bloquer — les deux avancent en parallèle.

## 2. Dettes concernées (arbitrage Nico 2026-07-17)

| Dette | Décision |
|---|---|
| **D-017** (injection de défauts) | **Devient le sprint 5.** Inversion assumée de l'ordre du 2026-07-16 (qui mettait le forçage D-016 avant) : forcer les **mots** se bat des deux côtés (le M580 réécrit toute la zone `cmd` à chaque scan ; `PushReturns` réécrit `ret` à chaque tick), alors que forcer **l'état physique** compose proprement avec les deux — et c'est ce qui sert la campagne M580. |
| **D-016** (forçage des mots + édition in-app) | Reportée (sprint ultérieur). Le mécanisme UI de ce sprint (contrôles par ligne du tableau) doit **préparer** sans hypothéquer : le forçage réutilisera la même entrée. |
| **D-018** (nœuds vestigiaux `CarrouselScene`) | **À solder au passage** : ce sprint touche `CarrouselScene.cs`, condition de remboursement remplie (5 champs + assignations, aucune logique n'en dépend). |
| D-002/D-003/D-005/D-009/D-011/D-012/D-014, D-015 (vitesse) | Inchangées, hors périmètre. |

## 3. Périmètre proposé — Sprint 05 « Injection de défauts »

**Intention unique** : pouvoir forcer, depuis l'IHM, un état de défaillance **physique ou
de comm** par élément, pour éprouver le programme automate — sans jamais écrire un mot
Modbus directement.

### 3.1 Dans le périmètre

1. **Modèle de défauts dans la sim pure** (`CarrouselCore`, zéro Godot, testable xUnit).
   Le défaut vit côté simulation, les mots `ret` en découlent naturellement. Modes V1,
   définis **par type de composant** (générique, rien de spécifique carrousel) :
   - **Capteur bloqué à 0 / bloqué à 1** (S11/S12/S21/S22, B1/B2) ;
   - **Vérin défaillant** : ne sort pas / coincé mi-course (via `CylinderState`) ;
   - **Convoyeur défaillant** : patine (palettes immobiles, KM1_AUX normal) et/ou
     KM1_AUX ne retombe pas — variantes exactes à trancher en phase A ;
   - **Coupure des retours** : `ret` figé (heartbeat immobile). Sémantique à instruire
     (point dur §4.1) — candidat au **dernier sous-sprint**, isolable.
2. **Sélection d'élément au clic**, symétrique comme le survol S4.3 : clic sur l'élément
   3D **ou** sur sa ligne du tableau latéral → même sélection (source unique, patron
   `SetHover` → `SetSelected`).
3. **Menu déroulant des défauts applicables** au type de l'élément sélectionné :
   déclencher / réparer. Indication claire d'un défaut actif (marquage 3D + colonne ou
   badge dans le tableau).
4. **Solde D-018** (retrait des 5 champs vestigiaux, passage déjà justifié).
5. **Sortie observable** : `demo_sprint_05.ps1` — `io_scanner_sim.py` joue le M580 pendant
   que le script guide l'injection (« bloque S12 à 0, regarde le PLC détecter le défaut
   fin de course »), banc `dotnet test` étendu (scénarios de défaut sur modèles purs) +
   4 pytest full-chain **inchangés**.

### 3.2 HORS périmètre (NE PAS FAIRE)

- **Aucune écriture de mots Modbus** depuis l'IHM (`cmd` comme `ret`) : le défaut force
  l'état physique, la publication `ret` reste le fait exclusif de la sim (D-016 reportée).
- Pas de catalogue/édition d'éléments, pas de mapping depuis l'UI (sprint 6+).
- Pas de vitesse de simulation réglable (reste de D-015).
- **Pivot JSON inchangé** (modes par type portés par le runtime en V1 — voir §5 Q3) ;
  Arch A intacte ; boucle à pas fixe 10 Hz intacte.

## 4. Points durs / incertitudes anticipés (à instruire en phase A)

1. **Sémantique « coupure des retours »** : geler les valeurs `ret` (TCP vivant, données
   figées → le PLC doit détecter via le heartbeat) ≠ couper la connexion (l'I/O Scanner
   passe en défaut de scrutation, health bits). Les deux sont des tests légitimes du PLC
   mais des mécanismes très différents (le second touche `ModbusServer`). Trancher lequel
   (ou les deux) — rattaché à D-017.
2. **Où vit l'état « défaillant »** : dans les modèles purs quand le défaut est physique
   (vérin coincé = clamp de `Position`) vs masque à la publication pour les capteurs
   (bloqué = bit forcé après calcul). Reco à instruire : rester dans la sim pure dans les
   deux cas pour la testabilité ; interdire tout masque au niveau datastore (sinon on
   recrée le forçage de mots par la fenêtre).
3. **Fin du « lecture seule » côté IHM** : premier sprint où l'UI modifie l'état de la
   sim. Pas de traversée de threads (UI et tick sim sont tous deux sur le thread
   principal), mais le point d'application doit être défini (flag consommé en début de
   tick, comme le snapshot des commandes) pour préserver le déterminisme.
4. **Invariant « l'app sert et ne décide pas »** : un défaut n'est pas une décision
   métier, c'est un état physique forcé **par l'humain**. Aucun déclenchement automatique
   ou scénarisé en V1.
5. **Généricité (§0bis)** : applicabilité des modes déduite du **type** de composant du
   pivot (cylinder/sensor/conveyor), jamais des id carrousel. Le mécanisme doit survivre
   à une machine générée d'un DWG inconnu.

## 5. Questions pressenties pour les QCM de phase A (sans les trancher)

- **Q1** — Coupure comm : gel des valeurs `ret`, déconnexion TCP, ou les deux ? Et si gel :
  heartbeat inclus ou séparable ?
- **Q2** — Entrées utilisateur : souris seule (clic 3D/ligne + menu) ou aussi raccourcis
  clavier (cycler les éléments, déclencher/réparer) ? Nico a exprimé de l'intérêt pour les
  deux — trancher le V1.
- **Q3** — Les modes de défaut doivent-ils un jour être déclarés dans le **pivot**
  (paramétrables par machine) ? Reco pressentie : non en V1 (par type, dans le runtime),
  consigner l'ouverture.
- **Q4** — Visibilité du défaut : toujours marqué à l'écran, ou **mode « à l'aveugle »**
  (défaut masqué) pour tester le diagnostic de l'automaticien sans indice ?

## 6. Rappels d'invariants (CLAUDE.md) applicables

- Aucune adresse Modbus en dur : tout décodage via `Signal.AbsWord/.Bit`.
- Le thread serveur ne touche ni scene tree ni datastore ; seul le thread physique accède
  au datastore (Arch A) — ce sprint n'y change rien.
- Cinématique déterministe, paramètres du pivot, pas de moteur physique.
- Modifications chirurgicales : D-018 se solde parce que le fichier est touché, rien
  d'autre n'est « amélioré » au passage.
- Amorces autosuffisantes avec DÉPENDANCES et FICHIERS TOUCHÉS au découpage
  (`CarrouselCore` sim / `ElementPanel`+`CarrouselScene` UI / démo : probablement
  séquençables en 3 sous-sprints, le modèle pur d'abord).

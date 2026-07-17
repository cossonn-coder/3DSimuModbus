# Amorce de conception — Sprint 06 « Forçage de debug »

> **Usage** : lancer `/conception docs/sprints/sprint_06/amorce_conception.md` (ce fichier
> est le $ARGUMENTS). Intention, retours du sprint 5, priorisation arbitrée avec Nico
> (2026-07-18), périmètre proposé et points durs anticipés. La phase A part d'ici ; rien
> n'est figé tant que Nico n'a pas tranché les QCM.

---

## 1. Retours de validation du sprint 5 — à reporter au journal

- [ ] `demo_sprint_05.ps1` déroulé — **À CONFIRMER PAR NICO** (validation visuelle notée
  « restante » au backlog). Tout rejet UX s'instruit en phase A avant code.
- [ ] **Premiers retours de la campagne M580 (Phase 4, démarrée le 2026-07-17)** — latence
  scan↔sim, comportement I/O Scanner réel, besoins de l'automaticien. À consigner dès
  disponibles ; s'ils contredisent un choix de cette amorce, la phase A arbitre.
- ℹ Échange 2026-07-17 (catastrophes mécaniques) : en réalité, des palettes qui poussent
  sur un bloqueur engagé = **mode nominal** d'un convoyeur à accumulation (entraînement à
  friction conçu pour glisser). Pas d'animation de catastrophe (contraire à l'invariant
  « pas de moteur physique », non générique, zéro observable Modbus). En revanche le défaut
  **« bloqueur inefficace »** (S12=1 mais ne retient plus → la palette file) est réaliste,
  petit et à forte signature PLC → **brique 1 de ce sprint** (§3.1).

## 2. Dettes concernées (arbitrage Nico 2026-07-18)

| Dette | Décision |
|---|---|
| **D-016 (volet forçage)** | **Devient le sprint 6** : forcer les commandes depuis l'IHM pour piloter/déboguer sans PLC — et composer avec le PLC présent. Le volet **édition/catalogue** de D-016 reste reporté (sprint ultérieur, réveille D-005). |
| D-017 (soldée) | Le sprint réutilise ses acquis : sélection, `MenuButton`/colonne par ligne (D-Q5 : « entrée réutilisée par D-016 »), patron `FaultSet` pour le cœur pur. Extension : `BlockerIneffective` au catalogue. |
| D-015 (vitesse réglable), diff canonique Python↔C#, polish | Hors périmètre, inchangées. |

## 3. Périmètre proposé — Sprint 06 « Forçage de debug »

**Intention unique** : forcer depuis l'IHM la valeur **effective** d'un signal de commande
(KM1 run, YV1, YV2) — pour piloter la machine **sans PLC** et pour forcer **malgré** le
PLC — sans jamais casser l'écriture du scanner ni écrire un mot `ret`.

### 3.1 Dans le périmètre

1. **`BlockerIneffective`** (brique 1, petite) : nouveau mode physique vérin au
   `FaultCatalog` — la tige monte, S12=1, mais le poste est **exclu** de
   `CollectBlockedStations` → les palettes traversent une tige levée. Signature PLC :
   « je crois bloquer, B1 se libère quand même ». Banc re-figé (+ cas xUnit).
2. **Cœur pur du forçage** (`CarrouselCore`, patron `FaultSet`) : masque de forçage
   **par signal `cmd`** (forcé à 0 / forcé à 1 / auto), appliqué **après
   `SnapshotCommands` en tête de `Tick`** — symétrique du masque capteur S5.1 (appliqué
   après encodage). **Jamais d'écriture au datastore** : le PLC continue d'écrire `cmd`,
   la sim substitue à la lecture. Reco à instruire : ce seul mécanisme couvre AUSSI le
   pilotage sans PLC (personne n'écrit `cmd` → mots à 0 → forcer à 1 = piloter). Testable
   xUnit, défaut inactif = nominal.
3. **UI par ligne** : contrôle de forçage sur les lignes à signal `cmd` (réutilise
   l'entrée D-Q5), marquage visuel **distinct du défaut** (convention Control Expert :
   le forçage se signale, couleur/badge dédiés), affichage de l'écart **cmd écrite par le
   PLC vs cmd effective** quand un forçage est actif.
4. **Sortie observable** : `demo_sprint_06.ps1` — phases **sans** io_scanner (pilotage
   pur IHM : démarrer le convoyeur, sortir YV1, à la souris) puis **avec** scan (le
   forçage gagne sur le PLC, l'écart est visible ; le PLC voit KM1_AUX=1 sans l'avoir
   commandé). Banc `dotnet test` étendu + 4 pytest full-chain **inchangés**.

### 3.2 HORS périmètre (NE PAS FAIRE)

- **Aucune écriture des mots `ret`** (la sim reste seule maîtresse — invariant tenu au
  sprint 5, non négociable).
- Pas d'édition/catalogue d'éléments ni de mapping depuis l'UI (reste de D-016).
- Pas de vitesse de simulation réglable (reste D-015).
- Pas de forçage des **analogiques** (aucun dans le carrousel ; le mécanisme par bit
  suffit en V1 — l'extension mot entier attendra une machine qui en a).
- Pivot inchangé ; Arch A intacte ; boucle 10 Hz intacte.

## 4. Points durs / incertitudes anticipés (à instruire en phase A)

1. **Forçage vs I/O Scanner** (le point dur historique, amorce S4 §5.1) : le M580 réécrit
   toute la zone `cmd` à chaque scan. Le masque **à la lecture** (post-snapshot) y est
   insensible par construction — vérifier qu'aucun besoin réel n'exige d'écrire le
   datastore (mode « PLC absent » explicite) ; si oui, le cadrer comme cas dégradé séparé.
2. **KM1_AUX sous forçage** : le retour de marche recopie la commande **effective**
   (physique) → le PLC verra KM1_AUX=1 sans avoir commandé la marche. C'est cohérent
   (marche forcée localement = détectable) mais à assumer explicitement — l'automaticien
   doit le savoir pour ses diagnostics de vraisemblance.
3. **Composition forçage × défauts** : forcer YV1=1 avec un défaut « ne sort pas » actif
   → ordre d'application à figer (forçage cmd → défaut physique → masque capteur) et à
   documenter dans les NOTES ; le résultat doit rester déterministe et testé.
4. **Lisibilité des marquages** : la pile visuelle s'allonge (défaut rouge > sélection
   cyan > survol bleu, + forçage). Choisir un signalement qui ne se confond ni avec le
   défaut ni avec l'état — vocabulaire et convention Control Expert (variable forcée) à
   respecter.
5. **Généricité (§0bis)** : « forçable » = tout signal de zone `cmd` déclaré au pivot,
   jamais une liste d'id carrousel.

## 5. Questions pressenties pour les QCM de phase A (sans les trancher)

- **Q1** — Mécanisme unique (masque à la lecture, couvre aussi le pilotage sans PLC) ou
  mode « PLC absent » distinct avec écriture datastore ?
- **Q2** — UI du forçage : 3 états par ligne (forcé 0 / forcé 1 / auto) via MenuButton
  existant, ou toggles dédiés ? Et raccourcis clavier ?
- **Q3** — Affichage de l'écart PLC/effectif : deux valeurs côte à côte dans la colonne
  cmd, ou badge + tooltip ?
- **Q4** — `BlockerIneffective` : marquage 3D spécifique (tige levée mais inefficace) ou
  le marquage défaut standard suffit-il ?

## 6. Rappels d'invariants (CLAUDE.md) applicables

- « L'app sert et ne décide pas » : un forçage est une action **humaine** explicite,
  comme un défaut — jamais automatique, jamais scénarisé.
- Aucune adresse Modbus en dur (`Signal.AbsWord/.Bit`) ; vocabulaire automaticien
  (forçage au sens Control Expert, repères KM/YV/S/B).
- Thread principal seul pour IHM→sim (patron `OnFault` S5.3) ; le thread serveur ne
  touche ni scene tree ni datastore (Arch A).
- Modifications chirurgicales ; commentaires pédagogiques en français ; amorces
  autosuffisantes avec DÉPENDANCES et FICHIERS TOUCHÉS au découpage (pressenti :
  S6.1 BlockerIneffective + cœur forçage [core] → S6.2 UI [scène/panneau] → S6.3 démo).

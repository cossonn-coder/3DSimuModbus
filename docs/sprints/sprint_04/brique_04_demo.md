# Amorce S4.4 — Démo guidée + sortie observable

> **Cold-start** : lire `CLAUDE.md` (dont §0bis) + `docs/sprints/sprint_04/00_etat.md` + cette amorce.
> Sous-sprint **4/4** (dernier). **Dépend de : S4.1 + S4.2 + S4.3**. **Sortie observable** (règle
> permanente du skill : le sprint n'est complet qu'avec une scène animée + un script de démo).

## Objectif

Livrer `runtime/scripts/demo_sprint_04.ps1` : un script qui **joue le M580** (via
`testbench/io_scanner_sim.py`) et **guide Nico à la voix** (console) pour éprouver les nouveautés du
sprint — **navigation caméra**, **panneau des éléments**, **surbrillance croisée** — pendant que la
machine s'anime. Re-montre au passage le bandeau D-013.

## Contexte (déjà en place)

- `runtime/scripts/demo_sprint_03.ps1` : **patron à reprendre**. Structure : `param($PyHost, $Port,
  [switch]$Repeat)`, résolution de l'arborescence (`scripts/ → runtime/ → repo`, `testbench/` à côté),
  résolution de l'interpréteur (`python` sinon `py`), **pré-vol du port 502** (refuse de tourner si ce
  n'est pas la scène Godot qui écoute → évite de reproduire D-013 par accident), puis des **phases
  guidées** qui écrivent des commandes FC16 via `io_scanner_sim.py` et annoncent quoi regarder.
- `testbench/io_scanner_sim.py` : émule l'I/O Scanner (écrit `cmd`, lit `ret`). Interface identique à
  celle utilisée par `demo_sprint_03.ps1`.
- Contraintes d'encodage : **ASCII pur** (Windows PowerShell 5.1 lit les .ps1 sans BOM en
  Windows-1252 — pas d'accents ni de tirets longs). Ne **pas** mettre `$ErrorActionPreference='Stop'`
  (le stderr d'io_scanner tant que le serveur n'écoute pas deviendrait fatal).

## Contrat visé (contenu du script)

Reprendre l'ossature de `demo_sprint_03.ps1` (pré-vol + phases + `-Repeat`), avec des **phases
adaptées aux nouveautés**. La souris ne peut pas être automatisée : le script **anime** la machine et
**dit** à Nico quoi faire. Séquence suggérée (à ajuster) :

1. **Présentation / navigation** (S4.1) : « la fenetre est maximisee ; bouton du milieu = orbite,
   Shift+milieu = pan, molette = zoom ; appuie sur F11 pour le plein ecran ». Laisser quelques
   secondes, KM1 en marche pour que ça tourne.
2. **Panneau des elements** (S4.2) : « regarde le panneau lateral : une ligne par element (KM1, YV1,
   YV2, B1, B2) avec les adresses %MW ». Sortir YV1 (`cmd_extend` bit) → « la ligne YV1 passe a
   tige 100% (sorti), S11=0 S12=1 » ; rentrer → l'inverse.
3. **Surbrillance croisee** (S4.2 + S4.3) : « survole la ligne S12/YV1 dans le panneau : la tige du
   verin 90 s'allume » ; puis « survole directement la tige du verin dans la 3D : sa ligne se
   surligne ». Faire présence B1/B2 (bloquer une palette) pour éclairer une fenêtre capteur.
4. **Rappel D-013** (opportuniste, zéro code) : rappeler que si un `SimHost` tenait le port 502, la
   scène afficherait le bandeau rouge — proposer à Nico de le vérifier une fois (lancer SimHost puis F5).

## Décisions pré-tranchées

- **Guidage à la voix** (comme sprint 3) : le script n'automatise que les **commandes Modbus** ; la
  **souris/caméra reste manuelle** (annoncée). C'est la nature d'une démo de navigation.
- **Pré-vol 502 obligatoire** (repris tel quel) : refuse de tourner si le port n'est pas tenu par la
  scène Godot.
- **ASCII pur**, `-Repeat` conservé pour une démo en boucle.

## Ce qu'il NE faut PAS faire

- **Ne pas** lancer Godot depuis le script (Nico fait F5 lui-même, comme aux sprints 2/3).
- **Ne pas** écrire dans `ret` (le script joue le M580 : il écrit `cmd`, lit `ret`).
- **Ne pas** modifier le core ni les fichiers de scène ici (démo seule). Si un manque est constaté
  dans la scène, c'est une régression de S4.1–S4.3, pas à corriger dans le script.

## Definition of Done (cochable)

- [ ] `runtime/scripts/demo_sprint_04.ps1` créé (ASCII pur, `param` + pré-vol 502 + phases + `-Repeat`).
- [ ] Les phases couvrent **navigation**, **panneau**, **surbrillance croisée**, **rappel D-013**.
- [ ] Le script tourne sans erreur PowerShell (parse + exécution) quand la scène écoute sur 502.
- [ ] Banc **inchangé** : `dotnet test` = **95**, 4 pytest full-chain verts (le script ne touche pas le code).

## Vérif autosuffisante

1. `powershell -File runtime/scripts/demo_sprint_04.ps1` **sans scène lancée** → pré-vol échoue
   proprement (message clair « rien n'ecoute sur 502 »), exit non nul, **pas** de stacktrace.
2. Avec la scène Godot lancée (F5, à l'écoute 502) → les phases se déroulent, les commandes FC16
   passent, le panneau et la 3D réagissent, le guidage console est lisible.
3. `dotnet test` → **95** ; `pytest testbench/test_modbus_chain.py` → 4 verts (inchangés).
4. **Validation manuelle (Nico)** : dérouler la démo, suivre le guidage, confirmer navigation +
   panneau + surbrillance croisée à l'œil.

## Banc attendu

`dotnet test` **inchangé (95)** + 4 pytest full-chain **inchangés/verts**. Le script ne touche aucun code.

## Fichiers touchés

- `runtime/scripts/demo_sprint_04.ps1` — **créé**.

# =============================================================================
# demo_sprint_06.ps1 - Demo guidee du FORCAGE DE DEBUG (sprint 6)
# =============================================================================
#
# But : eprouver le FORCAGE d'un signal de commande (cmd : KM1 run, YV1, YV2) depuis
# l'IHM, qui surclasse la valeur EFFECTIVE du bit A LA LECTURE (masque post-snapshot, en
# tete de Tick). La sim ne reecrit JAMAIS un mot Modbus (ni ret ni cmd datastore) : le
# forcage vit dans _sim.Forces et n'agit que sur la copie snapshot des commandes.
#
# Deux histoires sont prouvees :
#   1. PILOTAGE IHM PUR (sans PLC) : des phases ou io_scanner n'est PAS lance (aucun M580
#      ne scrute, personne n'ecrit cmd). Tu forces depuis l'IHM et la machine bouge : le
#      forcage EST le pilote. La sim tourne a 10 Hz meme sans scan -> la 3D repond.
#   2. FORCAGE MALGRE LE PLC (avec scan) : io_scanner ecrit cmd a une valeur, le forcage
#      la surclasse a la lecture. On regarde l'ecart cmd (PLC=n -> force m), la 3D qui suit
#      le forcage, et KM1_AUX=1 vu par le PLC SANS qu'il l'ait commande (marche forcee
#      localement, detectable). Plus une phase BLOQUEUR INEFFICACE (tige levee, mais le
#      poste est exclu du blocage -> une palette traverse).
#
# La demo VALIDE l'IHM : le script ne clique pas a ta place. Il te DIT quoi forcer / quel
# defaut injecter, te laisse un temps de preparation, puis (pour les phases AVEC scan) lance
# io_scanner_sim.py qui joue le M580 (ECRIT cmd via FC16, LIT ret via FC3) et AFFICHE les
# retours vus par le PLC. Pour les phases SANS scan, aucun Python n'est lance : tu observes
# la 3D et le panneau, le forcage seul anime la machine.
#
# Ce script ne lance PAS Godot : tu lances la scene toi-meme (F5), puis ce script dans un
# autre terminal.
#
# Raccourcis IHM utilises par la demo (sprint 6) :
#   A / Z : cycler la selection (AZERTY)      G : ouvrir le menu FORCAGE de la selection
#   F     : menu DEFAUT                        R : reparer les defauts de la selection
#   B     : mode aveugle                       clic 3D / ligne : selectionner
#   menu Forcage d'une ligne : Auto / forcer a 0 / forcer a 1  (par signal cmd)
#
# Rappel du piege (D-013) : si un autre process (SimHost) tient deja le port 502, la scene
# Godot demarre SANS serveur et affiche un bandeau rouge. Le pre-vol ci-dessous verifie donc
# QUI ecoute sur 502 et refuse de tourner si ce n'est pas la scene Godot. NB : le pre-vol
# 502 reste requis MEME pour les phases sans scan, car c'est la SCENE qui tient le port.
#
# NB encodage : ASCII pur (pas d'accents ni de tirets longs). Windows PowerShell 5.1 lit les
# .ps1 sans BOM en Windows-1252 ; un caractere multi-octet dans une chaine casserait le parseur.
#
# Rythme de la demo (IMPORTANT) :
#   Par DEFAUT la demo est INTERACTIVE : a chaque phase, elle affiche TOUTES les consignes
#   (ce qu il faut faire dans l IHM + ce qu il faut regarder) puis ATTEND que tu appuies sur
#   ENTREE avant de continuer (lancer le scan, ou passer a la phase suivante en no-scan). Tu
#   prends le temps de lire, de preparer l IHM et de comprendre chaque effet, sans course
#   contre la montre. Passe -Prep <n> pour enchainer AUTOMATIQUEMENT avec un decompte de n
#   secondes par phase (utile pour une demo qui tourne seule, sans personne au clavier).
#
# Usage :
#   powershell -File runtime/scripts/demo_sprint_06.ps1                 # interactif (ENTREE = go)
#   powershell -File runtime/scripts/demo_sprint_06.ps1 -Prep 10        # auto : decompte 10 s/phase
#   powershell -File runtime/scripts/demo_sprint_06.ps1 -PyHost 127.0.0.1 -Port 502
#
# Prerequis : la scene Godot LANCEE (F5) et a l'ecoute sur 502 ; Python + pymodbus.

param(
    [string]$PyHost = '127.0.0.1',
    [int]$Port = 502,
    [int]$Prep = 9         # secondes de decompte/phase EN MODE AUTO (ignore en interactif) ; voir -Prep
)

# Mode interactif = defaut. Il devient AUTO (decompte) uniquement si -Prep est passe explicitement.
# On distingue "non fourni" de "fourni a 9" via $PSBoundParameters : sinon le defaut a 9 masquerait
# l intention de l utilisateur.
$script:Interactive = -not $PSBoundParameters.ContainsKey('Prep')

# On NE met PAS $ErrorActionPreference='Stop' : io_scanner peut ecrire des echecs de connexion
# sur stderr ; sous 'Stop' ce stderr deviendrait fatal et arreterait la demo.

# --- Arborescence : scripts/ -> runtime/ -> racine repo (testbench/ a cote) ------------------
$scriptDir  = $PSScriptRoot
$projectDir = Split-Path -Parent $scriptDir
$repoDir    = Split-Path -Parent $projectDir
$testbench  = Join-Path $repoDir 'testbench'
$ioScanner  = Join-Path $testbench 'io_scanner_sim.py'

if (-not (Test-Path $ioScanner)) {
    Write-Error "DEMO KO : io_scanner_sim.py introuvable ($ioScanner)"
    exit 1
}

# --- Resolution de l'interpreteur Python (python, sinon py) ----------------------------------
$pyCmd = Get-Command python -ErrorAction SilentlyContinue
if (-not $pyCmd) { $pyCmd = Get-Command py -ErrorAction SilentlyContinue }
if (-not $pyCmd) {
    Write-Error "DEMO KO : ni 'python' ni 'py' dans le PATH."
    exit 1
}
$py = $pyCmd.Source
if (-not $py) { $py = $pyCmd.Name }

# --- Pre-vol : QUI ecoute sur le port 502 ? --------------------------------------------------
# On veut que ce soit la SCENE GODOT. Si c'est SimHost, la demo piloterait un hote headless
# (aucun visuel) : on refuse. Si personne n'ecoute, la scene n'est pas lancee : on refuse aussi.
# Ce pre-vol est requis MEME pour les phases sans scan : c'est la scene qui tient le port.
$listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $listener) {
    Write-Host ''
    Write-Host "DEMO KO : rien n'ecoute sur le port $Port." -ForegroundColor Red
    Write-Host "  -> Lance d'abord la scene dans Godot (touche F5), puis relance ce script." -ForegroundColor Yellow
    exit 1
}
$owner = (Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue).ProcessName
if ($owner -match 'SimHost') {
    Write-Host ''
    Write-Host "DEMO KO : c'est SimHost (pas la scene Godot) qui ecoute sur $Port." -ForegroundColor Red
    Write-Host "  -> Arrete-le :  Stop-Process -Name SimHost -Force" -ForegroundColor Yellow
    Write-Host "  -> Puis lance la scene dans Godot (F5) et relance ce script." -ForegroundColor Yellow
    exit 1
}
if ($owner -notmatch 'Godot') {
    Write-Host "Avertissement : le port $Port est tenu par '$owner' (attendu : Godot)." -ForegroundColor Yellow
}
Write-Host "Serveur sur ${PyHost}:${Port} tenu par '$owner' - OK." -ForegroundColor Green

$period = 0.1                       # 100 ms = cadence de scan (aligne sur le heartbeat)

# --- Pause avant chaque phase : laisse a l'humain le temps de lire et de preparer l'IHM ------
# La demo VALIDE l'IHM : c'est TOI qui forces/injectes/repares. Cette pause intervient APRES
# l'affichage de toutes les consignes (a faire + a regarder). Deux rythmes :
#   - INTERACTIF (defaut) : on ATTEND ENTREE. Tu lis, tu prepares, tu declenches quand tu es pret.
#   - AUTO (-Prep n) : decompte de n secondes puis on enchaine, sans intervention.
function Wait-Go {
    param([int]$Seconds)        # duree du decompte EN MODE AUTO uniquement
    if ($script:Interactive) {
        Write-Host ''
        $null = Read-Host '  >>> Prepare l IHM tranquillement, puis appuie sur ENTREE pour continuer'
    }
    elseif ($Seconds -gt 0) {
        for ($s = $Seconds; $s -gt 0; $s--) {
            Write-Host ("`r  ... prepare l'IHM, suite dans {0,2}s " -f $s) -NoNewline -ForegroundColor DarkGray
            Start-Sleep -Seconds 1
        }
        Write-Host "`r  ... on continue.                    " -ForegroundColor DarkGray
    }
    else {
        Start-Sleep -Milliseconds 700   # phase sans preparation : petit temps de lecture de la banniere
    }
}

# --- Une phase = (consignes IHM + pause) puis, si AVEC scan, N cycles de scan io_scanner -----
# Le switch -NoScan marque une phase de PILOTAGE IHM PUR : aucun io_scanner n'est lance (aucun
# M580 ne scrute). La sim tourne quand meme a 10 Hz : le FORCAGE seul anime la machine. On
# n'observe alors que la 3D + le panneau (pas de console PLC).
# En phase AVEC scan, io_scanner ecrit la zone cmd COMPLETE a chaque appel (les bits non demandes
# retombent a 0) puis scrute --cycles fois a --period, et affiche les retours vus par le PLC.
function Invoke-Phase {
    param(
        [string]  $Title,           # titre de la phase
        [string[]]$Inject = @(),    # consignes IHM AVANT le scan (forcage/injection/reparation), + pause
        [string[]]$Watch,           # ce qu'il faut regarder (3D + panneau, et console PLC si scan)
        [string[]]$CmdArgs,         # forcages io_scanner, ex. @('--run','1','--yv1','1') (ignore si -NoScan)
        [int]     $Cycles,          # duree de scan = Cycles * period secondes (ignore si -NoScan)
        [int]     $PrepSeconds = 0, # temps de preparation avant scan/suite (0 = pas de pause auto)
        [switch]  $NoScan           # phase de pilotage IHM pur : NE PAS lancer io_scanner
    )
    Write-Host ''
    if ($NoScan) {
        Write-Host ('=== {0}   (SANS PLC : aucun scan) ===' -f $Title) -ForegroundColor Magenta
    }
    else {
        $dur = [math]::Round($Cycles * $period, 1)
        Write-Host ('=== {0}   (~{1}s de scan) ===' -f $Title, $dur) -ForegroundColor Cyan
    }
    # On affiche d'abord TOUTES les consignes (a faire + a regarder), PUIS on marque la pause.
    foreach ($d in $Inject) { Write-Host ("  A FAIRE dans l'IHM : " + $d) -ForegroundColor Yellow }
    if ($NoScan) {
        Write-Host '  CE QU IL FAUT REGARDER (3D + panneau ; aucun PLC ne scrute) :' -ForegroundColor White
    }
    else {
        Write-Host '  CE QU IL FAUT REGARDER (3D <-> console PLC) :' -ForegroundColor White
    }
    foreach ($w in $Watch) { Write-Host ("    - " + $w) -ForegroundColor White }

    if ($NoScan) {
        Write-Host '  commande M580 (io_scanner) : AUCUNE (le forcage IHM est le seul pilote)' -ForegroundColor DarkGray
        Wait-Go -Seconds $PrepSeconds
        return
    }

    Write-Host ('  commande M580 (io_scanner) : ' + ($CmdArgs -join ' ')) -ForegroundColor DarkGray
    Wait-Go -Seconds $PrepSeconds   # ENTREE (interactif) ou decompte (-Prep) avant le scan

    Push-Location $testbench
    & $py io_scanner_sim.py --host $PyHost --port $Port @CmdArgs --cycles $Cycles --period $period
    $rc = $LASTEXITCODE
    Pop-Location
    if ($rc -ne 0) {
        Write-Host ("  (io_scanner a renvoye {0} - serveur injoignable ou scan interrompu)" -f $rc) -ForegroundColor Yellow
    }
}

function Invoke-Sequence {

    # --- 1/8 : PILOTAGE SANS PLC - convoyeur (no-scan) --------------------------------------
    # Personne n'ecrit cmd. On force KM1 run a 1 depuis l'IHM : la sim (10 Hz) fait tourner
    # l'anneau. KM1_AUX suit la commande EFFECTIVE (forcee) apres feedback_delay -> passe a 1
    # sans qu'aucun PLC ne l'ait commande. La cellule cmd affiche l'ecart PLC=0 -> force 1.
    Invoke-Phase -Title 'PHASE 1/8 : PILOTAGE SANS PLC - CONVOYEUR' -NoScan `
        -PrepSeconds $Prep `
        -Inject @('selectionne le convoyeur (clic sur l anneau, ou cycle avec A / Z jusqu a KM1).',
                  'ouvre le menu FORCAGE : touche G (ou le MenuButton de la colonne Forcage, ligne KM1).',
                  'choisis "forcer a 1" pour KM1 run.') `
        -Watch @('l ANNEAU TOURNE et les palettes avancent, alors qu AUCUN M580 ne scrute.',
                 'la cellule cmd de KM1 montre l ecart : PLC=0 -> force 1 (teinte magenta).',
                 'KM1_AUX passe a 1 apres le feedback_delay (marche confirmee EFFECTIVE, non commandee).',
                 'c est le FORCAGE seul qui pilote : pas de scan, la sim tourne quand meme.')

    # --- 2/8 : PILOTAGE SANS PLC - verins (no-scan) -----------------------------------------
    # Pilotage bit par bit sans PLC : forcer YV1 puis YV2 a 1 fait sortir les tiges (S12/S22
    # suivent la commande effective forcee). Retour Auto -> les tiges rentrent (monostable).
    Invoke-Phase -Title 'PHASE 2/8 : PILOTAGE SANS PLC - VERINS' -NoScan `
        -PrepSeconds $Prep `
        -Inject @('selectionne YV1 (clic sur la tige a 90 deg, ou A / Z jusqu a YV1), G, "forcer a 1".',
                  'selectionne YV2 (clic ou A / Z jusqu a YV2), G, "forcer a 1".',
                  'puis remets chacun en "Auto" (G -> Auto) pour les faire rentrer.') `
        -Watch @('les TIGES SORTENT une a une des que tu forces a 1 ; S12 puis S22 passent a 1.',
                 'chaque cellule cmd forcee montre PLC=0 -> force 1 (teinte magenta).',
                 'en repassant a Auto : la commande effective retombe a 0 -> les tiges RENTRENT.',
                 'le pilotage se fait bit par bit, entierement depuis l IHM, sans aucun PLC.')

    # --- 3/8 : FORCAGE MALGRE LE PLC (scan run=0 tout a 0) ----------------------------------
    # Le M580 commande tout a 0, mais YV1 reste FORCE a 1 -> le forcage surclasse la lecture.
    Invoke-Phase -Title 'PHASE 3/8 : FORCAGE MALGRE LE PLC (YV1)' `
        -PrepSeconds $Prep `
        -Inject @('garde YV1 FORCE a 1 (si tu l as remis a Auto en phase 2 : selectionne YV1, G, "forcer a 1").',
                  'les autres signaux : laisse-les en Auto.') `
        -Watch @('le PLC commande TOUT a 0 (--run 0 --yv1 0 --yv2 0), mais la tige YV1 RESTE SORTIE.',
                 'la cellule cmd YV1 montre l ecart : PLC=0 -> force 1 (le forcage gagne a la lecture).',
                 'S12 reste a 1 cote console : le PLC voit le verin sorti alors qu il commande 0.',
                 'preuve : le forcage surclasse la commande PLC sans jamais ecrire un mot Modbus.') `
        -CmdArgs @('--run','0','--yv1','0','--yv2','0') -Cycles 90

    # --- 4/8 : KM1_AUX NON COMMANDE (scan run=0) --------------------------------------------
    # Marche forcee localement : le scanner commande --run 0, on force KM1 run a 1. Le PLC lit
    # KM1_AUX=1 (retour de marche effective) alors qu il n a jamais commande la marche.
    Invoke-Phase -Title 'PHASE 4/8 : KM1_AUX NON COMMANDE (marche forcee)' `
        -PrepSeconds $Prep `
        -Inject @('remets YV1 en Auto (selectionne YV1, G, "Auto") pour isoler l effet convoyeur.',
                  'selectionne le convoyeur (A / Z jusqu a KM1), G, "forcer a 1" sur KM1 run.') `
        -Watch @('le scanner commande --run 0 (marche NON demandee par le PLC).',
                 'mais l anneau TOURNE et la console lit KM1_AUX=1 : marche forcee localement.',
                 'la cellule cmd KM1 montre PLC=0 -> force 1 : l ecart trahit le forcage.',
                 'sur site : marche forcee en local, DETECTABLE via KM1_AUX=1 sans commande.') `
        -CmdArgs @('--run','0') -Cycles 90

    # --- 5/8 : FORCAGE A 0 CONTRE LE PLC (scan run=1 yv1=1) ---------------------------------
    # Le PLC commande YV1 a sortir, mais on force YV1 a 0 -> le forcage a 0 masque la commande.
    Invoke-Phase -Title 'PHASE 5/8 : FORCAGE A 0 CONTRE LE PLC (YV1)' `
        -PrepSeconds $Prep `
        -Inject @('remets KM1 en Auto (selectionne KM1, G, "Auto").',
                  'selectionne YV1 (A / Z ou clic), G, "forcer a 0".') `
        -Watch @('le PLC COMMANDE YV1 a sortir (--yv1 1), mais la tige RESTE RENTREE.',
                 'la cellule cmd YV1 montre PLC=1 -> force 0 (le forcage a 0 gagne).',
                 'S12 reste a 0 : le PLC commande l extension mais ne voit jamais la fin de course.',
                 'le forcage a 0 masque la commande PLC : utile pour neutraliser un actionneur.') `
        -CmdArgs @('--run','1','--yv1','1') -Cycles 90

    # --- 6/8 : COMPOSITION FORCAGE x DEFAUT (scan) ------------------------------------------
    # Ordre des couches : forcage cmd (tete de Tick) -> defaut physique (AdvanceCylinder). Un YV1
    # force a 1 + "ne sort pas" : le defaut physique GAGNE sur la commande forcee -> tige rentree.
    Invoke-Phase -Title 'PHASE 6/8 : COMPOSITION FORCAGE x DEFAUT (YV1)' `
        -PrepSeconds $Prep `
        -Inject @('selectionne YV1, G, "forcer a 1" (commande effective d extension forcee).',
                  'sur YV1 toujours : F (menu DEFAUT), choisis "verin : ne sort pas".') `
        -Watch @('YV1 est FORCE a sortir, mais la tige RESTE RENTREE : le defaut physique gagne.',
                 'S12 reste a 0 : la couche defaut (physique) surclasse la commande forcee.',
                 'ordre deterministe : forcage cmd en tete de Tick, puis defaut physique par-dessus.',
                 'la cellule cmd montre force 1, mais la 3D montre tige basse (defaut = badge YV1).') `
        -CmdArgs @('--run','1') -Cycles 90

    # --- 7/8 : BLOQUEUR INEFFICACE (scan run=1 yv1=1) ---------------------------------------
    # BlockerIneffective : la tige sort normalement (S12=1) mais CollectBlockedStations exclut ce
    # poste -> une palette TRAVERSE le poste 90 deg (B1 monte puis retombe). "Je crois bloquer".
    Invoke-Phase -Title 'PHASE 7/8 : BLOQUEUR INEFFICACE (YV1)' `
        -PrepSeconds $Prep `
        -Inject @('repare le defaut precedent : selectionne YV1 puis R (le badge "ne sort pas" disparait).',
                  'garde YV1 en Auto (G -> Auto) : le PLC commande la sortie via --yv1 1.',
                  'selectionne YV1, F (menu DEFAUT), choisis "bloqueur inefficace".') `
        -Watch @('la tige YV1 est bien LEVEE (S12=1) : elle a l air de bloquer.',
                 'mais une palette TRAVERSE le poste 90 deg : B1 monte puis RETOMBE (elle ne reste pas).',
                 'le poste est exclu du blocage : la tige levee ne retient rien.',
                 'signature terrain : "je crois bloquer, la palette file quand meme" (badge rouge YV1).') `
        -CmdArgs @('--run','1','--yv1','1') -Cycles 120

    # --- 8/8 : RETOUR AU NOMINAL ------------------------------------------------------------
    Invoke-Phase -Title 'PHASE 8/8 : RETOUR AU NOMINAL' `
        -PrepSeconds $Prep `
        -Inject @('repare le bloqueur : selectionne YV1 puis R (plus aucun badge de defaut).',
                  'remets TOUS les forcages a Auto : pour chaque signal force (KM1, YV1, YV2) -> G -> "Auto".',
                  'verifie la colonne Forcage : plus aucune ligne ne doit afficher "force 0/1".') `
        -Watch @('cmd tout a 0 : verins rentres, convoyeur a l arret (KM1_AUX=0).',
                 'plus aucun ecart dans les cellules cmd (plus de teinte magenta).',
                 'aucun element marque : la machine est revenue au nominal propre.') `
        -CmdArgs @('--run','0','--yv1','0','--yv2','0') -Cycles 40
}

Write-Host ''
Write-Host 'Demo FORCAGE de debug sprint 6 - regarde la fenetre Godot et suis le guidage console.'
Write-Host '  2 histoires : (1) PILOTAGE SANS PLC (phases 1-2, aucun scan : le forcage anime seul la'
Write-Host '  machine) ; (2) FORCAGE MALGRE LE PLC (phases 3-7, avec scan : le forcage surclasse la'
Write-Host '  commande, KM1_AUX=1 non commande, bloqueur inefficace). TU forces/injectes dans l IHM.' -ForegroundColor DarkGray
Write-Host '  Rappel : le forcage n ecrit JAMAIS un mot Modbus (masque a la lecture, tete de Tick).' -ForegroundColor DarkGray
Write-Host ''
Write-Host '  Raccourcis sprint 6 :' -ForegroundColor DarkGray
Write-Host '    A / Z : cycler la selection      G : menu FORCAGE de la selection' -ForegroundColor DarkGray
Write-Host '    F     : menu DEFAUT              R : reparer les defauts     B : mode aveugle' -ForegroundColor DarkGray
if ($script:Interactive) {
    Write-Host '  Rythme INTERACTIF : a chaque phase, lis les consignes, prepare l IHM, puis ENTREE pour continuer.' -ForegroundColor DarkGray
    Write-Host '  (Passe -Prep <n> pour enchainer automatiquement avec un decompte de n secondes par phase.)' -ForegroundColor DarkGray
} else {
    Write-Host ("  Rythme AUTO : decompte de {0}s par phase (relance sans -Prep pour le mode interactif)." -f $Prep) -ForegroundColor DarkGray
}

Invoke-Sequence

# --- Cloture : on s assure que forcages et defauts sont revenus au nominal --------------------
Write-Host ''
Write-Host 'AVANT DE PARTIR : remets TOUS les forcages a Auto (colonne Forcage : plus aucun "force 0/1")' -ForegroundColor Yellow
Write-Host '  et repare les defauts restants (selectionne puis R) : plus aucun badge sur les elements.' -ForegroundColor Yellow
Write-Host ''
Write-Host 'Rappel D-013 : si SimHost tenait le port 502, la scene Godot demarrerait SANS serveur' -ForegroundColor DarkGray
Write-Host '  et afficherait un bandeau rouge (voir le pre-vol en tete de ce script).' -ForegroundColor DarkGray
Write-Host ''
Write-Host 'Demo terminee.' -ForegroundColor Green
exit 0

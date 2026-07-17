# =============================================================================
# demo_sprint_05.ps1 - Demo guidee de l'INJECTION DE DEFAUTS (sprint 5)
# =============================================================================
#
# But : jouer le role du M580 (I/O Scanner) et animer la machine pendant que la console
# te GUIDE A LA VOIX pour eprouver les nouveautes du sprint 5 :
#   - S5.1 Coeur pur des defauts (physique par composant, capteur-bloque par bit ret,
#     gel des retours) : injecte dans la sim, JAMAIS un mot Modbus ecrit par la sim.
#   - S5.2 Selection d'un element (clic 3D <-> ligne du panneau, ou clavier [ / ]).
#   - S5.3 Menu de defaut par ligne + marquage 3D/badge + mode aveugle (touche B).
#   - S5.4 Coupure de comm : gel des retours (heartbeat fige) et coupure TCP reelle
#     (perte de l'esclave cote I/O Scanner), reparables depuis le panneau.
#
# L'injection des defauts se fait DANS L'IHM (la demo VALIDE l'IHM) : le script ne clique
# pas a ta place. Il te DIT quoi selectionner / quel defaut choisir, te laisse un temps de
# preparation, puis lance io_scanner_sim.py qui joue le M580 (il ECRIT la zone cmd via FC16,
# il LIT la zone ret via FC3) et AFFICHE les retours que le PLC voit (S11/S12, S21/S22,
# KM1_AUX, B1/B2, heartbeat). Tu compares ainsi "defaut injecte a l'ecran" <-> "ce que
# le M580 detecte" pour chaque famille de defaut.
#
# Ce script ne lance PAS Godot : tu lances la scene toi-meme (F5), puis ce script dans un
# autre terminal.
#
# Raccourcis IHM utilises par la demo (sprint 5) :
#   [ / ]  : cycler la selection d'element     R : reparer l'element selectionne
#   F      : ouvrir le menu de defaut          B : basculer le mode aveugle
#   clic gauche sur un element 3D ou sa ligne  : selectionner
#   panneau comm (sous le panneau sante) : cases "Geler retours" et "Couper TCP"
#
# Rappel du piege (D-013) : si un autre process (SimHost) tient deja le port 502, la scene
# Godot demarre SANS serveur et affiche un bandeau rouge. Le pre-vol ci-dessous verifie donc
# QUI ecoute sur 502 et refuse de tourner si ce n'est pas la scene Godot.
#
# NB encodage : ASCII pur (pas d'accents ni de tirets longs). Windows PowerShell 5.1 lit les
# .ps1 sans BOM en Windows-1252 ; un caractere multi-octet dans une chaine casserait le parseur.
#
# Rythme de la demo (IMPORTANT) :
#   Par DEFAUT la demo est INTERACTIVE : a chaque phase, elle affiche TOUTES les consignes
#   (ce qu il faut faire dans l IHM + ce qu il faut regarder) puis ATTEND que tu appuies sur
#   ENTREE avant de lancer le scan (l automate reprend la main). Tu prends donc le temps de lire,
#   de preparer l IHM et de comprendre les consequences de chaque defaut, sans course contre la
#   montre. Passe -Prep <n> pour au contraire enchainer AUTOMATIQUEMENT avec un decompte de n
#   secondes par phase (utile pour une demo qui tourne seule, sans personne au clavier).
#
# Usage :
#   powershell -File runtime/scripts/demo_sprint_05.ps1                 # interactif (ENTREE = go)
#   powershell -File runtime/scripts/demo_sprint_05.ps1 -Prep 10        # auto : decompte 10 s/phase
#   powershell -File runtime/scripts/demo_sprint_05.ps1 -PyHost 127.0.0.1 -Port 502
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

# On NE met PAS $ErrorActionPreference='Stop' : io_scanner ecrit ses echecs de connexion sur
# stderr (notamment en PHASE COUPURE TCP, ou la perte de l'esclave est VOULUE) ; sous 'Stop'
# ce stderr deviendrait fatal et arreterait la demo au moment le plus interessant.

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

# --- Pause avant le scan : laisse a l'humain le temps de lire et de preparer l'IHM -----------
# La demo VALIDE l'IHM : c'est TOI qui injectes/repare les defauts. Cette pause intervient APRES
# l'affichage de toutes les consignes (a faire + a regarder), juste avant que io_scanner ne
# reprenne la main. Deux rythmes :
#   - INTERACTIF (defaut) : on ATTEND que tu appuies sur ENTREE. Tu lis, tu prepares, tu declenches
#     quand tu es pret -> aucune course contre la montre pour comprendre chaque defaut.
#   - AUTO (-Prep n) : decompte de n secondes puis scan, sans intervention (demo qui tourne seule).
# Read-Host est ici volontaire : le script est lance a la main dans un terminal interactif, et
# c'est precisement la pause "je reprends mon souffle" demandee. En mode AUTO on ne l'appelle pas.
function Wait-Go {
    param([int]$Seconds)        # duree du decompte EN MODE AUTO uniquement
    if ($script:Interactive) {
        Write-Host ''
        $null = Read-Host '  >>> Prepare l IHM tranquillement, puis appuie sur ENTREE pour lancer le scan (l automate reprend la main)'
    }
    elseif ($Seconds -gt 0) {
        for ($s = $Seconds; $s -gt 0; $s--) {
            Write-Host ("`r  ... prepare l'IHM, scan dans {0,2}s " -f $s) -NoNewline -ForegroundColor DarkGray
            Start-Sleep -Seconds 1
        }
        Write-Host "`r  ... lancement du scan.               " -ForegroundColor DarkGray
    }
    else {
        Start-Sleep -Milliseconds 700   # phase sans preparation : petit temps de lecture de la banniere
    }
}

# --- Une phase = (consignes IHM + pause) puis N cycles de scan io_scanner --------------------
# io_scanner ecrit la zone cmd COMPLETE a chaque appel (les bits non forces retombent a 0) puis
# scrute --cycles fois a --period. Le datastore CONSERVE la derniere commande ecrite : l'etat
# de commande persiste, mais le DEFAUT, lui, vit dans la SIM (injecte via l'IHM) et n'est jamais
# ecrit en Modbus. La console affiche les retours vus par le PLC : compare-les a la 3D.
function Invoke-Phase {
    param(
        [string]  $Title,           # titre de la phase
        [string[]]$Inject = @(),    # consignes IHM AVANT le scan (injection/reparation), + pause
        [string[]]$Watch,           # ce qu'il faut regarder pendant le scan (3D <-> console)
        [string[]]$CmdArgs,         # forcages io_scanner, ex. @('--run','1','--yv1','1')
        [int]     $Cycles,          # duree de scan = Cycles * period secondes
        [int]     $PrepSeconds = 0  # temps de preparation avant scan (0 = pas de pause)
    )
    $dur = [math]::Round($Cycles * $period, 1)
    Write-Host ''
    Write-Host ('=== {0}   (~{1}s de scan) ===' -f $Title, $dur) -ForegroundColor Cyan
    # On affiche d'abord TOUTES les consignes (a faire + a regarder), PUIS on marque la pause :
    # ainsi rien ne defile entre la lecture et la reprise de main de l'automate.
    foreach ($d in $Inject) { Write-Host ("  A FAIRE dans l'IHM : " + $d) -ForegroundColor Yellow }
    Write-Host '  CE QU IL FAUT REGARDER (3D <-> console PLC) :' -ForegroundColor White
    foreach ($w in $Watch) { Write-Host ("    - " + $w) -ForegroundColor White }
    Write-Host ('  commande M580 (io_scanner) : ' + ($CmdArgs -join ' ')) -ForegroundColor DarkGray
    Wait-Go -Seconds $PrepSeconds   # ENTREE (interactif) ou decompte (-Prep) avant le scan

    Push-Location $testbench
    & $py io_scanner_sim.py --host $PyHost --port $Port @CmdArgs --cycles $Cycles --period $period
    $rc = $LASTEXITCODE
    Pop-Location
    if ($rc -ne 0) {
        Write-Host ("  (io_scanner a renvoye {0} - NORMAL en phase COUPURE TCP ; sinon serveur injoignable)" -f $rc) -ForegroundColor Yellow
    }
}

function Invoke-Sequence {

    # --- 1/8 : NOMINAL (reference avant defauts) --------------------------------------------
    Invoke-Phase -Title 'PHASE 1/8 : NOMINAL (reference)' `
        -Watch @('convoyeur en MARCHE : l anneau tourne, les palettes avancent (KM1_AUX=1).',
                 'on commande YV1 et YV2 : les tiges SORTENT ; S12 puis S22 passent a 1.',
                 'S11/S21 (rentre) retombent a 0 quand les verins sortent.',
                 'heartbeat HB defile a chaque cycle (~100 ms) : la scrutation est vivante.') `
        -CmdArgs @('--run','1','--yv1','1','--yv2','1') -Cycles 90

    # --- 2/8 : VERIN NE SORT PAS (physique, YV1) --------------------------------------------
    # Defaut pre-injecte : le verin reste rentre malgre la commande d extension -> S12 ne vient
    # jamais. Le M580 voit une commande sans confirmation de fin de course (defaut de course).
    Invoke-Phase -Title 'PHASE 2/8 : VERIN NE SORT PAS (YV1)' `
        -PrepSeconds $Prep `
        -Inject @('selectionne YV1 : clique la tige du verin a 90 deg, ou cycle avec ] jusqu a YV1.',
                  'ouvre le menu de defaut : touche F (ou le bouton Defaut de la ligne YV1).',
                  'choisis "verin : ne sort pas" -> la ligne se marque (badge), la 3D signale le defaut.') `
        -Watch @('on COMMANDE YV1 a sortir, mais la tige NE MONTE PAS (reste rentree).',
                 'S11 reste a 1, S12 ne passe JAMAIS a 1 -> commande sans confirmation fin de course.',
                 'compare : 3D = tige basse ; console = S12=0 alors qu on commande l extension.') `
        -CmdArgs @('--run','1','--yv1','1','--yv2','0') -Cycles 90

    # --- 3/8 : VERIN COINCE MI-COURSE (physique, YV2) ---------------------------------------
    # Ici l injection se fait PENDANT le scan : on commande d abord YV2 a sortir, et TANT QUE la
    # tige monte on injecte "coince mi-course" pour la figer la ou elle est (ni S21 ni S22).
    Invoke-Phase -Title 'PHASE 3/8 : VERIN COINCE MI-COURSE (YV2)' `
        -PrepSeconds $Prep `
        -Inject @('repare d abord YV1 : selectionne YV1 puis appuie sur R (le badge disparait).',
                  'selectionne YV2 (clic ou ] ) et TIENS-TOI PRET : tu injecteras PENDANT le scan.') `
        -Watch @('des que le scan demarre, YV2 commence a SORTIR (la tige monte).',
                 'PENDANT que la tige monte : F puis "verin : coince mi-course" -> la tige se FIGE.',
                 'la tige reste a mi-hauteur : ni S21 (rentre) ni S22 (sorti) ne sont a 1.',
                 'etat incoherent : aucune fin de course -> le PLC ne sait pas ou est le verin.') `
        -CmdArgs @('--run','1','--yv1','0','--yv2','1') -Cycles 120

    # --- 4/8 : CAPTEUR BLOQUE (generique, S12 menteur) --------------------------------------
    # Capteur-bloque : on force le bit ret S12 a 1 alors que la tige est rentree. Le PLC lit un
    # capteur menteur (fin de course "sortie" collee) - purement dans la sim, aucun mot ecrit.
    Invoke-Phase -Title 'PHASE 4/8 : CAPTEUR BLOQUE (S12 menteur, YV1)' `
        -PrepSeconds $Prep `
        -Inject @('repare YV2 : selectionne YV2 puis R.',
                  'selectionne YV1, ouvre le menu (F), choisis "S12 bloque a 1".',
                  'aucune commande d extension ne sera envoyee : la tige DOIT rester rentree.') `
        -Watch @('la tige YV1 est RENTREE (on ne commande pas l extension).',
                 'mais la console lit S12=1 : le M580 croit le verin SORTI -> capteur menteur.',
                 'compare : 3D = tige basse ; console = S12=1 (l ecart est le defaut a diagnostiquer).') `
        -CmdArgs @('--run','1','--yv1','0','--yv2','0') -Cycles 90

    # --- 5/8 : CONVOYEUR PATINE (physique, KM1) ---------------------------------------------
    # ConveyorSlip : KM1_AUX reste a 1 (marche confirmee) mais les palettes ne bougent plus et
    # B1/B2 n evoluent pas -> glissement/perte d entrainement, indetectable sans codeur.
    Invoke-Phase -Title 'PHASE 5/8 : CONVOYEUR PATINE (KM1)' `
        -PrepSeconds $Prep `
        -Inject @('repare YV1 : selectionne YV1 puis R.',
                  'selectionne le convoyeur (clic sur l anneau ou ] jusqu a KM1).',
                  'ouvre le menu (F), choisis "convoyeur : patine".') `
        -Watch @('KM1_AUX reste a 1 (le PLC croit le convoyeur en marche).',
                 'mais les PALETTES sont IMMOBILES dans la 3D (le convoyeur patine).',
                 'B1 et B2 n evoluent plus : aucune palette n arrive aux stations.',
                 'sans codeur, le PLC ne voit PAS le glissement : incoherence marche/mouvement.') `
        -CmdArgs @('--run','1','--yv1','0','--yv2','0') -Cycles 100

    # --- 6/8 : GEL DES RETOURS (comm, watchdog) ---------------------------------------------
    # RetFrozen : la sim continue de tourner (la 3D bouge) mais cesse de PUBLIER ret + heartbeat.
    # Cote PLC : HB fige et retours geles alors que le TCP reste vivant -> le watchdog doit lever.
    Invoke-Phase -Title 'PHASE 6/8 : GEL DES RETOURS (heartbeat fige)' `
        -PrepSeconds $Prep `
        -Inject @('repare le convoyeur : selectionne KM1 puis R.',
                  'dans le panneau comm (sous le panneau sante), COCHE "Geler retours".') `
        -Watch @('la 3D CONTINUE de bouger (anneau qui tourne, verins qui reagissent).',
                 'mais la console FIGE : HB ne s incremente plus, les retours restent gelees.',
                 'le TCP reste vivant (io_scanner lit toujours) : seule la donnee est gelee.',
                 'c est le cas d un watchdog PLC : plus de heartbeat = automate en defaut de vie.') `
        -CmdArgs @('--run','1','--yv1','0','--yv2','0') -Cycles 90

    # --- 7/8 : COUPURE TCP (comm, perte d esclave) ------------------------------------------
    # Disconnect() = Stop() du serveur : le socket 502 se ferme, io_scanner perd la connexion et
    # sort en erreur (VOULU). "Reparer" = decoche -> Reconnect() = re-bind 502, il rescanne.
    Invoke-Phase -Title 'PHASE 7a/8 : COUPURE TCP (perte de l esclave)' `
        -PrepSeconds $Prep `
        -Inject @('DECOCHE d abord "Geler retours" (retour au nominal de la donnee).',
                  'puis COCHE "Couper TCP" : le serveur ferme le port 502 (esclave perdu).') `
        -Watch @('io_scanner PERD la connexion : il affiche une erreur et s arrete (c est VOULU).',
                 'cote M580 reel : defaut de scrutation (esclave Modbus injoignable).',
                 'le bandeau/panneau comm de la scene indique "TCP coupe".') `
        -CmdArgs @('--run','1') -Cycles 40

    Invoke-Phase -Title 'PHASE 7b/8 : REPARATION TCP (re-bind 502)' `
        -PrepSeconds $Prep `
        -Inject @('DECOCHE "Couper TCP" : le serveur re-ouvre le port 502 (Reconnect).') `
        -Watch @('io_scanner se RECONNECTE et rescanne : les retours reviennent, HB reprend.',
                 'la scrutation reprend sans redemarrer la scene : l esclave est de retour.') `
        -CmdArgs @('--run','1','--yv1','1') -Cycles 60

    # --- 8/8 : MODE AVEUGLE (diagnostic sans indice visuel) ---------------------------------
    # Mode aveugle (B) : le marquage 3D/badge est masque, mais les defauts restent injectes. On
    # rejoue "verin ne sort pas" a l aveugle -> la sim se comporte anormalement SANS indice visuel.
    Invoke-Phase -Title 'PHASE 8/8 : MODE AVEUGLE (B)' `
        -PrepSeconds $Prep `
        -Inject @('appuie sur B : MODE AVEUGLE actif (les marquages de defaut disparaissent).',
                  'selectionne YV1, F, choisis "verin : ne sort pas" (aucun badge ne s affiche).') `
        -Watch @('on commande YV1 a sortir mais la tige NE MONTE PAS -> S12 ne vient pas.',
                 'AUCUN indice visuel de defaut : il faut DIAGNOSTIQUER par le comportement seul.',
                 'compare 3D <-> console comme un vrai depannage a l aveugle sur site.') `
        -CmdArgs @('--run','1','--yv1','1','--yv2','0') -Cycles 90

    # --- Retour au repos + reparation finale ------------------------------------------------
    Invoke-Phase -Title 'RETOUR AU REPOS + REPARATION' `
        -PrepSeconds $Prep `
        -Inject @('appuie sur B pour QUITTER le mode aveugle (marquages a nouveau visibles).',
                  'selectionne YV1 puis R pour reparer le dernier defaut (plus aucun badge).') `
        -Watch @('cmd tout a 0 : verins rentres, convoyeur a l arret (KM1_AUX=0).',
                 'aucun element marque, la machine est revenue au nominal propre.') `
        -CmdArgs @('--run','0','--yv1','0','--yv2','0') -Cycles 40
}

Write-Host ''
Write-Host 'Demo injection de defauts sprint 5 - regarde la fenetre Godot et suis le guidage console.'
Write-Host '  8 familles : verin ne sort pas / coince mi-course / capteur menteur / convoyeur patine /'
Write-Host '  gel retours / coupure TCP / mode aveugle. TU injectes dans l IHM ; io_scanner montre le PLC.'
if ($script:Interactive) {
    Write-Host '  Rythme INTERACTIF : a chaque phase, lis les consignes, prepare l IHM, puis ENTREE pour lancer le scan.' -ForegroundColor DarkGray
    Write-Host '  (Passe -Prep <n> pour enchainer automatiquement avec un decompte de n secondes par phase.)' -ForegroundColor DarkGray
} else {
    Write-Host ("  Rythme AUTO : decompte de {0}s par phase avant chaque scan (relance sans -Prep pour le mode interactif)." -f $Prep) -ForegroundColor DarkGray
}

Invoke-Sequence

# --- Cloture : on s assure que la comm est revenue au nominal --------------------------------
Write-Host ''
Write-Host 'AVANT DE PARTIR : verifie dans le panneau comm que "Geler retours" et "Couper TCP" sont' -ForegroundColor Yellow
Write-Host '  DECOCHES, et que plus aucun element ne porte de badge de defaut (sinon appuie sur R).' -ForegroundColor Yellow
Write-Host ''
Write-Host 'Rappel D-013 : si SimHost tenait le port 502, la scene Godot demarrerait SANS serveur' -ForegroundColor DarkGray
Write-Host '  et afficherait un bandeau rouge (voir le pre-vol en tete de ce script).' -ForegroundColor DarkGray
Write-Host ''
Write-Host 'Demo terminee.' -ForegroundColor Green
exit 0

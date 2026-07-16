# =============================================================================
# demo_sprint_03.ps1 - Demo guidee de la CHAINE DE COMMANDE par element (sprint 3)
# =============================================================================
#
# But : jouer le role du M580 (I/O Scanner) et enchainer AUTOMATIQUEMENT des scenarios,
# pour que tu n'aies qu'a REGARDER la fenetre Godot pendant que la console annonce chaque
# phase. La nouveaute S3.3 : chaque element 3D porte une ETIQUETTE "cmd -> physique -> ret"
# (avec l'adresse %MW exacte lue du pivot) ET change de COULEUR selon son etat :
#   - tige de verin : teintee en ambre a mesure qu'elle sort (degrade selon la course) ;
#   - anneau du convoyeur : passe au VERT quand KM1_AUX est ferme (convoyeur confirme en marche) ;
#   - fenetre d'un capteur : s'ALLUME en vert quand une palette est presente (B1/B2 = 1).
# La demo te dit, a chaque phase, quelle etiquette lire et quelle couleur doit basculer.
#
# Ce script ne lance PAS Godot : tu lances la scene toi-meme (F5), puis ce script dans un autre
# terminal. Il ecrit les commandes (FC16) via testbench/io_scanner_sim.py, comme la demo sprint 2.
#
# Rappel du piege (D-013, solde S3.2) : si un autre process tient deja le port 502, la scene
# Godot demarre SANS serveur et affiche un bandeau rouge. Le pre-vol ci-dessous verifie donc QUI
# ecoute sur 502 et refuse de tourner si ce n'est pas la scene Godot.
#
# NB encodage : ASCII pur (pas d'accents ni de tirets longs). Windows PowerShell 5.1 lit les .ps1
# sans BOM en Windows-1252 ; un caractere multi-octet dans une chaine casserait le parseur.
#
# Usage :
#   powershell -File runtime/scripts/demo_sprint_03.ps1
#   powershell -File runtime/scripts/demo_sprint_03.ps1 -Repeat        # boucle jusqu'a Ctrl+C
#   powershell -File runtime/scripts/demo_sprint_03.ps1 -PyHost 127.0.0.1 -Port 502
#
# Prerequis : la scene Godot LANCEE (F5) et a l'ecoute sur 502 ; Python + pymodbus.

param(
    [string]$PyHost = '127.0.0.1',
    [int]$Port = 502,
    [switch]$Repeat        # rejoue la sequence en boucle (pour une demo continue)
)

# On NE met PAS $ErrorActionPreference='Stop' : io_scanner ecrit ses echecs de connexion sur
# stderr tant que le serveur n'ecoute pas ; sous 'Stop' ce stderr deviendrait fatal.

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
# Sur ce poste 'pip' seul n'existe pas mais 'python' et 'py' marchent ; on prend ce qui repond.
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
    # Cas non prevu (autre serveur Modbus ?) : on avertit mais on laisse jouer.
    Write-Host "Avertissement : le port $Port est tenu par '$owner' (attendu : Godot)." -ForegroundColor Yellow
}
Write-Host "Serveur sur ${PyHost}:${Port} tenu par '$owner' - OK." -ForegroundColor Green

# --- Une phase = un forcage de commandes (FC16) + N cycles de lecture (io_scanner) -----------
# io_scanner ecrit la zone cmd COMPLETE a chaque appel (les bits non forces retombent a 0) puis
# scrute --cycles fois a --period. Le datastore CONSERVE la derniere commande ecrite, donc l'etat
# persiste d'une phase a l'autre. La console affiche les retours (HB, S11/S12, KM1_AUX, B1/B2)
# EXACTEMENT ce que montrent les etiquettes 3D : de quoi comparer texte console <-> texte 3D.
$period = 0.1                       # 100 ms = cadence de scan (aligne sur le heartbeat)

function Invoke-Phase {
    param(
        [string]  $Title,           # titre de la phase
        [string[]]$Watch,           # ce qu'il faut regarder dans la 3D (une ligne par element)
        [string[]]$CmdArgs,         # forcages io_scanner, ex. @('--run','1','--yv1','1')
        [int]     $Cycles           # duree = Cycles * period secondes
    )
    $dur = [math]::Round($Cycles * $period, 1)
    Write-Host ''
    Write-Host ('=== {0}   (~{1}s) ===' -f $Title, $dur) -ForegroundColor Cyan
    foreach ($w in $Watch) { Write-Host ("  a regarder : " + $w) -ForegroundColor White }
    Write-Host ('  commande M580 : ' + ($CmdArgs -join ' ')) -ForegroundColor DarkGray
    Start-Sleep -Milliseconds 900   # laisse le temps de lire la banniere avant le flot de lignes

    Push-Location $testbench
    & $py io_scanner_sim.py --host $PyHost --port $Port @CmdArgs --cycles $Cycles --period $period
    $rc = $LASTEXITCODE
    Pop-Location
    if ($rc -ne 0) {
        Write-Host "  (io_scanner a renvoye $rc - serveur injoignable ? phase interrompue)" -ForegroundColor Yellow
    }
}

function Invoke-Sequence {
    # 1) Etat de repos : tout a 0. Point de depart connu, toutes les etiquettes a l'etat repos.
    Invoke-Phase -Title 'PHASE 1/6 : REPOS' `
        -Watch @('etiquette KM1 : cmd_run %MW100.0=0  ->  arret  ->  KM1_AUX %MW201.6=0.',
                 'etiquettes YV1/YV2 : tige 0% (rentre), S11/S21=1 (verins rentres).',
                 'couleurs : anneau GRIS, tiges CLAIRES, fenetres capteurs BLEUES (eteintes).') `
        -CmdArgs @('--run','0','--yv1','0','--yv2','0') -Cycles 15

    # 2) Convoyeur en marche : KM1_AUX passe a 1 (apres le delai contacteur) -> anneau vert.
    Invoke-Phase -Title 'PHASE 2/6 : CONVOYEUR ON' `
        -Watch @('etiquette KM1 : cmd_run passe a 1, puis KM1_AUX %MW201.6 bascule a 1.',
                 'couleur : l anneau du convoyeur passe au VERT (retour de marche confirme).',
                 'les 3 palettes tournent en CCW.') `
        -CmdArgs @('--run','1') -Cycles 80

    # 3) Blocage YV1 (poste 90 deg) : cmd->tige->S12, tige teintee, B1 s allume, palette bloquee.
    Invoke-Phase -Title 'PHASE 3/6 : BLOCAGE YV1 (poste 90 deg)' `
        -Watch @('etiquette YV1 : cmd_extend %MW100.1=1  ->  tige monte de 0% a 100% (sort)  ->  S12 %MW201.1 passe a 1.',
                 'couleur : la tige du verin 1 se TEINTE en ambre a mesure qu elle sort.',
                 'etiquette B1 : palette presente  ->  B1 %MW201.4=1 ; la fenetre capteur 1 s ALLUME (vert).',
                 'une palette se BLOQUE a 90 deg, les suivantes s accumulent derriere.') `
        -CmdArgs @('--run','1','--yv1','1') -Cycles 90

    # 4) Rappel ressort YV1 : commande a 0 -> tige redescend (monostable), S11 revient a 1, flux repart.
    Invoke-Phase -Title 'PHASE 4/6 : RAPPEL RESSORT YV1' `
        -Watch @('etiquette YV1 : cmd_extend %MW100.1=0  ->  tige redescend (rentre)  ->  S11 %MW201.0 revient a 1.',
                 'couleur : la tige du verin 1 REDEVIENT claire ; la fenetre B1 s ETEINT quand la palette repart.',
                 'les palettes accumulees REPARTENT.') `
        -CmdArgs @('--run','1','--yv1','0') -Cycles 60

    # 5) Blocage YV2 (poste 270 deg, devant) : meme chaine sur le second poste.
    Invoke-Phase -Title 'PHASE 5/6 : BLOCAGE YV2 (poste 270 deg)' `
        -Watch @('etiquette YV2 : cmd_extend %MW100.2=1  ->  tige sort  ->  S22 %MW201.3 passe a 1.',
                 'couleur : tige du verin 2 teintee ; fenetre capteur 2 s allume  ->  B2 %MW201.5=1.') `
        -CmdArgs @('--run','1','--yv2','1') -Cycles 90

    # 6) Arret general : tout a 0. Anneau redevient gris apres le delai contacteur.
    Invoke-Phase -Title 'PHASE 6/6 : ARRET GENERAL' `
        -Watch @('etiquette YV2 : la tige redescend, S21 revient a 1.',
                 'etiquette KM1 : KM1_AUX %MW201.6 retombe a 0  ->  l anneau redevient GRIS.',
                 'toutes les etiquettes reviennent a l etat repos.') `
        -CmdArgs @('--run','0','--yv1','0','--yv2','0') -Cycles 40
}

Write-Host ''
Write-Host 'Demo chaine de commande sprint 3 - regarde la fenetre Godot :'
Write-Host '  chaque element porte une etiquette "cmd %MWx.y -> etat physique -> ret %MWa.b" et change de couleur.'
do {
    Invoke-Sequence
    if ($Repeat) { Write-Host ''; Write-Host '--- fin de sequence, on rejoue (Ctrl+C pour arreter) ---' -ForegroundColor DarkGray }
} while ($Repeat)

Write-Host ''
Write-Host 'Demo terminee.' -ForegroundColor Green
exit 0

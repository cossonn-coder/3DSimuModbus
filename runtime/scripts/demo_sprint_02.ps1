# =============================================================================
# demo_sprint_02.ps1 - Demo VISUELLE guidee de la cinematique (sprint 2)
# =============================================================================
#
# But : jouer le role du M580 (I/O Scanner) et enchainer AUTOMATIQUEMENT les scenarios
# de la maquette, pour que tu n'aies qu'a REGARDER la fenetre Godot pendant que la console
# annonce chaque phase et affiche les retours decodes.
#
# Ce script ne lance PAS Godot : tu lances la scene toi-meme dans l'editeur (F5), puis tu
# lances ce script dans un autre terminal. Il ecrit les commandes (FC16) via le meme outil
# que la validation manuelle (testbench/io_scanner_sim.py) et laisse tourner chaque phase.
#
# Rappel du piege deja rencontre : si un SimHost (ou un ancien lancement) tient deja le port
# 502, la scene Godot demarre SANS serveur (bind echoue en silence) et reste figee. Le pre-vol
# ci-dessous verifie donc QUI ecoute sur 502 et refuse de tourner si ce n'est pas la scene Godot.
#
# NB encodage : ASCII pur (pas d'accents ni de tirets longs). Windows PowerShell 5.1 lit les .ps1
# sans BOM en Windows-1252 ; un caractere multi-octet dans une chaine casserait le parseur.
#
# Usage :
#   powershell -File runtime/scripts/demo_sprint_02.ps1
#   powershell -File runtime/scripts/demo_sprint_02.ps1 -Repeat        # boucle jusqu'a Ctrl+C
#   powershell -File runtime/scripts/demo_sprint_02.ps1 -PyHost 127.0.0.1 -Port 502
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
# persiste d'une phase a l'autre. Chaque phase affiche donc en direct les retours (HB, S11/S12,
# KM1_AUX, B1/B2) pendant que tu regardes bouger la 3D.
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
    # 1) Etat de repos : on remet tout a 0 pour un point de depart connu (verins rentres, arret).
    Invoke-Phase -Title 'PHASE 1/6 : REPOS' `
        -Watch @('tout est immobile, les deux tiges de verin sont rentrees.') `
        -CmdArgs @('--run','0','--yv1','0','--yv2','0') -Cycles 15

    # 2) Convoyeur en marche : les palettes tournent (KM1_AUX passe a 1 apres le delai contacteur).
    Invoke-Phase -Title 'PHASE 2/6 : CONVOYEUR ON' `
        -Watch @('les 3 palettes tournent en CCW (sens trigo, vu de dessus).',
                 'console : KM1_AUX passe de 0 a 1 (retour de marche du contacteur).') `
        -CmdArgs @('--run','1') -Cycles 80

    # 3) Blocage YV1 (poste 90 deg) : la tige sort, une palette bute au poste, les autres s'accumulent.
    Invoke-Phase -Title 'PHASE 3/6 : BLOCAGE YV1 (poste 90 deg)' `
        -Watch @('la tige du verin 1 MONTE (poste au fond).',
                 'une palette se BLOQUE a 90 deg, les suivantes s ACCUMULENT derriere (ecart ~20 deg).',
                 'console : S12=1 (verin sorti), B1=1 (palette presente au poste 1).') `
        -CmdArgs @('--run','1','--yv1','1') -Cycles 80

    # 4) Rappel ressort YV1 : commande retombee a 0 -> le verin monostable redescend, le flux repart.
    Invoke-Phase -Title 'PHASE 4/6 : RAPPEL RESSORT YV1' `
        -Watch @('la tige du verin 1 REDESCEND toute seule (monostable, rappel par ressort).',
                 'les palettes accumulees REPARTENT.',
                 'console : S12 retombe a 0, S11 revient a 1 (verin rentre).') `
        -CmdArgs @('--run','1','--yv1','0') -Cycles 60

    # 5) Blocage YV2 (poste 270 deg, devant) : meme scenario sur le second poste.
    Invoke-Phase -Title 'PHASE 5/6 : BLOCAGE YV2 (poste 270 deg)' `
        -Watch @('la tige du verin 2 MONTE (poste devant).',
                 'une palette se bloque a 270 deg, accumulation derriere.',
                 'console : S22=1, B2=1.') `
        -CmdArgs @('--run','1','--yv2','1') -Cycles 80

    # 6) Arret general : tout retombe a 0. Le convoyeur s'arrete apres le delai contacteur.
    Invoke-Phase -Title 'PHASE 6/6 : ARRET GENERAL' `
        -Watch @('la tige du verin 2 redescend, puis tout s immobilise.',
                 'console : KM1_AUX retombe a 0 (convoyeur a l arret).') `
        -CmdArgs @('--run','0','--yv1','0','--yv2','0') -Cycles 40
}

Write-Host ''
Write-Host 'Demo cinematique sprint 2 - regarde la fenetre Godot, la console annonce chaque phase.'
do {
    Invoke-Sequence
    if ($Repeat) { Write-Host ''; Write-Host '--- fin de sequence, on rejoue (Ctrl+C pour arreter) ---' -ForegroundColor DarkGray }
} while ($Repeat)

Write-Host ''
Write-Host 'Demo terminee.' -ForegroundColor Green
exit 0

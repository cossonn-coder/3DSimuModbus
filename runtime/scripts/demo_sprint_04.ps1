# =============================================================================
# demo_sprint_04.ps1 - Demo guidee de l'ERGONOMIE d'utilisation (sprint 4)
# =============================================================================
#
# But : jouer le role du M580 (I/O Scanner) et animer la machine pendant que la console
# te GUIDE A LA VOIX pour eprouver les nouveautes du sprint 4 :
#   - S4.1 Navigation camera CAO : fenetre Maximized ; bouton du MILIEU = orbite ;
#     Shift+MILIEU = pan ; molette = zoom (vitesse ~ distance) ; F11 = plein ecran.
#   - S4.2 Panneau des elements : une ligne par element (KM1, YV1, YV2, B1, B2) avec
#     son etat et ses adresses %MW (colonnes Repere | Type | Etat | cmd %MW=bit | ret %MW=bit).
#   - S4.2+S4.3 Surbrillance croisee : survoler une LIGNE allume l'element 3D, et
#     survoler l'element 3D surligne sa LIGNE (rendu symetrique, canal emission).
# La souris ne peut PAS etre automatisee : le script anime la machine (commandes FC16) et
# te DIT quoi regarder et quoi faire a la souris a chaque phase.
#
# Ce script ne lance PAS Godot : tu lances la scene toi-meme (F5), puis ce script dans un
# autre terminal. Il ecrit les commandes (FC16) via testbench/io_scanner_sim.py (il joue le
# M580 : il ECRIT la zone cmd, il LIT la zone ret ; jamais l'inverse).
#
# Rappel du piege (D-013) : si un autre process (SimHost) tient deja le port 502, la scene
# Godot demarre SANS serveur et affiche un bandeau rouge. Le pre-vol ci-dessous verifie donc
# QUI ecoute sur 502 et refuse de tourner si ce n'est pas la scene Godot.
#
# NB encodage : ASCII pur (pas d'accents ni de tirets longs). Windows PowerShell 5.1 lit les
# .ps1 sans BOM en Windows-1252 ; un caractere multi-octet dans une chaine casserait le parseur.
#
# Usage :
#   powershell -File runtime/scripts/demo_sprint_04.ps1
#   powershell -File runtime/scripts/demo_sprint_04.ps1 -Repeat        # boucle jusqu'a Ctrl+C
#   powershell -File runtime/scripts/demo_sprint_04.ps1 -PyHost 127.0.0.1 -Port 502
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
# EXACTEMENT ce que montre la colonne "Etat" du panneau : de quoi comparer console <-> panneau.
$period = 0.1                       # 100 ms = cadence de scan (aligne sur le heartbeat)

function Invoke-Phase {
    param(
        [string]  $Title,           # titre de la phase
        [string[]]$Watch,           # ce qu'il faut regarder / faire (une ligne par consigne)
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
    # 1) Navigation camera (S4.1). Convoyeur en marche pour que ca tourne pendant que tu navigues.
    Invoke-Phase -Title 'PHASE 1/5 : NAVIGATION CAMERA (S4.1)' `
        -Watch @('la fenetre est MAXIMISEE au lancement ; MSAA lisse les bords.',
                 'bouton du MILIEU enfonce + souris = ORBITE autour de la machine.',
                 'Shift + bouton du MILIEU = PAN (translation laterale).',
                 'molette = ZOOM (la vitesse depend de la distance ; jamais sous le sol).',
                 'appuie sur F11 : plein ecran ; F11 encore : retour fenetre.') `
        -CmdArgs @('--run','1') -Cycles 90

    # 2) Panneau des elements (S4.2) : lecture des lignes, puis on sort YV1 pour voir une ligne bouger.
    Invoke-Phase -Title 'PHASE 2/5 : PANNEAU DES ELEMENTS (S4.2)' `
        -Watch @('regarde le panneau lateral DROIT : une ligne par element (KM1, YV1, YV2, B1, B2).',
                 'colonnes : Repere | Type | Etat | cmd %MW=bit | ret %MW=bit (adresses lues du pivot).',
                 'ligne KM1 : Etat MARCHE ; ret KM1_AUX %MW201.6=1 (convoyeur confirme).',
                 'on va SORTIR YV1 : sa ligne doit passer a tige 100% (sorti), S11=0 puis S12=1.') `
        -CmdArgs @('--run','1','--yv1','1') -Cycles 90

    # 3) Rappel ressort YV1 : la ligne YV1 revient (monostable) -> compare panneau <-> console.
    Invoke-Phase -Title 'PHASE 3/5 : YV1 RENTRE (monostable, S4.2)' `
        -Watch @('cmd_extend YV1 %MW100.1 repasse a 0 : la ligne YV1 revient a tige 0% (rentre).',
                 'S12 %MW201.1 retombe a 0, S11 %MW201.0 revient a 1 (verin rentre confirme).',
                 'la colonne Etat du panneau suit l animation en direct.') `
        -CmdArgs @('--run','1','--yv1','0') -Cycles 60

    # 4) Surbrillance croisee (S4.2 + S4.3) : on bloque YV1 pour amener/eclairer une palette au capteur.
    Invoke-Phase -Title 'PHASE 4/5 : SURBRILLANCE CROISEE (S4.2 + S4.3)' `
        -Watch @('SURVOLE la ligne YV1 dans le panneau : la tige du verin 90 deg s ALLUME (glow emission).',
                 'SURVOLE directement la tige du verin dans la 3D : sa LIGNE se surligne (rendu symetrique).',
                 'la coloration d ETAT (albedo) n est jamais perdue : le glow s ajoute, puis s eteint.',
                 'YV1 sorti bloque une palette -> B1 %MW201.4=1 ; survole B1 pour eclairer sa fenetre capteur.') `
        -CmdArgs @('--run','1','--yv1','1') -Cycles 120

    # 5) Retour au repos : tout a 0. Le panneau revient a l etat de repos, l anneau redevient gris.
    Invoke-Phase -Title 'PHASE 5/5 : RETOUR AU REPOS' `
        -Watch @('cmd tout a 0 : YV1/YV2 rentrent, KM1_AUX %MW201.6 retombe a 0.',
                 'l anneau redevient GRIS, les fenetres capteurs s eteignent.',
                 'toutes les lignes du panneau reviennent a l etat de repos.') `
        -CmdArgs @('--run','0','--yv1','0','--yv2','0') -Cycles 40
}

Write-Host ''
Write-Host 'Demo ergonomie sprint 4 - regarde la fenetre Godot et suis le guidage console :'
Write-Host '  navigation camera (souris/F11), panneau des elements (%MW), surbrillance croisee ligne <-> 3D.'
do {
    Invoke-Sequence
    if ($Repeat) { Write-Host ''; Write-Host '--- fin de sequence, on rejoue (Ctrl+C pour arreter) ---' -ForegroundColor DarkGray }
} while ($Repeat)

# --- Rappel D-013 (opportuniste, zero code) --------------------------------------------------
Write-Host ''
Write-Host 'Rappel D-013 : si SimHost tenait le port 502, la scene Godot demarrerait SANS serveur' -ForegroundColor DarkGray
Write-Host '  et afficherait un bandeau rouge. Pour le verifier une fois : lance SimHost puis F5,' -ForegroundColor DarkGray
Write-Host '  observe le bandeau, puis Stop-Process -Name SimHost -Force et relance la scene.' -ForegroundColor DarkGray
Write-Host ''
Write-Host 'Demo terminee.' -ForegroundColor Green
exit 0

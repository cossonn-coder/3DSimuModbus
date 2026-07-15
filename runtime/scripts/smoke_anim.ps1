# =============================================================================
# smoke_anim.ps1 - smoke-test headless de la CINEMATIQUE VISUELLE (sous-sprint S2.2)
# =============================================================================
#
# But : prouver, sans oeil humain, que ApplyToScene recopie bien l'etat de la simulation
# sur la geometrie 3D. On lance la scene en --headless, on joue le M580 via un petit client
# Modbus (cmd_run=1 + YV1=cmd_extend), on laisse tourner N frames, puis on ASSERTE sur la
# trace de la sonde de diagnostic (lignes prefixees [anim]) :
#   - la tige du cylinder_1 a MONTE (rod1Y a augmente : le verin sort en +Y) ;
#   - au moins une palette a AVANCE (angle change : le convoyeur entraine les palettes) ;
#   - le heartbeat s'est incremente (la simulation vit).
# On verifie aussi le recensement statique de la brique 5 (ring=1 cylinders=2 pallets=3 sensors=2).
# Ceci solde D-010 (smoke headless de la maquette animee) pour la partie observable sans editeur.
#
# Pourquoi patcher main.tscn : la scene doit ecouter en LOOPBACK (test local isole, sans exposer
# le port 502) et imprimer la sonde. Les deux sont des [Export] lus par Godot AVANT _Ready ; on les
# force donc en ajoutant BindLoopback=true + DiagAnim=true au noeud racine, puis on RESTAURE le
# fichier a l'identique (copie de sauvegarde) : la scene de prod reste inchangee (IPAddress.Any, muette).
#
# NB encodage : ce script est volontairement en ASCII pur (pas d'accents ni de tirets longs). Windows
# PowerShell 5.1 lit les .ps1 sans BOM en Windows-1252 ; un caractere multi-octet dans une chaine
# casserait le parseur. On garde donc l'ASCII pour une robustesse totale quel que soit l'encodage.
#
# Usage :
#   powershell -File runtime/scripts/smoke_anim.ps1
#   $env:GODOT = 'C:\chemin\Godot_mono_console.exe'; powershell -File runtime/scripts/smoke_anim.ps1
#
# Prerequis : Godot 4.6 .NET (mono), Python + pymodbus (testbench/requirements.txt).

param(
    [string]$Godot = $env:GODOT,
    [int]$QuitAfter = 3000   # iterations de boucle principale ; ~15-25 s en temps reel (cadence Godot)
)

# NB : on NE met PAS $ErrorActionPreference='Stop'. Le client python ecrit ses echecs de connexion
# sur stderr tant que le serveur n'ecoute pas encore ; sous 'Stop' ce stderr (NativeCommandError)
# deviendrait fatal et casserait la boucle de retry. On teste donc les codes de retour explicitement.
if (-not $Godot) {
    $Godot = 'C:\Users\Nicol\Documents\Godot_v4.6-stable_mono_win64\Godot_v4.6-stable_mono_win64_console.exe'
}

# Arborescence : scripts/ -> runtime/ (projet Godot) -> racine repo (testbench/ a cote).
$scriptDir  = $PSScriptRoot
$projectDir = Split-Path -Parent $scriptDir
$repoDir    = Split-Path -Parent $projectDir
$tscn       = Join-Path $projectDir 'scenes\main.tscn'
$testbench  = Join-Path $repoDir 'testbench'
$census     = '[carrousel] ring=1 cylinders=2 pallets=3 sensors=2'

$outFile = Join-Path $env:TEMP 'smoke_anim_out.txt'
$errFile = Join-Path $env:TEMP 'smoke_anim_err.txt'
$tscnBak = "$tscn.smokebak"

Write-Host "Godot   : $Godot"
Write-Host "Projet  : $projectDir"

# --- 0) Compiler l'assembly .NET AVANT de lancer la scene ------------------------------------
# On build explicitement via dotnet (et NON via --build-solutions, qui basculerait Godot en mode
# EDITEUR au lieu d'executer la scene de jeu). Ainsi le DLL est pret et Godot demarre directement
# la scene principale en --headless.
Write-Host 'Build   : dotnet build DemonstrateurCarrousel.csproj ...'
& dotnet build (Join-Path $projectDir 'DemonstrateurCarrousel.csproj') -nologo -v quiet
if ($LASTEXITCODE -ne 0) {
    Write-Error 'SMOKE KO : echec du build .NET'
    exit 1
}

# --- 1) Sauvegarde byte-a-byte + patch de main.tscn (loopback + sonde) -----------------------
Copy-Item $tscn $tscnBak -Force
try {
    Add-Content -Path $tscn -Value 'BindLoopback = true'
    Add-Content -Path $tscn -Value 'DiagAnim = true'

    if (Test-Path $outFile) { Remove-Item $outFile -Force }
    if (Test-Path $errFile) { Remove-Item $errFile -Force }

    # --- 2) Lancer Godot headless en arriere-plan (sortie redirigee vers un fichier) ---------
    $godotArgs = @('--headless', '--path', $projectDir, '--quit-after', "$QuitAfter")
    $proc = Start-Process -FilePath $Godot -ArgumentList $godotArgs `
        -RedirectStandardOutput $outFile -RedirectStandardError $errFile -NoNewWindow -PassThru
    # Quirk Start-Process : sans acceder au Handle juste apres le lancement, .ExitCode reste $null
    # apres la sortie (l'objet ne met pas le code en cache). On force donc la mise en cache ici.
    $null = $proc.Handle

    # --- 3) Ecrire les commandes du M580 des que le serveur ecoute -----------------------------
    # On rejoue l'I/O Scanner (FC16) : cmd_run=1 (convoyeur) + YV1=cmd_extend (cylinder_1 sort).
    # Le datastore CONSERVE les commandes ecrites : un seul FC16 suffit, la sim les relira a chaque
    # tick meme apres deconnexion du client. On retente tant que le serveur n'est pas pret (build+_Ready).
    $written = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        if ($proc.HasExited) { break }
        Push-Location $testbench
        $null = & python io_scanner_sim.py --run 1 --yv1 1 --cycles 3 --period 0.1 2>&1
        $rc = $LASTEXITCODE
        Pop-Location
        if ($rc -eq 0) { $written = $true; break }
    }
    if (-not $written) {
        if (-not $proc.HasExited) { $proc.Kill() }
        Write-Error 'SMOKE KO : serveur Modbus injoignable, commandes non ecrites.'
        exit 1
    }
    Write-Host 'Commandes ecrites (FC16) : KM1.cmd_run=1, cylinder_1.cmd_extend=1'

    # --- 4) Attendre la fin propre de Godot (quit-after -> _ExitTree dispose le serveur) --------
    $proc.WaitForExit()
    $code = $proc.ExitCode
}
finally {
    # Restauration systematique de main.tscn a l'identique (meme si une exception est levee).
    Copy-Item $tscnBak $tscn -Force
    Remove-Item $tscnBak -Force -ErrorAction SilentlyContinue
}

# --- 5) Depouillement de la trace ------------------------------------------------------------
$output = @()
if (Test-Path $outFile) { $output += Get-Content $outFile }
if (Test-Path $errFile) { $output += Get-Content $errFile }
# La trace complete (des centaines de lignes de sonde) reste dans $outFile ; on n'echo au terminal
# que les lignes de banniere/recensement/erreurs, pour garder un verdict lisible.
$animLines = @($output | Where-Object { $_ -match '^\[anim\]' })
$output | Where-Object { $_ -notmatch '^\[anim\]' } | ForEach-Object { Write-Host $_ }
Write-Host ('(trace complete : {0} - {1} lignes de sonde)' -f $outFile, $animLines.Count)

# $code peut rester $null si le quirk de Start-Process n'a pas ete evite : on ne fait echouer que
# sur un code NON nul explicite (la vraie preuve du bon deroulement, ce sont les assertions ci-dessous).
if ($null -ne $code -and $code -ne 0) {
    Write-Error "SMOKE KO : Godot a quitte avec le code $code"
    exit 1
}

# Filet defensif : aucune erreur de script/exception Godot ne doit apparaitre.
$errs = $output | Where-Object { $_ -match 'SCRIPT ERROR|Unhandled exception|ERROR:' }
if ($errs) {
    Write-Error 'SMOKE KO : erreurs detectees dans la sortie Godot'
    exit 1
}

# Recensement statique (brique 5) : la ligne exacte doit apparaitre une fois.
if (-not ($output | Where-Object { $_ -eq $census })) {
    Write-Error "SMOKE KO : ligne de recensement absente ('$census')"
    exit 1
}

# Extraction des lignes de la sonde : [anim] hb=<n> rod1Y=<f> rod2Y=<f> pallets=<a0>,<a1>,...
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$anim = @()
foreach ($line in $animLines) {
    if ($line -match '^\[anim\] hb=(\d+) rod1Y=([-\d.]+) rod2Y=([-\d.]+) pallets=([-\d.,]+)') {
        $anim += [pscustomobject]@{
            Hb      = [int]$Matches[1]
            Rod1Y   = [double]::Parse($Matches[2], $inv)
            Pallets = @($Matches[4] -split ',' | ForEach-Object { [double]::Parse($_, $inv) })
        }
    }
}
if ($anim.Count -lt 2) {
    Write-Error "SMOKE KO : trace de la sonde insuffisante ($($anim.Count) lignes)"
    exit 1
}

$first = $anim[0]
$last  = $anim[$anim.Count - 1]

# Assertion A : heartbeat incremente (la simulation vit).
if ($last.Hb -le $first.Hb) {
    Write-Error "SMOKE KO : heartbeat non incremente ($($first.Hb) -> $($last.Hb))"
    exit 1
}

# Assertion B : la tige du cylinder_1 a monte (verin sort en +Y). Epsilon large : la course fait
# ~0.15 m, un simple depassement du bruit (1 mm) suffit a prouver le mouvement.
if ($last.Rod1Y -le $first.Rod1Y + 0.001) {
    Write-Error "SMOKE KO : tige cylinder_1 non montee (Y $($first.Rod1Y) -> $($last.Rod1Y))"
    exit 1
}

# Assertion C : au moins une palette a avance (angle change > 1 deg : au-dela de tout bruit numerique).
$moved = $false
for ($i = 0; $i -lt $first.Pallets.Count; $i++) {
    if ([math]::Abs($last.Pallets[$i] - $first.Pallets[$i]) -gt 1.0) { $moved = $true; break }
}
if (-not $moved) {
    Write-Error "SMOKE KO : aucune palette n'a bouge (before=$($first.Pallets -join ',') after=$($last.Pallets -join ','))"
    exit 1
}

Write-Host ''
Write-Host 'SMOKE OK - cinematique visuelle validee (headless) :'
Write-Host ('  heartbeat : {0} -> {1}' -f $first.Hb, $last.Hb)
Write-Host ('  rod1 Y    : {0:F5} -> {1:F5} m  (tige montee)' -f $first.Rod1Y, $last.Rod1Y)
Write-Host ('  pallets   : {0}  ->  {1}' -f ($first.Pallets -join ','), ($last.Pallets -join ','))
Write-Host '  recensement statique : conforme (ring=1 cylinders=2 pallets=3 sensors=2)'
exit 0

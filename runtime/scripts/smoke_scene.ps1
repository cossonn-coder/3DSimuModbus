# =============================================================================
# smoke_scene.ps1 — smoke-test headless de la scene 3D (brique 5)
# =============================================================================
#
# But : verifier que main.tscn se CONSTRUIT sans erreur et cree le bon nombre de noeuds,
# sans inspection visuelle manuelle. On lance Godot en --headless quelques frames, puis on
# controle (1) le code de sortie et (2) la ligne de recensement imprimee par CarrouselScene.
#
# Usage :
#   pwsh runtime/scripts/smoke_scene.ps1                 # 'godot' doit etre sur le PATH
#   $env:GODOT = 'C:\chemin\Godot_mono.exe'; pwsh runtime/scripts/smoke_scene.ps1
#
# Prerequis : Godot 4.6 .NET (mono). Le premier lancement compile la solution C# (--build-solutions).

param([string]$Godot = $env:GODOT)

if (-not $Godot) { $Godot = 'godot' }

# runtime/ = dossier parent de scripts/. C'est le --path du projet Godot.
$projectDir = Split-Path -Parent $PSScriptRoot
$expected = '[carrousel] ring=1 cylinders=2 pallets=3 sensors=2'

Write-Host "Godot : $Godot"
Write-Host "Projet : $projectDir"

# --build-solutions : compile l'assembly .NET avant de lancer la scene (indispensable au 1er run).
# --quit-after 5    : rend la main apres ~5 frames, largement de quoi executer _Ready.
$output = & $Godot --headless --path $projectDir --build-solutions --quit-after 5 2>&1
$code = $LASTEXITCODE

$output | ForEach-Object { Write-Host $_ }

if ($code -ne 0) {
    Write-Error "SMOKE KO : Godot a quitte avec le code $code"
    exit 1
}

$census = $output | Where-Object { $_ -match [regex]::Escape($expected) }
if (-not $census) {
    Write-Error "SMOKE KO : ligne de recensement attendue absente ('$expected')"
    exit 1
}

# S4.2 : le panneau lateral cree une ligne par composant du pivot (5 : convoyeur + 2 verins +
# 2 capteurs). On verifie la trace deterministe imprimee par ElementPanel.Build (peuplement pivot).
$expectedPanel = '[panel] rows=5'
$panel = $output | Where-Object { $_ -match [regex]::Escape($expectedPanel) }
if (-not $panel) {
    Write-Error "SMOKE KO : trace panneau attendue absente ('$expectedPanel')"
    exit 1
}

# Filet defensif : aucune erreur de script Godot ne doit apparaitre dans la sortie.
$errors = $output | Where-Object { $_ -match 'SCRIPT ERROR|Unhandled exception|ERROR:' }
if ($errors) {
    Write-Error "SMOKE KO : erreurs detectees dans la sortie Godot"
    exit 1
}

Write-Host 'SMOKE OK : scene construite, recensement conforme, aucune erreur.'
exit 0

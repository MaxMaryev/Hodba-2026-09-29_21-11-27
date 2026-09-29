param(
    [ValidateSet('All','Rocks','Textures','Traveler','Audio','Verify')][string]$Stage = 'All',
    [string]$Blender = 'D:\Max\Tools\Blender\blender-4.5.9-windows-x64\blender.exe'
)
$ErrorActionPreference = 'Stop'
$fieldRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not (Test-Path -LiteralPath $Blender)) { throw 'Supply -Blender with the path to Blender 4.5.9 LTS.' }
Push-Location $fieldRoot
try {
    $version = & $Blender --version
    if ($version[0] -notmatch '4\.5\.9') { throw "Expected Blender 4.5.9 LTS, found $($version[0])" }
    $scripts = switch ($Stage) {
        'All' { 'rocks.py'; 'textures.py'; 'traveler.py'; 'audio.py' }
        'Rocks' { 'rocks.py' }
        'Textures' { 'textures.py' }
        'Traveler' { 'traveler.py' }
        'Audio' { 'audio.py' }
    }
    foreach ($script in $scripts) {
        & $Blender --background --factory-startup --threads 4 --python-exit-code 1 --python "ArtSource/Field/$script"
        if ($LASTEXITCODE -ne 0) { throw "$script failed with exit $LASTEXITCODE" }
    }
    $python = Join-Path (Split-Path $Blender) '4.5\python\bin\python.exe'
    foreach ($test in @('test_rocks.py','test_textures.py','test_traveler.py','test_audio.py')) {
        & $python (Join-Path $PSScriptRoot $test)
        if ($LASTEXITCODE -ne 0) { throw "$test failed with exit $LASTEXITCODE" }
    }
    & $Blender --background --threads 2 --python-exit-code 1 --python 'ArtSource/Field/validate_motion.py'
    if ($LASTEXITCODE -ne 0) { throw 'Evaluated motion validation failed' }
    & $Blender --background --threads 2 --python-exit-code 1 --python 'ArtSource/Field/validate_traveler_mesh.py'
    if ($LASTEXITCODE -ne 0) { throw 'Traveler topology validation failed' }
    Write-Host 'Asset checks passed. In Unity run Hodba > Field > Build and Validate.'
} finally { Pop-Location }

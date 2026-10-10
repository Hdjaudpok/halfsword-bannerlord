# Build output -> <Bannerlord>\Modules\HalfSwordCombat. Adds a module folder; touches nothing else in the game.
param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dll = Join-Path $root 'src\HalfSwordCombat\bin\Release\HalfSwordCombat.dll'
if (-not (Test-Path $dll)) { throw "build first: $dll" }
$mod = Join-Path $GameDir 'Modules\HalfSwordCombat'
$bin = Join-Path $mod 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item (Join-Path $root 'module\SubModule.xml') $mod -Force
# Our DLL plus its private dependencies (BEPUphysics v1). Game assemblies are never copied (Private=false).
Get-ChildItem (Split-Path $dll) -Filter *.dll | Copy-Item -Destination $bin -Force
$bepuLicense = Join-Path (Split-Path $root -Parent) 'deps\bepuphysics1\LICENSE.md'
if (Test-Path $bepuLicense) { Copy-Item $bepuLicense (Join-Path $mod 'BEPUphysics-LICENSE.md') -Force }
"deployed -> $mod"
(Get-FileHash (Join-Path $bin 'HalfSwordCombat.dll') -Algorithm SHA256).Hash

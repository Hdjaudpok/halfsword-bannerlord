# Lab launch: vanilla modules + Harmony + this mod, through BLSE, with the lab switches on.
# Passes the module list on the command line, so LauncherData.xml and the user's TAOM shortcuts stay untouched.
param(
  [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord',
  [switch]$NoAutoQuit,
  [string]$Probe = 'ragdoll,bone'   # ragdoll | force | bone, comma-separated
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$logs = Join-Path $root 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
$log = Join-Path $logs ("lab-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
$env:HSC_LAB = '1'
$env:HSC_LOG = $log
$env:HSC_AUTOQUIT = if ($NoAutoQuit) { '0' } else { '1' }
$env:HSC_PROBE = $Probe
if ($env:HSC_DRIVER_VISUAL -eq $null) { $env:HSC_DRIVER_VISUAL = '0' }
$mods = '_MODULES_*Bannerlord.Harmony*Native*SandBoxCore*Sandbox*StoryMode*CustomBattle*HalfSwordCombat*_MODULES_'
$exe = Join-Path $GameDir 'bin\Win64_Shipping_Client\Bannerlord.BLSE.Standalone.exe'
$p = Start-Process -FilePath $exe -ArgumentList ('"' + $mods + '"') -WorkingDirectory (Split-Path $exe) -PassThru
"PID=$($p.Id)"
"LOG=$log"

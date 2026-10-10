# Lab helper: close Discord before a test run and start it again afterwards (the user's choice, 2026-10-03).
# Why: Discord's overlay hook (DiscordHook64.dll, injected mid-battle even with the overlay toggled off) deadlocks with
# the RenoDX DLSS ReShade add-on on the loader lock (lab runs 11-18, native stacks in MODLOG).
# Kills by exact PID only.
# -HookOff / -HookOn (2026-10-05, the user's choice while in a voice call): keep Discord running but rename its game
# hook DLL (DiscordHook64.dll, injected by the overlay and by discord_clips, which Discord starts when it detects a
# game) so nothing gets injected into Bannerlord; -HookOn puts it back. Voice (discord_voice) is untouched; the in-game
# overlay and clips don't work while it is off. Turn it back on only after Bannerlord has closed.
param([switch]$Stop, [switch]$Start, [switch]$HookOff, [switch]$HookOn)
$state = Join-Path $env:TEMP 'hsc-discord-was-running.txt'
if ($HookOff -or $HookOn) {
  $hooks = @(Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Discord') -Recurse -Filter 'DiscordHook64.dll*' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\modules\\discord_hook-\d+\\' })
  if ($hooks.Count -eq 0) { 'no DiscordHook64.dll found'; return }
  foreach ($h in $hooks) {
    if ($HookOff -and $h.Name -eq 'DiscordHook64.dll') { Rename-Item $h.FullName 'DiscordHook64.dll.hsc-off'; "hook off: $($h.FullName)" }
    elseif ($HookOn -and $h.Name -eq 'DiscordHook64.dll.hsc-off') { Rename-Item $h.FullName 'DiscordHook64.dll'; "hook on: $($h.DirectoryName)\DiscordHook64.dll" }
    else { "unchanged: $($h.FullName)" }
  }
  return
}
if ($Stop) {
  $procs = @(Get-Process -Name 'Discord' -ErrorAction SilentlyContinue)
  if ($procs.Count -eq 0) { 'Discord not running'; return }
  $path = ($procs | Where-Object { $_.Path } | Select-Object -First 1).Path
  Set-Content -Path $state -Value $path -Encoding UTF8
  foreach ($p in $procs) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
  Start-Sleep -Milliseconds 800
  $left = @(Get-Process -Name 'Discord' -ErrorAction SilentlyContinue).Count
  "Discord stopped ($($procs.Count) processes, $left left), will restart from $path"
}
if ($Start) {
  if (-not (Test-Path $state)) { 'Discord was not running before the test, nothing to restart'; return }
  $path = (Get-Content $state -Raw).Trim()
  $update = Join-Path $env:LOCALAPPDATA 'Discord\Update.exe'
  if (Test-Path $update) { Start-Process $update -ArgumentList '--processStart', 'Discord.exe' } else { Start-Process $path }
  Remove-Item $state
  'Discord restarted'
}

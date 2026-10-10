# Passthrough: starts the Half Sword demo (with the UE4SS mod HSLink) minimized, so it streams its player to Bannerlord
# without taking the user's screen. Prints its PID (stop it by exact PID only) and waits until HSLink reports the player
# or the timeout passes.
param([int]$WaitSeconds = 90)
Add-Type -Namespace W -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int c);'
$exe = 'C:\Program Files (x86)\Steam\steamapps\common\Half Sword Demo\HalfSwordUE5\Binaries\Win64\HalfSwordUE5-Win64-Shipping.exe'
$log = Join-Path $env:USERPROFILE 'halfsword-decomp\hslink.log'
$mark = (Get-Date)
$p = Start-Process -FilePath $exe -PassThru
"PID=$($p.Id)"
# The player's raw mouse for HSLink (hs_mouse_relay.ps1): hidden, never focused, exits with this Half Sword.
$relay = Start-Process -FilePath powershell -WindowStyle Hidden -PassThru -ArgumentList @(
  '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'hs_mouse_relay.ps1'), '-GamePid', $p.Id)
"mouse relay PID=$($relay.Id)"
$minimized = $false
$deadline = (Get-Date).AddSeconds($WaitSeconds)
while ((Get-Date) -lt $deadline) {
  $p.Refresh()
  if (-not $minimized -and $p.MainWindowHandle -ne [IntPtr]::Zero) { [W.U]::ShowWindow($p.MainWindowHandle, 6) | Out-Null; $minimized = $true; 'minimized' }
  # Only a "player found" after this launch's "# HSLink loaded" line counts (2026-10-05: the last 3 lines still held
  # the previous session's "player found" and the script reported it while the demo sat in its main menu).
  if ((Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $mark) {
    $lines = @(Get-Content $log)
    $loaded = -1
    for ($i = $lines.Count - 1; $i -ge 0; $i--) { if ($lines[$i] -match '# HSLink loaded') { $loaded = $i; break } }
    if ($loaded -ge 0 -and ($lines[$loaded..($lines.Count - 1)] -match 'player found')) { 'player found'; break }
  }
  if ($p.HasExited) { "exited early ($($p.ExitCode))"; break }
  Start-Sleep -Milliseconds 500
}
Get-Content $log -Tail 4 -ErrorAction SilentlyContinue

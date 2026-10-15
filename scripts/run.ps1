param([switch]$Solo, [switch]$HostGame, [string]$Join, [int]$Port = 28747, [int]$Bots = 3, [switch]$Headless,
    [switch]$Dedicated, [switch]$AutoStart, [switch]$Autoplay, [int]$WaitPlayers = 1)
. "$PSScriptRoot/common.ps1"
& "$PSScriptRoot/build.ps1"
$gameArgs = @('--path', (Join-Path $RepoRoot 'src/Game.Client'))
if ($Headless) { $gameArgs += '--headless' }
$gameArgs += @('--', "--port=$Port", "--bots=$Bots")
if ($Solo) { $gameArgs += '--solo' }
if ($HostGame -or $Dedicated) { $gameArgs += '--host' }
if ($Join) { $gameArgs += "--join=$Join" }
if ($Dedicated) { $gameArgs += '--dedicated' }
if ($AutoStart) { $gameArgs += @('--autostart', "--wait-players=$WaitPlayers") }
if ($Autoplay) { $gameArgs += '--autoplay' }
Invoke-Checked (Get-Godot) $gameArgs

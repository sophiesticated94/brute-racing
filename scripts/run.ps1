param([switch]$Solo, [switch]$HostGame, [string]$Join, [int]$Port = 28747, [int]$Bots = 3, [switch]$Headless,
    [switch]$Dedicated, [switch]$AutoStart, [switch]$Autoplay, [int]$WaitPlayers = 1, [ValidateSet("Exhibition","Campaign","Quick")][string]$Mode="Exhibition",
    [ValidateRange(3,10)][int]$CampaignRaces=6,[string]$DirectoryUrl="",[ValidateRange(1,3)][int]$Preset=1,
    [ValidateSet("Stock","FairCap","OpenMayhem")][string]$Balance="")
. "$PSScriptRoot/common.ps1"
& "$PSScriptRoot/build.ps1"
$gameArgs = @('--path', (Join-Path $RepoRoot 'src/Game.Client'))
if ($Headless) { $gameArgs += '--headless' }
$gameArgs += @('--', "--port=$Port", "--bots=$Bots")
if ($Mode -ne "Exhibition") { $gameArgs += @("--mode=$($Mode.ToLowerInvariant())", "--campaign-races=$CampaignRaces", "--preset=$Preset") }
if ($DirectoryUrl) { $gameArgs += "--directory=$DirectoryUrl" }
if ($Balance) { $gameArgs += "--balance=$Balance" }
if ($Solo) { $gameArgs += '--solo' }
if ($HostGame -or $Dedicated) { $gameArgs += '--host' }
if ($Join) { $gameArgs += "--join=$Join" }
if ($Dedicated) { $gameArgs += '--dedicated' }
if ($AutoStart) { $gameArgs += @('--autostart', "--wait-players=$WaitPlayers") }
if ($Autoplay) { $gameArgs += '--autoplay' }
Invoke-Checked (Get-Godot) $gameArgs

param([switch]$Integration, [switch]$Impaired, [switch]$Combat, [switch]$Dedicated, [int]$Clients = 1)
. "$PSScriptRoot/common.ps1"
Invoke-Checked dotnet @('test', (Join-Path $RepoRoot 'MiniCarMayhem.sln'), '--verbosity', 'minimal')
if ($Integration) { & "$PSScriptRoot/integration.ps1" -Impaired:$Impaired -Combat:$Combat -Dedicated:$Dedicated -Clients $Clients }

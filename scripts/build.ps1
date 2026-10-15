param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
. "$PSScriptRoot/common.ps1"
Invoke-Checked dotnet @('build', (Join-Path $RepoRoot 'MiniCarMayhem.sln'), '-c', $Configuration)
Invoke-Checked (Get-Godot) @('--headless', '--editor', '--path', (Join-Path $RepoRoot 'src/Game.Client'), '--import')

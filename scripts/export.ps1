param([ValidateSet('Windows','Linux','All')][string]$Platform = 'All')
. "$PSScriptRoot/common.ps1"
& "$PSScriptRoot/build.ps1" -Configuration Release
$engine = Get-Godot
$exports = @()
if ($Platform -in @('Windows','All')) { $exports += @{ Preset = 'Windows'; Folder = 'windows'; File = 'MiniCarMayhem.exe' } }
if ($Platform -in @('Linux','All')) { $exports += @{ Preset = 'Linux'; Folder = 'linux'; File = 'MiniCarMayhem.x86_64' } }
foreach ($export in $exports) {
    $folder = Join-Path $RepoRoot ('artifacts/' + $export.Folder)
    New-Item -ItemType Directory -Force $folder | Out-Null
    $exportArgs = @('--headless','--path',(Join-Path $RepoRoot 'src/Game.Client'),'--export-release',$export.Preset,(Join-Path $folder $export.File))
    $exportOutput = @(& $engine @exportArgs 2>&1)
    $exportExit = $LASTEXITCODE
    $exportOutput | Write-Output
    if ($exportExit -ne 0 -or ($exportOutput -join "`n") -match '(^|\n)ERROR:') { throw "Eksport $($export.Preset) zakończył się błędem." }
}

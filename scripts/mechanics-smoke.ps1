param([switch]$SkipBuild)
. "$PSScriptRoot/common.ps1"
if (!$SkipBuild) { & "$PSScriptRoot/build.ps1" }
$folder=Join-Path $RepoRoot 'artifacts/tests/mechanics'
New-Item -ItemType Directory -Force $folder | Out-Null
Invoke-Checked (Get-Godot) @('--headless','--path',(Join-Path $RepoRoot 'src/Game.Client'),'--fixed-fps','60','--','--smoke','--bots=7','--balance=open','--weapon-level=5','--mine-level=5','--exercise-mechanics',"--results=$folder/report.json") *> (Join-Path $folder 'engine.log')
$report=Get-Content (Join-Path $folder 'report.json') -Raw | ConvertFrom-Json
if (!$report.success -or $report.events.boost -lt 1 -or $report.events.emp -lt 1) { throw 'Nie wykonano mechanik boost/EMP.' }
Write-Host "PASS: 8 buildów, boost po drifcie i EMP. Raporty: $folder"

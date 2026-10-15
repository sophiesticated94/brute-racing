$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
function Get-Godot {
    if ($env:GODOT_PATH -and (Test-Path -LiteralPath $env:GODOT_PATH)) { return $env:GODOT_PATH }
    $localEngine = Get-ChildItem -LiteralPath (Join-Path $RepoRoot '.tools/godot') -Recurse -Filter '*console.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($localEngine) { return $localEngine.FullName }
    $systemEngine = Get-Command godot -ErrorAction SilentlyContinue
    if ($systemEngine) { return $systemEngine.Source }
    throw 'Brak Godota .NET. Uruchom scripts/setup.ps1 lub ustaw GODOT_PATH.'
}
function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Polecenie zakończone kodem ${LASTEXITCODE}: $Executable" }
}

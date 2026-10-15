param([switch]$Impaired, [switch]$Combat, [switch]$Dedicated, [ValidateRange(1,7)][int]$Clients = 1, [int]$Port = 29847, [ValidateRange(1,3)][int]$Rounds = 1, [switch]$SkipBuild)
. "$PSScriptRoot/common.ps1"
if (!$SkipBuild) { & "$PSScriptRoot/build.ps1" }
$engine = Get-Godot
$testRoot = Join-Path $RepoRoot ('artifacts/tests/' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force $testRoot | Out-Null
$processes = @()
function Start-GameProcess([string]$name, [string[]]$extra) {
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $engine
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $allArgs = @('--headless','--path',(Join-Path $RepoRoot 'src/Game.Client'),'--log-file',"$testRoot/$name.engine.log",'--max-fps','60','--','--smoke',"--port=$Port", "--rounds=$Rounds", "--timeout=$(210 * $Rounds)","--name=$name", "--results=$testRoot/$name.json") + $extra
    if (!$Combat) { $allArgs += '--no-weapons' }
    if ($Impaired) { $allArgs += @('--latency=50','--loss=0.03') }
    foreach ($argument in $allArgs) { $info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::Start($info)
    return [pscustomobject]@{ Process = $process; Output = $process.StandardOutput.ReadToEndAsync(); Error = $process.StandardError.ReadToEndAsync(); Name = $name }
}
try {
    $humans = $Clients + 1
    $hostArgs = @('--host', '--bots=3', "--wait-players=$humans")
    if ($Dedicated) { $hostArgs = @('--host','--dedicated','--bots=3',"--wait-players=$Clients") }
    $processes += Start-GameProcess 'host' $hostArgs
    Start-Sleep -Milliseconds 1800
    for ($clientIndex = 1; $clientIndex -le $Clients; $clientIndex++) {
        $processes += Start-GameProcess "client$clientIndex" @('--join=127.0.0.1','--probe-authority')
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(230 * $Rounds)
    while (@($processes | Where-Object { !$_.Process.HasExited }).Count -gt 0) {
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Przekroczono czas testu integracyjnego.' }
        Start-Sleep -Milliseconds 400
    }
    foreach ($entry in $processes) {
        $stdout = $entry.Output.GetAwaiter().GetResult()
        $stderr = $entry.Error.GetAwaiter().GetResult()
        Set-Content -LiteralPath (Join-Path $testRoot ($entry.Name + '.log')) -Value ($stdout + $stderr)
        if ($entry.Process.ExitCode -ne 0 -or $stderr -match 'ERROR:|Exception') { throw "Błąd procesu $($entry.Name). Zobacz $testRoot" }
    }
    $hostReport = Get-Content -LiteralPath (Join-Path $testRoot 'host.json') -Raw | ConvertFrom-Json
    if (!$hostReport.success -or $hostReport.phase -ne 'Podium' -or $hostReport.checkpointPasses -lt 36) { throw 'Host nie ukończył poprawnego wyścigu.' }
    foreach ($entry in $processes | Where-Object Name -NE 'host') {
        $clientReport = Get-Content -LiteralPath (Join-Path $testRoot ($entry.Name + '.json')) -Raw | ConvertFrom-Json
        if (!$clientReport.success -or $clientReport.receivedSnapshots -lt 30 -or ($hostReport.ranking -join ',') -ne ($clientReport.ranking -join ',')) { throw "Niezgodna klasyfikacja: $($entry.Name)" }
        foreach ($car in $hostReport.vehicles) {
            $clientCar = $clientReport.vehicles | Where-Object id -EQ $car.id
            if ($clientCar.passed -ne $car.passed -or $clientCar.finish -ne $car.finish) { throw "Niezgodny wynik auta $($car.id)" }
        }
        if ($clientReport.rounds.Count -ne $Rounds) { throw 'Nie wykonano wszystkich rewanżów.' }
        foreach ($property in $hostReport.events.psobject.Properties) {
            if ($clientReport.events.($property.Name) -ne $property.Value) { throw "Niezgodna liczba zdarzeń: $($property.Name)" }
        }
    }
    if ($Combat -and ($hostReport.shots -lt 10 -or $hostReport.destructions -lt 1)) { throw 'Nie wykonano scenariusza walki.' }
    Write-Host "PASS: $($processes.Count) procesy, wspólne podium. Raporty: $testRoot"
} finally {
    foreach ($entry in $processes) { if (!$entry.Process.HasExited) { $entry.Process.Kill($true) }; $entry.Process.Dispose() }
}

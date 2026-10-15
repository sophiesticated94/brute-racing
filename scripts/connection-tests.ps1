param([switch]$SkipBuild, [int]$Port = 29855)
. "$PSScriptRoot/common.ps1"
if (!$SkipBuild) { & "$PSScriptRoot/build.ps1" }
$engine = Get-Godot
$testRoot = Join-Path $RepoRoot ('artifacts/tests/connections-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force $testRoot | Out-Null
$running = [System.Collections.Generic.List[object]]::new()
function Launch([string]$name, [int]$udpPort, [string[]]$extra) {
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $engine; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $engineLog = Join-Path $testRoot "$name.engine.log"
    $argsForGame = @('--headless','--path',(Join-Path $RepoRoot 'src/Game.Client'),'--log-file',$engineLog,'--max-fps','60','--',"--port=$udpPort",'--bots=0','--autoplay','--no-weapons',"--name=$name") + $extra
    foreach ($argForGame in $argsForGame) { $info.ArgumentList.Add($argForGame) }
    $process = [System.Diagnostics.Process]::Start($info)
    $entry = [pscustomobject]@{ Process=$process; Name=$name; Log=$engineLog; Output=$process.StandardOutput.ReadToEndAsync(); Error=$process.StandardError.ReadToEndAsync() }
    $running.Add($entry); return $entry
}
function Await-Log($entry, [string]$pattern, [int]$seconds = 15) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ((Test-Path -LiteralPath $entry.Log) -and (Get-Content -LiteralPath $entry.Log -Raw) -match $pattern) { return }
        if ($entry.Process.HasExited) { throw "$($entry.Name) zakończył się przed $pattern" }
        Start-Sleep -Milliseconds 100
    }
    throw "Nie odebrano $pattern z $($entry.Name)"
}
try {
    $hostGame = Launch 'takeover-host' $Port @('--host','--smoke','--wait-players=2',"--results=$testRoot/takeover.json")
    Await-Log $hostGame 'HOST_READY'
    $clientGame = Launch 'takeover-client' $Port @('--join=127.0.0.1')
    Await-Log $hostGame 'RACE_START'
    Start-Sleep -Milliseconds 1200
    $clientGame.Process.Kill($true)
    Await-Log $hostGame 'PLAYER_AI_TAKEOVER' 15
    if (!$hostGame.Process.WaitForExit(150000)) { throw 'Host nie ukończył wyścigu po przejęciu auta przez AI.' }
    if ($hostGame.Process.ExitCode -ne 0) { throw 'Host zakończył się błędem.' }
    $report = Get-Content -LiteralPath (Join-Path $testRoot 'takeover.json') -Raw | ConvertFrom-Json
    if (!$report.success -or $report.vehicles.Count -ne 2) { throw 'Niepoprawny wynik po rozłączeniu klienta.' }

    $lossPort = $Port + 1
    $lostHost = Launch 'loss-host' $lossPort @('--host','--autostart','--wait-players=2')
    Await-Log $lostHost 'HOST_READY'
    $survivingClient = Launch 'loss-client' $lossPort @('--join=127.0.0.1','--exit-on-disconnect')
    Await-Log $survivingClient 'RACE_START'
    $lostHost.Process.Kill($true)
    if (!$survivingClient.Process.WaitForExit(20000)) { throw 'Klient nie obsłużył utraty hosta.' }
    $clientOutput = $survivingClient.Output.GetAwaiter().GetResult()
    $clientError = $survivingClient.Error.GetAwaiter().GetResult()
    if ($survivingClient.Process.ExitCode -ne 0 -or $clientOutput -notmatch 'Gospodarz zakończył połączenie' -or $clientError -match 'ERROR:|Fatal|Exception') { throw 'Błąd klienta po utracie hosta.' }
    Write-Host "PASS: przejęcie auta przez AI i bezpieczna utrata hosta. Logi: $testRoot"
} finally {
    foreach ($entry in $running) {
        if (!$entry.Process.HasExited) { $entry.Process.Kill($true) }
        $entry.Process.WaitForExit()
        Set-Content -LiteralPath (Join-Path $testRoot ($entry.Name + '.log')) -Value ($entry.Output.GetAwaiter().GetResult() + $entry.Error.GetAwaiter().GetResult())
        $entry.Process.Dispose()
    }
}

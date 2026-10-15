param([Parameter(Mandatory)][string]$Stage)
. "$PSScriptRoot/common.ps1"
$stageRoot = Join-Path $RepoRoot "artifacts/stages/$Stage"
New-Item -ItemType Directory -Force $stageRoot | Out-Null
& "$PSScriptRoot/build.ps1" *> (Join-Path $stageRoot 'build.log')
& "$PSScriptRoot/test.ps1" *> (Join-Path $stageRoot 'tests.log')
$checks = @(
    @{ Name='network'; File='integration.ps1'; Args=@('-SkipBuild','-Impaired','-Combat') },
    @{ Name='connections'; File='connection-tests.ps1'; Args=@('-SkipBuild') }
)
$running = @()
try {
    foreach ($check in $checks) {
        $info = [System.Diagnostics.ProcessStartInfo]::new()
        $info.FileName = (Get-Command pwsh).Source; $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        foreach ($argument in (@('-NoProfile','-File',(Join-Path $PSScriptRoot $check.File)) + $check.Args)) { $info.ArgumentList.Add($argument) }
        $process = [System.Diagnostics.Process]::Start($info)
        $running += [pscustomobject]@{Name=$check.Name; Process=$process; Output=$process.StandardOutput.ReadToEndAsync(); Error=$process.StandardError.ReadToEndAsync()}
    }
    Invoke-Checked (Get-Godot) @('--headless','--path',(Join-Path $RepoRoot 'src/Game.Client'),'--fixed-fps','60','--','--smoke','--bots=7',"--results=$stageRoot/smoke.json") *> (Join-Path $stageRoot 'smoke.log')
    foreach ($check in $running) {
        $check.Process.WaitForExit()
        $result = $check.Output.GetAwaiter().GetResult() + $check.Error.GetAwaiter().GetResult()
        Set-Content -LiteralPath (Join-Path $stageRoot ($check.Name + '.log')) -Value $result
        if ($check.Process.ExitCode -ne 0 -or $result -notmatch 'PASS:') { throw "Niepowodzenie $($check.Name). Log: $stageRoot" }
    }
    Write-Host "PASS: etap $Stage — build, xUnit, ENet impaired/combat, rozłączenia i 8 aut headless. $stageRoot"
} finally {
    foreach ($check in $running) { if (!$check.Process.HasExited) { $check.Process.Kill($true) }; $check.Process.Dispose() }
}

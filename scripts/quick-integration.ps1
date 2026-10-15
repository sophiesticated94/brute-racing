param([ValidateRange(2,8)][int]$Players=2,[int]$DirectoryPort=5087,[int]$GamePort=29920,[switch]$UnreachableHost,[switch]$SkipBuild)
. "$PSScriptRoot/common.ps1"
if (!$SkipBuild) { & "$PSScriptRoot/build.ps1" }
$testRoot=Join-Path $RepoRoot ('artifacts/tests/quick-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force $testRoot | Out-Null
$running=@();$directoryUrl="http://127.0.0.1:$DirectoryPort"
function Start-CheckedProcess([string]$name,[string]$exe,[string[]]$arguments) {
    $info=[System.Diagnostics.ProcessStartInfo]::new();$info.FileName=$exe;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.StandardOutputEncoding=[System.Text.Encoding]::UTF8;$info.StandardErrorEncoding=[System.Text.Encoding]::UTF8
    foreach($argument in $arguments){$info.ArgumentList.Add($argument)}
    $process=[System.Diagnostics.Process]::Start($info)
    return [pscustomobject]@{Name=$name;Process=$process;Output=$process.StandardOutput.ReadToEndAsync();Error=$process.StandardError.ReadToEndAsync()}
}
try {
    $running+=Start-CheckedProcess 'directory' (Get-Command dotnet).Source @((Join-Path $RepoRoot 'src/Game.DirectoryApi/bin/Debug/net10.0/Game.DirectoryApi.dll'),'--urls',$directoryUrl)
    $health=$null
    for($attempt=0;$attempt -lt 30;$attempt++) {
        try{$health=Invoke-RestMethod "$directoryUrl/health";break}catch{Start-Sleep -Milliseconds 200}
    }
    if(!$health){throw 'Katalog nie wystartował.'}
    $ghost=$null
    if($UnreachableHost) {
        $body=@{clientId=[guid]::NewGuid().ToString();name='Unreachable';port=29999;contentHash=$health.contentHash;canHost=$true;build=@{carId='rook';weaponId='machine_gun';weaponLevel=1;mineLevel=1;playerLevel=1;cosmeticId='stock';skillRanks=@{}}}|ConvertTo-Json -Depth 10
        $ghost=Invoke-RestMethod "$directoryUrl/queue" -Method Post -ContentType application/json -Body $body
    }
    for($i=0;$i -lt $Players;$i++) {
        $name="quick$i"
        $running+=Start-CheckedProcess $name (Get-Godot) @('--headless','--path',(Join-Path $RepoRoot 'src/Game.Client'),'--log-file',"$testRoot/$name.engine.log",'--max-fps','60','--','--smoke','--mode=quick',"--directory=$directoryUrl","--port=$($GamePort+$i)",'--timeout=280',"--name=$name","--results=$testRoot/$name.json")
    }
    $deadline=[DateTime]::UtcNow.AddSeconds(300);$ghostOfferAt=$null
    while(@($running|Where-Object {$_.Name -ne 'directory' -and !$_.Process.HasExited}).Count -gt 0) {
        if([DateTime]::UtcNow -gt $deadline){throw 'Przekroczono czas quick.'}
        if($ghost) {
            $ghostStatus=Invoke-RestMethod "$directoryUrl/queue/$($ghost.id)" -Headers @{'X-Queue-Secret'=$ghost.secret}
            if($ghostStatus.offer -and !$ghostOfferAt){$ghostOfferAt=[DateTime]::UtcNow}
            if($ghostOfferAt -and ([DateTime]::UtcNow-$ghostOfferAt).TotalSeconds -gt 14) {
                Invoke-RestMethod "$directoryUrl/queue/$($ghost.id)" -Method Delete -Headers @{'X-Queue-Secret'=$ghost.secret}|Out-Null
                $ghost=$null
            }
        }
        Start-Sleep -Milliseconds 400
    }
    $reports=@()
    foreach($entry in $running|Where-Object Name -NE 'directory') {
        $stdout=$entry.Output.GetAwaiter().GetResult();$stderr=$entry.Error.GetAwaiter().GetResult()
        Set-Content -LiteralPath "$testRoot/$($entry.Name).log" -Value ($stdout+$stderr)
        if($entry.Process.ExitCode -ne 0 -or $stderr -match 'ERROR:|Exception'){throw "Błąd quick: $($entry.Name), $testRoot"}
        $report=Get-Content "$testRoot/$($entry.Name).json" -Raw|ConvertFrom-Json
        if(!$report.success -or $report.mode -ne 'Quick' -or $report.vehicles.Count -ne $Players -or $report.quickOffers -lt 1){throw 'Niepoprawny mecz quick.'}
        if($UnreachableHost -and $report.quickOffers -lt 2){throw 'Nie sprawdzono ponownej oferty po nieosiągalnym hoście.'}
        $reports+=$report
    }
    foreach($report in $reports){if(($report.ranking -join ',') -ne ($reports[0].ranking -join ',') -or $report.quickMatchId -ne $reports[0].quickMatchId){throw 'Niezgodne wyniki quick.'}}
    if(@($reports|Where-Object host).Count -ne 1){throw 'Quick nie wybrał jednego hosta.'}
    Write-Host "PASS: quick przez HTTP + ENet, $Players procesy, unreachable=$UnreachableHost. Raporty: $testRoot"
} finally {
    foreach($entry in $running) {
        if(!$entry.Process.HasExited){$entry.Process.Kill($true);$entry.Process.WaitForExit()}
        Set-Content -LiteralPath "$testRoot/$($entry.Name).log" -Value ($entry.Output.GetAwaiter().GetResult()+$entry.Error.GetAwaiter().GetResult())
        $entry.Process.Dispose()
    }
}

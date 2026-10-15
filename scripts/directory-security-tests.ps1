param([int]$Port=5091,[switch]$SkipBuild)
. "$PSScriptRoot/common.ps1"
if(!$SkipBuild){& "$PSScriptRoot/build.ps1"}
$testRoot=Join-Path $RepoRoot ('artifacts/tests/directory-'+[guid]::NewGuid().ToString('N').Substring(0,8));New-Item -ItemType Directory -Force $testRoot|Out-Null
$info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=(Get-Command dotnet).Source;$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
foreach($arg in @((Join-Path $RepoRoot 'src/Game.DirectoryApi/bin/Debug/net10.0/Game.DirectoryApi.dll'),'--urls',"http://127.0.0.1:$Port")){$info.ArgumentList.Add($arg)}
$api=[Diagnostics.Process]::Start($info);$output=$api.StandardOutput.ReadToEndAsync();$errors=$api.StandardError.ReadToEndAsync();$url="http://127.0.0.1:$Port"
function Request([string]$method,[string]$path,[string]$body='', $headers=@{}){
    $params=@{Uri="$url$path";Method=$method;SkipHttpErrorCheck=$true;Headers=$headers}
    if($body){$params.ContentType='application/json';$params.Body=$body}
    Invoke-WebRequest @params
}
try{
    $health=$null;for($i=0;$i -lt 30;$i++){try{$health=Invoke-RestMethod "$url/health";break}catch{Start-Sleep -Milliseconds 200}}
    if(!$health){throw 'Brak API.'}
    $requests=@();$tickets=@()
    for($i=0;$i -lt 8;$i++){
        $body=@{clientId=[guid]::NewGuid().ToString();name="NAT$i";port=29900+$i;contentHash=$health.contentHash;build=@{carId='rook';weaponId='machine_gun';weaponLevel=1;mineLevel=1;playerLevel=1;cosmeticId='stock';skillRanks=@{}}}|ConvertTo-Json -Depth 10
        $requests+=$body;$response=Request POST '/queue' $body;if($response.StatusCode -ne 200){throw 'Ośmiu graczy za NAT-em zostało odrzuconych.'};$tickets+=($response.Content|ConvertFrom-Json)
    }
    $before=(Request GET "/queue/$($tickets[0].id)" '' @{'X-Queue-Secret'=$tickets[0].secret}).Content|ConvertFrom-Json
    Start-Sleep -Milliseconds 1200
    $conflict=Request POST '/queue' $requests[0]
    if($conflict.StatusCode -ne 409 -or !$conflict.Headers['Retry-After']){throw 'Brak 409 dla duplikatu.'}
    $after=(Request GET "/queue/$($tickets[0].id)" '' @{'X-Queue-Secret'=$tickets[0].secret}).Content|ConvertFrom-Json
    if(!$before.offer -or $before.offer.id -ne $after.offer.id){throw 'Atak zmienił ofertę.'}
    if((Request DELETE "/queue/$($tickets[0].id)" '' @{'X-Queue-Secret'='wrong'}).StatusCode -ne 404){throw 'Nie odrzucono sekretu.'}
    Start-Sleep -Milliseconds 1100
    if((Request POST '/queue' ('{"padding":"'+('x'*40000)+'"}')).StatusCode -ne 413){throw 'Brak limitu 32 KiB.'}
    Start-Sleep -Milliseconds 1100
    $deep='{"x":'*20+'0'+'}'*20
    if((Request POST '/queue' $deep).StatusCode -ne 413){throw 'Brak limitu głębokości JSON.'}
    Start-Sleep -Milliseconds 1100
    $http=[Net.Http.HttpClient]::new();$chunked=[Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Post,"$url/queue")
    $chunked.Headers.TransferEncodingChunked=$true;$chunked.Content=[Net.Http.StringContent]::new(('{"padding":"'+('x'*40000)+'"}'),[Text.Encoding]::UTF8,'application/json')
    $large=$http.SendAsync($chunked).GetAwaiter().GetResult()
    if([int]$large.StatusCode -ne 413){throw 'Brak limitu chunked body.'};$large.Dispose();$chunked.Dispose();$http.Dispose()
    # Eight clients polling once a second share one IP without exhausting the general bucket.
    for($round=0;$round -lt 3;$round++){foreach($ticket in $tickets){if((Request GET "/queue/$($ticket.id)" '' @{'X-Queue-Secret'=$ticket.secret}).StatusCode -ne 200){throw 'Normalny heartbeat NAT odrzucony.'}};Start-Sleep -Seconds 1}
    $limited=$false;for($i=0;$i -lt 25;$i++){$response=Request POST '/queue' $requests[0];if($response.StatusCode -eq 429){if(!$response.Headers['Retry-After']){throw 'Brak Retry-After.'};$limited=$true;break}}
    if(!$limited){throw 'Brak limitu enqueue.'}
    $limited=$false;for($i=0;$i -lt 300;$i++){$response=Request GET '/health';if($response.StatusCode -eq 429){$limited=$true;break}}
    if(!$limited){throw 'Brak ogólnego limitu.'}
    Write-Host "PASS: HTTP 409 bez zmiany oferty, sekrety, 413 body/JSON, 429/Retry-After i 8 klientów za NAT-em. $testRoot"
}finally{if(!$api.HasExited){$api.Kill($true);$api.WaitForExit()};Set-Content "$testRoot/api.log" ($output.GetAwaiter().GetResult()+$errors.GetAwaiter().GetResult());$api.Dispose()}

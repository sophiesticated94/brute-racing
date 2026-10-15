param([int]$Port=5092,[switch]$SkipBuild,[switch]$Orphan)
. "$PSScriptRoot/common.ps1"
if(!$SkipBuild){& "$PSScriptRoot/build.ps1"}
$folder=Join-Path $RepoRoot ('artifacts/tests/cancel-'+[guid]::NewGuid().ToString('N').Substring(0,8));New-Item -ItemType Directory -Force $folder|Out-Null
$info=[Diagnostics.ProcessStartInfo]::new();$info.FileName=(Get-Command dotnet).Source;$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
foreach($arg in @((Join-Path $RepoRoot 'src/Game.DirectoryApi/bin/Debug/net10.0/Game.DirectoryApi.dll'),'--urls',"http://127.0.0.1:$Port")){$info.ArgumentList.Add($arg)}
$api=[Diagnostics.Process]::Start($info);$output=$api.StandardOutput.ReadToEndAsync();$errors=$api.StandardError.ReadToEndAsync();$url="http://127.0.0.1:$Port"
try{
    $health=$null;for($i=0;$i -lt 30;$i++){try{$health=Invoke-RestMethod "$url/health";break}catch{Start-Sleep -Milliseconds 200}}
    if(!$health){throw 'Brak API.'}
    [void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromStream([IO.MemoryStream]::new([IO.File]::ReadAllBytes((Join-Path $RepoRoot 'src/Game.Core/bin/Debug/net8.0/Game.Core.dll'))))
    $catalog=[Game.Core.GameCatalog]::Load([Func[string,string]]{param($file)[IO.File]::ReadAllText((Join-Path $RepoRoot "data/$file"))})
    $profile=[Game.Core.PlayerProfile]::new();[IO.File]::WriteAllText("$folder/profile.json",[Game.Core.ProfileCodec]::Encode($catalog,$profile))
    $extra=@()
    if($Orphan){
        $request=[Game.Core.QueueRequest]::new($profile.PlayerId,'Orphan',[Game.Core.PlayerLoadoutDto]::Default,29990,$health.contentHash,$true,'127.0.0.1')
        $body=[Text.Json.JsonSerializer]::Serialize($request,[Game.Core.GameCatalog]::Json)
        $ticket=Invoke-RestMethod "$url/queue" -Method Post -ContentType application/json -Body $body
        $extra+='--expect-queue-conflict'
    }
    $args=@('--headless','--path',(Join-Path $RepoRoot 'src/Game.Client'),'--max-fps','60','--','--smoke','--mode=quick','--queue-cancel-smoke',"--directory=$url",'--timeout=115',"--profile-path=$folder/profile.json")+$extra
    $result=@(& (Get-Godot) @args 2>&1);$exit=$LASTEXITCODE;Set-Content "$folder/client.log" ($result -join "`n")
    if($exit -ne 0 -or ($result -join "`n") -notmatch 'PASS: QUEUE_CLIENT_SMOKE' -or ($result -join "`n") -match 'ERROR:|Exception'){throw "Błąd anulowania quick. $folder"}
    if($Orphan){try{Invoke-RestMethod "$url/queue/$($ticket.id)" -Headers @{'X-Queue-Secret'=$ticket.secret}|Out-Null;throw 'Stary bilet pozostał aktywny.'}catch{if($_.Exception.Response.StatusCode.value__ -ne 404){throw}}}
    Write-Host "PASS: klient quick anuluje przed enqueue; powrót po utracie sekretu=$Orphan. $folder"
}finally{if(!$api.HasExited){$api.Kill($true);$api.WaitForExit()};Set-Content "$folder/api.log" ($output.GetAwaiter().GetResult()+$errors.GetAwaiter().GetResult());$api.Dispose()}

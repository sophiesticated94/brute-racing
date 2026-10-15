param([int]$Port=29950,[switch]$SkipBuild,[switch]$RewardCrash)
. "$PSScriptRoot/common.ps1"
if(!$SkipBuild){& "$PSScriptRoot/build.ps1"}
$testRoot=Join-Path $RepoRoot ('artifacts/tests/resume-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force $testRoot|Out-Null
$running=[System.Collections.Generic.List[object]]::new()
function Start-Player([string]$name,[string]$profileName,[string[]]$extra){
    $info=[System.Diagnostics.ProcessStartInfo]::new();$info.FileName=Get-Godot;$info.UseShellExecute=$false;$info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true;$info.StandardOutputEncoding=[System.Text.Encoding]::UTF8;$info.StandardErrorEncoding=[System.Text.Encoding]::UTF8
    $arguments=@('--headless','--path',(Join-Path $RepoRoot 'src/Game.Client'),'--log-file',"$testRoot/$name.engine.log",'--max-fps','60','--','--smoke','--mode=campaign','--campaign-races=3','--no-weapons',"--port=$Port",'--timeout=500',"--profile-path=$testRoot/$profileName.profile.json",'--exit-on-disconnect',"--results=$testRoot/$name.json")+$extra
    foreach($argument in $arguments){$info.ArgumentList.Add($argument)}
    $process=[System.Diagnostics.Process]::Start($info)
    $entry=[pscustomobject]@{Name=$name;Process=$process;Output=$process.StandardOutput.ReadToEndAsync();Error=$process.StandardError.ReadToEndAsync()};$running.Add($entry);return $entry
}
function Wait-Log([string]$name,[string]$pattern,[int]$seconds=120){
    $deadline=[DateTime]::UtcNow.AddSeconds($seconds)
    while([DateTime]::UtcNow -lt $deadline){
        if((Test-Path "$testRoot/$name.engine.log") -and (Get-Content "$testRoot/$name.engine.log" -Raw) -match $pattern){return}
        $entry=$running|Where-Object Name -EQ $name|Select-Object -Last 1
        if($entry.Process.HasExited){throw "Proces $name zakończył się przed $pattern. $testRoot"}
        Start-Sleep -Milliseconds 200
    }
    throw "Brak $pattern w $name. $testRoot"
}
function Read-LiveJson([string]$file){
    for($attempt=0;$attempt -lt 20;$attempt++){
        $reader=$null
        try{
            $stream=[IO.FileStream]::new($file,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            $reader=[IO.StreamReader]::new($stream);return ($reader.ReadToEnd()|ConvertFrom-Json)
        }catch [IO.IOException]{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 20}
        finally{if($reader){$reader.Dispose()}}
    }
}
function Stop-Player($entry){if(!$entry.Process.HasExited){$entry.Process.Kill($true);$entry.Process.WaitForExit()}}
function Copy-TestProfile([string]$source,[string]$target,[bool]$wrongToken){
    $candidate=[Game.Core.ProfileCodec]::Decode($script:catalog,[IO.File]::ReadAllText("$testRoot/$source.profile.json"))
    if($wrongToken){$key=@($candidate.CampaignCredentials.Keys)[0];$old=$candidate.CampaignCredentials[$key];$candidate.CampaignCredentials[$key]=[Game.Core.CampaignCredential]::new($old.ParticipantId,[Game.Core.CampaignAuthentication]::NewSecret())}
    else{$candidate.CampaignCredentials.Clear()}
    [IO.File]::WriteAllText("$testRoot/$target.profile.json",[Game.Core.ProfileCodec]::Encode($script:catalog,$candidate))
}
try{
    [void][System.Runtime.Loader.AssemblyLoadContext]::Default.LoadFromStream([IO.MemoryStream]::new([IO.File]::ReadAllBytes((Join-Path $RepoRoot 'src/Game.Core/bin/Debug/net8.0/Game.Core.dll'))))
    $script:catalog=[Game.Core.GameCatalog]::Load([Func[string,string]]{param($file)[IO.File]::ReadAllText((Join-Path $RepoRoot "data/$file"))})
    $host1=Start-Player 'host1' 'host' @('--host','--bots=1','--wait-players=2')
    Wait-Log 'host1' 'HOST_READY' 25
    $client1=Start-Player 'client1' 'client' @('--join=127.0.0.1','--rounds=3')
    Wait-Log 'host1' 'RACE_FINISH' 130
    # The second start happens only after intermission purchases have committed.
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    while(@(Select-String -Path "$testRoot/host1.engine.log" -Pattern 'RACE_START').Count -lt 2){if([DateTime]::UtcNow -gt $deadline){throw 'Nie rozpoczęto drugiej rundy.'};Start-Sleep -Milliseconds 100}
    $save=Get-ChildItem "$testRoot/campaigns/*.json"|Select-Object -First 1
    $before=(Read-LiveJson $save.FullName).campaign
    if($before.completedRounds -ne 1 -or @($before.participants|Where-Object {$_.ownedLevels.machine_gun -ge 2}).Count -lt 1){throw 'Nie utrwalono rundy i zakupów.'}
    Stop-Player $host1;Stop-Player $client1
    $host2=Start-Player 'host2' 'host' @("--resume-campaign=$($before.campaignId)",'--wait-players=2')
    Wait-Log 'host2' 'CAMPAIGN_RESUMED' 25
    Copy-Item -LiteralPath "$testRoot/host.profile.json" -Destination "$testRoot/active.profile.json"
    $active=Start-Player 'active' 'active' @('--join=127.0.0.1')
    Wait-Log 'active' 'już aktywne' 20;Stop-Player $active
    Copy-TestProfile 'client' 'missing' $false
    $missing=Start-Player 'missing' 'missing' @('--join=127.0.0.1')
    Wait-Log 'missing' 'nie posiada tokenu' 20;Stop-Player $missing
    Copy-TestProfile 'client' 'wrong' $true
    $wrong=Start-Player 'wrong' 'wrong' @('--join=127.0.0.1')
    Wait-Log 'wrong' 'Nieprawidłowy lub zużyty' 20;Stop-Player $wrong
    $afterAuth=(Read-LiveJson $save.FullName).campaign
    if($afterAuth.revision -ne $before.revision){throw 'Nieautoryzowana próba zmieniła zapis.'}
    $client2=Start-Player 'client2' 'client' @('--join=127.0.0.1','--rounds=2')
    Wait-Log 'host2' 'CAMPAIGN_REJOIN' 25
    if($RewardCrash){
        Wait-Log 'host2' 'RACE_FINISH' 130
        $deadline=[DateTime]::UtcNow.AddSeconds(30)
        while(@(Select-String -Path "$testRoot/host2.engine.log" -Pattern 'RACE_START').Count -lt 2){if([DateTime]::UtcNow -gt $deadline){throw 'Nie rozpoczęto finału.'};Start-Sleep -Milliseconds 100}
        Stop-Player $client2
    }
    $deadline=[DateTime]::UtcNow.AddSeconds(260)
    while(!$host2.Process.HasExited -or !$client2.Process.HasExited){if([DateTime]::UtcNow -gt $deadline){throw "Timeout wznowienia. $testRoot"};Start-Sleep -Milliseconds 300}
    foreach($entry in $(if($RewardCrash){@($host2)}else{@($host2,$client2)})){
        if($entry.Process.ExitCode -ne 0 -or $entry.Error.GetAwaiter().GetResult() -match 'ERROR:|Exception'){throw "Błąd $($entry.Name). $testRoot"}
        $report=Get-Content "$testRoot/$($entry.Name).json" -Raw|ConvertFrom-Json
        if(!$report.success -or !$report.campaign.complete -or $report.profileRaces -ne 3 -or $report.profileCampaigns -ne 1){throw "Nieprawidłowe nagrody $($entry.Name). $testRoot"}
    }
    if($RewardCrash){
        $pending=(Read-LiveJson $save.FullName).campaign
        if($pending.pendingRewards.Count -ne 2){throw 'Brak trwałych nagród nieobecnego gracza.'}
        $host3=Start-Player 'host3' 'host' @("--resume-campaign=$($before.campaignId)",'--wait-players=2')
        Wait-Log 'host3' 'CAMPAIGN_RESUMED' 25
        $client3=Start-Player 'client3' 'client' @('--join=127.0.0.1')
        Wait-Log 'host3' 'CAMPAIGN_REJOIN' 25
        $deadline=[DateTime]::UtcNow.AddSeconds(30)
        do{
            $received=(Read-LiveJson "$testRoot/client.profile.json").profile
            $saved=(Read-LiveJson $save.FullName).campaign
            if($received.completedRaces -eq 3 -and $received.completedCampaigns -eq 1 -and $saved.pendingRewards.Count -eq 0){break}
            if([DateTime]::UtcNow -gt $deadline){throw 'Nie dostarczono nagród po restarcie zakończonej kampanii.'};Start-Sleep -Milliseconds 200
        }while($true)
        if(Select-String -Path "$testRoot/host3.engine.log" -Pattern 'RACE_START'){throw 'Ukończona kampania uruchomiła dodatkową rundę.'}
        Stop-Player $host3;Stop-Player $client3
        foreach($entry in @($host3,$client3)){if($entry.Error.GetAwaiter().GetResult() -match 'ERROR:|Exception'){throw 'Błąd procesu dostarczania nagród.'}}
    }
    $after=(Read-LiveJson $save.FullName).campaign
    if($after.completedRounds -ne 3 -or @($after.rounds.raceId|Select-Object -Unique).Count -ne 3 -or $after.pendingRewards.Count -ne 0){throw 'Powtórzone rundy lub niepotwierdzone nagrody.'}
    foreach($member in $before.participants){if($member.participantId -notin $after.participants.participantId){throw 'Zmieniono trwałą tożsamość.'}}
    $firstPeer=(Select-String -Path "$testRoot/host1.engine.log" -Pattern 'PLAYER_JOINED id=(\d+)').Matches.Groups[1].Value
    $secondPeer=(Select-String -Path "$testRoot/host2.engine.log" -Pattern 'CAMPAIGN_REJOIN peer=(\d+)').Matches.Groups[1].Value
    if(!$firstPeer -or !$secondPeer -or $firstPeer -eq $secondPeer){throw 'Nie sprawdzono nowego PeerId.'}
    Write-Host "PASS: restart hosta w drugiej rundzie, zachowane zakupy i GUID, nowe PeerId, odrzucone brak/błędny token, 3 rundy i pojedyncze nagrody, rewardCrash=$RewardCrash. $testRoot"
}finally{
    foreach($entry in $running){Stop-Player $entry;Set-Content -LiteralPath "$testRoot/$($entry.Name).log" -Value ($entry.Output.GetAwaiter().GetResult()+$entry.Error.GetAwaiter().GetResult());$entry.Process.Dispose()}
}

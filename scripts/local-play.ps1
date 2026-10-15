param([ValidateRange(1,7)][int]$Clients = 1, [int]$Port = 28747)
. "$PSScriptRoot/common.ps1"
& "$PSScriptRoot/build.ps1"
$engine = Get-Godot
for ($instance = 0; $instance -le $Clients; $instance++) {
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $engine
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $gameArguments = @('--path',(Join-Path $RepoRoot 'src/Game.Client'),'--position',"$($instance * 100),$($instance * 60)",'--','--bots=0',"--port=$Port", "--name=Gracz$($instance + 1)")
    if ($instance -eq 0) { $gameArguments += '--host' } else { $gameArguments += '--join=127.0.0.1' }
    foreach ($argument in $gameArguments) { $info.ArgumentList.Add($argument) }
    [System.Diagnostics.Process]::Start($info) | Out-Null
    if ($instance -eq 0) { Start-Sleep -Milliseconds 1500 }
}

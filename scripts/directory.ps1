param([int]$Port=5080,[string]$BindAddress='127.0.0.1')
. "$PSScriptRoot/common.ps1"
Invoke-Checked dotnet @('run','--project',(Join-Path $RepoRoot 'src/Game.DirectoryApi'),'--','--urls',"http://${BindAddress}:$Port")

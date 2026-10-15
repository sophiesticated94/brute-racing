param([switch]$ExportTemplates)
. "$PSScriptRoot/common.ps1"
$version = '4.7-stable'
$toolsRoot = Join-Path $RepoRoot '.tools'
New-Item -ItemType Directory -Force $toolsRoot | Out-Null
$ProgressPreference = 'SilentlyContinue'
$release = Invoke-RestMethod "https://api.github.com/repos/godotengine/godot-builds/releases/tags/$version"
function Download-Verified([string]$assetName, [string]$destination) {
    $asset = $release.assets | Where-Object name -EQ $assetName | Select-Object -First 1
    if (!$asset) { throw "Brak oficjalnego pliku $assetName" }
    if (!(Test-Path -LiteralPath $destination)) { Invoke-WebRequest $asset.browser_download_url -OutFile $destination }
    if ($asset.digest -match '^sha256:(.+)$') {
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $destination).Hash -ne $Matches[1]) { throw "Niepoprawna suma SHA256: $assetName" }
    }
}
$zip = Join-Path $toolsRoot 'godot.zip'
Download-Verified "Godot_v${version}_mono_win64.zip" $zip
if (!(Test-Path -LiteralPath (Join-Path $toolsRoot 'godot/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64_console.exe'))) {
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $toolsRoot 'godot') -Force
}
if ($ExportTemplates) {
    $templates = Join-Path $toolsRoot 'templates.tpz'
    Download-Verified "Godot_v${version}_mono_export_templates.tpz" $templates
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($templates)
    $destination = Join-Path $env:APPDATA 'Godot/export_templates/4.7.stable.mono'
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -match '^templates/(windows|linux|version)' -and $entry.Name) {
                $target = Join-Path $destination ($entry.FullName.Substring(10))
                New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
        }
    } finally { $archive.Dispose() }
}
Invoke-Checked dotnet @('restore', (Join-Path $RepoRoot 'MiniCarMayhem.sln'))
Write-Host 'Godot .NET gotowy:' (Get-Godot)

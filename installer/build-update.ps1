param(
    [string]$Dotnet = 'dotnet',
    [string]$BasePayload = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/SynthwaveNowPlaying/SynthwaveNowPlaying.csproj'
$payload = Join-Path $PSScriptRoot 'payload'
$legacyPayload = Join-Path $PSScriptRoot 'payload-legacy'
$release = Join-Path $PSScriptRoot 'release'

function Clear-GeneratedFolder([string]$folder, [string]$expectedName) {
    $rootPath = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    $target = [IO.Path]::GetFullPath($folder).TrimEnd('\')
    if (-not $target.StartsWith($rootPath + '\', [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($target) -ne $expectedName) {
        throw "Refusing to clear unexpected folder: $target"
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}

function New-Archive([string]$destination, [string]$sourceRoot, [string[]]$names) {
    $temporary = "$destination.tmp"
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    $archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $names) {
            $source = Join-Path $sourceRoot $name
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing package file: $name" }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $source, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $destination -Force
}

Clear-GeneratedFolder $payload 'payload'
Clear-GeneratedFolder $legacyPayload 'payload-legacy'
& $Dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Multi-file player publish failed.' }
& $Dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -o $legacyPayload
if ($LASTEXITCODE -ne 0) { throw 'Legacy bridge publish failed.' }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $release -Force | Out-Null
$version = ([Version](Get-Item (Join-Path $payload 'Waveplayer.exe')).VersionInfo.FileVersion).ToString(3)
$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Where-Object Extension -ne '.pdb' |
    ForEach-Object {
        $name = [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\', '/')
        [ordered]@{ Path = $name; Size = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    } | Sort-Object { $_.Path })
$manifest = [ordered]@{ Format = 2; Version = $version; BaseVersion = $null; Files = $files; Changed = $null }
$manifestPath = Join-Path $payload 'waveplayer-update.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
$fileNames = @($files | ForEach-Object { $_.Path })
New-Archive (Join-Path $release 'WaveplayerFiles.zip') $payload @($fileNames + 'waveplayer-update.json')
New-Archive (Join-Path $release 'WaveplayerPortable.zip') $legacyPayload `
    @('Waveplayer.exe', 'Classic/SynthwaveNowPlaying.dll')

$patchPath = Join-Path $release 'WaveplayerPatch.zip'
if ($BasePayload) {
    $baseManifestPath = Join-Path $BasePayload 'waveplayer-update.json'
    if (-not (Test-Path -LiteralPath $baseManifestPath -PathType Leaf)) { throw 'Base payload manifest missing.' }
    $baseManifest = Get-Content -LiteralPath $baseManifestPath -Raw | ConvertFrom-Json
    if ($baseManifest.Format -ne 2) { throw 'Unsupported base payload manifest.' }
    $oldHashes = @{}
    foreach ($file in $baseManifest.Files) { $oldHashes[$file.Path] = $file.Sha256 }
    $changed = @($files | Where-Object { -not $oldHashes.ContainsKey($_.Path) -or
        $oldHashes[$_.Path] -ne $_.Sha256 } | ForEach-Object { $_.Path })
    $patchManifest = [ordered]@{ Format = 2; Version = $version; BaseVersion = $baseManifest.Version;
        Files = $files; Changed = $changed }
    $patchManifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    New-Archive $patchPath $payload @($changed + 'waveplayer-update.json')
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}
elseif (Test-Path -LiteralPath $patchPath) { Remove-Item -LiteralPath $patchPath -Force }

Get-ChildItem -LiteralPath $release -Filter 'Waveplayer*.zip' -File |
    Select-Object Name, Length, @{Name='SHA256';Expression={(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}}

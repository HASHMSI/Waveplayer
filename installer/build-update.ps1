param(
    [string]$Dotnet = 'dotnet',
    [string]$BasePayload = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/SynthwaveNowPlaying/SynthwaveNowPlaying.csproj'
$payload = Join-Path $PSScriptRoot 'payload'
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
& $Dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Multi-file player publish failed.' }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $release -Force | Out-Null
& (Join-Path $PSScriptRoot 'write-manifest.ps1') -Payload $payload
$manifestPath = Join-Path $payload 'WavePlayer-update.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = $manifest.Version
$files = $manifest.Files
$fileNames = @($files | ForEach-Object { $_.Path })
New-Archive (Join-Path $release 'WavePlayerFiles.zip') $payload @($fileNames + 'WavePlayer-update.json')
$patchPath = Join-Path $release 'WavePlayerPatch.zip'
if ($BasePayload) {
    $baseManifestPath = Join-Path $BasePayload 'WavePlayer-update.json'
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
    New-Archive $patchPath $payload @($changed + 'WavePlayer-update.json')
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}
elseif (Test-Path -LiteralPath $patchPath) { Remove-Item -LiteralPath $patchPath -Force }

@('WavePlayerFiles.zip', 'WavePlayerPatch.zip') | ForEach-Object { Get-Item -LiteralPath (Join-Path $release $_) -ErrorAction SilentlyContinue } |
    Select-Object Name, Length, @{Name='SHA256';Expression={(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}}

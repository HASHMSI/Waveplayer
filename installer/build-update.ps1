param([string]$Dotnet = 'dotnet')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/SynthwaveNowPlaying/SynthwaveNowPlaying.csproj'
$payload = Join-Path $PSScriptRoot 'payload'
$release = Join-Path $PSScriptRoot 'release'
$package = Join-Path $release 'WaveplayerPortable.zip'
$temporary = "$package.tmp"

& $Dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Player publish failed.' }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Path $release -Force | Out-Null
if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
$archive = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @('Waveplayer.exe', 'Classic/SynthwaveNowPlaying.dll')) {
        $source = Join-Path $payload $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing update file: $name" }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $source, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }

Move-Item -LiteralPath $temporary -Destination $package -Force
$hash = Get-FileHash -LiteralPath $package -Algorithm SHA256
Write-Host "Update package: $package"
Write-Host "SHA-256: $($hash.Hash.ToLowerInvariant())"

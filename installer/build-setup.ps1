param([string]$Dotnet = 'dotnet', [string]$OutputDirectory = '')

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/SynthwaveNowPlaying/SynthwaveNowPlaying.csproj'
$setupProject = Join-Path $PSScriptRoot 'src/NeonWaveSetup.csproj'
$zip = Join-Path $PSScriptRoot 'intermediate/payload.zip'
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $PSScriptRoot 'release' }
$payload = Join-Path $PSScriptRoot 'payload'

# First install uses the setup EXE; subsequent in-app updates use manifest ZIPs.
$root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$target = [IO.Path]::GetFullPath($payload).TrimEnd('\')
if (-not $target.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($target) -ne 'payload') { throw "Unexpected payload directory: $target" }
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
& $Dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Player publish failed.' }
& (Join-Path $PSScriptRoot 'write-manifest.ps1') -Payload $payload
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
New-Item -ItemType Directory -Path (Split-Path -Parent $zip) -Force | Out-Null
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $Dotnet publish $setupProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }
Write-Host "Setup created at $output/WavePlayerSetup.exe"

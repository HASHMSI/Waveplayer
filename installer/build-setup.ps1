param([string]$Dotnet = 'dotnet')

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-update.ps1') -Dotnet $Dotnet
if ($LASTEXITCODE -ne 0) { throw 'Player packages failed.' }

$setupProject = Join-Path $PSScriptRoot 'src/NeonWaveSetup.csproj'
$zip = Join-Path $PSScriptRoot 'src/payload.zip'
$output = Join-Path $PSScriptRoot 'release'
Copy-Item -LiteralPath (Join-Path $output 'WaveplayerFiles.zip') -Destination $zip -Force
& $Dotnet publish $setupProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }
Write-Host "Setup created at $output/WaveplayerSetup.exe"

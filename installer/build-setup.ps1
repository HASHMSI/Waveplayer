$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$playerProject = Join-Path $root 'src/SynthwaveNowPlaying/SynthwaveNowPlaying.csproj'
$setupProject = Join-Path $PSScriptRoot 'src/NeonWaveSetup.csproj'
$payload = Join-Path $PSScriptRoot 'payload'
$zip = Join-Path $PSScriptRoot 'src/payload.zip'
$temporaryZip = "$zip.tmp"
$output = Join-Path $PSScriptRoot 'release'

dotnet publish $playerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Player publish failed.' }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($temporaryZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in @('Waveplayer.exe', 'Classic/SynthwaveNowPlaying.dll')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, (Join-Path $payload $name), $name,
            [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}
if ([IO.File]::Exists($zip)) {
    [IO.File]::Replace($temporaryZip, $zip, [System.Management.Automation.Language.NullString]::Value)
} else {
    [IO.File]::Move($temporaryZip, $zip)
}

dotnet publish $setupProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $output
if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }
Write-Host "Setup created at $output/WaveplayerSetup.exe"

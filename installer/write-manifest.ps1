param([Parameter(Mandatory)][string]$Payload)

$ErrorActionPreference = 'Stop'
$Payload = [IO.Path]::GetFullPath($Payload)
$version = ([Version](Get-Item -LiteralPath (Join-Path $Payload 'WavePlayer.exe')).VersionInfo.FileVersion).ToString(3)
# Both setup and updater must ship the same complete inventory of managed files.
$files = @(Get-ChildItem -LiteralPath $Payload -Recurse -File |
    Where-Object { $_.Extension -ne '.pdb' -and $_.Name -ne 'WavePlayer-update.json' } |
    ForEach-Object {
        [ordered]@{
            Path = [IO.Path]::GetRelativePath($Payload, $_.FullName).Replace('\', '/')
            Size = $_.Length
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object { $_.Path })
$manifest = [ordered]@{ Format = 2; Version = $version; BaseVersion = $null; Files = $files; Changed = $null }
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Payload 'WavePlayer-update.json') -Encoding utf8NoBOM

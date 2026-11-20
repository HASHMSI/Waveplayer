param(
    [string]$Ffmpeg = 'F:\WavePlayer-Tools\ffmpeg\imageio_ffmpeg\binaries\ffmpeg-win-x86_64-v9.0.2.exe',
    [string]$CacheDirectory = 'F:\WavePlayer-RadioPreviewData'
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $Ffmpeg)) { throw 'Pass -Ffmpeg with an installed FFmpeg executable.' }
New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
$source = Join-Path $PSScriptRoot 'src\SynthwaveNowPlaying\Assets\SynthwaveRoad.mp4'
$output = Join-Path $CacheDirectory 'SynthwaveRoad-640x218.bgra'
& $Ffmpeg -hide_banner -loglevel error -y -i $source -vf 'scale=640:218:flags=lanczos,fps=30' -an -pix_fmt bgra -f rawvideo $output
if ($LASTEXITCODE -ne 0) { throw 'Could not build the video frame cache.' }

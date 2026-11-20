@echo off
set "WAVEPLAYER_LAB_DATA_DIR=F:\WavePlayer-RadioPreviewData"
set "WAVEPLAYER_RADIO_PREVIEW=0"
start "" "%~dp0.build\radio\bin\Release\net10.0-windows10.0.19041.0\WavePlayer.exe"

@echo off
rem Downloads NirSoft SoundVolumeView (x64) into bin\ - all files, unmodified, as NirSoft's license requires.
setlocal
set "ROOT=%~dp0.."
if not exist "%ROOT%\bin" mkdir "%ROOT%\bin"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference = 'Stop';" ^
  "$z = Join-Path $env:TEMP 'soundvolumeview-x64.zip';" ^
  "Invoke-WebRequest -Uri 'https://www.nirsoft.net/utils/soundvolumeview-x64.zip' -OutFile $z -UserAgent 'Mozilla/5.0';" ^
  "Expand-Archive $z -DestinationPath $env:ROOT\bin -Force; Remove-Item $z;" ^
  "$v = [version](Get-Item (Join-Path $env:ROOT 'bin\SoundVolumeView.exe')).VersionInfo.FileVersion.Trim();" ^
  "if ($v -lt [version]'2.48') { Write-Host ('SoundVolumeView ' + $v + ' is older than 2.48 - /SetSpatial may not work on Windows 11 25H2') -ForegroundColor Red; exit 2 }" ^
  "Write-Host ('SoundVolumeView ' + $v + ' installed to bin\')"
if errorlevel 1 ( echo DOWNLOAD FAILED & exit /b 1 )
endlocal

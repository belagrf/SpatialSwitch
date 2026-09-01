@echo off
rem Downloads NirSoft SoundVolumeView (x64) into bin\ - all files, unmodified, as NirSoft's license requires.
setlocal
set "ROOT=%~dp0.."
if not exist "%ROOT%\bin" mkdir "%ROOT%\bin"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$z = Join-Path $env:TEMP 'soundvolumeview-x64.zip';" ^
  "Invoke-WebRequest -Uri 'https://www.nirsoft.net/utils/soundvolumeview-x64.zip' -OutFile $z -UserAgent 'Mozilla/5.0';" ^
  "Expand-Archive $z -DestinationPath '%ROOT%\bin' -Force;" ^
  "$v = (Get-Item '%ROOT%\bin\SoundVolumeView.exe').VersionInfo.FileVersion;" ^
  "Write-Host ('SoundVolumeView ' + $v + ' installed to bin\ (need 2.48 or newer for /SetSpatial on Windows 11 25H2)')"
endlocal

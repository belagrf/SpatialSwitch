@echo off
rem Builds bin\SpatialSwitch.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set "ROOT=%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" ( echo csc.exe not found - is .NET Framework 4.x installed? & exit /b 1 )
if not exist "%ROOT%\bin" mkdir "%ROOT%\bin"
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
  /out:"%ROOT%\bin\SpatialSwitch.exe" /win32icon:"%ROOT%\src\icon.ico" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Xml.dll ^
  "%ROOT%\src\SpatialSwitch.cs"
if errorlevel 1 ( echo BUILD FAILED & exit /b 1 )
echo Built %ROOT%\bin\SpatialSwitch.exe
if not exist "%ROOT%\bin\SoundVolumeView.exe" echo NOTE: bin\SoundVolumeView.exe is missing - run scripts\get-soundvolumeview.cmd
endlocal

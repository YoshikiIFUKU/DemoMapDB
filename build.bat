@echo off
rem Build DemoMapDB.exe with the C# compiler bundled in Windows (.NET Framework 4.x). No SDK needed.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.x is required.
  exit /b 1
)
if not exist dist mkdir dist
"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /codepage:65001 ^
  /win32manifest:src\app.manifest /out:dist\DemoMapDB.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\*.cs
if errorlevel 1 exit /b 1
echo Built: %CD%\dist\DemoMapDB.exe

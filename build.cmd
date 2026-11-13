@echo off
rem Builds SsmsToolkit. Usage: build.cmd [Debug|Release]   (default: Release)
setlocal
set CONFIG=%~1
if "%CONFIG%"=="" set CONFIG=Release

if not exist "%~dp0lib\ssms22\SqlWorkbench.Interfaces.dll" (
  echo lib\ssms22 is empty. Run: powershell -ExecutionPolicy Bypass -File tools\copy-ssms-refs.ps1
  exit /b 1
)

set VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe
if not exist "%VSWHERE%" (
  echo vswhere.exe not found. Install Visual Studio with the "Visual Studio extension development" workload.
  exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -prerelease -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Component.VSSDK -find MSBuild\**\Bin\MSBuild.exe`) do set MSBUILD=%%i
if "%MSBUILD%"=="" (
  echo MSBuild with the VSSDK component not found.
  exit /b 1
)

echo Using %MSBUILD%
"%MSBUILD%" "%~dp0SsmsToolkit.sln" /restore /m /nologo /v:minimal /p:Configuration=%CONFIG%
exit /b %ERRORLEVEL%

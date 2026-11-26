@echo off
setlocal
for %%I in ("%~dp0..") do set "REPO_ROOT=%%~fI"
if "%REPO_ROOT:~-1%"=="\" set "REPO_ROOT=%REPO_ROOT%."
set "DEBUG_DLL=%REPO_ROOT%\MSBuildDiagnosticPatcher\bin\Debug\net8.0\MSBuildDiagnosticPatcher.dll"
set "RELEASE_DLL=%REPO_ROOT%\MSBuildDiagnosticPatcher\bin\Release\net8.0\MSBuildDiagnosticPatcher.dll"

if exist "%DEBUG_DLL%" (
  if "%~1"=="" (
    dotnet "%DEBUG_DLL%"
  ) else (
    dotnet "%DEBUG_DLL%" --project-root "%REPO_ROOT%" %*
  )
) else if exist "%RELEASE_DLL%" (
  if "%~1"=="" (
    dotnet "%RELEASE_DLL%"
  ) else (
    dotnet "%RELEASE_DLL%" --project-root "%REPO_ROOT%" %*
  )
) else (
  if "%~1"=="" (
    dotnet run --project "%REPO_ROOT%\MSBuildDiagnosticPatcher\MSBuildDiagnosticPatcher.csproj" -p:UseSharedCompilation=false --
  ) else (
    dotnet run --project "%REPO_ROOT%\MSBuildDiagnosticPatcher\MSBuildDiagnosticPatcher.csproj" -p:UseSharedCompilation=false -- --project-root "%REPO_ROOT%" %*
  )
)
exit /b %ERRORLEVEL%

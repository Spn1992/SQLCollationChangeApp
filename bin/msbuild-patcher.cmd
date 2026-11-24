@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%.."
set "DEBUG_DLL=%REPO_ROOT%\MSBuildDiagnosticPatcher\bin\Debug\net8.0\MSBuildDiagnosticPatcher.dll"
set "RELEASE_DLL=%REPO_ROOT%\MSBuildDiagnosticPatcher\bin\Release\net8.0\MSBuildDiagnosticPatcher.dll"

if exist "%DEBUG_DLL%" (
  dotnet "%DEBUG_DLL%" %*
) else if exist "%RELEASE_DLL%" (
  dotnet "%RELEASE_DLL%" %*
) else (
  dotnet run --project "%REPO_ROOT%\MSBuildDiagnosticPatcher\MSBuildDiagnosticPatcher.csproj" -p:UseSharedCompilation=false -- %*
)

@echo off
set SCRIPT_DIR=%~dp0
set REPO_ROOT=%SCRIPT_DIR%..
dotnet run --project "%REPO_ROOT%\MSBuildDiagnosticPatcher\MSBuildDiagnosticPatcher.csproj" -- %*

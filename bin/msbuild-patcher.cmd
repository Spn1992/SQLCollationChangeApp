@echo off
setlocal
for %%I in ("%~dp0..") do set "REPO_ROOT=%%~fI"
if "%REPO_ROOT:~-1%"=="\" set "REPO_ROOT=%REPO_ROOT%."
if "%~1"=="" (
    dotnet run --project "%REPO_ROOT%\MSBuildDiagnosticPatcher\MSBuildDiagnosticPatcher.csproj" -p:UseSharedCompilation=false --
) else (
    dotnet run --project "%REPO_ROOT%\MSBuildDiagnosticPatcher\MSBuildDiagnosticPatcher.csproj" -p:UseSharedCompilation=false -- --project-root "%REPO_ROOT%" %*
)
exit /b %ERRORLEVEL%

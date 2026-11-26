@echo off
setlocal
for %%I in ("%~dp0..") do set "REPO_ROOT=%%~fI"
if "%REPO_ROOT:~-1%"=="\" set "REPO_ROOT=%REPO_ROOT%."
set "PATCHER_DIR=%REPO_ROOT%\MSBuildDiagnosticPatcher"
set "PATCHER_PROJECT=%PATCHER_DIR%\MSBuildDiagnosticPatcher.csproj"
set "PATCHER_DLL=%PATCHER_DIR%\bin\Debug\net8.0\MSBuildDiagnosticPatcher.dll"
set "PATCHER_RUNTIMECONFIG=%PATCHER_DIR%\bin\Debug\net8.0\MSBuildDiagnosticPatcher.runtimeconfig.json"

if not exist "%PATCHER_DLL%" goto :build_patcher
if not exist "%PATCHER_RUNTIMECONFIG%" goto :build_patcher

powershell -NoProfile -NonInteractive -Command "$dll = [IO.File]::GetLastWriteTimeUtc($env:PATCHER_DLL); $sep = [IO.Path]::DirectorySeparatorChar; $b = $env:PATCHER_DIR + $sep + 'bin' + $sep; $o = $env:PATCHER_DIR + $sep + 'obj' + $sep; foreach ($f in [IO.Directory]::EnumerateFiles($env:PATCHER_DIR, '*', [IO.SearchOption]::AllDirectories)) { if (-not $f.StartsWith($b, [StringComparison]::OrdinalIgnoreCase) -and -not $f.StartsWith($o, [StringComparison]::OrdinalIgnoreCase) -and ($f.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) -or $f.EndsWith('.csproj', [StringComparison]::OrdinalIgnoreCase)) -and [IO.File]::GetLastWriteTimeUtc($f) -gt $dll) { exit 1 } }; exit 0" >nul 2>nul
if not errorlevel 1 goto :run_patcher

:build_patcher
dotnet build "%PATCHER_PROJECT%" -c Debug -p:UseSharedCompilation=false --nologo -v quiet 1>&2
if errorlevel 1 exit /b %ERRORLEVEL%
powershell -NoProfile -NonInteractive -Command "if ([IO.File]::Exists($env:PATCHER_DLL)) { [IO.File]::SetLastWriteTimeUtc($env:PATCHER_DLL, [DateTime]::UtcNow) }" >nul 2>nul

:run_patcher
if "%~1"=="" (
    dotnet "%PATCHER_DLL%"
) else (
    dotnet "%PATCHER_DLL%" --project-root "%REPO_ROOT%" %*
)
exit /b %ERRORLEVEL%

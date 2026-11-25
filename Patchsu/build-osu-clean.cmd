@echo off
rem Builds this osu! checkout WITHOUT any patches, exactly the same way as build-patchsu.cmd builds Patchsu
rem (Release, self-contained, same runtime), so the two can be compared fairly (debug graphs, garbage collection, ...).
rem
rem Nothing in the checkout is modified: the current commit is cloned into work\clean-src and built from there.
rem The result is installed separately; your Patchsu install and the official osu!lazer are not touched.
rem
rem Usage:
rem   build-osu-clean.cmd          build (into %LOCALAPPDATA%\osu-clean-app)
rem   build-osu-clean.cmd run      build if needed, then start it
rem Optional: set LP_PREFIX to install somewhere else.

setlocal
set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
for %%I in ("%SCRIPT_DIR%\osu-lazer") do set "REPO_DIR=%%~fI"
set "SRC_DIR=%SCRIPT_DIR%\work\clean-src"
if defined LP_PREFIX (set "PREFIX=%LP_PREFIX%") else (set "PREFIX=%LOCALAPPDATA%\osu-clean-app")
set "RID=win-x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "RID=win-arm64"
rem Runtime performance options baked into the build (written to osu!.runtimeconfig.json): server GC (parallel, much shorter
rem pauses), concurrent GC, memory kept for reuse, profile-guided JIT, and ReadyToRun (precompiled code, no JIT stalls on first use).
set "PERF_OPTIONS=-p:ServerGarbageCollection=true -p:ConcurrentGarbageCollection=true -p:RetainVMGarbageCollection=true -p:TieredPGO=true -p:PublishReadyToRun=true"

if /i "%~1"=="run" if exist "%PREFIX%\osu!.exe" goto :start

where git >nul 2>&1 || (echo Error: git is not installed or not on PATH.& exit /b 1)
where dotnet >nul 2>&1 || (echo Error: dotnet is not installed or not on PATH.& exit /b 1)

echo ==^> Preparing a clean, unpatched copy in %SRC_DIR%
if not exist "%SCRIPT_DIR%\work" mkdir "%SCRIPT_DIR%\work"
if exist "%SRC_DIR%" rmdir /s /q "%SRC_DIR%"
git clone --quiet --shared --no-tags "%REPO_DIR%" "%SRC_DIR%" || exit /b 1

echo ==^> Building (Release, %RID%) into %PREFIX%. The first build takes a few minutes...
pushd "%SRC_DIR%"
dotnet publish osu.Desktop\osu.Desktop.csproj --configuration Release --runtime %RID% --self-contained true --output "%PREFIX%" -p:DebugType=None %PERF_OPTIONS% -nologo
set "RESULT=%ERRORLEVEL%"
popd
if not "%RESULT%"=="0" exit /b %RESULT%
if not exist "%PREFIX%\osu!.exe" (echo Error: build finished but osu!.exe was not found.& exit /b 1)
echo ==^> Done: %PREFIX%\osu!.exe

if /i not "%~1"=="run" exit /b 0

:start
echo ==^> Starting the unpatched build (close any running osu! / Patchsu first).
start "" /d "%PREFIX%" "%PREFIX%\osu!.exe"
exit /b 0

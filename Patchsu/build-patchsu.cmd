@echo off
rem Builds "Patchsu" (the patched, local-priority osu!lazer) from the osu-lazer checkout and adds a Start Menu shortcut for it.
rem Patchsu shares the official osu!lazer data folder (beatmaps, skins, database), so nothing is duplicated.
rem Only one of the two can run at a time; starting the second hands over to the one already running.
rem (Windows has no .desktop files; the Start Menu shortcut is the equivalent.)
rem
rem Nothing in the original checkout is modified: the current commit of .\osu-lazer is cloned into .\work\src,
rem the patches in .\patches are applied there, and the result is published to the install folder.
rem
rem Usage:
rem   build-patchsu.cmd              build + install + create the shortcut
rem   build-patchsu.cmd uninstall    remove the installed build and the shortcut (your osu! data is untouched)
rem
rem Optional: set LP_PREFIX to install somewhere other than %LOCALAPPDATA%\patchsu-app

setlocal EnableExtensions DisableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
if "%SCRIPT_DIR:~-1%"=="\" set "SCRIPT_DIR=%SCRIPT_DIR:~0,-1%"
for %%I in ("%SCRIPT_DIR%\osu-lazer") do set "REPO_DIR=%%~fI"
set "PATCH_DIR=%SCRIPT_DIR%\patches"
rem The Patchsu logo: replaces osu!'s lazer.ico, which becomes the .exe icon, the window icon and the shortcut icon.
for %%I in ("%SCRIPT_DIR%\..\assets\patchsu.ico") do set "LOGO_ICO=%%~fI"
set "WORK_DIR=%SCRIPT_DIR%\work"
set "SRC_DIR=%WORK_DIR%\src"

if defined LP_PREFIX (set "PREFIX=%LP_PREFIX%") else (set "PREFIX=%LOCALAPPDATA%\patchsu-app")
set "SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Patchsu.lnk"
set "OSULOCAL_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\osu!local.lnk"
rem Left over from earlier names of this build (osu!local / local-priority); replaced by Patchsu.
set "OLD_PREFIX=%LOCALAPPDATA%\osu-local-priority-app"
set "OSULOCAL_PREFIX=%LOCALAPPDATA%\osu-local-app"
set "OLD_SHORTCUT=%APPDATA%\Microsoft\Windows\Start Menu\Programs\osu! (local-priority).lnk"

set "RID=win-x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "RID=win-arm64"
rem Runtime performance options baked into the build (written to osu!.runtimeconfig.json): server GC (parallel, much shorter
rem pauses), concurrent GC, memory kept for reuse, profile-guided JIT, and ReadyToRun (precompiled code, no JIT stalls on first use).
set "PERF_OPTIONS=-p:ServerGarbageCollection=true -p:ConcurrentGarbageCollection=true -p:RetainVMGarbageCollection=true -p:TieredPGO=true -p:PublishReadyToRun=true"

if /i "%~1"=="uninstall" goto :uninstall

where git >nul 2>&1 || (echo Error: git is not installed or not on PATH.& exit /b 1)
where dotnet >nul 2>&1 || (echo Error: dotnet is not installed or not on PATH.& exit /b 1)
if not exist "%REPO_DIR%\.git" (echo Error: expected the osu! git checkout at "%REPO_DIR%".& exit /b 1)
if not exist "%PATCH_DIR%\*.patch" (echo Error: no patches found in "%PATCH_DIR%".& exit /b 1)

echo ==^> Preparing a clean copy of the current commit in "%SRC_DIR%"
if not exist "%WORK_DIR%" mkdir "%WORK_DIR%"
if exist "%SRC_DIR%" rmdir /s /q "%SRC_DIR%"
rem --shared borrows objects from your checkout (fast, no copy) and lets --3way patching use the original base files.
git clone --quiet --shared --no-tags "%REPO_DIR%" "%SRC_DIR%" || (echo Error: could not clone the checkout.& exit /b 1)

pushd "%SRC_DIR%"
git config user.name "patchsu"
git config user.email "patchsu@localhost"

for %%P in ("%PATCH_DIR%\*.patch") do (
    echo ==^> Applying %%~nxP
    rem Plain apply first; if the code moved (newer osu! checkout), retry as a 3-way merge against the original base.
    git apply --whitespace=nowarn "%%~fP" 2>nul || git apply --3way --whitespace=nowarn "%%~fP"
    if errorlevel 1 (
        echo Error: %%~nxP does not apply to this osu! version. The patches were made for commit 8cb6e6e68d; they need updating for newer code.
        popd
        exit /b 1
    )
)

if exist "%LOGO_ICO%" (
    copy /y "%LOGO_ICO%" "osu.Desktop\lazer.ico" >nul
) else (
    echo Warning: "%LOGO_ICO%" not found, keeping osu!'s icon.
)

echo ==^> Building (Release, %RID%). The first build downloads packages and takes a few minutes...
dotnet publish osu.Desktop\osu.Desktop.csproj --configuration Release --runtime %RID% --self-contained true --output "%PREFIX%" -p:DebugType=None %PERF_OPTIONS% -nologo
if errorlevel 1 (
    echo Error: the build failed.
    popd
    exit /b 1
)
popd

if not exist "%PREFIX%\osu!.exe" (echo Error: build finished but "%PREFIX%\osu!.exe" was not found.& exit /b 1)

rem The game is called Patchsu: rename the launcher (it finds osu!.dll next to it on its own).
move /y "%PREFIX%\osu!.exe" "%PREFIX%\Patchsu.exe" >nul

if exist "%OLD_SHORTCUT%" del /q "%OLD_SHORTCUT%"
if exist "%OSULOCAL_SHORTCUT%" del /q "%OSULOCAL_SHORTCUT%"
if exist "%OLD_PREFIX%" rmdir /s /q "%OLD_PREFIX%"
if exist "%OSULOCAL_PREFIX%" rmdir /s /q "%OSULOCAL_PREFIX%"

echo ==^> Creating Start Menu shortcut
set "LP_TARGET=%PREFIX%\Patchsu.exe"
set "LP_WORKDIR=%PREFIX%"
set "LP_SHORTCUT=%SHORTCUT%"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s = (New-Object -ComObject WScript.Shell).CreateShortcut($env:LP_SHORTCUT);" ^
  "$s.TargetPath = $env:LP_TARGET;" ^
  "$s.WorkingDirectory = $env:LP_WORKDIR;" ^
  "$s.IconLocation = $env:LP_TARGET + ',0';" ^
  "$s.Description = 'osu!lazer that never submits scores; local profiles on right-click; shares the official osu! data';" ^
  "$s.Save()"
if errorlevel 1 echo Warning: could not create the Start Menu shortcut. You can start the game from "%PREFIX%\Patchsu.exe".

echo ==^> Done.
echo   Game:  "%PREFIX%\Patchsu.exe"
echo   Menu:  Patchsu
echo   Data:  %APPDATA%\osu  (shared with the official osu!lazer; Patchsu settings and local profiles in patchsu-osu\)
exit /b 0

:uninstall
echo ==^> Removing "%PREFIX%" and the Start Menu shortcut
if exist "%PREFIX%" rmdir /s /q "%PREFIX%"
if exist "%SHORTCUT%" del /q "%SHORTCUT%"
echo ==^> Done. Your osu! data (including Patchsu settings and local profiles in %APPDATA%\osu\patchsu-osu) is untouched.
exit /b 0

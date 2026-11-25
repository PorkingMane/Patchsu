@echo off
rem Starts the installed Patchsu with some of its features turned off, to find which one costs performance.
rem Compare the debug graphs (Ctrl+F11) of each run with clean osu!lazer.
rem
rem Usage:
rem   patchsu-diagnose.cmd                    list the features that can be turned off
rem   patchsu-diagnose.cmd tags               turn off one feature
rem   patchsu-diagnose.cmd tags,autoswitch    turn off several
rem   patchsu-diagnose.cmd all                turn off everything listed (closest to clean osu!lazer)
rem Optional: set LP_PREFIX if Patchsu is installed somewhere other than %LOCALAPPDATA%\patchsu-app

setlocal
if defined LP_PREFIX (set "PREFIX=%LP_PREFIX%") else (set "PREFIX=%LOCALAPPDATA%\patchsu-app")

if "%~1"=="" goto :list

if not exist "%PREFIX%\Patchsu.exe" (echo Patchsu is not installed at %PREFIX% ^(run build-patchsu.cmd first^).& exit /b 1)

echo Starting Patchsu with turned off: %~1
set "OSU_LOCAL_DISABLE=%~1"
cd /d "%PREFIX%"
start "" "%PREFIX%\Patchsu.exe"
exit /b 0

:list
echo Features that can be turned off (comma separated, or "all"):
echo   tags           local beatmap tags (song select details row and search)
echo   autoswitch     profile auto-switching on beatmap changes
echo   avatars        local profile pictures on leaderboards / results
echo   modrules       profile mod rules (enforcing, greying out mods, settings reset at play start)
echo   leaderboard    forcing the Local leaderboard while the network is off
echo   switcher-menu  the full-screen right-click menu layer around the profile switcher
echo   profile-ui     the local profile page and the profile switcher (not loaded at all)
echo   toolbar        the top bar's local profile name / picture updates
echo   recording      recording plays for local profile statistics
echo.
echo Suggested order: run "all" first. If that matches clean osu!lazer, turn features off one at a time.
exit /b 0

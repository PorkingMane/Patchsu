#!/usr/bin/env bash
# Starts the installed Patchsu with some of its features turned off, to find which one costs performance.
# Compare the debug graphs (Ctrl+F11) of each run with clean osu!lazer.
#
# Usage:
#   ./patchsu-diagnose.sh                      list the features that can be turned off
#   ./patchsu-diagnose.sh tags                 turn off one feature
#   ./patchsu-diagnose.sh tags,autoswitch      turn off several
#   ./patchsu-diagnose.sh all                  turn off everything listed (closest to clean osu!lazer)
#   ./patchsu-diagnose.sh --prefix DIR tags    the build is installed somewhere else
#
# A notification at startup shows what was turned off. Nothing is saved: the next normal start has everything on again.

set -euo pipefail

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
PREFIX="$DATA_HOME/patchsu-app"

if [ "${1:-}" = "--prefix" ]; then
    PREFIX="$2"
    shift 2
fi

features="${1:-}"

if [ -z "$features" ] || [ "$features" = "-h" ] || [ "$features" = "--help" ]; then
    cat <<'EOF'
Features that can be turned off (comma separated, or "all"):
  tags           local beatmap tags (song select details row and search)
  autoswitch     profile auto-switching on beatmap changes
  avatars        local profile pictures on leaderboards / results
  modrules       profile mod rules (enforcing, greying out mods, settings reset at play start)
  leaderboard    forcing the Local leaderboard while the network is off
  switcher-menu  the full-screen right-click menu layer around the profile switcher
  profile-ui     the local profile page and the profile switcher (not loaded at all)
  toolbar        the top bar's local profile name / picture updates
  recording      recording plays for local profile statistics

Suggested order: run "all" first. If that matches clean osu!lazer, turn features off one at a time
(e.g. "tags", then "autoswitch", ...) until the run where the spikes disappear.
EOF
    exit 0
fi

EXECUTABLE="$PREFIX/Patchsu"
[ -x "$EXECUTABLE" ] || { echo "Patchsu is not installed at $PREFIX (run build-patchsu.sh first)." >&2; exit 1; }

echo "Starting Patchsu with turned off: $features"
cd "$PREFIX"
OSU_LOCAL_DISABLE="$features" exec "$EXECUTABLE"

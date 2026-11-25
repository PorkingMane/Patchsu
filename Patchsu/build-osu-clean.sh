#!/usr/bin/env bash
# Builds this osu! checkout WITHOUT any patches, exactly the same way as build-patchsu.sh builds Patchsu
# (Release, self-contained, same runtime), so the two can be compared fairly (debug graphs, garbage collection, ...).
#
# Nothing in the checkout is modified: the current commit is cloned into ./work/clean-src and built from there.
# The result is installed separately; your Patchsu install and the official osu!lazer are not touched.
#
# Usage:
#   ./build-osu-clean.sh                 build (into ~/.local/share/osu-clean-app)
#   ./build-osu-clean.sh run             build if needed, then start it
#   ./build-osu-clean.sh --prefix DIR    install somewhere else
#
# Note: this is plain osu!lazer from source. It uses the same osu! data folder and, if you are logged in,
# behaves online like the official game (it is a "local release" build, so it never updates itself).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/osu-lazer" && pwd)"
SRC_DIR="$SCRIPT_DIR/work/clean-src"

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
PREFIX="$DATA_HOME/osu-clean-app"
RUN=0

case "$(uname -m)" in
    aarch64|arm64) RID="linux-arm64" ;;
    *)             RID="linux-x64" ;;
esac

# Runtime performance options baked into the build (written to "osu!.runtimeconfig.json"):
#   ServerGarbageCollection  one GC heap per core, collected in parallel: much shorter pauses when changing beatmaps
#   ConcurrentGarbageCollection  full collections run in the background (the default, kept explicit)
#   RetainVMGarbageCollection  freed memory is kept for reuse instead of returned to the OS after every collection
#   TieredPGO                 the JIT optimises hot code using what it saw at runtime (the default, kept explicit)
#   PublishReadyToRun         game code is precompiled, so first use of a screen / beatmap doesn't stall on JIT compilation
PERF_OPTIONS=(-p:ServerGarbageCollection=true -p:ConcurrentGarbageCollection=true -p:RetainVMGarbageCollection=true -p:TieredPGO=true -p:PublishReadyToRun=true)

while [ $# -gt 0 ]; do
    case "$1" in
        run)          RUN=1; shift ;;
        --prefix)     PREFIX="$2"; shift 2 ;;
        --prefix=*)   PREFIX="${1#*=}"; shift ;;
        -h|--help)    sed -n '2,15p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

say()  { printf '\033[1;35m==>\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

EXECUTABLE="$PREFIX/osu!"

if [ "$RUN" -eq 0 ] || [ ! -x "$EXECUTABLE" ]; then
    for tool in git dotnet; do
        command -v "$tool" >/dev/null 2>&1 || fail "'$tool' is not installed or not on PATH."
    done

    [ -d "$REPO_DIR/.git" ] || fail "Expected the osu! git checkout at $REPO_DIR"

    say "Preparing a clean, unpatched copy of $(git -C "$REPO_DIR" rev-parse --short HEAD) in $SRC_DIR"
    mkdir -p "$SCRIPT_DIR/work"
    rm -rf -- "$SRC_DIR"
    git clone --quiet --shared --no-tags "$REPO_DIR" "$SRC_DIR"

    say "Building (Release, $RID) into $PREFIX. The first build downloads packages and takes a few minutes..."
    (cd "$SRC_DIR" && dotnet publish osu.Desktop/osu.Desktop.csproj \
        --configuration Release \
        --runtime "$RID" \
        --self-contained true \
        --output "$PREFIX" \
        -p:DebugType=None "${PERF_OPTIONS[@]}" \
        -nologo)

    [ -x "$EXECUTABLE" ] || fail "Build finished but $EXECUTABLE was not found."
    say "Done: $EXECUTABLE"
fi

if [ "$RUN" -eq 1 ]; then
    say "Starting the unpatched build (close any running osu! / Patchsu first)."
    cd "$PREFIX"
    exec "./osu!"
fi

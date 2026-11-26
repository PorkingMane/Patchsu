#!/usr/bin/env bash
# Builds "Patchsu" (the patched, local-priority osu!lazer) from the osu-lazer checkout and adds a desktop menu entry for it.
#
# Patchsu shares the official osu!lazer data folder (beatmaps, skins, database), so nothing is duplicated.
# Only one of the two can run at a time; starting the second hands over to the one already running.
#
# Nothing in the original checkout is modified: the current commit of ./osu-lazer is cloned into ./work/src,
# the patches in ./patches are applied there, and the result is published to the install folder.
#
# Usage:
#   ./build-patchsu.sh                 build + install + create the menu entry
#   ./build-patchsu.sh --prefix DIR    install somewhere else (default: ~/.local/share/patchsu-app)
#   ./build-patchsu.sh --no-desktop    don't create the menu entry
#   ./build-patchsu.sh --uninstall     remove the installed build and the menu entry (your osu! data is untouched)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/osu-lazer" && pwd)"
PATCH_DIR="$SCRIPT_DIR/patches"
# The Patchsu logo: patchsu.ico replaces osu!'s lazer.ico (window icon, Windows .exe icon), logo.png is the menu icon.
LOGO_ICO="$SCRIPT_DIR/../assets/patchsu.ico"
LOGO_PNG="$SCRIPT_DIR/../assets/logo.png"
WORK_DIR="$SCRIPT_DIR/work"
SRC_DIR="$WORK_DIR/src"

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
PREFIX="$DATA_HOME/patchsu-app"
DESKTOP_FILE="$DATA_HOME/applications/patchsu.desktop"

# Left over from earlier names of this build (osu!local / local-priority); replaced by Patchsu.
OLD_PREFIXES=("$DATA_HOME/osu-local-app" "$DATA_HOME/osu-local-priority-app")
OLD_DESKTOP_FILES=("$DATA_HOME/applications/osu-local.desktop" "$DATA_HOME/applications/osu-local-priority.desktop")
CREATE_DESKTOP=1
UNINSTALL=0

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
        --prefix)     PREFIX="$2"; shift 2 ;;
        --prefix=*)   PREFIX="${1#*=}"; shift ;;
        --no-desktop) CREATE_DESKTOP=0; shift ;;
        --uninstall)  UNINSTALL=1; shift ;;
        -h|--help)    sed -n '2,16p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1" >&2; exit 2 ;;
    esac
done

say()  { printf '\033[1;35m==>\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31mError:\033[0m %s\n' "$*" >&2; exit 1; }

if [ "$UNINSTALL" -eq 1 ]; then
    say "Removing $PREFIX and $DESKTOP_FILE"
    rm -rf -- "$PREFIX"
    rm -f -- "$DESKTOP_FILE"
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DATA_HOME/applications" >/dev/null 2>&1 || true
    say "Done. Your osu! data (including Patchsu settings and local profiles in $DATA_HOME/osu/patchsu-osu) is untouched."
    exit 0
fi

for tool in git dotnet; do
    command -v "$tool" >/dev/null 2>&1 || fail "'$tool' is not installed or not on PATH."
done

[ -d "$REPO_DIR/.git" ] || fail "Expected the osu! git checkout at $REPO_DIR"
ls "$PATCH_DIR"/*.patch >/dev/null 2>&1 || fail "No patches found in $PATCH_DIR"

say "Preparing a clean copy of $(git -C "$REPO_DIR" rev-parse --short HEAD) in $SRC_DIR"
mkdir -p "$WORK_DIR"
rm -rf -- "$SRC_DIR"
# --shared borrows objects from your checkout (fast, no copy) and lets --3way patching use the original base files.
git clone --quiet --shared --no-tags "$REPO_DIR" "$SRC_DIR"

cd "$SRC_DIR"
git config user.name "patchsu"
git config user.email "patchsu@localhost"

for patch in "$PATCH_DIR"/*.patch; do
    say "Applying $(basename "$patch")"
    # Plain apply first; if the code moved (newer osu! checkout), retry as a 3-way merge against the original base.
    if ! git apply --whitespace=nowarn "$patch" 2>/dev/null && ! git apply --3way --whitespace=nowarn "$patch"; then
        fail "$(basename "$patch") does not apply to this osu! version. The patches were made for commit 8cb6e6e68d; they need updating for newer code."
    fi
done

# The game's own icon (the window icon comes from osu.Desktop/lazer.ico, embedded at build time).
if [ -f "$LOGO_ICO" ]; then
    cp -f -- "$LOGO_ICO" osu.Desktop/lazer.ico
else
    say "Warning: $LOGO_ICO not found, keeping osu!'s icon"
fi

# osu! pins its .NET SDK major version in global.json; let this build copy use a newer installed SDK too (e.g. .NET 11).
[ -f global.json ] && sed -i 's/"rollForward": *"[A-Za-z]*"/"rollForward": "latestMajor"/' global.json

say "Building (Release, $RID). The first build downloads packages and takes a few minutes..."
dotnet publish osu.Desktop/osu.Desktop.csproj \
    --configuration Release \
    --runtime "$RID" \
    --self-contained true \
    --output "$PREFIX" \
    -p:DebugType=None "${PERF_OPTIONS[@]}" \
    -nologo

[ -x "$PREFIX/osu!" ] || fail "Build finished but $PREFIX/osu! was not found."

# The game is called Patchsu: rename the launcher (it finds osu!.dll next to it on its own).
EXECUTABLE="$PREFIX/Patchsu"
mv -f -- "$PREFIX/osu!" "$EXECUTABLE"

cp -f -- "$LOGO_PNG" "$PREFIX/patchsu-icon.png" 2>/dev/null || cp -f -- "$REPO_DIR/assets/lazer.png" "$PREFIX/patchsu-icon.png" 2>/dev/null || true

for old in "${OLD_DESKTOP_FILES[@]}" "${OLD_PREFIXES[@]}"; do
    if [ -e "$old" ]; then
        say "Removing the old launcher/install $old"
        rm -rf -- "$old"
    fi
done

if [ "$CREATE_DESKTOP" -eq 1 ]; then
    say "Creating menu entry $DESKTOP_FILE"
    mkdir -p "$(dirname "$DESKTOP_FILE")"

    # Desktop-entry quoting: wrap in double quotes and escape \ " ` $
    quoted_exec=$(printf '%s' "$EXECUTABLE" | sed -e 's/[\\"`$]/\\&/g')

    cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=Patchsu
GenericName=Rhythm game (osu!lazer, local-priority)
Comment=osu!lazer that never submits scores; local profiles on right-click; shares the official osu! data
Exec="$quoted_exec"
Path=$PREFIX
Icon=$PREFIX/patchsu-icon.png
Terminal=false
Categories=Game;
StartupNotify=true
StartupWMClass=Patchsu
EOF
    chmod +x "$DESKTOP_FILE"
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DATA_HOME/applications" >/dev/null 2>&1 || true
fi

say "Done."
echo "  Game:     $EXECUTABLE"
[ "$CREATE_DESKTOP" -eq 1 ] && echo "  Menu:     Patchsu"
echo "  Data:     $DATA_HOME/osu  (shared with the official osu!lazer; Patchsu settings and local profiles in patchsu-osu/)"

# Patchsu-CLI

Builds osu!lazer with the Patchsu patches you choose. Pick patches or a preset, and the CLI checks your system, gets what's missing, applies the patches and installs the game with a menu entry.

Runs on Linux, macOS and Windows (x64 and arm64). It's a single program with nothing else to install, and the patches are
built into it.

## Using it

Run `Patchsu-CLI` (or `Patchsu-CLI.exe`) in a terminal:

1. **Pick patches.** The preset row is at the top (tab or ←/→ to switch); toggle single patches with space.
   - A grayed-out patch needs others that aren't selected. Selecting it selects them too (`Also enabled N: ...`).
   - Deselecting a patch also deselects the patches that need it (`Also disabled N: ...`).
   - Meanwhile the CLI quietly checks your system. The line under the list shows what's there and how much space the build needs, which is less once the SDK, NuGet packages or an earlier build exist.
2. **Choose the data folder** (first run only): next to the program or in Documents. Downloads and build files go there (about 3–4 GB).
3. **Check.** The CLI looks for the .NET SDK, git, the osu! source and the patches. If something is missing, it lists exactly what it will download or run and asks first.
4. **Build.** Patches are applied, the game is built and installed, and a menu entry / Start Menu shortcut / `.app` is created.

### Presets

| Preset | Patches | Builds |
|---|---|---|
| Vanilla+ | 0001, 0003, 0003b, 0005, 0006, 0007, 0010 | Patchsu |
| Local play | every patch | Patchsu |

Presets come with the patches (`patches.meta.json`), so they change with them.

### Data folder and install location

The first build asks where the data folder (downloads and build files, about 4 GB) goes: next to the program, in
Documents, **next to osu!'s data folder** (`~/.local/share/patchsu-data`, `%APPDATA%\patchsu-data`,
`~/Library/Application Support/patchsu-data`), or **somewhere else** that you type (it gets a `patchsu-data` folder
inside, unless you typed one). `f` on the check screen asks again. osu!'s own data folder is never touched.

Press `i` on the check screen (the one before the build starts) to choose where the game goes:

| Choice | Linux | Windows | macOS |
|---|---|---|---|
| Standard | `~/.local/share/patchsu-app` | `%LOCALAPPDATA%\patchsu-app` | `~/Applications/Patchsu.app` |
| Next to osu! data | `~/.local/share/Patchsu`, next to `osu` | `%APPDATA%\Patchsu`, next to `osu` | `~/Library/Application Support/Patchsu.app`, next to `osu` |

**Somewhere else** lets you type a folder; the game goes into a `Patchsu` folder inside it (`Patchsu.app` on macOS).
`~`, `$VAR` and `%VAR%` work. The choice is remembered (`installDir` in `Patchsu-CLI.json`) and
used by `build` and `uninstall` too; `--prefix DIR` overrides it. Folders that aren't writable, or that already hold
something other than a Patchsu build, can't be chosen. The menu entry and your osu! data don't move.

### What gets installed where

| What | Where | Notes |
|---|---|---|
| .NET SDK (only if you don't have one) | `patchsu-data/tools/dotnet` | The official SDK archive, SHA-512 checked and unpacked. No installer runs, nothing is registered system-wide, only the CLI's builds use it. |
| git (only if missing) | Linux: your package manager (`sudo pacman -S git`, `sudo apt-get install git`, ...). macOS: `brew install git` or Apple's Command Line Tools. Windows: portable MinGit in `patchsu-data/tools/git` | On Linux your password goes to sudo, never to the CLI. |
| osu! source | `patchsu-data/osu-lazer` | Shallow download of the one commit the patches are made for (~25 MB). |
| NuGet packages | `patchsu-data/nuget` | Kept out of `~/.nuget`. |
| The game | Linux `~/.local/share/patchsu-app`, Windows `%LOCALAPPDATA%\patchsu-app`, macOS `~/Applications/Patchsu.app` | Shares the official osu! data folder (beatmaps, skins, scores). |

The game gets the Patchsu logo: as the `.exe` icon and window icon (it replaces osu!'s `lazer.ico` before the build), the
Linux menu icon and the macOS app icon.

After a successful build, the final screen offers to delete `patchsu-data`. The game stays installed; the next build downloads everything again. Keeping it makes rebuilds much faster.

## Commands

```
Patchsu-CLI                                   interactive
Patchsu-CLI build --preset "Vanilla+" --yes
Patchsu-CLI build --patches 0003,0009 --yes   # 0009 pulls in 0002
Patchsu-CLI list                              # patches, needs, presets
Patchsu-CLI check                             # exit code 1 if something is missing
Patchsu-CLI uninstall                         # game + launcher; osu! data is untouched
```

Flags: `--release TAG` (patches from a GitHub release instead of the built-in ones; `latest` = the newest),
`--local DIR` (local patches folder), `--data-dir DIR`, `--prefix DIR` (install into exactly this folder; see Install location), `--no-launcher`,
`--no-update` (stay on the built-in patches).

Where the patches come from, first match wins: `--local DIR`, `--release TAG`, a `patches/` folder next to the program,
the patches built into the program, the newest GitHub release (only for a CLI built without patches).

**Patch updates.** Before using the built-in patches, the CLI asks GitHub (for up to 8 seconds) for the newest patch
release. If it came out after the built-in patches were last changed and its patches or presets differ, the
CLI uses it instead and says so on the first screen; the patches are downloaded with the build. So a new patch release
reaches users without a new Patchsu-CLI download. A release that needs a newer CLI (`minCli`) is skipped with a note.
Offline, the built-in patches are used without a fuss. `--no-update` skips the check. The time the built-in patches
last changed is written by `go generate` (`internal/bundle/patches/bundled-at`: the last commit touching
`Patchsu/patches`, or the current time when they have uncommitted changes).

## For patch authors

Patch releases on [github.com/PorkingMane/Patchsu](https://github.com/PorkingMane/Patchsu) carry two assets:

- `manifest.json`: osu! commit, .NET channel, patches (id, file, name, description, needs, SHA-256) and presets.
- `patches.zip`: the `.patch` files.

Both are generated from the patches folder and its hand-edited `patches.meta.json`:

```
Patchsu-CLI make-release --local ../Patchsu/patches --tag v26 --out dist/
```

The CLI takes the newest release whose tag doesn't start with `cli-` (CLI releases are tagged `cli-v1.1.0`). To try patches without publishing, use `--local DIR`. The CLI then reads `patches.meta.json` directly and computes the hashes itself.

A `patches/` folder next to the program (with `manifest.json` or `patches.meta.json`) is used automatically, as if `--local` was given. `./install-dev.sh` builds the CLI into `Patchsu-CLI/Patchsu-CLI`; that build also finds `../Patchsu/patches` on its own, so `./Patchsu-CLI` in this folder builds from your working patches.

The manifest is validated on load: unknown ids in `needs` or presets, loops, and duplicate ids are errors.

## Building the CLI

```
go generate ./internal/bundle      # copies ../Patchsu/patches and the logo into the binary
go build -o Patchsu-CLI .
go test ./...
# release binaries (run go generate first, or they have no built-in patches)
for t in linux/amd64 linux/arm64 darwin/amd64 darwin/arm64 windows/amd64 windows/arm64; do
  GOOS=${t%/*} GOARCH=${t#*/} go build -ldflags "-s -w -X main.Version=1.1.0" -o dist/Patchsu-CLI-${t%/*}-${t#*/}$( [ ${t%/*} = windows ] && echo .exe ) .
done
```

Without `go generate` the binary has no built-in patches and looks for a GitHub release instead. The patch copies in
`internal/bundle/patches` aren't committed; the logo copies in `internal/bundle/logo` are.

The Windows icon of `Patchsu-CLI.exe` comes from `rsrc_windows_amd64.syso` / `rsrc_windows_arm64.syso` (committed).
After changing `assets/patchsu.ico`, run `go generate ./internal/bundle` and then `go generate .` (uses go-winres, needs network).


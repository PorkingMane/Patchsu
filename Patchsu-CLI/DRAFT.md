# Patchsu-CLI: design (draft v3)

> Historical draft: patch numbers here are the ones from before the 2026-11-26 renumbering (0007a/b/c, 0101, …).
> The mapping to the current numbers is in [Agents/CHANGELOG_AGENTS.md](../Agents/CHANGELOG_AGENTS.md).

> Implemented in this folder (see README.md). Differences from draft v2 are listed at the end, under "Changes while implementing".

A single Go binary, `Patchsu-CLI` (`Patchsu-CLI.exe` on Windows), that lets you pick Patchsu patches (one by one or from a preset), checks your system, fetches what's missing and builds the patched osu!lazer.
It builds the main Patchsu build only. Older patch sets (`versions/`) and the clean osu! build stay with the existing scripts.

---

## 1. Flow overview

```
 ┌──────────────────┐   ┌──────────────────┐   ┌───────────────┐   ┌───────────────┐   ┌────────────────────┐   ┌─────────────┐
 │ 0. Fetch patch   │ → │ 1. Pick preset / │ → │ 2. Detect OS  │ → │ 3. Check deps │ → │ 4. Ask: download / │ → │ 5. Build +  │
 │    list (GitHub) │   │    patches       │   │    + arch     │   │               │   │    install, where? │   │    install  │
 └──────────────────┘   └──────────────────┘   └───────────────┘   └───────────────┘   └────────────────────┘   └─────────────┘
```

All steps are screens of one TUI. `Esc` goes back a step. `q` / `Ctrl+C` quits, and asks first once downloads or the build have started.

---

## 2. Step 0: patches come from GitHub releases

The binary contains no patches or patch descriptions. Each release of the Patchsu repo carries:

| Release asset | Contents |
|---|---|
| `manifest.json` | osu! commit, .NET SDK version, patch list (id, file, name, description, needs), presets, SHA-256 of every patch file |
| `patches.zip` | the `.patch` files |

- On start, the CLI asks the GitHub API for the latest release and downloads only `manifest.json` (a few KB, kept in memory). That is enough to draw the list.
- `patches.zip` is downloaded in step 4, into the data folder, and each file is checked against the SHA-256 in the manifest.
- The release is cached in the data folder (`patchsu-data/releases/<tag>/`). With no network, the CLI uses the newest cached release and shows `offline: using release v26 (cached)`.
- The header shows which release is in use, e.g. `release v26 · osu!lazer 8cb6e6e68d`.
- Flags: `--release TAG` builds an older release, and `--patches DIR` uses a local folder with its own `manifest.json` (for patch development, no download).

The CLI's version and the patch release are independent: patches can update without a new CLI. The manifest has a `"minCli"` field, so an old CLI can say "this release needs Patchsu-CLI ≥ 1.2".

Repo: **https://github.com/PorkingMane/Patchsu**. The CLI lives in the same repo, in `Patchsu-CLI/`, with its own release tags (`cli-v1.0.0`) next to the patch releases (`v26`). The CLI only reads releases whose tag isn't `cli-*`. The repo has no patch releases yet, so until the first one exists the CLI says "No patch release published yet" (or use `--patches DIR`).

---

## 3. Step 1: presets and patch selection

### Layout

```
 Patchsu-CLI                                            release v26 · osu!lazer 8cb6e6e68d

  Preset ‹ Minimal Vanilla ›   Vanilla-ish local play   Custom mods   Custom local play
         Patchsu   Custom

   [■] 0001  Online restrictions                      required
   [ ] 0002  Local profiles                           big, slower build
   [ ] 0003  Mods from Ritsu
   [ ] 0004  Mods from Torii
 › [x] 0005  Interface adjustments
   [ ] 0006  Pause PP penalty                         needs 0002              ← grayed out
   [x] 0007a Replay mod toggles
   [ ] 0007b Replay mod toggles: Ritsu mods           needs 0003              ← grayed out
   [ ] 0007c Replay mod toggles: Torii mods           needs 0004              ← grayed out
   [ ] 0008  PP simulation
   [x] 0010  Perfect-play PP sorting
   [ ] 0011  Mod compatibility                        needs 0002              ← grayed out
   [ ] 0012  No-Fail PP                               needs 0002              ← grayed out

 ─ 0005 Interface adjustments ───────────────────────────────────────────────────────────
  Song select tweaks: PP leaderboard sort, right-click reverses sorts, middle-click mute...

  Also disabled 2: 0006 Pause PP penalty, 0012 No-Fail PP

 ↑/↓ move  space toggle  tab/←/→ preset  enter continue  ? help
```

### Selection rules

- **0001 is always on** and can't be turned off (`[■]`, "required").
- A patch whose requirements aren't all selected is **grayed out but still selectable**. The "needs" column lists only the requirements that are still missing.
- **Selecting a grayed-out patch selects everything it needs**, recursively. Notice at the bottom: `Also enabled 2: 0003 Mods from Ritsu, 0007a Replay mod toggles`.
- **Deselecting a patch others need** removes those dependents too, without asking. Notice at the bottom: `Also disabled 2: 0006 Pause PP penalty, 0012 No-Fail PP`.
- The notice stays until the next toggle.
- Patches are always applied in manifest order, whatever order you selected them in.

### Presets

The preset row is at the top. `tab` or `←`/`→` moves between presets, and choosing one replaces the whole selection. Changing any checkbox afterwards switches the row to **Custom**, shown as `Custom (from Vanilla-ish local play)`. The last selection is remembered for the next run.

| Preset | Patches | Idea |
|---|---|---|
| **Minimal Vanilla** | 0001, 0005, 0007a, 0008, 0010 | Close to vanilla: UI tweaks, replay mod toggles, PP simulation, perfect-play PP sorting. |
| **Vanilla-ish local play** | Minimal Vanilla + 0002 | Minimal Vanilla plus local profiles. |
| **Custom mods** | Minimal Vanilla + 0003, 0004, 0007b, 0007c | Minimal Vanilla plus the Ritsu and Torii mods and their replay toggles. |
| **Custom local play** | Vanilla-ish local play + 0003, 0004, 0007b, 0007c | Local profiles plus the custom mods. |
| **Patchsu** | every patch | Everything at once. |

The presets live in `manifest.json` (`"presets": [{ "name": ..., "description": ..., "patches": [...] }]`), so a new patch can join a preset with a new release, without a CLI update. `"patches": "*"` means every patch.

### Dependency graph

| Patch | Needs |
|---|---|
| 0001 Online restrictions | – (always on) |
| 0002 Local profiles | 0001 |
| 0003 Mods from Ritsu | 0001 |
| 0004 Mods from Torii | 0001 |
| 0005 Interface adjustments | 0001 |
| 0006 Pause PP penalty | 0002 |
| 0007a Replay mod toggles | 0001 |
| 0007b Replay mod toggles: Ritsu | 0003, 0007a |
| 0007c Replay mod toggles: Torii | 0004, 0007a |
| 0008 PP simulation | 0001 |
| 0010 Perfect-play PP sorting | 0001 |
| 0011 Mod compatibility | 0002 |
| 0012 No-Fail PP | 0002 |

(0002 already needs 0001, so 0001 isn't repeated in the rows that need 0002.)

Change from draft 1: 0005 needs only 0001. It was made on top of 0001+0002, but it doesn't use local profiles. Checked: 0001 + 0005 + 0007a + 0010 (the Minimal Vanilla set) applies to 8cb6e6e68d and `dotnet build` of osu.Desktop succeeds.

### Manifest example

```json
{
  "release": "v26",
  "minCli": "1.0.0",
  "osuCommit": "8cb6e6e68db6ad3387004c004ed2eb5f9728be37",
  "dotnetSdk": "10.0",
  "patches": [
    { "id": "0001", "file": "0001-patchsu-online-restrictions.patch", "name": "Online restrictions",
      "required": true, "needs": [], "sha256": "…", "description": "…" },
    { "id": "0007b", "file": "0007b-replay-mod-toggles-Ritsu.patch", "name": "Replay mod toggles: Ritsu mods",
      "needs": ["0003", "0007a"], "sha256": "…", "description": "…" }
  ],
  "presets": [
    { "name": "Minimal Vanilla", "patches": ["0005", "0007a", "0008", "0010"], "description": "…" },
    { "name": "Vanilla-ish local play", "patches": ["0002", "0005", "0007a", "0008", "0010"], "description": "…" },
    { "name": "Custom mods", "patches": ["0003", "0004", "0005", "0007a", "0007b", "0007c", "0008", "0010"], "description": "…" },
    { "name": "Custom local play", "patches": ["0002", "0003", "0004", "0005", "0007a", "0007b", "0007c", "0008", "0010"], "description": "…" },
    { "name": "Patchsu", "patches": "*", "description": "Every patch." }
  ]
}
```

A small repo script (`tools/make-release.sh`) generates `manifest.json` (hashes, file names) from the patches folder and a hand-edited `patches.meta.json` (names, descriptions, needs, presets). The CLI validates the manifest on load: unknown ids, cycles, and presets that name missing patches are errors.

---

## 4. Step 2: system detection

From Go's `runtime.GOOS` / `runtime.GOARCH`, plus `/etc/os-release` on Linux to find the package manager:

| OS | .NET RID | Install folder (default) | Launcher |
|---|---|---|---|
| Linux | `linux-x64` / `linux-arm64` | `~/.local/share/patchsu-app` | `.desktop` entry, `StartupWMClass=Patchsu` |
| macOS | `osx-x64` / `osx-arm64` | `~/Applications/Patchsu.app` | `.app` bundle (Info.plist, icon), ad-hoc signed with `codesign -s -`, otherwise Apple Silicon won't start it |
| Windows | `win-x64` / `win-arm64` | `%LOCALAPPDATA%\patchsu-app` | Start Menu shortcut `Patchsu.lnk` |

Shown as one line: `Detected: Linux x64 · CachyOS (pacman)`.

macOS is new: the current scripts don't build it. osu! itself ships macOS builds from the same `osu.Desktop` project, so the build is the same, but the `.app` bundle and signing are new code that can only be tested on a Mac.

---

## 5. Step 3: dependency check

```
 Checking what's needed

   Build
   ✓ .NET SDK 10.0.404          ~/.dotnet/dotnet

   Getting the files
   ✗ git                        not found
   ✗ osu!lazer source           not downloaded yet (8cb6e6e68d, ~150 MB)
   ✗ patches                    release v26 not downloaded yet (1.4 MB)

   Disk space: 4.1 GB needed, 212 GB free     ✓
```

| Item | Needed for | Found by |
|---|---|---|
| **.NET SDK 10+** | building | `dotnet --list-sdks`: `PATH`, then `~/.dotnet`, then `patchsu-data/tools/dotnet`. SDK 11 also counts (the build sets `rollForward: latestMajor`, as the scripts do now). |
| **git** | cloning osu! and applying patches (`git apply --3way`) | `git --version`: `PATH`, then `patchsu-data/tools/git` |
| **osu!lazer source** at the manifest's commit | building | `patchsu-data/osu-lazer`, or `--osu-source DIR` |
| **patches** of the chosen release | applying | `patchsu-data/releases/<tag>/`, checked against SHA-256 |
| **disk space** | everything | about 4 GB: SDK, source, NuGet packages, build output |

---

## 6. Step 4: getting what's missing

### Permission

```
 Some things are missing

   ✗ git                    system package    sudo pacman -S --needed git
   ✗ osu!lazer source       ~150 MB           from github.com/ppy/osu
   ✗ patches v26            1.4 MB            from github.com/PorkingMane/Patchsu

 Download / install them?

 › Yes
   No, I'll do it myself   (prints the same commands and exits)
```

Each line shows exactly what will run or where it downloads from, before anything happens.

### Installing git

Yes, the CLI installs git itself. The only thing it asks for is your password, and the password goes to `sudo`, not to the CLI:

| System | What runs | Password? |
|---|---|---|
| Arch / CachyOS / Manjaro | `sudo pacman -S --needed git` | sudo |
| Debian / Ubuntu / Mint | `sudo apt-get install -y git` | sudo |
| Fedora / RHEL | `sudo dnf install -y git` | sudo |
| openSUSE | `sudo zypper install -y git` | sudo |
| Alpine / Void / Gentoo | `apk add` / `xbps-install` / `emerge` with `sudo` (or `doas` when there's no sudo) | sudo / doas |
| macOS with Homebrew | `brew install git` | no |
| macOS without Homebrew | `xcode-select --install` (Apple's own dialog: Command Line Tools, includes git) | macOS asks in its dialog |
| Windows | **MinGit** portable zip from git-for-windows releases into `patchsu-data/tools/git` | no admin needed |

- The package manager is found from `/etc/os-release` (`ID` / `ID_LIKE`) and by checking which of `pacman`/`apt-get`/`dnf`/... exists.
- The TUI hands the terminal over to that command (Bubble Tea `ExecProcess`). You see the real sudo prompt and the package manager's output, then the TUI comes back and checks `git --version` again.
- With `pkexec` available and no terminal (e.g. started from a file manager), the graphical password prompt is used instead.
- If no known package manager is found, the CLI shows the message "Install git with your package manager, then press r to check again".

### .NET SDK

Always downloaded to the data folder, never installed system-wide and with no password: the official `dotnet-install.sh` / `dotnet-install.ps1 --channel 10.0 --install-dir patchsu-data/tools/dotnet`. Builds then use that SDK through `DOTNET_ROOT` (only for the CLI's own processes).

### Where the data folder goes

Asked the first time, then remembered in `Patchsu-CLI.json` in the user config folder (`~/.config/Patchsu-CLI/`, `%APPDATA%\Patchsu-CLI\`, `~/Library/Application Support/Patchsu-CLI/`):

```
 Where should Patchsu-CLI keep its downloads and build files?

 › Next to this program    /home/you/Downloads/Patchsu-CLI/patchsu-data
   Documents               /home/you/Documents/patchsu-data

   (about 4 GB; you can delete it after building, the game stays installed)
```

- "Next to this program" is the folder of `os.Executable()`. It's grayed out, with "(not writable)" next to it, if the folder can't be written (e.g. `Program Files`).
- Documents is found per system: Windows asks for the real Documents folder (`FOLDERID_Documents`), which is right even when it's moved or in OneDrive. macOS uses `~/Documents`. Linux uses `xdg-user-dir DOCUMENTS`, falling back to `~/Documents`.
- `--data-dir DIR` overrides both.

```
patchsu-data/
├── tools/dotnet/        portable .NET SDK (only if downloaded)
├── tools/git/           MinGit (Windows only, only if downloaded)
├── releases/v26/        manifest.json + patches of that release
├── osu-lazer/           osu! source (shallow fetch of the manifest commit); patches are applied here
├── nuget/               NuGet cache (NUGET_PACKAGES), so nothing lands in ~/.nuget
└── logs/                full log of the last build
```

Downloads show a progress bar and are checked with SHA-256 (patches) / SHA-512 (.NET). An interrupted download is restarted, never used half-finished.

---

## 7. Step 5: build and install

The same steps as `build-patchsu.sh` / `.cmd`, run from Go:

1. Reset `osu-lazer/` to the manifest commit (`checkout`, `reset --hard`, `clean -fd`).
2. Apply the selected patches in manifest order: plain `git apply`, then `git apply --3way` if that fails. If both fail: stop, name the patch, keep the log.
3. Set `rollForward` in `global.json` to `latestMajor`.
4. `dotnet publish osu.Desktop -c Release -r <RID> --self-contained` with the same PERF_OPTIONS as today.
5. Rename `osu!` to `Patchsu` and create the launcher for the system.
6. Write `patchsu-build.json` into the install folder: patches, preset, release, osu! commit, CLI version, date. A later run offers: `Installed: Vanilla-ish local play (release v25). Rebuild with release v26?`

```
 Building Patchsu · Vanilla-ish local play · release v26

   ✓ osu! source ready
   ✓ 0001 Online restrictions        ✓ 0007a Replay mod toggles
   ✓ 0002 Local profiles             ✓ 0010 Perfect-play PP sorting
   ✓ 0005 Interface adjustments
   ◐ dotnet publish   restoring packages...      ▕██████░░░░░░░░▏ 1m 12s

   l  show full log
```

---

## 8. Non-interactive use

```
Patchsu-CLI build --preset "Vanilla-ish local play" --yes
Patchsu-CLI build --patches 0005,0007b --data-dir ./patchsu-data --yes   # 0007b pulls in 0003 + 0007a
Patchsu-CLI list                 # patches, needs, presets of the latest release
Patchsu-CLI check                # only step 3; exit code 1 if something is missing
Patchsu-CLI uninstall            # removes the install + launcher; osu! data is untouched
```

`--yes` approves downloads. Installing git with sudo still shows the sudo prompt, since the password can't be skipped.

---

## 9. Go project layout

```
Patchsu-CLI/
├── go.mod
├── main.go
└── internal/
    ├── release/     GitHub API: latest/tagged release, manifest + patches.zip download, cache, offline fallback
    ├── manifest/    parse + validate, dependency closure (enable/disable), presets
    ├── tui/         Bubble Tea screens: picker, sysinfo, deps, download, build
    ├── sysinfo/     OS / arch / RID, distro + package manager, Documents folder, writable check, free space
    ├── deps/        detectors (dotnet, git, source) + installers (package manager via sudo, MinGit, dotnet-install)
    ├── download/    HTTP with progress, checksums, zip / tar.gz extraction
    └── build/       patch application, dotnet publish, launchers per OS (.desktop, .lnk, .app)
```

Libraries: `charmbracelet/bubbletea` + `bubbles` + `lipgloss` for the TUI, the standard library for the rest. The `.lnk` shortcut is made via PowerShell, the same way `build-patchsu.cmd` does it. Releases are cross-compiled for linux, darwin and windows on amd64 and arm64. Each is a single binary with no runtime needed.

---

## Status

All draft questions are answered. Nothing is uploaded to GitHub yet. Next step: implement, testing against a local `--patches` folder until the first release exists.

## Changes while implementing

- **`--local DIR`** is the flag for a local patches folder (`--patches` is the list of patch ids for `build`). The folder needs `manifest.json` or `patches.meta.json`; the latter is the hand-edited source file, kept at `Patchsu/patches/patches.meta.json`, and the CLI computes the hashes itself.
- **`Patchsu-CLI make-release --local DIR --tag TAG --out DIR`** writes the two release assets (`manifest.json`, `patches.zip`) instead of a separate `tools/make-release.sh`.
- **No `work/src` clone**: patches are applied in place in `patchsu-data/osu-lazer` after `git reset --hard` + `git clean -fd` to the manifest commit. Ignored `bin/`/`obj/` stay, so rebuilds are incremental.
- **osu! source** is a shallow fetch of only the manifest commit: about 25 MB, not 150 MB.
- **.NET SDK** comes from Microsoft's release metadata (`releases.json`): the SDK archive is downloaded, checked against its SHA-512 and unpacked into `patchsu-data/tools/dotnet`. No installer runs, nothing is registered system-wide, and only the CLI's own build processes use it (`DOTNET_ROOT`/`PATH`). An SDK you already have is used instead. The final screen offers to delete the whole data folder.
- If `/usr/bin/git` on macOS is only Apple's stub (no Command Line Tools), it counts as missing.

## Changes on 2026-11-25

- **Patches are built into the binary** (`internal/bundle`, filled by `go generate ./internal/bundle` from `Patchsu/patches`).
  This replaces "the binary contains no patches" from section 2. They're unpacked into `patchsu-data/releases/builtin-<hash>/`
  at the start of a build. GitHub releases still work with `--release TAG` (`latest` = the newest), and a CLI built without
  `go generate` falls back to them.
- **Presets** are now Vanilla+ (the old Minimal Vanilla + 0013) and Local play (every patch).
- **Logo**: the game's `lazer.ico` is replaced by the Patchsu logo before `dotnet publish` (exe + window icon); the Linux menu
  icon and the macOS `.icns` come from the logo PNG instead of osu!'s `lazer.png`. Patchsu-CLI.exe has the icon via committed
  `rsrc_windows_*.syso` files (go-winres).
- **Data folder / install location**: the data folder screen (first build, or `f` on the check screen) has Next to this
  program, Documents, Next to osu! data (`paths.FindOsuData`: `$XDG_DATA_HOME/osu`, `%APPDATA%\osu`,
  `~/Library/Application Support/osu`; only its parent is used) and a typed folder (gets `patchsu-data` appended unless
  it ends in it, because `d` on the done screen deletes the data folder whole). `i` on the check screen picks the install:
  Standard, Next to osu! data, or a typed folder; the game goes into `<folder>/<Name>` (`<Name>.app` on macOS), stored
  as `installDir` in `Patchsu-CLI.json`; `--prefix` still wins. `paths.Occupied` refuses a non-standard folder that holds
  something without `patchsu-build.json` or the game executable, since uninstall deletes the whole folder.
- **Patch updates**: with the built-in patches (no `--local` / `--release`), `release.Update` fetches the newest GitHub
  patch release (8 s timeout) and uses it when `published_at` is after `Source.Bundled` (from `bundled-at`, written by
  sync.go) and the patch hashes, osu! commit, presets or servers differ. A release whose `minCli` is too new is skipped
  with a notice. Offline: built-in, silently. `--no-update` / `Options.NoUpdate` turn it off. The TUI shows the result as
  the picker's notice; `build` / `list` / `check` print it.

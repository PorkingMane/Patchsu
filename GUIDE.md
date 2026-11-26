# Building Patchsu yourself

The easiest way is [Patchsu-CLI](Patchsu-CLI/README.md): it auto picks patches listed as dependecy for other ones, installs what's missing (without touching your system)
and builds everything. This guide is for doing the same by hand with the scripts in `Patchsu/`.

## What you need

- **git**
- **.NET SDK 10** (osu! pins it in `global.json`). The build scripts let a newer SDK (11) build too, by setting
  `rollForward: latestMajor` in their own copy of the source. Check with `dotnet --list-sdks`.
- About **3–4 GB** of disk: the osu! source, NuGet packages (~1.7 GB on the first build), build files and the installed game.
- Linux or Windows for the scripts. On macOS use Patchsu-CLI.

## 1. Get the source

```sh
git clone --recurse-submodules --shallow-submodules https://github.com/PorkingMane/Patchsu.git
cd Patchsu/Patchsu
```

Already cloned without submodules? Run `git submodule update --init --depth 1` in the repository.

`Patchsu/osu-lazer` is upstream [ppy/osu](https://github.com/ppy/osu) at the exact commit the patches are made for
(`8cb6e6e68d`). **Don't update it**: the patches only apply to that commit. The scripts never edit it either; they clone it into
`Patchsu/work/` and apply the patches there.

## 2. Choose the patches

`build-patchsu.sh` / `.cmd` apply **every** `*.patch` in `Patchsu/patches/`, in file-name order. To build fewer, move the ones you
don't want out of that folder. Keep what the remaining ones need (table in [PATCHES.md](PATCHES.md)):

- 0001 is always required.
- 0004 and 0009 need 0002.
- 0500 and 0501 get replay toggles for their mods when 0005 is applied too (nothing extra to pick).

A missing dependency shows up as `git apply` failing or as a compile error.

## 3. Build and install

Linux:

```sh
./build-patchsu.sh                 # build + install to ~/.local/share/patchsu-app + "Patchsu" menu entry
./build-patchsu.sh --prefix DIR    # install somewhere else
./build-patchsu.sh --no-desktop    # no menu entry
./build-patchsu.sh --uninstall     # remove the build and the menu entry (your osu! data is untouched)
```

Windows (`cmd`):

```bat
build-patchsu.cmd                  rem installs to %LOCALAPPDATA%\patchsu-app + Start Menu shortcut "Patchsu"
set "LP_PREFIX=D:\Games\Patchsu"
build-patchsu.cmd                  rem installs there instead
```

The first build takes a while (NuGet restore, and 0002 is big). Later builds reuse the downloaded packages.

### Applying the patches by hand

If you'd rather do it yourself:

```sh
git clone --shared Patchsu/osu-lazer my-osu && cd my-osu
for p in ../Patchsu/patches/{0001,0002,0003}-*.patch; do git apply --whitespace=nowarn "$p"; done
dotnet run --project osu.Desktop -c Release
```

Apply them in file-name order (0003 before 0003b, 0010 before 0500, and so on).

## Other scripts

| Script | What it does |
|---|---|
| `build-osu-clean.sh` / `.cmd` | Builds the same osu! commit **without** patches, the same way (Release, self-contained), into `~/.local/share/osu-clean-app`. For fair comparisons. `run` starts it. |
| `patchsu-diagnose.sh` / `.cmd` | Starts the installed Patchsu with some features turned off (`tags`, `autoswitch`, …, or `all`) via `OSU_LOCAL_DISABLE`, to find what costs performance. Nothing is saved. |
| `patchsu-check.targets` | Optional, for development. If `osu-lazer/after.osu.sln.targets` imports it, `dotnet build` of the solution builds the patched desktop game in a scratch clone instead. |

## Caveats

- **Data folder.** Patchsu uses the official osu! data folder (`~/.local/share/osu`, `%APPDATA%\osu`): beatmaps, skins, scores
  and settings are shared with the official client. Patchsu's own data is in its `patchsu-osu/` subfolder (every patch's settings in
  `PatchsuSettings.json`, local profiles, caches; an old `osu-local/` folder is renamed on the first start). Back it up if you care.
- **One at a time.** Only one of Patchsu and the official client can run at once; starting the second hands over to the first.
- **Logging in.** Patchsu is a third-party client once patched. Logging into your osu! account in it can get you banned if you tinker passed whatever provided, even though
  0001 blocks everything but read-only requests. Local profiles need no account at all, but its really nice to be loggined in and download maps directly in-game.
- **Scores made with patch-only mods** (0500, 0501, 0008's combinations) may not load correctly in the official client
  or in a Patchsu build without that patch. Treat them as local-only.
- **Removing a patch later** keeps your data: profile data and settings stay in `patchsu-osu/`, collection folders are just collection names.
- **License.** A build with 0500 contains GPL-3.0 code (Modosu); see [LICENSE](LICENSE).
- **Updates.** Patchsu never updates itself. Pull the repository (and its submodule) and build again.

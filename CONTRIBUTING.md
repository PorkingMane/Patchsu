# Contributing

Most of this repository is written with AI agents and I easily accept AI things, but **you** need to read it, test it and play with it, I won't ever accept half-ass pull requests that are written with Qwen3.6-7b and are just here because 'Hey this is really funny feature where osu logo changes to my logo from this very specific folder!'... Think about other people too, game is meant to be PLAYED and ENJOYED by humans, not AI agents... Despite this, 95% of this contributing page is written by Opus 5.5 xd

## AI agents

Point your agent at [AGENTS.md](Agents/AGENTS.md), it lists the performance issues I've come across, what every [patch](Agents/PATCHES_AGENTS.md) does in detail and how to [contribute](Agents/CONTRIBUTING_AGENTS.md) if you are an AI agent.

**Without 0001 nothing should build.** Every patch builds on the online restrictions. Pull requests that remove them, add write requests to the allow-list or submit anything to osu! servers won't be merged. Patches that connect to *other* servers (Akatsuki, Torii, etc.) are fine, as their own patch.

## Suggesting a feature / reporting a bug

Open an issue if you found something / want to suggest, but don't want to use AI or don't want to write things yourself. If this is an actual problem and/or you are being reasonable, I'll try to implement it ASAP :)

For a bug, say which patches (or preset) you built, your OS, and the steps how can I reproduce it. Attach latest `runtime.log` and `performance.log` from the osu! data folder (`~/.local/share/osu/logs` on Linux, `%APPDATA%\osu\logs` on Windows) if the bug is severe and not simple thing.

For a feature, describe what you want to see; self-made mockups or screenshots would help a ton to see your vision. However, we will NOT be going off official osu!lazer development path and implement things that are reasonable, for example: 
- Implement multiplayer connection locally, so I can 1v1 my friends on your client with custom mods!
This is possible to do and does not contradicts lazer development, also our client is local-focused, so this can and probably will be done
- Implement mods like Aim-assist, Time-warp and *insert favorite cheat name* because this is local anyway so why not
No. This is going off track of lazer development and adds features that can be used maliciously for local-play-multiplayer, Custom servers and Official osu!Bancho. Fuck you in particular.

Features that make sense for vanilla lazer might already be suggested for official lazer build via [Discussion](https://github.com/ppy/osu/discussions) tab on GitHub, please check if its there, and if so, also link it for me to read through. For example [THIS](https://github.com/ppy/osu/discussions/38941) is a REALLY GOOD suggestion which has both really good description and self-made mockup and deserve to be both in .patch files and in official osu!lazer!

## Writing a patch

A patch is a `git diff --binary` against the upstream commit in `Patchsu/osu-lazer` (`8cb6e6e68d`) plus whatever it needs.

1. **Pick the smallest base.** Most patches should need only 0001. Depend on 0002 only if you really use local profile code.
2. **Write the patch file**: a plain-text header, then the diff.
   ```sh
   { cat header.txt; git diff --binary HEAD~1 HEAD; } > ../../patches/00NN-short-name.patch
   ```
   In the header type what the patch does, what it needs, also write a small description how it works that will be used by AI to look through code and rewrite its features more detailed in [PATCHES.md](Agents/PATCHES_AGENTS.md).
3. **Add it to `Patchsu/patches/patches.meta.json`**: `id`, `file`, `name`, `needs`, `description` (one or two sentences for the CLI description).
4. **Check if its still stackable.** Your patch has to apply on its own (or with ones it also needs) *and* on the full stack, in file-name order. Prefer new files and `partial` classes, and don't rename things other patches touch. File names must sort correctly under any locale (use letter suffixes like `0014a`, `0014b`).
5. **Settings** in Settings → Patchsu need to create 1 subsection per patch with a divide text between them as divider between internal sections. `PatchSettingsSubsection` from 0001 with `PatchNumber` and a summary of three words or less (e.g "Patch 9. Ignore No Fail"). It's found by reflection, so you never edit the list. Only patches that have settings get a subsection. The values are saved through `PatchsuSettings.Current` (also from 0001) in `patchsu-osu/PatchsuSettings.json`, one section per patch, not in game.ini.

When updating an existing patch, regenerate only the files you changed with `tools/merge_sections.py` so other sections stay byte for byte:

```sh
tools/merge_sections.py old.patch full-new.diff new.patch osu.Game/Path/To/Changed.cs [...]
```

## Before opening a pull request

- Apply the full stack and every preset to a fresh clone of `Patchsu/osu-lazer` (or build them with `Patchsu-CLI --local Patchsu/patches`).
- `dotnet build osu.Desktop` should build with **0 warnings**.
- Run the tests if you/your agent did any. Test files live in `Patchsu/work/*-tests/` and `Patchsu/work/opt-tests/`. They are not part of the patches. Copy them
  into osu!'s test projects in a patched clone (the folder's files say where, e.g. `osu.Game.Tests/Visual/Navigation`) and run
  `dotnet test osu.Game.Tests --filter <TestName>`. Add tests for what you change.
- Play it. Check it in the real game, not only in tests, and against light and dark backgrounds if it draws something.
- Mods that change beatmaps (`IBeatmapVariant`) must keep **byte-identical** output, or old replays break.

One patch per pull request please, otherwise I won't be able to keep up/test everything myself.

## License

By contributing you agree that your patch is MIT licensed, like osu!lazer, unless it ports code under another license (say so in the
patch header and add that license file to the patch, like 0500 does for GPL-3.0).

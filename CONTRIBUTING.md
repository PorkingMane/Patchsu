# Contributing

Most of this repository is written with AI agents and I easily accept AI things, but **you** need to read it, test it and play with it, I won't ever accept half-ass pull requests that are written with Qwen3.6-7b and are just here because 'Hey this is really funny feature where osu logo changes to my logo from this very specific folder!'... Think about other people too, game is meant to be PLAYED and ENJOYED by humans, not AI agents... Despite this, 95% of this contributing page is written by Opus 5.5 xd

## AI agents

Point your agent at [AGENTS.md](AGENTS.md), it lists the performance and patch rules I learned while tinkering.

**Without 0001 nothing should build.** Every patch builds on the online restrictions. Pull requests that remove them, add write requests to the allow-list or submit anything to osu! servers won't be merged. Patches that connect to *other* servers (Akatsuki, Torii, etc.) are fine, as their own patch.

## Suggesting a feature / reporting a bug

Open an issue; For a bug, say which patches (or preset) you built, your OS, and the steps; attach `logs/` from the osu! data folder
if it crashed. For a feature, describe what you want to see; mock-ups or screenshots help a lot. Features that make sense for
vanilla lazer might already be suggested for official lazer build via 'Discussion' tab in github, please check if its there, and if so, also link it for me to read through.

## Writing a patch

A patch is a `git diff --binary` against the upstream commit in `Patchsu/osu-lazer` (`8cb6e6e68d`) plus whatever it needs.

1. **Pick the smallest base.** Most patches should need only 0001. Depend on 0002 only if you really use local profile code.
   The "Vanilla+" preset builds 0005, 0005b, 0007, 0008, 0010 and 0013 **without 0002**, and 0003 and 0004 must build without it too, so those must never
   reference 0002 code. The active local profile is reachable through the API's `LocalUser` instead.
2. **Write the patch file**: a plain-text header, then the diff.
   ```sh
   { cat header.txt; git diff --binary HEAD~1 HEAD; } > ../../patches/00NN-short-name.patch
   ```
   The header is everything above the first `diff --git`: what the patch does for players, what it needs, and a
   "How it works (for later patches)" part naming the hooks other patches have to stay away from. Look at 0007 or 0013, or better ask your AI agent for it. Its a mess xdd.
3. **Add it to `Patchsu/patches/patches.meta.json`**: `id`, `file`, `name`, `needs`, `description` (one or two sentences for the CLI description).
   Add it to presets if it fits one. See [PATCHES.md](PATCHES.md#dependencies-and-presets).
4. **Check if its still stackable.** Your patch has to apply on its own base *and* on the full stack, in file-name order. Prefer new files and
   `partial` classes, and don't rename things other patches touch. File names must sort correctly under any locale (use letter
   suffixes like `0014a`, `0014b`, never `0014` next to `0014-b`).
   To add something for another *optional* patch without depending on it, use a compile flag: 0007 defines
   `PATCHSU_REPLAY_MOD_TOGGLES` in `Directory.Build.props`, and 0003 / 0004 wrap their replay-toggle code in
   `#if PATCHSU_REPLAY_MOD_TOGGLES`. Build both with and without the flag.
5. **Settings** in Settings → Patchsu need to create 1 subsection per patch with *insert the name for audio auto adjust plaque* as divider between internal sections. Subclass `PatchSettingsSubsection` (from 0002) with
   `PatchNumber` and a summary of three words or less ("Patch 12. Ignore No Fail"). It's found by reflection, so you never edit the
   list. Only patches that have settings get a subsection. A patch that can't need 0002 puts its settings in a vanilla section.
6. **Keep the disclaimer comment once per patch**, at the top of one new file (copy it from an existing patch).

When updating an existing patch, regenerate only the files you changed with `tools/merge_sections.py` so other sections stay byte for byte:

```sh
tools/merge_sections.py old.patch full-new.diff new.patch osu.Game/Path/To/Changed.cs [...]
```

## Before opening a pull request

- Apply the full stack and every preset to a fresh clone of `Patchsu/osu-lazer` (or build them with `Patchsu-CLI --local Patchsu/patches`).
- `dotnet build osu.Desktop` with **0 warnings**.
- Run the tests if you/your agent did any. Test files live in `Patchsu/work/*-tests/` and `Patchsu/work/opt-tests/`. They are not part of the patches: copy them
  into osu!'s test projects in a patched clone (the folder's files say where, e.g. `osu.Game.Tests/Visual/Navigation`) and run
  `dotnet test osu.Game.Tests --filter <TestName>`. Add tests for what you change.
- Play it. Check it in the real game, not only in tests, and against light and dark backgrounds if it draws something.
- Mods that change beatmaps (`IBeatmapVariant`) must keep **byte-identical** output, or old replays break.

One patch per pull request please, otherwise I won't be able to keep up/test everything myself.

## License

By contributing you agree that your patch is MIT licensed, like osu!lazer, unless it ports code under another license (say so in the
patch header and add that license file to the patch, like 0003 does for GPL-3.0).

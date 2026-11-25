# AGENTS.md: performance rules for Patchsu patch code

Lessons from the 2026-11-24 optimisation passes (details: `Patchsu/work/notes/patch-optimisation-review.md`).
Every item below was a real cost found in patches. Fixed now, but there is a list of ground rules you need to follow.
If you need to check what EACH patch does, look at Patchsu/PATCHES.md (Not PATCHES_FOR_HUMANS.md in root folder)

## Don't → do instead

**Mods and scores**
- **Don't** read `score.Mods` over and over on a detached, cloned (`DeepClone()`) or realm score. Each read rebuilds every mod via
  reflection (`Ruleset.CreateInstance()` per mod), and `DeepClone()` drops the built mods.
  **Do** `score.Mods = score.Mods;` once after cloning or detaching (the setter caches them). Check acronyms through `score.APIMods`
  (RX / AP / NF / CL need no mod objects).
- **Don't** work out mod facts per score when scanning many scores. **Do** cache per distinct `ModsJson` (see
  `LocalProfileStatistics.ModFactsCache`). Make one `Ruleset` instance per run, not one per item.

**Per-frame code (`Update()`, `UpdateAfterChildren()`)**
- **Don't** compare mods, build LINQ queries, allocate, query realm or poll state every frame.
  **Do** update on change (bindables, events, `Changed`) or cache the comparison result when the inputs change.
- **Don't** busy-wait threads (`Thread.Sleep(50)` loops). **Do** wait on a `ManualResetEventSlim` / `WaitHandle` that's set on
  change, with a long fallback timeout.

**Realm**
- **Don't** run one query per row on unindexed fields (`FirstOrDefault(b => b.MD5Hash == …)` in a loop).
  **Do** run one `Filter("MD5Hash == $0 OR MD5Hash == $1 …", …)` for the batch.
- **Don't** `Detach()` whole objects in big loops. **Do** read only the fields you need inside `realm.Run`.

**UI lists and pages**
- **Don't** build right-click menus for every row up front. **Do** build them when the menu opens
  (`ContextMenuItems => CreateContextMenuItems?.Invoke()`).
- **Don't** clear and rebuild a whole list on "show more". **Do** append only the new rows.
- **Don't** load full-size beatmap backgrounds for thumbnails. **Do** use a shrunk, shared texture
  (`LocalProfileOverlay.BeatmapThumbnails`) inside a `DelayedLoadUnloadWrapper`. `GetPanelBackground()` is a 6.4:1 crop, so it only suits wide strips.
- **Don't** keep content permanently inside an uncached `BufferedContainer` (a full-screen framebuffer redrawn every frame) when
  its effect is only sometimes needed. **Do** move the content into the buffer only while the effect runs (see `usePageBuffer`).
- Scrolled-out content is "masked away": not drawn, and its insides not updated. That's what you want for long lists. But anything
  **added** below the visible area never loads, because its parent doesn't update it, and then the page stays short. **Do** give the
  containers that receive new children `RequiresChildrenUpdate => true` (`UpdatingContainer` / `UpdatingFillFlowContainer`).
  **Don't** switch masking off for a whole page (`ComputeChildMaskingBounds` override): that draws every off-screen row.

**Drawing: fewer draw calls** (see the frame-statistics overlay: `DrawCalls`, `TextureBinds`, `FBORedraw`)
- Every switch of texture, shader or masking inside a drawn tree starts a new draw call. A row with a background box, rank badge,
  several fonts, mod icons and a PP box costs about 12 calls, so 30 visible rows cost 360 calls before anything else.
- **Do** draw static, many-part content (list rows, statistics blocks) once into a **cached** framebuffer and show it as one quad:
  `CachedDrawBuffer` in `LocalProfileOverlay.cs` (`BufferedContainer(pixelSnapping: true, cachedFrameBuffer: true)`). Pixel snapping
  keeps text sharp at any scroll position. Each cached row keeps a framebuffer (4 bytes per pixel, about 0.3 MB per row at 1440p).
- A plain cached `BufferedContainer` **only redraws when its size changes, and stops updating its children**, so hover highlights
  and "N minutes ago" dates freeze. `CachedDrawBuffer` keeps updating children and calls `ForceRedraw()` when any descendant's
  `InvalidationID` changes. Copy that pattern; don't use a bare `cachedFrameBuffer: true` for anything that changes.
- **Don't** put continuously animated parts (e.g. `DrawableRank`'s moving triangles) inside a cached buffer: the buffer would redraw
  every frame. **Do** leave an empty placeholder of the same size in the picture and draw the live part above it (`LiveLayer.Track`).
  Proxies (`CreateProxy()`) do **not** work for content inside a cached buffer: nothing gets drawn.
- Measure on a real renderer, because headless tests can't count draw calls: a scratch `OsuGame` subclass reading
  `GlobalStatistics.Get<long>("Draw", "DrawCalls")` (and `TextureBinds`, `FBORedraw`), with its own data directory. In headless tests,
  check culling by counting drawables that are not masked away (`Patchsu/work/opt-tests/TestSceneProfileDrawCost.cs`).
- After any buffering change, compare screenshots before and after (badges, hover, faded rows), not only the numbers.

**Files and caches**
- Keep JSON readable (indented). The user wants that. **Don't** `JsonConvert.SerializeObject(...)` large data into a string.
  **Do** `JsonSerializer.CreateDefault(settings).Serialize(new JsonTextWriter(writer), value)` straight into the file stream.
- **Don't** write files synchronously on every change, or while holding a lock that readers need. **Do** debounce the save
  (the 500 ms `save()`/`Flush()` pattern in `LocalProfileStore`) and write a snapshot outside the lock.
- **Don't** repeat long keys per entry. **Do** nest by setup, then by beatmap hash, and round stored values
  (PP 0.01, stars 0.001). Migrate old files on load instead of bumping a version that throws data away.
- Shared singletons per storage must compare the **same** storage object they were created with. `PerfectPerformanceCache.For()`
  once compared against a sub-storage and re-read a 2.8 MB file on every filter change.
- **Don't** throw away session state on every filter keystroke. **Do** reuse it while its identity (ruleset, mods, options) is unchanged.
- PP-only caches follow `LocalProfileManager.PerformanceRevision`. Anything shown on the page follows `Revision`.

**Algorithms and mods code**
- **Don't** re-sort inside a loop (O(n² log n)). **Do** keep a sorted list (binary insert/remove).
- Hoist loop invariants (`OfType<…>().FirstOrDefault()`, repeated `TimingPointAt`, repeated pure checks).
- **No debug `Logger.Log`** in `ApplyToBeatmap` / difficulty paths. They run on every star-rating calculation.
- **Don't** declare a class with a framework or game type's name in a shared namespace. 0003's `CircularContainer` in
  `osu.Game.Rulesets.Osu.Mods` silently replaced the framework class for osu!'s Bubbles mod.
- **Don't** copy upstream helpers. **Do** call the public `OsuHitObjectGenerationUtils` methods. Beatmap-variant mods
  (`IBeatmapVariant`) must keep **byte-identical** output (old replays depend on it): check with
  `Patchsu/work/opt-tests/RitsuVariantOutputTest.cs`, dumping before and after and comparing with `cmp`. Float trig is not exact, even when rotating by 0.

## Patch rules that affect this

- `Patchsu/patches/patches.meta.json` lists each patch's `needs`. 0003, 0004, 0005, 0005b, 0007, 0008, 0010 and 0013 must build **without 0002**
  (the "Vanilla+" preset, and 0003/0004 on their own). Reach 0002 state through game APIs instead: the active local profile is the API's `LocalUser`.
- 0003 and 0004 contain the replay toggles for their own mods inside `#if PATCHSU_REPLAY_MOD_TOGGLES`, which 0007 defines in
  `Directory.Build.props`. Build them both with and without 0007.
- Regenerate a patch by splicing only the files you changed (`tools/merge_sections.py`) against that patch's own base. Keep the
  disclaimer header **once per patch**.
- Before finishing:
  - apply the full stack plus the presets;
  - build with **0 warnings**;
  - run the tests in `Patchsu/work/*-tests` and `Patchsu/work/opt-tests`;
  - save backups in `Patchsu/work/old-patches/`;
  - check that no other session changed a patch while you worked (compare with your backup before copying).
- `/tmp` is a 16 GB tmpfs. Every scratch clone and build is about 0.8–2 GB, so delete them as you go. A full `/tmp` makes headless tests fail
  with misleading errors ("No space left on device").

## Repository layout (what's tracked, what's local)

- Tracked: `Patchsu/patches/` (+ `patches.meta.json`), the build scripts in `Patchsu/`, `Patchsu-CLI/`, `tools/merge_sections.py`,
  the test files in `Patchsu/work/*-tests/` and `Patchsu/work/opt-tests/`, `Patchsu/work/notes/patch-optimisation-review.md`, and the
  root docs (README, PATCHES, GUIDE, CONTRIBUTING, CHANGELOG, this file).
- `Patchsu/osu-lazer` is a git submodule (ppy/osu at `8cb6e6e68d`). **Never edit or update it**; work in clones.
- Local only (gitignored): `Patchsu/work/*` clones and builds, `work/old-patches/` backups, `Patchsu/versions/`, `Patchsu/archive/`,
  and **`Ritsu/`**. `Ritsu/` exists on the maintainer's machine only, to port Patchsu features over to Ritsu. It is never part of this
  repository; don't reference it from patches, docs or scripts that are committed.
- When `patches/` changes, update `patches.meta.json`, `PATCHES.md` and `CHANGELOG.md` in the same change.

## Working on a patch

- **Work clones**: `git clone --shared Patchsu/osu-lazer Patchsu/work/pNN`, apply the patch's base (its `needs`, in glob order),
  commit as the base, then commit your change. Regenerate with `{ cat header; git diff --binary HEAD~1 HEAD; }`. Patches without a
  header (0005, 0006) are just the diff. Keep each patch's header in a durable file (`work/old-patches/<id>-header.txt`), not only in a
  scratch directory.
- **Before overwriting a patch, diff the regenerated file against the current one.** The maintainer sometimes hand-edits patches
  (UI wording especially). Port any hunk you didn't make into the work clone first, or splice only your files with `merge_sections.py`.
- New files in a clone need `git add -N` (or `git add -A`) before `git diff`, or they're silently missing from the patch.
- The clones check `.cs` files out with **CRLF**. Scripted edits must match `\r\n`.
- `global.json` wants **SDK 10**. If the system `dotnet` is newer, run with `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH`
  (or wherever SDK 10 is).
- Tests are copied into the clone's test projects (`osu.Game.Tests/Visual/Navigation`, `…/SongSelect`, `…/Gameplay`, `NonVisual`,
  `osu.Game.Rulesets.Osu.Tests`) and run with `dotnet test --filter`. They never go into the patches.
- Visual changes: check them on screen (a scratch `OsuGameBase`/`OsuGame` subclass + `Host.TakeScreenshotAsync`, with its own data
  directory that you delete afterwards), compare screenshots by pixels, and check light and coloured backgrounds too, not just black.

## Things the maintainer decided (don't "fix" them back)

- Settings → Patchsu: one `PatchSettingsSubsection` per patch that has settings, titled "Patch N. <≤3 words>". No info-only subsections.
- The product name is "Patchsu"; the data subfolder `osu-local/` and the `OSU_LOCAL_DISABLE` env var keep their old names (existing data
  depends on them).
- JSON files stay indented and readable. Don't store values nobody needs (e.g. unfinished plays keep only non-zero statistics).
- Replay mod toggles (0007, and the toggles 0003/0004/0009 build in with it) must **never change a replay's result**: only what is seen and heard. Rate mods stay in the gameplay
  rate and are cancelled on the audible track only; hidden objects stay clickable.

## Safety

- **Never touch the real osu! data folder** (`~/.local/share/osu`, `%APPDATA%\osu`) from tests or scratch apps. Use a separate data
  directory.
- `BeatmapManager.Import(new ImportTask(path))` **deletes the source file** after importing. Copy a user's `.osz` (or a repo test
  resource) to a scratch directory before importing it.
- Never build or install over the user's installed Patchsu (`~/.local/share/patchsu-app`, `%APPDATA%\patchsu-app`) unless asked; use `--prefix` into scratch.
- Don't push, publish releases or upload anything to GitHub unless the maintainer says so.

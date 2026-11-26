# Patchsu patch optimisation review (2026-11-24)

Scope: all 13 patches in `Patchsu/patches` at the time (old numbers 0001–0012, including 0007a/b/c; renumbered on 2026-11-26, the numbers below use the new scheme), applied together on upstream `8cb6e6e68d`.
Nothing was changed. Every item here is a proposal. Unless a line says "measured", the speed numbers are estimates.
Line numbers point at the **patched tree** (the file as it is after the patches are applied).

Your data, for scale (read-only look at `~/.local/share/osu/osu-local`): 1 profile, 1,744 cached score PPs,
503 played beatmaps, and **15,117 perfect-play PP entries in `perfect-pp-cache.json` (2.8 MB)**.

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
- **Don't** declare a class with a framework or game type's name in a shared namespace. 0500's `CircularContainer` in
  `osu.Game.Rulesets.Osu.Mods` silently replaced the framework class for osu!'s Bubbles mod.
- **Don't** copy upstream helpers. **Do** call the public `OsuHitObjectGenerationUtils` methods. Beatmap-variant mods
  (`IBeatmapVariant`) must keep **byte-identical** output (old replays depend on it): check with
  `Patchsu/work/opt-tests/RitsuVariantOutputTest.cs`, dumping before and after and comparing with `cmp`. Float trig is not exact, even when rotating by 0.

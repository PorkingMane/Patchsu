# Patchsu patch optimisation review (2026-11-24)

Scope: all 13 patches in `Patchsu/patches` (0001–0012, including 0007a/b/c), applied together on upstream `8cb6e6e68d`.
Nothing was changed. Every item here is a proposal. Unless a line says "measured", the speed numbers are estimates.
Line numbers point at the **patched tree** (the file as it is after the patches are applied).

Your data, for scale (read-only look at `~/.local/share/osu/osu-local`): 1 profile, 1,744 cached score PPs,
503 played beatmaps, and **15,117 perfect-play PP entries in `perfect-pp-cache.json` (2.8 MB)**.

## Implementation status (2026-11-24, same day)

Your answers: re-filter at least every 3 s, rounding OK, keep JSON readable, disclaimer once per patch, keep the logo,
implement the rest. Changed patches: **0002, 0010, 0012**. 0005 is unchanged (see #12). Backups of every patch from before:
`work/old-patches/before-optimisation/`. Tests (not part of the patches): `work/opt-tests/`.

| # | Status |
|---|---|
| 1 | Done (0010). The session is kept while ruleset, mods and the "calculate" toggle stay the same. A beatmap whose file changed (edited) is looked up again. |
| 2 | Done (0010). New file `osu-local/perfect-pp-cache-v2.json`, nested and rounded (PP 0.01, stars 0.001), one readable line per beatmap. The old `perfect-pp-cache.json` is converted on first use and **deleted once the new file is written**. **Also fixed a bug found while doing this:** `PerfectPerformanceCache.For()` compared the game storage with its own `osu-local` sub-storage, which never matched, so every filter change re-read and re-parsed the whole cache file. |
| 3 | Done (0010). At most one re-filter every 3 s while calculating, plus one 250 ms after the queue empties. |
| 4 | Done (0002). Mods are worked out once per distinct mods JSON (+ legacy flag) per scan, with one ruleset instance. Exclusions use hash sets. Count-all-as-ranked skips the mod check. Verified identical to the old per-score rule in 4 settings (`TestScenePatchsuStatistics`). |
| 5 | Done (0002). A sorted list with binary insert/remove gives the same output. The test checks that the history ends at the total. |
| 6 | Done differently (0002): nothing runs at play start any more. After the score is saved, one pass works out PP with and without that score (`CalculatePerformanceChangeAsync`). Fails and retries cost nothing, and nothing runs during gameplay. |
| 7 | Done (0002). `LocalProfileManager.PerformanceRevision` goes up on score changes (realm subscription) and on store changes that touch PP inputs (options, exclusions, ranking options, linked or synced lists). The profile page reuses other profiles' PP while it stays the same. 0012 calls `InvalidatePerformance()`. |
| 8 | Skipped (your call). |
| 9 | Done (0002, which owns that loop, not 0001). The thread waits on an event that's set when the profile or online-lookups setting changes, with a 1 s fallback. |
| 10 | Done (0012). |
| 11 | Done (0002, 0012). Stream serialisation, still indented. The PP cache now goes through the 500 ms delayed save and is written outside the store lock. |
| 12 | **Not done.** `patches.meta.json` (new, from another session) says 0005 needs only 0001, and the "Minimal Vanilla" preset builds it without 0002, so 0005 can't use 0002's PP cache. Reverted to the original 0005. |
| 13 | Done (0002). One `MD5Hash == $0 OR …` query. |
| small | Disclaimer kept once per patch (0002: `LocalAutoSwitchCondition.cs`, 0012: `LocalNoFailPerformance.cs`). Tag filter concat skipped when empty. Lock-free tag lookups (copy-on-write). Difficulty-calculator version is static. The Ritsu `IncompatibleMods` caching was **skipped**: about 20 upstream mods use the same pattern, and a static cache would be wrong for `OsuModBeatmapVariant` subclasses. |

Checks: the full stack, 0003-only, 0004-only, each patch on its base and the "Minimal Vanilla" preset all apply. The full stack, 0001+0010 and
Minimal Vanilla compile with 0 warnings. Tests: 42 unit tests, 27 game-level tests (new + the p2/p6/p10/p12 ones) and
15 ruleset tests, all passing. The installed Patchsu was **not** rebuilt.

---

## TL;DR: ranked by value

| # | Patch | What | Gain | Risk | Effort |
|---|-------|------|------|------|--------|
| 1 | 0010 | Perfect-play PP session is thrown away on **every** filter change (each search keystroke). Pending calculations restart and every beatmap is looked up in the cache again | Big while sorting/grouping/filtering by PP | Low | S |
| 2 | 0010 | `perfect-pp-cache.json` repeats the ruleset, version and mods JSON in every key and stores doubles at full precision. **2.8 MB → 1.23 MB (measured on your file)**. The full string is rebuilt every 5 s while calculating | −56 % disk, about −60 % memory, faster load and save | Low (needs a one-time migration, not a version bump) | M |
| 3 | 0010 | While calculating, the whole carousel is re-filtered, re-sorted and re-grouped **every ~1 s** until the queue is empty (that can take minutes) | Less CPU and fewer carousel hitches during a long calculation | Low | S |
| 4 | 0002/0012 | Profile statistics scan: `score.Mods` is built 2–3× per score. Each build creates **one Ruleset per mod through reflection**, plus the mod instances | The scan behind every PP total gets several times faster | Low | M |
| 5 | 0002 | PP history graph is O(n² log n): every improving score re-sorts every best value so far | Linear-ish. Small now, but grows quadratically with the number of maps | None (same output) | S |
| 6 | 0002 | A full PP scan runs **at every play start** (`BeginStatisticsUpdate`) and again after the score is saved | One full scan per play saved. No background CPU while the map loads | Low | S–M |
| 7 | 0002 | With the profile page open, **any** store change (rename, reorder, section drag, auto-switch edit, recorded play) recomputes the PP of every other profile from scratch | Matters with several profiles (death profiles too) | Low | M |
| 8 | 0003 | `Textures/Ritsu/logo.png` is 256×256 (37.6 KB) but drawn at 16 px | 0003 gets about 41 KB smaller (−12 %). GPU texture 256 KB → 16 KB | None | XS |
| 9 | 0001 | Offline API thread wakes every **50 ms** forever while a local profile is active | 20 wakeups/s → about 0 | Low | S |
| 10 | 0012 | `LocalNoFailPerformance.Apply` builds `score.Mods` on every PP calculation, before checking for NF | Cheaper PP calculations everywhere while the setting is on | None | XS |
| 11 | 0002/0006 | Store writes: `JsonConvert.SerializeObject(..., Indented)` builds the whole file as a string first. `performance-cache.json` is written **synchronously, inside the lock**, after each stats run that calculated anything | Fewer large allocations. No lock held during disk IO | Low | S |
| 12 | 0005 | Leaderboard "sort by PP" recalculates local scores even when the profile store already has their PP cached | Faster PP sort on the local leaderboard | Low | S |
| 13 | 0002 | `LocalBeatmapEntry.Load`: one query per row (50 queries) on `BeatmapInfo.MD5Hash`, which isn't indexed | Faster "Most Played" preload | Low | S |

Small or cosmetic items are at the end.

---

## 1. Keep the perfect-play PP session across filter changes (0010)

`BeatmapCarousel.Filter()` calls `updatePerformanceSession(criteria)`
(`osu.Game/Screens/Select/BeatmapCarousel.cs:793`), and that **always** disposes the session and makes a new one
(`BeatmapCarousel.PerfectPerformance.cs`, `updatePerformanceSession`). So every search keystroke, star-range drag or
collection change with PP sort/group/filter on does all of this:

- cancels the background calculation queue (finished work is in the disk cache, but the queue order is lost);
- forgets the in-memory `results` dictionary, so every beatmap goes through `Lookup.getKey` again. That builds a string of
  about 130 characters (`"1|osu|20260706|<sha256>|[{\"acronym\":\"HD\"},...]"`) per beatmap, then hashes it under a lock;
- runs `Task.Run(flush)`, which serialises the whole cache if anything is dirty (see #2).

**Proposal:** remember the session's identity (`ruleset.ShortName`, the `modsKey` string and `CalculateMapPerformance`).
In `updatePerformanceSession`, when the new criteria have the same identity, give the same session to
`criteria.PerformanceSession` and return. Only replace it when the ruleset, mods or "calculate" toggle change, or when no
PP feature is in use any more. `SuspendPerformanceCalculations` and `ResumePerformanceCalculations` stay as they are.

Features don't change: values, sorting, grouping and the progress text are the same, only cheaper.

## 2. Smaller, faster perfect-play PP cache file (0010)

`PerfectPerformanceCache` stores `Dictionary<string, double[]>` with the full key per beatmap:

```json
"1|osu|20260706|ddf3…a820|[{\"acronym\":\"HD\"},{\"acronym\":\"NF\"},{\"acronym\":\"SO\"}]":[53.876516868234795,3.3538096765744076]
```

On your file all 15,117 entries share **one** prefix. Measured with your data:

| Layout | Size |
|---|---|
| current | 2,802,677 B |
| flat, values rounded (pp 0.01, stars 0.001) | 2,443,993 B |
| **nested `{ "<format>|<ruleset>|<diffVersion>|<mods>": { "<hash>": [pp, sr] } }` + rounding** | **1,234,717 B** |

In memory: `Dictionary<string setupKey, Dictionary<string hash, (double pp, double sr)>>`. A struct value instead of
`double[]` removes 15k small arrays too. `Lookup` would resolve its setup dictionary once in its constructor, so a per-beatmap lookup is
just `dict.TryGetValue(beatmap.Hash)`, with no string building.

Also:
- `Save()` builds the whole JSON string (2.8 MB, so a large-object-heap allocation) every 5 s while calculating, and on
  every session dispose. Serialise straight into the file stream with `JsonSerializer.Serialize(JsonTextWriter, …)`.
- **Migration, not a version bump:** bumping `format_version` would drop the cache and recalculate all 15k maps.
  Read the old flat format when the new file is missing (split the key on the first four `|`), then write the new format.

Rounding is safe for display and grouping (PP is shown with no decimals, stars with two). It only affects sort ties
between values that are less than 0.01 pp apart.

## 3. Re-filter less often while calculating (0010)

Every 5 finished beatmaps, `performanceChanged` schedules `refreshPerformance` 1 s later, and that calls `FilterAsync()`:
a full match, sort and group of every beatmap (thousands), then carousel layout on the update thread. A cold
calculation of a big library runs for minutes, so that's a full re-filter every second for minutes.

**Proposal (pick one, see questions):**
- adaptive delay: 1 s for the first ~10 s, then 3 s, then 5 s; or
- refresh at most every N s and once more when the queue is empty (already done: `changed(this)` after the loop).

The progress text can still update every 5 maps. It's only the re-filter that would be throttled.

## 4. Stop rebuilding mods per score in the statistics scan (0002, 0012)

`LocalProfileStatistics.calculateAsync` reads every score of the profile inside `realm.Run`
(`LocalProfileStatistics.cs:360-414`). Per score it does:

- `getNoPerformanceReason` → `score.Mods.Any(IsRelaxOrAutopilot)` (one build of `Mods`);
- `LocalPriorityPolicy.HasUnrankedMods` → `score.Mods.Where(...)` (a second build, **even when "count everything as
  ranked" is on**, where every mod is treated as ranked anyway);
- synced scores: `ModUtils.CalculateRateWithMods(score.Mods)` (a third).

`ScoreInfo.Mods` on a managed or detached score does not cache:
`APIMods.Select(m => m.ToMod(Ruleset.CreateInstance()))`. So **every mod of every score** costs a `Type.GetType`,
an `Activator.CreateInstance` of the ruleset, a mod lookup, `Mod.CreateInstance()`, and reflection to copy the settings.

This scan sits behind the profile page, every other profile's PP chip, the toolbar PP change (twice per play, see #6),
exclusions and ranking-option changes.

**Proposal:**
- Make one `Ruleset` instance per scan and pass it in.
- Work out per **distinct `ModsJson` string** once per scan (scores share only a handful of mod combinations):
  `Dictionary<string, ModFacts>`, where `ModFacts` holds `HasRelaxOrAutopilot`, `HasNoFail`, `HasUnranked(countAll, rxap, ignoreUnranked)` and `Rate`.
- RX/AP/NF/CL checks use acronyms from `APIMods` only, with no instances.
- `HasUnrankedMods`: return `false` right away when `countAllAsRanked` (same result as today).

Same results, a fraction of the allocations. `LocalNoFailPerformance.HasNoFail` already checks `APIMods` acronyms, which
is the right pattern.

## 5. PP history in O(n) per step (0002)

```csharp
history.Add((score.Date, WeightedTotal(bestSoFar.Values.OrderByDescending(v => v).ToArray())));
```
(`LocalProfileStatistics.cs:549`) re-sorts every best value for each score that improves a map. Keep a
`List<double>` sorted descending next to `bestSoFar`: binary-search out the old value, binary-insert the new one, then
compute the weighted sum. The output is exactly the same. With 500 maps it's milliseconds either way, but at 5k maps the current
code does about 10⁸ comparisons plus 5k array allocations on every profile page open.

## 6. Reuse the PP total across plays (0002)

`BeginStatisticsUpdate` (`LocalProfileManager.StatisticsUpdate.cs`) runs a full `CalculatePerformanceAsync` on a
background thread **as gameplay loads**, and `CompleteStatisticsUpdate` runs another after the save. Nearly always, the
"before" of play N+1 is the "after" of play N.

**Proposal:** keep a tiny memo `(profileId, ruleset, ppStamp) → UserStatistics`, where `ppStamp` changes whenever
something that affects PP changes (see #7). The "before" then comes from the memo and the scan doesn't run during map
load. The "after" still recalculates.

## 7. Only recalculate PP when PP can have changed (0002)

The profile page's cache key is `"{profile}|{mode}|{manager.Revision}|{scoresVersion}"`
(`LocalProfileOverlay.cs:807`). `Revision` goes up on **every** `Store.Changed`: rename, reorder, section-order drag,
avatar, auto-switch or mod-rule edits, death counters, and `RecordPlay` (play counts don't touch PP). Each bump
recalculates the shown profile's full statistics **and** `CalculatePerformanceAsync` for every other profile, one after another.

**Proposal:** a separate `PerformanceRevision` on the manager, bumped only by score import, delete or transfer, sync,
exclusions, options that affect PP, ranking options, linked owners, and the NF and pause-penalty toggles. Keep
`ppByProfile` in a dictionary keyed by `(profileId, mode, PerformanceRevision)`, so cosmetic changes reuse it. The full
statistics still follow the current key, because play counts and other numbers on the page do change.

You have 1 profile today, so this is cheap right now. It matters once death profiles or extra profiles exist.

(Also small: `refresh()` calls `manager.GetProfiles()` twice, at lines 790 and 802. That's two clones of every profile, and one is enough.)

## 8. Shrink the Ritsu logo (0003)

`osu.Game/Textures/Ritsu/logo.png` is 256×256 and 37,652 B, but `RitsuModPlaque` draws it at `Size = new Vector2(16)`.
Measured: 64×64 is 4,558 B, and 48×48 is 3,066 B. The patch stores binaries as base85 (about +25 %), so 0003 loses
about 41 KB of its 330 KB. 64 px still covers 4× UI scale.

## 9. Offline API thread busy-waits (0001)

`APIAccess.run()`, with a local profile and no online-account lookups:
```csharp
if (state.Value != APIState.Offline) state.Value = APIState.Offline;
Thread.Sleep(50);
continue;
```
That's 20 wakeups a second for the whole session. **Proposal:** a `ManualResetEventSlim` that's set when
`LocalProfilesUseOnlineAccount` or the offline user changes, with `Wait(1000)` instead of `Sleep(50)` so cancellation is
still noticed. Switching back to online reacts instantly, and idle cost drops to about zero. A simpler option is `Sleep(250)`.

## 10. Check acronyms before building mods in `LocalNoFailPerformance.Apply` (0012)

`PerformanceCalculator.Calculate/CalculateAsync` always calls `Apply(score)`. With the setting on it calls `score.Mods`
(a full build for realm or detached scores) just to look for NF. Use `if (!HasNoFail(score)) return score;` first.

## 11. JSON writes in the profile store (0002, 0006, 0012)

- `LocalProfileStore.write` / `LocalBeatmapTags.save` / `LocalNoFailPerformance.write` use
  `writer.Write(JsonConvert.SerializeObject(value, Formatting.Indented))`. Serialise into the stream instead
  (`JsonSerializer.Create().Serialize(new JsonTextWriter(writer), value)`). Optionally drop `Indented`: about 10 % smaller,
  but harder to read by hand (see questions).
- `SetCachedPerformance` writes `performance-cache.json` (107 KB) **synchronously while holding `sync`**, so every
  `TryGetCachedPerformance`, `GetProfiles` and the rest wait on disk IO. Mark it dirty and reuse the 500 ms debounced
  `save()`/`Flush()` path that `profiles.json` already uses. `Flush()` on exit then writes both.

## 12. Leaderboard PP sort can use the profile PP cache (0005)

`LeaderboardManager.fillPerformance` calculates PP for every local score without `PP`, through the difficulty cache.
For local scores, `LocalProfileStore.TryGetCachedPerformance(score.ID)` usually has the value already (the same number the
profile page shows, without the pause penalty). Try it first. The in-memory `calculatedPerformance` dictionary also never
shrinks, but that's harmless.

## 13. One query for "Most Played" rows (0002)

`LocalBeatmapEntry.Load` does `r.All<BeatmapInfo>().FirstOrDefault(b => b.MD5Hash == …)` once per row.
`MD5Hash` isn't indexed, so each one is a table scan. Build one `Filter("MD5Hash == $0 OR MD5Hash == $1 …", hashes)`
for the batch (50 at most) and map the results back by hash.

---

## Small / cosmetic

- **Disclaimer header**: the 6-line "Patched for fun…" block is in 32 new files, about 23 KB across the patches
  (27 in 0002). Kept, since it's intentional (see questions).
- **`LICENCE-Modosu.txt`** (36 KB in 0003): GPL text, required. Don't trim.
- `BeatmapCarouselFilterMatching`: with a tag filter active, `Metadata.UserTags.Concat(local).ToArray()` allocates per
  beatmap even with no local tags. Skip the concat when `Get()` returns an empty list.
- `LocalBeatmapTags.Get` takes a lock per beatmap during filtering. You could swap an immutable snapshot reference on writes instead. It's micro, so only worth doing alongside other work there.
- Ritsu mods' `IncompatibleMods => base.IncompatibleMods.Concat(...).ToArray()` allocates on every access (mod select
  calls this a lot). It's the same style as upstream. A `static readonly` array would fix it for the constant ones (not HR, which depends on a setting).
- `PerfectPerformanceCache.Lookup` gets its difficulty-calculator version through
  `CreateInstance().CreateDifficultyCalculator(new FlatWorkingBeatmap(new Beatmap()))` once per ruleset per lookup. That's fine,
  and could be static across lookups.

## Checked and fine as is

- Per-frame `Update()` hooks: profile-row edge fades, switcher height, `PauseCountCounter`, replay track compensation
  (only with mods toggled off), result particles, simulation boxes. All O(1).
- `LocalProfile.Clone()` already leaves out `Stats` and shares the synced/linked lists. `profiles.json` saves are already
  debounced and run on a background thread.
- Auto-switch only runs after a 2 s dwell and is skipped entirely with no rules.
- Gameplay hooks from 0003/0007a (decoy checks, hit policy, `Judgement.IsValidResult`, replay toggles) add nothing
  measurable per frame.
- Perfect-play PP sorting precomputes values once per sort run (no per-comparison lookups).

---

## Questions

1. **#3, re-filter cadence while calculating PP:** adaptive (1 s → 3 s → 5 s), a fixed 3 s, or keep 1 s and only make
   each re-filter cheaper?
2. **#2, rounding cached perfect-play values** (pp to 0.01, stars to 0.001): OK? It halves the file together with the nested
   layout. Without rounding the nested layout alone gives about 1.6 MB.
3. **#11, compact JSON** for `profiles.json` / `beatmap-tags.json` / performance caches: OK to drop indentation, or do you
   open them by hand and want them readable?
4. **Disclaimer header** in every new file (about 23 KB in total): keep it in every file, or have it only once per patch
   (for example in the first new file, or the patch's leading comment)?
5. **#8, logo size:** 64×64 (safe at 4× UI scale) or 48×48?
6. Should I implement these? If yes, all at once or a chosen subset? The low-risk, same-output group is #1, #4, #5, #8, #9 and #10.

---

## Line-by-line pass (2026-11-24, later)

Read every line of the parts the first pass only skimmed: 0002's profile page (`LocalProfileOverlay.cs`, 3,655 lines, and
`LocalProfileOverlay.Historical.cs`), all of 0008 (PP simulation) and all of 0003's code (the Ritsu / Modosu mods, utils
and hooks; the GPL licence text was skipped). Nothing has been changed yet. Line numbers are in the patched tree.

### High value

| # | Patch | What | Fix |
|---|---|---|---|
| L1 | 0008 | `PerformanceSimulationScreen.Update()` runs **every frame** `canResetToScore` → `sameModsAndSettings(simulatedMods, sourceScore.Mods)`. `sourceScore` is a detached score, so `sourceScore.Mods` is rebuilt every frame (a ruleset instance per mod via reflection, plus settings copied by reflection). `simulation.IsModified` also allocates a key union every frame. | Keep the source score's mods in a field once, and update the two buttons when the simulation or the mods change instead of polling. |
| L2 | 0008 (+0002, 0012) | `PerformanceSimulation` works on `template.DeepClone()`, and `DeepClone` drops the cached mods, so **every** `Score.Mods` read rebuilds them. `OsuPerformanceCalculator` reads `score.Mods` 16 times per calculation, and typing an accuracy or PP value runs about 14 calculations (binary search). That's hundreds of ruleset instantiations per keystroke. The same happens for detached scores in 0002's `calculatePerformance` (only for scores not cached yet) and 0012's No Fail loader. | One line after cloning or detaching: `score.Mods = score.Mods;` (the setter caches the array). Same output. |
| L3 | 0002 | "Most Played" rows use `UpdateableBeatmapBackgroundSprite`, which loads the beatmap's **full-size background** (`working.GetBackground()`) for an 80×60 thumbnail. That's up to 200 rows, each potentially a 1920×1080 decode plus about 8 MB of GPU texture (loaded 0.5 s after scrolling into view, dropped 10 s after leaving). | Use `working.GetPanelBackground()`, the downscaled, cached texture song select's panels use, in the same delayed-load wrapper. It looks the same at that size. |
| L4 | 0002 | Score rows get their right-click menu **built eagerly**: each row runs `LocalProfileScoreTransfer.CreateMenuItem` (→ `GetProfiles()`, a copy of every profile) and `CreateScoreMenuItem` (→ copies of the exclusion lists) plus about 8 menu items. With 200 rows, or a dying SD/PF profile's "show all", that's 200 profile-list copies on the update thread. | Build the menu on right-click (the row keeps a `Func<MenuItem[]>`). |

### Medium

| # | Patch | What | Fix |
|---|---|---|---|
| L5 | 0002 | The whole profile page (scroll, rows, header) is permanently inside `lastLookFade`, a `BufferedContainer` that only the SD/PF last look needs (the fade to grey). The page is drawn into a full-screen framebuffer and then that buffer is drawn again, every frame anything moves (scrolling, hover fades, counters), and it holds a full-screen framebuffer in VRAM. | Move the page into a buffered container only while a last look runs (or swap containers when `updateLastLookFade` starts), and keep it plain otherwise. |
| L6 | 0002 | "Show more" (Best / Recent scores and Most Played) **clears and rebuilds the whole list** each step: going 150 → 200 builds 200 rows again. | Remove the button, append only the new rows, then add the button again. |
| L7 | 0003 | Circle Generation: (a) a leftover debug `Logger.Log` for the first 3 circles runs on **every** application, including every star-rating / PP difficulty calculation, so it spams the log. (b) `beatmap.HitObjects.OfType<HitCircle>().FirstOrDefault()` sits inside the per-circle loop (thousands of iterations). | Delete the log and hoist the lookup. |
| L8 | 0003 (size) | `ModosuHitObjectGenerationUtils.cs` is a **verbatim copy** of osu!'s `OsuHitObjectGenerationUtils.cs` (only the namespace changed) plus the two slider-to-stream converters. About half of `ModosuHitObjectGenerationUtils.Reposition.cs` is copied as well. | Call osu!'s versions for the identical public methods and keep only the Ritsu variants (play-area extension, clamp-only, stream converters). That's about 250–300 lines (~12 KB of 0003) less, and later osu! fixes are picked up. |
| L9 | 0003 | Modosu's `CalculatePossibleMovementBounds` allocates a list and samples the path again on every call (per slider, twice when rotated). osu!'s version reads `slider.Path.CalculatedPath` with no allocation. | Use osu!'s logic (or reuse it as in L8). |

### Low

- **0003 RandomV2:**
  - `isStream()` (a timing-point binary search) is evaluated up to 4 times per object with the same arguments.
  - About 60 lines are commented-out dead code: an old `Update(Playfield)` and a debug block.
  - `getRelativeTargetAngle` / `…Stream` and `getRandomOffset` / `…Stream` are identical except for which sharpness setting they read.
- **0003 `RepositionHitObjectsClampOnly`:** `currentRot` and `origRot` are the same call, so the "limit rotation" branch rotates by 0. It does nothing, but it rewrites control points and measures the slider again. It can be deleted (or fixed, if it was meant to restore the original rotation).
- **0003 Decoy Circles:** every decoy's marker is a `BufferedContainer`, even with the default "Real Fake" style where nothing is greyed. It could be plain unless Grayscale is picked. Low impact, since decoys are at least 8 beats apart.
- **0003 `ExpoTooltips`:** it defines its own `CircularContainer` (osu.Framework already has the same class), and its offset arrays are properties that allocate on every read.
- **0003 `OsuModBeatmapVariant.IncompatibleMods`:** builds the list with reflection (`IsInstanceOfType`) on every read, which mod select does often. It could be cached per `GetType()`.
- **0003 + 0004 together:** both add `new DllResourceStore(typeof(OsuGameBase).Assembly)`, so every resource miss checks osu.Game's resources twice. This is harmless. Each patch must keep its own copy, since they also work alone.
- **0002 `TopSection.Update`:** with no countdown it still sets the alpha and calls `stopCountdownSounds()` every frame. It's cheap but could return early.

### Read and fine

- **0002:** the rest of the profile page (header, chips, avatar, name editing, flags, statistics box, graphs, tooltips) and the Historical charts.
- **0008:** `SimulationPanelMiddleContent`, which already debounces the accuracy circle. Also `EditableStatistic`, whose per-frame work is O(1) with no-op setters, and the footer / results / leaderboard / options hooks.
- **0003, other mods:** No Sliding, Spacing Adjust / Overtime, Metronome, One Eye, Miss Challenge, Phrase Mirror, Streamline, Swing, Echo, Reversed Sliders, the RandomV2 pattern fitting (bounded: 144 tries per phrase), the original-state store, the plaques, icons and the skin / auto-play hooks.

### Questions

1. Implement L1–L9 and the low items as one batch (same as last time: tests, every combination checked, backups)?
2. **L8 (deduplicating the Modosu utils)** touches code ported from Ritsu / Modosu. Is diverging from Ritsu's copy OK, or do you want 0003 to stay close to Ritsu's source so future Ritsu updates port easily?
3. **L5:** when a SD/PF last look starts, the page would move into the grey-fade container. It looks the same, but there could be a one-frame change at that moment. Fine to try, with a check in a scratch app?

### Implementation status (line-by-line pass, 2026-11-24)

Changed: **0002, 0003, 0008**. Backups: `work/old-patches/before-line-review/`, plus `work/old-patches/0003-mods-from-Ritsu-before-dedupe.patch`.

| # | Status |
|---|---|
| L1 | Done. The score's mods are built once; "does the simulation still use the score's mods" is worked out when the mods change; the per-frame statistics comparison no longer allocates. |
| L2 | Done in 0008 (`PerformanceSimulation`) and 0002 (`calculatePerformance`, which also covers 0012's No Fail path). |
| L3 | Done differently: `GetPanelBackground()` turned out to be a 6.4:1 crop (wrong for a 4:3 thumbnail) and isn't downscaled. A small shared store (`BeatmapThumbnails`) shrinks each background to 120 px high once. |
| L4 | Done (menus are built on right-click). |
| L5 | Done. The page moves into the grey-fade buffer only during a last look. **Also needed:** the page container now keeps the buffer's "children never masked away" behaviour (`UnmaskedContainer`). Without it, sections below the visible area never loaded, which `TestSceneProfileSwitchScroll` caught. Not checked by eye on a real screen; the death-profile and scroll tests pass. |
| L6 | Done for the Scores lists and Most Played (rows are appended). |
| L7 | Done. |
| L8 | Done for identical code only. `ModosuHitObjectGenerationUtils.cs` now holds just the stream converters (and a private `rotateVector`), and the mods call osu!'s `OsuHitObjectGenerationUtils` for the rest. Reposition uses osu!'s `GeneratePositionInfos` / `ObjectPositionInfo`. 0003: 329.8 KB → 313.0 KB. |
| L9 | Done differently: Modosu's own bounds maths is kept (osu!'s gives float-different results, which would change beatmap variants) but reuses one buffer instead of allocating per slider. |
| Low | Done: RandomV2 (`isStream` once per object, about 60 dead lines removed, the duplicate jump/stream helpers merged), Decoy marker framebuffer only for Grayscale, `OsuModBeatmapVariant.IncompatibleMods` cached per type, ExpoTooltips arrays static, TopSection early return. |
| Low, kept | `RepositionHitObjectsClampOnly`'s rotate-by-0 branch was left in: osu!'s rotation is float trig, so removing it would move such sliders by about 1e-6 and change variant output. The two `DllResourceStore`s with 0003+0004 are needed for either patch to work alone. |

**Found and fixed along the way (not performance):**
- 0003's `CircularContainer` replaced the framework class for osu!'s **Bubbles** mod (same namespace). Renamed to `ExpoCircleContainer`, so Bubbles is vanilla again.
- 0003's Miss Challenge used 0002's `LocalProfileManager`, so the "Custom mods" preset (0003 without 0002) **did not compile**, even before this pass. It now reads the API's local user (with 0002, that is the active local profile, so the behaviour is the same).

**Verification:**
- Beatmap-variant output is **byte-identical** before and after: `RitsuVariantOutputTest`, 3 beatmaps × 19 Ritsu / Modosu mod setups, 2.9 MB dumps compared with `cmp`.
- Full stack, "Minimal Vanilla" and "Custom mods" build with 0 warnings. Every combination applies (0003-only, 0004-only, each patch on its base).
- Tests: 78 game tests and 31 ruleset tests pass. `TestSceneDeathProfiles`' grey-level helper was updated, because a normal page is no longer inside the buffer (old copy kept in `before-line-review/`).

## Draw calls on the profile page (2026-11-24, later)

Reported: thousands of draw calls (3.28k on a large screen) on the profile page with expanded score lists. Measured on the real GPU
(2560×1440, scratch app `work/opt-tests/drawtest`, 120 scores), old build → new build:

| Step | Old | New | Page's own share (minus ~230 for the main menu behind) |
|---|---|---|---|
| Compact page | 452 | 317 | ~206 → ~86 |
| Inside the expanded list | 686 | 335 | ~440 → ~104 |
| Bottom of the list | 405 | 344 | |

Nothing redraws every frame (`FBORedraw` stays at the main menu's 1). Screenshots before and after match: rank badges, grade
badges, hover highlight, faded rows.

What changed (0002, `LocalProfileOverlay*.cs`):
- **Score rows, Most Played rows and the statistics block** are each drawn once into a cached framebuffer (`CachedDrawBuffer`).
  It redraws only when something inside changes (it watches the descendants' `InvalidationID`), and its children keep updating, so
  hover fades and relative dates still work.
- **Rank and grade badges** (moving triangles) are drawn live above the pictures (`LiveLayer`, with placeholders in the layout), so
  they don't force redraws.
- **Off-screen culling is on again.** `UnmaskedContainer` from the line-by-line pass is gone. Instead the containers that receive new
  sections or rows keep updating while out of view (`UpdatingContainer`, `UpdatingFillFlowContainer`, and the panels). Rows skip
  their insides while out of view, except for one first update.
- Not cached: the top part with the picture, name and chips. Its countdown is drawn through a proxy above the grey fade, which
  doesn't work inside a cached buffer.

Tests: `opt-tests/TestSceneProfileDrawCost.cs` (off-screen rows aren't drawn) and the 80-test game suite pass. The full stack with
0013 builds with 0 warnings. Old 0002: `work/old-patches/0002-before-draw-calls.patch`.

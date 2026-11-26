Changelog go in patch numerical order, so 0001 will be first and 1000 will be last unless its a breaking change.

## 2026-11-26 · Patchsu-CLI uses the patches on GitHub's main branch when they're newer

- **Patchsu-CLI** On start, it now checks `Patchsu/patches` on the repo's `main` branch (`internal/release/branch.go`). If the last
  commit that changed it is newer than the built-in patches (`bundled-at`) and the files differ, those patches are used
  (header: `GitHub patches (main@<sha>)`) and downloaded into `<data>/releases/main-<sha12>/`. Before, only GitHub *patch
  releases* (`manifest.json` + `patches.zip`) were checked, and none are published, so it always said "built-in patches".
  Requests: the commit Atom feed (no API quota); the contents API only for a new, newer commit; if the API refuses (60/h
  unauthenticated limit), it downloads the raw files and compares them itself. Patch releases are still checked when the branch
  can't be read. `--release main` picks the branch patches (cached copy when offline); with no built-in patches and no patch
  release, the branch is used. `--no-update` still keeps the built-in ones.
- Checked: `go test ./internal/release` (new `TestUpdateBranch` with a fake GitHub), live run against GitHub (14 patches, identical
  to `Patchsu/patches`, second start from cache). `TestDisableRemovesDependents` (manifest) and `TestPickerFlow` (tui) already
  fail on HEAD without this change.

## 2026-11-26 · 0002 Total Play Time format, fetched osu! plays count

- **0002** Total Play Time is formatted like the website / lazer's `TotalPlayTime`: `5h 12m`, with whole days in front from
  24 hours on (`3d 4h 12m`; it used to stay in hours, e.g. `76h 12m`). New tooltip: the total in hours (rounded), or in
  minutes while that's under 2 hours (`80 minutes`, `2 hours`, `29,621 hours`). Counting is unchanged.
- **0002** Fix: plays from "Fetch top 200 plays" that kept no replay counted no play, hits or play time (only the ones whose
  replay was downloaded did, as synced plays). New `LocalProfile.BanchoScores` (profiles.json) lists them; statistics count
  them like synced plays (one play each, the map's length / rate as time, which is what osu!web counts for a pass). When a
  replay replaces such a play, the play leaves the list (the replay counts as synced). `MovePlay` treats them like synced
  plays. Plays fetched by older builds are marked once on startup (`LocalProfileManager.migrateBanchoScores`, profiles.json
  `Version` 1 → 2): local profiles' scores with no replay file and no online ID. Not separated from other plays in the UI.
  Known limit: a replay-less fetched play transferred to another profile by an older build is marked there too, so it counts twice.
- Checked: 0002 applies on lazer + 0001 (tree identical to the work clone), full stack applies in glob order, Debug build
  0 warnings. Tests: 79 + 8 Patchsu 0002 tests in the 0001+0002 clone, 251 + 50 Patchsu tests on the full stack, all pass.
  New tests: `p2-tests/PatchsuPlayTimeFormatTest.cs`, `TestSceneBanchoTopPlays` (fetched plays counted, `TestOlderFetchedPlaysCountedOnce`).

## 2026-11-26 · 0002 new setting defaults

- **0002** Defaults changed (`LocalProfileOptions`, `LocalProfileManager` bindables, `LocalPriorityPolicy.LocalMapsGivePerformance`):
  "Count LOCAL maps towards PP" **off** (was on), "Gray out scores that do not go towards PP…" (`ShowNonCountingScores`) **on**
  (was off), "Create profiles with mod rules" **on** (was off). Options are saved as a whole object, so existing
  `PatchsuSettings.json` values are kept; only setups without saved options get the new defaults.
- **0004** Context lines refreshed for the two `ShowNonCountingScores` defaults (no code change).
- Checked: full stack applies (glob order), Release build 0 warnings, p2/p4/opt tests 85/85 pass.

## 2026-11-26 · 0003b renamed to Search adjustments, `playdate` search keyword

- **0003b** File renamed `0003b-Scoreswithmod-Search.patch` → **`0003b-Search-Adjustments.patch`**, display name
  "Search adjustments" (`patches.meta.json`: file, name and description; id stays `0003b`, still only needs 0001, still in
  Vanilla+). Patchsu-CLI selections are by id, so nothing to migrate.
- **0003b** New keyword **`playdate`** (also `scoredate`, `datescore`): only beatmaps with a local play of yours set a given
  time ago. `playdate<=180d` (at most 180 days ago), `playdate>4w` (more than 4 weeks ago), `<`, `<=`, `>`, `>=`, `=`
  (within the smallest unit given: `=3d` is 3 up to 4 days ago) and `!=`. Units `y`, `M`/`mo`, `w`, `d`, `h`, `m`, `s`,
  combinable (`1y6M`); a plain number is days. Several conditions and `scorewithmod=` must all match **the same play**.
  New file `Filter/ScoreDateFilter.cs`.
- **0003b** Code: `BeatmapCarousel.ScoreWithMods.cs` → `BeatmapCarousel.LocalScoreFilter.cs`,
  `GetBeatmapsWithScoreWithMods` → `GetBeatmapsWithMatchingScore` (also the `BeatmapCarouselFilterMatching` property), new
  `FilterCriteria.ScoreDate` and `HasLocalScoreFilter`. With `playdate`, the cached result is reused for at most a minute.
  `scorewithmod` behaves as before.
- Checked: 0003b applies on plain lazer + 0001 and in the full stack (glob order); tests 312/312 pass
  (`ScoreWithModFilterTest`, new `ScoreDateFilterTest`, `TestSceneSongSelectScoreWithMod` with new date and date + mod
  steps, lazer's `FilterQueryParserTest` / `FilterMatchingTest`). Test copy of the patch: `p3b-tests/0003b-Search-Adjustments.patch`;
  fresh work clone `work/p3b` (0001 + 0003b; the old `work/p5b` clone is stale).

## 2026-11-26 · Every setting in PatchsuSettings.json, data folder renamed to patchsu-osu (breaking)

- **0001** New `LocalPriority/PatchsuSettings.cs`: one file, `<osu data>/patchsu-osu/PatchsuSettings.json`, holds every
  patch's settings, one section per patch, only values that differ from the default (indented, readable).
  `PatchsuSettings.Current.GetBindable(section, key, default, oldGameIniKey)` / `BindWith(…)` give shared bindables (saved
  250 ms after a change and when the game closes); `Get` / `Set` keep whole objects. It's loaded in `OsuGameBase.SetHost`
  before game.ini (two small hunks there: `Load` and a `Flush` in `Dispose`). On the first start it renames the data
  subfolder `osu-local/` to **`patchsu-osu/`** (only if `patchsu-osu/` doesn't exist yet) and keeps game.ini's old lines,
  so settings that used to live there are taken over once. A broken file is kept as `.broken` and defaults are used.
- **0002** `LocalProfileStore.DIRECTORY` is now `PatchsuSettings.DIRECTORY`. Patch 2's options and "Online lookups" are the
  `LocalProfiles` section (`Options`, `UseOnlineAccount`); an older `profiles.json` has them taken over once and then
  removed from it. 0004's pause penalty is part of those options, so it moves with them (0004 itself is unchanged).
- **0003** Reversed sorts (song select, leaderboard) are the `Interface` section; no more `OsuSetting` entries / game.ini lines.
- **0007** "PP step", "PP calc" and the PP slider cap are the `PPAwareCarousel` section (`PerfectPerformanceSettings.SECTION`);
  no more `OsuSetting` entries. Its PP cache folder is `PatchsuSettings.DIRECTORY`.
- **0008** Each combination is a value in the `ModCompatibility` section (`ModCompatibilityOverride.Setting` is now the
  value's name, `GetSetting()` returns its bindable); no more `OsuSetting` entry.
- **0009** The `NoFail` section replaces `patch12.json`; an existing `patch12.json` is taken over once and deleted.
- Old values: game.ini values are taken over on the first start and osu! drops those lines from game.ini on its next save;
  `profiles.json` options and `patch12.json` are taken over and removed. Downgrading to an older build afterwards starts
  those settings from their defaults and recreates an empty `osu-local/`.
- Not moved: 1000's per-server login file (`patchsu-<server>.ini` next to framework.ini) holds a username and token, not
  settings, and stays apart on purpose. The `OSU_LOCAL_DISABLE` env var keeps its name.
- Checked: every patch applies on its own `needs`, the full stack in manifest order and in glob order (same tree);
  Release builds with 0 warnings for the full stack, Vanilla+ and 0001+0002+0004+0008+0009+0500+0501+0502 (no 0005).
  Tests: 83 non-visual + 50 osu! ruleset + 255 visual Patchsu tests pass (one visual test skipped).
  New tests: `p1-tests/PatchsuSettingsTest.cs`, `p2-tests/LocalProfileSettingsMigrationTest.cs`,
  `p9-tests/NoFailSettingsMigrationTest.cs`; the 0007 / 0008 / optimisation tests use `PatchsuSettings` now. Also fixed
  `TestSceneModCompatibilitySettings` still expecting `PatchNumber` 11 from before the renumbering.
- Docs: README, GUIDE, CONTRIBUTING, build script messages, Agents/AGENTS (new rule: settings only through
  `PatchsuSettings`), Agents/CONTRIBUTING_AGENTS and Agents/PATCHES_AGENTS say `patchsu-osu/` and `PatchsuSettings.json`.

## 2026-11-26 · Patch renumbering (breaking)

Every patch got a new number and file name. Numbering is now: **0001–0499** Patchsu's own patches, **0500–0999** mods
ported from other clients, **1000+** custom server patches. **Everything below this section uses the old numbers.**

| New | File | Old |
|---|---|---|
| 0001 | `0001-Offline-Fix.patch` | 0001 `0001-patchsu-online-restrictions.patch` |
| 0002 | `0002-Local-Profiles.patch` | 0002 `0002-patchsu-local-profiles.patch` |
| 0003 | `0003-Interface-Adjustments.patch` | 0005 `0005-interface-adjustments.patch` |
| 0003b | `0003b-Scoreswithmod-Search.patch` | 0005b `0005b-scorewithmod-search.patch` |
| 0004 | `0004-Pause-PP-Penalty.patch` | 0006 `0006-pause-pp-penalty.patch` |
| 0005 | `0005-Replay-Toggles.patch` | 0007 `0007-replay-mod-toggles.patch` (0007a before 2026-11-25) |
| 0006 | `0006-PP-Simulation.patch` | 0008 `0008-pp-simulation.patch` |
| 0007 | `0007-PP-Aware-Carousel.patch` | 0010 `0010-perfect-play-pp-sorting.patch` |
| 0008 | `0008-Mod-Compatability.patch` | 0011 `0011-mod-compatibility.patch` |
| 0009 | `0009-No-Fail-Disable.patch` | 0012 `0012-no-fail-pp.patch` |
| 0010 | `0010-Collection-Folders.patch` | 0013 `0013-collection-folders.patch` |
| 0500 | `0500-Ritsu-Mods.patch` | 0003 `0003-mods-from-Ritsu.patch` (+ old 0007b) |
| 0501 | `0501-Torii-Mods.patch` | 0004 `0004-mods-from-Torii.patch` (+ old 0007c) |
| 0502 | `0502-McOsu-Mods.patch` | 0009 `0009-mcosu-mods.patch` |
| 1000 | `1000-Mamestagram-Server.patch` (local only, gitignored) | 0101 `0101-mamestagram-server.patch` |

What changed with it:
- **Apply order.** Glob / manifest order is now 0001 … 0010, then 0500 … 0502, then 1000: the Ritsu, Torii and McOsu mods
  apply *after* the other patches instead of between them. Checked: every patch applies cleanly in the new order and the
  full stack gives the same source tree as the old order (apart from the renamed comments).
- **Patch contents.** Only comments, patch headers and `PatchNumber` changed; no code behaviour. Settings → Patchsu
  subsections are now "Patch 4. Pause PP penalty", "Patch 7. PP aware beatmap carousel", "Patch 8. Mod compatability" and
  "Patch 9. Ignore No Fail" (were 6, 10, 11, 12), so the subsections stay in the same order. Headers that listed ranges
  ("also applies on top of 0001–0008") now say "together with every other patch".
- **Kept on purpose:** the `PATCHSU_REPLAY_MOD_TOGGLES` define keeps its name. (0009's `patch12.json` was kept by the
  rename itself; the settings change below then moved it into `PatchsuSettings.json`.) Mentions of the old split patches 0007b / 0007c are marked "old numbering".
- **`patches.meta.json`:** new ids, files and `needs`, entries in the new order, Vanilla+ is now
  `0003, 0003b, 0005, 0006, 0007, 0010`. Display names follow the file names: "Offline fix", "Scoreswithmod search",
  "Replay toggles", "PP aware carousel", "Mod compatability", "No Fail disable", "Ritsu mods", "Torii mods".
- **Patchsu-CLI:** saved selections (`Patchsu-CLI.json`) and installed builds (`patchsu-build.json`) now carry
  `"idScheme": 2`; files without it are moved to the new ids when read (`manifest.MigrateIDs`, old 0007b / 0007c are
  dropped). Tests use the new ids; the bundled patch copy was regenerated. An *older* CLI binary reading the new manifest
  would map a remembered selection to the wrong patches; bump `minCli` / `Version` together if that matters for the release.
- **Test folders:** `Patchsu/work/pN-tests` renamed with the patches (p5→p3, p5b→p3b, p6→p4, p7→p5, p8→p6, p9→p502,
  p10→p7, p11→p8, p12→p9, p13→p10, p101→p1000). The test copy of the search patch is now `p3b-tests/0003b-Scoreswithmod-Search.patch`.
- **`.gitignore`:** the server patches pattern is now `/Patchsu/patches/1[0-9][0-9][0-9]-*.patch` (was `01*.patch`).
- **Docs:** README, PATCHES, GUIDE, CONTRIBUTING, LICENSE, screenshots/README, Patchsu-CLI/README, Agents/AGENTS,
  Agents/PATCHES_AGENTS (sections and table in the new order, numbering note added), Agents/CONTRIBUTING_AGENTS and
  the optimisation review notes use the new numbers. `Patchsu-CLI/DRAFT.md` is a historical draft and keeps the old
  numbers with a note on top. Older changelog entries keep the old numbers, since that's what was released then.
- Backup of everything before the rename: `Patchsu/work/old-patches/pre-renumber-20261126/` (local only).

### New patches
- **0005b** Search by play mods: `scorewithmod=HD`, `='Wind Up'` (exact name), `=HD,DT` (one play with both), `!=DT`, `=NM`.
  Reads the scores osu! already keeps; doesn't need 0002. In every preset with 0005. `scoreswithmod=` / `playwithmod=` work too.
- **0009** McOsu mods: **Mafham** (AR −∞, the whole map on screen at once, drawn into cached frame buffers) and
  **Click Control** (McOsu's Jigsaw 1: every unnecessary click is a miss). Both get a "from McOsu" plaque in mod select.

### Patch changes
- **0002** Speedrun profiles ("⋮" next to "+", named "SP Profile N"): set a PP or estimated-rank goal (`1000`, `#10000`;
  the other half is estimated as you type). A timer counts while the profile is active and stops, blue, with a "DONE!"
  notification when the goal is reached. "Reset SD/PF/SP profile count" resets their numbering too.
- **0002** "Fetch top 200 plays of <profile> from osu!" (Settings → Patch 2): adds that osu! player's best plays (current mode),
  downloads missing beatmaps after asking (one swipeable progress notification), then their replays where available.
  Skips plays already there. Needs "Online lookups on local profiles".
- **0002** Estimated global rank no longer stops at #120: it keeps improving above ~18,300pp, down to #1.
- **0002** SD/PF: countdown is 30 s (setting brings back 1 minute), shows seconds only, and only the last 3 s make a sound.
  On the last look, Best Performance scores fade away one by one (lowest PP first, #1 last) and rows below glide up.
- **0002** Clicking a failed / quit play in Recent Plays opens its beatmap in song select.
- **0002** Fix: replayless plays on the song select leaderboard now also offer "Transfer score", PP exclusion and Delete.
- **0003 / 0004 / 0009** Added mods now come after osu!'s own mods in every mod select column; RandomV2 sits right under Random.
- **0003 / 0004 / 0007** 0007a is now just **0007**; the 0007b / 0007c replay toggles moved into 0003 (Ritsu mods) and 0004
  (Torii mods) and switch on by themselves when 0007 is applied. 0003 / 0004 still build without 0007.
- **0003 / 0009** Fix: "from Modosu / Ritsu / McOsu" plaque text could get stuck in the wrong selected colour.
- **0003 / 0004** Mod plaques are capitalised: "from Modosu", "from Ritsu", "from Torii".
- **0005** New "Random" carousel sort (like F2 over and over; sets shuffled as wholes). Reshuffles when chosen again or on reopening song select.
- **0005** Leaderboard star rating moved next to the "Max Combo" caption so it lines up on every row.
- **0007** Bloom can be switched off in replays (the cursor eases back to normal size).
- **0009** Mafham: drawn invisibly in a 3 s lead-in (1 s on retry), no white glare, flicker, lag or missing pictures, pictures
  on whole pixels, semi-transparent skins look the same as live, no kiai flashing, no stutter on slider-heavy maps.
  It's a Fun mod, has no reading in star rating / PP, and has its replay toggle built in. "Far objects brightness"
  (10–100%) dims the next objects and shows as a rolling exponent on the −∞ AR (−∞^.5 at 50%).
- **0010** "PP calc" sits right under "Show converts" at the same width and shrinks with its "PP max" range when held.
- **0012** Fix: with NF score reduction removed, results panels and the in-game leaderboard order by the shown score.
- **0013** Collections menu: clicking a collection with sub-collections toggles the beatmap again. Manage collections:
  "+" and delete buttons no longer overlap. Collection filter keeps sub-folders on top. New "Merge into parent".
  Left / right arrows move past folder rows; shift+arrows step into sub-folders.

### Patchsu-CLI and build scripts
- Presets: **Vanilla+** (old Minimal Vanilla + 0013) and **Local play** (every patch).
- Patches are built into the binary; on start it checks GitHub for newer patches (`--no-update` skips, `--release TAG` /
  `--local DIR` pick a source).
- Data folder and install location can be chosen (`f` / `i` on the check screen), remembered for `build` / `uninstall`.
- The game, its installs and Patchsu-CLI.exe get the Patchsu logo as their icon.

## First public release

The repository goes public with these patches (see [PATCHES.md](PATCHES.md) for full description):

- **0001** Online restrictions: read-only allow-list for osu! requests, no submission / multiplayer / chat / update checks.
- **0002** Local profiles: profile page, local/unranked/unranked mod combinations/relax and autopilot/regular PP submission, estimated rank, auto-switch rules, local tags, Sudden Death / Perfect Death profiles that delete themselves whenever you trigger SD/PF, replay import / export, PP exclusions, failed plays in Recent Plays.
- **0003** Mods from Ritsu, **0004** Mods from Torii.
- **0005** Interface adjustments (reversed sorts, PP leaderboard sort, holding shift moves text ticker by 1).
- **0006** Pause PP penalty with a pause counter skin component.
- **0007a / 0007b / 0007c** Replay mod toggles for vanilla, Ritsu and Torii mods.
- **0008** PP simulation on a results-style screen; "Fix choke" turns misses into 100s (left click) or 300s (right click) and leaves slider ends alone.
- **0010** Perfect-play PP sorting and Farmability (from Ritsu). PP bar coloured like the star rating bar, capped at "1,000+" (setting to uncap).
- **0011** Unrestrict different mods compatibility.
- **0012** Ignore No Fail in PP/Scoring.
- **0013** Collection folders.
- **Patchsu-CLI**: cross-platform TUI that picks patches / presets and builds them.

Also optimisation pass over every patch file to try and match clean osu!lazer performance as much as possible, now mostly matches clean lazer (needs further testing). Added those findings as rules to [AGENTS.md](Agents/AGENTS.md)

## Before the repository

Patchsu started as two patches (online restrictions + local profiles) because I just wanted something I can play locally because of my personal reasons and problems, as well as my hyperfixated ADHD ass got access to Claude Pro, so I decided to actually implement something instead of waiting and flaming the game for my mistakes.

### Day by day

Only the bigger things are listed. The number next to each day is how many times a patch got saved as a new version that day
(backups and saved versions counted), just for fun.

#### 24 November · 78 changes
- Four new patches: perfect-play PP sorting and "Farmability" (0010), Hard Rock + Mirror together (0011), ignore No Fail in PP (0012)
  and **collection folders** (0013).
- Auto-switch rules understand things like `>5 <7` or `180, 190`.
- Failed and quit plays show up in Recent Plays with an F, like on the website.
- PP simulation got the results screen's rolling numbers, its own mod selection, a button on the results screen and a "Fix choke" button.
- Settings → Patchsu got one "Patch N." section per patch. The tab now comes with 0001, so patches with settings (0010, 0011) work without local profiles.
- Sudden Death / Perfect Death's last look now fades the page to grey.
- Collection folders also work with the collection filter, not only with "Group by Collections".
- No Fail can also stop halving your score (0012), and leaderboard scores show their star rating with mods (0005).
- **Patchsu-CLI**: a small program that picks patches and builds everything for you.
- Big optimisation passes: the profile page draws far fewer things per frame, PP sorting keeps a smaller cache, and a Ritsu mod class
  that accidentally broke osu!'s own Bubbles mod got fixed.

#### 23 November · 9 changes
- Pick your own profile page colour in settings.
- Grow / Deflate can be turned off in replays too, plus replay toggles for the Torii mods (0007c).
- Sudden Death / Perfect Death profiles name themselves ("SD Profile 3").
- **PP simulation (0008)**: type in any accuracy, combo or misses and see what the play would be worth.
- The pause counter got its own section in the skin editor.

#### 22 November · 30 changes
- Everything got renamed to **Patchsu**.
- New PP options: ignore "Unranked" mod status and count any play as ranked. The toolbar shows how your PP and rank change,
  just like after an online score.
- **Sudden Death and Perfect Death profiles**: miss once (or hit anything less than perfect) and the profile deletes itself after a
  last look at its page, with a red countdown.
- Each profile page gets its own colour.
- Replay mod toggles (0007a, 0007b): turn Hidden, Flashlight, DT and friends off while watching a replay, without changing the replay.
- "Rebuild play statistics from saved scores", and the profile page no longer jumps around when switching profiles.


#### 21 November · 9 changes
- The profile page got redone to look like the profile on the osu! website.
- Mods from Ritsu (0003) and Torii (0004) became their own optional patches, usable alone or together.
- First interface tweaks (0005): reverse any sort with right-click, sort the leaderboard by PP, right-click the star rating bar,
  middle-click "Now playing" to mute.
- Pausing can cost you PP if you want it to (0006).

#### 20 November · 23 changes
- Local profiles grew up fast: a profile page, a right-click menu on scores (delete, export), profiles that only allow certain mods,
  dragging profiles into any order, moving a score to another profile.
- Play history and "Most Played" on the profile page.
- Local beatmap tags, and profiles that switch automatically depending on the beatmap you pick.
- The online patch was rewritten to be as small as possible: it only cuts online access and does nothing else.

#### 19 November · 4 changes
- Started with two patches: one that cuts osu!lazer off from the online stuff, and one that adds local profiles.

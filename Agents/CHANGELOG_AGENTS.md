Changelog go in patch numerical order, so 0001 will be first and 1000 will be last unless its a breaking change.

## 2026-11-28 · 0001 Settings footer shows the Patchsu version and the osu! tree it was built from

- **0001** `SettingsFooter`: "Patchsu V1.1" / "osu! 2026.1117.0+6 · 8cb6e6e68d", from `patchsu-build.json` next to the binary;
  clicking the second line opens the ppy/osu tree, RMB copies it. No file → vanilla footer.
- **Patchsu-CLI**: manifest `osuBase` (new, `patches.meta.json`), written into `patchsu-build.json` with `osuTree`
  (`osuRepo/tree/osuCommit`). Older releases without `osuBase` show only the commit.
- Tests: full stack and Vanilla+ build 0 warnings; checked on screen (scratch `OsuGame`). All work tests pass (383) except
  `TestSpacingSliderShowsPreview` (p500, failed before). `go test`: manifest
  `TestDisableRemovesDependents` (no "Local play" preset in meta) and tui `TestPickerFlow` fail before this change too.

## 2026-11-28 · 0001/0500/0502 Incompatible patch mods can be swapped again, plaques follow it

- **0001** `UserModSelectOverlay.ComputeNewModsFromSelection`: also drops selected mods whose `IncompatibleMods` list the clicked
  mod. Patch mods (0500/0501/0502) list vanilla mods one way, so e.g. Strict Tracking with No Sliding selected, RandomV2 with
  Streamline, or No Sliding with Reversed Sliders could not be picked at all (`OsuGameBase` stripped the clicked mod again).
- **0500** `RitsuModPlaque`, **0502** `McOsuModPlaque`: colour from `active.Value` instead of `NewValue`. The rejected selection
  reset `Active` inside the same change, so the plaque got a stale `true` and stayed in its selected colour.
- Tests: `TestOneWayIncompatibleSwap` added to p500-tests. Full stack and 0001 alone build 0 warnings; all work tests pass except
  `TestSpacingSliderShowsPreview` (p500, failed before this change too); p1000-tests not run (needs 1000).

## 2026-11-28 · 0002 Local profile page always opens at the top

- **0002** `LocalProfileOverlay.PopIn`: `scroll.ScrollToStart(false)` before `base.PopIn()`, so reopening the page starts at the top
  instead of where it was closed (the online profile does the same). Switching profiles while the page is open still keeps the scroll.
- Tests: `TestReopenStartsAtTop` added to p2-tests `TestSceneProfileSwitchScroll`. Full stack builds 0 warnings; p2-tests + opt-tests pass (105).

## 2026-11-28 · 0002 Best Performance placement numbers after "show more"

- **0002** `LocalProfileOverlay` `ScoresPanel.addRows`: once the best list is expanded past its first 5, every best row gets
  `ScoreRow.ShowPlacement(index + 1)`: a small number (size 12, `Content2`) fades in on the left and the row content's left padding
  goes 20 → 44 (400 ms OutQuint). Rows not loaded yet (page opening fully expanded) get it without animation in `LoadComplete`.
  The number sprite sits in the row's cached buffer (alpha 0 until shown), so no extra draw calls once settled.
- Follow-up: the number also slides 10 px right → left into place, and rows added by "show more" animate with the first 5 (they used
  to appear already shifted, since they aren't loaded yet when placed): `ShowPlacement(place, animate)`, animated from `LoadComplete`.
  A page opening fully expanded still shows them without animation.
- Tests: full stack builds 0 warnings; p2-tests + opt profile/statistics tests pass (42). Checked on screen (scratch `OsuGame`).

## 2026-11-28 · 0003 PP sort and leaderboard tooltip follow PP option changes

- **0003** `LeaderboardManager.FetchWithCriteria`: fetching the same criteria again clears `calculatedPerformance`, so the PP sort
  recalculates plays without saved PP after a PP option changes (0009 "Remove 'No Fail' from PP calculation" kept the PP from when
  it was on). Switching beatmaps keeps the cache. `LeaderboardScoreTooltip.SetContent` compares by reference: a re-fetched score
  has the same ID, so the tooltip kept the old PP until another score was hovered.
- Not fixed: while NF is ignored, the PP sort still uses a NF play's *saved* PP (with NF); 0003 doesn't know about 0009.
- Tests: `TestSceneLeaderboardPPSortRefresh` (p3-tests, fails without the fix). Full stack builds 0 warnings.

## 2026-11-28 · 0001/0002/0003 "Include ... in PP again" is green with the Target Practice icon

- **0001** new `MenuItemType.Positive` (`Green1` in `DrawableOsuMenuItem`), own block after the offline parts in the header.
- **0002** `LocalPerformanceExclusions`: already excluded difficulty / set / score gives a `Positive` item (was `Destructive`), so right-click
  menus show "Include" green. The F3 popover buttons ("all difficulties", "this difficulty") go `Green1` with `OsuIcon.ModTargetPractice`
  instead of the crossed-out `ModNoScope` (`exclusionColour`, picks by item type); `NO_SCOPE_ICON_SCALE` renamed `MOD_ICON_SCALE`.
- **0003** `MenuItemType.cs` / `DrawableOsuMenuItem.cs` hunks regenerated on top of `Positive` (no code change).
- Tests: full stack, Vanilla+, 0001+0002, 0001+0003 and 0001+1000 build 0 warnings; p2-tests, p3-tests + opt `PatchsuOptimisationTest` pass (136).

## 2026-11-28 · 0003 Submenus with nested items open left when their children wouldn't fit on the right

- **0003** `OsuMenu.updateSide`: a submenu the framework put on the right that has items with sub-items (`hasNestedItems`) now
  opens left (same `X` offset as before) when the room right of it is smaller than its own width (guess for the child width) and the
  left fits. The existing "keep going left" rule then sends its children left too, instead of the child flipping left over the
  root menu (e.g. a sub-collection under "Collections" in the beatmap menu). Neither side fits: framework placement as before.
- Tests: `TestNestedSubmenuWithoutRoomOnRightOpensLeft` (p3-tests `TestSceneSubmenuSide`, fails without the fix). Full stack, Vanilla+
  and 0001+0003 build 0 warnings; p3-tests, p10-tests, opt-tests and vanilla menu tests pass (opt `DumpVariants` needs the osu ruleset tests project).

## 2026-11-28 · 0006 Typed PP / accuracy removes misses when needed, mixes 100s and 50s

- **0006** `PerformanceSimulation.distribute`: when the typed value can't be reached with the current misses (even at all 300s),
  the fewest misses that reach it become 300s (binary search on the miss count), then the 300s are split as before. Misses stay
  when the value can be reached with them. The combo follows like after typing the miss count (`applyStep`: at cap or no combo
  breaks → cap, otherwise kept).
- The 300s are now lowered in steps of 50 points (a 100 costs 4, a 50 costs 5, `splitLoss` uses as few 50s as possible), so
  accuracy / PP fit about 4x finer than 100s alone; up to 3 50s show up before 100s run out. Header and remarks updated.
- Tests: p6 `PerformanceSimulationTest` adds `TestTargetRemovesFewestMisses` and `TestAccuracyMixesOksAndMehs`; `TestAccuracyTarget`
  allows up to 3 50s, `TestPerformanceTarget` expects an unreachable PP to remove the miss. Full stack and Vanilla+ build 0 warnings;
  p6 tests + opt-tests pass (37).

## 2026-11-28 · 0009 Results screen and leaderboard PP ignore No Fail again

- **0009** `PerformanceStatistic` (results) and `BeatmapLeaderboardScore.Tooltip.PerformanceStatisticRow` (leaderboard PP) showed the
  saved `ScoreInfo.PP`, which was calculated with NF, so the `PerformanceCalculator` hook never ran for them (profiles worked, they calculate).
  Both now skip the saved PP for NF plays while NF is ignored (`LocalNoFailPerformance.IgnoresSavedPerformance`) and calculate instead.
  Hunks have no leading context so they apply with or without 0004.
- Tests: `TestSceneNoFailSavedPerformance` (p9-tests). Full stack and 0001+0002+0009 build 0 warnings; all `*-tests` + opt-tests pass
  except p500 `TestSpacingSliderShowsPreview` (unrelated).

## 2026-11-28 · 0003 Music button mute / duck only resets when entering a beatmap

- **0003** `ToolbarMusicButton`: removed the toolbar-hide fallback release. The toolbar also hides on other screens (opening a score),
  which undid the mute / duck there. Now only a pushed `PlayerLoader` (gameplay, replays, spectating) releases them.
- Tests: `TestSceneToolbarMusicButtonClicks` toolbar-hide tests replaced by `TestToolbarHidingKeepsDuckAndMute`;
  `TestSceneMusicButtonMuteIntoGameplay` +`TestButtonDuckReleasedOnEnteringBeatmap`. Full stack and Vanilla+ build 0 warnings;
  p3-tests + opt-tests pass (opt `DumpVariants` needs the osu ruleset tests project).

## 2026-11-28 · 0003 Music button mute no longer triggers the "music muted" warning

- **0003** `ToolbarMusicButton` releases its mute / duck on `OsuGame.ScreenStack.ScreenPushed` when the new screen is a
  `PlayerLoader` (fires before the loader loads, so before `showMuteWarningIfNeeded`). Before, it waited for the toolbar to hide,
  which on the first entry only happens when `Player` is pushed, after the warning. Toolbar-hide release kept as a fallback.
  A 0 music volume set in settings still warns.
- Tests: `TestSceneMusicButtonMuteIntoGameplay` (p3-tests, Navigation; resets `MutedAudioNotificationShownOnce`, which
  `OsuGameTestScene` sets to true). Fails without the fix. Full stack, Vanilla+ and 0001+0003 build 0 warnings; all `*-tests`
  + opt-tests pass except p500 `TestSpacingSliderShowsPreview` (unrelated).

## 2026-11-28 · 0003 Tighter F3 Options menu, 0 / - / + hotkeys

- **0003** `FooterButtonOptions.Popover`: `OptionButton` 265×34 (was 50, corner radius 7), flow spacing 2, header padding 5 vertical,
  spacers 5. `OnKeyDown` maps `Number0` / `Minus` / `Plus` to options 10–12 (after 1–9).
- Tests: `TestSceneOptionsPopoverHotkeys` (p3-tests). Full stack builds 0 warnings; it, `TestSceneLookForSimilar`,
  `TestScenePerformanceSimulation` and song select options tests pass.

## 2026-11-28 · 0003 Look for similar icon, see-through menu scrollbar

- **0003** F3 Options "Look for similar" is light blue (like the right-click menu) and uses the Random V2 mod icon
  (`OsuIcon.LookForSimilar`, own copy `Textures/Icons/Patchsu/look-for-similar.png`, cropped square so it's not tiny; 0003 doesn't
  need 0500). Embedded through a "Patchsu icons" csproj `ItemGroup` and a `DllResourceStore` in `InitialiseFonts`.
- **0003** `OsuMenu` scrollbar at 0.5 alpha so it no longer hides the submenu arrow.
- Tests: `TestSceneLookForSimilar.TestOptionsPopoverIconAndColour`. Full stack, Vanilla+ and 0001+0003 build 0 warnings; all
  `*-tests` + opt-tests pass except p500 `TestSpacingSliderShowsPreview` (0500 RandomV2 description, unrelated).

## 2026-11-28 · 0003 Toolbar music button: middle-click mutes music volume, right-click ducks

- **0003** `ToolbarMusicButton`: middle-click now sets `audio.VolumeTrack` to 0 and back to the value before (not saved across
  restarts) instead of adding a 0 volume adjustment to `audio.Tracks`. The icon shows `FontAwesome.Solid.VolumeMute` whenever the
  music volume is 0 (also when set to 0 in settings). Right-click toggles `MusicController.Duck` (same parameters as
  `ManageCollectionsDialog`); the icon / volume bar dim (0.35) moved from muting to ducking. Duck is released on dispose and when
  the parent `Toolbar` hides (entering gameplay), so it doesn't carry into a beatmap. A middle-click mute is undone there too
  (only if the button muted it and the volume wasn't changed since). Mute and duck are exclusive: muting releases the duck,
  ducking while the music volume is 0 restores it first (otherwise `SetIcon` re-showed the icon at full alpha while still ducked).
- **0003** New setting `Interface.SwapMusicButtonClicks` (Patch 3 subsection, "Swap middle and right click on the music button").
- Tests: `TestSceneToolbarMusicButtonClicks` (p3-tests, uses a real `Toolbar`). Full stack and Vanilla+ build 0 warnings.

## 2026-11-28 · 0010 Move collections by dragging in Manage collections

- **0010** The right mouse drag (hold 500 ms) also moves: top quarter of a row (or half the gap above it) = next to that row
  (`MoveFolderInto(row path[..^1])`), bottom quarter = inside it (`MoveFolderInto(row path)`), middle = merge as before. A 3 px rounded
  yellow line (above the drag preview) shows where it lands, indented by `FOLDER_INDENT` (25, now also used for the row indent) for
  "inside". No line / border when it would land inside itself or where it already is. Sub-collections move along.
- Tests: `TestSceneManageCollectionFolders` +2 (bottom edge moves inside with sub-collections and beatmaps, top edge moves to top level,
  no line on its own parent).

## 2026-11-28 · 0500 Scores and replays with old mod acronyms load again

- **0500** `ConfiguredModExtensions.ToMod`: acronyms 0500 mods used before they were renamed resolve to the new ones
  (DCY → DY, HST → SL, HFR → OE, RDV2 → V2, RSL → RS, SOT → SP). Old scores in realm and old `.osr` imports get the real mod
  (with its settings) instead of `UnknownMod`, so their replays stay in sync.
- Tests: full stack builds 0 warnings. No test run.

## 2026-11-28 · 0500 RandomV2 spacing previews

- **0500** Jump spacing and Stream spacing sliders show the rhythm-row preview (with the current value) instead of a bare
  number, updating while dragging.
- **0500** Exponential jumps / streams previews say "Only works when … spacing is above 1.0x" while it is 1.0x or lower
  (the formula returns the distance unchanged there, so toggling it showed no change).
- **0500** Fixed: moving the cursor between pattern checkboxes passed over 4px gaps that showed the "Patterns" text tooltip.
- **0500** Previews always animate when the hovered setting changes: circles both shapes share slide, extra ones grow out of
  the last circle or shrink into it, lines follow, the box resizes and row labels crossfade. Before, only previews with the
  same circle count animated and the rest snapped.
- Checked: full stack 0 warnings; osu ruleset tests 41/41 (4 new in `p500-tests`); screenshots of the new previews and of
  transitions mid-animation.

## 2026-11-27 · 0003 "Look for similar" in the beatmap right-click menu

- **0003** New `MenuItemType.External` (light blue, `OsuColour.BlueLight`). `SongSelect.CreateLookForSimilarMenuItem` is added under
  "Copy link" in `SongSelect` and `SoloSongSelect.GetForwardActions` when `OnlineID > 0`: opens bobert-web recommendations for the
  difficulty (`LinkWarnMode.NeverWarn`) and posts a `SimpleNotification`. The F3 Options popover shows it white: colouring it there
  would change the `addButton` line 0006 uses as context.
- Tests: `TestSceneLookForSimilar` (p3-tests).

## 2026-11-27 · 0003 Submenus keep opening to the left

- **0003** `OsuMenu.updateSide` (after `base.Update`): the framework (`Menu.Update`) picks each submenu's side alone (right unless only
  left fits), so a narrow submenu under a left-opened one jumped right over its parents. If the parent `OsuMenu` opened left
  (framework `x2` origin on its submenu container, or our own offset) and the framework put this one right, set
  `X = -(triggeringItem.DrawWidth + DrawWidth)` when the left fits. Triggering item = the parent's item in `MenuItemState.Selected`.
  Moving the menu's own `X` (not the parent's submenu container) keeps the framework's `positionLayout` valid.
- Tests: `TestSceneSubmenuSide` (p3-tests; vanilla `OsuMenu` fails `TestNarrowSubmenuKeepsGoingLeft`).

## 2026-11-27 · 0010 Merge collections in Manage collections, top level only collection dropdown

- **0010** Song select `CollectionDropdown` lists only top level collections (realm query `NOT Name CONTAINS '/'`); picking one
  already includes its sub-collections.
- **0010** Manage collections dialog: its rounded mask moved to an inner `Content` container with an `OsuContextMenuContainer` around it,
  so menus and submenus can draw past the dialog's edges (a menu swallows mouse down, so clicking one outside doesn't close the dialog). `DrawableCollectionListItem` is `IHasContextMenu`:
  "Merge into" (own tree builder, a folder with sub-collections opens a submenu that starts with its own collection; excludes the
  row itself and its sub-collections), "Rename" (`FocusName`), "New sub-collection" (same as "+").
- **0010** Right mouse down on a row is handled by the row: released before 500 ms it opens the menu (`TriggerEvent` of a right
  `MouseDownEvent` on the context menu container); held 500 ms starts a merge drag in `DrawableCollectionList` (right button has no
  framework drag, so the list follows the mouse in `Update` only while dragging). Preview = Box + 2 texts at 50 % alpha, the target
  row's name box gets the focused-textbox border. Release over a valid row merges, anywhere else cancels.
- **0010** `CollectionFolders.MergeInto(realm, folder, targetId)`: beatmaps of the folder and all its sub-collections go into the target
  (flattened), the folder's collections are removed. `MergeIntoParent` shares the helper.
- New files: `Collections/DrawableCollectionList.CollectionFolders.cs`, `Collections/DrawableCollectionListItem.CollectionFolders.cs`.
- Tests: `TestSceneManageCollectionFolders` (+5: menu on release, merge from menu flattens, menu item outside the dialog, hold-drag merges,
  release outside cancels),
  `TestSceneSongSelectCollectionFolders.TestCollectionDropdownShowsTopLevelOnly`.

## 2026-11-27 · 0002 "Exclude from PP" menu items are red

- **0002** Every exclude / include PP menu item (difficulty, beatmap set, score sub-menu and its items) uses
  `MenuItemType.Destructive` instead of `Standard`, like the other big-change items below it.
- **0002** Song select options menu: "Exclude all difficulties from PP" (red) above Delete under "For all difficulties"
  (new `FooterButtonOptions.Popover.cs` hunk, `CreateBeatmapSetMenuItem(..., allDifficulties: true)`); "Exclude this difficulty
  from PP" shows red under "For selected difficulty" through the forwarded actions. Both use the No Scope mod icon
  (`OsuIcon.ModNoScope`), also in the right-click menus.
- **0002** Options menu: "Exclude this difficulty from PP" sits right after "Edit" (the popover moves the
  `LocalPerformanceExclusions.BeatmapMenuItem` before the first spacer). Right-click menu: right after "Clear all local scores",
  before "Hide". No Scope icon drawn at x1.9 in the options menu (`addButton(..., iconScale)`), measured against the pencil icon
  (mod glyphs have more empty space).
- Checked: full stack and 0001+0002 alone 0 warnings, `PatchsuOptimisationTest` + Bancho tests 22/22 on the full stack.

## 2026-11-27 · 0002 Fetched osu! plays don't keep osu!'s PP

- **0002** Fix: plays fetched from osu! without a replay kept osu!'s own `pp` (`SoloScoreInfo.ToScoreInfo` copies it into
  `ScoreInfo.PP`). 0003's PP sort only calculates PP for scores without one, so these sorted by osu!'s (often older) PP
  next to locally calculated PP. The fetch now saves them with `PP = null`; `clearBanchoScoresPP` (`TakeMigration(3)`, on load)
  clears it once on already fetched plays (no online ID, no replay file, on a local profile). Plays with a downloaded replay
  were already fine (`.osr` has no PP).
- Checked: `TestSceneBanchoTopPlays` 16/16 (new `TestOlderFetchedPlaysLoseBanchoPP`, fetch test asserts PP is null); p2 / p3 /
  opt tests on the full stack 117/117 (+ `RitsuVariantOutputTest`, a manual dump tool for the osu! ruleset tests). Full stack 0 warnings.

## 2026-11-27 · 0003 PP sort: a cancelled calculation no longer leaves a play without PP

- **0003** Fix: `LeaderboardManager.fillPerformance` kept a play's PP as "none" (sorted last as -1, cached in
  `calculatedPerformance` for the session) when its calculation was cancelled by a newer fetch: lazer's
  `BeatmapDifficultyCache` returns null on cancellation instead of throwing. It now checks the token before keeping the
  result. Seen as an older fetched play (first in realm order, so usually the one being calculated) sorting below a play
  with less PP while its tooltip showed the right PP.
- Checked: new `p3-tests/TestSceneLeaderboardPPSortCancel` (a difficulty cache that waits until released or cancelled) fails
  without the fix and passes with it; the user's two plays copied from their realm: 227.6 vs 180.4 pp, sorted right.
  Full stack 0 warnings, p2 / p3 / opt tests 119/119.

## 2026-11-27 · 0500 / 0501 / 0502 New mod icons

- **0500** New icons for Decoy Circles, Miss Challenge and No Sliding; RandomV2 gets its own icon (`ritsu-random-v2.png`,
  `OsuIcon.ModRandomV2`) instead of stock Random.
- **0501** New Pitch Adjust icon; Hit Sounder gets its own icon (`torii-hit-sounder.png`, `OsuIcon.ModHitSounder`) instead of Metronome.
- **0502** Click Control and Mafham get mod icons (`mcosu-*.png`) instead of acronyms. 0502 embeds them itself (OsuIcon entries,
  osu.Game resource store, csproj item), placed so it applies with or without 0500/0501.
- All icons 120x84 white on transparent like the stock ones, from the maintainer's FigCraft file (`assets/`).
- Bigger after an in-game check: Decoy Circles, Click Control (x1.35), Miss Challenge, No Sliding, Mafham (x1.4, up to
  106 px wide) and Hit Sounder (x1.1). Pitch Adjust unchanged.
- Redrawn Decoy Circles, No Sliding and Mafham (taller eye) from the updated FigCraft file; RandomV2 x1.35, Mafham x1.5
  (112 px wide).
- Redrawn again: Mafham (infinity in the eye, FigCraft group "Mafham 2.2"), Decoy Circles (x1.45, 81 px tall), No Sliding, Hit Sounder.
- Redrawn Decoy Circles and No Sliding once more; No Sliding at x1.33 (0.95 of the earlier x1.4).
- Checked: each of 0500/0501/0502 on 0001 alone, every pair and all three apply and build with 0 warnings (without 0005 too);
  full stack 0 warnings; Mafham / Click Control / replay toggle / opt tests pass; a scratch test confirms every icon texture resolves.

## 2026-11-27 · 0500 RandomV2 settings tidied, hover previews

- **0500** RandomV2 settings in a fixed order: Jump spacing, Exponential jumps, Stream spacing, Exponential streams, Stream
  threshold (was Aim/Stream Divisor), Remove stacks, Angle style, Jump / Stream sharpness, Pattern mode, Patterns, Mirrored
  responses, Progressive spacing, Seed. Descriptions rewritten.
- **0500** "Angle style" dropdown (Random / Star / 45° / 90°) replaces the "Custom angle" toggle and the "Custom" angle.
  Patterns no longer need a toggle first. The saved settings (`CustomAngle`, `Angle`, masks) are unchanged, so old scores and
  replays give the same maps; presets with patterns selected but Custom angle off have the unused selection cleared once.
- **0500** Sharpness sliders now show whenever angles are random (they were hidden in the default mode while still used).
  "Pattern mode" (same list for all notes / per note type) copies the selection when switching. Mirrored responses and
  Progressive spacing only show once a pattern is selected.
- **0500** Hover previews with hit circles (Torus numbers): rhythm rows coloured stream / jump for the Exponential toggles and
  Stream threshold (live while dragging), angle path for Angle style and sharpness, each pattern's shape, mirrored pair,
  Remove stacks. Fixed: the Exponential tooltips ignored their setting's description.
- Checked: `RitsuVariantOutputTest` dump byte-identical before / after (0500 alone and full stack); new
  `p500-tests/TestSceneOsuModRandomV2Settings` 5/5, p502 + opt osu tests 37/37 on the full stack. 0500 alone and full stack
  0 warnings. Screenshots on black, light and coloured backgrounds.

## 2026-11-27 · 0002b P2P multiplayer lobby (LAN test)

- **0002b** New patch (needs 0002): Main menu → Play → "multi" opens "Multiplayer · P2P". Entering hosts a lobby on TCP 13381;
  "Create Code" shows an 8 symbol code (host LAN IPv4 + checksum, copied to the clipboard), "Connect via Code" joins one. Lobby members
  float around your own avatar like ranked play's queue; local profiles send their picture. Connecting only, no gameplay yet.
  No new dependencies.
- Checked: new `p2b-tests/P2PLobbyTest.cs` 7/7; on screen (scratch app, 2560x1440): three loopback guests appear (two with
  pictures, one without), one leaving fades out, no errors in the log.
- **0002b** Entry animation: header, notice and the two side columns slide and fade in one after another; the centre
  (your avatar and the lobby) scales in with OutElasticHalf. Leaving fades out. Content is centred vertically (same padding above
  and below). "Your code" is no longer ReadOnly, so it can be selected and copied; anything typed into it is put back.
- **0002b** Own background: osu!'s `Backgrounds/bg2` (`BackgroundScreenCustom`), blur sigma 5, colour Gray(0.6) (40% darker),
  instead of the menu's background with its triangles.

## 2026-11-27 · 0002 Speedrun takes the page's ruleset, cached profile pictures drawn 1:1

- **0002** "New 'Speedrun' profile" takes the ruleset shown on the profile page (`profileRuleset`, falling back to the game's),
  not the game's. Starting the run also selects that ruleset in the game (`mutableRuleset`, skipped while it's disabled), so
  the carousel shows the PP the goal counts. Plain / Sudden Death / Perfect Death profiles don't change the game ruleset.
- **0002** Fix: `CachedDrawBuffer` (statistics block, score rows) rounds its screen-space size to whole pixels
  (`ComputeScreenSpaceDrawQuad`). The frame buffer is `ceil(size)` pixels; drawn back into a fractional size with nearest
  sampling (pixel snapping) it dropped or doubled a pixel row depending on the sub-pixel scroll position. At 2560x1440 with
  UI scale 1, after the page springs back from an overscroll, text in the statistics block looked cut by 1px at the bottom.
- Checked: on screen at 2560x1440 (scratch app, 51 sub-pixel scroll offsets): before, the "PP history" text changed shape
  between offsets; after, it's the same at every offset. New `TestRunUsesPageRuleset` (taiko on the page, game on osu! →
  taiko goal, game switches to taiko); `TestSceneSpeedrunProfiles` 6/6. Full stack: Release 0 warnings, 273/273 + 50/50, 1000 applies on top.

## 2026-11-27 · 0003 skip button in breaks

- **0003** Solo plays and replays (`this is SoloPlayer || this is ReplayPlayer`, not multiplayer/playlists/spectating) show
  lazer's own `SkipOverlay` in breaks. `Player` binds `BreakTracker.CurrentPeriod`: on each break it adds one
  `SkipOverlay(target + MINIMUM_SKIP_TIME)` to `breakSkipContainer` (cleared when the break ends), target = period end − 3 s
  (the tracker's period, fade-out excluded). Only when ≥ 1 s is skippable. Skipping seeks there with samples disabled, like
  the intro skip. `SkipOverlay` itself is unchanged.
- **0003** Fix: in release builds the skip froze the playfield (music and timer kept going). `FrameStabilityContainer` ignores
  clock jumps over 500 ms after gameplay start as bad BASS times, except in tests and debug builds. The skip now turns
  `DrawableRuleset.FrameStablePlayback` off for one frame around the seek, as lazer's `SetGameplayStartTime` does.
  `TestSceneBreakSkip` also checks the frame-stable clock; with that guard forced on, it fails without the fix and passes with it.
- **0003** First Settings → Patchsu subsection for 0003: "Patch 3. Interface Improvements" → "Allow skipping breaks longer
  than 3 seconds" (`LocalInterfaceSettings`, `PatchsuSettings` "Interface"/"SkipBreaks", default on, read when the player loads).
- Checked: full stack, Vanilla+, 0001+0003 and full+1000 apply; full and Vanilla+ build 0 warnings. New
  `p3-tests/TestSceneBreakSkip.cs` 2/2; full stack (Release build): osu.Game.Tests 273/273, osu.Game.Rulesets.Osu.Tests 50/50 (p1000-tests left out).

## 2026-11-27 · 0002 Estimated global rank for all four rulesets

- **0002** New PP-to-rank curves for osu!, taiko, catch and mania (`LocalPriorityPolicy.Profiles.cs`, `osu_rank_curve` …
  `mania_rank_curve`, ~40 points each, highest PP first) from the osu! performance data dumps of 2026-11-05: every player of the
  top 10,000 plus 10,000 random players per ruleset (`rank_score` / `rank_score_index`). They replace the old osu!-only curve
  (Ritsu / Modosu / MOsu) and its exponential rule above the best sample. ln(rank) is interpolated linearly in PP (was rank),
  more PP than #1 is #1, below the lowest sample is the last rank. Median error against the real ranks: 0% at #1–9 (the
  samples), 1.5–3% at #10–99, about 1% from #100 down. The curves are a snapshot, so the top drifts as the leaderboard moves.
- **0002** API: `EstimateGlobalRank(string ruleset, double pp)` → `int?` and `EstimatePPForRank(string ruleset, int rank)` →
  `double?` (null for rulesets without a curve), `HasRankEstimate(string ruleset)`. The profile page, toolbar statistics update
  and Speedrun goals (`#rank` goals, the estimated other half) now work in every built-in ruleset; custom rulesets show "-" /
  "Not estimated for this ruleset." Profile page hint: "Estimate based on top 10000 and 10000 random accounts grabbed."
- Checked: 0002 applies on lazer + 0001 (tree identical to the work clone); full stack applies in glob order, 1000 applies on
  top; Release build 0 warnings. `TestSceneSpeedrunProfiles` updated (per-ruleset least-PP inverse at rank edges, more PP never
  gives a worse rank, custom ruleset gets no estimate): 5/5; other p2 tests 91/91. Full stack: 272/272 + 49/50 (Mafham
  `TestWholeMapShownAtOnce`, order-dependent, passes alone).

## 2026-11-27 · 0004 pauses in breaks, before the first object or after the last object are free

- **0004** Every recorded pause cost 7%, including pauses during the lead-in and in breaks. Now only pauses between the first hit
  object and the end of the last one, outside breaks, count. `LocalPausePenalty.CountPenalisedPauses(ScoreInfo)` checks the
  vanilla `ScoreInfo.Pauses` timestamps against the beatmap: before `CalculatePlayableBounds().start`, at/after its end, or inside a
  `BreakPeriod` with `HasEffect && Contains(time)` (the same break window as `BreakTracker`, fade-out excluded) is free. The beatmap
  comes from `LocalPausePenalty.GetBeatmap` (set by `LocalProfileManager` to `BeatmapManager.GetWorkingBeatmap(..).Beatmap`), only
  for scores with pauses, cached per score ID (+ beatmap hash, pause count). No beatmap / no objects: every pause counts.
  `ScoreInfo.Pauses` itself is never changed (it is submitted and sent to spectators). Applies to old scores too.
- **0004** Used by the results screen, leaderboard tooltips and profile statistics (both read the beatmap in `load()`, off the
  update thread). The pause counter skin component counts only pauses that cost PP (`gameplayState.Beatmap`, recalculated only
  when a pause is recorded).
- Checked: 0004 applies on lazer + 0001 + 0002; full stack applies in glob order, osu.Desktop builds 0 warnings. Tests: new
  `p4-tests/PausePenaltyFreePausesTest.cs` 7/7, p4-tests 11/11; full stack: osu.Game.Tests 269/269, osu.Game.Rulesets.Osu.Tests
  50/50 (p1000-tests left out; `p3-tests/TestSceneBreakSkip.cs` doesn't compile, `LocalInterfaceSettings` not found, unrelated).

## 2026-11-27 · 0002 "Fetch top plays": rate limit, own API key for other players, confirmation, 10-minute wait

- **0002** Every request Patchsu itself sends to osu! waits for one shared limiter (`BanchoRequestLimiter`, ≥ 1 s between
  request starts, process-wide, = osu!'s 60 requests/minute): player lookup, score pages, beatmap and replay downloads,
  API key tokens, through the account or the key alike. Lazer's own requests aren't limited.
- **0002** Where the plays come from (`LocalProfileManager.pickBanchoSource`): a profile named like the signed-in account
  (case-insensitive, space = underscore) goes through that account, with "Online lookups on local profiles" on while a local
  profile is active. Any other player needs the user's own osu! API key: group "osu! API key for other players' plays" in
  Settings → Patchsu → Patch 2 (client ID, masked client secret; `LocalProfiles.ApiClientId` / `ApiClientSecret` in
  PatchsuSettings.json). Player, score pages and replays then go through `api/v2` with a client-credentials token (`public`
  scope, cached, renewed on 401); a rejected key cancels the fetch with a message. Without a key, the fetch says so and sends
  nothing. Beatmaps are downloaded only with the signed-in account (without it: "N skipped (sign in to download their
  beatmaps)"). The fetch button is greyed out (and leads to online lookups) only while online lookups are off and no key is set.
  Files: `BanchoPlaySources.cs` (`IBanchoPlaySource`, `AccountBanchoPlaySource`, `ApiKeyBanchoPlaySource`), `BanchoRequestLimiter.cs`.
- **0002** Once the player is found, `BanchoConfirmDialog` asks "Fetch the top plays of X?" (says which account / key it uses and
  about the wait) before the score pages or anything else are requested; "No" or dismissing it ends the fetch. After a confirmed
  fetch ends (downloads included, also cancelled / failed), the next one waits 10 minutes (`BANCHO_FETCH_COOLDOWN_MINUTES`,
  all profiles, `LocalProfiles.TopPlaysFetchEndedAt` in PatchsuSettings.json, capped at 10 minutes if the clock goes back).
  During the wait the fetch button looks unclickable (`BanchoFetchCoolingDown`, cleared by itself when the wait ends) but
  clicking it still works: it sends nothing and posts the time left ("Try again in 7 minutes 5 seconds").
- Checked: 0002 applies on lazer + 0001; full stack applies in glob order, with and without 1000; Release builds 0 warnings.
  Tests: `TestSceneBanchoTopPlays` 15/15 (`TestOtherPlayerNeedsApiKey`, `TestOtherPlayerUsesApiKey`,
  `TestApiKeyWithoutSignInSkipsBeatmaps`, `TestRequestsAreRateLimited`, `TestDeclineSendsNothingElse`,
  `TestWaitStartsWhenDownloadsFinish`, `TestButtonGreyedOutWhileWaiting`; every fetch confirms via `confirmFetch()`, the setup
  resets the wait; settings items off screen don't animate, so colour checks scroll the button into view first), new
  `p2-tests/BanchoApiKeySourceTest.cs` 4/4 (mock HTTP server: token form, bearer + `x-api-version`, URLs, token renewal,
  rejected key, spacing), `TestSceneProfileColours` updated for the new settings group. Full stack: 262/262 + 49/50 Patchsu tests pass (the one is Mafham below);
  with 1000: p1000-tests + Bancho tests 50 passed, 1 skipped. `TestSceneOsuModMafham.TestWholeMapShownAtOnce` (0502) can fail
  when run in a batch (order-dependent, unrelated). Dialog and settings group checked on screen. Not checked against the real
  osu! API with a real key.

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
  Skips plays already there.
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

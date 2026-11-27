# Patches

Every patch is made against upstream osu!lazer commit [`8cb6e6e68d`](https://github.com/ppy/osu/commit/8cb6e6e68db6ad3387004c004ed2eb5f9728be37)
(the `Patchsu/osu-lazer` submodule) and is applied with `git apply` in file-name order. The files are in
[`Patchsu/patches/`](Patchsu/patches/). Each `.patch` starts with a plain-text description, which is the most detailed and up-to-date explanation of that patch (yet to be checked how readable it is for humans).

The machine-readable source of names, dependencies and presets is
[`Patchsu/patches/patches.meta.json`](Patchsu/patches/patches.meta.json). Patchsu-CLI and the release manifest are generated from it.

## Dependencies and presets

Numbering: **0001–0499** are Patchsu's own patches, **0500–0999** port mods from other clients (Ritsu, Torii, McOsu),
**1000+** connect to custom servers (hidden from the patch list, only with the "Custom server" preset). The old numbers
(before 2026-11-26) are listed in [CHANGELOG_AGENTS.md](CHANGELOG_AGENTS.md).

| Patch | Name | Needs | Notes |
|---|---|---|---|
| 0001 | Offline fix | – | **required**, every other patch builds on it |
| 0002 | Local profiles | 0001 | big, slower build |
| 0002b | P2P multiplayer | 0002 | very early test: LAN lobby by code, no gameplay yet |
| 0003 | Interface adjustments | 0001 | |
| 0003b | Search adjustments | 0001 | `scorewithmod=` (play mods) and `playdate=` (play date) search keywords |
| 0004 | Pause PP penalty | 0002 | Taken from Torii client |
| 0005 | Replay toggles | 0001 | Applies to plain osu!lazer as well |
| 0006 | PP simulation | 0001 | also applies to plain osu!lazer |
| 0007 | PP aware carousel | 0001 | |
| 0008 | Mod compatability | 0001 | |
| 0009 | No Fail disable | 0002 | |
| 0010 | Collection folders | 0001 | |
| 0500 | Ritsu mods | 0001 | osu! ruleset only; Modosu parts are GPL-3.0; replay toggles with 0005 |
| 0501 | Torii mods | 0001 | replay toggles with 0005 |
| 0502 | McOsu mods | 0001 | osu! ruleset only |

0500 and 0501 can be used together or alone, in any order.

In mod select, the mods added by 0500, 0501 and 0502 come after osu!'s own mods in each column (in patch order),
except RandomV2, which sits right under Random.

| Preset | Patches (0001 is always included) | Builds |
|---|---|---|
| Vanilla+ | 0003, 0003b, 0005, 0006, 0007, 0010 | Patchsu |
| Local play | every patch | Patchsu |

Patches that have settings put them in **Settings → Patchsu → "Patch N. …"**, one subsection per patch (the tab comes with 0001, so it works without 0002).
Every setting (UI state like reversed sorts too) is saved in **`<osu data>/patchsu-osu/PatchsuSettings.json`**, owned by 0001:
one section per patch, only values that differ from the default. Nothing goes into game.ini / `OsuSetting` or a patch's own file.

## 0001 · Offline fix

Only cuts online access, nothing else.

- Only read-only requests reach osu! servers (an allow-list inside `APIRequest`): logging in, beatmaps (lookup / search /
  download), beatmap tags, leaderboards, player profiles and replay downloads. Everything else fails before it touches the network.
- No score submission, no chat / notifications / spectator / multiplayer / presence connections, no crash reports, no update
  checks, no account creation.
- `PatchsuSettings` (`LocalPriority/PatchsuSettings.cs`): every patch's settings in `patchsu-osu/PatchsuSettings.json`.
  `PatchsuSettings.Current.GetBindable(section, key, default, oldGameIniKey)` / `BindWith(…)` for single values (shared
  bindables, written 250 ms after a change and on exit), `Get` / `Set` for whole objects. Loaded in `OsuGameBase.SetHost`
  before game.ini; on the first start it renames `osu-local/` to `patchsu-osu/` and remembers game.ini's lines so settings
  that used to be there are taken over (osu! drops the unknown lines from game.ini on its next save).
  Sections: `LocalProfiles` (0002 + 0004's pause penalty inside its options), `Interface` (0003's reversed sorts),
  `PPAwareCarousel` (0007), `ModCompatibility` (0008), `NoFail` (0009).
- News, changelog, wiki, rankings, chat and social buttons and multiplayer / playlists / daily challenge are removed from the menus.
- The game calls itself "Patchsu" (window title, app name). The data folder stays the official `osu` one.

Shared menu colour (not part of the offline purpose, lives here so patches don't conflict):

- `MenuItemType.Positive` → `OsuColour.Green1` in `DrawableOsuMenuItem`, for items that undo a destructive one (0002's
  "Include ... in PP again"). 0003's `External` is added after it, so 0003's hunks have `Positive` as context.

## 0002 · Local profiles

A fully local profile system with its own profile page, laid out like the website's.

- Right-click your name in the toolbar to switch between the online account and local profiles; left-click opens the profile page
  (stats, PP graph, best / recent scores, play history, most played, editing in place).
- Local profiles send nothing to osu! servers. With "online lookups" on, beatmap info / downloads, tags and leaderboards use the
  signed-in online account (scores are still never submitted).
- Per-profile play count, play time, hits, PP and an estimated global rank (all four rulesets). Total Play Time reads like the website's / lazer's
  ("5h 12m", "3d 4h 12m" from 24 hours on), with the total in hours (or minutes under 2 hours) on hover. Optional Relax / Autopilot PP, "Ignore 'Unranked' mod
  status", and "Count everything as ranked" (per profile or for all). PP / rank changes animate on the toolbar like lazer's.
- Plays saved under a profile's name (online account, guest, older profile, osu!stable imports) can be synced to it.
- Profiles with mod rules (block / only allow / only allow with exact settings / select on switch).
- Auto-switch rules per profile: tags, star rating, BPM, length, mapper, name, selected mods. Number fields take
  `6`, `<5`, `>=7`, `5-7`, `5+` and comma lists (`180, 190`).
- Local beatmap tags, searchable in song select.
- Sudden Death / Perfect Death profiles: the profile is deleted after a miss / a non-perfect hit, after a last look at its page
  (a 30-second countdown; Settings → Patchsu can extend it to 1 minute). During that last look its Best Performance scores fade
  away one by one: after a 5-second look, lowest PP first (shuffled among near neighbours), then the top 10 one a second over
  the last 10 seconds, #1 last. The countdown shows seconds only (30:00 / 60:00) and makes a sound only in its last 3 seconds.
- Best Performance "show more": every best score row (the first 5 and the newly added ones, in the same frame) fades in its place in
  the list (1, 2, 3 …) on the left, sliding 10 px right to left into place, while the rank badge and beatmap info slide 24 px right (`ScoreRow.ShowPlacement`, 400 ms OutQuint padding transform inside the row's cached buffer). Compact
  view (first 5) has no numbers; pages that open fully expanded (dying profile's last look) show them from the start. Recent Plays never.
- Failed / quit plays in Recent Plays open their beatmap in song select on click (there are no results or replay to open).
- Speedrun profiles ("⋮" → New 'Speedrun' profile, "SP Profile N", numbered and reset like SD/PF ones): set one goal in the
  window that opens, a PP value (`1000`) or an estimated global rank (`#10000`). The other half is estimated as you type and
  shown with the goal, e.g. `#3 • 29,240pp` or `#1,425,098 • 400pp` (osu!). The timer on the page counts the time the profile was active (saved every minute and on switching or
  quitting), stops when a play (or a deleted score, or anything else that changes PP) reaches the goal, turns blue, and a
  "DONE!" notification shows the final time. The goal counts the ruleset shown on the profile page when the run starts, and starting it switches the game (carousel) to that ruleset.
- Failed / quit plays show in Recent Plays (rank F).
- Replay import (`.osr` folder) into a profile, export of the top 50 or all replays for current profile, exclude a score / difficulty / set from PP (red No Scope menu items; "Include ... again" is green `MenuItemType.Positive`, Target Practice icon in the F3 popover), "Transfer score" between profiles (from the profile page or a leaderboard score's right-click, replay or not), "Rebuild play statistics from saved scores" if .json broke.
- "Fetch top 200 plays of <profile> from osu!" (Settings → Patchsu → Patch 2): the best plays (current mode) of the osu! player with the profile's name are added to the profile. Beatmaps you don't have are downloaded after asking, under one notification that counts how many are downloaded and how many are left, and each play is added once its beatmap is in. Replays osu! has for those plays are then downloaded too and replace the plays without one. The notification can be swiped away while it runs; the fetch carries on in the notification list. Plays already there are skipped. Fetched plays are saved without osu!'s PP (`PP = null`), so leaderboards / 0003's PP sort calculate it like any local score; plays fetched by older builds lose it once on load (`clearBanchoScoresPP`, migration 3). Each fetched play counts as one play (its hits, and the map's length adjusted for rate-changing mods as play time), with or without its replay; plays fetched by older builds are counted too (marked once on startup). Where the plays come from (`LocalProfileManager.pickBanchoSource`, `LocalPriority/BanchoPlaySources.cs`): if the profile's name is the signed-in online account (case-insensitive, space = underscore), the player, plays and replays go through that account, which needs "Online lookups on local profiles" on while a local profile is active. Any other name needs the user's **own osu! API key** ("osu! API key for other players' plays" group: client ID + client secret of their OAuth application, client-credentials grant, `public` scope, saved as `ApiClientId` / `ApiClientSecret` in the `LocalProfiles` section); the signed-in account is never used to ask for someone else's plays, and without a key the fetch says so and sends nothing. Beatmaps are only ever downloaded with the signed-in account; with only a key, those plays are skipped ("sign in to download their beatmaps"). The button is greyed out (and takes you to online lookups) only while online lookups are off and no key is set.
  Once the player is found (one lookup), a dialog (`BanchoConfirmDialog`) asks before anything else is sent; "No" or dismissing it ends the fetch. After a confirmed fetch ends (beatmap and replay downloads included, also when cancelled or failed), the next fetch waits 10 minutes (`BANCHO_FETCH_COOLDOWN_MINUTES`, for every profile; the end time is `TopPlaysFetchEndedAt` in the `LocalProfiles` section, so restarts keep it; never more than 10 minutes if the clock goes back). During the wait the button looks unclickable (`LocalProfileManager.BanchoFetchCoolingDown`, turns off by itself when the wait ends) but still works: clicking it sends nothing and posts a notification with the time left ("Try again in 7 minutes 5 seconds").
- Rate limit (`LocalPriority/BanchoRequestLimiter.cs`): every request Patchsu itself sends to osu! (player lookup, score pages, beatmap and replay downloads, API key tokens, through the account or the key) starts at least 1 s after the previous one, one limiter for the whole process (osu!'s API terms: max 60 requests/minute). Lazer's own requests aren't limited. A downloaded replay's import makes one cached `GetUserRequest` of its own (`ScoreImporter`), once per player.
- Setting defaults (Settings → Patchsu → Patch 2): "Count LOCAL maps towards PP" off, "Gray out scores that do not go towards PP…" on,
  "Create profiles with mod rules" on. Saved options keep their values; only setups without saved options get these.
- `PatchSettingsSubsection`: the base class other patches use for their "Patch N." settings subsection.
- Diagnostics: `OSU_LOCAL_DISABLE=tags,autoswitch,…` (or `patchsu-diagnose.sh`) turns features off at launch.

Data: `<osu data>/patchsu-osu/` (profiles, caches, tags; was `osu-local/`). Its settings are the `LocalProfiles` section of
`PatchsuSettings.json` (0001), no longer inside `profiles.json` (taken over once and removed from it). Nothing in the official osu! database schema changes.

## 0002b · P2P multiplayer

File `0002b-P2P-Multiplayer.patch`. Very early test of local P2P multiplayer: only connecting, LAN (or VPN) only, no gameplay yet.

- Main menu → Play → "multi" (0001 removed lazer's multiplayer buttons) opens `P2PScreen` ("Multiplayer · P2P").
- Entering it opens a lobby on this machine: `P2PLobby.Host` listens on TCP **13381**. "Create Code" shows the code and copies it;
  "Connect via Code" leaves your own lobby and joins (back to hosting if it fails or the host goes away).
- Code: 8 Crockford base32 symbols `XXXX-XXXX` = the host's LAN IPv4 (adapter with a default gateway, private ranges first) + a checksum
  symbol. Case, dashes and spaces are ignored; O→0, I/L→1.
- Protocol: 4 byte length (max 4 MB) + JSON. Guest → host `hello` (version 1, its `P2PPeer`); host → each guest `peers` (everyone else,
  host first) whenever someone joins or leaves. Peer ids come from the host (host 0).
- `P2PPeer`: username, user ID and, for local profiles, the profile picture file (≤ 512 KB, decoded only up to 1024 px).
  Others' local profile IDs are not looked up locally (they'd clash): no picture → online avatar for IDs > 1, else the guest one.
- Layout follows the maintainer's sketch: panels left, your avatar (`MatchmakingAvatar`, yellow ring) in the middle with
  `P2PFloatingAvatar`s around it (motion and appear sounds from ranked play's `QueueVisualisation.MovingAvatar`), help text right.
- Planned next (agreed in conversation): UPnP + IPv6 for internet play, host-run room logic from `TestMultiplayerClient`,
  spectator relay for the live leaderboard, local scores/results, beatmap download by online ID when logged in.
- Tests: `work/p2b-tests/P2PLobbyTest.cs` (codes, join/leave over loopback).

## 0003 · Interface adjustments

Small song select / toolbar tweaks that stay true to lazer's UI:

- PP sort on the beatmap leaderboard. PP calculated for it (plays without saved PP) is kept per play in `LeaderboardManager.calculatedPerformance`
  and cleared when the same criteria are fetched again (forced refresh, e.g. 0009's / 0002's PP options), so changed PP options apply.
  The leaderboard tooltip rebuilds for a re-fetched score (`SetContent` compares by reference, not score ID).
- Leaderboard scores show the play's star rating (with its mods) just left of the "Max Combo" caption, lined up on every
  row; it hides when the date doesn't fit beside it.
- Right-click a sort dropdown to reverse the sort (an arrow next to "Sort" shows it).
- "Random" in the beatmap Sort dropdown: shuffles the carousel the way F2 picks maps (whole sets, or single difficulties
  when they aren't grouped into sets). The order stays while you search; pick Random again (after another sort) or reopen
  song select for a new shuffle.
- Right-click / drag on the star rating bar (and 0007's PP bar) moves the further end to the cursor; double right-click an end resets it; middle-click resets both.
- Toolbar "Now playing" button: middle-click sets the music volume to 0 (muted icon) and back to what it was; right-click toggles
  ducking (muffled music, same `MusicController.Duck` as Manage collections), the icon and volume bar dim while ducked. The duck and a
  button mute (not a 0 set in settings) end as soon as a `PlayerLoader` is pushed (`OsuGame.ScreenStack.ScreenPushed`, before the
  loader's muted-music warning check; beatmaps and replays), not on other screens that hide the toolbar (e.g. opening a score). Muting ends the duck and ducking unmutes (only one at a
  time). Settings → Patchsu → Patch 3 can swap the two clicks.
- "Play" and "Clear all local scores" removed from the footer Options (F3) menu.
- F3 Options menu is tighter (buttons 34 high instead of 50, smaller header padding / spacers) and 0, - and + (=) select
  the 10th to 12th option after 1–9.
- Shift + arrow keys move the caret by one character while the search box has text.
- "Look for similar" (light blue, new `MenuItemType.External` → `OsuColour.BlueLight` in `DrawableOsuMenuItem`) under "Copy link" in a
  difficulty's right-click menu (both `GetForwardActions`, so also in the F3 Options menu, light blue there too with the Random V2
  icon: `OsuIcon.LookForSimilar`, `Textures/Icons/Patchsu/look-for-similar.png` cropped to 76×76, embedded via an
  `ItemGroup Label="Patchsu icons"` + a `DllResourceStore` in `InitialiseFonts`; the headless test runner skips that, so tests can't
  load the texture). The popover handles `External` with its own `continue` before the spacer check, so 0006's context line stays
  untouched. Only for maps with an online ID. Opens `https://bobert-web.pages.dev/recommendations?beatmap=<difficulty id>`
  (`LinkWarnMode.NeverWarn`) and posts a `SimpleNotification`. Helper `SongSelect.CreateLookForSimilarMenuItem`.
- Context menu scrollbar is half see-through (`OsuMenu.MenuScrollContainer`, scrollbar box alpha 0.5) so submenu arrows under it
  stay visible.
- Submenus keep going left: a submenu opened from one that went left (e.g. a sub-collection inside "Collections" on a right-side
  carousel) opens left too while it fits there, instead of jumping back over its parents. `OsuMenu.updateSide` (every `OsuMenu`)
  only moves the submenu's own `X`; the framework's placement of the parent's submenu container is left alone, so its
  `positionLayout` stays valid (no per-frame re-layout). A submenu with nested items that has less room on its right than its own
  width (guessed child width) opens left from the start when left fits, so its children follow it left instead of flipping over the root menu.
- Skip button in breaks (solo plays and replays only): skips to 3 seconds before the break ends, shown when at least 1 s can be
  skipped. Settings → Patchsu → "Patch 3. Interface Improvements" → "Allow skipping breaks longer than 3 seconds" (on by
  default, `PatchsuSettings` "Interface"/"SkipBreaks"). Files: `LocalPriority/LocalInterfaceSettings.cs`, `Screens/Play/Player.cs`.

## 0003b · Search adjustments

File `0003b-Search-Adjustments.patch` (was `0003b-Scoreswithmod-Search.patch`). Song select search keywords that show only
beatmaps you have a local play on (in the selected ruleset).

### Play mods: `scorewithmod=`

| Search | Shows beatmaps with |
|---|---|
| `scorewithmod=HD` | a play with HD (a one-word mod name works too: `scorewithmod=hidden`) |
| `scorewithmod='Wind Up'` | a play with that exact mod name, in quotes (`'…'` or `"…"`), ignoring case |
| `scorewithmod=HD,DT` | one play with all of them (also `HD+DT`, or `scorewithmod=HD scorewithmod=DT`) |
| `scorewithmod=HD scorewithmod!=DT` | a play with HD but without DT |
| `scorewithmod=NM` | a play without mods (Classic doesn't count as a mod) |

`scoreswithmod=` and `playwithmod=` are the same keyword and can be mixed (`playwithmod=HD scoreswithmod!=DT`).

Other mods on the play and mod settings (e.g. a custom DT rate) don't matter, and it combines with every other keyword and
search text. The scores come from osu!'s own database (the same ones "Group by: Rank achieved" uses), so nothing new is
stored and 0002 isn't needed; with 0002, an offline local profile only sees its own plays. A new or deleted play updates the
list by itself.

### Play date: `playdate=`

Filters by how long ago the play was set, counted back from now:

| Search | Shows beatmaps with |
|---|---|
| `playdate<=180d` | a play set at most 180 days ago |
| `playdate<180d` | a play set less than 180 days ago |
| `playdate>4w` | a play set more than 4 weeks ago |
| `playdate>=4w` | a play set at least 4 weeks ago |
| `playdate=3d` | a play set 3 days ago, counted in the smallest unit given: from 3 up to 4 days ago (`=1w` is 1–2 weeks ago, `=1w2d` 9–10 days ago) |
| `playdate!=3d` | a play not set 3 days ago (the opposite of `=`) |
| `playdate>1w playdate<4w` | a play set between 1 and 4 weeks ago (every condition must hold for the same play) |

- `scoredate=` and `datescore=` are the same keyword. `:` works like `=`, `<:` / `>:` like `<=` / `>=` (as in lazer's own keywords).
- Units: `y` years, `M` or `mo` months, `w` weeks, `d` days, `h` hours, `m` minutes, `s` seconds. They combine
  (`1y6M`, `2w3d`); a plain number is days. `M` (months) vs `m` (minutes) follows lazer's `lastplayed`. Years and months
  are calendar months; `w`, `d`, `h`, `m`, `s` take decimals (`1.5d`).
- A value that can't be read (`playdate<abc`) stays in the search text, like other keywords.
- With `scorewithmod=` too, **the same play** has to match both: `scorewithmod=HD playdate<30d` is an HD play from the last
  30 days, not "an HD play, and some play from the last 30 days".
- Unlike lazer's `lastplayed` (when the beatmap was last opened in gameplay, failed / quit plays included), this looks at the
  dates of saved scores.

### Code (for agents)

- `Filter/ScoreWithModFilter.cs` and `Filter/ScoreDateFilter.cs` each pull their keyword out of the query with their own
  regex in `FilterQueryParser.ApplyQueries` before lazer's generic `key op value` loop (one hunk, no `switch` cases added, so
  it stays clear of other patches touching that file).
- `FilterCriteria.ScoreWithMods` / `ScoreDate`, plus `HasLocalScoreFilter` (either set).
- `BeatmapCarousel.LocalScoreFilter.cs` (was `BeatmapCarousel.ScoreWithMods.cs`): `GetBeatmapsWithMatchingScore` scans the
  user's local scores once, checks date then mods per score, and returns the beatmap IDs; `BeatmapCarouselFilterMatching`
  gets it as `GetBeatmapsWithMatchingScore`. The result is cached by user + ruleset + both filters' `Key`; ages are not in
  the key (they move with the clock), so with `playdate` the cache is reused for at most a minute. Score inserts / deletes
  drop the cache and refilter.
- Tests (not in the patch): `Patchsu/work/p3b-tests/ScoreWithModFilterTest.cs`, `ScoreDateFilterTest.cs`,
  `TestSceneSongSelectScoreWithMod.cs` (song select with real scores, date and mod cases together). Work clone: `work/p3b`.

## 0004 · Pause PP penalty

Optional PP reduction for pausing: ×0.93 per pause (from Torii's pause penalty), on every map. Settings → Patchsu →
"Patch 4. Pause PP penalty". Shown on the results screen (with Torii's animation), leaderboard tooltips and the profile.
Toggling it animates the toolbar PP / rank. Also yoinkes a **pause counter** skin component, listed in its own skin editor section. Needs 0002

Pauses before the first hit object, inside a break (`BreakPeriod.HasEffect` + `Contains`, same as `BreakTracker`) or after the last hit object are free:
`LocalPausePenalty.CountPenalisedPauses` checks `ScoreInfo.Pauses` timestamps against the beatmap (`GetBeatmap`, set by `LocalProfileManager`;
cached per score ID + beatmap hash + pause count). Vanilla `ScoreInfo.Pauses` is never changed (it is submitted/spectated). The pause
counter shows only pauses that cost PP. Without the beatmap every pause counts.

## 0005 · Replay toggles

Switch mod effects off while watching a replay: checkboxes in the replay settings panel, or click the mod icons on screen
(a switched-off mod is greyed out). **The replay's result never changes**: frames, judgements, score, accuracy, combo, hit timing
statistics and spinner spins stay exactly as recorded; only what you see and hear changes.

- Speed: Half Time, Daycore, Double Time, Nightcore, Wind Up, Wind Down (playback returns to normal speed and pitch).
- Audio: Muted.
- Visual: Hidden, Traceable, Flashlight, Blinds, No Scope, Bloom, Grow, Deflate (Bloom's cursor goes back to normal size).

0500, 0501 and 0502 carry the code for their own mods; 0005 defines `PATCHSU_REPLAY_MOD_TOGGLES`. 

## 0006 · PP simulation

A results-screen panel where every number can be typed into, to see what a play would be worth (osu! ruleset).

- Open it from song select → Options (F3) → "Simulate PP" (starts at SS), a leaderboard score's right-click → "Simulate PP",
  or the calculator button on a results screen.
- Typing accuracy or PP turns 300s into 100s (then 50s) and keeps misses and combo (osu-tools style).
- "Fix choke": left click turns misses into 100s, right click into 300s. Hits every slider tick; slider ends stay.
  

## 0007 · PP aware carousel

Ported from Ritsu (MIT). New song select sorts **"PP (perfect play)"** and **"Farmability"** (1000 × PP ÷ seconds ÷ stars²),
and a **"PP perfect play"** grouping. **Right-click "Show converts"** to show the PP row (PP max range, PP step, PP calc on/off).
The PP max range is coloured like the star rating bar and goes up to "1,000+" (no upper limit); PP groups above 1,000 pp
become one "1,000+ pp" group. Settings → Patchsu → "Patch 7. PP aware beatmap carousel" → "Remove the 1,000 pp cap of the PP slider" makes it 0 – 3,000 (∞).
Values follow the selected ruleset and mods and are calculated in the background, cached in `patchsu-osu/perfect-pp-cache-v2.json`.
"PP step", "PP calc" and the cap are in the `PPAwareCarousel` section of `PatchsuSettings.json` (were in game.ini, taken over once).

## 0008 · Mod compatability

Settings → Patchsu → "Patch 8. Mod compatability". Off by default. Kept in the `ModCompatibility` section of `PatchsuSettings.json` (was game.ini).
- **Hard Rock + Mirror** (both flips apply). Added because I want to test how well can I read HR, but I don't want to flip the screen and Difficulty Adjust don't have presets for now.

## 0009 · No Fail disable

Settings → Patchsu → "Patch 9. Ignore No Fail": plays with No Fail give PP as if NF wasn't selected (results, leaderboards, HUD
PP counter, 0003's sort, 0006's simulation, local profiles). The play itself keeps NF. Settings in the `NoFail` section of `PatchsuSettings.json` (an old `patch12.json` is taken over once and deleted).
"Also remove 'No Fail' score reduction" shows NF plays' score x2, and every list orders by that shown score: leaderboards, the
results screen's score panels and the in-game leaderboard.
The results screen and leaderboard tooltip don't use a NF play's saved PP (it has NF in it) while this is on; they calculate it.

## 0010 · Collection folders

Sub-collections: a collection named `Parent/Child` sits inside `Parent`. Nothing else is stored, so vanilla osu! still sees normal
collections and removing the patch loses nothing.

- Group by Collections shows nested folder rows that open like beatmap sets (sub-folders first, then the sets).
- The Collection filter includes sub-collections: with Group by None they're folder rows at the top, followed by the
  collection's own sets. The list updates as soon as collections change.
- Left / right arrows from a folder row go on to the next / previous beatmap (into the folder, opening it if closed)
  instead of opening and closing it; shift+arrows step into an opened folder's sub-folders.
- A beatmap in both a collection and its sub-collection shows only in the outer folder.
- Right-click a folder: new sub-collection, rename, move to, merge into parent (a sub-folder's beatmaps go into the parent
  collection), manage, delete (with its sub-collections).
- Nested submenus in beatmap collection menus (a parent is still clickable), a tree with "+" and delete buttons in Manage collections.
- The song select Collection dropdown lists only top level collections.
- Manage collections: right-click a collection (menu opens on release) for "Merge into" (nested, a parent's submenu starts with
  itself), "Rename", "New sub-collection". Holding right-click 500 ms drags a see-through copy: middle of a row = merge into it
  (yellow border), top edge = move next to it, bottom edge = move inside it (yellow line, indented where it lands).
  Merging flattens the collection and its sub-collections into the target, without asking; moving keeps sub-collections.

## 0500 · Ritsu mods

Gameplay mods ported from [Ritsu](https://github.com/Azuuryy/ritsu) (by Azuuryy; Modosu by Azuuryy, MOsu by p-720). osu! ruleset only,
acronyms kept as in Ritsu.

| Type | Mods |
|---|---|
| Difficulty reduction | Streamline (HST) |
| Difficulty increase | Miss Challenge (MC), No Sliding (NL), One Eye (HFR), Decoy Circles (DCY), Reversed Sliders (RSL) |
| Conversion | Circle Generation (CG), RandomV2 (V2), Spacing Adjust (SA) / Spacing Overtime (SOT) |
| Fun | Metronome (ME) |
| System | Phrase Mirror (PMR), Echo (ECH), Swing (SWG) |

Patchsu additions: No Sliding length threshold, and scores saved with an unresolved mod (`XX??`)
resolve to the real mod, as does the old RandomV2 acronym `RDV2`. Own mod icons for Decoy Circles, Miss Challenge, No Sliding and RandomV2 (`ritsu-*.png`). Not ported: Directional Flicks, RandomV2 / Spacing Adjust replay panels.
RandomV2 settings are reordered (`OrderPosition`: spacing, angles, patterns, Seed last) and reworded. "Angle style"
(Random / Star / 45° / 90°, `AngleStyleSetting`) replaces the "Custom angle" toggle: it drives the still saved `CustomAngle` +
`Angle` (Random = `CustomAngle` off, or on with `AngleEnum.Custom` while patterns are selected; picking a pattern turns it on), so
old scores/replays keep their settings and maps (`RitsuVariantOutputTest` dump identical). Sharpness sliders show only for Random;
"Pattern mode" dropdown (one list / per note type, copies the selection when switching) replaces "Separate pattern selections";
Mirrored / Progressive show only with a pattern selected. Hover previews (`OsuModRandomV2.Previews.cs`, `CirclePreviewTooltip`,
built lazily on hover from the real spacing / angle formulas): rhythm rows for Jump / Stream spacing, the Exponential toggles
and Stream threshold (sliders update while dragging, `SpacingSliderSetting` + `PreviewSliderBar`), angle path, each pattern's
shape, mirrored pair, stacks. Exponential spacing returns the distance unchanged at 1.0x and below, so its preview says so there.
`CirclePreviewTooltip` always animates between contents (reuses circles by index, new ones grow in, extra ones shrink out and
expire, `PreviewLine`s track their two circles). Pattern checkboxes use padding instead of flow spacing, so moving between them never falls through to the "Patterns" tooltip.
The Modosu-derived files are **GPL-3.0** (`LICENCE-Modosu.txt` is added by the patch).

## 0501 · Torii mods

Mods from Torii (by ShikkesoraSIM): **Hit Sounder** (HS, osu!) and **Pitch Adjust** (PA, all rulesets). Both have their own
mod icons (`torii-*.png`, embedded in osu.Game).

## 0502 · McOsu mods

Two mods from McOsu, osu! ruleset only. Mod select shows a green "from McOsu" plaque on both. Both have their own mod icons
(`mcosu-*.png`, embedded in osu.Game the same way as 0500/0501, so 0502 still needs only 0001).

- **Mafham** (MH, Fun), "Maybe infinity IS finite after all?": approach rate is set to negative infinity. Every object except spinners is on screen
  at once, without approach circles, follow points or the "not hittable yet" dim. Gameplay doesn't change: hit windows,
  stacking and autoplay stay the same. Reading doesn't count: the star rating and PP leave out their reading part. Like McOsu, the whole map appears together 3 s before the first
  object, after a 3 s lead-in in which it's drawn invisibly. Objects that aren't close yet are drawn into cached frame buffers
  (like McOsu), so a 3-minute map with ~960 objects runs with the same draw calls and frame times as without the mod. The
  pictures look exactly like the objects, so nothing changes on screen when an object becomes playable (circles, slider ends
  and repeats don't flash in kiai sections, since pictures can't). A retry of the same map keeps the pictures, so the map appears
  1 s after a retry instead of 3 s. Song select and the mod select footer show AR as −∞. "Far objects brightness" in Customise
  darkens the objects that aren't within the normal approach rate yet, so the next ones stand out; below 100% it's shown as a
  small exponent on that −∞ (−∞^.5 at 50%, down to −∞^.1 at the 10% minimum) that rolls to its new value like the AR number does. While watching a replay,
  clicking its icon fades those objects to 10% (each fades in when it becomes playable). Doesn't combine with mods that
  animate or move objects or need approach circles (Hidden, Traceable, Approach Different, Freeze Frame, Transform, Wiggle,
  Spin In, Grow/Deflate, Magnetised, Repel, Depth, Barrel Roll).
- **Click Control** (CC, Difficulty Increase), McOsu's "Jigsaw 1": a click that doesn't hit anything counts as a miss. It breaks
  combo, lowers accuracy and health and can fail Sudden Death / Perfect. There's no limit (more misses than objects is possible),
  but the map's max combo stays the same. Clicks before the first object, after the last one, in breaks and in gaps over 5 s don't
  count, and a spinner takes your first click. Doesn't combine with Relax, Alternate and Single Tap.

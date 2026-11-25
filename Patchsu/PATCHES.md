# Patches

Every patch is made against upstream osu!lazer commit [`8cb6e6e68d`](https://github.com/ppy/osu/commit/8cb6e6e68db6ad3387004c004ed2eb5f9728be37)
(the `Patchsu/osu-lazer` submodule) and is applied with `git apply` in file-name order. The files are in
[`Patchsu/patches/`](Patchsu/patches/). Each `.patch` starts with a plain-text description, which is the most detailed and up-to-date explanation of that patch (yet to be checked how readable it is for humans).

The machine-readable source of names, dependencies and presets is
[`Patchsu/patches/patches.meta.json`](Patchsu/patches/patches.meta.json). Patchsu-CLI and the release manifest are generated from it.

## Dependencies and presets

| Patch | Name | Needs | Notes |
|---|---|---|---|
| 0001 | Online restrictions | – | **required**, every other patch builds on it |
| 0002 | Local profiles | 0001 | big, slower build |
| 0003 | Mods from Ritsu | 0001 | osu! ruleset only; Modosu parts are GPL-3.0; replay toggles with 0007 |
| 0004 | Mods from Torii | 0001 | replay toggles with 0007 |
| 0005 | Interface adjustments | 0001 | |
| 0005b | Search by play mods | 0001 | `scorewithmod=`, `playwithmod=`, `playswithmod=` search keyword |
| 0006 | Pause PP penalty | 0002 | Taken from Torii client |
| 0007 | Replay mod toggles | 0001 | Applies to plain osu!lazer as well |
| 0008 | PP simulation | 0001 | also applies to plain osu!lazer |
| 0009 | McOsu mods | 0001 | osu! ruleset only |
| 0010 | Perfect-play PP sorting | 0001 | |
| 0011 | Mod compatibility | 0001 | |
| 0012 | No-Fail PP | 0002 | |
| 0013 | Collection folders | 0001 | |

0003 and 0004 can be used together or alone, in any order.

In mod select, the mods added by 0003, 0004 and 0009 come after osu!'s own mods in each column (in patch order),
except RandomV2, which sits right under Random.

| Preset | Patches (0001 is always included) | Builds |
|---|---|---|
| Vanilla+ | 0005, 0005b, 0007, 0008, 0010, 0013 | Patchsu |
| Local play | every patch | Patchsu |

Patches that have settings put them in **Settings → Patchsu → "Patch N. …"**, one subsection per patch (the tab comes with 0001, so it works without 0002).

## 0001 · Online restrictions

Only cuts online access, nothing else.

- Only read-only requests reach osu! servers (an allow-list inside `APIRequest`): logging in, beatmaps (lookup / search /
  download), beatmap tags, leaderboards, player profiles and replay downloads. Everything else fails before it touches the network.
- No score submission, no chat / notifications / spectator / multiplayer / presence connections, no crash reports, no update
  checks, no account creation.
- News, changelog, wiki, rankings, chat and social buttons and multiplayer / playlists / daily challenge are removed from the menus.
- The game calls itself "Patchsu" (window title, app name). The data folder stays the official `osu` one.

## 0002 · Local profiles

A fully local profile system with its own profile page, laid out like the website's.

- Right-click your name in the toolbar to switch between the online account and local profiles; left-click opens the profile page
  (stats, PP graph, best / recent scores, play history, most played, editing in place).
- Local profiles send nothing to osu! servers. With "online lookups" on, beatmap info / downloads, tags and leaderboards use the
  signed-in online account (scores are still never submitted).
- Per-profile play count, play time, hits, PP and an estimated global rank. Optional Relax / Autopilot PP, "Ignore 'Unranked' mod
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
- Failed / quit plays in Recent Plays open their beatmap in song select on click (there are no results or replay to open).
- Speedrun profiles ("⋮" → New 'Speedrun' profile, "SP Profile N", numbered and reset like SD/PF ones): set one goal in the
  window that opens, a PP value (`1000`) or an estimated global rank (`#10000`, osu! only). In osu! the other half is estimated as you type and
  shown with the goal, e.g. `#3 • 26,158pp` or `#1,314,315 • 400pp`. The timer on the page counts the time the profile was active (saved every minute and on switching or
  quitting), stops when a play (or a deleted score, or anything else that changes PP) reaches the goal, turns blue, and a
  "DONE!" notification shows the final time. The goal counts the ruleset selected when the profile was made.
- Failed / quit plays show in Recent Plays (rank F).
- Replay import (`.osr` folder) into a profile, export of the top 50 or all replays for current profile, exclude a score / difficulty / set from PP, "Transfer score" between profiles (from the profile page or a leaderboard score's right-click, replay or not), "Rebuild play statistics from saved scores" if .json broke.
- "Fetch top 200 plays of <profile> from osu!" (Settings → Patchsu → Patch 2): the best plays (current mode) of the osu! player with the profile's name are added to the profile, without replays. Beatmaps you don't have are downloaded after asking, under one notification that counts how many are downloaded and how many are left, and each play is added once its beatmap is in. Replays osu! has for those plays are then downloaded too and replace the plays without one. The notification can be swiped away while it runs; the fetch carries on in the notification list. Plays already there are skipped. Needs "Online lookups on local profiles" turned on (while it's off the button is greyed out and takes you to that setting) and your online account signed in.
- `PatchSettingsSubsection`: the base class other patches use for their "Patch N." settings subsection.
- Diagnostics: `OSU_LOCAL_DISABLE=tags,autoswitch,…` (or `patchsu-diagnose.sh`) turns features off at launch.

Data: `<osu data>/osu-local/` (profiles, caches, tags). Nothing in the official osu! database schema changes.

## 0003 · Mods from Ritsu

Gameplay mods ported from [Ritsu](https://github.com/Azuuryy/ritsu) (by Azuuryy; Modosu by Azuuryy, MOsu by p-720). osu! ruleset only,
acronyms kept as in Ritsu.

| Type | Mods |
|---|---|
| Difficulty reduction | Streamline (HST) |
| Difficulty increase | Miss Challenge (MC), No Sliding (NL), One Eye (HFR), Decoy Circles (DCY), Reversed Sliders (RSL) |
| Conversion | Circle Generation (CG), RandomV2 (RDV2), Spacing Adjust (SA) / Spacing Overtime (SOT) |
| Fun | Metronome (ME) |
| System | Phrase Mirror (PMR), Echo (ECH), Swing (SWG) |

Patchsu additions: No Sliding length threshold, and scores saved with an unresolved mod (`XX??`)
resolve to the real mod. Not ported: Directional Flicks, RandomV2 / Spacing Adjust replay panels.
The Modosu-derived files are **GPL-3.0** (`LICENCE-Modosu.txt` is added by the patch).

## 0004 · Mods from Torii

Mods from Torii (by ShikkesoraSIM): **Hit Sounder** (HS, osu!) and **Pitch Adjust** (PA, all rulesets).

## 0005 · Interface adjustments

Small song select / toolbar tweaks that stay true to lazer's UI:

- PP sort on the beatmap leaderboard.
- Leaderboard scores show the play's star rating (with its mods) just left of the "Max Combo" caption, lined up on every
  row; it hides when the date doesn't fit beside it.
- Right-click a sort dropdown to reverse the sort (an arrow next to "Sort" shows it).
- "Random" in the beatmap Sort dropdown: shuffles the carousel the way F2 picks maps (whole sets, or single difficulties
  when they aren't grouped into sets). The order stays while you search; pick Random again (after another sort) or reopen
  song select for a new shuffle.
- Right-click / drag on the star rating bar (and 0010's PP bar) moves the further end to the cursor; double right-click an end resets it; middle-click resets both.
- Middle-click "Now playing" on the toolbar mutes the music.
- "Play" and "Clear all local scores" removed from the footer Options (F3) menu.
- Shift + arrow keys move the caret by one character while the search box has text.

## 0005b · Search by play mods

A song select search keyword that shows only beatmaps you have a local play on (in the selected ruleset) with the given mods:

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

## 0006 · Pause PP penalty

Optional PP reduction for pausing: ×0.93 per pause (from Torii's pause penalty), on every map. Settings → Patchsu →
"Patch 6. Pause PP penalty". Shown on the results screen (with Torii's animation), leaderboard tooltips and the profile.
Toggling it animates the toolbar PP / rank. Also yoinkes a **pause counter** skin component, listed in its own skin editor section. Needs 0002

## 0007 · Replay mod toggles

Switch mod effects off while watching a replay: checkboxes in the replay settings panel, or click the mod icons on screen
(a switched-off mod is greyed out). **The replay's result never changes**: frames, judgements, score, accuracy, combo, hit timing
statistics and spinner spins stay exactly as recorded; only what you see and hear changes.

- Speed: Half Time, Daycore, Double Time, Nightcore, Wind Up, Wind Down (playback returns to normal speed and pitch).
- Audio: Muted.
- Visual: Hidden, Traceable, Flashlight, Blinds, No Scope, Bloom, Grow, Deflate (Bloom's cursor goes back to normal size).

0003, 0004 and 0009 carry the code for their own mods; 0007 defines `PATCHSU_REPLAY_MOD_TOGGLES`. 

## 0008 · PP simulation

A results-screen panel where every number can be typed into, to see what a play would be worth (osu! ruleset).

- Open it from song select → Options (F3) → "Simulate PP" (starts at SS), a leaderboard score's right-click → "Simulate PP",
  or the calculator button on a results screen.
- Typing accuracy or PP turns 300s into 100s (then 50s) and keeps misses and combo (osu-tools style).
- "Fix choke": left click turns misses into 100s, right click into 300s. Hits every slider tick; slider ends stay.
  
## 0009 · McOsu mods

Two mods from McOsu, osu! ruleset only. Mod select shows a green "from McOsu" plaque on both.

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

## 0010 · Perfect-play PP sorting

Ported from Ritsu (MIT). New song select sorts **"PP (perfect play)"** and **"Farmability"** (1000 × PP ÷ seconds ÷ stars²),
and a **"PP perfect play"** grouping. **Right-click "Show converts"** to show the PP row (PP max range, PP step, PP calc on/off).
The PP max range is coloured like the star rating bar and goes up to "1,000+" (no upper limit); PP groups above 1,000 pp
become one "1,000+ pp" group. Settings → Patchsu → "Patch 10. PP aware beatmap carousel" → "Remove the 1,000 pp cap of the PP slider" makes it 0 – 3,000 (∞).
Values follow the selected ruleset and mods and are calculated in the background, cached in `osu-local/perfect-pp-cache-v2.json`.

## 0011 · Mod compatibility

Settings → Patchsu → "Patch 11. Mod compatability". Off by default.
- **Hard Rock + Mirror** (both flips apply). Added because I want to test how well can I read HR, but I don't want to flip the screen and Difficulty Adjust don't have presets for now.

## 0012 · No-Fail PP

Settings → Patchsu → "Patch 12. Ignore No Fail": plays with No Fail give PP as if NF wasn't selected (results, leaderboards, HUD
PP counter, 0005's sort, 0008's simulation, local profiles). The play itself keeps NF. Setting in `osu-local/patch12.json`.
"Also remove 'No Fail' score reduction" shows NF plays' score x2, and every list orders by that shown score: leaderboards, the
results screen's score panels and the in-game leaderboard.

## 0013 · Collection folders

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

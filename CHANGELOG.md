Changelog go in patch numerical order, so 0001 will be first and 1000 will be last unless its a breaking change.

## V1.1 Release

#### 26 November · 5 changes
- **BREAKING**. Patchsu changed patch numbering/naming:
  - 0001–0499 reserved for custom patches
  - 0500–0999 reserved for custom mods
  - 1000-1999 reserved for custom private servers (none exist yet)

**0001-0499 patches**
0001-patchsu-online-restrictions.patch > 0001-Offline-Fix.patch
0002-patchsu-local-profiles.patch > 0002-Local-Profiles.patch
0005-interface-adjustments.patch > 0003-Interface-Adjustments.patch
0005b-scorewithmod-search.patch > 0003b-Search-Adjustments.patch
0006-pause-pp-penalty.patch > 0004-Pause-PP-Penalty.patch
0007-replay-mod-toggles.patch > 0005-Replay-Toggles.patch
0008-pp-simulation.patch > 0006-PP-Simulation.patch
0010-perfect-play-pp-sorting.patch > 0007-PP-Aware-Carousel.patch
0011-mod-compatibility.patch > 0008-Mod-Compatability.patch
0012-no-fail-pp.patch > 0009-No-Fail-Disable.patch
0013-collection-folders.patch > 0010-Collection-Folders.patch

**0500-0999 patches**
0003-mods-from-Ritsu.patch > 0500-Ritsu-Mods.patch
0004-mods-from-Torii.patch > 0501-Torii-Mods.patch
0009-mcosu-mods.patch > 0502-McOsu-Mods.patch

- **Patchsu single setting file**, `patchsu-osu/PatchsuSettings.json` in the osu! data folder (0001).
- **Patchsu old folder rename**. The `osu-local` folder is renamed to `patchsu-osu` on the first start. Your old settings are moved over by themselves (from game.ini, profiles.json and patch12.json) and removed from the old places. (0001/0002/0003/0007/0008/0009)
- `LOCAL map PP`, `Profiles with mod rules` and `Gray out scores that do not go towards PP` are now ON by default (0002)
- New Feature. **Search scores by their set date**. You can now search scores by when they were set via `scoredate`,`datescore`,`playdate`. For example `playdate<=180d` will search for scores that are set within 180 days from now and show you those beatmaps. `<` `>` and `=` works as filters, as well as days `<30d`, weeks `>4w`, months `=1m` and years `<2y`. Example use case - whenever you import scores from Bancho to local profile you can filter out by date and remember your old maps without going through profile. (0003b)

## V1.0 Release

#### 25 November · ~50 changes
- **New Feature**. Speedrun profiles ("⋮" next to "+", named "SP Profile"). Set a PP or estimated-rank goal `1000 / #10000`, auto calculates rank from PP and vice versa. A timer counts while the profile is active and stops, blue, with a "DONE!" notification when the goal is reached. "Reset SD/PF/SP profile count" now resets SP numbering too. (0002)
- **New Feature**. Fetch top 200 plays for profile with the same name as your Local One from osu! (Settings > Patch 2): adds that osu! player's best plays (current mode), downloads missing beatmaps after asking (one swipeable progress notification), then their replays where available. Skips plays already there. Needs "Online lookups on local profiles". Added this because friend sent Neki!stable client and this is a dope feature. (0002)
- Estimated global rank no longer stops at #120. (0002)
- SD/PF profiles countdown is now 30s with a setting that brings back 1 minute, shows seconds only, and only the last 3 s make a sound. On the last look, Best Performance scores fade away one by one. (0002)
- Clicking a failed / quit play in Recent Plays opens its beatmap in song select instead of doing nothing. (0002)
- Fix replayless plays on leaderboard bind to player ID for right-click menu. (0002)
- Custom mods now come after osu!'s own mods in every mod select column, RandomV2 still sits right under Random. (0003/0004/0009)
- Plaque text could get stuck in the wrong selected colour on custom mods. (0003/0004/0009)
- 0007b / 0007c replay toggles moved into 0003 (Ritsu mods) and 0004 (Torii mods) and don't need 0007's anymore. (0003/0004)
- Added plural variants for play/scorewithmod= filter. (0005)
- New Feature. "Random" carousel sort F2 style. Reshuffles when selected again or on reopening song select. (0005)
- Combine 0007a and 0007 to 0007. (0007)
- Support Bloom for No Scope parity. (0007)
- New patch. **Mafham mod**. Drawn invisibly in a 3 s lead-in (1 s on retry), no lag on maps <10 minutes, semi-transparent skins look the same as live, no kiai flashing. Located at the bottom of Fun section. (0009)
- New patch. **Click Control**. Same as McOsu Jiggsaw 1. Misses can go past object count and will affect accuracy. (0009)
- Fix PP Calc button displacement. (0010)
- Fix No Fail score reduction removal not appearing in leaderboards. (0012)
- Clicking a collection with sub-collections allows you to add to main parent collection now. (0013)
- "Merge into parent" from subfolders option on RMB. (0013)
- Left / right arrows move past folder rows; shift+arrows step into sub-folders. (0013)
- Undid change where folders follow sorting rules. (0013)

#### 24 November · 78 changes
- Settings → Patchsu got one "Patch N." section per patch. Built in 0001 now. (0001)
- Auto-switch rules understand things like `>5 <7` or `180, 190`. (0002)
- Failed and quit plays show up in Recent Plays with an F, like on the website. (0002)
- Sudden Death / Perfect Death's last look now fades the page to grey. (0002)
- Big optimisation passes: the profile page draws far fewer things per frame, PP sorting keeps a smaller cache, and a Ritsu mod class that accidentally broke osu!'s own Bubbles mod got fixed. (0002/0003/0005)
- Leaderboard scores show their star rating with mods. (0005)
- PP simulation now look more presentable and polished. (0008)
- New patch. **Beatmap aware carousel**. (0010)
- New patch. **Incompatable mods compatable**. (0011)
- New patch. **Ignore No Fail in PP**. (0012)
- No Fail can also stop halving your score. (0012)
- New patch. **Collection folders**. (0013)
- Collection folders also work with the collection filter, not only with "Group by Collections". (0013)

Patchsu-CLI and build scripts
- Presets: **Vanilla+** (old Minimal Vanilla + 0013) and **Local play** (every patch).
- Patches are built into the binary; on start it checks GitHub for newer patches (`--no-update` skips, `--release TAG` /
  `--local DIR` pick a source).
- Data folder and install location can be chosen (`f` / `i` on the check screen), remembered for `build` / `uninstall`.
- The game, its installs and Patchsu-CLI.exe get the Patchsu logo as their icon.

#### 23 November · 9 changes
- Pick your own profile page colour in settings. (0002)
- Sudden Death / Perfect Death profiles name themselves ("SD Profile 3"). (0002)
- The pause counter got its own section in the skin editor. (0006)
- Grow / Deflate can be turned off in replays too, plus replay toggles for the Torii mods. (0007c)
- New patch. **PP simulation**: type in any accuracy, combo or misses and see what the play would be worth. (0008)

#### 22 November · 30 changes
- Client got renamed to **Patchsu** and got its own logo.
- Ignore "Unranked" mod status and count any play as ranked. The profile toolbar shows how your PP and rank change, just like after an online score. (0002)
- Sudden Death and Perfect Death profiles added: profile deletes itself after a last look at its page, with a red countdown. (0002)
- Each profile page gets its own colour. (0002)
- "Rebuild play statistics from saved scores" emergency button, and the profile page no longer jumps around when switching profiles. (0002)
- New patch. **Replay mod toggles**: turn mods off while watching a replay. (0007/0007a/0007b)

#### 21 November · 9 changes
- The profile page got redone to look like the online profile in-game. (0002)
- New patches. **Ported mods from Ritsu and Torii**. (0003/0004)
- New patch. **Interface tweaks**: reverse any sort with right-click, sort the leaderboard by PP, right-click the star rating bar, middle-click "Now playing" to mute. (0005)
- New patch. **Pausing PP reduction**. (0006)

#### 20 November · 23 changes
- The online patch was rewritten to be as small as possible: it only cuts online access and does nothing else. (0001)
- Local profiles: a profile page, a right-click menu on scores (delete, export), profiles that only allow certain mods, dragging profiles into any order, moving a score to another profile. (0002)
- Play history and "Most Played" on the profile page. (0002)
- Local beatmap tags, and profiles that switch automatically depending on the beatmap you pick. (0002)

#### 19 November · 4 changes
- Started with two patches: one that cuts osu!lazer off from the online stuff, and one that adds local profiles. (0001/0002)

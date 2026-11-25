## Unreleased

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

Also optimisation pass over every patch file to try and match clean osu!lazer performance as much as possible, now mostly matches clean lazer (needs further testing). Added those findings as rules to [AGENTS.md](AGENTS.md)

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

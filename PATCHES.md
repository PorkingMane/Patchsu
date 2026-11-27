Initially I thought that this repo will grow out of hand very quickly, but it actually happened because Claude like to write a lot of things... Video showcase probably would cover most of the information anyway, so this file is just for myself keeping track of big features.

These are all patches for now that are available in this github repo, I tried to explain each change as simple as possible. Again, I'm not a programmer, especially in C#, but I do understand every change that these patches do even without Claude explaining them to me, so everything is tested and *should* work fine, if not, create Issue on github or join discord

# Patch 0001. Offline-online-fix
This disables any submission activity from this client, you can only retrieve information such as leaderboards, beatmaps, replays. It also keeps every patch's settings in one file, `patchsu-osu/PatchsuSettings.json` in your osu! data folder. Every patch is dependent on it for obvious reasons, we don't want to submit anything nor make AI change something to allow this client submitting scores on Bancho. I do know that lazer itself is limiting capability of sending stuff to the servers whenever you run dev builds, but still think this is better than not having one.

# Features
- Also adds 2 new colors for tooltips and popups. This differs from original osu!lazer, but this is smallest addition possible to make everything look neat

# Patch 0002. Local profiles
This is a massive one. Literally, it has 16k lines of code in it at the time of writing.

## Features
- Local profiles with options to create profiles based on mod rules, as well as:
    - Estimated global rank based on your PP
    - Basic profile customisation with Profile Pictures, Flag, Nickname change
    - Sync your database scores with local profile name^
    - Auto switch between profiles on almost any trigger possible (BPM / Star Rating / Mapper / Map Tags, including local ones / Length / Title / Artist / Difficulty Names)
    - Sudden Death / Perfect mod dependent profiles that will 'self-destruct' if you miss / get anything lower than 100
    - Speedrun profile with in-game timer that tracks whenever this profile is active, showing PP / Rank you need to reach
    - Local maps / Unranked maps / Unranked mod combinations / Relax and Autopilot can all count towards PP with each being separate setting
    - Your actual Bancho profile top play downloading and import to local profile (with auto map and replay downloads, if Bancho has them)^^
    - Export your top 50 / All replays from local profile into folder
    - Import all replays from folder to your local profile (imports directly into lazer database)
    - Remove scores that can't be calculated (also from database)
    - Delete scores from local profile (with option to delete them from database as well)
    - Mod rules allows you to Block certain mods; Only allow selected mods; Allow selected mods with exact setting; Auto switch to profile whenever rule mod preset is activated

^ - if you have scores under same name in database (locally) as your local profile, game will suggest you to sync every single one of them to your profile. Changing your name/pfp/flag will reflect on local leaderboards as well if you sync them. Deleting your profile WITH scores that were synced WOULD NOT delete scores from database. They are kept as a separate connection and Patchsu gives them back to the player ID they belonged to before

^^ - Require API key for users other than the one you login with. You can fetch ONCE per 10 minutes with a rate limit of 1 request per second, complying with osu!API rules.

This is a massive .patch file on top of basic osu!lazer profile and database system that allow you to do basically whatever you want locally, count anything you want towards PP as well as create some challanges for yourself... *ahem* youtubers *ahem*...
I know the implementation itself might look a bit dry, but this is the best I could think of reusing same assets osu! already provides to not create something new, or something that looks vibecoded.

# Patch 0003 and 0003b. Interface improvements
Lazer doesn't have a few basic features people actually want that I saw on GitHub discussions, so I added a few I read and could think of. They are split because 0003b feels kinda big for database and lookups imo, so I decided to split them just in case

## Features 0003:
- Score sort by PP
- Shows Star Rating pill on each score that represent SR with mods score was set with
- Shift+arrow keys allow you to move text cursor by 1. Surprisingly this is not implemented in lazer yet... for some reason
- Reverse sorting on right-click on Sort menu
- "Random" sort in the beatmap Sort dropdown mimicking F2 button
- "Play" and "Clear all local scores" are removed from the F3 menu for time being (Subjected to change).
- Middle click on 'Now Playing' near progile will mute music fully, however will leave UI sounds
- Right clicking on Star Rating slider will move FURTHEST slider end to your cursor, double right-clicking reset it to default (matches LMB), Mouse wheel click on Star Rating bar reset it to default 
- Shows skip button on breaks that are longer than 3 seconds, pressing it will move you to 3 seconds left in the break (can be toggled off)
- Fix RMB menus to always show on 1 side if there is not enough room for other sub-elements
- RMB on "Now Playing" muffles the music (same as if you were in "Manage Collections" window), MMB mutes music only. Option to change mouse binds in Patchsu > Patch 3.
Also automatically re-enable/unmuffle music whenever entering beatmap
- Adds 'Look for similar' button that uses [Bobert](https://bobert-web.pages.dev/) to both RMB and Options menu
- Consolidate options menu to allow for more things
- Support for `0`, `-` and `=` in options menu, increasing number of buttons usable from keyboard only from 9 to 12

## Features 0003b:
- Adds `scorewithmod=`, `scoreswithmod=`, `playwithmod=`, `playswithmod=` search function. Will show maps with local plays that were played with those mods. Supports acronyms `scorewithmod=DT`, multiple mods `=DT,HR,HD`, and full mod titles `scorewithmod='Double Time','Hidden'`. Also wrorks with `!` to exclude them
- Adds `scoredate`,`datescore`,`playdate` search function. For example `playdate<=180d` will search for scores that are set within 180 days from now and show you those beatmaps. `<` `>` and `=` works as filters, as well as days `<30d`, weeks `>4w`, months `=1m` and years `<2y`. Example use case - whenever you import scores from Bancho to local profile you can filter out by date and remember your old maps without going through profile. Also works with `!` to exclude them

# Patch 0004. Pause PP Penalty (Require 0002)
First patch that requires local profiles to compile. I found out that Torii implemented skin element that counts your pauses in play, then found out that on any ranked play this will reduce your PP by 7% and it stacks, so 7% - 14% - 21% and so on. This worked on EVERY ranked play without an option to toggle... which is weird, so I added this in Settings > Patchsu > Patch 0004.
0002 Dependency is here to actually track those pauses in .json file rather than adding counter directly to lazer database

## Features:
- Pause counter skin element
- Compounding PP decay with each pause by 7%. Received penalty can be seen in result screen after small delay
- Toggle option to count PP penalty or not

# Patch 0005. Replay mod toggles
All mod ports from different clients has this baked in. This is for vanilla lazer mods only, which I think is a really missing feature :(

## Features:
- You can disable any of these mods in replay view by clicking on their icons
    - Half-Time / Daycore (will remove pitch adjust as well)
    - Double Time / Nightcore (also removes pitch adjust)
    - Hidden / Traceable
    - Flashlight / Blinds
    - Wind Up / Down
    - Grow / Deflate
    - Muted
    - No Scope / Bloom

# Patch 0006. In-game PP simulation
Reuses result screen and allow to edit every field present on it. I'll try to update it as fast as possible when new UI interface releases, but I can't guarantee it

## Features:
- Adds 'Simulate PP' button in F3 (Options on the bottom bar) menu
- Adds 'Simulate PP' button in RMB menu on scores
- Adds 'Simulate PP' button as a button with calculator at the bottom of result screen when clicking on scores
- Freely change mods in simulation (reuses mod selection menu)
- Will math out any combination you need to do for desired PP/Acc
- Buttons that:
    - Reset to score that was originally
    - Fix Choke, removes all misses and missed slider ticks replacing them with 100s on Left click and 300s on Right click, keeps missed slider ends though
    - Reset to SS
- Tab for quick input navigation, which makes it possible to not use mouse completely

# Patch 0007. PP Aware beatmap carousel
Ported from Ritsu, thanks Azury <3

## Features:
- Adds Sort option 'Farmability' that calculates how quickly you can get PP relative to its overall difficulty via ` 1000×PP÷Seconds÷stars² `... works SURPRISINGLY well if you also filter by `status=r`
- Adds Sorting and Grouping by PP (for SS) with currently selected mod combination. To sort and group by it, you need to
- Right-click on Show Converts slides in a menu for max PP slider^, PP grouping steps^^ and PP calculation that is turned off by default. It will automatically calculate maps with mods selected if you Sort/Group by PP (Perfect play). Calculations are cached in `patchsu-osu/perfect-pp-cache-v2` in your osu! folder 

^ - limit minimal and maximum amount of PP visible in Grouping, think that slider being on 200-250 will only show you maps that are 200-250 PP for SS no matter the PP strep value

^^ - PP step value is how big should be each collection. If you set PP step at 100, then collections will go 0-100, 100-200, etc.
Example: PP slider set on 140 ~ 190 and PP step at will show 5 different groups that go 140-150, 150-160, 160-170, 170-180, 180-190. All counted for SS with YOUR CURRENT mods

# Patch 0008. Re-allow mod compatability
Lazer limits mods that can be combined with each other for various reasons, but not all of them make sense. This is OFF by default

## Features:
- Allow Hard Rock and Mirror to be combined again

# Patch 0009. No-fail PP and Score Multiplier reduction remove (Require 0002)
I like play with NF. I don't like how missing 1 time basically halves my PP on hard maps and I don't like that if I FC I get 0.52x score multiplier despite it being actual FC. This is local profile / cosmetic only change. Your scores stays intact whenever this applied. This will touch PP in-game simulation as well

## Features:
- Settings > Patchsu > Patch 9.
    - Remove PP reduction for No Fail
    - Remove score reduction for No Fail (needs removed PP reduction first)

# Patch 0010. Sub-collections for collections
Remember osu!stable collection view? Think this but inside your lazer collections AS WELL as sub-collections

## Features:
- Adds support for sub-collections that are generated via / in the name of actual collection
- Show folders in beatmap carousel, sorted by folder names
- Folders can be switched through if you hold SHIFT
- Merge sub-collections, send all beatmaps from folder back to Parent (Initial collection thats before / )
- Updated F3 collection menu to allow for subfolder
- Sub-collections slide out from Parent collection do not make Parent hovered state unclickable^
- "Manage Collection" menu improvements, allowing you to create sub-collections, slide them around while holding right-click and a popup menu when you click on collection with RMB

^ - Whenever you right click on beatmap/mapset, there is a 'Collection' section in the menu, which on hover show all your collections. Initially those menus are not clickable, however with this patch collections that are parents (so they show all subcollections) are clickable.

# Patch 0500. Mods ported from Ritsu client
Ritsu is a fork of Torii... which is a fork of osu!lazer with custom patches applied on top, so its a double-hop updates. Rather, I ported everything over to clean lazer, because I trust peppy more than Torii

## Features:
- Streamline, most long streams will be converted in single-tap jumps that follow stream pattern with occasional tripples/small streams
- One eye, halves approach rate
- Decoy Circles, spawn Decoys near single notes with configurable Minimum Interval between each (in beats), appearance (Gray or use Combo color) and numbers on them (Either a cross, think fruit ninja bombs; Combo number from 0 to 9; Actual combo number). My personal favorite from all of new mods, its surprisingly fun to play with, for me at least :D
- Reversed sliders, make slider head appears on other end if sliders has no reverse arrows

# Patch 0501. Mods ported from Torii client
As stated above... I don't really like how there is big dependency chains while there is an active osu!lazer development happening as well, so its easier to update/port features to clean lazer than hoping for updates

## Features:
- Hit sounder, hit sounds follows your clicks rather than objects with a few settings
- Pitch adjust, configures pitch of a song without actually speeding/slowing the map, useful if you run into copyright :D

# Patch 0502. McOsu mods
Another mod port, but this time from McOsu! I also got official approval from McKay for it :)

## Features:
- Mafham mod. Biggest thing to port out of all tbh...
    - Optimised things as much as possible by culling objects in separate chunks that are drawn static as one object, until ~10 objects, then each one wakes up.
    Tested on Ryzen 7 5600 + RTX 3060, easily renders 10 minute maps, so for average Mafham enjoyer this should be enough. I will try to optimise it until it can run Unforgiving, but I can't promise anything
    - Customised culled object brightness from 100%, which is default Mafham, to 10%, which is just fun reading challange :D
    - Can be toggled off in replay view to see what you actually missed on
    - Is fully compatable with any other mod besides one that change how approach circle works
    - AR shows as −∞ with a 'brightness exponent' :D
- 'Jigsaw 1' renamed to 'Click Control'
    - Click that are not on object counted as misses (so they lower your accuracy and health)
    - Gaps over 5 seconds are ignored
    - There is no miss limit and it CAN go above object count, works fine
    - Doesn't work in breaks, first click on each spinner, before first object

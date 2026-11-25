Scratch tool (not part of any patch): measures real GPU draw statistics of the local profile page.
Opens a game window for ~30 s, uses its own data folder ~/.local/share/patchsu-drawtest (delete it afterwards).
Build against an applied tree:  dotnet build -c Release -p:Tree=/path/to/clone -o out
Run:                             dotnet out/drawtest.dll /path/to/copy-of-a.osz results.txt
(a copy of osu.Game.Tests/Resources/Archives/241526 Soleily - Renatus.osz works; the tool copies it again before importing)
Prints DrawCalls / TextureBinds / ShaderBinds / FBORedraw per step and saves a screenshot per step next to results.txt.

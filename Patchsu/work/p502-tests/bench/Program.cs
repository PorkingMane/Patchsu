// Scratch tool (not part of any patch): measures patch 502's Mafham on a real renderer.
// Usage: dotnet run -p:Tree=<work clone> -- <mode: none|mafham|naive> <out.txt> [skin: argon|classic]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Statistics;
using osu.Framework.Testing;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Menu;
using osu.Game.Skinning;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Visual;
using osuTK;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var host = Host.GetSuitableDesktopHost("patchsu-mafham-bench", new HostOptions { FriendlyGameName = "patchsu mafham bench" });
        host.Run(new BenchGame(args[0], args[1], args.Length > 2 ? args[2] : "argon", args.Length > 3 ? args[3] : "standard", args.Length > 4 ? args[4] : "dense"));
    }
}

/// <summary>
/// Mafham without frozen copies: every object's drawable is alive from the start (what a plain "AR -infinity" would do).
/// </summary>
public class NaiveMafham : OsuModMafham, IApplicableToDrawableRuleset<OsuHitObject>
{
    void IApplicableToDrawableRuleset<OsuHitObject>.ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
    {
        ((osu.Game.Rulesets.Osu.UI.DrawableOsuRuleset)drawableRuleset).Playfield.FollowPoints.Hide();

        var objects = drawableRuleset.Beatmap.HitObjects.Where(h => h is not Spinner).ToList();
        double appear = GetAppearTime(objects, 1);

        // Before the playfield makes its entries, so they all get the long preempt as their lifetime.
        foreach (var h in objects)
            ApplyPreempt(h, appear);
    }
}

public partial class BenchGame : OsuGame
{
    private readonly string mode;
    private readonly string outFile;
    private readonly string skin;
    private readonly string scenario;
    private readonly string map;

    private TestPlayer? player;
    private osu.Game.Screens.Play.ReplayPlayer? replayPlayer;

    public BenchGame(string mode, string outFile, string skin, string scenario, string map)
    {
        this.map = map;
        this.mode = mode;
        this.outFile = outFile;
        this.skin = skin;
        this.scenario = scenario;
    }

    internal OsuModMafham.MafhamLayer? layer => player?.DrawableRuleset.ChildrenOfType<OsuModMafham.MafhamLayer>().SingleOrDefault();

    private double maxFrame()
    {
        double m = 0;
        for (int i = 0; i < 20; i++) { m = Math.Max(m, Math.Max(Host.UpdateThread.Clock.ElapsedFrameTime, Host.DrawThread.Clock.ElapsedFrameTime)); Thread.Sleep(10); }
        return m;
    }

    private void state(string label)
    {
        var (t, alpha, shown, frozen, pages, copies) = onUpdate(() => (player!.DrawableRuleset.FrameStableClock.CurrentTime, layer?.Alpha ?? 0, layer?.AllPagesShown ?? false, layer?.FrozenCount ?? 0, layer?.PageCount ?? 0, layer?.CopyCount ?? 0));
        log($"{mode,-7} {scenario} {label,-10} t={t:0} layerAlpha={alpha:0.00} allShown={shown} frozen={frozen} pages={pages} copies={copies} maxFrame(200ms)={maxFrame():0.0}ms");
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Add(recorder = new FrameRecorder(this));
        Task.Run(script);
    }

    private FrameRecorder recorder = null!;

    public partial class FrameRecorder : osu.Framework.Graphics.Component
    {
        private readonly BenchGame game;
        public volatile bool Recording;
        public readonly List<(double T, double Update, double Draw, int Gc0, int Gc1, int Gc2, int Shown, int Copies)> Frames = new();
        private readonly Stopwatch watch = Stopwatch.StartNew();

        public FrameRecorder(BenchGame game) { this.game = game; AlwaysPresent = true; }
        private OsuModMafham.MafhamLayer? cachedLayer;

        protected override void Update()
        {
            base.Update();
            if (!Recording) return;
            cachedLayer ??= game.layer;
            var l = cachedLayer;
            Frames.Add((watch.Elapsed.TotalMilliseconds, game.Host.UpdateThread.Clock.ElapsedFrameTime, game.Host.DrawThread.Clock.ElapsedFrameTime,
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), l?.ShownPageCount ?? -1, l?.CopyCount ?? -1));
        }
    }

    private T onUpdate<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>();
        Schedule(() =>
        {
            try { tcs.SetResult(f()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    private void log(string line)
    {
        Console.WriteLine(line);
        File.AppendAllText(outFile, line + Environment.NewLine);
    }

    private static readonly string[] counters = { "DrawCalls", "TextureBinds", "VerticesDraw", "FBORedraw" };

    private void sample(string label)
    {
        var sums = new Dictionary<string, double>();
        double drawMs = 0, updateMs = 0, maxUpdate = 0, maxDraw = 0;
        int n = 0;

        for (int i = 0; i < 150; i++)
        {
            foreach (string c in counters)
                sums[c] = sums.GetValueOrDefault(c) + GlobalStatistics.Get<long>("Draw", c).Value;

            double u = Host.UpdateThread.Clock.ElapsedFrameTime, d = Host.DrawThread.Clock.ElapsedFrameTime;
            updateMs += u;
            drawMs += d;
            maxUpdate = Math.Max(maxUpdate, u);
            maxDraw = Math.Max(maxDraw, d);
            n++;
            Thread.Sleep(10);
        }

        long managed = GC.GetTotalMemory(true) / (1024 * 1024);
        long rss = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);

        int live = onUpdate(() => player?.DrawableRuleset.Playfield.AllHitObjects.Count() ?? 0);
        int frozen = onUpdate(() => player?.DrawableRuleset.ChildrenOfType<OsuModMafham.MafhamLayer>().SingleOrDefault()?.FrozenCount ?? 0);
        int pages = onUpdate(() => player?.DrawableRuleset.ChildrenOfType<OsuModMafham.MafhamLayer>().SingleOrDefault()?.PageCount ?? 0);
        int copies = onUpdate(() => player?.DrawableRuleset.ChildrenOfType<OsuModMafham.MafhamLayer>().SingleOrDefault()?.CopyCount ?? 0);
        bool shown = onUpdate(() => player?.DrawableRuleset.ChildrenOfType<OsuModMafham.MafhamLayer>().SingleOrDefault()?.AllPagesShown ?? true);
        double fps = Host.UpdateThread.Clock.FramesPerSecond;

        log($"{mode,-7} {label,-14} " + string.Join("  ", counters.Select(c => $"{c}={sums[c] / n:0}"))
            + $"  update={updateMs / n:0.00}ms (max {maxUpdate:0.0})  draw={drawMs / n:0.00}ms (max {maxDraw:0.0})  updateFps={fps:0}"
            + $"  liveDHO={live} frozen={frozen} pages={pages} copies={copies} allShown={shown}  managed={managed}MB rss={rss}MB");
    }

    private void shot(string label)
    {
        try
        {
            using var image = Host.TakeScreenshotAsync().GetAwaiter().GetResult();
            string file = Path.Combine(Path.GetDirectoryName(outFile)!, $"{mode}-{(skin.EndsWith(".osk") ? "userskin" : skin)}-{label}.png");
            using var stream = File.Create(file);
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, stream);
        }
        catch (Exception e)
        {
            log("screenshot failed: " + e.Message);
        }
    }

    private void seekAndFreeze(double time)
    {
        onUpdate(() =>
        {
            player!.GameplayClockContainer.Stop();
            player.Seek(time);
            return 0;
        });

        // wait for frame-stable catch-up (a paused clock only moves with a replay attached)
        var timeout = Stopwatch.StartNew();
        while (onUpdate(() => player!.DrawableRuleset.FrameStableClock.IsCatchingUp.Value || Math.Abs(player.DrawableRuleset.FrameStableClock.CurrentTime - time) > 1))
        {
            if (timeout.ElapsedMilliseconds > 90000) throw new TimeoutException($"seek to {time} didn't finish");
            Thread.Sleep(50);
        }

        Thread.Sleep(1000);
    }

    private void runScenario()
    {
        switch (scenario)
        {
            case "start":
            {
                var startWatch = Stopwatch.StartNew();
                for (int i = 0; i < 40; i++)
                {
                    state($"+{startWatch.ElapsedMilliseconds}ms");
                    if (i % 3 == 0) shot($"start-{i:00}");
                    Thread.Sleep(30);
                }

                log($"appear at {onUpdate(() => layer?.AppearTime ?? 0):0}, gameplay start {onUpdate(() => player!.GameplayClockContainer.GameplayStartTime):0}, first object {onUpdate(() => player!.DrawableRuleset.Objects.First().StartTime):0}");
                break;
            }

            case "prep":
            {
                while (onUpdate(() => !player!.GameplayClockContainer.IsRunning)) Thread.Sleep(1);
                recorder.Recording = true;
                log($"PREP-START ms since process start: {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:0}");
                var w = Stopwatch.StartNew();
                var prepLayer = onUpdate(() => layer);
                while (onUpdate(() => prepLayer?.AllPagesShown != true) && w.ElapsedMilliseconds < 20000) Thread.Sleep(20);
                Thread.Sleep(1500);
                recorder.Recording = false;
                var f = onUpdate(() => recorder.Frames.ToList());
                log($"pages {onUpdate(() => layer!.PageCount)}, frames {f.Count}, update>16.7: {f.Count(x => x.Update > 16.7)}, draw>16.7: {f.Count(x => x.Draw > 16.7)}, gc0 {f[^1].Gc0 - f[0].Gc0} gc1 {f[^1].Gc1 - f[0].Gc1} gc2 {f[^1].Gc2 - f[0].Gc2}");
                for (int i = 1; i < f.Count; i++)
                {
                    var x = f[i];
                    if (x.Update > 12 || x.Draw > 12 || x.Gc2 != f[i - 1].Gc2 || x.Shown != f[i - 1].Shown)
                        log($"  t={x.T - f[0].T,7:0} update={x.Update,6:0.0} draw={x.Draw,6:0.0} shown={x.Shown} copies={x.Copies} gc0+{x.Gc0 - f[i - 1].Gc0} gc1+{x.Gc1 - f[i - 1].Gc1} gc2+{x.Gc2 - f[i - 1].Gc2}");
                }
                break;
            }

            case "retry":
            {
                onUpdate(() => { player!.DrawableRuleset.Playfield.Cursor?.Hide(); return 0; });
                var w = Stopwatch.StartNew();
                while (onUpdate(() => OsuModMafham.PictureCache.Current == null) && w.ElapsedMilliseconds < 20000) Thread.Sleep(20);
                log($"first play: pictures kept after {w.ElapsedMilliseconds} ms, start {onUpdate(() => player!.GameplayClockContainer.GameplayStartTime):0}, appear {onUpdate(() => layer!.AppearTime):0}");
                double appear1 = onUpdate(() => layer!.AppearTime);
                while (onUpdate(() => player!.DrawableRuleset.FrameStableClock.CurrentTime < appear1 + 1200)) Thread.Sleep(10);
                shot("retry-first");

                var oldPlayer = player;
                onUpdate(() =>
                {
                    SelectedMods.Value = new List<Mod> { new OsuModAutoplay(), new OsuModMafham() };
                    player!.Exit();
                    PerformFromScreen(s => s.Push(player = new TestPlayer(true, false)), new[] { typeof(MainMenu) });
                    return 0;
                });
                var retryWatch = Stopwatch.StartNew();
                while (onUpdate(() => player == oldPlayer || player?.IsLoaded != true || !player.IsCurrentScreen())) Thread.Sleep(10);
                log($"retry: player shown after {retryWatch.ElapsedMilliseconds} ms, start {onUpdate(() => player!.GameplayClockContainer.GameplayStartTime):0}, snapshot pages {onUpdate(() => layer!.SnapshotPageCount)}/{onUpdate(() => layer!.PageCount)}");
                onUpdate(() => { player!.DrawableRuleset.Playfield.Cursor?.Hide(); return 0; });
                while (onUpdate(() => !player!.GameplayClockContainer.IsRunning || player.DrawableRuleset.FrameStableClock.CurrentTime >= 0)) Thread.Sleep(5);
                log($"retry: gameplay started at {retryWatch.ElapsedMilliseconds} ms");
                double shownAt = -1, visibleAt = -1;
                double maxFrame = 0;
                while (onUpdate(() => player!.DrawableRuleset.FrameStableClock.CurrentTime < layer!.AppearTime + 1200))
                {
                    var (shown, alpha) = onUpdate(() => (layer!.AllPagesShown, layer.Alpha));
                    if (shown && shownAt < 0) shownAt = retryWatch.ElapsedMilliseconds;
                    if (alpha > 0 && visibleAt < 0) visibleAt = retryWatch.ElapsedMilliseconds;
                    maxFrame = Math.Max(maxFrame, Math.Max(Host.UpdateThread.Clock.ElapsedFrameTime, Host.DrawThread.Clock.ElapsedFrameTime));
                    Thread.Sleep(5);
                }
                log($"retry: all pages shown at {shownAt} ms, visible at {visibleAt} ms, first object at {onUpdate(() => player!.DrawableRuleset.Objects.First().StartTime):0}, worst frame {maxFrame:0.0} ms, snapshot pages {onUpdate(() => layer!.SnapshotPageCount)}");
                shot("retry-second");
                state("retry");
                break;
            }

            case "compare":
                onUpdate(() => { player!.DrawableRuleset.Playfield.Cursor?.Hide(); return 0; });
                while (mode == "mafham" && !onUpdate(() => layer?.AllPagesShown == true)) Thread.Sleep(50);
                Thread.Sleep(3000);
                double firstTime = onUpdate(() => player!.DrawableRuleset.Objects.First().StartTime);
                foreach (double time in new[] { firstTime + 1500, firstTime + 30_000 })
                {
                    seekAndFreeze(time);
                    shot($"cmp-{time:0}");
                    state($"cmp-{time:0}");
                }

                log(onUpdate(() =>
                {
                    var circles = player!.DrawableRuleset.Playfield.AllHitObjects.SelectMany(d => d.ChildrenOfType<osu.Game.Rulesets.Osu.Objects.Drawables.DrawableHitCircle>()).ToList();
                    var legacy = circles.SelectMany(c => c.ChildrenOfType<LegacyKiaiFlashingDrawable>()).ToList();
                    var copies = layer?.ChildrenOfType<LegacyKiaiFlashingDrawable>().ToList() ?? new List<LegacyKiaiFlashingDrawable>();
                    string notOff = string.Join(",", copies.Where(l => l.FlashingDrawable.Scale != Vector2.Zero).Select(l => { osu.Framework.Graphics.Drawable p = l; while (p is not osu.Game.Rulesets.Objects.Drawables.DrawableHitObject) p = (osu.Framework.Graphics.Drawable)p.Parent!; var dho = (osu.Game.Rulesets.Objects.Drawables.DrawableHitObject)p; return $"{dho.GetType().Name}/{dho.HitObject.StartTime}/loaded={dho.IsLoaded}/state={dho.State.Value}"; }));
                    return $"  notOff: {notOff}\n  live circles={circles.Count} legacy flashes={legacy.Count} off={legacy.Count(l => l.FlashingDrawable.Scale == Vector2.Zero)}; in copies={copies.Count} off={copies.Count(l => l.FlashingDrawable.Scale == Vector2.Zero)}";
                }));

                // As if mid-way through the exit fade.
                onUpdate(() => { player!.Alpha = 0.4f; return 0; });
                Thread.Sleep(500);
                shot("cmp-faded");

                break;

            case "exit":
                while (onUpdate(() => player!.GameplayClockContainer.CurrentTime < 10_000)) Thread.Sleep(50);
                state("before");
                onUpdate(() => { player!.Exit(); return 0; });
                var sw = Stopwatch.StartNew();
                double worst = 0;
                int n = 0;
                while (sw.ElapsedMilliseconds < 1500)
                {
                    worst = Math.Max(worst, Math.Max(Host.UpdateThread.Clock.ElapsedFrameTime, Host.DrawThread.Clock.ElapsedFrameTime));
                    if (n++ % 3 == 0) shot($"exit-{sw.ElapsedMilliseconds:0000}");
                    Thread.Sleep(10);
                }

                log($"{mode,-7} exit worst frame during 1.5 s after exit: {worst:0.0} ms");
                break;

            case "relax":
            case "ap":
                for (int i = 0; i < 12; i++)
                {
                    state($"+{i}s");
                    Thread.Sleep(800);
                }

                shot("late");
                break;

            case "handoff":
                onUpdate(() => { player!.DrawableRuleset.Playfield.Cursor?.Hide(); return 0; });
                // object 12 (circle at 8000 ms, drawable alive at 7400): picture just before, drawable after its fade-in
                // object 13 (slider at 8500 ms, alive at 7900)
                foreach (var (name, time) in new[] { ("circle-picture", 7380.0), ("circle-live", 7700.0), ("slider-picture", 7880.0), ("slider-live", 8200.0) })
                {
                    seekAndFreeze(time);
                    shot(name);
                    state(name);
                    log(onUpdate(() =>
                    {
                        var target = player!.DrawableRuleset.Objects.ElementAt(name.StartsWith("circle") ? 12 : 13);
                        var copy = layer!.ChildrenOfType<OsuModMafham.FrozenObject>().FirstOrDefault(c => c.HitObject == target);
                        if (copy == null) return $"  {name}: no copy; frozen={layer.FrozenHitObjects.Contains(target)}";
                        var dho = copy.ChildrenOfType<osu.Game.Rulesets.Objects.Drawables.DrawableHitObject>().First();
                        var page = (osu.Framework.Graphics.Drawable)copy.Parent!.Parent!;
                        return $"  {name}: copyAlive={copy.IsAlive} copyPresent={copy.IsPresent} dhoAlpha={dho.Alpha:0.00} dhoAlive={dho.IsAlive} dhoPresent={dho.IsPresent} dhoTime={dho.Time.Current:0} dhoLifetime={dho.LifetimeStart:0}..{dho.LifetimeEnd:0} state={dho.State.Value} judged={dho.Judged} pageAlpha={page.Alpha:0.00} pagePresent={page.IsPresent} pageAlive={page.IsAlive} pageSize={page.DrawSize}";
                    }));
                }

                break;
        }
    }

    private void script()
    {
        try
        {
            onUpdate(() =>
            {
                ((osu.Framework.Allocation.IReadOnlyDependencyContainer)Dependencies).Get<FrameworkConfigManager>().SetValue(FrameworkSetting.FrameSync, FrameSync.Unlimited);

                if (skin == "classic")
                    SkinManager.CurrentSkinInfo.Value = osu.Game.Database.RealmObjectExtensions.ToLiveUnmanaged(DefaultLegacySkin.CreateInfo());
                return 0;
            });

            if (skin.EndsWith(".osk", StringComparison.Ordinal))
            {
                // Import deletes the file it imports, so it gets a copy.
                string copy = Path.Combine(Path.GetTempPath(), $"bench-skin-{Guid.NewGuid()}.osk");
                File.Copy(skin, copy);
                var imported = SkinManager.Import(new osu.Game.Database.ImportTask(copy)).GetAwaiter().GetResult();
                onUpdate(() => { SkinManager.CurrentSkinInfo.Value = imported!; return 0; });
            }

            Thread.Sleep(8000);

            WorkingBeatmap working;

            if (map.Contains(".osz::"))
            {
                // Import deletes the file it imports, so it gets a copy.
                string[] parts = map.Split("::");
                string copy = Path.Combine(Path.GetDirectoryName(outFile)!, $"bench-map-{Guid.NewGuid()}.osz");
                File.Copy(parts[0], copy);
                var imported = BeatmapManager.Import(new osu.Game.Database.ImportTask(copy)).GetAwaiter().GetResult()!;
                var info = imported.PerformRead(set => osu.Game.Database.RealmObjectExtensions.Detach(set.Beatmaps.Single(b => b.DifficultyName == parts[1])));
                working = BeatmapManager.GetWorkingBeatmap(info);
            }
            else
            {
                var beatmap = scenario == "handoff" ? Bench.CreateSparseBeatmap() : map == "sliders" ? Bench.CreateSliderBeatmap(180_000) : Bench.CreateLongBeatmap(180_000);
                working = new TestWorkingBeatmap(beatmap, null, Audio);
            }

            onUpdate(() =>
            {
                if (scenario == "compare")
                {
                    var rulesetConfig = (osu.Game.Rulesets.Osu.Configuration.OsuRulesetConfigManager)((osu.Framework.Allocation.IReadOnlyDependencyContainer)Dependencies)
                                        .Get<osu.Game.Rulesets.IRulesetConfigCache>().GetConfigFor(new OsuRuleset())!;
                    rulesetConfig.SetValue(osu.Game.Rulesets.Osu.Configuration.OsuRulesetSetting.SnakingInSliders, false);
                }

                Ruleset.Value = new OsuRuleset().RulesetInfo;
                Beatmap.Value = working;

                var mods = new List<Mod>();
                if (scenario is "standard" or "start" or "exit" or "handoff" or "compare" or "retry" or "prep") mods.Add(new OsuModAutoplay());
                if (scenario == "relax") { mods.Add(new OsuModRelax()); mods.Add(new OsuModNoFail()); }
                if (scenario == "ap") { mods.Add(new OsuModAutopilot()); mods.Add(new OsuModNoFail()); }
                if (mode == "mafham") mods.Add(new OsuModMafham());
                if (mode == "naive") mods.Add(new NaiveMafham());
                SelectedMods.Value = mods;

                if (scenario == "replaytoggle")
                {
                    SelectedMods.Value = new List<Mod> { new OsuModMafham() };
                    PerformFromScreen(s => s.Push(replayPlayer = new osu.Game.Screens.Play.ReplayPlayer((b, m) => new OsuModAutoplay().CreateScoreFromReplayData(b, m))), new[] { typeof(MainMenu) });
                }
                else if (scenario == "menu")
                    PerformFromScreen(s => s.Push(new osu.Game.Screens.Select.SoloSongSelect()), new[] { typeof(MainMenu) });
                else
                    PerformFromScreen(s => s.Push(player = new TestPlayer(true, false)), new[] { typeof(MainMenu) });
                return 0;
            });

            if (scenario == "replaytoggle")
            {
                while (onUpdate(() => replayPlayer?.IsLoaded != true || !replayPlayer.IsCurrentScreen())) Thread.Sleep(50);
                var rl = onUpdate(() => replayPlayer!.ChildrenOfType<OsuModMafham.MafhamLayer>().Single());
                while (onUpdate(() => rl.Alpha < 1)) Thread.Sleep(50);
                Thread.Sleep(2500);
                var toggle = onUpdate(() => replayPlayer!.ChildrenOfType<OsuModMafham.ReplayToggle>().SingleOrDefault());
                log($"toggle found: {toggle != null}");
                if (toggle == null) return;
                log(onUpdate(() => string.Join("; ", toggle.Icons.Select(i => $"icon {i.ScreenSpaceDrawQuad.AABBFloat} present={i.IsPresent}"))));
                log(onUpdate(() => string.Join("; ", toggle.ChildrenOfType<osu.Framework.Graphics.Drawable>().Skip(1).Select(a => $"area {a.ScreenSpaceDrawQuad.AABBFloat}"))));
                shot("replay-on");
                onUpdate(() =>
                {
                    // Click the first icon's area like a user would.
                    var icon = toggle.Icons.First();
                    var area = toggle.ChildrenOfType<osu.Framework.Graphics.Drawable>().Skip(1).First(a => a.ReceivePositionalInputAt(icon.ScreenSpaceDrawQuad.Centre));
                    area.TriggerClick();
                    return 0;
                });
                Thread.Sleep(1500);
                log(onUpdate(() => $"after click: enabled={replayPlayer!.GameplayState.Mods.OfType<OsuModMafham>().Single().EffectsEnabled} icon colour={toggle.Icons.First().Colour.AverageColour}"));
                shot("replay-off");
                return;
            }

            if (scenario == "menu")
            {
                Thread.Sleep(5000);
                shot("menu-songselect");
                onUpdate(() => { this.ChildrenOfType<osu.Game.Overlays.Mods.ModSelectOverlay>().First().Show(); return 0; });
                Thread.Sleep(2000);
                shot("menu-modselect");
                foreach (var mods in new[] { new List<Mod>(), new List<Mod> { new OsuModMafham() }, new List<Mod>() })
                {
                    onUpdate(() => { SelectedMods.Value = mods; return 0; });
                    var texts = new HashSet<string>();
                    for (int i = 0; i < 60; i++)
                    {
                        foreach (string t in onUpdate(() => this.ChildrenOfType<osu.Game.Overlays.Mods.VerticalAttributeDisplay>().SelectMany(d => d.ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>()).Select(x => x.Text.ToString()).ToList()))
                            texts.Add(t);
                        Thread.Sleep(5);
                    }
                    log($"mods [{string.Join(",", mods.Select(m => m.Acronym))}]: footer texts seen while rolling: {string.Join(" ", texts.OrderBy(x => x))}");
                }
                return;
            }

            var loadTimer = Stopwatch.StartNew();
            while (onUpdate(() => player?.IsLoaded != true || !player.IsCurrentScreen()))
                Thread.Sleep(50);
            log($"{mode,-7} player load {loadTimer.ElapsedMilliseconds} ms");

            if (scenario != "standard")
            {
                runScenario();
                return;
            }

            // a little after the first object (1000 ms)
            while (onUpdate(() => player!.GameplayClockContainer.CurrentTime < 3000))
                Thread.Sleep(50);
            sample("start");

            onUpdate(() => { player!.Seek(90_000); return 0; });
            while (onUpdate(() => player!.DrawableRuleset.FrameStableClock.IsCatchingUp.Value || player.GameplayClockContainer.CurrentTime < 91_000))
                Thread.Sleep(50);
            sample("middle");

            seekAndFreeze(2500);
            shot("t2500");
            seekAndFreeze(60_000);
            shot("t60000");
            seekAndFreeze(30_000); // rewind
            shot("t30000-rewound");
            sample("paused");

            if (Environment.GetEnvironmentVariable("BENCH_HOLD") != null)
            {
                log("holding for heap dump, pid " + Environment.ProcessId);
                Thread.Sleep(int.Parse(Environment.GetEnvironmentVariable("BENCH_HOLD")!));
            }
        }
        catch (Exception e)
        {
            log("ERROR " + e);
        }
        finally
        {
            Schedule(Exit);
        }
    }
}

public static class Bench
{
    /// <summary>
    /// 30 circles / sliders 500 ms apart, each in its own spot (for comparing an object's picture with its drawable).
    /// </summary>
    public static Beatmap CreateSparseBeatmap()
    {
        var hitObjects = new List<HitObject>();

        for (int i = 0; i < 30; i++)
        {
            var position = new Vector2(50 + i % 6 * 85, 40 + i / 6 * 75);
            double t = 2000 + i * 500;

            if (i % 2 == 1)
                hitObjects.Add(new Slider { StartTime = t, Position = position, Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(30, 25) }) });
            else
                hitObjects.Add(new HitCircle { StartTime = t, Position = position, NewCombo = i % 4 == 0 });
        }

        var beatmap = new Beatmap
        {
            HitObjects = hitObjects,
            BeatmapInfo = { Difficulty = { ApproachRate = 9, CircleSize = 6, OverallDifficulty = 8 }, Ruleset = new OsuRuleset().RulesetInfo },
        };

        beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 300 });
        return beatmap;
    }

    /// <summary>
    /// Mostly long curved sliders (some repeating) 300 ms apart, a circle now and then: like a slider-heavy ranked map.
    /// </summary>
    public static Beatmap CreateSliderBeatmap(double length)
    {
        var random = new Random(4321);
        var hitObjects = new List<HitObject>();

        double t = 1000;

        for (int i = 0; t < length; i++)
        {
            var position = new Vector2(random.Next(60, 452), random.Next(60, 324));
            bool newCombo = i % 6 == 0;

            if (i % 5 == 4)
            {
                hitObjects.Add(new HitCircle { StartTime = t, Position = position, NewCombo = newCombo });
                t += 150;
                continue;
            }

            Vector2 clamp(Vector2 v) => Vector2.Clamp(position + v, Vector2.Zero, new Vector2(512, 384)) - position;
            var mid = clamp(new Vector2(random.Next(-90, 90), random.Next(-90, 90)));
            var end = clamp(new Vector2(random.Next(-140, 140), random.Next(-140, 140)));

            hitObjects.Add(new Slider
            {
                StartTime = t,
                Position = position,
                NewCombo = newCombo,
                Path = new SliderPath(PathType.BEZIER, new[] { Vector2.Zero, mid, end }),
                RepeatCount = i % 7 == 3 ? 1 : 0,
            });

            t += 300;
        }

        var beatmap = new Beatmap
        {
            HitObjects = hitObjects,
            BeatmapInfo = { Difficulty = { ApproachRate = 8, CircleSize = 4, OverallDifficulty = 7, SliderMultiplier = 1.4 }, Ruleset = new OsuRuleset().RulesetInfo },
        };

        beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 300 });
        return beatmap;
    }

    /// <summary>
    /// Same map as TestSceneOsuModMafham: a circle every 100 ms, a short slider every fourth object, a spinner now and then.
    /// </summary>
    public static Beatmap CreateLongBeatmap(double length)
    {
        var random = new Random(1234);
        var hitObjects = new List<HitObject>();

        double t = 1000;

        for (int i = 0; t < length; i++)
        {
            var position = new Vector2(random.Next(40, 472), random.Next(40, 344));
            bool newCombo = i % 8 == 0;

            if (i % 150 == 149)
            {
                hitObjects.Add(new Spinner { StartTime = t, EndTime = t + 1500, Position = new Vector2(256, 192), NewCombo = true });
                t += 2000;
                continue;
            }

            if (i % 4 == 3)
            {
                var end = Vector2.Clamp(position + new Vector2(random.Next(-60, 60), random.Next(-60, 60)), Vector2.Zero, new Vector2(512, 384)) - position;

                hitObjects.Add(new Slider
                {
                    StartTime = t,
                    Position = position,
                    NewCombo = newCombo,
                    Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, end }),
                    RepeatCount = i % 8 == 7 ? 1 : 0,
                });

                t += 400;
                continue;
            }

            hitObjects.Add(new HitCircle { StartTime = t, Position = position, NewCombo = newCombo });
            t += 100;
        }

        var beatmap = new Beatmap
        {
            HitObjects = hitObjects,
            BeatmapInfo = { Difficulty = { ApproachRate = 9, CircleSize = 4, OverallDifficulty = 8 }, Ruleset = new OsuRuleset().RulesetInfo },
        };

        beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 300 });
        return beatmap;
    }
}

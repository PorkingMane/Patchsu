// Scratch tool (not part of any patch): measures GPU draw statistics of the local profile page on a real renderer.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osu.Framework.Statistics;
using osu.Framework.Testing;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.LocalPriority;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var host = Host.GetSuitableDesktopHost("patchsu-drawtest", new HostOptions { FriendlyGameName = "patchsu draw test" });
        host.Run(new DrawTestGame(args[0], args[1]));
    }
}

public partial class DrawTestGame : OsuGame
{
    private readonly string osz;
    private readonly string outFile;

    public DrawTestGame(string osz, string outFile)
    {
        this.osz = osz;
        this.outFile = outFile;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Task.Run(script);
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

    private static readonly string[] counters = { "DrawCalls", "TextureBinds", "ShaderBinds", "VerticesDraw", "FBORedraw", "UniformUpl" };

    private void sample(string label)
    {
        Thread.Sleep(1500);
        var sums = new Dictionary<string, double>();
        int n = 0;
        double frameMs = 0;

        for (int i = 0; i < 100; i++)
        {
            foreach (string c in counters)
                sums[c] = sums.GetValueOrDefault(c) + GlobalStatistics.Get<long>("Draw", c).Value;
            frameMs += Host.DrawThread.Clock.ElapsedFrameTime;
            n++;
            Thread.Sleep(10);
        }

        shot(label);
        log($"{label,-28} " + string.Join("  ", counters.Select(c => $"{c}={sums[c] / n:0}")) + $"  drawFrame={frameMs / n:0.00}ms");
    }

    private void shot(string label)
    {
        try
        {
            using var image = Host.TakeScreenshotAsync().GetAwaiter().GetResult();
            string file = Path.Combine(Path.GetDirectoryName(outFile)!, Path.GetFileNameWithoutExtension(outFile) + "-" + label.Replace(' ', '_').Replace("(", "").Replace(")", "") + ".png");
            using var stream = File.Create(file);
            SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, stream);
        }
        catch (Exception e)
        {
            log("screenshot failed: " + e.Message);
        }
    }

    private void script()
    {
        try
        {
            Thread.Sleep(9000);

            var manager = onUpdate(() => this.ChildrenOfType<LocalProfileManager>().Single());
            var profile = onUpdate(() =>
            {
                var p = manager.CreateDefaultProfile();
                manager.Switch(p.Id);
                return p;
            });

            string copy = Path.Combine(Path.GetTempPath(), $"drawtest-{Guid.NewGuid()}.osz");
            File.Copy(osz, copy);
            var set = BeatmapManager.Import(new ImportTask(copy)).GetResultSafely2();
            var beatmaps = set!.PerformRead(s => s.Beatmaps.Where(b => b.Ruleset.ShortName == "osu").ToArray().Select(b => b.Detach()).ToArray());
            var beatmap = beatmaps.First(b => BeatmapManager.GetWorkingBeatmap(b).GetPlayableBeatmap(new OsuRuleset().RulesetInfo).HitObjects.Count > 20);

            var ruleset = new OsuRuleset();
            var playable = BeatmapManager.GetWorkingBeatmap(beatmap).GetPlayableBeatmap(ruleset.RulesetInfo, Array.Empty<Mod>());
            var processor = ruleset.CreateScoreProcessor();
            processor.ApplyBeatmap(playable);
            var max = processor.MaximumStatistics;

            for (int i = 0; i < 120; i++)
            {
                var statistics = max.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value);
                statistics[HitResult.Great] -= i % 10;
                statistics[HitResult.Miss] = i % 10;

                ScoreManager.Import(new ScoreInfo
                {
                    Hash = Guid.NewGuid().ToString(),
                    BeatmapInfo = beatmap,
                    BeatmapHash = beatmap.Hash,
                    Ruleset = ruleset.RulesetInfo,
                    User = new APIUser { Id = profile.UserId, Username = profile.Name },
                    Mods = i % 3 == 0 ? new Mod[] { new OsuModHidden(), new OsuModHardRock() } : i % 3 == 1 ? new Mod[] { new OsuModDoubleTime() } : Array.Empty<Mod>(),
                    Statistics = statistics,
                    MaximumStatistics = new Dictionary<HitResult, int>(max),
                    MaxCombo = 100,
                    Accuracy = 0.9 + i / 2000.0,
                    TotalScore = 500000 + i,
                    Rank = ScoreRank.A,
                    Date = DateTimeOffset.Now.AddMinutes(-i),
                });
            }

            sample("main menu");

            var overlay = onUpdate(() => this.ChildrenOfType<LocalProfileOverlay>().Single());
            onUpdate(() => { overlay.Show(); return 0; });
            Thread.Sleep(3000);
            sample("profile page (compact)");

            for (int step = 0; step < 4; step++)
            {
                onUpdate(() => { overlay.ChildrenOfType<ShowMoreButton>().LastOrDefault()?.TriggerClick(); return 0; });
                Thread.Sleep(1500);
            }

            sample("recent expanded (top)");
            onUpdate(() => { overlay.ChildrenOfType<OverlayScrollContainer>().First().ScrollTo(1400, false); return 0; });
            sample("recent expanded (in list)");
            onUpdate(() => { overlay.ChildrenOfType<OverlayScrollContainer>().First().ScrollToEnd(false); return 0; });
            sample("recent expanded (bottom)");
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

public static class TaskExtensions
{
    public static T GetResultSafely2<T>(this Task<T> task) => task.GetAwaiter().GetResult();
}

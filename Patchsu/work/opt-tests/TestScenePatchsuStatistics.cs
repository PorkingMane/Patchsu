// Patchsu optimisation pass local test (not part of any patch): profile statistics after the 0002 changes
// (mods worked out once per combination, PP history, PP with/without a score, PerformanceRevision).
// Copy into osu.Game.Tests/Visual/Navigation and run `dotnet test --filter TestScenePatchsuStatistics`.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.LocalPriority;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Beatmaps.IO;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestScenePatchsuStatistics : OsuGameTestScene
    {
        private LocalProfileManager manager => Game.Dependencies.Get<LocalProfileManager>();
        private RealmAccess realm => Game.Dependencies.Get<RealmAccess>();

        private LocalProfile profile = null!;
        private BeatmapInfo[] beatmaps = null!;

        private LocalProfile current => manager.Store.GetProfile(profile.Id)!;

        private LocalProfileStatistics stats() => LocalProfileStatistics.CalculateAsync(current, new OsuRuleset().RulesetInfo, realm, Game.BeatmapManager, manager.Store, CancellationToken.None)
                                                                        .GetResultSafely();

        private double pp() => LocalProfileStatistics.CalculatePerformanceAsync(current, new OsuRuleset().RulesetInfo, realm, Game.BeatmapManager, manager.Store, CancellationToken.None)
                                                      .GetResultSafely();

        private ScoreInfo importScore(BeatmapInfo beatmap, Mod[] mods, double accuracy, int missCount)
        {
            var ruleset = new OsuRuleset();
            var playable = Game.BeatmapManager.GetWorkingBeatmap(beatmap).GetPlayableBeatmap(ruleset.RulesetInfo, mods);
            var processor = ruleset.CreateScoreProcessor();
            processor.ApplyBeatmap(playable);
            var max = processor.MaximumStatistics;
            var statistics = max.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value);
            statistics[HitResult.Great] -= missCount;
            statistics[HitResult.Miss] = missCount;

            return Game.ScoreManager.Import(new ScoreInfo
            {
                Hash = Guid.NewGuid().ToString(),
                BeatmapInfo = beatmap,
                BeatmapHash = beatmap.Hash,
                Ruleset = ruleset.RulesetInfo,
                User = new APIUser { Id = profile.UserId, Username = profile.Name },
                Mods = mods,
                Statistics = statistics,
                MaximumStatistics = new Dictionary<HitResult, int>(max),
                MaxCombo = max.Where(k => k.Key.AffectsCombo()).Sum(k => k.Value) / (missCount + 1),
                Accuracy = accuracy,
                TotalScore = (long)(1000000 * accuracy) - missCount * 1000,
                Rank = ScoreRank.A,
                Date = DateTimeOffset.Now,
            })!.Value.Detach();
        }

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("create profile", () =>
            {
                profile = manager.CreateDefaultProfile();
                manager.Switch(profile.Id);
            });
            AddStep("import beatmap", () => BeatmapImportHelper.LoadQuickOszIntoOsu(Game).WaitSafely());
            AddStep("mark ranked", () =>
            {
                realm.Write(r =>
                {
                    foreach (var b in r.All<BeatmapInfo>())
                        b.Status = BeatmapOnlineStatus.Ranked;
                });

                beatmaps = realm.Run(r => r.All<BeatmapInfo>().AsEnumerable().Where(b => b.Ruleset.ShortName == "osu").Detach().ToArray())
                                .Where(b => Game.BeatmapManager.GetWorkingBeatmap(b).GetPlayableBeatmap(new OsuRuleset().RulesetInfo).HitObjects.Count > 20)
                                .ToArray();
            });
            AddAssert("several osu! difficulties", () => beatmaps.Length, () => Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void TestNoPerformanceReasons()
        {
            ScoreInfo plain = null!, relax = null!, unrankedSetting = null!, classic = null!;

            AddStep("import scores", () =>
            {
                plain = importScore(beatmaps[0], new Mod[] { new OsuModHidden() }, 0.95, 2);
                relax = importScore(beatmaps[0], new Mod[] { new OsuModRelax() }, 0.99, 0);
                unrankedSetting = importScore(beatmaps[1], new Mod[] { new OsuModDoubleTime { SpeedChange = { Value = 1.4 } } }, 0.97, 1);
                classic = importScore(beatmaps[1], new Mod[] { new OsuModClassic() }, 0.9, 5);
            });

            string? reason(LocalProfileStatistics s, ScoreInfo score) => s.RecentScores.Single(e => e.ID == score.ID).NoPerformanceReason;

            // The rule as it was before mods were worked out once per combination (built from each score's own mods).
            string? oldReason(ScoreInfo score)
            {
                var p = current;
                var full = realm.Run(r => r.Find<ScoreInfo>(score.ID)!.Detach());

                if (!p.IncludeRelaxAutopilot && full.Mods.Any(LocalPriorityPolicy.IsRelaxOrAutopilot))
                    return @"PP is not awarded for Relax / Autopilot on this profile.";

                if (LocalPriorityPolicy.HasUnrankedMods(full, p.CountAllAsRanked, p.IncludeRelaxAutopilot, p.IgnoreUnrankedMods))
                    return @"PP is not awarded for unranked mods on this profile.";

                return null;
            }

            void checkAll(string name)
            {
                AddAssert($"{name}: same reasons as before", () =>
                {
                    var s = stats();
                    var mismatches = new[] { plain, relax, unrankedSetting, classic }
                                     .Where(score => reason(s, score) != oldReason(score))
                                     .Select(score => $"{string.Join(",", score.Mods.Select(m => m.Acronym))}: {reason(s, score)} / {oldReason(score)}")
                                     .ToArray();
                    if (mismatches.Length > 0)
                        Console.WriteLine(string.Join("\n", mismatches));
                    return mismatches.Length == 0;
                });
            }

            checkAll("default");
            AddAssert("HD counts, RX and DT 1.4 don't", () =>
            {
                var s = stats();
                return reason(s, plain) == null
                       && reason(s, relax) == @"PP is not awarded for Relax / Autopilot on this profile."
                       && reason(s, unrankedSetting) == @"PP is not awarded for unranked mods on this profile.";
            });

            AddStep("count relax", () => manager.SetRankingOptions(profile.Id, includeRelaxAutopilot: true));
            checkAll("relax");
            AddAssert("relax counts", () => reason(stats(), relax) == null);
            AddStep("ignore unranked mods", () => manager.SetRankingOptions(profile.Id, ignoreUnrankedMods: true));
            checkAll("ignore unranked");
            AddAssert("DT 1.4 counts", () => reason(stats(), unrankedSetting) == null);
            AddStep("count all as ranked", () => manager.SetRankingOptions(profile.Id, includeRelaxAutopilot: false, ignoreUnrankedMods: false, countAllAsRanked: true));
            checkAll("count all");
        }

        [Test]
        public void TestHistoryAndChange()
        {
            ScoreInfo first = null!, better = null!, second = null!;
            double ppAfterFirst = 0;

            AddStep("import first", () => first = importScore(beatmaps[0], Array.Empty<Mod>(), 0.9, 10));
            AddStep("remember PP", () => ppAfterFirst = pp());
            AddAssert("has PP", () => ppAfterFirst, () => Is.GreaterThan(0));

            AddStep("import second map", () => second = importScore(beatmaps[1], Array.Empty<Mod>(), 0.95, 3));
            AddStep("import better on first map", () => better = importScore(beatmaps[0], Array.Empty<Mod>(), 0.99, 0));

            AddAssert("history ends at the total", () =>
            {
                var s = stats();
                return s.History.Count == 3 && Math.Abs(s.History[^1].PP - s.PP) < 1e-9;
            });

            AddAssert("with/without the better score", () =>
            {
                var (without, with) = LocalProfileStatistics.CalculatePerformanceChangeAsync(current, new OsuRuleset().RulesetInfo, realm, Game.BeatmapManager, manager.Store, better.ID)
                                                            .GetResultSafely();
                double expectedWithout = LocalProfileStatistics.WeightedTotal(new[] { first, second }
                                                                              .Select(s => manager.Store.TryGetCachedPerformance(s.ID, out double? v) ? v!.Value : double.NaN)
                                                                              .OrderByDescending(v => v).ToArray());
                return Math.Abs(with - pp()) < 1e-9 && Math.Abs(without - expectedWithout) < 1e-9 && with > without;
            });
        }

        [Test]
        public void TestPerformanceRevision()
        {
            int revision = 0;

            AddUntilStep("settled", () =>
            {
                int now = manager.PerformanceRevision.Value;
                bool same = now == revision;
                revision = now;
                return same;
            });
            AddStep("rename", () => manager.Store.RenameProfile(profile.Id, "Renamed"));
            AddWaitStep("wait", 3);
            AddAssert("PP revision unchanged", () => manager.PerformanceRevision.Value, () => Is.EqualTo(revision));

            AddStep("count relax", () => manager.SetRankingOptions(profile.Id, includeRelaxAutopilot: true));
            AddUntilStep("PP revision bumped", () => manager.PerformanceRevision.Value > revision);

            AddStep("remember", () => revision = manager.PerformanceRevision.Value);
            AddStep("import score", () => importScore(beatmaps[0], Array.Empty<Mod>(), 0.9, 1));
            AddUntilStep("PP revision bumped by the score", () => manager.PerformanceRevision.Value > revision);
        }
    }
}

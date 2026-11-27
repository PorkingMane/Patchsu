// Patchsu 0009 local test (not part of the patch): the results screen and the leaderboard tooltip don't show a No Fail play's
// saved PP (it has No Fail in it) while "Remove 'No Fail' from PP calculation" is on.
// Copy into osu.Game.Tests/Visual/Navigation and run `dotnet test --filter TestSceneNoFailSavedPerformance`.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.LocalPriority;
using osu.Game.Overlays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Screens.Select;
using osu.Game.Tests.Beatmaps.IO;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneNoFailSavedPerformance : OsuGameTestScene
    {
        private const double saved_pp = 1;

        private ScoreInfo score = null!;
        private Container? container;

        [TestCase(true)]
        [TestCase(false)]
        public void TestSavedPerformance(bool ignoreNoFail)
        {
            PushAndConfirm(() => new SoloSongSelect());
            AddStep("import beatmap", () => BeatmapImportHelper.LoadQuickOszIntoOsu(Game).WaitSafely());
            AddUntilStep("wait for selected", () => !Game.Beatmap.IsDefault);
            AddStep("NF score with saved PP", () =>
            {
                var beatmap = Game.Beatmap.Value.BeatmapInfo;
                var ruleset = new OsuRuleset();
                var mods = new Mod[] { new OsuModNoFail() };
                var processor = ruleset.CreateScoreProcessor();
                processor.ApplyBeatmap(Game.BeatmapManager.GetWorkingBeatmap(beatmap).GetPlayableBeatmap(ruleset.RulesetInfo, mods));
                var max = processor.MaximumStatistics;
                var statistics = max.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value);
                statistics[HitResult.Great] -= 3;
                statistics[HitResult.Miss] = 3;

                score = new ScoreInfo
                {
                    BeatmapInfo = beatmap,
                    BeatmapHash = beatmap.Hash,
                    Ruleset = ruleset.RulesetInfo,
                    Mods = mods,
                    Statistics = statistics,
                    MaximumStatistics = new Dictionary<HitResult, int>(max),
                    MaxCombo = max.Where(k => k.Key.AffectsCombo()).Sum(k => k.Value) / 2,
                    Accuracy = 0.98,
                    TotalScore = 600000,
                    Rank = ScoreRank.A,
                    PP = saved_pp,
                    Date = DateTimeOffset.Now,
                };
            });
            AddStep($"ignore NF: {ignoreNoFail}", () => LocalNoFailPerformance.SetEnabled(ignoreNoFail));

            AddStep("show tooltip row and results PP", () =>
            {
                Game.Add(container = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new DependencyProvidingContainer
                        {
                            CachedDependencies = new (Type, object)[] { (typeof(OverlayColourProvider), new OverlayColourProvider(OverlayColourScheme.Aquamarine)) },
                            AutoSizeAxes = Axes.Both,
                            Child = new BeatmapLeaderboardScore.LeaderboardScoreTooltip.PerformanceStatisticRow("pp", score),
                        },
                        new PerformanceStatistic(score),
                    }
                });
            });

            if (ignoreNoFail)
            {
                AddUntilStep("tooltip calculated", () => tooltipPP(), () => Is.GreaterThan(saved_pp));
                AddUntilStep("results calculated", () => resultsPP(), () => Is.GreaterThan(saved_pp));
            }
            else
            {
                AddUntilStep("tooltip saved", () => tooltipPP(), () => Is.EqualTo(saved_pp));
                AddUntilStep("results saved", () => resultsPP(), () => Is.EqualTo(saved_pp));
            }

            AddStep("clean up", () =>
            {
                container?.Expire();
                LocalNoFailPerformance.SetEnabled(false);
            });
        }

        private double tooltipPP()
        {
            var row = container!.ChildrenOfType<BeatmapLeaderboardScore.LeaderboardScoreTooltip.PerformanceStatisticRow>().Single();
            string text = row.ChildrenOfType<SpriteText>().Last().Text.ToString();
            return double.Parse(text.Replace("pp", string.Empty).Replace(",", string.Empty));
        }

        private int resultsPP()
        {
            var statistic = container!.ChildrenOfType<PerformanceStatistic>().Single();
            // With 0004 the counter only rolls once the panel appears; the value it rolls to is kept in basePerformance.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (typeof(PerformanceStatistic).GetField("basePerformance", flags) is FieldInfo basePerformance)
                return (int)basePerformance.GetValue(statistic)!;

            return ((Bindable<int>)typeof(PerformanceStatistic).GetField("performance", flags)!.GetValue(statistic)!).Value;
        }
    }
}

// Patchsu 0012 local test (not part of the patch). Copy into osu.Game.Rulesets.Osu.Tests and run:
//   dotnet test osu.Game.Rulesets.Osu.Tests --filter NoFailPerformanceTest

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.LocalPriority;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    public class NoFailPerformanceTest
    {
        private static (ScoreInfo score, DifficultyAttributes attributes, PerformanceCalculator calculator) create(params Mod[] mods)
        {
            var ruleset = new OsuRuleset();
            var working = new TestWorkingBeatmap(new TestBeatmap(ruleset.RulesetInfo));
            var playable = working.GetPlayableBeatmap(ruleset.RulesetInfo, mods);
            var processor = ruleset.CreateScoreProcessor();
            processor.Mods.Value = mods;
            processor.ApplyBeatmap(playable);
            var max = processor.MaximumStatistics;
            var statistics = max.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value);
            statistics[HitResult.Great] -= 4;
            statistics[HitResult.Miss] = 4;

            var score = new ScoreInfo(working.BeatmapInfo, ruleset.RulesetInfo)
            {
                Mods = mods,
                Statistics = statistics,
                MaximumStatistics = new Dictionary<HitResult, int>(max),
                MaxCombo = max.Where(k => k.Key.AffectsCombo()).Sum(k => k.Value) / 3,
                Accuracy = 0.97,
            };

            return (score, ruleset.CreateDifficultyCalculator(working).Calculate(mods), ruleset.CreatePerformanceCalculator()!);
        }

        [Test]
        public async Task TestNoFailIgnoredOnlyWhenAsked()
        {
            var (nf, nfAttributes, calculator) = create(new OsuModNoFail(), new OsuModHidden());
            var (plain, plainAttributes, _) = create(new OsuModHidden());

            double withPenalty = (await LocalNoFailPerformance.RunWith(false, () => calculator.CalculateAsync(nf, nfAttributes, default))).Total;
            double ignored = (await LocalNoFailPerformance.RunWith(true, () => calculator.CalculateAsync(nf, nfAttributes, default))).Total;
            double ignoredSync = await LocalNoFailPerformance.RunWith(true, () => Task.FromResult(calculator.Calculate(nf, nfAttributes).Total));
            double withoutNf = calculator.Calculate(plain, plainAttributes).Total;

            TestContext.WriteLine($"NF={withPenalty} ignored={ignored} plain={withoutNf}");
            Assert.That(withPenalty, Is.LessThan(withoutNf));
            Assert.That(ignored, Is.EqualTo(withoutNf).Within(1e-9));
            Assert.That(ignoredSync, Is.EqualTo(withoutNf).Within(1e-9));

            // The play itself keeps No Fail.
            Assert.That(nf.Mods.Any(m => m is OsuModNoFail));
            // Outside RunWith the (default off) setting applies.
            Assert.That(calculator.Calculate(nf, nfAttributes).Total, Is.EqualTo(withPenalty).Within(1e-9));
        }

        [Test]
        public void TestNoFailScoreReductionOnlyWhenAsked()
        {
            var ruleset = new OsuRuleset().RulesetInfo;
            var nf = new ScoreInfo(null, ruleset) { Mods = new Mod[] { new OsuModNoFail() }, TotalScore = 500000 };
            var plain = new ScoreInfo(null, ruleset) { Mods = new Mod[] { new OsuModHidden() }, TotalScore = 600000 };

            try
            {
                Assert.That(nf.GetDisplayScore(ScoringMode.Standardised), Is.EqualTo(500000));

                LocalNoFailPerformance.SetEnabled(true);
                Assert.That(nf.GetDisplayScore(ScoringMode.Standardised), Is.EqualTo(500000), "the PP option alone keeps the score");

                LocalNoFailPerformance.SetScoreEnabled(true);
                Assert.That(nf.GetDisplayScore(ScoringMode.Standardised), Is.EqualTo(1000000));
                Assert.That(plain.GetDisplayScore(ScoringMode.Standardised), Is.EqualTo(600000));
                Assert.That(new[] { plain, nf }.OrderByTotalScore().First(), Is.SameAs(nf));
                Assert.That(nf.TotalScore, Is.EqualTo(500000), "the play itself is unchanged");

                LocalNoFailPerformance.SetEnabled(false);
                Assert.That(nf.GetDisplayScore(ScoringMode.Standardised), Is.EqualTo(500000), "needs the PP option");
            }
            finally
            {
                LocalNoFailPerformance.SetScoreEnabled(false);
                LocalNoFailPerformance.SetEnabled(false);
            }
        }
    }
}

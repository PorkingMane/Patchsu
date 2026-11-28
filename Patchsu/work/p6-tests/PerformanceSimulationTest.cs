// Patchsu local test (not part of the patch). Copy into osu.Game.Rulesets.Osu.Tests and run:
//   dotnet test osu.Game.Rulesets.Osu.Tests --filter PerformanceSimulationTest

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Simulation;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    public class PerformanceSimulationTest
    {
        private PerformanceSimulation sim = null!;

        private PerformanceSimulation create(params Mod[] mods)
        {
            var ruleset = new OsuRuleset();
            var working = new TestWorkingBeatmap(new TestBeatmap(ruleset.RulesetInfo));
            var playable = working.GetPlayableBeatmap(ruleset.RulesetInfo, mods);

            var processor = ruleset.CreateScoreProcessor();
            processor.Mods.Value = mods;
            processor.ApplyBeatmap(playable);
            var maximum = processor.MaximumStatistics;

            var attributes = ruleset.CreateDifficultyCalculator(working).Calculate(mods);

            var template = new ScoreInfo(working.BeatmapInfo, ruleset.RulesetInfo)
            {
                Mods = mods,
                Statistics = maximum.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value),
                MaximumStatistics = new Dictionary<HitResult, int>(maximum),
                MaxCombo = maximum.Where(k => k.Key.AffectsCombo()).Sum(k => k.Value),
            };

            return new PerformanceSimulation(template, maximum, attributes, false);
        }

        [SetUp]
        public void SetUp() => sim = create();

        [Test]
        public void TestStartsAtSS()
        {
            TestContext.WriteLine($"objects={sim.BasicTotal} combo={sim.MaximumCombo} ticks={sim.GetMaximum(HitResult.LargeTickHit)} ends={sim.GetMaximum(HitResult.SliderTailHit)} pp={sim.Performance} score={sim.Score.TotalScore}");
            Assert.That(sim.Accuracy, Is.EqualTo(1));
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo));
            Assert.That(sim.Score.Rank, Is.EqualTo(ScoreRank.X));
            Assert.That(sim.Performance, Is.GreaterThan(0));
            Assert.That(sim.Score.TotalScore, Is.EqualTo(1000000));
            Assert.That(sim.TickResults, Does.Contain(HitResult.LargeTickHit));
            Assert.That(sim.TickResults, Does.Contain(HitResult.SliderTailHit));
            Assert.That(sim.IsModified, Is.False);
        }

        [Test]
        public void TestOksMoveGreatsNotCombo()
        {
            double pp = sim.Performance!.Value;
            sim.SetCount(HitResult.Ok, 5);
            Assert.That(sim.Get(HitResult.Great), Is.EqualTo(sim.BasicTotal - 5));
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo));
            Assert.That(sim.Accuracy, Is.LessThan(1));
            Assert.That(sim.Performance, Is.LessThan(pp));
            Assert.That(sim.IsModified, Is.True);

            // more than possible clamps
            Assert.That(sim.SetCount(HitResult.Meh, 100000), Is.EqualTo(sim.BasicTotal - 5));
            Assert.That(sim.Get(HitResult.Great), Is.EqualTo(0));
        }

        [Test]
        public void TestMissesLimitCombo()
        {
            sim.SetCount(HitResult.Miss, 3);
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo - 3));
            Assert.That(sim.Get(HitResult.Great), Is.EqualTo(sim.BasicTotal - 3));
            Assert.That(sim.Score.Rank, Is.EqualTo(ScoreRank.A).Or.LessThan(ScoreRank.A));

            sim.SetCount(HitResult.Miss, 0);
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo), "combo at the cap follows it back up");

            sim.SetCombo(10);
            sim.SetCount(HitResult.Miss, 1);
            Assert.That(sim.Combo, Is.EqualTo(10), "a lower combo stays");
        }

        [Test]
        public void TestFixingChokeRestoresCombo()
        {
            int ends = sim.GetMaximum(HitResult.SliderTailHit);

            // A choke: 2 misses, a missed tick, 3 dropped slider ends and a combo well below the limit.
            sim.SetCount(HitResult.Miss, 2);
            sim.SetCount(HitResult.LargeTickHit, sim.GetMaximum(HitResult.LargeTickHit) - 1);
            sim.SetCount(HitResult.SliderTailHit, ends - 3);
            sim.SetCombo(sim.MaximumCombo / 3);
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo / 3));

            // Still a missed tick: the combo stays where it was.
            sim.SetCount(HitResult.Miss, 0);
            Assert.That(sim.HasNoComboBreaks, Is.False);
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo / 3));

            // Last break gone: combo = max minus the dropped slider ends.
            sim.SetCount(HitResult.LargeTickHit, sim.GetMaximum(HitResult.LargeTickHit));
            Assert.That(sim.HasNoComboBreaks, Is.True);
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo - 3));
            Assert.That(sim.Combo, Is.EqualTo(sim.ComboCap));

            // Other edits without breaks keep it at the limit; dropping another end lowers it by 1.
            sim.SetCount(HitResult.Ok, 4);
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo - 3));
            sim.SetCount(HitResult.SliderTailHit, ends - 4);
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo - 4));

            // Typing a lower combo by hand is still allowed.
            Assert.That(sim.SetCombo(100), Is.EqualTo(100));
        }

        [Test]
        public void TestFixingMissesOnlyChoke()
        {
            sim.SetCount(HitResult.Miss, 1);
            sim.SetCombo(50);
            sim.SetCount(HitResult.Miss, 0);
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo));
        }

        [Test]
        public void TestTicksAndEnds()
        {
            int ticks = sim.GetMaximum(HitResult.LargeTickHit);
            sim.SetCount(HitResult.LargeTickHit, ticks - 1);
            Assert.That(sim.Get(HitResult.LargeTickMiss), Is.EqualTo(1));
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo - 2));
            Assert.That(sim.Accuracy, Is.LessThan(1));
            Assert.That(sim.Get(HitResult.Great), Is.EqualTo(sim.BasicTotal));

            int ends = sim.GetMaximum(HitResult.SliderTailHit);
            sim.SetCount(HitResult.SliderTailHit, ends - 2);
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo - 4));

            Assert.That(sim.SetCombo(sim.MaximumCombo), Is.EqualTo(sim.MaximumCombo - 4));
        }

        [Test]
        public void TestAccuracyTarget()
        {
            sim.SetCount(HitResult.Miss, 2);
            sim.SetAccuracy(0.95);
            TestContext.WriteLine($"acc={sim.Accuracy} 300={sim.Get(HitResult.Great)} 100={sim.Get(HitResult.Ok)} 50={sim.Get(HitResult.Meh)}");
            Assert.That(sim.Accuracy, Is.EqualTo(0.95).Within(0.005));
            Assert.That(sim.Get(HitResult.Miss), Is.EqualTo(2));
            Assert.That(sim.Get(HitResult.Meh), Is.LessThanOrEqualTo(3));
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo - 2));

            sim.SetAccuracy(0.2);
            TestContext.WriteLine($"acc={sim.Accuracy} 300={sim.Get(HitResult.Great)} 100={sim.Get(HitResult.Ok)} 50={sim.Get(HitResult.Meh)}");
            Assert.That(sim.Get(HitResult.Great), Is.EqualTo(0));
            Assert.That(sim.Get(HitResult.Meh), Is.GreaterThan(0));
        }

        [TestCase(0.9)]
        [TestCase(0.6)]
        public void TestPerformanceTarget(double fraction)
        {
            sim.SetCount(HitResult.Miss, 1);
            int combo = sim.Combo;
            double target = sim.Performance!.Value * fraction;

            sim.SetPerformance(target);
            TestContext.WriteLine($"target={target} pp={sim.Performance} acc={sim.Accuracy} 300={sim.Get(HitResult.Great)} 100={sim.Get(HitResult.Ok)} 50={sim.Get(HitResult.Meh)}");
            Assert.That(sim.Performance, Is.EqualTo(target).Within(target * 0.03 + 1));
            Assert.That(sim.Get(HitResult.Miss), Is.EqualTo(1));
            Assert.That(sim.Combo, Is.EqualTo(combo));

            // out of reach even without misses: every miss goes.
            sim.SetPerformance(100000);
            Assert.That(sim.Get(HitResult.Ok), Is.EqualTo(0));
            Assert.That(sim.Get(HitResult.Miss), Is.EqualTo(0));
            Assert.That(sim.Get(HitResult.Great), Is.EqualTo(sim.BasicTotal));
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo));
        }

        [Test]
        public void TestTargetRemovesFewestMisses()
        {
            double full = sim.Performance!.Value;
            sim.SetCount(HitResult.Miss, 20);
            sim.SetCombo(sim.ComboCap / 3);
            double withMisses = sim.Performance!.Value;
            double target = withMisses + (full - withMisses) / 3;

            sim.SetPerformance(target);
            int misses = sim.Get(HitResult.Miss);
            TestContext.WriteLine($"full={full} start={withMisses} target={target} pp={sim.Performance} miss={misses} 300={sim.Get(HitResult.Great)} 100={sim.Get(HitResult.Ok)} 50={sim.Get(HitResult.Meh)} combo={sim.Combo}");
            Assert.That(misses, Is.InRange(1, 19));
            Assert.That(sim.Performance, Is.EqualTo(target).Within(target * 0.03 + 1));

            // one more miss can't reach the target any more.
            sim.SetCount(HitResult.Miss, misses + 1);
            sim.SetCount(HitResult.Ok, 0);
            sim.SetCount(HitResult.Meh, 0);
            Assert.That(sim.Performance, Is.LessThan(target));

            // reachable with the misses: they stay.
            sim.SetPerformance(sim.Performance!.Value * 0.8);
            Assert.That(sim.Get(HitResult.Miss), Is.EqualTo(misses + 1));
        }

        [Test]
        public void TestAccuracyMixesOksAndMehs()
        {
            // steps of 50 points: 100s alone are 4 steps apart, the mix gets within one step (every step from 12 on can be made).
            for (int i = 12; i <= 40; i++)
            {
                double target = 1 - i * 50.0 / 300 / sim.BasicTotal;
                sim.SetAccuracy(target);
                Assert.That(sim.Accuracy, Is.EqualTo(target).Within(50.0 / 300 / sim.BasicTotal), $"step {i}");
                Assert.That(sim.Get(HitResult.Meh), Is.LessThanOrEqualTo(3));
                Assert.That(sim.Get(HitResult.Great) + sim.Get(HitResult.Ok) + sim.Get(HitResult.Meh), Is.EqualTo(sim.BasicTotal));
            }
        }

        [Test]
        public void TestReset()
        {
            sim.SetCount(HitResult.Miss, 4);
            sim.SetCount(HitResult.LargeTickHit, 0);
            sim.Reset();
            Assert.That(sim.IsModified, Is.False);
            Assert.That(sim.Accuracy, Is.EqualTo(1));
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestFixChokeButton(bool missesBecomeGreat)
        {
            int ends = sim.GetMaximum(HitResult.SliderTailHit);
            sim.SetCount(HitResult.Ok, 3);
            sim.SetCount(HitResult.Miss, 2);
            sim.SetCount(HitResult.LargeTickHit, sim.GetMaximum(HitResult.LargeTickHit) - 1);
            sim.SetCount(HitResult.SliderTailHit, ends - 1);
            sim.SetCombo(10);
            Assert.That(sim.IsChokeFixed, Is.False);

            sim.FixChoke(missesBecomeGreat);

            Assert.That(sim.IsChokeFixed, Is.True);
            Assert.That(sim.Get(HitResult.Miss), Is.EqualTo(0));
            Assert.That(sim.Get(HitResult.Ok), Is.EqualTo(missesBecomeGreat ? 3 : 5), missesBecomeGreat ? "misses became 300s" : "misses became 100s");
            Assert.That(sim.Get(HitResult.Great), Is.EqualTo(sim.BasicTotal - (missesBecomeGreat ? 3 : 5)));
            Assert.That(sim.Get(HitResult.LargeTickHit), Is.EqualTo(sim.GetMaximum(HitResult.LargeTickHit)));
            Assert.That(sim.Get(HitResult.SliderTailHit), Is.EqualTo(ends - 1), "slider ends stay");
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo - 1), "full combo minus the dropped slider end");
            Assert.That(sim.Combo, Is.EqualTo(sim.ComboCap));
            Assert.That(sim.IsPerfect, Is.False);

            sim.SetCombo(5);
            Assert.That(sim.IsChokeFixed, Is.False, "a lower combo is still a choke");
        }

        [Test]
        public void TestSetPerfect()
        {
            sim.SetCount(HitResult.Miss, 3);
            sim.SetCount(HitResult.Ok, 5);
            sim.SetCount(HitResult.LargeTickHit, 1);
            sim.SetCount(HitResult.SliderTailHit, 0);
            Assert.That(sim.IsPerfect, Is.False);

            sim.SetPerfect();
            Assert.That(sim.IsPerfect, Is.True);
            Assert.That(sim.Accuracy, Is.EqualTo(1));
            Assert.That(sim.Combo, Is.EqualTo(sim.MaximumCombo));
            Assert.That(sim.Get(HitResult.Miss), Is.Zero);
            Assert.That(sim.Get(HitResult.LargeTickMiss), Is.Zero);
            Assert.That(sim.Get(HitResult.SliderTailHit), Is.EqualTo(sim.GetMaximum(HitResult.SliderTailHit)));
        }

        [Test]
        public void TestCopyFromOtherMods()
        {
            sim.SetCount(HitResult.Miss, 2);
            sim.SetCount(HitResult.Ok, 7);
            sim.SetCount(HitResult.Meh, 1);
            sim.SetCount(HitResult.LargeTickHit, sim.GetMaximum(HitResult.LargeTickHit) - 1);
            sim.SetCombo(sim.ComboCap / 2);

            var dt = create(new OsuModDoubleTime());
            dt.CopyFrom(sim);

            Assert.That(dt.Get(HitResult.Miss), Is.EqualTo(2));
            Assert.That(dt.Get(HitResult.Ok), Is.EqualTo(7));
            Assert.That(dt.Get(HitResult.Meh), Is.EqualTo(1));
            Assert.That(dt.Get(HitResult.LargeTickHit), Is.EqualTo(sim.Get(HitResult.LargeTickHit)));
            Assert.That(dt.Combo, Is.EqualTo(sim.Combo));
            Assert.That(dt.Accuracy, Is.EqualTo(sim.Accuracy).Within(1e-9));
            Assert.That(dt.Performance, Is.GreaterThan(sim.Performance));

            // a combo at the cap stays at the cap.
            sim.SetCombo(int.MaxValue);
            dt.CopyFrom(sim);
            Assert.That(dt.Combo, Is.EqualTo(dt.ComboCap));
        }

        [Test]
        public void TestWithClassicAndHidden()
        {
            sim = create(new OsuModClassic(), new OsuModHidden());
            TestContext.WriteLine(string.Join(", ", sim.TickResults) + $" rank={sim.Score.Rank} pp={sim.Performance}");
            Assert.That(sim.Score.Rank, Is.EqualTo(ScoreRank.XH));
            sim.SetCount(HitResult.Ok, 3);
            Assert.That(sim.Score.Rank, Is.EqualTo(ScoreRank.SH));
        }
    }
}

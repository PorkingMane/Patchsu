// Patchsu test (0004): pauses before the first hit object, in breaks or after the last hit object don't cost PP.

using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Timing;
using osu.Game.LocalPriority;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class PausePenaltyFreePausesTest
    {
        private static Beatmap createBeatmap()
        {
            var beatmap = new Beatmap();

            beatmap.HitObjects.Add(new HitObject { StartTime = 1000 });
            beatmap.HitObjects.Add(new HitObject { StartTime = 2000 });
            beatmap.HitObjects.Add(new HitObject { StartTime = 10000 });
            beatmap.HitObjects.Add(new HitObject { StartTime = 11000 });

            beatmap.Breaks.Add(new BreakPeriod(2500, 9500));

            return beatmap;
        }

        [Test]
        public void TestLeadInIsFree() => Assert.That(LocalPausePenalty.CountPenalisedPauses(new[] { -1500, 0, 999 }, createBeatmap()), Is.EqualTo(0));

        [Test]
        public void TestBreakIsFree() => Assert.That(LocalPausePenalty.CountPenalisedPauses(new[] { 3000, 6000, 8000 }, createBeatmap()), Is.EqualTo(0));

        [Test]
        public void TestAfterLastObjectIsFree() => Assert.That(LocalPausePenalty.CountPenalisedPauses(new[] { 11000, 12500 }, createBeatmap()), Is.EqualTo(0));

        [Test]
        public void TestGameplayPausesCount() => Assert.That(LocalPausePenalty.CountPenalisedPauses(new[] { 1000, 1500, 2400, 10500 }, createBeatmap()), Is.EqualTo(4));

        [Test]
        public void TestBreakFadeOutCounts()
        {
            // The last part of a break (while it fades out) is gameplay again, like BreakTracker.
            Assert.That(LocalPausePenalty.CountPenalisedPauses(new[] { 9400 }, createBeatmap()), Is.EqualTo(1));
        }

        [Test]
        public void TestShortBreakCounts()
        {
            var beatmap = createBeatmap();
            beatmap.Breaks.Clear();
            beatmap.Breaks.Add(new BreakPeriod(2100, 2600));

            Assert.That(LocalPausePenalty.CountPenalisedPauses(new[] { 2300 }, beatmap), Is.EqualTo(1));
        }

        [Test]
        public void TestNoObjectsCountsEverything() => Assert.That(LocalPausePenalty.CountPenalisedPauses(new[] { 0, 5000 }, new Beatmap()), Is.EqualTo(2));
    }
}

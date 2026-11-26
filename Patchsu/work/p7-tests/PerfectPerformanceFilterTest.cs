// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Patchsu 0007 tests (not part of the patch). Copy into osu.Game.Tests/Visual/SongSelect.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Graphics.Carousel;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.SongSelect
{
    [TestFixture]
    public class PerfectPerformanceFilterTest
    {
        private List<BeatmapInfo> beatmaps = null!;
        private Dictionary<Guid, PerfectPerformanceValue?> values = null!;

        [SetUp]
        public void SetUp()
        {
            beatmaps = Enumerable.Range(0, 5).Select(_ => TestResources.CreateTestBeatmapSetInfo(1).Beatmaps.Single()).ToList();

            // [0] 150 pp, 4* 60s | [1] 420 pp, 6* 120s | [2] unavailable | [3] 90 pp, 2* 30s | [4] 1000 pp, 8* 240s
            values = new Dictionary<Guid, PerfectPerformanceValue?>
            {
                [beatmaps[0].ID] = new PerfectPerformanceValue(150, 4),
                [beatmaps[1].ID] = new PerfectPerformanceValue(420, 6),
                [beatmaps[2].ID] = null,
                [beatmaps[3].ID] = new PerfectPerformanceValue(90, 2),
                [beatmaps[4].ID] = new PerfectPerformanceValue(1000, 8),
            };

            beatmaps[0].Length = 60_000;
            beatmaps[1].Length = 120_000;
            beatmaps[2].Length = 60_000;
            beatmaps[3].Length = 30_000;
            beatmaps[4].Length = 240_000;
        }

        private PerfectPerformanceSession createSession(bool allowCalculations = false, Func<BeatmapInfo, PerfectPerformanceValue?>? calculate = null, Action<PerfectPerformanceSession>? changed = null)
            => new PerfectPerformanceSession(b => allowCalculations ? null : values[b.ID], (b, _) => calculate?.Invoke(b), () => { }, changed ?? (_ => { }), allowCalculations);

        private IEnumerable<CarouselItem> items => beatmaps.Select(b => new CarouselItem(b));

        [Test]
        public async Task TestSortByPerformance()
        {
            var criteria = new FilterCriteria { Sort = SortMode.PerformancePoints, PerformanceSession = createSession() };
            var result = (await new BeatmapCarouselFilterSorting(() => criteria).Run(items, CancellationToken.None)).Select(i => (BeatmapInfo)i.Model).ToList();

            Assert.That(result, Is.EqualTo(new[] { beatmaps[4], beatmaps[1], beatmaps[0], beatmaps[3], beatmaps[2] }));
        }

        [Test]
        public async Task TestSortByFarmability()
        {
            // 1000 * pp / seconds / stars^2: [0] 1000*150/60/16 = 156.25, [1] 1000*420/120/36 = 97.2, [3] 1000*90/30/4 = 750, [4] 1000*1000/240/64 = 65.1
            var criteria = new FilterCriteria { Sort = SortMode.Farmability, PerformanceSession = createSession() };
            var result = (await new BeatmapCarouselFilterSorting(() => criteria).Run(items, CancellationToken.None)).Select(i => (BeatmapInfo)i.Model).ToList();

            Assert.That(result, Is.EqualTo(new[] { beatmaps[3], beatmaps[0], beatmaps[1], beatmaps[4], beatmaps[2] }));
        }

        [Test]
        public void TestPerformanceSortsSplitSets()
        {
            Assert.That(BeatmapCarouselFilterGrouping.ShouldGroupBeatmapsTogether(new FilterCriteria { Sort = SortMode.PerformancePoints }), Is.False);
            Assert.That(BeatmapCarouselFilterGrouping.ShouldGroupBeatmapsTogether(new FilterCriteria { Sort = SortMode.Farmability }), Is.False);
            Assert.That(BeatmapCarouselFilterGrouping.ShouldGroupBeatmapsTogether(new FilterCriteria { Group = GroupMode.PerformancePoints }), Is.False);
        }

        [Test]
        public async Task TestGroupByPerformance()
        {
            var criteria = new FilterCriteria { Group = GroupMode.PerformancePoints, PerformanceGroupInterval = 200, PerformanceSession = createSession() };
            var grouping = new BeatmapCarouselFilterGrouping
            {
                GetCriteria = () => criteria,
                GetCollections = () => new List<BeatmapCollection>(),
                GetLocalUserTopRanks = _ => new Dictionary<Guid, ScoreRank>(),
                GetFavouriteBeatmapSets = () => new HashSet<int>(),
            };

            var result = await grouping.Run(items.ToList(), CancellationToken.None);
            var groups = result.Select(i => i.Model).OfType<GroupDefinition>().Select(g => g.Title.ToString()).ToList();

            Assert.That(groups, Is.EqualTo(new[] { "0 - 200 pp", "400 - 600 pp", $"800 - {1000:N0} pp", "No PP" }));
            Assert.That(grouping.GroupItems.Single(g => g.Key.Title.ToString() == "0 - 200 pp").Value.Select(i => i.Model).OfType<GroupedBeatmap>().Select(b => b.Beatmap),
                Is.EquivalentTo(new[] { beatmaps[0], beatmaps[3] }));
        }

        [Test]
        public async Task TestGroupByPerformanceWithCap()
        {
            values[beatmaps[2].ID] = new PerfectPerformanceValue(1850, 9);

            var criteria = new FilterCriteria { Group = GroupMode.PerformancePoints, PerformanceGroupInterval = 200, PerformanceGroupCap = 1000, PerformanceSession = createSession() };
            var grouping = new BeatmapCarouselFilterGrouping
            {
                GetCriteria = () => criteria,
                GetCollections = () => new List<BeatmapCollection>(),
                GetLocalUserTopRanks = _ => new Dictionary<Guid, ScoreRank>(),
                GetFavouriteBeatmapSets = () => new HashSet<int>(),
            };

            var result = await grouping.Run(items.ToList(), CancellationToken.None);
            var groups = result.Select(i => i.Model).OfType<GroupDefinition>().Select(g => g.Title.ToString()).ToList();

            // exactly 1,000 pp is still in the last group below the cap.
            Assert.That(groups, Is.EqualTo(new[] { "0 - 200 pp", "400 - 600 pp", $"800 - {1000:N0} pp", $"{1000:N0}+ pp" }));

            criteria.PerformanceGroupCap = null;
            result = await grouping.Run(items.ToList(), CancellationToken.None);
            groups = result.Select(i => i.Model).OfType<GroupDefinition>().Select(g => g.Title.ToString()).ToList();
            Assert.That(groups, Does.Contain($"{1800:N0} - {2000:N0} pp"));
        }

        [Test]
        public async Task TestPerformanceRangeFilter()
        {
            var criteria = new FilterCriteria { PerformanceSession = createSession() };
            criteria.UserPerformance.Min = 100;
            criteria.UserPerformance.Max = 500;

            var result = (await new BeatmapCarouselFilterMatching(() => criteria).Run(items, CancellationToken.None)).Select(i => (BeatmapInfo)i.Model).ToList();

            Assert.That(result, Is.EquivalentTo(new[] { beatmaps[0], beatmaps[1] }));
        }

        [Test]
        public async Task TestPendingBeatmapsStayUntilCalculated()
        {
            var calculated = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            int changes = 0;

            var session = createSession(true, b =>
            {
                release.Wait(5000);
                return values[b.ID];
            }, _ =>
            {
                Interlocked.Increment(ref changes);
                calculated.Set();
            });

            var criteria = new FilterCriteria { PerformanceSession = session };
            criteria.UserPerformance.Max = 500;

            var result = await new BeatmapCarouselFilterMatching(() => criteria).Run(items, CancellationToken.None);
            Assert.That(result.Count, Is.EqualTo(5), "pending beatmaps should stay");
            Assert.That(session.Progress, Is.EqualTo((0, 5)));

            release.Set();
            Assert.That(SpinWait.SpinUntil(() => session.Progress.completed == 5, 5000), Is.True);

            result = await new BeatmapCarouselFilterMatching(() => criteria).Run(items, CancellationToken.None);
            Assert.That(result.Select(i => (BeatmapInfo)i.Model), Is.EquivalentTo(new[] { beatmaps[0], beatmaps[1], beatmaps[3] }));
            Assert.That(changes, Is.GreaterThan(0));
            session.Dispose();
        }

        [Test]
        public void TestCalculationsOffUsesOnlyCache()
        {
            bool calculateCalled = false;
            var session = new PerfectPerformanceSession(_ => null, (_, _) =>
            {
                calculateCalled = true;
                return new PerfectPerformanceValue(1, 1);
            }, () => { }, _ => { }, allowCalculations: false);

            Assert.That(session.Get(beatmaps[0]).State, Is.EqualTo(PerfectPerformanceState.Unavailable));
            Assert.That(calculateCalled, Is.False);
        }

        [Test]
        public void TestFarmability()
        {
            Assert.That(BeatmapCarouselFilterSorting.CalculateFarmability(100, 50, 2), Is.EqualTo(500).Within(1e-9));
            Assert.That(BeatmapCarouselFilterSorting.CalculateFarmability(100, 0, 2), Is.Null);
            Assert.That(BeatmapCarouselFilterSorting.CalculateFarmability(100, 50, 0), Is.Null);
        }
    }
}

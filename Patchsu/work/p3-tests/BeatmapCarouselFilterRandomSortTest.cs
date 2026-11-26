// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.SongSelect
{
    /// <summary>
    /// Patchsu 0003: "Sort by Random".
    /// </summary>
    [TestFixture]
    public class BeatmapCarouselFilterRandomSortTest
    {
        private List<BeatmapSetInfo> sets = null!;
        private FilterCriteria criteria = null!;
        private BeatmapCarouselFilterSorting sorter = null!;

        [SetUp]
        public void SetUp()
        {
            sets = Enumerable.Range(0, 50).Select(_ => TestResources.CreateTestBeatmapSetInfo(4)).ToList();
            criteria = new FilterCriteria { Sort = SortMode.Random };
            sorter = new BeatmapCarouselFilterSorting(() => criteria);
        }

        [Test]
        public async Task TestSetsShuffledAndKeptTogether()
        {
            var result = await run(sets);

            var setOrder = result.Select(b => b.BeatmapSet!).Distinct().ToList();
            Assert.That(setOrder, Has.Count.EqualTo(sets.Count), "difficulties of a set must stay together");

            foreach (var set in setOrder)
                Assert.That(result.Where(b => b.BeatmapSet == set).Select(b => b.StarRating), Is.Ordered, "difficulties stay ordered by stars");

            var byTitle = (await runWith(SortMode.Title, sets)).Select(b => b.BeatmapSet!).Distinct();
            Assert.That(setOrder, Is.Not.EqualTo(byTitle));
            Assert.That(setOrder, Is.Not.EqualTo(sets));
        }

        [Test]
        public async Task TestStableWhileSelected()
        {
            var first = (await run(sets)).ToList();
            var second = (await run(sets)).ToList();
            Assert.That(second, Is.EqualTo(first));

            // Filtering to a subset keeps the relative order.
            var subset = sets.Where((_, i) => i % 3 == 0).ToList();
            var filtered = (await run(subset)).ToList();
            Assert.That(filtered, Is.EqualTo(first.Where(b => subset.Contains(b.BeatmapSet!)).ToList()));
        }

        [Test]
        public async Task TestReshuffledWhenChosenAgain()
        {
            var first = (await run(sets)).ToList();

            criteria = new FilterCriteria { Sort = SortMode.Title };
            await run(sets);

            criteria = new FilterCriteria { Sort = SortMode.Random };
            var again = (await run(sets)).ToList();

            Assert.That(again, Is.Not.EqualTo(first));
        }

        [Test]
        public async Task TestDifficultiesShuffledWhenNotGrouped()
        {
            criteria = new FilterCriteria { Sort = SortMode.Random, Group = GroupMode.Difficulty };
            var result = (await run(sets)).ToList();

            int setChanges = result.Zip(result.Skip(1)).Count(p => p.First.BeatmapSet != p.Second.BeatmapSet);
            Assert.That(setChanges, Is.GreaterThan(sets.Count * 2), "single difficulties should be spread out");
        }

        private async Task<List<BeatmapInfo>> runWith(SortMode sort, List<BeatmapSetInfo> input)
        {
            var other = new BeatmapCarouselFilterSorting(() => new FilterCriteria { Sort = sort });
            return (await other.Run(input.SelectMany(s => s.Beatmaps.Select(b => new CarouselItem(b))), CancellationToken.None))
                   .Select(ci => ci.Model).OfType<BeatmapInfo>().ToList();
        }

        private async Task<List<BeatmapInfo>> run(List<BeatmapSetInfo> input)
            => (await sorter.Run(input.SelectMany(s => s.Beatmaps.Select(b => new CarouselItem(b))), CancellationToken.None))
               .Select(ci => ci.Model).OfType<BeatmapInfo>().ToList();
    }
}

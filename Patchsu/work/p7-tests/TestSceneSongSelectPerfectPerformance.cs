// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Patchsu 0007 tests (not part of the patch). Copy into osu.Game.Tests/Visual/SongSelect.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Graphics.Carousel;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.LocalPriority;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneSongSelectPerfectPerformance : SongSelectTestScene
    {
        [Test]
        public void TestSortAndGroupByPerfectPerformance()
        {
            ImportBeatmapForRuleset(0);
            ImportBeatmapForRuleset(0);
            LoadSongSelect();

            SortAndGroupBy(SortMode.PerformancePoints, GroupMode.PerformancePoints);
            WaitForFiltering();

            AddUntilStep("calculation finished", () => Carousel.PerformanceProgressText == null);
            AddUntilStep("status shows matches", () => SongSelect.ChildrenOfType<FilterControl>().Single().StatusText.ToString().Contains('6'));
            AddUntilStep("no pending group", () => !Carousel.Filters.OfType<BeatmapCarouselFilterGrouping>().Single().GroupItems.Keys.Any(g => g.Title.ToString().StartsWith("Calculating", System.StringComparison.Ordinal)));
            AddAssert("difficulties split", () => !Carousel.Filters.OfType<BeatmapCarouselFilterGrouping>().Single().BeatmapSetsGroupedTogether);

            ChangeMods(new OsuModDoubleTime());
            WaitForFiltering();
            AddUntilStep("calculation finished with DT", () => Carousel.PerformanceProgressText == null);

            SortBy(SortMode.Farmability);
            WaitForFiltering();
            AddUntilStep("farmability calculation finished", () => Carousel.PerformanceProgressText == null);

            AddStep("turn PP calc off", () => PatchsuSettings.Current.GetBindable(PerfectPerformanceSettings.SECTION, "SongSelectCalculatePerformance", true).Value = false);
            WaitForFiltering();
            AddAssert("nothing pending", () => Carousel.PerformanceProgressText == null);
            AddStep("turn PP calc on", () => PatchsuSettings.Current.GetBindable(PerfectPerformanceSettings.SECTION, "SongSelectCalculatePerformance", true).Value = true);
        }
    }
}

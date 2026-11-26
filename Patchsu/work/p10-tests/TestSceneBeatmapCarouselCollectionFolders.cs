// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Collections;
using osu.Game.Graphics.Carousel;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Tests.Visual.SongSelect
{
    /// <summary>
    /// Patchsu patch 10: nested collection folders in "Group by: Collections".
    /// </summary>
    [TestFixture]
    public partial class TestSceneBeatmapCarouselCollectionFolders : BeatmapCarouselTestScene
    {
        private List<BeatmapCollection> collections = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            RemoveAllBeatmaps();
            CreateCarousel();

            AddBeatmaps(10, 3);

            AddStep("set up collections", () =>
            {
                collections =
                [
                    new BeatmapCollection("alpha", [.. BeatmapSets[0].Beatmaps.Select(b => b.MD5Hash), .. BeatmapSets[1].Beatmaps.Select(b => b.MD5Hash)]),
                    new BeatmapCollection("alpha/beta", [.. BeatmapSets[2].Beatmaps.Select(b => b.MD5Hash)]),
                    new BeatmapCollection("alpha / beta / gamma", [BeatmapSets[3].Beatmaps[0].MD5Hash]),
                    // "delta" itself isn't a collection.
                    new BeatmapCollection("delta/epsilon", [.. BeatmapSets[4].Beatmaps.Select(b => b.MD5Hash)]),
                    // empty collections stay hidden, like vanilla.
                    new BeatmapCollection("empty"),
                ];
                Carousel.AllCollections = () => collections;
            });

            SortAndGroupBy(SortMode.Title, GroupMode.Collections);
            WaitForDrawablePanels();
        }

        [Test]
        public void TestStructure()
        {
            // alpha, alpha/beta, alpha/beta/gamma, delta, delta/epsilon, not in collection
            CheckDisplayedGroupsCount(6);
            checkVisibleFolders("alpha", "delta", "");

            AddAssert("sub-folders come before own sets", () =>
            {
                var items = Carousel.GetCarouselItems()!;
                int betaIndex = items.ToList().FindIndex(i => i.Model is CollectionFolderGroupDefinition { Path: "alpha/beta" });
                int alphaSetIndex = items.ToList().FindIndex(i => i.Model is GroupedBeatmapSet { Group: CollectionFolderGroupDefinition { Path: "alpha" } });
                return betaIndex >= 0 && betaIndex < alphaSetIndex;
            });

            AddAssert("alpha counts sets inside it", () => folderItem("alpha").NestedItemCount, () => Is.EqualTo(4));
        }

        [Test]
        public void TestOpenAndCloseChain()
        {
            activate("alpha");
            checkVisibleFolders("alpha", "alpha/beta", "delta", "");
            checkOwnSetsVisible("alpha", true);
            checkOwnSetsVisible("alpha/beta", false);

            activate("alpha/beta");
            checkVisibleFolders("alpha", "alpha/beta", "alpha/beta/gamma", "delta", "");
            AddAssert("alpha still open", () => folderItem("alpha").IsExpanded);
            checkOwnSetsVisible("alpha", true);
            checkOwnSetsVisible("alpha/beta", true);

            // closing the parent closes everything inside it.
            activate("alpha");
            checkVisibleFolders("alpha", "delta", "");
            AddAssert("beta closed", () => !folderItem("alpha/beta").IsExpanded);
            checkOwnSetsVisible("alpha/beta", false);

            activate("alpha");
            activate("alpha/beta");

            // closing the sub-folder keeps the parent open.
            activate("alpha/beta");
            AddAssert("alpha still open", () => folderItem("alpha").IsExpanded);
            checkVisibleFolders("alpha", "alpha/beta", "delta", "");

            // opening another folder closes the chain.
            activate("delta");
            checkVisibleFolders("alpha", "delta", "delta/epsilon", "");
            checkOwnSetsVisible("alpha", false);
        }

        [Test]
        public void TestSelectingInParentKeepsSubFolderOpen()
        {
            activate("alpha");
            activate("alpha/beta");

            AddStep("select set in alpha", () => Carousel.Activate(Carousel.GetCarouselItems()!.First(i => i.Model is GroupedBeatmapSet { Group: CollectionFolderGroupDefinition { Path: "alpha" } })));
            WaitForFiltering();

            AddAssert("beta still open", () => folderItem("alpha/beta").IsExpanded);
            AddAssert("selected set is visible", () => Carousel.GetCarouselItems()!.Single(i => i.Model is GroupedBeatmapSet s && s.BeatmapSet.Equals(Carousel.SelectedBeatmapSet) && s.Group is CollectionFolderGroupDefinition { Path: "alpha" }).IsVisible);
        }

        [Test]
        public void TestSelectionInClosedFolderOpensChain()
        {
            AddStep("select gamma beatmap", () => Carousel.CurrentGroupedBeatmap = (GroupedBeatmap)Carousel.GetCarouselItems()!
                                                                                                         .First(i => i.Model is GroupedBeatmap { Group: CollectionFolderGroupDefinition { Path: "alpha/beta/gamma" } }).Model);

            AddUntilStep("gamma open", () => folderItem("alpha/beta/gamma").IsExpanded);
            AddAssert("parents open", () => folderItem("alpha").IsExpanded && folderItem("alpha/beta").IsExpanded);
            AddAssert("beatmap visible", () => Carousel.GetCarouselItems()!.Single(i => i.Model is GroupedBeatmap { Group: CollectionFolderGroupDefinition { Path: "alpha/beta/gamma" } }).IsVisible);
        }

        [Test]
        public void TestToggleGroupFromSetInParent()
        {
            activate("alpha");
            activate("alpha/beta");

            AddStep("select set in alpha", () => Carousel.Activate(Carousel.GetCarouselItems()!.First(i => i.Model is GroupedBeatmapSet { Group: CollectionFolderGroupDefinition { Path: "alpha" } })));
            WaitForFiltering();

            // the closest header above an alpha set is alpha/beta's, but alpha should be toggled.
            ToggleGroupCollapse();
            AddUntilStep("alpha closed", () => !folderItem("alpha").IsExpanded);
            checkVisibleFolders("alpha", "delta", "");
        }

        [Test]
        public void TestGroupTraversalSkipsHiddenFolders()
        {
            SelectNextGroup();
            AddUntilStep("alpha open", () => folderItem("alpha").IsExpanded);
            SelectNextGroup();
            AddUntilStep("next visible folder opened", () => folderItem("alpha/beta").IsExpanded || folderItem("delta").IsExpanded);
            AddAssert("gamma (hidden) not opened directly", () => !folderItem("alpha/beta/gamma").IsExpanded);
        }

        [Test]
        public void TestRandomInFolderWithOnlySubFolders()
        {
            activate("delta");
            SelectRandomSet();
            AddUntilStep("selection in delta/epsilon", () => Carousel.CurrentGroupedBeatmap?.Group is CollectionFolderGroupDefinition { Path: "delta/epsilon" });
        }

        [Test]
        public void TestFilterHidesEmptiedFolders()
        {
            ApplyToFilterAndWaitForFilter("search for set 2", c => c.SearchText = BeatmapSets[2].Metadata.Title.Split(' ').Last());

            // alpha (parent of beta) and alpha/beta.
            AddAssert("remaining folders", () => Carousel.GetCarouselItems()!.Select(i => i.Model).OfType<CollectionFolderGroupDefinition>().Select(f => f.Path),
                () => Is.EquivalentTo(new[] { "alpha", "alpha/beta" }));

            ApplyToFilterAndWaitForFilter("clear search", c => c.SearchText = string.Empty);
            CheckDisplayedGroupsCount(6);
        }

        [Test]
        public void TestFlatCollectionsUnchanged()
        {
            AddStep("flat collections", () =>
            {
                collections =
                [
                    new BeatmapCollection("one", [.. BeatmapSets[0].Beatmaps.Select(b => b.MD5Hash)]),
                    new BeatmapCollection("two", [.. BeatmapSets[1].Beatmaps.Select(b => b.MD5Hash)]),
                ];
            });
            ApplyToFilterAndWaitForFilter("refilter", null);

            CheckDisplayedGroupsCount(3);
            CheckDisplayedBeatmapSetsCount(1 + 1 + 8);
        }

        [Test]
        public void TestBeatmapInParentAndSubFolderOnlyInParent()
        {
            AddStep("add set 0 to alpha/beta too", () =>
            {
                foreach (var b in BeatmapSets[0].Beatmaps)
                    collections[1].BeatmapMD5Hashes.Add(b.MD5Hash);
            });
            ApplyToFilterAndWaitForFilter("refilter", null);

            AddAssert("set 0 in alpha", () => Carousel.GetCarouselItems()!.Any(i => i.Model is GroupedBeatmapSet { Group: CollectionFolderGroupDefinition { Path: "alpha" } } s && s.BeatmapSet.Equals(BeatmapSets[0])));
            AddAssert("set 0 not in alpha/beta", () => !Carousel.GetCarouselItems()!.Any(i => i.Model is GroupedBeatmapSet { Group: CollectionFolderGroupDefinition { Path: "alpha/beta" } } s && s.BeatmapSet.Equals(BeatmapSets[0])));
            AddAssert("alpha/beta keeps its own set", () => Carousel.GetCarouselItems()!.Any(i => i.Model is GroupedBeatmapSet { Group: CollectionFolderGroupDefinition { Path: "alpha/beta" } } s && s.BeatmapSet.Equals(BeatmapSets[2])));
        }

        [Test]
        public void TestCollectionFilterFoldersOnTop()
        {
            string[] hashes = null!;

            AddStep("put sets in sub-folders", () =>
            {
                collections =
                [
                    new BeatmapCollection("alpha", [.. BeatmapSets[0].Beatmaps.Select(b => b.MD5Hash), .. BeatmapSets[5].Beatmaps.Select(b => b.MD5Hash)]),
                    new BeatmapCollection("alpha/beta", [.. BeatmapSets[2].Beatmaps.Select(b => b.MD5Hash), .. BeatmapSets[7].Beatmaps.Select(b => b.MD5Hash)]),
                    new BeatmapCollection("alpha/gamma", [.. BeatmapSets[3].Beatmaps.Select(b => b.MD5Hash)]),
                    new BeatmapCollection("alpha/delta", [.. BeatmapSets[9].Beatmaps.Select(b => b.MD5Hash)]),
                ];
                hashes = collections.SelectMany(c => c.BeatmapMD5Hashes).ToArray();
            });

            ApplyToFilterAndWaitForFilter("filter to alpha with folders", c =>
            {
                c.Group = GroupMode.None;
                c.CollectionBeatmapMD5Hashes = hashes;
                c.CollectionFolder = ["alpha"];
            });

            AddAssert("folders in collection order", () => Carousel.GetCarouselItems()!.Select(i => i.Model).OfType<CollectionFolderGroupDefinition>().Where(f => f.Depth == 0).Select(f => f.Path),
                () => Is.EqualTo(new[] { "alpha/beta", "alpha/gamma", "alpha/delta" }));

            AddAssert("folders above alpha's own sets", () =>
            {
                var models = Carousel.GetCarouselItems()!.Select(i => i.Model).ToList();
                int lastFolder = models.FindLastIndex(m => m is CollectionFolderGroupDefinition);
                int firstLooseSet = models.FindIndex(m => m is GroupedBeatmapSet { Group: null });

                return lastFolder >= 0 && firstLooseSet > lastFolder && models.Count(m => m is GroupedBeatmapSet { Group: null }) == 2;
            });

            ApplyToFilterAndWaitForFilter("sort by artist", c => c.Sort = SortMode.Artist);
            AddAssert("folders still above alpha's own sets", () =>
            {
                var models = Carousel.GetCarouselItems()!.Select(i => i.Model).ToList();
                return models.FindIndex(m => m is GroupedBeatmapSet { Group: null }) > models.FindLastIndex(m => m is CollectionFolderGroupDefinition);
            });
        }

        [Test]
        public void TestNextSetFromOpenFolderHeader()
        {
            selectIn("delta/epsilon");

            // keyboard focus is on the alpha header, the selection is elsewhere.
            activate("alpha");
            SelectNextSet();

            // goes through the folder (depth first: alpha/beta/gamma's set is the first one after the header) instead of
            // closing it.
            AddUntilStep("selected first set after alpha", () => selectedPath(), () => Is.EqualTo("alpha/beta/gamma"));
            AddAssert("alpha open", () => folderItem("alpha").IsExpanded);

            SelectNextSet();
            AddUntilStep("then alpha/beta", () => selectedPath(), () => Is.EqualTo("alpha/beta"));
            SelectNextSet();
            AddUntilStep("then alpha", () => selectedPath(), () => Is.EqualTo("alpha"));
        }

        [Test]
        public void TestNextSetFromClosedFolderHeader()
        {
            selectIn("alpha");

            // open and close delta: keyboard focus stays on its (closed) header.
            activate("delta");
            activate("delta");
            AddAssert("delta closed", () => !folderItem("delta").IsExpanded);

            SelectNextSet();
            AddUntilStep("delta opened and its set selected", () => selectedPath(), () => Is.EqualTo("delta/epsilon"));
            AddAssert("delta open", () => folderItem("delta").IsExpanded);

            SelectNextSet();
            AddUntilStep("moves on past delta", () => selectedPath(), () => Is.EqualTo(string.Empty));
        }

        [Test]
        public void TestPreviousSetFromFolderHeader()
        {
            selectIn("delta/epsilon");
            activate("alpha");
            activate("delta");

            // keyboard focus on the delta header: the set right above it is alpha's last one.
            SelectPrevSet();
            AddUntilStep("selected alpha's set", () => selectedPath(), () => Is.EqualTo("alpha"));
        }

        [Test]
        public void TestNextGroupStepsIntoSubFolders()
        {
            selectIn("alpha");

            activate("alpha");
            activate("alpha");
            AddAssert("alpha open", () => folderItem("alpha").IsExpanded);

            SelectNextGroup();
            AddUntilStep("alpha/beta opened", () => folderItem("alpha/beta").IsExpanded);
            SelectNextGroup();
            AddUntilStep("alpha/beta/gamma opened", () => folderItem("alpha/beta/gamma").IsExpanded);
            SelectNextGroup();
            AddUntilStep("then delta", () => folderItem("delta").IsExpanded);
        }

        private void selectIn(string path)
        {
            AddStep($"select beatmap in {path}", () => Carousel.CurrentGroupedBeatmap = (GroupedBeatmap)Carousel.GetCarouselItems()!
                                                                                                          .First(i => i.Model is GroupedBeatmap { Group: CollectionFolderGroupDefinition f } && f.Path == path).Model);
            WaitForFiltering();
        }

        private string? selectedPath() => (Carousel.CurrentGroupedBeatmap?.Group as CollectionFolderGroupDefinition)?.Path;

        private CarouselItem folderItem(string path) => Carousel.GetCarouselItems()!.Single(i => i.Model is CollectionFolderGroupDefinition f && !f.IsNotInCollection && f.Path == path);

        private void activate(string path) => AddStep($"activate {path}", () => Carousel.Activate(folderItem(path)));

        private void checkVisibleFolders(params string[] paths)
        {
            AddUntilStep($"visible folders: {string.Join(", ", paths)}", () => Carousel.GetCarouselItems()!
                                                                                     .Where(i => i.IsVisible)
                                                                                     .Select(i => i.Model)
                                                                                     .OfType<CollectionFolderGroupDefinition>()
                                                                                     .Select(f => f.Path), () => Is.EquivalentTo(paths));
        }

        private void checkOwnSetsVisible(string path, bool visible)
        {
            AddAssert($"{path} sets {(visible ? "visible" : "hidden")}", () => Carousel.GetCarouselItems()!
                                                                                     .Where(i => i.Model is GroupedBeatmapSet { Group: CollectionFolderGroupDefinition f } && f.Path == path)
                                                                                     .All(i => i.IsVisible == visible));
        }
    }
}

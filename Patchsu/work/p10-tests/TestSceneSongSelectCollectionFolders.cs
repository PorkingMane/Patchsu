// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osuTK.Input;

namespace osu.Game.Tests.Visual.SongSelect
{
    /// <summary>
    /// Patchsu patch 10: collection folder panels in song select.
    /// </summary>
    public partial class TestSceneSongSelectCollectionFolders : SongSelectTestScene
    {
        private BeatmapSetInfo[] beatmapSets = null!;

        [SetUpSteps]
        public void SetUpCollections()
        {
            ImportBeatmapForRuleset(0);
            ImportBeatmapForRuleset(0);

            AddStep("add collections", () =>
            {
                beatmapSets = Beatmaps.GetAllUsableBeatmapSets().OrderBy(b => b.OnlineID).ToArray();

                Realm.Write(r =>
                {
                    r.RemoveAll<BeatmapCollection>();
                    r.Add(new BeatmapCollection("Folder", beatmapSets[0].Beatmaps.Select(b => b.MD5Hash).ToList()));
                    r.Add(new BeatmapCollection("Folder/Sub", beatmapSets[1].Beatmaps.Select(b => b.MD5Hash).ToList()));
                    r.Add(new BeatmapCollection("Other", beatmapSets[1].Beatmaps.Select(b => b.MD5Hash).ToList()));
                });
            });

            LoadSongSelect();
            GroupBy(GroupMode.Collections);
            WaitForFiltering();
        }

        [Test]
        public void TestFolderPanelsShown()
        {
            // song select selects a beatmap on load, which opens its folder; opening "Other" closes that chain.
            AddUntilStep("top level folder panels", () => visibleFolderTitles(true), () => Is.EqualTo(new[] { "Folder", "Other" }));
            AddStep("open Other", () => folderPanel("Other").TriggerClick());
            AddUntilStep("only top level shown", () => visibleFolderTitles(), () => Is.EqualTo(new[] { "Folder", "Other" }));

            AddStep("open Folder", () => folderPanel("Folder").TriggerClick());
            AddUntilStep("Sub shown inside", () => visibleFolderTitles(), () => Is.EqualTo(new[] { "Folder", "Sub", "Other" }));
            AddAssert("Sub indented", () => folderPanel("Sub").ScreenSpaceDrawQuad.TopLeft.X > folderPanel("Folder").ScreenSpaceDrawQuad.TopLeft.X);
            AddAssert("folder panels are 30 px", () => folderPanel("Folder").DrawHeight, () => Is.EqualTo(30));
        }

        [Test]
        public void TestCreateSubCollectionFromPanel()
        {
            openContextMenu("Other");
            clickMenuItem("New sub-collection...");

            AddUntilStep("name box focused", () => popoverTextBox()?.HasFocus == true);
            AddStep("type name and confirm", () =>
            {
                popoverTextBox()!.Text = "Inner";
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("collection created", () => collectionNames().Contains("Other/Inner"));
            AddUntilStep("popover closed", () => popoverTextBox() == null);
        }

        [Test]
        public void TestRenameFolderFromPanel()
        {
            openContextMenu("Folder");
            clickMenuItem("Rename...");

            AddUntilStep("name box focused", () => popoverTextBox()?.HasFocus == true);
            AddStep("type name and confirm", () =>
            {
                popoverTextBox()!.Text = "Renamed";
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("folder and sub renamed", () => collectionNames(), () => Is.EquivalentTo(new[] { "Renamed", "Renamed/Sub", "Other" }));
            WaitForFiltering();
            AddUntilStep("panel renamed", () => visibleFolderTitles(true), () => Is.EqualTo(new[] { "Other", "Renamed" }));
        }

        [Test]
        public void TestMoveFolderFromPanel()
        {
            AddUntilStep("Other panel present", () => Carousel.ChildrenOfType<PanelCollectionFolder>().Any(p => p.Item?.IsVisible == true && ((CollectionFolderGroupDefinition)p.Item.Model).Title.ToString() == "Other"));
            AddStep("Move to → Folder", () =>
            {
                var moveTo = folderPanel("Other").ContextMenuItems.Single(i => i.Text.Value.ToString() == "Move to");
                moveTo.Items.Single(i => i.Text.Value.ToString() == "Folder").Action.Value!.Invoke();
            });

            AddUntilStep("moved", () => collectionNames(), () => Is.EquivalentTo(new[] { "Folder", "Folder/Sub", "Folder/Other" }));
        }

        [Test]
        public void TestMoveSubFolderToTopLevel()
        {
            AddUntilStep("Folder panel present", () => Carousel.ChildrenOfType<PanelCollectionFolder>().Any(p => p.Item?.IsVisible == true && ((CollectionFolderGroupDefinition)p.Item.Model).Title.ToString() == "Folder"));
            AddStep("open Other", () => folderPanel("Other").TriggerClick());
            AddStep("open Folder", () => folderPanel("Folder").TriggerClick());
            AddUntilStep("Sub panel present", () => Carousel.ChildrenOfType<PanelCollectionFolder>().Any(p => p.Item?.IsVisible == true && ((CollectionFolderGroupDefinition)p.Item.Model).Title.ToString() == "Sub"));
            AddStep("Move to → Top level", () =>
            {
                var moveTo = folderPanel("Sub").ContextMenuItems.Single(i => i.Text.Value.ToString() == "Move to");
                moveTo.Items.Single(i => i.Text.Value.ToString() == "Top level").Action.Value!.Invoke();
            });

            AddUntilStep("moved", () => collectionNames(), () => Is.EquivalentTo(new[] { "Folder", "Sub", "Other" }));
            AddAssert("Sub keeps its beatmaps", () => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Single(c => c.Name == "Sub").BeatmapMD5Hashes.Count),
                () => Is.EqualTo(beatmapSets[1].Beatmaps.Count));
            WaitForFiltering();
            AddUntilStep("Sub folder at top level", () => visibleFolderTitles(true), () => Does.Contain("Sub"));
            AddStep("open Sub", () => folderPanel("Sub").TriggerClick());
            AddUntilStep("Sub's set shown", () => Carousel.GetCarouselItems()!.Any(i => i.IsVisible && i.Model is GroupedBeatmapSet { Group: CollectionFolderGroupDefinition { Path: "Sub" } }));
        }

        [Test]
        public void TestMergeSubFolderIntoParent()
        {
            AddUntilStep("Folder panel present", () => Carousel.ChildrenOfType<PanelCollectionFolder>().Any(p => p.Item?.IsVisible == true && ((CollectionFolderGroupDefinition)p.Item.Model).Title.ToString() == "Folder"));
            AddStep("open Other", () => folderPanel("Other").TriggerClick());
            AddStep("open Folder", () => folderPanel("Folder").TriggerClick());
            AddUntilStep("Sub panel present", () => Carousel.ChildrenOfType<PanelCollectionFolder>().Any(p => p.Item?.IsVisible == true && ((CollectionFolderGroupDefinition)p.Item.Model).Title.ToString() == "Sub"));
            AddStep("Merge into parent", () => folderPanel("Sub").ContextMenuItems.Single(i => i.Text.Value.ToString() == "Merge into parent").Action.Value!.Invoke());

            AddUntilStep("Sub removed", () => collectionNames(), () => Is.EquivalentTo(new[] { "Folder", "Other" }));
            AddAssert("Folder has both sets", () => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Single(c => c.Name == "Folder").BeatmapMD5Hashes.ToArray()),
                () => Is.EquivalentTo(beatmapSets[0].Beatmaps.Concat(beatmapSets[1].Beatmaps).Select(b => b.MD5Hash)));
            AddAssert("no merge item on top level folders", () => folderPanel("Folder").ContextMenuItems.All(i => i.Text.Value.ToString() != "Merge into parent"));
        }

        [Test]
        public void TestMergeIntoImplicitParent()
        {
            // "Lonely" only exists because of its sub-collections; a merge creates it. Nested ones merge too, without duplicates.
            AddStep("add sub-collections", () => Realm.Write(r =>
            {
                r.Add(new BeatmapCollection("Lonely/Child", beatmapSets[0].Beatmaps.Select(b => b.MD5Hash).ToList()));
                r.Add(new BeatmapCollection("Lonely/Child/Deeper", beatmapSets[0].Beatmaps.Concat(beatmapSets[1].Beatmaps).Select(b => b.MD5Hash).ToList()));
            }));
            AddStep("merge Lonely/Child", () => CollectionFolders.MergeIntoParent(Realm, ["Lonely", "Child"]));
            AddAssert("only Lonely left", () => collectionNames(), () => Is.EquivalentTo(new[] { "Folder", "Folder/Sub", "Other", "Lonely" }));
            AddAssert("Lonely has both sets once", () => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Single(c => c.Name == "Lonely").BeatmapMD5Hashes.ToArray()),
                () => Is.EqualTo(beatmapSets[0].Beatmaps.Concat(beatmapSets[1].Beatmaps).Select(b => b.MD5Hash)));
        }

        [Test]
        public void TestDeleteFolderFromPanel()
        {
            openContextMenu("Folder");
            clickMenuItem("Delete");

            AddStep("confirm", () => DialogOverlay.CurrentDialog!.PerformAction<osu.Game.Overlays.Dialog.PopupDialogDangerousButton>());
            AddUntilStep("folder and sub deleted", () => collectionNames(), () => Is.EquivalentTo(new[] { "Other" }));
        }

        [Test]
        public void TestCollectionFilterShowsSubFolders()
        {
            GroupBy(GroupMode.None);
            // a selection inside Sub would open it (song select picks a random beatmap otherwise).
            AddStep("select Folder's own beatmap", () => Beatmap.Value = Beatmaps.GetWorkingBeatmap(beatmapSets[0].Beatmaps[0]));
            AddUntilStep("carousel selected it", () => Carousel.CurrentGroupedBeatmap?.Beatmap.BeatmapSet?.Equals(beatmapSets[0]) == true);
            filterToCollection("Folder");

            // Sub is a folder at the top, Folder's own set is listed loose below it.
            AddUntilStep("sub folder shown", () => visibleFolderTitles(), () => Is.EqualTo(new[] { "Sub" }));
            AddUntilStep("own set shown loose", () => Carousel.GetCarouselItems()!.Any(i => i.IsVisible && i.Model is GroupedBeatmapSet { Group: null } s && s.BeatmapSet.Equals(beatmapSets[0])));
            AddAssert("sub's set hidden until opened", () => !Carousel.GetCarouselItems()!.Any(i => i.IsVisible && i.Model is GroupedBeatmapSet s && s.BeatmapSet.Equals(beatmapSets[1])));

            AddStep("open Sub", () => folderPanel("Sub").TriggerClick());
            AddUntilStep("sub's set shown", () => Carousel.GetCarouselItems()!.Any(i => i.IsVisible && i.Model is GroupedBeatmapSet { Group: CollectionFolderGroupDefinition { Path: "Folder/Sub" } }));
            AddAssert("own set still shown", () => Carousel.GetCarouselItems()!.Any(i => i.IsVisible && i.Model is GroupedBeatmapSet { Group: null }));
        }

        [Test]
        public void TestCollectionFilterWithoutSubCollectionsIsVanilla()
        {
            GroupBy(GroupMode.None);
            filterToCollection("Other");

            AddUntilStep("no folders", () => Carousel.ChildrenOfType<PanelCollectionFolder>().Count(p => p.Item?.IsVisible == true), () => Is.Zero);
            AddUntilStep("other's set shown", () => Carousel.GetCarouselItems()!.Any(i => i.IsVisible && i.Model is GroupedBeatmapSet s && s.BeatmapSet.Equals(beatmapSets[1])));
        }

        [Test]
        public void TestCollectionFilterUpdatesOnChange()
        {
            GroupBy(GroupMode.None);
            filterToCollection("Other");

            AddUntilStep("only set 1", () => Carousel.GetCarouselItems()!.Select(i => i.Model).OfType<GroupedBeatmapSet>().Select(s => s.BeatmapSet.OnlineID).ToArray(),
                () => Is.EqualTo(new[] { beatmapSets[1].OnlineID }));

            AddStep("add set 0 to Other", () => Realm.Write(r =>
            {
                var other = r.All<BeatmapCollection>().AsEnumerable().Single(c => c.Name == "Other");
                foreach (var b in beatmapSets[0].Beatmaps)
                    other.BeatmapMD5Hashes.Add(b.MD5Hash);
            }));

            AddUntilStep("set 0 appears without reloading", () => Carousel.GetCarouselItems()!.Select(i => i.Model).OfType<GroupedBeatmapSet>().Any(s => s.BeatmapSet.Equals(beatmapSets[0])));

            AddStep("add sub-collection with set 1 (already in Other)", () => Realm.Write(r => r.Add(new BeatmapCollection("Other/Inner", beatmapSets[1].Beatmaps.Select(b => b.MD5Hash).ToList()))));
            WaitForFiltering();
            AddAssert("Inner holds only maps of Other, so it's hidden", () => visibleFolderTitles(), () => Is.Empty);

            AddStep("remove set 1 from Other", () => Realm.Write(r =>
            {
                var other = r.All<BeatmapCollection>().AsEnumerable().Single(c => c.Name == "Other");
                foreach (var b in beatmapSets[1].Beatmaps)
                    other.BeatmapMD5Hashes.Remove(b.MD5Hash);
            }));
            AddUntilStep("Inner folder appears", () => visibleFolderTitles(), () => Is.EqualTo(new[] { "Inner" }));
        }

        [Test]
        public void TestCollectionDropdownShowsTopLevelOnly()
        {
            AddUntilStep("only top level collections listed", () => SongSelect.ChildrenOfType<CollectionDropdown>().Single().Items
                                                                             .Where(i => i.Collection != null)
                                                                             .Select(i => i.CollectionName.ToString())
                                                                             .ToArray(), () => Is.EqualTo(new[] { "Folder", "Other" }));
        }

        private void filterToCollection(string name)
        {
            AddStep($"filter to {name}", () => Config.SetValue(OsuSetting.SongSelectCollectionFilter,
                Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Single(c => c.Name == name).ID.ToString())));
            WaitForFiltering();
        }

        private string[] collectionNames() => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Select(c => c.Name).ToArray());

        private string[] visibleFolderTitles(bool topLevelOnly = false) => Carousel.ChildrenOfType<PanelCollectionFolder>()
                                                          .Where(p => p.Item?.IsVisible == true && (!topLevelOnly || ((CollectionFolderGroupDefinition)p.Item.Model).Depth == 0))
                                                          .OrderBy(p => p.Item!.CarouselYPosition)
                                                          .Select(p => ((CollectionFolderGroupDefinition)p.Item!.Model).Title.ToString())
                                                          .ToArray();

        private PanelCollectionFolder folderPanel(string title) => Carousel.ChildrenOfType<PanelCollectionFolder>()
                                                                           .Single(p => p.Item?.IsVisible == true && ((CollectionFolderGroupDefinition)p.Item.Model).Title.ToString() == title);

        private TextBox? popoverTextBox() => SongSelect.ChildrenOfType<OsuPopover>().SelectMany(p => p.ChildrenOfType<OsuTextBox>()).FirstOrDefault();

        private void openContextMenu(string title)
        {
            AddUntilStep($"{title} panel present", () => Carousel.ChildrenOfType<PanelCollectionFolder>().Any(p => p.Item?.IsVisible == true && ((CollectionFolderGroupDefinition)p.Item.Model).Title.ToString() == title));
            AddStep($"right click {title}", () =>
            {
                InputManager.MoveMouseTo(folderPanel(title).TopLevelContent);
                InputManager.Click(MouseButton.Right);
            });
        }

        private void clickMenuItem(string text)
        {
            AddUntilStep($"menu item \"{text}\" shown", () => menuItem(text) != null);
            AddStep($"click \"{text}\"", () =>
            {
                InputManager.MoveMouseTo(menuItem(text)!);
                InputManager.Click(MouseButton.Left);
            });
        }

        private DrawableOsuMenuItem? menuItem(string text) => SongSelect.ChildrenOfType<DrawableOsuMenuItem>()
                                                                         .FirstOrDefault(i => i.IsPresent && i.Item.Text.Value.ToString() == text);
    }
}

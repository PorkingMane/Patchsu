// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Game.Collections;
using osu.Game.Overlays;
using osuTK.Input;

namespace osu.Game.Tests.Visual.SongSelect
{
    /// <summary>
    /// Patchsu patch 13: sub-collections in the manage collections dialog.
    /// </summary>
    public partial class TestSceneManageCollectionFolders : OsuManualInputManagerTestScene
    {
        protected override Container<Drawable> Content { get; } = new Container { RelativeSizeAxes = Axes.Both };

        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Aquamarine);

        private ManageCollectionsDialog dialog = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Dependencies.Cache(Realm);

            DialogOverlay dialogOverlay;
            base.Content.AddRange(new Drawable[]
            {
                Content,
                dialogOverlay = new DialogOverlay(),
            });

            Dependencies.CacheAs<IDialogOverlay>(dialogOverlay);
        }

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            Realm.Write(r => r.RemoveAll<BeatmapCollection>());
            Child = dialog = new ManageCollectionsDialog();
        });

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("show dialog", () => dialog.Show());
        }

        [Test]
        public void TestTreeOrderAndNames()
        {
            AddStep("add collections", () => Realm.Write(r =>
            {
                // "a b" sorts between "a" and "a/x" in plain name order.
                r.Add(new BeatmapCollection("a"));
                r.Add(new BeatmapCollection("a b"));
                r.Add(new BeatmapCollection("a/x"));
                r.Add(new BeatmapCollection("a/x/y"));
                r.Add(new BeatmapCollection("q/z"));
            }));

            assertRows(("a", 0), ("x", 1), ("y", 2), ("a b", 0), ("q/z", 0));
        }

        [Test]
        public void TestRenameParentMovesChildren()
        {
            AddStep("add collections", () => Realm.Write(r =>
            {
                r.Add(new BeatmapCollection("a"));
                r.Add(new BeatmapCollection("a/x"));
            }));

            assertRows(("a", 0), ("x", 1));

            AddStep("rename a to b", () =>
            {
                var textBox = rowTextBoxes().First();
                InputManager.MoveMouseTo(textBox);
                InputManager.Click(MouseButton.Left);
                textBox.Text = "b";
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("names in realm", () => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Select(c => c.Name).OrderBy(n => n).ToArray()),
                () => Is.EqualTo(new[] { "b", "b/x" }));
            assertRows(("b", 0), ("x", 1));
        }

        [Test]
        public void TestRenameChildKeepsParent()
        {
            AddStep("add collections", () => Realm.Write(r =>
            {
                r.Add(new BeatmapCollection("a"));
                r.Add(new BeatmapCollection("a/x"));
            }));

            assertRows(("a", 0), ("x", 1));

            AddStep("rename x to renamed", () =>
            {
                var textBox = rowTextBoxes().ElementAt(1);
                InputManager.MoveMouseTo(textBox);
                InputManager.Click(MouseButton.Left);
                textBox.Text = "renamed";
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("names in realm", () => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Select(c => c.Name).OrderBy(n => n).ToArray()),
                () => Is.EqualTo(new[] { "a", "a/renamed" }));
        }

        [Test]
        public void TestAddSubCollectionButton()
        {
            AddStep("add collection", () => Realm.Write(r => r.Add(new BeatmapCollection("a"))));
            assertRows(("a", 0));

            AddStep("click +", () =>
            {
                InputManager.MoveMouseTo(dialog.ChildrenOfType<DrawableCollectionListItem.AddSubCollectionButton>().First());
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("sub-collection created", () => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Any(c => c.Name == "a/New collection")));
            assertRows(("a", 0), ("New collection", 1));
            AddUntilStep("new name focused", () => rowTextBoxes().ElementAt(1).HasFocus);

            AddStep("type name", () =>
            {
                rowTextBoxes().ElementAt(1).Text = "child";
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("renamed", () => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Any(c => c.Name == "a/child")));
        }

        private TextBox[] rowTextBoxes() => dialog.ChildrenOfType<DrawableCollectionList>().Single().OrderedItems
                                                  .Select(i => i.ChildrenOfType<TextBox>().First()).ToArray();

        private void assertRows(params (string name, int depth)[] rows)
        {
            AddUntilStep($"rows: {string.Join(", ", rows.Select(r => $"{r.name}@{r.depth}"))}", () =>
                dialog.ChildrenOfType<DrawableCollectionList>().Single().OrderedItems
                      .OfType<DrawableCollectionListItem>()
                      .Select(i => (i.ChildrenOfType<TextBox>().First().Text, i.FolderDepth))
                      .ToArray(), () => Is.EqualTo(rows));
        }
    }
}

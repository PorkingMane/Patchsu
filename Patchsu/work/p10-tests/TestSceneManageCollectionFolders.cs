// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Game.Collections;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osuTK.Input;

namespace osu.Game.Tests.Visual.SongSelect
{
    /// <summary>
    /// Patchsu patch 10: sub-collections in the manage collections dialog.
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

        [Test]
        public void TestRightClickOpensMenuOnRelease()
        {
            AddStep("add collections", () => Realm.Write(r =>
            {
                r.Add(new BeatmapCollection("a"));
                r.Add(new BeatmapCollection("b"));
            }));
            assertRows(("a", 0), ("b", 0));

            AddStep("press right on a", () =>
            {
                InputManager.MoveMouseTo(rowTextBoxes()[0]);
                InputManager.PressButton(MouseButton.Right);
            });
            AddAssert("no menu while held", () => menuItem("Rename") == null);
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Right));
            AddUntilStep("menu shown", () => menuItem("Merge into") != null && menuItem("Rename") != null && menuItem("New sub-collection") != null);

            AddStep("click rename", () =>
            {
                InputManager.MoveMouseTo(menuItem("Rename")!);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("name focused", () => rowTextBoxes()[0].HasFocus);
        }

        [Test]
        public void TestMergeFromMenuFlattens()
        {
            AddStep("add collections", () => Realm.Write(r =>
            {
                r.Add(new BeatmapCollection("a", new List<string> { "1" }));
                r.Add(new BeatmapCollection("a/x", new List<string> { "2" }));
                r.Add(new BeatmapCollection("b", new List<string> { "1", "3" }));
                r.Add(new BeatmapCollection("b/y"));
            }));
            assertRows(("a", 0), ("x", 1), ("b", 0), ("y", 1));

            AddStep("right click a", () =>
            {
                InputManager.MoveMouseTo(rowTextBoxes()[0]);
                InputManager.Click(MouseButton.Right);
            });
            AddUntilStep("menu shown", () => menuItem("Merge into") != null);
            AddStep("hover merge into", () => InputManager.MoveMouseTo(menuItem("Merge into")!));
            AddUntilStep("own sub-collection not listed", () => menuItem("b") != null && menuItem("x") == null && menuItem("a") == null);
            AddStep("hover b", () => InputManager.MoveMouseTo(menuItem("b")!));
            AddUntilStep("b's submenu starts with b itself", () => menuItems("b").Length == 2 && menuItem("y") != null);
            AddStep("click b itself", () =>
            {
                InputManager.MoveMouseTo(menuItems("b").Last());
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("a merged into b", () => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().OrderBy(c => c.Name).Select(c => $"{c.Name}:{string.Join(",", c.BeatmapMD5Hashes.OrderBy(h => h))}").ToArray()),
                () => Is.EqualTo(new[] { "b:1,2,3", "b/y:" }));
        }

        [Test]
        public void TestMenuReachesPastDialog()
        {
            AddStep("add collections", () => Realm.Write(r =>
            {
                r.Add(new BeatmapCollection("a", new List<string> { "1" }));
                r.Add(new BeatmapCollection("b target collection"));
            }));
            assertRows(("a", 0), ("b target collection", 0));

            AddStep("right click a near its right edge", () =>
            {
                var box = rowTextBoxes()[0];
                InputManager.MoveMouseTo(new osuTK.Vector2(box.ScreenSpaceDrawQuad.TopRight.X - 10, box.ScreenSpaceDrawQuad.Centre.Y));
                InputManager.Click(MouseButton.Right);
            });
            AddUntilStep("menu shown", () => menuItem("Merge into") != null);
            AddStep("hover merge into", () => InputManager.MoveMouseTo(menuItem("Merge into")!));
            AddUntilStep("target past the dialog's right edge", () =>
            {
                var target = menuItem("b target collection");
                return target != null && target.ScreenSpaceDrawQuad.TopRight.X > dialog.ScreenSpaceDrawQuad.TopRight.X;
            });
            AddStep("click the part outside the dialog", () =>
            {
                var target = menuItem("b target collection")!;
                InputManager.MoveMouseTo(new osuTK.Vector2(dialog.ScreenSpaceDrawQuad.TopRight.X + 5, target.ScreenSpaceDrawQuad.Centre.Y));
                InputManager.Click(MouseButton.Left);
            });

            AddUntilStep("merged", () => Realm.Run(r => r.All<BeatmapCollection>().Count()), () => Is.EqualTo(1));
            AddAssert("dialog still open", () => dialog.State.Value, () => Is.EqualTo(Visibility.Visible));
        }

        [Test]
        public void TestHoldRightAndDragMerges()
        {
            AddStep("add collections", () => Realm.Write(r =>
            {
                r.Add(new BeatmapCollection("a", new List<string> { "1" }));
                r.Add(new BeatmapCollection("a/x", new List<string> { "2" }));
                r.Add(new BeatmapCollection("b", new List<string> { "3" }));
            }));
            assertRows(("a", 0), ("x", 1), ("b", 0));

            AddStep("hold right on a", () =>
            {
                InputManager.MoveMouseTo(rowTextBoxes()[0]);
                InputManager.PressButton(MouseButton.Right);
            });
            AddUntilStep("drag started after hold", () => rows()[0].Alpha < 1);

            AddStep("move over own sub-collection", () => InputManager.MoveMouseTo(rowTextBoxes()[1]));
            AddAssert("not a target", () => rows()[1].IsMergeTarget, () => Is.False);

            AddStep("move over b", () => InputManager.MoveMouseTo(rowTextBoxes()[2]));
            AddUntilStep("b highlighted", () => rows()[2].IsMergeTarget);
            AddAssert("menu not shown", () => menuItem("Rename") == null);

            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Right));
            AddUntilStep("a merged into b", () => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Select(c => $"{c.Name}:{string.Join(",", c.BeatmapMD5Hashes.OrderBy(h => h))}").ToArray()),
                () => Is.EqualTo(new[] { "b:1,2,3" }));
            AddAssert("menu not shown", () => menuItem("Rename") == null);
        }

        [Test]
        public void TestDragOntoBottomEdgeMovesInside()
        {
            AddStep("add collections", () => Realm.Write(r =>
            {
                r.Add(new BeatmapCollection("a", new List<string> { "1" }));
                r.Add(new BeatmapCollection("a/x"));
                r.Add(new BeatmapCollection("b", new List<string> { "3" }));
            }));
            assertRows(("a", 0), ("x", 1), ("b", 0));

            holdRight(0);
            AddStep("move to bottom edge of b", () => InputManager.MoveMouseTo(edgeOf(2, bottom: true)));
            AddUntilStep("line shown, b not a merge target", () => moveLineShown() && !rows()[2].IsMergeTarget);
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Right));

            AddUntilStep("a moved inside b with its sub-collection", () => names(), () => Is.EqualTo(new[] { "b", "b/a", "b/a/x" }));
            AddAssert("beatmaps kept", () => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Single(c => c.Name == "b/a").BeatmapMD5Hashes.Single()), () => Is.EqualTo("1"));
            assertRows(("b", 0), ("a", 1), ("x", 2));
        }

        [Test]
        public void TestDragOntoTopEdgeMovesBeside()
        {
            AddStep("add collections", () => Realm.Write(r =>
            {
                r.Add(new BeatmapCollection("a"));
                r.Add(new BeatmapCollection("a/x"));
                r.Add(new BeatmapCollection("b"));
            }));
            assertRows(("a", 0), ("x", 1), ("b", 0));

            holdRight(1);
            AddStep("move to bottom edge of its own parent", () => InputManager.MoveMouseTo(edgeOf(0, bottom: true)));
            AddAssert("no line where it already is", () => !moveLineShown());
            AddStep("move to top edge of b", () => InputManager.MoveMouseTo(edgeOf(2, bottom: false)));
            AddUntilStep("line shown", moveLineShown);
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Right));

            AddUntilStep("x moved to top level", () => names(), () => Is.EqualTo(new[] { "a", "b", "x" }));
        }

        private void holdRight(int row)
        {
            AddStep($"hold right on row {row}", () =>
            {
                InputManager.MoveMouseTo(rowTextBoxes()[row]);
                InputManager.PressButton(MouseButton.Right);
            });
            AddUntilStep("drag started after hold", () => rows()[row].Alpha < 1);
        }

        private osuTK.Vector2 edgeOf(int row, bool bottom)
        {
            var quad = rowTextBoxes()[row].ScreenSpaceDrawQuad;
            return new osuTK.Vector2(quad.Centre.X, bottom ? quad.BottomLeft.Y - 3 : quad.TopLeft.Y + 3);
        }

        private bool moveLineShown() => dialog.ChildrenOfType<DrawableCollectionList>().Single().ShowingMoveLine;

        private string[] names() => Realm.Run(r => r.All<BeatmapCollection>().AsEnumerable().Select(c => c.Name).OrderBy(n => n).ToArray());

        [Test]
        public void TestDragReleasedOutsideCancels()
        {
            AddStep("add collections", () => Realm.Write(r =>
            {
                r.Add(new BeatmapCollection("a"));
                r.Add(new BeatmapCollection("b"));
            }));
            assertRows(("a", 0), ("b", 0));

            AddStep("hold right on a", () =>
            {
                InputManager.MoveMouseTo(rowTextBoxes()[0]);
                InputManager.PressButton(MouseButton.Right);
            });
            AddUntilStep("drag started after hold", () => rows()[0].Alpha < 1);
            AddStep("move away and release", () =>
            {
                InputManager.MoveMouseTo(dialog.ScreenSpaceDrawQuad.TopLeft + new osuTK.Vector2(5));
                InputManager.ReleaseButton(MouseButton.Right);
            });
            AddUntilStep("a back to full opacity", () => rows()[0].Alpha, () => Is.EqualTo(1));
            AddAssert("nothing merged", () => Realm.Run(r => r.All<BeatmapCollection>().Count()), () => Is.EqualTo(2));
        }

        private DrawableCollectionListItem[] rows() => dialog.ChildrenOfType<DrawableCollectionList>().Single().OrderedItems.OfType<DrawableCollectionListItem>().ToArray();

        private DrawableOsuMenuItem[] menuItems(string text) => dialog.ChildrenOfType<DrawableOsuMenuItem>()
                                                                     .Where(i => i.IsPresent && i.Item.Text.Value.ToString() == text).ToArray();

        private DrawableOsuMenuItem? menuItem(string text) => menuItems(text).FirstOrDefault();

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

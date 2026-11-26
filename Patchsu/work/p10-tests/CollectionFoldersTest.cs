// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Game.Collections;
using osu.Game.Graphics.UserInterface;

namespace osu.Game.Tests.NonVisual
{
    /// <summary>
    /// Patchsu patch 10: collection folder paths.
    /// </summary>
    [TestFixture]
    public class CollectionFoldersTest
    {
        [TestCase("a", new[] { "a" })]
        [TestCase("a/b", new[] { "a", "b" })]
        [TestCase(" a / b ", new[] { "a", "b" })]
        [TestCase("a//b/", new[] { "a", "b" })]
        [TestCase("/", new[] { "/" })]
        [TestCase("", new[] { "" })]
        public void TestSplit(string name, string[] expected) => Assert.That(CollectionFolders.Split(name), Is.EqualTo(expected));

        [Test]
        public void TestIsSameOrInside()
        {
            Assert.That(CollectionFolders.IsSameOrInside(new[] { "a", "b" }, new[] { "a" }), Is.True);
            Assert.That(CollectionFolders.IsSameOrInside(new[] { "a" }, new[] { "a" }), Is.True);
            Assert.That(CollectionFolders.IsSameOrInside(new[] { "ab" }, new[] { "a" }), Is.False);
            Assert.That(CollectionFolders.IsSameOrInside(new[] { "a" }, new[] { "a", "b" }), Is.False);
        }

        [Test]
        public void TestTreeWithVirtualParent()
        {
            var roots = CollectionFolders.BuildTree(new[]
            {
                new BeatmapCollection("a"),
                new BeatmapCollection("a/b"),
                new BeatmapCollection("c/d"),
                new BeatmapCollection("a / b"),
            });

            Assert.That(roots.Select(r => r.Path), Is.EqualTo(new[] { "a", "c" }));
            Assert.That(roots[0].Children.Single().Collections, Has.Count.EqualTo(2), "same path after normalising is one folder");
            Assert.That(roots[1].Collections, Is.Empty, "c only exists through c/d");
            Assert.That(roots[1].Children.Single().Path, Is.EqualTo("c/d"));
            Assert.That(roots[1].Children.Single().Depth, Is.EqualTo(1));
        }

        [Test]
        public void TestMenuNesting()
        {
            var items = CollectionFolders.CreateMenuItems(new[]
            {
                new BeatmapCollection("a"),
                new BeatmapCollection("a/b"),
                new BeatmapCollection("z"),
            }, c => new OsuMenuItem(c.Name));

            // "a" is its own toggle (clickable) with its sub-collections in a submenu.
            Assert.That(items.Select(i => i.Text.Value.ToString()), Is.EqualTo(new[] { "a", "z" }));
            Assert.That(items[0].Items.Select(i => i.Text.Value.ToString()), Is.EqualTo(new[] { "b" }));
        }

        [Test]
        public void TestMenuFolderWithoutOwnCollection()
        {
            var items = CollectionFolders.CreateMenuItems(new[]
            {
                new BeatmapCollection("a/b"),
            }, c => new OsuMenuItem(c.Name, MenuItemType.Standard, () => { }));

            Assert.That(items.Single().Text.Value.ToString(), Is.EqualTo("a"));
            Assert.That(items.Single().Action.Value, Is.Null);
            Assert.That(items.Single().Items.Single().Text.Value.ToString(), Is.EqualTo("b"));
        }
    }
}

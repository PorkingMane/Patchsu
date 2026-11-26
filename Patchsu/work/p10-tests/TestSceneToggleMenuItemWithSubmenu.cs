// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterface;
using osuTK.Input;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Patchsu patch 10: a collection with sub-collections is a toggle with a submenu, and clicking it must still toggle it.
    /// </summary>
    public partial class TestSceneToggleMenuItemWithSubmenu : OsuManualInputManagerTestScene
    {
        private ToggleMenuItem parent = null!;
        private ToggleMenuItem child = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create menu", () =>
            {
                parent = new ToggleMenuItem("Parent") { Items = new MenuItem[] { child = new ToggleMenuItem("Child") } };

                Child = new OsuMenu(Direction.Vertical)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Items = new MenuItem[] { parent, new ToggleMenuItem("Other") },
                    State = MenuState.Open,
                };
            });
        }

        [Test]
        public void TestClickParentToggles()
        {
            AddStep("click parent", () =>
            {
                InputManager.MoveMouseTo(item("Parent"));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("parent toggled on", () => parent.State.Value);
            AddAssert("child untouched", () => !child.State.Value);

            AddStep("click parent again", () =>
            {
                InputManager.MoveMouseTo(item("Parent"));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("parent toggled off", () => !parent.State.Value);
        }

        [Test]
        public void TestChildStillReachable()
        {
            AddStep("hover parent", () => InputManager.MoveMouseTo(item("Parent")));
            AddUntilStep("child shown", () => this.ChildrenOfType<DrawableOsuMenuItem>().Any(i => i.IsPresent && i.Item.Text.Value.ToString() == "Child"));
            AddStep("click child", () =>
            {
                InputManager.MoveMouseTo(item("Child"));
                InputManager.Click(MouseButton.Left);
            });
            AddAssert("child toggled on", () => child.State.Value);
            AddAssert("parent untouched", () => !parent.State.Value);
        }

        private DrawableOsuMenuItem item(string text) => this.ChildrenOfType<DrawableOsuMenuItem>().First(i => i.IsPresent && i.Item.Text.Value.ToString() == text);
    }
}

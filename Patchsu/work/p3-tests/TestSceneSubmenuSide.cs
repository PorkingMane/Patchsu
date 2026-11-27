// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.UserInterface;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Patchsu patch 3: a submenu opened from a submenu that went left keeps going left instead of jumping back over its parents.
    /// </summary>
    public partial class TestSceneSubmenuSide : OsuManualInputManagerTestScene
    {
        private void createMenu(Anchor side, float x = 0)
        {
            AddStep($"create menu at {side} {x}", () =>
            {
                Child = new OsuContextMenuContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new MenuBox
                    {
                        Anchor = side,
                        Origin = side,
                        RelativePositionAxes = Axes.X,
                        X = x,
                    }
                };
            });
            AddStep("right click", () =>
            {
                InputManager.MoveMouseTo(this.ChildrenOfType<MenuBox>().Single());
                InputManager.Click(MouseButton.Right);
            });
        }

        private partial class MenuBox : Box, IHasContextMenu
        {
            public MenuBox()
            {
                Size = new Vector2(20);
            }

            public MenuItem[] ContextMenuItems => new MenuItem[]
                {
                    new OsuMenuItem("Play"),
                    new OsuMenuItem("Collections")
                    {
                        Items = new MenuItem[]
                        {
                            new OsuMenuItem("\"gigapogchamp\" scores needs a really long name")
                            {
                                Items = new MenuItem[] { new OsuMenuItem("BANGERS BANGERS BANGERS BANGERS BANGERS"), new OsuMenuItem("Hard scores but mid song") }
                            },
                            new OsuMenuItem("aimslop") { Items = new MenuItem[] { new OsuMenuItem("Actual aim"), new OsuMenuItem("Slop") } },
                        }
                    },
                };
        }

        [Test]
        public void TestNarrowSubmenuKeepsGoingLeft()
        {
            createMenu(Anchor.CentreRight);
            hover("Collections");
            AddUntilStep("collections went left", () => menuOf("aimslop").ScreenSpaceDrawQuad.TopRight.X <= menuOf("Play").ScreenSpaceDrawQuad.TopLeft.X + 1);

            hover("aimslop");
            AddUntilStep("narrow submenu also left", () => menuOf("Slop").ScreenSpaceDrawQuad.TopRight.X <= menuOf("aimslop").ScreenSpaceDrawQuad.TopLeft.X + 1);

            hover("\"gigapogchamp\" scores needs a really long name");
            AddUntilStep("wide submenu left", () => menuOf("Hard scores but mid song").ScreenSpaceDrawQuad.TopRight.X <= menuOf("aimslop").ScreenSpaceDrawQuad.TopLeft.X + 1);
        }

        [Test]
        public void TestRightSideUnchanged()
        {
            createMenu(Anchor.CentreLeft);
            hover("Collections");
            AddUntilStep("collections went right", () => menuOf("aimslop").ScreenSpaceDrawQuad.TopLeft.X >= menuOf("Play").ScreenSpaceDrawQuad.TopRight.X - 1);

            hover("aimslop");
            AddUntilStep("submenu also right", () => menuOf("Slop").ScreenSpaceDrawQuad.TopLeft.X >= menuOf("aimslop").ScreenSpaceDrawQuad.TopRight.X - 1);
        }

        [Test]
        public void TestNestedSubmenuWithoutRoomOnRightOpensLeft()
        {
            // the root menu fits on the right, "Collections" fits there too, but its nested submenus wouldn't.
            createMenu(Anchor.CentreLeft, 0.6f);
            hover("Collections");
            AddUntilStep("collections went left", () => menuOf("aimslop").ScreenSpaceDrawQuad.TopRight.X <= menuOf("Play").ScreenSpaceDrawQuad.TopLeft.X + 1);

            hover("\"gigapogchamp\" scores needs a really long name");
            AddUntilStep("nested submenu also left", () => menuOf("Hard scores but mid song").ScreenSpaceDrawQuad.TopRight.X <= menuOf("aimslop").ScreenSpaceDrawQuad.TopLeft.X + 1);
        }

        private void hover(string text)
        {
            AddUntilStep($"\"{text}\" shown", () => item(text) != null);
            AddStep($"hover \"{text}\"", () => InputManager.MoveMouseTo(item(text)!));
        }

        private DrawableOsuMenuItem? item(string text) => this.ChildrenOfType<DrawableOsuMenuItem>().FirstOrDefault(i => i.IsPresent && i.Item.Text.Value.ToString() == text);

        private OsuMenu menuOf(string text) => this.ChildrenOfType<OsuMenu>().First(m => m.IsPresent && m.Items.Any(i => i.Text.Value.ToString() == text));
    }
}

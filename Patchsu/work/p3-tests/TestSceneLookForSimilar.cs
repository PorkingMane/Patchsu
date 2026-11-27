// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Screens.Select;
using osuTK.Input;

namespace osu.Game.Tests.Visual.SongSelect
{
    /// <summary>
    /// Patchsu patch 3: "Look for similar" in a beatmap's right-click menu.
    /// </summary>
    public partial class TestSceneLookForSimilar : SongSelectTestScene
    {
        private const string item_text = "Look for similar";

        [Test]
        public void TestShownInLightBlue()
        {
            ImportBeatmapForRuleset(0);
            LoadSongSelect();
            WaitForFiltering();

            AddUntilStep("beatmap panel shown", () => selectedPanel() != null);
            AddStep("right click selected beatmap", () =>
            {
                InputManager.MoveMouseTo(selectedPanel()!);
                InputManager.Click(MouseButton.Right);
            });

            AddUntilStep("item shown", () => menuItem() != null);
            AddAssert("item is external", () => ((OsuMenuItem)menuItem()!.Item).Type, () => Is.EqualTo(MenuItemType.External));
            AddAssert("text is light blue", () => menuItem()!.ChildrenOfType<Drawable>().Any(d => d.Colour.Equals((ColourInfo)new OsuColour().BlueLight)));
        }

        [Test]
        public void TestOptionsPopoverIconAndColour()
        {
            ImportBeatmapForRuleset(0);
            LoadSongSelect();
            WaitForFiltering();

            AddUntilStep("options enabled", () => this.ChildrenOfType<FooterButtonOptions>().Single().Enabled.Value);
            AddStep("open options", () => this.ChildrenOfType<FooterButtonOptions>().Single().TriggerClick());
            AddUntilStep("popover shown", () => this.ChildrenOfType<FooterButtonOptions.Popover>().Any(p => p.IsPresent));

            AddUntilStep("icon shown", () => popoverIcon() != null);
            AddAssert("icon is light blue", () => popoverIcon()!.Colour.Equals((ColourInfo)new OsuColour().BlueLight));
        }

        [Test]
        public void TestHiddenWithoutOnlineID()
        {
            ImportBeatmapForRuleset(s =>
            {
                s.OnlineID = -1;
                foreach (var b in s.Beatmaps)
                    b.OnlineID = -1;
            }, 1, 0);
            LoadSongSelect();
            WaitForFiltering();

            AddUntilStep("beatmap selected", () => !Beatmap.IsDefault);
            AddAssert("no item", () => SongSelect.GetForwardActions(Beatmap.Value.BeatmapInfo).All(i => i.Text.Value.ToString() != item_text));
        }

        private SpriteIcon? popoverIcon() => this.ChildrenOfType<FooterButtonOptions.Popover>().SelectMany(p => p.ChildrenOfType<SpriteIcon>())
                                                    .FirstOrDefault(i => i.Icon.Equals(OsuIcon.LookForSimilar));

        private PanelBeatmap? selectedPanel() => Carousel.ChildrenOfType<PanelBeatmap>().FirstOrDefault(p => p.Selected.Value && p.IsPresent);

        private DrawableOsuMenuItem? menuItem() => SongSelect.ChildrenOfType<DrawableOsuMenuItem>()
                                                             .FirstOrDefault(i => i.IsPresent && i.Item.Text.Value.ToString() == item_text);
    }
}

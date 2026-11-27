// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterface;
using osu.Game.Screens.Select;
using osuTK.Input;

namespace osu.Game.Tests.Visual.SongSelect
{
    /// <summary>
    /// Patchsu patch 3: tighter F3 options popover, 0 / - / + select the 10th to 12th option.
    /// </summary>
    public partial class TestSceneOptionsPopoverHotkeys : SongSelectTestScene
    {
        [Test]
        public void TestExtraHotkeys()
        {
            int pressed = -1;

            ImportBeatmapForRuleset(0);
            LoadSongSelect();
            WaitForFiltering();

            AddUntilStep("options enabled", () => this.ChildrenOfType<FooterButtonOptions>().Single().Enabled.Value);
            AddStep("open options", () => this.ChildrenOfType<FooterButtonOptions>().Single().TriggerClick());
            AddUntilStep("popover shown", () => this.ChildrenOfType<FooterButtonOptions.Popover>().Any(p => p.IsPresent));
            AddAssert("buttons are 34 high", () => buttons().All(b => b.DrawHeight == 34));

            AddStep("replace actions", () =>
            {
                var list = buttons();
                for (int i = 0; i < list.Count; i++)
                {
                    int index = i;
                    list[i].Action = () => pressed = index;
                }
            });

            check(Key.Number1, 0);
            check(Key.Number9, 8);
            check(Key.Number0, 9);
            check(Key.Minus, 10);
            check(Key.Plus, 11);

            void check(Key key, int index)
            {
                AddStep($"press {key}", () =>
                {
                    pressed = -1;
                    InputManager.Key(key);
                });
                AddAssert($"option {index + 1} (or none if missing)", () => pressed, () => Is.EqualTo(buttons().Count > index ? index : -1));
            }
        }

        private List<OsuButton> buttons() => this.ChildrenOfType<FooterButtonOptions.Popover>().SelectMany(p => p.ChildrenOfType<OsuButton>()).ToList();
    }
}

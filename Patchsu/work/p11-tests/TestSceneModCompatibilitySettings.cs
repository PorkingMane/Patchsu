// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.LocalPriority;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Utils;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneModCompatibilitySettings : OsuGameTestScene
    {
        [Test]
        public void TestToggleHardRockMirror()
        {
            AddStep("allow HR + MR", () => Game.LocalConfig.SetValue(OsuSetting.ModCompatibilityHardRockMirror, true));
            AddAssert("override enabled", () => ModCompatibilityOverrides.HardRockMirror.Enabled);

            AddStep("select HR + MR", () => Game.SelectedMods.Value = new Mod[] { new OsuModHardRock(), new OsuModMirror() });
            AddAssert("both kept", () => Game.SelectedMods.Value.Count == 2);

            AddStep("disallow HR + MR", () => Game.LocalConfig.SetValue(OsuSetting.ModCompatibilityHardRockMirror, false));
            AddAssert("override disabled", () => !ModCompatibilityOverrides.HardRockMirror.Enabled);
            AddAssert("only HR left", () => Game.SelectedMods.Value.Single() is OsuModHardRock);

            AddStep("select HR + MR again", () => Game.SelectedMods.Value = new Mod[] { new OsuModHardRock(), new OsuModMirror() });
            AddAssert("rejected", () => !ModUtils.CheckCompatibleSet(Game.SelectedMods.Value) || Game.SelectedMods.Value.Count < 2);
        }

        [Test]
        public void TestSettingsSubsectionShown()
        {
            AddStep("open settings", () => Game.Settings.Show());
            AddUntilStep("subsection in Patchsu section", () => Game.Settings.ChildrenOfType<LocalPrioritySection>().SingleOrDefault()?.ChildrenOfType<ModCompatibilitySettings>().Any() == true);
            AddAssert("subsections in patch order", () =>
            {
                var flow = (osu.Framework.Graphics.Containers.FillFlowContainer)Game.Settings.ChildrenOfType<ModCompatibilitySettings>().Single().Parent!;
                var numbers = flow.FlowingChildren.OfType<PatchSettingsSubsection>().Select(s => s.PatchNumber).ToList();
                // works with or without 0002 ("Patch 2"): the tab comes from 0001.
                return numbers.Contains(11) && numbers.SequenceEqual(numbers.OrderBy(n => n));
            });
            AddAssert("header", () => Game.Settings.ChildrenOfType<ModCompatibilitySettings>().Single()
                                          .ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>().First().Text.ToString() == "Patch 11. Mod compatability");
        }
    }
}

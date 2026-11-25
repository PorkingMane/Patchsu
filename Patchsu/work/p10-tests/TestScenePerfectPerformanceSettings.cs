// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Patchsu 0010 tests (not part of the patch). Copy into osu.Game.Tests/Visual/SongSelect.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.LocalPriority;
using osu.Game.Overlays;
using osu.Game.Screens.Select;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestScenePerfectPerformanceSettings : OsuTestScene
    {
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Purple);

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Test]
        public void TestPatchsuSectionHasPatch10()
        {
            LocalPrioritySection section = null!;

            AddStep("create Patchsu section", () => Child = section = new LocalPrioritySection());
            AddAssert("tab is shown", () => LocalPrioritySection.HasSubsections);
            AddAssert("has Patch 10 subsection", () => section.ChildrenOfType<PerfectPerformanceSettings>().Single().ChildrenOfType<osu.Framework.Graphics.Sprites.SpriteText>().Any(t => t.Text.ToString() == "Patch 10. PP aware beatmap carousel"));

            AddStep("cap on", () => config.SetValue(OsuSetting.SongSelectPerformanceUncapped, false));
            AddStep("tick the checkbox", () => section.ChildrenOfType<PerfectPerformanceSettings>().Single().ChildrenOfType<FormCheckBox>().Single().Current.Value = true);
            AddAssert("uncapped in config", () => config.Get<bool>(OsuSetting.SongSelectPerformanceUncapped));
            AddStep("reset", () => config.SetValue(OsuSetting.SongSelectPerformanceUncapped, false));
        }
    }
}

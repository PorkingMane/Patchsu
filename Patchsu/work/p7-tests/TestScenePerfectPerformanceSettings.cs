// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Patchsu 0007 tests (not part of the patch). Copy into osu.Game.Tests/Visual/SongSelect.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
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

        private Bindable<bool> uncapped => PatchsuSettings.Current.GetBindable(PerfectPerformanceSettings.SECTION, PerfectPerformanceSettings.UNCAPPED, false);

        [Test]
        public void TestPatchsuSectionHasPatch7()
        {
            LocalPrioritySection section = null!;

            AddStep("create Patchsu section", () => Child = section = new LocalPrioritySection());
            AddAssert("tab is shown", () => LocalPrioritySection.HasSubsections);
            AddAssert("has Patch 7 subsection", () => section.ChildrenOfType<PerfectPerformanceSettings>().Single().ChildrenOfType<osu.Framework.Graphics.Sprites.SpriteText>().Any(t => t.Text.ToString() == "Patch 7. PP aware beatmap carousel"));

            AddStep("cap on", () => uncapped.Value = false);
            AddStep("tick the checkbox", () => section.ChildrenOfType<PerfectPerformanceSettings>().Single().ChildrenOfType<FormCheckBox>().Single().Current.Value = true);
            AddAssert("uncapped in PatchsuSettings", () => uncapped.Value);
            AddStep("reset", () => uncapped.Value = false);
        }
    }
}

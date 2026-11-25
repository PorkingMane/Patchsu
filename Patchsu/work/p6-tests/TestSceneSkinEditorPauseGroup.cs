// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Screens.Edit;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Skinning;
using osu.Game.Skinning.Components;
using osu.Game.Tests.Visual;

namespace osu.Game.Tests.Visual.Gameplay
{
    /// <summary>
    /// Patchsu 0006: the pause counter is listed in its own "Pause reduction patch" section, not under "Components".
    /// </summary>
    public partial class TestSceneSkinEditorPauseGroup : PlayerTestScene
    {
        private SkinEditor skinEditor = null!;

        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Blue);

        [Cached]
        public readonly EditorClipboard Clipboard = new EditorClipboard();

        protected override bool Autoplay => true;

        protected override Ruleset CreatePlayerRuleset() => new OsuRuleset();

        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("reset skin", () => skins.CurrentSkinInfo.SetDefault());
            AddUntilStep("wait for hud load", () => Player.ChildrenOfType<SkinnableContainer>().All(c => c.ComponentsLoaded));

            AddStep("load skin editor", () =>
            {
                if (skinEditor.IsNotNull())
                    skinEditor.Expire();
                Player.ScaleTo(0.4f);
                LoadComponentAsync(skinEditor = new SkinEditor(Player), Add);
            });
            AddUntilStep("wait for loaded", () => skinEditor.IsLoaded);
        }

        private SkinComponentToolbox[] toolboxes => skinEditor.ChildrenOfType<SkinComponentToolbox>().ToArray();

        // The section header is a text flow (one sprite per word), so compare the joined words of the header only.
        private static bool isPauseGroup(SkinComponentToolbox t)
            => t.ChildrenOfType<SectionHeader>().Any(h => string.Concat(h.ChildrenOfType<SpriteText>().Select(s => s.Text.ToString())).Replace(@" ", string.Empty) == @"Pausereductionpatch");

        [Test]
        public void TestPauseCounterHasItsOwnSection()
        {
            AddUntilStep("pause group section exists", () => toolboxes.Count(isPauseGroup), () => Is.EqualTo(1));
            AddUntilStep("pause counter listed in it", () => toolboxes.Single(isPauseGroup).ChildrenOfType<PauseCountCounter>().Count(), () => Is.EqualTo(1));
            AddAssert("section is visible", () => toolboxes.Single(isPauseGroup).IsPresent);
            AddAssert("not listed in other sections", () => toolboxes.Where(t => !isPauseGroup(t)).Sum(t => t.ChildrenOfType<PauseCountCounter>().Count()), () => Is.EqualTo(0));
            AddAssert("other components still listed", () => toolboxes.Where(t => !isPauseGroup(t)).Sum(t => t.ChildrenOfType<SkinComponentToolbox.ToolboxComponentButton>().Count()), () => Is.GreaterThan(5));
            AddAssert("group section is last", () => isPauseGroup(toolboxes.Last()));
        }
    }
}

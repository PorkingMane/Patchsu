// Patchsu local test (not part of any patch): RandomV2 settings layout (0500). Copy into osu.Game.Rulesets.Osu.Tests/Mods
// and run `dotnet test --filter TestSceneOsuModRandomV2Settings`.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.Overlays;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    public partial class TestSceneOsuModRandomV2Settings : OsuManualInputManagerTestScene
    {
        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Green);

        private OsuModRandomV2 mod = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create settings", () =>
            {
                mod = new OsuModRandomV2();
                Child = new FillFlowContainer
                {
                    Width = 400,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    ChildrenEnumerable = mod.CreateSettingsControls(),
                };
            });
        }

        private OsuModRandomV2.AngleStyleSetting style => this.ChildrenOfType<OsuModRandomV2.AngleStyleSetting>().Single();

        [Test]
        public void TestAngleStyleDrivesSavedSettings()
        {
            AddAssert("random by default", () => style.Current.Value == OsuModRandomV2.AngleEnum.Custom && !mod.CustomAngle.Value);
            AddAssert("sharpness shown", () => this.ChildrenOfType<OsuModRandomV2.AngleSharpnessSetting>().All(s => s.IsPresent));

            AddStep("pick 90°", () => style.Current.Value = OsuModRandomV2.AngleEnum.Ninety);
            AddAssert("custom angle on", () => mod.CustomAngle.Value && mod.Angle.Value == OsuModRandomV2.AngleEnum.Ninety);
            AddUntilStep("sharpness hidden", () => this.ChildrenOfType<OsuModRandomV2.AngleSharpnessSetting>().All(s => !s.IsPresent));

            AddStep("pick random", () => style.Current.Value = OsuModRandomV2.AngleEnum.Custom);
            AddAssert("custom angle off, angle default", () => !mod.CustomAngle.Value && mod.Angle.IsDefault);

            AddStep("select a pattern", () => mod.PatternMask.Value = 1);
            AddAssert("random angles with patterns", () => mod.CustomAngle.Value && mod.Angle.Value == OsuModRandomV2.AngleEnum.Custom);
            AddAssert("pattern options shown", () => this.ChildrenOfType<OsuModRandomV2.MirroredResponsesSetting>().Single().IsPresent);

            AddStep("clear patterns", () => mod.PatternMask.Value = 0);
            AddAssert("back to plain random", () => !mod.CustomAngle.Value && mod.Angle.IsDefault);
            AddUntilStep("pattern options hidden", () => !this.ChildrenOfType<OsuModRandomV2.MirroredResponsesSetting>().Single().IsPresent);

            AddStep("reset mod", () => mod.ResetSettingsToDefaults());
            AddAssert("shows random", () => style.Current.Value == OsuModRandomV2.AngleEnum.Custom);
        }

        [Test]
        public void TestPatternModeCopiesSelection()
        {
            AddStep("select patterns", () => mod.PatternMask.Value = 0b101);
            AddStep("per note type", () => this.ChildrenOfType<OsuModRandomV2.PatternModeSetting>().Single().Current.Value = true);
            AddAssert("lists copied", () => mod.JumpPatterns.Value == 0b101 && mod.StreamPatterns.Value == 0b101 && mod.BurstPatterns.Value == 0b101);
            AddUntilStep("only per type lists shown", () => this.ChildrenOfType<OsuModRandomV2.PatternSetting>().Count(p => p.IsPresent) == 3);
            AddAssert("custom angle stays on", () => mod.CustomAngle.Value);
        }

        [Test]
        public void TestThresholdTooltipFollowsValue()
        {
            AddStep("hover threshold slider", () => InputManager.MoveMouseTo(this.ChildrenOfType<PreviewSliderBar<int>>().Single()));
            AddUntilStep("preview shown", () => this.ChildrenOfType<CirclePreviewTooltip>().Any(t => t.State.Value == Visibility.Visible));
            AddStep("set divisor 4", () => mod.Divisor.Value = 4);
            AddUntilStep("1/4 row turned into jumps", () => this.ChildrenOfType<CirclePreviewTooltip>().Where(t => t.State.Value == Visibility.Visible)
                                                                   .SelectMany(t => t.ChildrenOfType<SpriteText>()).Any(t => t.Text.ToString() == "1/4 jump"));
        }

        [Test]
        public void TestSpacingSliderShowsPreview()
        {
            AddStep("hover jump spacing", () => InputManager.MoveMouseTo(this.ChildrenOfType<OsuModRandomV2.JumpSpacingSetting>().First(s => s.GetType() == typeof(OsuModRandomV2.JumpSpacingSetting))
                                                                                .ChildrenOfType<PreviewSliderBar<float>>().Single()));
            AddUntilStep("preview shown", () => this.ChildrenOfType<CirclePreviewTooltip>().Any(t => t.State.Value == Visibility.Visible));
            AddStep("set 2x", () => mod.AimDistanceMultiplier.Value = 2);
            AddAssert("value in description", () => preview<OsuModRandomV2.JumpSpacingSetting>().Description.EndsWith("Now: 2.0x", StringComparison.Ordinal));
        }

        [Test]
        public void TestExpoExplainsNoEffectAtDefaultSpacing()
        {
            float linearGap = 0;

            AddAssert("note at 1.0x", () => preview<OsuModRandomV2.ExpoJumpsSetting>().Description.Contains("above 1.0x"));
            AddStep("set 2x", () => mod.AimDistanceMultiplier.Value = 2);
            AddAssert("no note above 1.0x", () => !preview<OsuModRandomV2.ExpoJumpsSetting>().Description.Contains("above 1.0x"));
            AddStep("store 1/2 gap", () => linearGap = gap(preview<OsuModRandomV2.ExpoJumpsSetting>(), 1));
            AddStep("toggle expo", () => mod.ExpoJumps.Value = true);
            AddAssert("1/2 gap changed", () => !Precision.AlmostEquals(linearGap, gap(preview<OsuModRandomV2.ExpoJumpsSetting>(), 1)));
        }

        [Test]
        public void TestPatternCheckboxesHaveNoGaps()
        {
            AddAssert("rows touch", () =>
            {
                var boxes = this.ChildrenOfType<OsuModRandomV2.PatternSetting>().First().ChildrenOfType<Graphics.UserInterface.OsuCheckbox>().ToArray();
                return Precision.AlmostEquals(boxes[0].ScreenSpaceDrawQuad.BottomLeft.Y, boxes[2].ScreenSpaceDrawQuad.TopLeft.Y);
            });
        }

        [Test]
        public void TestPreviewAnimatesBetweenDifferentCircleCounts()
        {
            CirclePreviewTooltip tooltip = null!;
            StaticCirclePiece first = null!;

            AddStep("show triangle", () =>
            {
                Add(tooltip = new CirclePreviewTooltip { Anchor = Anchor.Centre, Origin = Anchor.Centre });
                tooltip.SetContent(OsuModRandomV2.CreateTurnPreview("three", MathF.PI * 2 / 3));
                tooltip.Show();
            });
            AddUntilStep("3 circles", () => tooltip.ChildrenOfType<StaticCirclePiece>().Count() == 3);
            AddStep("store first circle", () => first = tooltip.ChildrenOfType<StaticCirclePiece>().First());

            AddStep("show 6-note path", () => tooltip.SetContent(OsuModRandomV2.CreateTurnPreview("six", 0.3f)));
            AddAssert("new circles fade in", () => tooltip.ChildrenOfType<StaticCirclePiece>().Count(c => c.Alpha < 1) == 3);
            AddAssert("first circle reused", () => tooltip.ChildrenOfType<StaticCirclePiece>().First() == first);
            AddUntilStep("all shown", () => tooltip.ChildrenOfType<StaticCirclePiece>().Count(c => c.Alpha == 1) == 6);

            AddStep("back to triangle", () => tooltip.SetContent(OsuModRandomV2.CreateTurnPreview("three", MathF.PI * 2 / 3)));
            AddAssert("extra circles still fading out", () => tooltip.ChildrenOfType<StaticCirclePiece>().Count() == 6);
            AddUntilStep("extra circles removed", () => tooltip.ChildrenOfType<StaticCirclePiece>().Count() == 3);
            AddAssert("first circle kept", () => tooltip.ChildrenOfType<StaticCirclePiece>().First() == first);
        }

        private static float gap(CirclePreview preview, int row) => preview.Circles[row * 4 + 1].Position.X - preview.Circles[row * 4].Position.X;

        private CirclePreview preview<T>() where T : Drawable, osu.Framework.Graphics.Cursor.IHasCustomTooltip
            => (CirclePreview)this.ChildrenOfType<T>().First(s => s.GetType() == typeof(T)).TooltipContent!;

        [Test]
        public void TestOldPresetWithUnusedPatternsIsCleared()
        {
            AddStep("old preset", () =>
            {
                mod = new OsuModRandomV2 { PatternMask = { Value = 3 }, Angle = { Value = OsuModRandomV2.AngleEnum.Custom } };
                Child = new FillFlowContainer { Width = 400, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, ChildrenEnumerable = mod.CreateSettingsControls() };
            });
            AddAssert("unused patterns cleared", () => mod.PatternMask.Value == 0);
            AddAssert("angle untouched", () => mod.Angle.Value == OsuModRandomV2.AngleEnum.Custom && !mod.CustomAngle.Value);
        }
    }
}

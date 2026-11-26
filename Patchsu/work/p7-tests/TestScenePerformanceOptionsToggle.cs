// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Patchsu 0007 tests (not part of the patch). Copy into osu.Game.Tests/Visual/SongSelect.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.LocalPriority;
using osu.Game.Screens.Select;
using osuTK.Input;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestScenePerformanceOptionsToggle : SongSelectComponentsTestScene
    {
        private FilterControl filterControl = null!;

        protected override Anchor ComponentAnchor => Anchor.TopRight;
        protected override float InitialRelativeWidth => 0.7f;

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            Child = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Child = filterControl = new FilterControl
                {
                    State = { Value = Visibility.Visible },
                    RelativeSizeAxes = Axes.X,
                },
            };
        });

        private Bindable<bool> uncapped => PatchsuSettings.Current.GetBindable(PerfectPerformanceSettings.SECTION, PerfectPerformanceSettings.UNCAPPED, false);

        private ShearedRangeSlider starSlider => filterControl.ChildrenOfType<FilterControl.DifficultyRangeSlider>().Single();
        private ShearedRangeSlider ppSlider => filterControl.ChildrenOfType<ShearedRangeSlider>().Single(s => s.GetType().Name == "PerformanceRangeSlider");
        private ShearedDropdown<Screens.Select.Filter.SortMode> sortDropdown => filterControl.ChildrenOfType<ShearedDropdown<Screens.Select.Filter.SortMode>>().Single();

        // the gap between the star rating bar and the sort row, as upstream has it (the flow's 5 px spacing).
        private float gapBelowStars => filterControl.ToLocalSpace(sortDropdown.ScreenSpaceDrawQuad.TopLeft).Y - filterControl.ToLocalSpace(starSlider.ScreenSpaceDrawQuad.BottomLeft).Y;

        private string ppUpperText => ppSlider.ChildrenOfType<SpriteText>().Where(t => t.Text.ToString().Contains('+') || t.Text.ToString() == "∞" || t.Text.ToString().Contains(',')).Select(t => t.Text.ToString()).LastOrDefault() ?? string.Empty;

        [Test]
        public void TestNoGapWhileClosed()
        {
            AddAssert("gap like upstream", () => Precision.AlmostEquals(gapBelowStars, 5, 0.5f));
        }

        [Test]
        public void TestPerformanceCap()
        {
            AddStep("cap on", () => uncapped.Value = false);
            AddAssert("max is 1000", () => ppSlider.UpperBound.Value == 1000 && ((osu.Framework.Bindables.BindableNumber<double>)ppSlider.UpperBound).MaxValue == 1000);
            AddAssert("reads 1,000+", () => ppUpperText == $"{1000:N0}+");

            AddStep("uncap", () => uncapped.Value = true);
            AddAssert("max is 3000 and unlimited", () => ppSlider.UpperBound.Value == 3000 && ppSlider.UpperBound.IsDefault);
            AddAssert("reads ∞", () => ppUpperText == "∞");

            AddStep("set upper to 2500", () => ppSlider.UpperBound.Value = 2500);
            AddStep("cap on", () => uncapped.Value = false);
            AddAssert("clamped to the cap", () => ppSlider.UpperBound.Value == 1000 && ppSlider.UpperBound.IsDefault);
            AddAssert("reads 1,000+", () => ppUpperText == $"{1000:N0}+");
        }

        private ShearedToggleButton showConverts => filterControl.ChildrenOfType<ShearedToggleButton>().Single(b => b.Text == UserInterfaceStrings.ShowConverts);
        private VisibilityContainer row => filterControl.ChildrenOfType<VisibilityContainer>().Single(c => c.GetType().Name == "PerformanceOptionsRow");

        [Test]
        public void TestRightClickShowConvertsTogglesPerformanceRow()
        {
            AddAssert("row hidden", () => row.State.Value == Visibility.Hidden && row.DrawHeight == 0);
            AddAssert("tooltip", () => showConverts.TooltipText.ToString() == "Right clickable");

            bool convertsBefore = false;
            float closedHeight = 0;
            AddStep("remember show converts", () => convertsBefore = showConverts.Active.Value);
            AddStep("remember height", () => closedHeight = filterControl.DrawHeight);

            AddStep("right click show converts", () =>
            {
                InputManager.MoveMouseTo(showConverts);
                InputManager.Click(MouseButton.Right);
            });
            AddAssert("row shown", () => row.State.Value == Visibility.Visible);
            AddAssert("show converts unchanged", () => showConverts.Active.Value == convertsBefore);
            AddAssert("opens gradually", () => filterControl.DrawHeight < closedHeight + 35);
            AddUntilStep("row fully open", () => Precision.AlmostEquals(row.DrawHeight, 35, 0.01f));
            AddAssert("filter control grew", () => filterControl.DrawHeight > closedHeight + 20);

            AddStep("left click show converts", () => InputManager.Click(MouseButton.Left));
            AddAssert("show converts toggled", () => showConverts.Active.Value != convertsBefore);
            AddAssert("row still shown", () => row.State.Value == Visibility.Visible);

            AddStep("right click again", () => InputManager.Click(MouseButton.Right));
            AddUntilStep("row hidden", () => row.DrawHeight == 0 && row.Alpha == 0);
            AddAssert("filter control back to its height", () => Precision.AlmostEquals(filterControl.DrawHeight, closedHeight, 0.5f));
            AddAssert("gap like upstream", () => Precision.AlmostEquals(gapBelowStars, 5, 0.5f));
        }

        private ShearedToggleButton ppCalc => filterControl.ChildrenOfType<ShearedToggleButton>().Single(b => b.Text.ToString() == "PP calc");

        // In the filter control's own (sheared) space a slanted edge is vertical: right edges on one slanted line have the same X.
        private float rightEdge(Drawable d) => filterControl.ToLocalSpace(d.ScreenSpaceDrawQuad.TopRight).X;

        [Test]
        public void TestPerformanceRowFollowsSlant()
        {
            AddStep("open the row", () =>
            {
                InputManager.MoveMouseTo(showConverts);
                InputManager.Click(MouseButton.Right);
            });
            AddUntilStep("row fully open", () => Precision.AlmostEquals(row.DrawHeight, 35, 0.01f));
            AddStep("print", () => System.Console.WriteLine($"EDGES: converts={rightEdge(showConverts)} ppcalc={rightEdge(ppCalc)} slider={rightEdge(ppSlider)} star={rightEdge(starSlider)} convertsW={showConverts.DrawWidth} ppcalcW={ppCalc.DrawWidth} convertsL={filterControl.ToLocalSpace(showConverts.ScreenSpaceDrawQuad.TopLeft).X} ppcalcL={filterControl.ToLocalSpace(ppCalc.ScreenSpaceDrawQuad.TopLeft).X}"));
            AddAssert("PP calc's right edge on Show converts' slanted line", () => rightEdge(ppCalc), () => Is.EqualTo(rightEdge(showConverts)).Within(1));
            AddAssert("PP calc's left edge on Show converts' slanted line", () => filterControl.ToLocalSpace(ppCalc.ScreenSpaceDrawQuad.TopLeft).X,
                () => Is.EqualTo(filterControl.ToLocalSpace(showConverts.ScreenSpaceDrawQuad.TopLeft).X).Within(1));
        }

        [Test]
        public void TestHoldingPPCalcShrinksItLikeShowConverts()
        {
            float calcWidth = 0, sliderWidth = 0, convertsWidth = 0, starWidth = 0;

            AddStep("open the row", () =>
            {
                InputManager.MoveMouseTo(showConverts);
                InputManager.Click(MouseButton.Right);
            });
            AddUntilStep("row fully open", () => Precision.AlmostEquals(row.DrawHeight, 35, 0.01f));
            AddAssert("as wide as Show converts", () => ppCalc.DrawWidth, () => Is.EqualTo(showConverts.DrawWidth).Within(0.5f));
            AddStep("remember widths", () =>
            {
                calcWidth = ppCalc.DrawWidth;
                sliderWidth = ppSlider.DrawWidth;
                convertsWidth = showConverts.DrawWidth;
                starWidth = starSlider.DrawWidth;
            });

            AddStep("hold PP calc", () =>
            {
                InputManager.MoveMouseTo(ppCalc);
                InputManager.PressButton(MouseButton.Left);
            });
            AddUntilStep("PP calc shrinks", () => ppCalc.DrawWidth < calcWidth * 0.93f);
            AddAssert("PP max range widens with it", () => ppSlider.DrawWidth > sliderWidth + 5);
            AddAssert("Show converts and the star bar stay", () => Precision.AlmostEquals(showConverts.DrawWidth, convertsWidth, 0.5f)
                                                              && Precision.AlmostEquals(starSlider.DrawWidth, starWidth, 0.5f));
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
            AddUntilStep("PP calc back to its width", () => Precision.AlmostEquals(ppCalc.DrawWidth, calcWidth, 0.5f));
            AddUntilStep("PP max range back", () => Precision.AlmostEquals(ppSlider.DrawWidth, sliderWidth, 0.5f));

            AddStep("hold Show converts", () =>
            {
                InputManager.MoveMouseTo(showConverts);
                InputManager.PressButton(MouseButton.Left);
            });
            AddUntilStep("Show converts shrinks", () => showConverts.DrawWidth < convertsWidth * 0.93f);
            AddAssert("PP calc stays", () => ppCalc.DrawWidth, () => Is.EqualTo(calcWidth).Within(0.5f));
            AddStep("release", () => InputManager.ReleaseButton(MouseButton.Left));
        }
    }
}

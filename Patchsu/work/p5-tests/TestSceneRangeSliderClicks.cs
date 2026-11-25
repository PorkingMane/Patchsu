// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Patchsu 0005 tests (not part of the patch). Copy into osu.Game.Tests/Visual/UserInterface.
// The right/middle-click rules live in ShearedRangeSlider, so the star rating bar and the PP bar (0010) both get them.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.UserInterface
{
    public partial class TestSceneRangeSliderClicks : OsuManualInputManagerTestScene
    {
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Aquamarine);

        private ShearedRangeSlider slider = null!;

        private readonly BindableDouble lower = new BindableDouble { MinValue = 0, MaxValue = 1000, Precision = 10 };
        private readonly BindableDouble upper = new BindableDouble(1000) { MinValue = 0, MaxValue = 1000, Precision = 10 };

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            lower.SetDefault();
            upper.SetDefault();

            Child = slider = new ShearedRangeSlider("test")
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 600,
                LowerBound = lower,
                UpperBound = upper,
                MinRange = 10,
            };
        });

        private Vector2 at(float proportion) => slider.ToScreenSpace(new Vector2(slider.DrawWidth * proportion, slider.DrawHeight / 2));

        [Test]
        public void TestRightClickMovesFurthestNub()
        {
            AddStep("right click near the right", () =>
            {
                InputManager.MoveMouseTo(at(0.8f));
                InputManager.Click(MouseButton.Right);
            });
            AddAssert("lower nub moved (it was further)", () => lower.Value > 500 && upper.IsDefault);

            AddStep("wait out double-click", () => { });
            AddWaitStep("wait", 3);

            AddStep("right click near the left", () =>
            {
                InputManager.MoveMouseTo(at(0.3f));
                InputManager.Click(MouseButton.Right);
            });
            AddAssert("upper nub moved (it was further)", () => upper.Value < 500 && !upper.IsDefault);

            AddStep("middle click resets", () => InputManager.Click(MouseButton.Middle));
            AddAssert("both reset", () => lower.IsDefault && upper.IsDefault);
        }

        [Test]
        public void TestDoubleRightClickResetsNub()
        {
            AddStep("set upper to 500", () => upper.Value = 500);
            AddWaitStep("let the nub settle", 10);
            AddStep("move onto the upper nub", () => InputManager.MoveMouseTo(slider.ChildrenOfType<ShearedNub>().MaxBy(n => n.ScreenSpaceDrawQuad.Centre.X)!));
            AddStep("right click", () => InputManager.Click(MouseButton.Right));
            AddAssert("nothing moved", () => upper.Value == 500 && lower.IsDefault);
            AddWaitStep("wait out the double-click window", 10);
            // (steps are further apart than a double-click, so both clicks of it go in one step; the nub isn't moving.)
            AddStep("double right click", () =>
            {
                InputManager.Click(MouseButton.Right);
                InputManager.Click(MouseButton.Right);
            });
            AddAssert("upper nub reset", () => upper.IsDefault && lower.IsDefault);
        }
    }
}

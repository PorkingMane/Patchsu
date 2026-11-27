// Patchsu test for 0003: the skip button in breaks (Settings → Patchsu → "Patch 3. Interface Improvements").

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Timing;
using osu.Game.LocalPriority;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Gameplay
{
    public partial class TestSceneBreakSkip : PlayerTestScene
    {
        private const double break_start = 2000;
        private const double break_end = 20000;

        protected override bool HasCustomSteps => true;

        protected override Ruleset CreatePlayerRuleset() => new OsuRuleset();

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset) => new Beatmap
        {
            BeatmapInfo = { Ruleset = ruleset },
            HitObjects =
            {
                new HitCircle { StartTime = 1000, Position = new Vector2(256, 192) },
                new HitCircle { StartTime = break_end + 1000, Position = new Vector2(256, 192) },
            },
            Breaks = { new BreakPeriod(break_start, break_end) },
        };

        [TearDown]
        public void TearDown() => LocalInterfaceSettings.GetSkipBreaks().SetDefault();

        private SkipOverlay? breakSkip => Player.ChildrenOfType<SkipOverlay>().FirstOrDefault(o => o.IsAlive && o.IsButtonVisible);

        [Test]
        public void TestSkipBreak()
        {
            CreateTest();
            AddUntilStep("wait for break", () => Player.IsBreakTime.Value && Player.GameplayClockContainer.CurrentTime > break_start);
            AddUntilStep("skip button shown", () => breakSkip != null);
            AddAssert("early in break", () => Player.GameplayClockContainer.CurrentTime < break_end - 8000);
            AddStep("press skip", () => InputManager.Key(Key.Space));
            AddAssert("3s of the break left", () => Player.GameplayClockContainer.CurrentTime >= break_end - BreakOverlay.BREAK_FADE_DURATION - 3000 - 100);
            AddAssert("playfield is there too", () => Player.DrawableRuleset.FrameStableClock.CurrentTime >= break_end - BreakOverlay.BREAK_FADE_DURATION - 3000 - 100);
            AddAssert("frame stable again", () => Player.DrawableRuleset.FrameStablePlayback);
            AddAssert("still in break", () => Player.IsBreakTime.Value);
            AddUntilStep("skip button gone", () => breakSkip == null);
        }

        [Test]
        public void TestSettingOff()
        {
            AddStep("turn setting off", () => LocalInterfaceSettings.GetSkipBreaks().Value = false);
            CreateTest();
            AddUntilStep("wait for break", () => Player.IsBreakTime.Value && Player.GameplayClockContainer.CurrentTime > break_start + 500);
            AddWaitStep("wait", 5);
            AddAssert("no skip button", () => breakSkip == null);
            AddAssert("still early in break", () => Player.GameplayClockContainer.CurrentTime < break_end - 5000);
        }
    }
}

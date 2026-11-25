// Patchsu 0007 local test (not part of the patch).

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Osu.UI.Cursor;
using osuTK;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.PlayerSettings;

namespace osu.Game.Tests.Visual.Gameplay
{
    public partial class TestSceneReplayModToggles : RateAdjustedBeatmapTestScene
    {
        private TestReplayPlayer player = null!;

        [Test]
        public void TestToggleRateAndHidden()
        {
            AddStep("create player", () =>
            {
                var ruleset = new OsuRuleset();
                Beatmap.Value = CreateWorkingBeatmap(ruleset.RulesetInfo);
                SelectedMods.Value = new Mod[] { ruleset.GetAutoplayMod()!, new OsuModDoubleTime(), new OsuModHidden(), new OsuModFlashlight() };
                player = new TestReplayPlayer(false);
            });
            AddStep("load", () => LoadScreen(player));
            AddUntilStep("loaded", () => player.IsLoaded);
            AddUntilStep("wait for first hit", () => player.ScoreProcessor.TotalScore.Value > 0);

            AddAssert("three toggles", () => player.ChildrenOfType<ReplayModSettings>().Single().ChildrenOfType<PlayerCheckbox>().Count(), () => Is.EqualTo(3));
            AddAssert("track at 1.5x", () => Beatmap.Value.Track.Rate, () => Is.EqualTo(1.5).Within(0.001));

            AddStep("switch DT off", () => checkbox("Double Time").Current.Value = false);
            AddAssert("track at 1x", () => Beatmap.Value.Track.Rate, () => Is.EqualTo(1).Within(0.001));
            AddAssert("gameplay rate still 1.5x", () => player.GameplayClockContainer.GetTrueGameplayRate(), () => Is.EqualTo(1.5).Within(0.001));

            AddStep("switch HD off", () => checkbox("Hidden").Current.Value = false);
            AddAssert("fade-in restored", () =>
            {
                var circle = player.GameplayState.Beatmap.HitObjects.OfType<HitCircle>().First();
                return circle.TimeFadeIn;
            }, () => Is.EqualTo(400).Within(0.001));

            AddStep("switch HD on", () => checkbox("Hidden").Current.Value = true);
            AddAssert("fade-in hidden", () =>
            {
                var circle = player.GameplayState.Beatmap.HitObjects.OfType<HitCircle>().First();
                return circle.TimeFadeIn;
            }, () => Is.EqualTo(player.GameplayState.Beatmap.HitObjects.OfType<HitCircle>().First().TimePreempt * 0.4).Within(0.001));

            AddStep("switch FL off", () => checkbox("Flashlight").Current.Value = false);
            AddStep("switch DT on", () => checkbox("Double Time").Current.Value = true);
            AddAssert("track at 1.5x again", () => Beatmap.Value.Track.Rate, () => Is.EqualTo(1.5).Within(0.001));
        }

        [Test]
        public void TestToggleGrow()
        {
            AddStep("create player", () =>
            {
                var ruleset = new OsuRuleset();
                Beatmap.Value = CreateWorkingBeatmap(ruleset.RulesetInfo);
                SelectedMods.Value = new Mod[] { ruleset.GetAutoplayMod()!, new OsuModGrow { StartScale = { Value = 0.5f } } };
                player = new TestReplayPlayer(false);
            });
            AddStep("load", () => LoadScreen(player));
            AddUntilStep("loaded", () => player.IsLoaded);
            AddUntilStep("a circle is growing", () => player.ChildrenOfType<DrawableHitCircle>().Any(c => c.Scale.X < 0.99f));

            AddStep("switch GR off", () => checkbox("Grow").Current.Value = false);
            AddAssert("all circles full size", () => player.ChildrenOfType<DrawableHitCircle>().All(c => c.Scale == Vector2.One));
            AddWaitStep("wait", 5);
            AddAssert("still full size", () => player.ChildrenOfType<DrawableHitCircle>().All(c => c.Scale == Vector2.One));

            AddStep("switch GR on", () => checkbox("Grow").Current.Value = true);
            AddUntilStep("a circle is growing again", () => player.ChildrenOfType<DrawableHitCircle>().Any(c => c.Scale.X < 0.99f));
        }

        [Test]
        public void TestToggleBloom()
        {
            AddStep("create player", () =>
            {
                var ruleset = new OsuRuleset();
                Beatmap.Value = CreateWorkingBeatmap(ruleset.RulesetInfo);
                SelectedMods.Value = new Mod[] { ruleset.GetAutoplayMod()!, new OsuModBloom { MaxSizeComboCount = { Value = 5 } } };
                player = new TestReplayPlayer(false);
            });
            AddStep("load", () => LoadScreen(player));
            AddUntilStep("loaded", () => player.IsLoaded);
            AddUntilStep("cursor bloomed", () => cursor().ModScaleAdjust.Value, () => Is.GreaterThan(2));

            AddStep("switch BM off", () => checkbox("Bloom").Current.Value = false);
            AddUntilStep("cursor back to normal size", () => cursor().ModScaleAdjust.Value, () => Is.EqualTo(1).Within(0.01));

            AddStep("switch BM on", () => checkbox("Bloom").Current.Value = true);
            AddUntilStep("cursor bloomed again", () => cursor().ModScaleAdjust.Value, () => Is.GreaterThan(2));
        }

        private OsuCursor cursor() => (OsuCursor)player.ChildrenOfType<OsuPlayfield>().Single().Cursor!.ActiveCursor;

        private PlayerCheckbox checkbox(string name) => player.ChildrenOfType<ReplayModSettings>().Single().ChildrenOfType<PlayerCheckbox>().Single(c => c.LabelText.ToString() == name);
    }
}

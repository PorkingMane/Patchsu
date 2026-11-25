// Patchsu 0007b local test (not part of the patch).

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Screens.Play.PlayerSettings;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.Gameplay
{
    public partial class TestSceneReplayModTogglesRitsu : RateAdjustedBeatmapTestScene
    {
        private TestReplayPlayer player = null!;

        [Test]
        public void TestToggleRitsuMods()
        {
            double preemptWithOneEye = 0;

            AddStep("create player", () =>
            {
                var ruleset = new OsuRuleset();
                Beatmap.Value = CreateWorkingBeatmap(ruleset.RulesetInfo);
                SelectedMods.Value = new Mod[] { ruleset.GetAutoplayMod()!, new OsuModOneEye(), new OsuModDecoyCircles { IntervalBeats = { Value = 2 } }, new OsuModMetronome(), new OsuModHidden() };
                player = new TestReplayPlayer(false);
            });
            AddStep("load", () => LoadScreen(player));
            AddUntilStep("loaded", () => player.IsLoaded);
            AddUntilStep("wait for first hit", () => player.ScoreProcessor.TotalScore.Value > 0);

            AddAssert("four toggles", () => player.ChildrenOfType<ReplayModSettings>().Single().ChildrenOfType<PlayerCheckbox>().Count(), () => Is.EqualTo(4));
            AddStep("remember preempt", () => preemptWithOneEye = firstCircle().TimePreempt);

            AddStep("switch One Eye off", () => checkbox("One Eye").Current.Value = false);
            AddAssert("preempt shorter", () => firstCircle().TimePreempt, () => Is.LessThan(preemptWithOneEye));
            AddStep("switch HD off", () => checkbox("Hidden").Current.Value = false);
            AddAssert("fade-in normal", () => firstCircle().TimeFadeIn, () => Is.EqualTo(400 * System.Math.Min(1, firstCircle().TimePreempt / 450)).Within(0.01));
            AddStep("switch One Eye on", () => checkbox("One Eye").Current.Value = true);
            AddAssert("preempt restored", () => firstCircle().TimePreempt, () => Is.EqualTo(preemptWithOneEye));

            AddStep("switch decoys off", () => checkbox("Decoy Circles").Current.Value = false);
            AddUntilStep("decoys invisible", () => player.ChildrenOfType<DrawableDecoyCircle>().Where(d => d.IsAlive).All(d => d.Colour.AverageColour.Linear.A == 0) && player.ChildrenOfType<DrawableDecoyCircle>().Any(d => d.IsAlive));
            AddStep("switch metronome off", () => checkbox("Metronome").Current.Value = false);
            AddUntilStep("wait for completion", () => player.ScoreProcessor.HasCompleted.Value);
            AddAssert("no misses", () => player.ScoreProcessor.Combo.Value, () => Is.EqualTo(player.ScoreProcessor.HighestCombo.Value));
        }

        private HitCircle firstCircle() => player.GameplayState.Beatmap.HitObjects.OfType<HitCircle>().First(h => h is not DecoyCircle);

        private PlayerCheckbox checkbox(string name) => player.ChildrenOfType<ReplayModSettings>().Single().ChildrenOfType<PlayerCheckbox>().Single(c => c.LabelText.ToString() == name);
    }
}

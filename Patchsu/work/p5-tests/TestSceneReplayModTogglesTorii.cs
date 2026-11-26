// Patchsu 0005 + 0501 local test (replay toggles of the Torii mods, old 0007c) (not part of the patch).

using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.PlayerSettings;

namespace osu.Game.Tests.Visual.Gameplay
{
    public partial class TestSceneReplayModTogglesTorii : RateAdjustedBeatmapTestScene
    {
        private TestReplayPlayer player = null!;

        [Test]
        public void TestToggleToriiMods()
        {
            AddStep("create player", () =>
            {
                var ruleset = new OsuRuleset();
                Beatmap.Value = CreateWorkingBeatmap(ruleset.RulesetInfo);
                SelectedMods.Value = new Mod[] { ruleset.GetAutoplayMod()!, new OsuModHitSounder(), new ModPitchAdjust { PitchShift = { Value = 1.5 } } };
                player = new TestReplayPlayer(false);
            });
            AddStep("load", () => LoadScreen(player));
            AddUntilStep("loaded", () => player.IsLoaded);
            AddUntilStep("wait for first hit", () => player.ScoreProcessor.TotalScore.Value > 0);

            AddAssert("two toggles", () => player.ChildrenOfType<ReplayModSettings>().Single().ChildrenOfType<PlayerCheckbox>().Count(), () => Is.EqualTo(2));
            AddAssert("pitch up", () => Beatmap.Value.Track.AggregateFrequency.Value, () => Is.EqualTo(1.5).Within(0.001));
            AddAssert("notes silent", () => firstCircle().Samples.Count, () => Is.EqualTo(0));

            AddStep("switch PA off", () => checkbox("Pitch Adjust").Current.Value = false);
            AddAssert("pitch normal", () => Beatmap.Value.Track.AggregateFrequency.Value, () => Is.EqualTo(1).Within(0.001));
            AddAssert("speed normal", () => Beatmap.Value.Track.Rate, () => Is.EqualTo(1).Within(0.001));
            AddAssert("gameplay rate 1x", () => player.GameplayClockContainer.GetTrueGameplayRate(), () => Is.EqualTo(1).Within(0.001));

            AddStep("switch HS off", () => checkbox("Hit Sounder").Current.Value = false);
            AddAssert("notes have samples", () => firstCircle().Samples.Count, () => Is.GreaterThan(0));

            AddStep("switch both on", () =>
            {
                checkbox("Hit Sounder").Current.Value = true;
                checkbox("Pitch Adjust").Current.Value = true;
            });
            AddAssert("notes silent again", () => firstCircle().Samples.Count, () => Is.EqualTo(0));
            AddAssert("pitch up again", () => Beatmap.Value.Track.AggregateFrequency.Value, () => Is.EqualTo(1.5).Within(0.001));
        }

        private HitCircle firstCircle() => player.GameplayState.Beatmap.HitObjects.OfType<HitCircle>().First();

        private PlayerCheckbox checkbox(string name) => player.ChildrenOfType<ReplayModSettings>().Single().ChildrenOfType<PlayerCheckbox>().Single(c => c.LabelText.ToString() == name);
    }
}

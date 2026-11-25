// Patchsu 0002 local test (not part of the patch): Speedrun profiles ("⋮" → New 'Speedrun' profile, goal window, greenish timer).
// Copy into osu.Game.Tests/Visual/Navigation and run `dotnet test --filter TestSceneSpeedrunProfiles`.

using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.LocalPriority;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Rulesets.Osu;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneSpeedrunProfiles : OsuGameTestScene
    {
        private LocalProfileManager manager => Game.Dependencies.Get<LocalProfileManager>();
        private LocalProfileOverlay overlay => Game.ChildrenOfType<LocalProfileOverlay>().Single();

        private SpriteIcon? moreButton() => overlay.ChildrenOfType<SpriteIcon>().FirstOrDefault(i => i.Icon.Equals(FontAwesome.Solid.EllipsisV));

        private OsuSpriteText? text(string value) => Game.ChildrenOfType<OsuSpriteText>().FirstOrDefault(t => t.Text.ToString() == value && t.IsPresent);

        private LocalSpeedrunGoalWindow? window() => overlay.ChildrenOfType<LocalSpeedrunGoalWindow>().SingleOrDefault(w => w.IsPresent);

        private LocalSpeedrunTimeDisplay? timer() => overlay.ChildrenOfType<LocalSpeedrunTimeDisplay>().FirstOrDefault(t => t.Alpha > 0 && t.Text.Length > 0);

        private void startOnPlainProfile()
        {
            AddStep("create profile", () =>
            {
                var profile = manager.CreateDefaultProfile();
                manager.Switch(profile.Id);
                ((DummyAPIAccess)API).LocalUser.Value = profile.ToUser();
            });
            AddUntilStep("page ready", () => Game.ChildrenOfType<LocalProfileOverlay>().SingleOrDefault()?.IsLoaded == true);
            AddStep("open page", () => overlay.Show());
            AddUntilStep("⋮ shown", () => moreButton()?.IsPresent == true);
            AddWaitStep("let the page slide in", 10);
        }

        private void openGoalWindow()
        {
            AddStep("left-click ⋮", () =>
            {
                InputManager.MoveMouseTo(moreButton()!);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("Speedrun row shown", () => text("New 'Speedrun' profile") != null);
            AddStep("click Speedrun", () =>
            {
                InputManager.MoveMouseTo(text("New 'Speedrun' profile")!);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("goal window open", () => window() != null);
        }

        private OsuTextBox goalBox() => window()!.ChildrenOfType<OsuTextBox>().Single();

        [Test]
        public void TestCreateAndComplete()
        {
            LocalProfile? speedrunProfile = null;
            int profilesBefore = 0;

            startOnPlainProfile();
            AddStep("count profiles", () => profilesBefore = manager.GetProfiles().Count);
            openGoalWindow();

            AddAssert("nothing made yet", () => manager.GetProfiles().Count, () => Is.EqualTo(profilesBefore));
            AddAssert("one goal field", () => window()!.ChildrenOfType<OsuTextBox>().Count(), () => Is.EqualTo(1));

            AddStep("start without a goal", () =>
            {
                InputManager.MoveMouseTo(text("Start the run")!);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("error shown", () => text("Type in PP, or a rank after a #.") != null);
            AddAssert("still nothing made", () => manager.GetProfiles().Count, () => Is.EqualTo(profilesBefore));

            AddStep("type #1#0 (a # only at the start)", () => goalBox().Text = "#1#0");
            AddStep("start", () => InputManager.Key(Key.Enter));
            AddUntilStep("rank error", () => text("Type the rank as #10000.") != null);
            AddStep("type 1,2x50 (digits and # only)", () => goalBox().Text = "1,2x50");
            AddAssert("box has 1250", () => goalBox().Text, () => Is.EqualTo("1250"));
            AddStep("press enter in the box", () =>
            {
                InputManager.MoveMouseTo(goalBox());
                InputManager.Click(MouseButton.Left);
                InputManager.Key(Key.Enter);
            });

            AddUntilStep("window closed", () => window() == null);
            AddUntilStep("speedrun profile active", () => (speedrunProfile = manager.ActiveProfile.Value)?.Speedrun != null);
            AddAssert("named SP Profile 1", () => speedrunProfile!.Name, () => Is.EqualTo("SP Profile 1"));
            AddAssert("goal saved", () => (speedrunProfile!.Speedrun!.TargetPP, speedrunProfile.Speedrun.TargetRank, speedrunProfile.Speedrun.Ruleset),
                () => Is.EqualTo(((double?)1250, (int?)null, "osu")));
            AddAssert("timer running", () => manager.IsSpeedrunRunning(speedrunProfile!.Id));

            AddUntilStep("timer on the page", () => timer() != null);
            AddAssert("timer is greenish white", () => timer()!.Colour.TopLeft.SRGB.G > 0.95f && timer()!.Colour.TopLeft.SRGB.R is > 0.8f and < 0.9f && timer()!.Colour.TopLeft.SRGB.B is > 0.8f and < 0.95f);
            AddUntilStep("goal under it, with its estimated rank", () => text($"#{LocalPriorityPolicy.EstimateGlobalRank(1250):N0} • {1250:N0}pp") != null);
            string? first = null;
            AddStep("note time", () => first = timer()!.Text);
            AddWaitStep("wait", 5);
            AddAssert("counts up", () => timer()!.Text, () => Is.Not.EqualTo(first));

            AddStep("not enough PP", () => manager.CheckSpeedrunGoal(speedrunProfile!.Id, new OsuRuleset().RulesetInfo, 1200));
            AddWaitStep("wait", 2);
            AddAssert("still running", () => manager.IsSpeedrunRunning(speedrunProfile!.Id));

            AddStep("reach the goal", () => manager.CheckSpeedrunGoal(speedrunProfile!.Id, new OsuRuleset().RulesetInfo, 1300));
            AddUntilStep("timer stopped", () => !manager.IsSpeedrunRunning(speedrunProfile!.Id));
            AddAssert("completed and saved", () => manager.Store.GetSpeedrun(speedrunProfile!.Id)?.Completed, () => Is.Not.Null);
            AddUntilStep("timer turned blue", () => timer()!.Colour.TopLeft.SRGB.B > 0.9f && timer()!.Colour.TopLeft.SRGB.R < 0.7f);
            AddUntilStep("notification posted", () => Game.ChildrenOfType<NotificationOverlay>().Single().ChildrenOfType<LocalSpeedrunCompletedNotification>()
                                                          .Any(n => Regex.IsMatch(n.Text.ToString(), @"^You've completed the run in \d+:\d\d\.\d\d!$")));
            AddAssert("notification uses the classic icon", () => Game.ChildrenOfType<NotificationOverlay>().Single().ChildrenOfType<LocalSpeedrunCompletedNotification>()
                                                                      .First().Icon.Equals(osu.Game.Graphics.OsuIcon.ModClassic));
            string? final = null;
            AddStep("note final time", () => final = timer()!.Text);
            AddWaitStep("wait", 5);
            AddAssert("time stands still", () => timer()!.Text, () => Is.EqualTo(final));
            AddAssert("time saved matches", () => LocalSpeedrun.FormatTime(manager.Store.GetSpeedrun(speedrunProfile!.Id)!.Seconds), () => Is.EqualTo(final));
        }

        [Test]
        public void TestTimeOnlyCountsWhileActive()
        {
            LocalProfile run = null!;
            LocalProfile other = null!;
            double paused = 0;

            AddStep("create speedrun + other", () =>
            {
                other = manager.CreateDefaultProfile();
                run = manager.CreateSpeedrunProfile(new LocalSpeedrun { TargetPP = 100 });
                manager.Switch(run.Id);
            });
            AddUntilStep("running", () => manager.IsSpeedrunRunning(run.Id));
            AddWaitStep("wait", 10);
            AddStep("switch away", () => manager.Switch(other.Id));
            AddUntilStep("stopped", () => !manager.IsSpeedrunRunning(run.Id));
            AddStep("note saved time", () => paused = manager.Store.GetSpeedrun(run.Id)!.Seconds);
            AddAssert("some time saved", () => paused, () => Is.GreaterThan(0));
            AddWaitStep("wait", 10);
            AddAssert("no time added while away", () => manager.Store.GetSpeedrun(run.Id)!.Seconds, () => Is.EqualTo(paused));
            AddStep("goal reached while away does nothing", () => manager.CheckSpeedrunGoal(run.Id, new OsuRuleset().RulesetInfo, 500));
            AddWaitStep("wait", 2);
            AddAssert("not completed", () => manager.Store.GetSpeedrun(run.Id)!.Completed, () => Is.Null);
            AddStep("switch back", () => manager.Switch(run.Id));
            AddUntilStep("running again", () => manager.IsSpeedrunRunning(run.Id));
            AddUntilStep("continues from the saved time", () => manager.RunningSpeedrunSeconds, () => Is.GreaterThan(paused));
            AddStep("mania PP doesn't count", () => manager.CheckSpeedrunGoal(run.Id, new osu.Game.Rulesets.Mania.ManiaRuleset().RulesetInfo, 500));
            AddWaitStep("wait", 2);
            AddAssert("still running", () => manager.IsSpeedrunRunning(run.Id));
            AddAssert("second speedrun numbered 2", () => manager.CreateSpeedrunProfile(new LocalSpeedrun { TargetPP = 1 }).Name, () => Is.EqualTo("SP Profile 2"));
            AddAssert("counted", () => manager.GetSpeedrunProfileCount(), () => Is.EqualTo(2));
            AddStep("delete it", () => manager.Delete(manager.GetProfiles().Single(p => p.Name == "SP Profile 2").Id, deleteScores: true));
            AddAssert("deleted ones still count: SP Profile 3", () => manager.CreateSpeedrunProfile(new LocalSpeedrun { TargetPP = 1 }).Name, () => Is.EqualTo("SP Profile 3"));
            AddStep("reset counts (Settings)", () => manager.ResetDeathProfileCounts());
            AddAssert("SP count zeroed", () => manager.GetSpeedrunProfileCount(), () => Is.EqualTo(0));
            AddAssert("SP Profile 1 still exists, so SP Profile 2", () => manager.CreateSpeedrunProfile(new LocalSpeedrun { TargetPP = 1 }).Name, () => Is.EqualTo("SP Profile 2"));
        }

        [Test]
        public void TestRankGoal()
        {
            startOnPlainProfile();
            openGoalWindow();
            AddStep("type 400", () => goalBox().Text = "400");
            AddUntilStep("estimated rank shown", () => text($"#{LocalPriorityPolicy.EstimateGlobalRank(400):N0} • 400pp") != null);
            AddStep("type #10000", () => goalBox().Text = "#10000");
            AddUntilStep("estimated PP shown", () => text($"#{10000:N0} • {LocalPriorityPolicy.EstimatePPForRank(10000):N0}pp") != null);
            AddStep("type #", () => goalBox().Text = "#");
            AddUntilStep("no estimate for an unfinished goal", () => !window()!.ChildrenOfType<OsuSpriteText>().Any(t => t.IsPresent && t.Text.ToString().Contains('•')));
            AddStep("type #10000", () => goalBox().Text = "#10000");
            AddStep("start", () =>
            {
                InputManager.MoveMouseTo(text("Start the run")!);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("rank goal only", () => manager.ActiveProfile.Value?.Speedrun is { TargetPP: null, TargetRank: 10000 });
            AddUntilStep("goal under the timer", () => text($"#{10000:N0} • {LocalPriorityPolicy.EstimatePPForRank(10000):N0}pp") != null);
        }

        [Test]
        public void TestGoalChecks()
        {
            AddAssert("pp shown rounded", () => new LocalSpeedrun { TargetPP = 1000 }.IsReachedBy(999.6));
            AddAssert("pp below", () => !new LocalSpeedrun { TargetPP = 1000 }.IsReachedBy(999.4));
            AddAssert("rank", () => new LocalSpeedrun { TargetRank = LocalPriorityPolicy.EstimateGlobalRank(5000) }.IsReachedBy(5000));
            AddAssert("rank not yet", () => !new LocalSpeedrun { TargetRank = LocalPriorityPolicy.EstimateGlobalRank(5000) }.IsReachedBy(4000));
            AddAssert("both needed", () => !new LocalSpeedrun { TargetPP = 9000, TargetRank = LocalPriorityPolicy.EstimateGlobalRank(5000) }.IsReachedBy(6000));
            AddAssert("rank only in osu!", () => !new LocalSpeedrun { Ruleset = "mania", TargetRank = 1_000_000 }.IsReachedBy(5000));
            AddAssert("no goal never", () => !new LocalSpeedrun().IsReachedBy(5000));

            foreach (int rank in new[] { 1, 3, 50, 119, 120, 121, 553, 10000, 500000, 2852393, 2852394, 5000000 })
            {
                AddAssert($"PP for #{rank} is the least that reaches it", () =>
                {
                    double pp = LocalPriorityPolicy.EstimatePPForRank(rank);
                    return pp >= 1 && pp % 1 == 0 && LocalPriorityPolicy.EstimateGlobalRank(pp) <= rank && (pp == 1 || LocalPriorityPolicy.EstimateGlobalRank(pp - 1) > rank);
                });
            }

            AddAssert("ranks better than the curve's best are reachable", () => new LocalSpeedrun { TargetRank = 3 }.IsReachedBy(LocalPriorityPolicy.EstimatePPForRank(3)));
            AddAssert("#3 needs more than #120", () => LocalPriorityPolicy.EstimatePPForRank(3), () => Is.GreaterThan(LocalPriorityPolicy.EstimatePPForRank(120) + 1000));
            AddAssert("curve unchanged below its best sample", () => LocalPriorityPolicy.EstimateGlobalRank(17051.7), () => Is.EqualTo(211));
            AddAssert("pp goal described", () => LocalSpeedrun.DescribeGoal("osu", 400, null), () => Is.EqualTo($"#{LocalPriorityPolicy.EstimateGlobalRank(400):N0} • {400:N0}pp"));
            AddAssert("rank goal described", () => LocalSpeedrun.DescribeGoal("osu", null, 3), () => Is.EqualTo($"#3 • {LocalPriorityPolicy.EstimatePPForRank(3):N0}pp"));
            AddAssert("no estimate outside osu!", () => LocalSpeedrun.DescribeGoal("mania", 400, null), () => Is.EqualTo($"{400:N0}pp"));
            AddAssert("format", () => LocalSpeedrun.FormatTime(3847.329), () => Is.EqualTo("1:04:07.32"));
            AddAssert("format short", () => LocalSpeedrun.FormatTime(247.329), () => Is.EqualTo("4:07.32"));
        }
    }
}

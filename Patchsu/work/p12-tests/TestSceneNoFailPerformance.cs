// Patchsu 0012 local test (not part of the patch): Settings → Patchsu → Patch 12 → "Remove 'No Fail' from PP calculation".
// Copy into osu.Game.Tests/Visual/Navigation and run `dotnet test --filter TestSceneNoFailPerformance`.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Database;
using osu.Game.LocalPriority;
using osu.Game.Online;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Resources;
using osu.Game.Tests.Beatmaps.IO;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneNoFailPerformance : OsuGameTestScene
    {
        private LocalProfileManager manager => Game.Dependencies.Get<LocalProfileManager>();

        private double profilePP() => LocalProfileStatistics.CalculatePerformanceAsync(manager.Store.ActiveProfile!, new OsuRuleset().RulesetInfo, Game.Dependencies.Get<osu.Game.Database.RealmAccess>(),
            Game.BeatmapManager, manager.Store, CancellationToken.None).GetResultSafely();

        [Test]
        public void TestToggle()
        {
            LocalProfile profile = null!;
            double ppBefore = 0;
            ScoreBasedUserStatisticsUpdate? update = null;

            AddStep("create profile", () =>
            {
                profile = manager.CreateDefaultProfile();
                manager.Switch(profile.Id);
                manager.CountEverythingAsRanked.Value = true;
            });
            PushAndConfirm(() => new osu.Game.Screens.Select.SoloSongSelect());
            AddStep("import beatmap", () => BeatmapImportHelper.LoadQuickOszIntoOsu(Game).WaitSafely());
            AddUntilStep("wait for selected", () => !Game.Beatmap.IsDefault);
            AddStep("import NF score", () =>
            {
                var beatmap = Game.Beatmap.Value.BeatmapInfo;
                var ruleset = new OsuRuleset();
                var mods = new osu.Game.Rulesets.Mods.Mod[] { new OsuModNoFail() };
                var playable = Game.BeatmapManager.GetWorkingBeatmap(beatmap).GetPlayableBeatmap(ruleset.RulesetInfo, mods);
                var processor = ruleset.CreateScoreProcessor();
                processor.ApplyBeatmap(playable);
                var max = processor.MaximumStatistics;
                var statistics = max.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value);
                statistics[HitResult.Great] -= 3;
                statistics[HitResult.Miss] = 3;

                Game.ScoreManager.Import(new ScoreInfo
                {
                    Hash = Guid.NewGuid().ToString(),
                    BeatmapInfo = beatmap,
                    BeatmapHash = beatmap.Hash,
                    Ruleset = ruleset.RulesetInfo,
                    User = new APIUser { Id = profile.UserId, Username = profile.Name },
                    Mods = mods,
                    Statistics = statistics,
                    MaximumStatistics = new Dictionary<HitResult, int>(max),
                    MaxCombo = max.Where(k => k.Key.AffectsCombo()).Sum(k => k.Value) / 2,
                    Accuracy = 0.98,
                    TotalScore = 600000,
                    Rank = ScoreRank.A,
                    Date = DateTimeOffset.Now,
                });
            });
            AddStep("PP with NF penalty", () => ppBefore = profilePP());
            AddAssert("has PP", () => ppBefore, () => Is.GreaterThan(0));

            AddStep("watch toolbar updates", () => manager.LatestStatisticsUpdate.BindValueChanged(e => update = e.NewValue));
            AddStep("open settings", () => Game.Settings.Show());
            AddUntilStep("Patch 12 subsection", () => Game.Settings.ChildrenOfType<LocalNoFailPerformanceSettings>().SingleOrDefault()?.IsLoaded == true);
            AddStep("tick checkbox", () => Game.Settings.ChildrenOfType<LocalNoFailPerformanceSettings>().Single().ChildrenOfType<FormCheckBox>().First().Current.Value = true);
            AddUntilStep("toolbar shows PP going up", () => update != null && update.After.PP > update.Before.PP);
            AddAssert("before matches old PP", () => (double)update!.Before.PP!.Value, () => Is.EqualTo(ppBefore).Within(0.01));
            AddAssert("profile PP is higher", () => profilePP(), () => Is.GreaterThan(ppBefore));
            AddAssert("setting saved", () => LocalNoFailPerformance.Enabled);

            AddStep("untick", () =>
            {
                update = null;
                Game.Settings.ChildrenOfType<LocalNoFailPerformanceSettings>().Single().ChildrenOfType<FormCheckBox>().First().Current.Value = false;
            });
            AddUntilStep("toolbar shows PP going down", () => update != null && update.After.PP < update.Before.PP);
            AddAssert("profile PP back", () => profilePP(), () => Is.EqualTo(ppBefore).Within(1e-6));
        }
    }
}

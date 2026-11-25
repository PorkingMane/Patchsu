// Patchsu 0006 local test (not part of the patch): Settings → Patchsu → Patch 6 → "Reduce PP for pauses" animates the toolbar PP.
// Copy into osu.Game.Tests/Visual/Navigation and run `dotnet test --filter TestScenePausePenaltyToolbar`.

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
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Resources;
using osu.Game.Tests.Beatmaps.IO;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestScenePausePenaltyToolbar : OsuGameTestScene
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
            AddStep("import score with 2 pauses", () =>
            {
                var beatmap = Game.Beatmap.Value.BeatmapInfo;
                var ruleset = new OsuRuleset();
                var mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>();
                var playable = Game.BeatmapManager.GetWorkingBeatmap(beatmap).GetPlayableBeatmap(ruleset.RulesetInfo, mods);
                var processor = ruleset.CreateScoreProcessor();
                processor.ApplyBeatmap(playable);
                var max = processor.MaximumStatistics;
                var statistics = max.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value);
                statistics[HitResult.Great] -= 3;
                statistics[HitResult.Miss] = 3;

                var score = new ScoreInfo
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
                };
                score.Pauses.Add(5000);
                score.Pauses.Add(15000);
                Game.ScoreManager.Import(score);
            });
            AddStep("PP without pause penalty", () => ppBefore = profilePP());
            AddAssert("has PP", () => ppBefore, () => Is.GreaterThan(0));

            AddStep("watch toolbar updates", () => manager.LatestStatisticsUpdate.BindValueChanged(e => update = e.NewValue));
            AddStep("open settings", () => Game.Settings.Show());
            AddUntilStep("Patch 6 subsection", () => Game.Settings.ChildrenOfType<LocalPausePenaltySettings>().SingleOrDefault()?.IsLoaded == true);
            AddStep("tick checkbox", () => Game.Settings.ChildrenOfType<LocalPausePenaltySettings>().Single().ChildrenOfType<FormCheckBox>().Single().Current.Value = true);
            AddUntilStep("toolbar shows PP going down", () => update != null && update.After.PP < update.Before.PP);
            AddAssert("before matches old PP", () => (double)update!.Before.PP!.Value, () => Is.EqualTo(ppBefore).Within(0.01));
            AddAssert("profile PP is lower", () => profilePP(), () => Is.LessThan(ppBefore));
            AddAssert("setting saved", () => LocalPausePenalty.Enabled);

            AddStep("untick", () =>
            {
                update = null;
                Game.Settings.ChildrenOfType<LocalPausePenaltySettings>().Single().ChildrenOfType<FormCheckBox>().Single().Current.Value = false;
            });
            AddUntilStep("toolbar shows PP going up", () => update != null && update.After.PP > update.Before.PP);
            AddAssert("profile PP back", () => profilePP(), () => Is.EqualTo(ppBefore).Within(1e-6));
        }
    }
}

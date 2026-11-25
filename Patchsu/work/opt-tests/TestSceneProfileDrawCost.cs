// Patchsu optimisation pass local test (not part of any patch): how much of the local profile page is actually drawn.
// Rows scrolled out of view must not be drawn (masked away). Counts drawn rows and drawn leaf drawables (sprites, boxes, text),
// a stand-in for draw calls (headless tests have no GPU). Copy into osu.Game.Tests/Visual/Navigation, run
// `dotnet test --filter TestSceneProfileDrawCost --logger "console;verbosity=detailed"` and read the DRAWCOST lines.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.LocalPriority;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Beatmaps.IO;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneProfileDrawCost : OsuGameTestScene
    {
        private LocalProfileManager manager => Game.Dependencies.Get<LocalProfileManager>();
        private LocalProfileOverlay overlay => Game.ChildrenOfType<LocalProfileOverlay>().Single();

        private static readonly System.Reflection.PropertyInfo is_masked_away =
            typeof(Drawable).GetProperty("IsMaskedAway", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;

        private static bool isDrawn(Drawable d, Drawable root)
        {
            for (Drawable? a = d; a != null; a = a.Parent)
            {
                if (!a.IsPresent || (bool)is_masked_away.GetValue(a)!)
                    return false;

                if (a == root)
                    return true;
            }

            return false;
        }

        private (int rows, int allRows, int leaves) measure()
        {
            var all = overlay.ChildrenOfType<Drawable>().ToArray();
            var rows = all.Where(d => d.GetType().Name is "ScoreRow" or "MostPlayedRow").ToArray();
            return (rows.Count(r => isDrawn(r, overlay)), rows.Length, all.Count(d => d is not CompositeDrawable && isDrawn(d, overlay)));
        }

        [Test]
        public void TestExpandedListOnlyDrawsVisibleRows()
        {
            LocalProfile profile = null!;

            AddStep("create profile", () =>
            {
                profile = manager.CreateDefaultProfile();
                manager.Switch(profile.Id);
            });
            AddStep("import beatmap", () => BeatmapImportHelper.LoadQuickOszIntoOsu(Game).WaitSafely());
            AddStep("import 60 scores", () =>
            {
                var realm = Game.Dependencies.Get<RealmAccess>();
                var beatmap = realm.Run(r => r.All<BeatmapInfo>().AsEnumerable().Where(b => b.Ruleset.ShortName == "osu").Detach().ToArray())
                                  .First(b => Game.BeatmapManager.GetWorkingBeatmap(b).GetPlayableBeatmap(new OsuRuleset().RulesetInfo).HitObjects.Count > 20);
                var ruleset = new OsuRuleset();
                var playable = Game.BeatmapManager.GetWorkingBeatmap(beatmap).GetPlayableBeatmap(ruleset.RulesetInfo, Array.Empty<Mod>());
                var processor = ruleset.CreateScoreProcessor();
                processor.ApplyBeatmap(playable);
                var max = processor.MaximumStatistics;

                for (int i = 0; i < 60; i++)
                {
                    var statistics = max.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value);
                    statistics[HitResult.Great] -= i % 10;
                    statistics[HitResult.Miss] = i % 10;

                    Game.ScoreManager.Import(new ScoreInfo
                    {
                        Hash = Guid.NewGuid().ToString(),
                        BeatmapInfo = beatmap,
                        BeatmapHash = beatmap.Hash,
                        Ruleset = ruleset.RulesetInfo,
                        User = new APIUser { Id = profile.UserId, Username = profile.Name },
                        Mods = i % 3 == 0 ? new Mod[] { new osu.Game.Rulesets.Osu.Mods.OsuModHidden() } : Array.Empty<Mod>(),
                        Statistics = statistics,
                        MaximumStatistics = new Dictionary<HitResult, int>(max),
                        MaxCombo = 100,
                        Accuracy = 0.9 + i / 1000.0,
                        TotalScore = 500000 + i,
                        Rank = ScoreRank.A,
                        Date = DateTimeOffset.Now.AddMinutes(-i),
                    });
                }
            });
            AddUntilStep("page ready", () => Game.ChildrenOfType<LocalProfileOverlay>().SingleOrDefault()?.IsLoaded == true);
            AddStep("open page", () => overlay.Show());
            AddUntilStep("statistics loaded", () => overlay.ChildrenOfType<osu.Game.Overlays.Profile.ProfileSection>().Count() == 2);
            AddWaitStep("settle", 10);
            AddStep("measure compact", () =>
            {
                var m = measure();
                Console.WriteLine($"DRAWCOST compact: drawn rows {m.rows}/{m.allRows}, drawn leaves {m.leaves}");
            });
            AddStep("expand recent plays", () => overlay.ChildrenOfType<ShowMoreButton>().Last().TriggerClick());
            AddUntilStep("50 recent rows", () => measure().allRows >= 50);
            AddWaitStep("settle", 10);
            AddStep("measure expanded", () =>
            {
                var m = measure();
                Console.WriteLine($"DRAWCOST expanded (top of page): drawn rows {m.rows}/{m.allRows}, drawn leaves {m.leaves}");
            });
            AddStep("scroll to the bottom", () => overlay.ChildrenOfType<OverlayScrollContainer>().First().ScrollToEnd(false));
            AddWaitStep("settle", 10);
            AddStep("measure expanded, scrolled", () =>
            {
                var m = measure();
                Console.WriteLine($"DRAWCOST expanded (bottom of page): drawn rows {m.rows}/{m.allRows}, drawn leaves {m.leaves}");
            });
            AddAssert("rows out of view aren't drawn", () => measure().rows, () => Is.LessThan(30));
        }
    }
}

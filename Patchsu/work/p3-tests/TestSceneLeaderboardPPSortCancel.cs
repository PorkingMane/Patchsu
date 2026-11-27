// Patchsu patch 3 test (not part of the patch): a PP sort that is cancelled while a play's PP is being calculated (the
// leaderboard is fetched again) doesn't leave that play without PP, sorted last, for the rest of the session.
// Copy into osu.Game.Tests/Visual/SongSelect and run `dotnet test --filter TestSceneLeaderboardPPSortCancel`.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneLeaderboardPPSortCancel : OsuTestScene
    {
        private readonly GatedDifficultyCache difficultyCache = new GatedDifficultyCache();

        private BeatmapManager beatmapManager = null!;
        private LeaderboardManager leaderboardManager = null!;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

            var rulesets = new RealmRulesetStore(Realm);
            dependencies.CacheAs<RulesetStore>(rulesets);
            dependencies.Cache(beatmapManager = new BeatmapManager(LocalStorage, Realm, null, dependencies.Get<AudioManager>(), Resources, dependencies.Get<GameHost>(), Beatmap.Default));
            dependencies.CacheAs<BeatmapDifficultyCache>(difficultyCache);
            dependencies.Cache(leaderboardManager = new LeaderboardManager());
            Dependencies.Cache(Realm);

            return dependencies;
        }

        [BackgroundDependencyLoader]
        private void load() => Add(leaderboardManager);

        [Test]
        public void TestCancelledCalculationIsNotKept()
        {
            BeatmapInfo beatmap = null!;

            AddStep("import beatmap and two plays", () =>
            {
                beatmapManager.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely();
                beatmap = beatmapManager.GetAllUsableBeatmapSets().First().Beatmaps.First(b => b.Ruleset.OnlineID == 0);

                Realm.Write(r =>
                {
                    var b = r.Find<BeatmapInfo>(beatmap.ID)!;
                    r.Add(score(b, 300_000));
                    r.Add(score(b, 400_000));
                });
            });

            AddStep("sort by PP", () => fetch(beatmap, LeaderboardSortMode.Performance));
            AddUntilStep("calculating", () => difficultyCache.Calls, () => Is.GreaterThan(0));

            AddStep("fetch again (by score)", () => fetch(beatmap, LeaderboardSortMode.Score));
            AddUntilStep("calculation cancelled", () => difficultyCache.Cancelled, () => Is.EqualTo(1));
            AddWaitStep("let it finish", 5);

            AddStep("sort by PP again", () =>
            {
                difficultyCache.Gate.Set();
                fetch(beatmap, LeaderboardSortMode.Performance);
            });
            AddUntilStep("sorted", () => leaderboardManager.Scores.Value?.TopScores.Count, () => Is.EqualTo(2));
            AddAssert("every play has PP", () => leaderboardManager.Scores.Value!.TopScores.All(s => s.PP != null));
        }

        private void fetch(BeatmapInfo beatmap, LeaderboardSortMode sorting)
            => leaderboardManager.FetchWithCriteria(new LeaderboardCriteria(beatmap, new OsuRuleset().RulesetInfo, BeatmapLeaderboardScope.Local, null, sorting), forceRefresh: true);

        private static ScoreInfo score(BeatmapInfo b, long total) => new ScoreInfo(b, b.Ruleset, new osu.Game.Models.RealmUser { OnlineID = 2, Username = "someone" })
        {
            TotalScore = total,
            Accuracy = 0.98,
            MaxCombo = 100,
            Rank = ScoreRank.A,
            Date = DateTimeOffset.Now,
            BeatmapHash = b.Hash,
            StatisticsJson = @"{""great"":98,""ok"":2}",
            MaximumStatisticsJson = @"{""great"":100}",
        };

        /// <summary>
        /// Waits for <see cref="Gate"/>; like lazer's cache, returns null (doesn't throw) when cancelled.
        /// </summary>
        private partial class GatedDifficultyCache : BeatmapDifficultyCache
        {
            public readonly ManualResetEventSlim Gate = new ManualResetEventSlim();
            public int Calls;
            public int Cancelled;

            public override async Task<StarDifficulty?> GetDifficultyAsync(IBeatmapInfo beatmapInfo, IRulesetInfo? rulesetInfo = null, IEnumerable<Mod>? mods = null,
                                                                            CancellationToken cancellationToken = default, int computationDelay = 0)
            {
                Interlocked.Increment(ref Calls);

                try
                {
                    await Task.Run(() => Gate.Wait(cancellationToken), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref Cancelled);
                    return null;
                }

                return new StarDifficulty(new OsuDifficultyAttributes { StarRating = 5, MaxCombo = 100 }, new PerformanceAttributes());
            }
        }
    }
}

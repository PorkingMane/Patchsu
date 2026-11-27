// Patchsu patch 3 test (not part of the patch): PP calculated for the PP sort is calculated again when the same leaderboard is
// fetched again (a PP option like 0009's "Remove 'No Fail' from PP calculation" changed), instead of keeping the old value.
// Copy into osu.Game.Tests/Visual/SongSelect and run `dotnet test --filter TestSceneLeaderboardPPSortRefresh`.

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
    public partial class TestSceneLeaderboardPPSortRefresh : OsuTestScene
    {
        private readonly ChangingDifficultyCache difficultyCache = new ChangingDifficultyCache();

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
        public void TestRefetchRecalculates()
        {
            BeatmapInfo beatmap = null!;
            double firstPP = 0;

            AddStep("import beatmap and a play", () =>
            {
                beatmapManager.Import(TestResources.GetQuickTestBeatmapForImport()).WaitSafely();
                beatmap = beatmapManager.GetAllUsableBeatmapSets().First().Beatmaps.First(b => b.Ruleset.OnlineID == 0);

                Realm.Write(r =>
                {
                    var b = r.Find<BeatmapInfo>(beatmap.ID)!;
                    r.Add(score(b, 300_000));
                });
            });

            AddStep("sort by PP", () => fetch(beatmap, LeaderboardSortMode.Performance));
            AddUntilStep("has PP", () => leaderboardManager.Scores.Value?.TopScores.SingleOrDefault()?.PP, () => Is.Not.Null);
            AddStep("remember PP", () => firstPP = leaderboardManager.Scores.Value!.TopScores.Single().PP!.Value);

            AddStep("other beatmap selected", () => leaderboardManager.FetchWithCriteria(new LeaderboardCriteria(null, null, BeatmapLeaderboardScope.Local, null), forceRefresh: true));
            AddStep("PP calculation changes", () => difficultyCache.Aim = 4);
            AddStep("back, sort by PP", () => fetch(beatmap, LeaderboardSortMode.Performance));
            AddUntilStep("other beatmap keeps known PP", () => leaderboardManager.Scores.Value?.TopScores.SingleOrDefault()?.PP, () => Is.EqualTo(firstPP));

            AddStep("same leaderboard fetched again", () => leaderboardManager.FetchWithCriteria(leaderboardManager.CurrentCriteria!, forceRefresh: true));
            AddUntilStep("PP calculated again", () => leaderboardManager.Scores.Value?.TopScores.SingleOrDefault()?.PP, () => Is.GreaterThan(firstPP));
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

        private partial class ChangingDifficultyCache : BeatmapDifficultyCache
        {
            public volatile int Aim = 2;

            public override Task<StarDifficulty?> GetDifficultyAsync(IBeatmapInfo beatmapInfo, IRulesetInfo? rulesetInfo = null, IEnumerable<Mod>? mods = null,
                                                                      CancellationToken cancellationToken = default, int computationDelay = 0)
                => Task.FromResult<StarDifficulty?>(new StarDifficulty(new OsuDifficultyAttributes { StarRating = 5, AimDifficulty = Aim, SpeedDifficulty = 2, MaxCombo = 100 },
                    new PerformanceAttributes()));
        }
    }
}

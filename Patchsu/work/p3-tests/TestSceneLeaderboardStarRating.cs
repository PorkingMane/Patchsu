// Patchsu patch 3 test (not part of the patch): the star rating sits left of the statistics, in the same spot on every
// row, whatever the date's width.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osuTK;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneLeaderboardStarRating : SongSelectComponentsTestScene
    {
        [Cached]
        private OverlayColourProvider colourProvider { get; set; } = new OverlayColourProvider(OverlayColourScheme.Aquamarine);

        [Cached(typeof(BeatmapDifficultyCache))]
        private readonly FixedDifficultyCache difficultyCache = new FixedDifficultyCache();

        private FillFlowContainer flow = null!;

        [TestCase(true)]
        [TestCase(false)]
        public void TestStarRatingLinesUp(bool sheared)
        {
            AddStep("create rows", () =>
            {
                Child = new PopoverContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = flow = new FillFlowContainer
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 50,
                        Width = 900,
                        AutoSizeAxes = Axes.Y,
                        Spacing = new Vector2(0, 2),
                        Shear = sheared ? OsuGame.SHEAR : Vector2.Zero,
                    }
                };

                // "10mos" vs "3yrs" vs "2d": dates of different widths.
                foreach (var date in new[] { DateTimeOffset.Now.AddMonths(-10), DateTimeOffset.Now.AddYears(-3), DateTimeOffset.Now.AddDays(-2) })
                {
                    flow.Add(new BeatmapLeaderboardScore(createScore(date), sheared)
                    {
                        Rank = 1,
                        Shear = Vector2.Zero,
                    });
                }

                foreach (var row in flow.Children)
                    row.Show();
            });

            AddUntilStep("star ratings shown", () => starRatings().All(s => s.Alpha == 1));

            AddAssert("same spot on every row", () =>
            {
                // in the flow's own space, so the shear doesn't shift lower rows.
                var xs = starRatings().Select(s => flow.ToLocalSpace(s.ScreenSpaceDrawQuad.Centre).X).ToArray();
                return xs.Max() - xs.Min() < 0.5f;
            });

            AddAssert("left of the statistics", () => flow.Children.All(row =>
            {
                var star = row.ChildrenOfType<StarRatingDisplay>().Single();
                var statistics = row.ChildrenOfType<FillFlowContainer>().Single(f => f.Name == @"Statistics container");
                return star.ScreenSpaceDrawQuad.TopRight.X < statistics.ScreenSpaceDrawQuad.TopLeft.X;
            }));

            AddAssert("level with the max combo caption", () => flow.Children.All(row =>
            {
                var star = row.ChildrenOfType<StarRatingDisplay>().Single();
                var caption = row.ChildrenOfType<osu.Game.Graphics.Sprites.OsuSpriteText>().First(t => t.Text.ToString().Equals("MAX COMBO", StringComparison.OrdinalIgnoreCase));
                return Math.Abs(star.ScreenSpaceDrawQuad.Centre.Y - caption.ScreenSpaceDrawQuad.Centre.Y) < 1;
            }));

            // song select's leaderboard rows are about this wide at 1366x768.
            AddStep("song select width", () => flow.Width = 595);
            AddUntilStep("star ratings still shown", () => starRatings().All(s => s.Alpha == 1));

            AddStep("narrow rows", () => flow.Width = 300);
            AddUntilStep("star ratings hidden", () => starRatings().All(s => s.Alpha == 0));
        }

        private IEnumerable<StarRatingDisplay> starRatings() => flow.ChildrenOfType<StarRatingDisplay>();

        private static ScoreInfo createScore(DateTimeOffset date) => new ScoreInfo
        {
            Rank = ScoreRank.S,
            Accuracy = 0.98,
            MaxCombo = 443,
            TotalScore = 1_128_465,
            Ruleset = new OsuRuleset().RulesetInfo,
            BeatmapInfo = new BeatmapInfo(),
            User = new APIUser { Id = 3, Username = @"someone" },
            Date = date,
        };

        private partial class FixedDifficultyCache : BeatmapDifficultyCache
        {
            public override Task<StarDifficulty?> GetDifficultyAsync(IBeatmapInfo beatmapInfo, IRulesetInfo? rulesetInfo = null, IEnumerable<Mod>? mods = null,
                                                                     CancellationToken cancellationToken = default, int computationDelay = 0)
                => Task.FromResult<StarDifficulty?>(new StarDifficulty(9.24, 0));
        }
    }
}

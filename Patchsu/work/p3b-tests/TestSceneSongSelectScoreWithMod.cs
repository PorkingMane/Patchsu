// Patchsu patch 3b test (not part of the patch): the "scorewithmod" and "playdate" search keywords in song select, with real local scores.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Scoring;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneSongSelectScoreWithMod : SongSelectTestScene
    {
        private ShearedFilterTextBox filterTextBox => SongSelect.ChildrenOfType<ShearedFilterTextBox>().Single();

        private BeatmapSetInfo[] sets = null!;

        [Test]
        public void TestScoreWithMod()
        {
            for (int i = 0; i < 5; i++)
                ImportBeatmapForRuleset(_ => { }, 1, 0);

            AddStep("log in", () =>
            {
                API.Login("user1", string.Empty);
                API.AuthenticateSecondFactor("abcdefgh");
            });

            Guid nmScore = Guid.Empty;

            AddStep("add scores", () =>
            {
                sets = Beatmaps.GetAllUsableBeatmapSets().OrderBy(b => b.OnlineID).ToArray();

                ScoreManager.Import(score(sets[0], new OsuModHidden(), new OsuModDoubleTime()));
                ScoreManager.Import(score(sets[1], new OsuModHidden()));
                nmScore = ScoreManager.Import(score(sets[2], new OsuModClassic()))!.PerformRead(s => s.ID);
                // someone else's HD play doesn't count.
                ScoreManager.Import(score(sets[3], s => s.User = new APIUser { Id = 1337, Username = "user2" }, new OsuModHidden()));
                // a taiko play doesn't count in osu!.
                ScoreManager.Import(score(sets[4], s => s.Ruleset = Rulesets.GetRuleset(1)!, new OsuModHidden()));
                // an old EZ play and an old HD play.
                ScoreManager.Import(score(sets[0], s => s.Date = DateTimeOffset.Now.AddDays(-200), new OsuModEasy()));
                ScoreManager.Import(score(sets[1], s => s.Date = DateTimeOffset.Now.AddDays(-40), new OsuModHidden()));
            });

            LoadSongSelect();

            search("scorewithmod=HD", 0, 1);
            search("scorewithmod='Hidden'", 0, 1);
            search("playwithmod=HD", 0, 1);
            search("scoreswithmod=HD", 0, 1);
            search("playwithmod=HD scoreswithmod!=DT", 1);
            search("scorewithmod=HD,DT", 0);
            search("scorewithmod='Hidden' scorewithmod='Double Time'", 0);
            search("scorewithmod=HD scorewithmod!=DT", 1);
            search("scorewithmod=NM", 2);
            search("scorewithmod=FL");

            search("playdate<=180d", 0, 1, 2);
            search("scoredate>180d", 0);
            search("datescore>4w", 0, 1);
            search("playdate>4w playdate<1y", 0, 1);
            search("playdate>4w playdate<60d", 1);
            search("playdate=40d", 1);
            search("playdate!=40d", 0, 1, 2);
            search("playdate>6M", 0);
            // the same play has to match both: set 0's EZ play is old, its HD play is new.
            search("scorewithmod=HD playdate>4w", 1);
            search("scorewithmod=EZ playdate>4w", 0);
            search("scorewithmod=EZ playdate<4w");
            search("scorewithmod=NM playdate<4w", 2);
            search("", 0, 1, 2, 3, 4);

            // a new play shows up without changing the search.
            search("scorewithmod=HR");
            AddStep("add HR play", () => ScoreManager.Import(score(sets[3], new OsuModHardRock())));
            assertShown(3);

            // a deleted play goes away.
            search("scorewithmod=NM", 2);
            AddStep("delete NM play", () => ScoreManager.Delete(s => s.ID == nmScore));
            assertShown();
        }

        private void search(string text, params int[] expectedSets)
        {
            AddStep($"search \"{text}\"", () => filterTextBox.Current.Value = text);
            assertShown(expectedSets);
        }

        private void assertShown(params int[] expectedSets)
        {
            AddUntilStep($"sets {string.Join(", ", expectedSets)} shown", () =>
            {
                if (SongSelect.IsFiltering)
                    return false;

                var shown = Carousel.Filters.OfType<Screens.Select.BeatmapCarouselFilterGrouping>().Single().SetItems.Keys.Select(s => s.BeatmapSet.ID).ToHashSet();
                return shown.SetEquals(expectedSets.Select(i => sets[i].ID));
            });
        }

        private ScoreInfo score(BeatmapSetInfo set, params Mod[] mods) => score(set, null, mods);

        private ScoreInfo score(BeatmapSetInfo set, Action<ScoreInfo>? apply, params Mod[] mods)
        {
            var score = TestResources.CreateTestScoreInfo(set.Beatmaps[0]);
            score.User = API.LocalUser.Value;
            score.Mods = mods;
            score.Date = DateTimeOffset.Now;
            apply?.Invoke(score);
            return score;
        }
    }
}

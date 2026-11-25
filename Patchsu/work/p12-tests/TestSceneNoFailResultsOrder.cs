// Patchsu 0012 local test (not part of the patch): with "Also remove 'No Fail' score reduction" on, the results screen's
// score panels are ordered by the score as shown (a No Fail play shown at 1,000,000 goes before a 600,000 play).
// Copy into osu.Game.Tests/Visual/Ranking and run `dotnet test --filter TestSceneNoFailResultsOrder`.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.LocalPriority;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.Ranking
{
    public partial class TestSceneNoFailResultsOrder : OsuManualInputManagerTestScene
    {
        private ScorePanelList list = null!;
        private ScoreInfo noFail = null!;
        private ScoreInfo plain = null!;

        private float x(ScoreInfo score) => list.ChildrenOfType<ScorePanel>().Single(p => p.Score.Equals(score)).ScreenSpaceDrawQuad.Centre.X;

        private void addScores()
        {
            AddStep("create list", () =>
            {
                noFail = TestResources.CreateTestScoreInfo();
                noFail.Mods = new Mod[] { new OsuModNoFail() };
                noFail.TotalScore = 500_000;

                plain = TestResources.CreateTestScoreInfo();
                plain.Mods = new Mod[] { new OsuModHidden() };
                plain.TotalScore = 600_000;

                Child = list = new ScorePanelList { Anchor = Anchor.Centre, Origin = Anchor.Centre };
            });
            AddUntilStep("wait for load", () => list.IsLoaded);
            AddStep("add scores", () =>
            {
                list.AddScore(plain);
                list.AddScore(noFail);
            });
            AddWaitStep("let panels settle", 5);
        }

        [Test]
        public void TestOrderFollowsShownScore()
        {
            AddStep("turn on (PP + score)", () =>
            {
                LocalNoFailPerformance.SetEnabled(true);
                LocalNoFailPerformance.SetScoreEnabled(true);
            });
            addScores();
            AddUntilStep("No Fail play first", () => x(noFail) < x(plain));
            AddStep("turn off", () =>
            {
                LocalNoFailPerformance.SetScoreEnabled(false);
                LocalNoFailPerformance.SetEnabled(false);
            });
        }

        [Test]
        public void TestOrderUnchangedWhenOff()
        {
            addScores();
            AddUntilStep("plain play first", () => x(plain) < x(noFail));
        }
    }
}

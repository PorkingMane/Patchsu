// Patchsu local test (not part of the patch). Copy into osu.Game.Tests/Visual/Navigation and run:
//   dotnet test osu.Game.Tests --filter TestScenePerformanceSimulation

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Simulation;
using osu.Game.Screens.Select;
using osu.Game.Tests.Resources;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Tests.Beatmaps.IO;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestScenePerformanceSimulation : OsuGameTestScene
    {
        private SoloSongSelect songSelect = null!;

        private PerformanceSimulationScreen screen => (PerformanceSimulationScreen)Game.ScreenStack.CurrentScreen;

        private EditableStatistic[] stats => screen.ChildrenOfType<EditableStatistic>().ToArray();

        // order: accuracy, combo, pp, 300, 100, 50, miss, slider tick, slider end
        private EditableStatistic stat(int index) => stats[index];

        private void enterSongSelect()
        {
            PushAndConfirm(() => songSelect = new SoloSongSelect());
            AddUntilStep("wait for song select", () => songSelect.CarouselItemsPresented);
            AddStep("import beatmap", () => BeatmapImportHelper.LoadQuickOszIntoOsu(Game).WaitSafely());
            AddUntilStep("wait for selected", () => !Game.Beatmap.IsDefault);
        }

        private void type(Func<EditableStatistic> statistic, string text)
        {
            AddStep($"type {text}", () =>
            {
                var s = statistic();
                InputManager.MoveMouseTo(s.Box);
                InputManager.Click(MouseButton.Left);
            });
            AddUntilStep("focused", () => statistic().Box.HasFocus);
            AddStep($"enter {text}", () => statistic().Box.Text = text);
        }

        private void commit() => AddStep("unfocus", () => InputManager.Key(Key.Enter));

        [Test]
        public void TestFromOptionsMenu()
        {
            enterSongSelect();

            AddStep("open options", () => Game.ChildrenOfType<FooterButtonOptions>().Single().TriggerClick());
            AddUntilStep("popover shown", () => Game.ChildrenOfType<FooterButtonOptions.Popover>().Any(p => p.IsPresent));
            AddStep("click simulate", () => Game.ChildrenOfType<FooterButtonOptions.Popover>().Single()
                                                .ChildrenOfType<OsuButton>().Single(b => b.Text.ToString() == "Simulate PP").TriggerClick());

            AddUntilStep("simulation screen", () => Game.ScreenStack.CurrentScreen is PerformanceSimulationScreen s && s.IsLoaded);
            AddUntilStep("stats loaded", () => stats.Length >= 7 && stats.All(s => s.IsLoaded));

            AddUntilStep("random / options gone", () => !Game.ChildrenOfType<FooterButtonRandom>().Any() && !Game.ChildrenOfType<FooterButtonOptions>().Any());
            AddAssert("one mods button", () => Game.ChildrenOfType<FooterButtonMods>().Count(), () => Is.EqualTo(1));
            AddAssert("reset to SS shown, off", () => resetButton("Reset to SS")?.Enabled.Value, () => Is.False);
            AddAssert("no reset to score", () => resetButton("Reset to score"), () => Is.Null);
            AddAssert("hint shown", () => Game.ChildrenOfType<ShearedButton>().Any(b => b.Text.ToString() == "Tab moves to the next one"));
            AddUntilStep("accuracy counter shows 100.00%", () => stat(0).ChildrenOfType<OsuSpriteText>().Any(t => t.Text.ToString() == "100.00%"));

            AddStep("tab with nothing focused", () => InputManager.Key(Key.Tab));
            AddAssert("accuracy focused", () => stat(0).Box.HasFocus);
            AddAssert("box shows the accuracy", () => stat(0).Box.Text, () => Is.EqualTo("100.00"));
            commit();
            AddAssert("accuracy not focused", () => !stat(0).Box.HasFocus);

            AddAssert("accuracy 100", () => stat(0).EditText(), () => Is.EqualTo("100.00"));
            AddAssert("pp shown", () => int.Parse(stat(2).EditText()), () => Is.GreaterThan(0));
            AddAssert("no 100s", () => stat(4).EditText(), () => Is.EqualTo("0"));

            int maxCombo = 0;
            int objects = 0;
            int ppSS = 0;
            AddStep("remember SS pp", () => ppSS = int.Parse(stat(2).EditText()));
            AddStep("remember", () =>
            {
                maxCombo = int.Parse(stat(1).EditText());
                objects = int.Parse(stat(3).EditText());
            });

            type(() => stat(4), "4");
            AddAssert("300s follow while typing", () => stat(3).EditText(), () => Is.EqualTo((objects - 4).ToString()));
            AddAssert("accuracy follows while typing", () => stat(0).EditText(), () => Is.Not.EqualTo("100.00"));
            AddAssert("combo untouched", () => stat(1).EditText(), () => Is.EqualTo(maxCombo.ToString()));

            type(() => stat(6), "2");
            AddAssert("combo capped by misses", () => stat(1).EditText(), () => Is.EqualTo((maxCombo - 2).ToString()));

            AddStep("tab to next box", () => InputManager.Key(Key.Tab));
            AddAssert("slider tick focused", () => stats.Length > 7 && stat(7).Box.HasFocus);
            commit();

            type(() => stat(1), "99999");
            AddAssert("combo clamped immediately", () => stat(1).EditText(), () => Is.EqualTo((maxCombo - 2).ToString()));
            commit();

            int ppBefore = 0;
            AddStep("remember pp", () => ppBefore = int.Parse(stat(2).EditText()));
            type(() => stat(2), "1");
            AddAssert("misses kept", () => stat(6).EditText(), () => Is.EqualTo("2"));
            AddAssert("100s / 50s added", () => int.Parse(stat(4).EditText()) + int.Parse(stat(5).EditText()), () => Is.GreaterThan(4));
            commit();
            AddAssert("pp lower", () => int.Parse(stat(2).EditText()), () => Is.LessThan(ppBefore));

            type(() => stat(6), "0");
            commit();
            AddStep("log size", () => Console.WriteLine($"objects={objects} combo={maxCombo}"));
            type(() => stat(0), "97,5");
            commit();
            AddAssert("accuracy close", () => double.Parse(stat(0).EditText(), System.Globalization.CultureInfo.InvariantCulture), () => Is.EqualTo(97.5).Within(100.0 / objects));
            AddAssert("no 50s at 97.5%", () => stat(5).EditText(), () => Is.EqualTo("0"));

            AddAssert("reset to SS on", () => resetButton("Reset to SS")?.Enabled.Value, () => Is.True);
            AddStep("reset to SS", () => resetButton("Reset to SS")!.TriggerClick());
            AddAssert("back at 100%", () => stat(0).EditText(), () => Is.EqualTo("100.00"));

            // mods are the simulation's own: the game's selection stays as it was.
            AddStep("simulate DT", () => Game.ChildrenOfType<FooterButtonMods>().Single().Mods.Value = new osu.Game.Rulesets.Mods.Mod[] { new osu.Game.Rulesets.Osu.Mods.OsuModDoubleTime() });
            AddUntilStep("pp went up", () => int.Parse(stat(2).EditText()), () => Is.GreaterThan(ppSS));
            AddAssert("game mods untouched", () => Game.SelectedMods.Value, () => Is.Empty);

            AddStep("back", () => Game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("song select", () => songSelect.IsCurrentScreen());
            AddUntilStep("random / options back", () => Game.ChildrenOfType<FooterButtonRandom>().Count() == 1 && Game.ChildrenOfType<FooterButtonOptions>().Count() == 1);
            AddUntilStep("one mods button", () => Game.ChildrenOfType<FooterButtonMods>().Count(), () => Is.EqualTo(1));
            AddUntilStep("reset buttons gone", () => resetButton("Reset to SS"), () => Is.Null);
        }

        [Test]
        public void TestFixChokeMouseButtons()
        {
            enterSongSelect();

            AddStep("open options", () => Game.ChildrenOfType<FooterButtonOptions>().Single().TriggerClick());
            AddUntilStep("popover shown", () => Game.ChildrenOfType<FooterButtonOptions.Popover>().Any(p => p.IsPresent));
            AddStep("click simulate", () => Game.ChildrenOfType<FooterButtonOptions.Popover>().Single()
                                                .ChildrenOfType<OsuButton>().Single(b => b.Text.ToString() == "Simulate PP").TriggerClick());
            AddUntilStep("simulation screen", () => Game.ScreenStack.CurrentScreen is PerformanceSimulationScreen s && s.IsLoaded);
            AddUntilStep("stats loaded", () => stats.Length >= 9 && stats.All(s => s.IsLoaded));
            AddUntilStep("fix choke off", () => resetButton("Fix choke")?.Enabled.Value == false);
            AddAssert("no tooltip", () => resetButton("Fix choke")!.TooltipText.ToString(), () => Is.Empty);

            int objects = 0;
            int maxCombo = 0;
            int ends = 0;
            AddStep("remember", () =>
            {
                objects = int.Parse(stat(3).EditText());
                maxCombo = int.Parse(stat(1).EditText());
                ends = int.Parse(stat(8).EditText());
            });

            foreach (var button in new[] { MouseButton.Left, MouseButton.Right })
            {
                type(() => stat(6), "3");
                commit();
                type(() => stat(7), "0");
                commit();
                type(() => stat(8), "0");
                commit();
                AddUntilStep("fix choke on", () => resetButton("Fix choke")!.Enabled.Value);

                AddStep($"{button} click fix choke", () =>
                {
                    InputManager.MoveMouseTo(resetButton("Fix choke")!);
                    InputManager.Click(button);
                });

                AddAssert("no misses", () => stat(6).EditText(), () => Is.EqualTo("0"));
                if (button == MouseButton.Left)
                    AddAssert("misses became 100s", () => stat(4).EditText(), () => Is.EqualTo("3"));
                else
                    AddAssert("misses became 300s", () => stat(3).EditText(), () => Is.EqualTo(objects.ToString()));
                AddAssert("slider ticks hit", () => stat(7).EditText(), () => Is.Not.EqualTo("0"));
                AddAssert("slider ends stay", () => stat(8).EditText(), () => Is.EqualTo("0"));
                AddAssert("combo full minus slider ends", () => stat(1).EditText(), () => Is.EqualTo((maxCombo - ends).ToString()));
                AddUntilStep("fix choke off", () => !resetButton("Fix choke")!.Enabled.Value);
                AddStep("reset to SS", () => resetButton("Reset to SS")!.TriggerClick());
            }
        }

        private ShearedButton? resetButton(string text) => Game.ChildrenOfType<ShearedButton>().FirstOrDefault(b => ((b as ShearedButtonWithIcon)?.Text ?? b.Text).ToString() == text && b.Parent?.Name == "Right footer buttons");

        [Test]
        public void TestFromResultsScreen()
        {
            enterSongSelect();

            AddStep("show results", () =>
            {
                var beatmap = Game.Beatmap.Value.BeatmapInfo;
                var ruleset = new OsuRuleset();
                var playable = Game.BeatmapManager.GetWorkingBeatmap(beatmap).GetPlayableBeatmap(ruleset.RulesetInfo, Array.Empty<osu.Game.Rulesets.Mods.Mod>());
                var processor = ruleset.CreateScoreProcessor();
                processor.ApplyBeatmap(playable);
                var max = processor.MaximumStatistics;
                var statistics = max.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value);
                statistics[HitResult.Great] -= 1;
                statistics[HitResult.Miss] = 1;

                songSelect.Push(new osu.Game.Screens.Ranking.SoloResultsScreen(new ScoreInfo(beatmap, ruleset.RulesetInfo)
                {
                    User = new APIUser { Username = "Tester" },
                    Statistics = statistics,
                    MaximumStatistics = new Dictionary<HitResult, int>(max),
                    MaxCombo = 10,
                    Accuracy = 0.99,
                    TotalScore = 500000,
                    Rank = ScoreRank.A,
                    Date = DateTimeOffset.Now,
                }));
            });

            AddUntilStep("calculator button", () => Game.ChildrenOfType<SimulatePerformanceButton>().Any(b => b.IsLoaded));
            AddStep("click it", () => Game.ChildrenOfType<SimulatePerformanceButton>().Single().TriggerClick());
            AddUntilStep("simulation screen", () => Game.ScreenStack.CurrentScreen is PerformanceSimulationScreen s && s.IsLoaded);
            AddUntilStep("stats loaded", () => stats.Length >= 7 && stats.All(s => s.IsLoaded));
            AddAssert("the play's miss", () => stat(6).EditText(), () => Is.EqualTo("1"));
            AddAssert("the play's combo", () => stat(1).EditText(), () => Is.EqualTo("10"));

            AddStep("back", () => Game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("results again", () => Game.ScreenStack.CurrentScreen is osu.Game.Screens.Ranking.SoloResultsScreen);
        }

        [Test]
        public void TestFromResultsScreenTakesOverPanel()
        {
            enterSongSelect();

            osu.Game.Screens.Ranking.SoloResultsScreen results = null!;
            osu.Game.Screens.Ranking.ScorePanel resultsPanel = null!;
            osuTK.Vector2 resultsTopLeft = default;
            var frames = new List<(osuTK.Vector2 topLeft, float resultsAlpha, bool resultsPresent, string combo)>();

            AddStep("show results", () =>
            {
                var beatmap = Game.Beatmap.Value.BeatmapInfo;
                var ruleset = new OsuRuleset();
                var playable = Game.BeatmapManager.GetWorkingBeatmap(beatmap).GetPlayableBeatmap(ruleset.RulesetInfo, Array.Empty<osu.Game.Rulesets.Mods.Mod>());
                var processor = ruleset.CreateScoreProcessor();
                processor.ApplyBeatmap(playable);
                var max = processor.MaximumStatistics;
                var statistics = max.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value);
                statistics[HitResult.Great] -= 1;
                statistics[HitResult.Miss] = 1;

                songSelect.Push(results = new osu.Game.Screens.Ranking.SoloResultsScreen(new ScoreInfo(beatmap, ruleset.RulesetInfo)
                {
                    User = new APIUser { Username = "Tester" },
                    Statistics = statistics,
                    MaximumStatistics = new Dictionary<HitResult, int>(max),
                    MaxCombo = 10,
                    Accuracy = 0.99,
                    TotalScore = 500000,
                    Rank = ScoreRank.A,
                    Date = DateTimeOffset.Now,
                }));
            });

            AddUntilStep("calculator button", () => Game.ChildrenOfType<SimulatePerformanceButton>().Any(b => b.IsLoaded));
            AddUntilStep("results panel settled", () =>
            {
                resultsPanel = results.ChildrenOfType<osu.Game.Screens.Ranking.ScorePanel>().FirstOrDefault(p => p.State == osu.Game.Screens.Ranking.PanelState.Expanded)!;
                return resultsPanel != null && !resultsPanel.ChildrenOfType<osu.Framework.Graphics.Drawable>().Any(d => d.Transforms.Any());
            });

            AddStep("click it, record frames", () =>
            {
                resultsTopLeft = resultsPanel.ScreenSpaceDrawQuad.TopLeft;
                Game.ChildrenOfType<SimulatePerformanceButton>().Single().TriggerClick();

                Scheduler.AddDelayed(() =>
                {
                    if (frames.Count >= 60 || Game.ScreenStack.CurrentScreen is not PerformanceSimulationScreen s || !s.IsLoaded)
                        return;

                    var panel = s.ChildrenOfType<SimulationPanelMiddleContent>().Single().Parent!.Parent!;
                    var comboCounter = s.ChildrenOfType<EditableStatistic>().ElementAtOrDefault(1)?.ChildrenOfType<OsuSpriteText>().FirstOrDefault(t => t.Text.ToString().Length > 0 && char.IsDigit(t.Text.ToString()[0]));
                    frames.Add((panel.ScreenSpaceDrawQuad.TopLeft, results.Alpha, results.IsPresent, comboCounter?.Text.ToString() ?? "?"));
                }, 0, true);
            });

            AddUntilStep("recorded", () => frames.Count >= 60);
            AddStep("print", () =>
            {
                TestContext.WriteLine($"results panel top-left {resultsTopLeft}");
                for (int i = 0; i < frames.Count; i += 16)
                    TestContext.WriteLine($"frame {i}: sim top-left {frames[i].topLeft} results alpha {frames[i].resultsAlpha} present {frames[i].resultsPresent} combo '{frames[i].combo}'");
            });
            AddAssert("starts on the results panel", () => (frames[0].topLeft - resultsTopLeft).Length, () => Is.LessThan(1));
            AddAssert("numbers shown right away", () => frames[0].combo, () => Does.StartWith("10"));
            AddAssert("results hidden at once", () => frames[0].resultsPresent, () => Is.False);
            // at this window size both panels may rest at the same spot; the slide must end at the screen's own spot either way.
            AddAssert("slid into its own place", () => screen.ChildrenOfType<SimulationPanelMiddleContent>().Single().Parent!.Parent!.Position, () => Is.EqualTo(osuTK.Vector2.Zero));
            AddAssert("no transforms left", () => screen.ChildrenOfType<SimulationPanelMiddleContent>().Single().Parent!.Parent!.Transforms.Any(), () => Is.False);

            AddStep("back", () => Game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("results again", () => Game.ScreenStack.CurrentScreen is osu.Game.Screens.Ranking.SoloResultsScreen);
            AddUntilStep("results visible again", () => results.Alpha, () => Is.EqualTo(1));
        }

        [Test]
        public void TestTakeoverSlidesFromOffsetPanel()
        {
            enterSongSelect();

            osuTK.Vector2? natural = null;
            var tops = new List<float>();

            AddStep("open normally to measure", () => songSelect.Push(new PerformanceSimulationScreen(Game.Beatmap.Value.BeatmapInfo)));
            AddUntilStep("loaded", () => Game.ScreenStack.CurrentScreen is PerformanceSimulationScreen s && s.IsLoaded && s.ChildrenOfType<SimulationPanelMiddleContent>().Any());
            AddWaitStep("settle", 5);
            AddStep("measure", () => natural = screen.ChildrenOfType<SimulationPanelMiddleContent>().Single().Parent!.Parent!.ScreenSpaceDrawQuad.TopLeft);
            AddStep("back", () => Game.ScreenStack.CurrentScreen.Exit());
            AddUntilStep("song select", () => songSelect.IsCurrentScreen());

            AddStep("open as if from a results panel 60 px lower", () =>
            {
                var from = screen_quad(natural!.Value + new osuTK.Vector2(0, 60));
                songSelect.Push(new PerformanceSimulationScreen(Game.Beatmap.Value.BeatmapInfo, null, from));

                Scheduler.AddDelayed(() =>
                {
                    if (tops.Count >= 40 || Game.ScreenStack.CurrentScreen is not PerformanceSimulationScreen s || !s.IsLoaded || !s.ChildrenOfType<SimulationPanelMiddleContent>().Any())
                        return;

                    tops.Add(s.ChildrenOfType<SimulationPanelMiddleContent>().Single().Parent!.Parent!.ScreenSpaceDrawQuad.TopLeft.Y);
                }, 0, true);
            });
            AddUntilStep("recorded", () => tops.Count >= 40);
            AddStep("print", () => TestContext.WriteLine($"natural {natural!.Value.Y}, tops {string.Join(" ", tops.Take(12).Select(t => t.ToString("0.0")))} ... {tops[^1]:0.0}"));
            AddAssert("starts 60 px lower", () => tops[0] - natural!.Value.Y, () => Is.EqualTo(60).Within(1));
            AddAssert("moves up steadily", () => tops.Zip(tops.Skip(1)).All(p => p.Second <= p.First + 0.01f));
            AddAssert("ends at its own spot", () => tops[^1] - natural!.Value.Y, () => Is.EqualTo(0).Within(1));
        }

        private static osu.Framework.Graphics.Primitives.Quad screen_quad(osuTK.Vector2 topLeft)
            => new osu.Framework.Graphics.Primitives.Quad(topLeft.X, topLeft.Y, 100, 100);

        [Test]
        public void TestFromScoreContextMenu()
        {
            enterSongSelect();

            AddStep("import score", () =>
            {
                var beatmap = Game.Beatmap.Value.BeatmapInfo;
                var ruleset = new OsuRuleset();
                var playable = Game.BeatmapManager.GetWorkingBeatmap(beatmap).GetPlayableBeatmap(ruleset.RulesetInfo, Array.Empty<osu.Game.Rulesets.Mods.Mod>());
                var processor = ruleset.CreateScoreProcessor();
                processor.ApplyBeatmap(playable);
                var max = processor.MaximumStatistics;

                var statistics = max.Where(k => !k.Key.IsBonus()).ToDictionary(k => k.Key, k => k.Value);
                statistics[HitResult.Great] -= 5;
                statistics[HitResult.Ok] = 3;
                statistics[HitResult.Miss] = 2;

                Game.ScoreManager.Import(new ScoreInfo
                {
                    Hash = Guid.NewGuid().ToString(),
                    BeatmapInfo = beatmap,
                    BeatmapHash = beatmap.Hash,
                    Ruleset = ruleset.RulesetInfo,
                    User = new APIUser { Username = "Tester" },
                    Statistics = statistics,
                    MaximumStatistics = new Dictionary<HitResult, int>(max),
                    MaxCombo = max.Where(k => k.Key.AffectsCombo()).Sum(k => k.Value) / 2,
                    Accuracy = 0.98,
                    TotalScore = 600000,
                    Rank = ScoreRank.A,
                    Date = DateTimeOffset.Now,
                });
            });

            AddUntilStep("score on leaderboard", () => songSelect.ChildrenOfType<BeatmapLeaderboardScore>().Any());
            AddStep("simulate from menu", () =>
            {
                var item = ((osu.Framework.Graphics.Cursor.IHasContextMenu)songSelect.ChildrenOfType<BeatmapLeaderboardScore>().First())
                           .ContextMenuItems.OfType<OsuMenuItem>().Single(i => i.Text.Value.ToString() == "Simulate PP");
                item.Action.Value!.Invoke();
            });

            AddUntilStep("simulation screen", () => Game.ScreenStack.CurrentScreen is PerformanceSimulationScreen s && s.IsLoaded);
            AddUntilStep("stats loaded", () => stats.Length >= 7 && stats.All(s => s.IsLoaded));
            AddAssert("score's misses", () => stat(6).EditText(), () => Is.EqualTo("2"));
            AddAssert("score's 100s", () => stat(4).EditText(), () => Is.EqualTo("3"));
            string combo = string.Empty;
            AddStep("remember combo", () => combo = stat(1).EditText());
            AddAssert("score's combo is half", () => int.Parse(combo) * 2, () => Is.EqualTo(int.Parse(stat(1).SuffixText!()!.TrimStart('/'))).Within(1));

            AddAssert("both reset buttons", () => resetButton("Reset to score") != null && resetButton("Reset to SS") != null);
            AddAssert("reset to score off", () => resetButton("Reset to score")!.Enabled.Value, () => Is.False);

            type(() => stat(6), "0");
            // No misses and no missed slider ticks left (the score has all ticks and ends): the choke is fixed, combo goes to max.
            AddAssert("fixed choke gives full combo", () => stat(1).EditText(), () => Is.EqualTo(stat(1).SuffixText!()!.TrimStart('/')));
            commit();
            AddAssert("reset to score on", () => resetButton("Reset to score")!.Enabled.Value, () => Is.True);

            AddStep("reset to SS", () => resetButton("Reset to SS")!.TriggerClick());
            AddAssert("no 100s", () => stat(4).EditText(), () => Is.EqualTo("0"));
            AddAssert("full combo", () => stat(1).EditText(), () => Is.EqualTo(stat(1).SuffixText!()!.TrimStart('/')));

            EditableStatistic? accuracyBefore = null;
            AddStep("remember panel", () => accuracyBefore = stat(0));
            AddStep("simulate HD", () => Game.ChildrenOfType<FooterButtonMods>().Single().Mods.Value = new osu.Game.Rulesets.Mods.Mod[] { new osu.Game.Rulesets.Osu.Mods.OsuModHidden() });
            AddUntilStep("panel shows HD", () => screen.ChildrenOfType<osu.Game.Screens.Play.HUD.ModDisplay>().Any(d => d.Current.Value.Any(m => m.Acronym == "HD")));
            AddAssert("still SS after the mod change", () => stat(4).EditText(), () => Is.EqualTo("0"));
            AddAssert("panel updated in place", () => stat(0), () => Is.SameAs(accuracyBefore));

            AddStep("reset to score", () => resetButton("Reset to score")!.TriggerClick());
            AddUntilStep("score's mods back", () => screen.ChildrenOfType<osu.Game.Screens.Play.HUD.ModDisplay>().Any(d => !d.Current.Value.Any()));
            AddUntilStep("score's misses back", () => stats.Length > 6 ? stat(6).EditText() : "", () => Is.EqualTo("2"));
            AddUntilStep("score's 100s back", () => stat(4).EditText(), () => Is.EqualTo("3"));
            AddUntilStep("reset to score off again", () => resetButton("Reset to score")!.Enabled.Value, () => Is.False);
            type(() => stat(6), "0");
            AddAssert("300s took the misses", () => stat(3).EditText(), () => Is.EqualTo((int.Parse(stat(1).SuffixText!()!.TrimStart('/')) >= 0 ? stat(3).EditText() : "")));
        }
    }
}

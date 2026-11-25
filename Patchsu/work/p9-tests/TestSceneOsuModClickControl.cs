// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Patchsu patch 9 test (not part of the patch). Copy into osu.Game.Rulesets.Osu.Tests/Mods/ and run with
// dotnet test osu.Game.Rulesets.Osu.Tests --filter "FullyQualifiedName~TestSceneOsuModClickControl"

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Replays;
using osu.Game.Rulesets.Replays;
using osu.Game.Rulesets.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    public partial class TestSceneOsuModClickControl : OsuModTestScene
    {
        private static readonly Vector2 empty_space = new Vector2(450, 350);

        private int extraMisses => Player.ScoreProcessor.HitEvents.Count(e => e.HitObject is OsuModClickControl.ClickControlMiss);
        private int statistic(HitResult result) => Player.ScoreProcessor.Statistics.GetValueOrDefault(result);
        private bool completed => Player.ScoreProcessor.HasCompleted.Value;

        [Test]
        public void TestUnnecessaryClickIsMiss() => CreateModTest(new ModTestData
        {
            Mod = new OsuModClickControl(),
            Autoplay = false,
            CreateBeatmap = () => circles(500, 1000, 1500),
            ReplayFrames = frames(press(500, 0), press(750, empty_space), press(1000, 1), press(1500, 2)),
            PassCondition = () => completed
                                  && extraMisses == 1
                                  && statistic(HitResult.Miss) == 1
                                  && statistic(HitResult.Great) == 3
                                  && Player.ScoreProcessor.Combo.Value == 2
                                  && Player.ScoreProcessor.HighestCombo.Value == 2
                                  && Precision(Player.ScoreProcessor.Accuracy.Value, 0.75)
                                  // the beatmap's maximum stays (3 circles, max combo 3)
                                  && Player.ScoreProcessor.MaximumStatistics.GetValueOrDefault(HitResult.Great) == 3
                                  && Player.ScoreProcessor.MaximumCombo == 3,
        });

        [Test]
        public void TestMoreMissesThanObjects() => CreateModTest(new ModTestData
        {
            Mod = new OsuModClickControl(),
            Autoplay = false,
            CreateBeatmap = () => circles(500, 1500),
            ReplayFrames = frames(press(500, 0), press(700, empty_space), press(800, empty_space), press(900, empty_space), press(1000, empty_space),
                press(1100, empty_space), press(1500, 1)),
            // The play still completes: the extra misses don't count as judged objects.
            PassCondition = () => completed && extraMisses == 5 && statistic(HitResult.Miss) == 5 && statistic(HitResult.Great) == 2,
        });

        [Test]
        public void TestClicksOutsideThePlayDontCount() => CreateModTest(new ModTestData
        {
            Mod = new OsuModClickControl(),
            Autoplay = false,
            CreateBeatmap = () => circles(1000, 9000),
            ReplayFrames = frames(
                press(200, empty_space), // before the first object
                press(1000, 0),
                press(3000, empty_space), // long gap (6 s until the next object)
                press(8000, empty_space), // 1 s before the next object: counts
                press(9000, 1),
                press(9300, empty_space)), // after the last object
            PassCondition = () => completed && extraMisses == 1 && statistic(HitResult.Great) == 2,
        });

        [Test]
        public void TestClickDuringBreakDoesntCount()
        {
            CreateModTest(new ModTestData
            {
                Mod = new OsuModClickControl(),
                Autoplay = false,
                CreateBeatmap = () =>
                {
                    var beatmap = circles(1000, 1500, 5500, 6000);
                    beatmap.Breaks.Add(new osu.Game.Beatmaps.Timing.BreakPeriod(1700, 5300));
                    return beatmap;
                },
                ReplayFrames = frames(press(1000, 0), press(1500, 1), press(3000, empty_space), press(5500, 2), press(5750, empty_space), press(6000, 3)),
                PassCondition = () => completed && extraMisses == 1 && statistic(HitResult.Great) == 4,
            });
        }

        [Test]
        public void TestClickOnCircleOutsideHitWindowIsMiss() => CreateModTest(new ModTestData
        {
            Mod = new OsuModClickControl(),
            Autoplay = false,
            // AR5: the second circle is alive from 800 ms, its miss window starts at 1600 ms.
            CreateBeatmap = () => circles(500, 2000),
            ReplayFrames = frames(press(500, 0), press(1200, 1), press(2000, 1)),
            PassCondition = () => completed && extraMisses == 1 && statistic(HitResult.Great) == 2,
        });

        [Test]
        public void TestEarlyClickInMissWindowIsNormalMiss() => CreateModTest(new ModTestData
        {
            Mod = new OsuModClickControl(),
            Autoplay = false,
            // A click in the circle's miss window judges it (a normal miss); it is not an extra one.
            CreateBeatmap = () => circles(500, 2000),
            ReplayFrames = frames(press(500, 0), press(1700, 1)),
            PassCondition = () => completed && extraMisses == 0 && statistic(HitResult.Miss) == 1 && statistic(HitResult.Great) == 1,
        });

        [Test]
        public void TestSpinnerTakesOneClick() => CreateModTest(new ModTestData
        {
            Mod = new OsuModClickControl(),
            Autoplay = false,
            CreateBeatmap = () => new Beatmap
            {
                HitObjects = new List<HitObject>
                {
                    new HitCircle { StartTime = 500, Position = position(0) },
                    new Spinner { StartTime = 1000, EndTime = 3000, Position = OsuPlayfieldCentre },
                    new HitCircle { StartTime = 3500, Position = position(1) },
                }
            },
            ReplayFrames = frames(press(500, 0), press(1100, empty_space), press(1500, empty_space), press(3500, 1)),
            PassCondition = () => completed && extraMisses == 1 && statistic(HitResult.Great) == 2,
        });

        [Test]
        public void TestAutoplayHasNoExtraMisses() => CreateModTest(new ModTestData
        {
            Mod = new OsuModClickControl(),
            Autoplay = true,
            CreateBeatmap = mixedBeatmap,
            PassCondition = () => completed && extraMisses == 0 && statistic(HitResult.Miss) == 0 && Precision(Player.ScoreProcessor.Accuracy.Value, 1),
        });

        [Test]
        public void TestHoldingIsNotClicking() => CreateModTest(new ModTestData
        {
            Mod = new OsuModClickControl(),
            Autoplay = false,
            CreateBeatmap = () => circles(500, 1000, 1500),
            // Left held from the first circle to the end, the others hit with right: a held button is one click.
            ReplayFrames = new List<ReplayFrame>
            {
                new OsuReplayFrame(500, position(0), OsuAction.LeftButton),
                new OsuReplayFrame(1000, position(1), OsuAction.LeftButton, OsuAction.RightButton),
                new OsuReplayFrame(1010, position(1), OsuAction.LeftButton),
                new OsuReplayFrame(1500, position(2), OsuAction.LeftButton, OsuAction.RightButton),
                new OsuReplayFrame(1510, position(2), OsuAction.LeftButton),
                new OsuReplayFrame(1600, position(2)),
            },
            PassCondition = () => completed && extraMisses == 0 && statistic(HitResult.Great) == 3,
        });

        private static readonly Vector2 OsuPlayfieldCentre = new Vector2(256, 192);

        private static bool Precision(double a, double b) => System.Math.Abs(a - b) < 1e-6;

        private static Vector2 position(int index) => new Vector2(60 + index * 80, 80);

        private static Beatmap circles(params double[] times) => new Beatmap
        {
            HitObjects = times.Select((t, i) => (HitObject)new HitCircle { StartTime = t, Position = position(i) }).ToList(),
        };

        private static Beatmap mixedBeatmap()
        {
            var beatmap = new Beatmap
            {
                HitObjects = new List<HitObject>
                {
                    new HitCircle { StartTime = 500, Position = position(0) },
                    new HitCircle { StartTime = 750, Position = position(1) },
                    new Slider
                    {
                        StartTime = 1000,
                        Position = position(2),
                        Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(0, 100) }),
                        RepeatCount = 1,
                    },
                    new HitCircle { StartTime = 2500, Position = position(3) },
                    new Spinner { StartTime = 3000, EndTime = 4500, Position = OsuPlayfieldCentre },
                    new HitCircle { StartTime = 5000, Position = position(4) },
                    new HitCircle { StartTime = 5100, Position = position(4) },
                }
            };

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
            return beatmap;
        }

        private static (double Time, Vector2 Position) press(double time, int circle) => (time, position(circle));

        private static (double Time, Vector2 Position) press(double time, Vector2 position) => (time, position);

        private static List<ReplayFrame> frames(params (double Time, Vector2 Position)[] presses)
        {
            var list = new List<ReplayFrame>();
            bool left = true;

            foreach (var (time, pos) in presses)
            {
                list.Add(new OsuReplayFrame(time, pos, left ? OsuAction.LeftButton : OsuAction.RightButton));
                list.Add(new OsuReplayFrame(time + 20, pos));
                left = !left;
            }

            return list;
        }
    }

    public partial class TestSceneOsuModClickControlFailing : OsuModTestScene
    {
        protected override bool AllowFail => true;

        [Test]
        public void TestSuddenDeathFailsOnUnnecessaryClick() => CreateModTest(new ModTestData
        {
            Mods = new Mod[] { new OsuModClickControl(), new OsuModSuddenDeath() },
            Autoplay = false,
            CreateBeatmap = () => new Beatmap
            {
                HitObjects = new List<HitObject>
                {
                    new HitCircle { StartTime = 500, Position = new Vector2(100) },
                    new HitCircle { StartTime = 1000, Position = new Vector2(200) },
                    new HitCircle { StartTime = 1500, Position = new Vector2(300) },
                }
            },
            ReplayFrames = new List<ReplayFrame>
            {
                new OsuReplayFrame(500, new Vector2(100), OsuAction.LeftButton),
                new OsuReplayFrame(520, new Vector2(100)),
                new OsuReplayFrame(750, new Vector2(450, 350), OsuAction.RightButton),
                new OsuReplayFrame(770, new Vector2(450, 350)),
            },
            PassCondition = () => Player.GameplayState.HasFailed,
        });
    }
}

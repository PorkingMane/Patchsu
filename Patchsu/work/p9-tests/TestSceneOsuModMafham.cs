// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Patchsu patch 9 test (not part of the patch). Copy into osu.Game.Rulesets.Osu.Tests/Mods/ and run with
// dotnet test osu.Game.Rulesets.Osu.Tests --filter "FullyQualifiedName~TestSceneOsuModMafham"

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.Skinning.Default;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Tests.Beatmaps;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    public partial class TestSceneOsuModMafham : OsuModTestScene
    {
        /// <summary>
        /// AR9: objects normally come alive 600 ms before they're hit.
        /// </summary>
        private const double normal_preempt = 600;

        private const double first_object_time = 1000;

        private OsuModMafham.MafhamLayer layer => Player.DrawableRuleset.ChildrenOfType<OsuModMafham.MafhamLayer>().Single();

        /// <summary>
        /// The playfield's (frame-stable) time, which can lag behind the gameplay clock right after a seek.
        /// </summary>
        private double time => Player.DrawableRuleset.Playfield.Time.Current;

        private bool caughtUp => !Player.DrawableRuleset.FrameStableClock.IsCatchingUp.Value;

        private IEnumerable<OsuHitObject> objects => Player.DrawableRuleset.Objects.Cast<OsuHitObject>();

        [SetUp]
        public void SetUp() => OsuModMafham.ClearPictureCache();

        [Test]
        public void TestModSelectDetails()
        {
            var mafham = new OsuModMafham();

            AddAssert("fun mod", () => mafham.Type, () => Is.EqualTo(ModType.Fun));
            AddAssert("description", () => mafham.Description.ToString(), () => Is.EqualTo("Maybe infinity IS finite after all?"));
            AddAssert("listed with the fun mods", () => new OsuRuleset().GetModsFor(ModType.Fun).Any(m => m is OsuModMafham));
            AddAssert("not with the difficulty reduction mods", () => !new OsuRuleset().GetModsFor(ModType.DifficultyReduction).Any(m => m is OsuModMafham));
        }

        [Test]
        public void TestApproachRateShownAsNegativeInfinity()
        {
            var beatmapInfo = new BeatmapInfo { Difficulty = { ApproachRate = 8, OverallDifficulty = 7 } };
            var ruleset = new OsuRuleset();

            AddAssert("display approach rate is negative infinity", () => ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, new Mod[] { new OsuModMafham() }).ApproachRate,
                () => Is.EqualTo(float.NegativeInfinity));
            AddAssert("also with double time", () => ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, new Mod[] { new OsuModMafham(), new OsuModDoubleTime() }).ApproachRate,
                () => Is.EqualTo(float.NegativeInfinity));
            AddAssert("other values unchanged", () => ruleset.GetAdjustedDisplayDifficulty(beatmapInfo, new Mod[] { new OsuModMafham() }).OverallDifficulty, () => Is.EqualTo(7));
            AddAssert("shown as a reduction, approach time infinite", () =>
            {
                var ar = ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, new Mod[] { new OsuModMafham() }).Single(a => a.Acronym == "AR");
                return ar.AdjustedValue < ar.OriginalValue && ar.AdditionalMetrics.Single().Value.ToString() == "∞ ms";
            });
            AddAssert("gameplay difficulty unchanged", () =>
            {
                var difficulty = new BeatmapDifficulty(beatmapInfo.Difficulty);
                foreach (var m in new Mod[] { new OsuModMafham() }.OfType<IApplicableToDifficulty>())
                    m.ApplyToDifficulty(difficulty);
                return difficulty.ApproachRate == 8;
            });
        }

        [Test]
        public void TestBrightnessShownAsExponent()
        {
            var beatmapInfo = new BeatmapInfo { Difficulty = { ApproachRate = 8 } };
            var ruleset = new OsuRuleset();

            float? exponent(float? brightness)
            {
                var mods = new List<Mod>();

                if (brightness != null)
                {
                    var mafham = new OsuModMafham();
                    mafham.FarObjectBrightness.Value = brightness.Value;
                    mods.Add(mafham);
                }

                return ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, mods).Single(a => a.Acronym == "AR").ValueExponent;
            }

            AddAssert("none without Mafham", () => exponent(null), () => Is.Null);
            AddAssert("none at 100%", () => exponent(1), () => Is.Null);
            AddAssert("0.5 at 50%", () => exponent(0.5f), () => Is.EqualTo(0.5f));
            AddAssert("0.3 at 30%", () => exponent(0.3f), () => Is.EqualTo(0.3f).Within(0.001f));
            AddAssert("0.1 at 10% (the minimum)", () => exponent(0.1f), () => Is.EqualTo(0.1f).Within(0.001f));
            AddAssert("minimum is 10%", () => new OsuModMafham().FarObjectBrightness.MinValue, () => Is.EqualTo(0.1f));
            AddAssert("5% clamps to 10%", () =>
            {
                var mafham = new OsuModMafham();
                mafham.FarObjectBrightness.Value = 0.05f;
                return mafham.FarObjectBrightness.Value;
            }, () => Is.EqualTo(0.1f).Within(0.001f));
            AddAssert("only on AR", () =>
            {
                var mafham = new OsuModMafham();
                mafham.FarObjectBrightness.Value = 0.5f;
                return ruleset.GetBeatmapAttributesForDisplay(beatmapInfo, new Mod[] { mafham }).Count(a => a.ValueExponent != null) == 1;
            });
        }

        [Test]
        public void TestFarObjectBrightness()
        {
            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham { FarObjectBrightness = { Value = 0.5f } },
                CreateBeatmap = () => createLongBeatmap(20_000),
                PassCondition = () => true,
            });

            AddAssert("pictures darker", () => layer.PictureColour.AverageColour.SRGB.R, () => Is.EqualTo(0.5f).Within(0.01f));
            AddUntilStep("wait for live circles", () => liveObjects.OfType<DrawableHitCircle>().Count(c => time > c.LifetimeStart + OsuModMafham.BRIGHTEN_DURATION) >= 2);
            AddAssert("live circles brightened", () => liveObjects.OfType<DrawableHitCircle>()
                                                                     .Where(c => !c.Judged && time > c.LifetimeStart + OsuModMafham.BRIGHTEN_DURATION)
                                                                     .All(c => c.Colour.AverageColour.SRGB.R == 1));
            AddAssert("a circle that just came alive starts as dark as the pictures", () =>
            {
                var circle = liveObjects.OfType<DrawableHitCircle>().First(c => !c.Judged);
                var colour = circle.Transforms.FirstOrDefault(t => t.TargetMember == nameof(Colour));
                return colour != null && Math.Abs(colour.StartTime - circle.LifetimeStart) < 1;
            });
        }

        [Test]
        public void TestPictureOpacity()
        {
            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham(),
                CreateBeatmap = () => createLongBeatmap(20_000),
                PassCondition = () => true,
            });

            AddAssert("fully visible", () => layer.PictureOpacity, () => Is.EqualTo(1));
            AddStep("switch off (replay toggle)", () => layer.SetPictureOpacity(OsuModMafham.REPLAY_OFF_OPACITY));
            AddAssert("pictures faint", () => layer.PictureOpacity, () => Is.EqualTo(OsuModMafham.REPLAY_OFF_OPACITY));
            AddStep("switch on", () => layer.SetPictureOpacity(1));
            AddAssert("fully visible again", () => layer.PictureOpacity, () => Is.EqualTo(1));
        }

        [Test]
        public void TestSwitchedOffInReplay()
        {
            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham(),
                CreateBeatmap = () => createLongBeatmap(20_000),
                PassCondition = () => true,
            });

            OsuModMafham mafham() => Player.GameplayState.Mods.OfType<OsuModMafham>().Single();

            AddUntilStep("wait for live circles", () => liveObjects.OfType<DrawableHitCircle>().Count() >= 2);
            AddStep("switch off", () => mafham().SetEffectsEnabled(false));
            AddAssert("pictures faint", () => layer.PictureOpacity, () => Is.EqualTo(OsuModMafham.REPLAY_OFF_OPACITY));
            float fadingAlpha = 1;
            AddUntilStep("wait for a circle coming alive", () =>
            {
                var circle = liveObjects.OfType<DrawableHitCircle>().FirstOrDefault(c => time > c.LifetimeStart && time < c.LifetimeStart + OsuModMafham.BRIGHTEN_DURATION / 2);

                if (circle == null)
                    return false;

                fadingAlpha = circle.Colour.AverageColour.Alpha;
                return true;
            });
            AddAssert("it fades in from the pictures' opacity", () => fadingAlpha, () => Is.LessThan(1));
            AddUntilStep("live circles fully visible after the fade", () => liveObjects.OfType<DrawableHitCircle>()
                                                                                  .Where(c => !c.Judged && time > c.LifetimeStart + OsuModMafham.BRIGHTEN_DURATION)
                                                                                  .All(c => c.Colour.AverageColour.Alpha == 1));
            AddStep("switch on", () => mafham().SetEffectsEnabled(true));
            AddAssert("pictures visible", () => layer.PictureOpacity, () => Is.EqualTo(1));
        }

        [Test]
        public void TestRetryUsesKeptPictures()
        {
            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham(),
                CreateBeatmap = () => createLongBeatmap(20_000),
                PassCondition = () => true,
            });

            AddAssert("first play draws for the full time", () => Player.GameplayClockContainer.GameplayStartTime,
                () => Is.EqualTo(first_object_time - OsuModMafham.VIEW_TIME - OsuModMafham.PREPARE_TIME));
            AddUntilStep("pictures kept", () => OsuModMafham.PictureCache.Current != null);
            AddAssert("one per page", () => OsuModMafham.PictureCache.Current!.Snapshots.Count, () => Is.EqualTo(layer.PageCount));

            // A retry: a new player, the same map and mods.
            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham(),
                CreateBeatmap = () => createLongBeatmap(20_000),
                PassCondition = () => true,
            });

            AddAssert("retry starts shortly before the map appears", () => Player.GameplayClockContainer.GameplayStartTime,
                () => Is.EqualTo(first_object_time - OsuModMafham.VIEW_TIME - OsuModMafham.RETRY_PREPARE_TIME));
            // (The first pages load copies in the background for when objects are taken out, and switch to them once they're there.)
            AddAssert("pages made from the kept pictures", () => layer.SnapshotPageCount, () => Is.GreaterThanOrEqualTo(layer.PageCount - OsuModMafham.MafhamLayer.WARM_PAGES));
            AddAssert("no copies made for most pages", () => layer.CopyCount, () => Is.LessThanOrEqualTo(OsuModMafham.MafhamLayer.FRONT_PAGE_COST * OsuModMafham.MafhamLayer.WARM_PAGES));
            AddUntilStep("map appears", () => time >= layer.AppearTime + OsuModMafham.FADE_IN_DURATION && layer.Alpha == 1);
            AddAssert("every page shown", () => layer.AllPagesShown);
            checkEveryObjectShownOnce();
            AddUntilStep("objects taken out as usual", () => layer.FrozenCount < objects.Count(h => FrozenObjectCanCopy(h)) - 5);
            checkEveryObjectShownOnce();

            AddStep("retry with other mods", () => { });
            CreateModTest(new ModTestData
            {
                Mods = new Mod[] { new OsuModMafham(), new OsuModHardRock() },
                CreateBeatmap = () => createLongBeatmap(20_000),
                PassCondition = () => true,
            });
            AddAssert("drawn from the start again", () => Player.GameplayClockContainer.GameplayStartTime,
                () => Is.EqualTo(first_object_time - OsuModMafham.VIEW_TIME - OsuModMafham.PREPARE_TIME));
            AddAssert("no kept pictures used", () => layer.SnapshotPageCount, () => Is.Zero);
        }

        private static bool FrozenObjectCanCopy(HitObject h) => OsuModMafham.FrozenObject.CanCopy(h);

        [Test]
        public void TestWholeMapShownAtOnce()
        {
            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham(),
                CreateBeatmap = () => createLongBeatmap(180_000),
                PassCondition = () => true,
            });

            AddUntilStep("wait for the first object", () => time >= first_object_time - 300);
            AddAssert("map is about three minutes long", () => objects.Last().StartTime, () => Is.GreaterThan(170_000));
            AddAssert("map has more than 900 objects", () => objects.Count(), () => Is.GreaterThan(900));
            checkEveryObjectShownOnce();
            checkFewLiveObjects();
            AddAssert("few pages", () => layer.PageCount, () => Is.LessThanOrEqualTo(30));
            AddAssert("front page is small", () => layer.FrontPageCost, () => Is.LessThanOrEqualTo(OsuModMafham.MafhamLayer.FRONT_PAGE_COST));

            AddStep("seek to the middle", () => Player.Seek(90_000));
            AddUntilStep("wait for seek", () => time >= 90_000 && caughtUp);
            checkEveryObjectShownOnce();
            checkFewLiveObjects();
            AddAssert("front page is small", () => layer.FrontPageCost, () => Is.LessThanOrEqualTo(OsuModMafham.MafhamLayer.FRONT_PAGE_COST));

            AddStep("seek back (replay rewind)", () => Player.Seek(20_000));
            AddUntilStep("wait for seek", () => time < 25_000 && time >= 20_000 && caughtUp);
            checkEveryObjectShownOnce();
            checkFewLiveObjects();
        }

        [Test]
        public void TestObjectsAppearTogether()
        {
            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham(),
                CreateBeatmap = () => createLongBeatmap(20_000),
                PassCondition = () => true,
            });

            AddAssert("hidden during the lead-in", () => time < layer.AppearTime && layer.Alpha == 0);
            AddUntilStep("wait for the map to appear", () => time >= layer.AppearTime + OsuModMafham.FADE_IN_DURATION);
            AddAssert("pictures are fully shown", () => layer.Alpha, () => Is.EqualTo(1));
            AddAssert("every page shown", () => layer.AllPagesShown);
            AddAssert("last object's picture is shown", () => layer.FrozenHitObjects.Contains(objects.Last(h => h is not Spinner)));
            AddAssert("shown before the first object comes alive", () => time < first_object_time - normal_preempt);
        }

        [Test]
        public void TestLiveObjectsLookLikeMafham()
        {
            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham(),
                CreateBeatmap = () => createLongBeatmap(20_000),
                PassCondition = () => true,
            });

            AddUntilStep("wait for live circles", () => liveObjects.OfType<DrawableHitCircle>().Count() >= 2);
            AddAssert("no approach circles", () => liveObjects.OfType<DrawableHitCircle>().All(c => c.ApproachCircle.Alpha == 0));
            AddAssert("no dim", () => liveObjects.OfType<DrawableHitCircle>().All(c => c.CirclePiece.Colour.AverageColour.Linear == Color4.White));
            AddAssert("live circles are fully visible (they take over from identical pictures)",
                () => liveObjects.OfType<DrawableHitCircle>().Where(c => !c.Judged).All(c => c.Alpha == 1 && c.CirclePiece.Alpha == 1));
            AddAssert("objects have kiai flashes", () => liveObjects.Sum(c => c.ChildrenOfType<KiaiFlash>().Count()
                                                                       + c.ChildrenOfType<LegacyKiaiFlashingDrawable>().Count()), () => Is.GreaterThan(0));
            AddAssert("kiai flashes switched off", () => liveObjects.All(c =>
                c.ChildrenOfType<KiaiFlash>().All(k => k.Alpha == 0)
                && c.ChildrenOfType<LegacyKiaiFlashingDrawable>().All(l => l.FlashingDrawable.Scale == Vector2.Zero)));
            AddAssert("follow points hidden", () => Player.DrawableRuleset.ChildrenOfType<osu.Game.Rulesets.Osu.UI.OsuPlayfield>().Single().FollowPoints.Alpha == 0);
            AddAssert("drawables come alive at the normal time",
                () => liveObjects.All(d => Math.Abs(d.LifetimeStart - (d.HitObject.StartTime - normal_preempt)) < 1));
        }

        [Test]
        public void TestAutoplayIsPerfect() => CreateModTest(new ModTestData
        {
            Mod = new OsuModMafham(),
            CreateBeatmap = () => createLongBeatmap(12_000),
            PassCondition = () => Player.ScoreProcessor.HasCompleted.Value
                                  && Player.ScoreProcessor.Statistics.GetValueOrDefault(HitResult.Miss) == 0
                                  && Player.ScoreProcessor.Accuracy.Value == 1,
        });

        [Test]
        public void TestLongerLeadIn()
        {
            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham(),
                CreateBeatmap = () => createLongBeatmap(20_000),
                PassCondition = () => true,
            });

            // McOsu-like: the map appears a few seconds before the first object, and is drawn during a longer lead-in before that.
            AddAssert("map appears a few seconds before the first object",
                () => layer.AppearTime, () => Is.EqualTo(first_object_time - OsuModMafham.VIEW_TIME));
            AddAssert("gameplay starts the drawing time before that",
                () => Player.GameplayClockContainer.GameplayStartTime, () => Is.EqualTo(first_object_time - OsuModMafham.VIEW_TIME - OsuModMafham.PREPARE_TIME));
        }

        [Test]
        public void TestLeadInIsRealTime()
        {
            CreateModTest(new ModTestData
            {
                Mods = new Mod[] { new OsuModMafham(), new OsuModDoubleTime() },
                CreateBeatmap = () => createLongBeatmap(20_000),
                PassCondition = () => true,
            });

            AddAssert("appears three real seconds before the first object",
                () => layer.AppearTime, () => Is.EqualTo(first_object_time - OsuModMafham.VIEW_TIME * 1.5).Within(0.01));
            AddAssert("gameplay starts the drawing time before that",
                () => Player.GameplayClockContainer.GameplayStartTime,
                () => Is.EqualTo(first_object_time - (OsuModMafham.VIEW_TIME + OsuModMafham.PREPARE_TIME) * 1.5).Within(0.01));
        }

        [Test]
        public void TestNoReadingWithMafham()
        {
            OsuDifficultyAttributes without = null!, with = null!;

            AddStep("calculate", () =>
            {
                var calculator = new OsuRuleset().CreateDifficultyCalculator(new TestWorkingBeatmap(createLongBeatmap(30_000)));
                without = (OsuDifficultyAttributes)calculator.Calculate(Array.Empty<Mod>());
                with = (OsuDifficultyAttributes)calculator.Calculate(new Mod[] { new OsuModMafham() });
            });

            AddAssert("map has reading difficulty", () => without.ReadingDifficulty, () => Is.GreaterThan(0));
            AddAssert("none with Mafham", () => with.ReadingDifficulty, () => Is.Zero);
            AddAssert("lower star rating", () => with.StarRating, () => Is.LessThan(without.StarRating));
            AddAssert("aim and speed unchanged", () => with.AimDifficulty == without.AimDifficulty && with.SpeedDifficulty == without.SpeedDifficulty);
            AddAssert("no reading PP", () =>
            {
                var score = new ScoreInfo
                {
                    Ruleset = new OsuRuleset().RulesetInfo,
                    Mods = new Mod[] { new OsuModMafham() },
                    Accuracy = 1,
                    MaxCombo = with.MaxCombo,
                    Statistics = { [HitResult.Great] = with.HitCircleCount + with.SliderCount + with.SpinnerCount },
                };

                var performance = (OsuPerformanceAttributes)new OsuRuleset().CreatePerformanceCalculator()!.Calculate(score, with);
                return performance.Reading == 0 && performance.Total > 0 && double.IsFinite(performance.Total);
            });
        }

        [Test]
        public void TestSpinnersNotFrozen()
        {
            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham(),
                CreateBeatmap = () => createLongBeatmap(60_000),
                PassCondition = () => true,
            });

            AddUntilStep("wait for the first object", () => time >= first_object_time - 300);
            AddAssert("map has spinners", () => objects.OfType<Spinner>().Count(), () => Is.GreaterThan(0));
            AddAssert("no spinner frozen", () => !layer.FrozenHitObjects.OfType<Spinner>().Any());
            AddAssert("spinner preempt unchanged", () => objects.OfType<Spinner>().All(s => s.TimePreempt <= 1800));
        }

        private IEnumerable<DrawableOsuHitObject> liveObjects => Player.DrawableRuleset.Playfield.HitObjectContainer.AliveObjects.OfType<DrawableOsuHitObject>();

        /// <summary>
        /// Every upcoming object (except spinners) is shown: by its picture until its drawable has come alive, then by its drawable.
        /// </summary>
        private void checkEveryObjectShownOnce()
        {
            AddAssert("every upcoming object shown", () =>
            {
                var frozen = layer.FrozenHitObjects.ToHashSet();
                var live = liveObjects.Select(d => d.HitObject).ToHashSet();

                foreach (var h in objects)
                {
                    if (h is Spinner || h.StartTime < time + 50)
                        continue;

                    bool isFrozen = frozen.Contains(h);
                    bool isLive = live.Contains(h);

                    if (!isFrozen && !isLive)
                        return false;

                    if (isLive != (time >= h.StartTime - normal_preempt))
                        return false;

                    if (isFrozen != (time < layer.GetRemovalTime(h)))
                        return false;
                }

                return true;
            });
        }

        [Test]
        public void TestPictureSwappedForDrawable()
        {
            // AR10: drawables come alive 450 ms before their circle. Circles every 500 ms, far apart.
            const double ar10_preempt = 450;

            CreateModTest(new ModTestData
            {
                Mod = new OsuModMafham(),
                CreateBeatmap = () => new Beatmap
                {
                    HitObjects = Enumerable.Range(0, 10).Select(i => (HitObject)new HitCircle
                    {
                        StartTime = 2000 + i * 500,
                        Position = new Vector2(60 + i % 5 * 90, 100 + i / 5 * 150),
                    }).ToList(),
                    BeatmapInfo = { Difficulty = { ApproachRate = 10 } },
                },
                PassCondition = () => true,
            });

            AddUntilStep("wait for start", () => time > 1000);

            // circle 4 (4000 ms) comes alive at 3550
            seekTo(3530);
            AddAssert("circle 4 is a picture", () => layer.FrozenHitObjects.Contains(objects.ElementAt(4)));
            AddAssert("its drawable isn't alive", () => liveObjects.All(d => d.HitObject != objects.ElementAt(4)));

            seekTo(3570);
            AddAssert("picture gone once the drawable is alive", () => !layer.FrozenHitObjects.Contains(objects.ElementAt(4)));
            AddAssert("drawable fully shown straight away", () => liveObjects.Single(d => d.HitObject == objects.ElementAt(4)).Alpha, () => Is.EqualTo(1));
            AddAssert("came alive at the normal time", () => liveObjects.Single(d => d.HitObject == objects.ElementAt(4)).LifetimeStart,
                () => Is.EqualTo(objects.ElementAt(4).StartTime - ar10_preempt).Within(1));
            AddAssert("next circle still a picture", () => layer.FrozenHitObjects.Contains(objects.ElementAt(5)));
        }

        private void seekTo(double target)
        {
            AddStep($"seek to {target}", () =>
            {
                Player.GameplayClockContainer.Stop();
                Player.Seek(target);
            });
            AddUntilStep("wait for seek", () => Math.Abs(time - target) < 1 && caughtUp);
        }

        private void checkFewLiveObjects() =>
            AddAssert("live drawables as without the mod", () => liveObjects.Count(), () => Is.LessThanOrEqualTo(20));

        /// <summary>
        /// A dense map: a circle every 100 ms, a short slider every fourth object (some with a repeat), a spinner now and then.
        /// </summary>
        private static Beatmap createLongBeatmap(double length)
        {
            var random = new Random(1234);
            var hitObjects = new List<HitObject>();

            double t = first_object_time;

            for (int i = 0; t < length; i++)
            {
                var position = new Vector2(random.Next(40, 472), random.Next(40, 344));
                bool newCombo = i % 8 == 0;

                if (i % 150 == 149)
                {
                    hitObjects.Add(new Spinner { StartTime = t, EndTime = t + 1500, Position = new Vector2(256, 192), NewCombo = true });
                    t += 2000;
                    continue;
                }

                if (i % 4 == 3)
                {
                    var end = Vector2.Clamp(position + new Vector2(random.Next(-60, 60), random.Next(-60, 60)), Vector2.Zero, new Vector2(512, 384)) - position;

                    hitObjects.Add(new Slider
                    {
                        StartTime = t,
                        Position = position,
                        NewCombo = newCombo,
                        Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, end }),
                        RepeatCount = i % 8 == 7 ? 1 : 0,
                    });

                    t += 400;
                    continue;
                }

                hitObjects.Add(new HitCircle { StartTime = t, Position = position, NewCombo = newCombo });
                t += 100;
            }

            var beatmap = new Beatmap
            {
                HitObjects = hitObjects,
                BeatmapInfo = { Difficulty = { ApproachRate = 9, CircleSize = 4, OverallDifficulty = 8 } },
            };

            beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 300 });
            return beatmap;
        }
    }
}

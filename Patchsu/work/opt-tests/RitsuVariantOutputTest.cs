// Patchsu optimisation pass local test (not part of any patch): dumps what the Ritsu / Modosu conversion mods (0003) make of real beatmaps,
// so two builds (before / after a change) can be compared byte for byte. Needs 0003.
// Copy into osu.Game.Rulesets.Osu.Tests and run `PATCHSU_DUMP=/path/out.txt dotnet test --filter RitsuVariantOutputTest`.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Rulesets.Osu.Tests
{
    public class RitsuVariantOutputTest : DifficultyCalculatorTest
    {
        protected override string ResourceAssembly => "osu.Game.Rulesets.Osu.Tests";

        protected override DifficultyCalculator CreateDifficultyCalculator(IWorkingBeatmap beatmap) => new OsuRuleset().CreateDifficultyCalculator(beatmap);

        protected override Ruleset CreateRuleset() => new OsuRuleset();

        private static IEnumerable<(string Name, Func<Mod[]> Mods)> setups()
        {
            yield return ("none", () => Array.Empty<Mod>());
            yield return ("rdv2", () => new Mod[] { new OsuModRandomV2 { Seed = { Value = 1234 } } });
            yield return ("rdv2-expo", () => new Mod[]
            {
                new OsuModRandomV2
                {
                    Seed = { Value = 99 }, ExpoJumps = { Value = true }, ExpoStreams = { Value = true }, AimDistanceMultiplier = { Value = 2 },
                    StreamDistanceMultiplier = { Value = 1.5f }, RemoveStacks = { Value = true }, AngleSharpness = { Value = 3 }, StreamAngleSharpness = { Value = 9 },
                }
            });
            yield return ("rdv2-patterns", () => new Mod[] { new OsuModRandomV2 { Seed = { Value = 7 }, CustomAngle = { Value = true }, PatternMask = { Value = 0x7FFF }, Angle = { Value = OsuModRandomV2.AngleEnum.Ninety } } });
            yield return ("rdv2-advanced", () => new Mod[]
            {
                new OsuModRandomV2
                {
                    Seed = { Value = 7 }, CustomAngle = { Value = true }, SeparatePatterns = { Value = true }, JumpPatterns = { Value = 0x0FF },
                    StreamPatterns = { Value = 0x0F0 }, BurstPatterns = { Value = 0x70F }, MirrorResponses = { Value = true }, ProgressiveSpacing = { Value = true },
                }
            });
            yield return ("sa-up", () => new Mod[] { new OsuModSpacingAdjust { ObjectSpacing = { Value = 1.7f }, MaximumJumpDistance = { Value = 250 } } });
            yield return ("sa-down", () => new Mod[] { new OsuModSpacingAdjust { ObjectSpacing = { Value = 0.5f } } });
            yield return ("so", () => new Mod[] { new OsuModSpacingOvertime() });
            yield return ("nl-streams", () => new Mod[] { new OsuModNoSliding() });
            yield return ("nl-head", () => new Mod[] { new OsuModNoSliding { Conversion = { Value = OsuModNoSliding.SliderConversion.HeadAndTail } } });
            yield return ("cg", () => new Mod[] { new OsuModCircleGeneration() });
            yield return ("cg-breaks", () => new Mod[] { new OsuModCircleGeneration { Break = { Value = true }, BreakInterval = { Value = 40 }, Kickslider = { Value = true } } });
            yield return ("cg-increasing", () => new Mod[] { new OsuModCircleGeneration { Increasing = { Value = true } } });
            yield return ("decoys", () => new Mod[] { new OsuModDecoyCircles() });
            yield return ("echo", () => new Mod[] { new OsuModEcho() });
            yield return ("swing", () => new Mod[] { new OsuModSwing() });
            yield return ("phrase-mirror", () => new Mod[] { new OsuModPhraseMirror() });
            yield return ("streamline", () => new Mod[] { new OsuModHalfStream() });
            yield return ("reversed", () => new Mod[] { new OsuModReversedSliders() });
        }

        [Test]
        public void DumpVariants()
        {
            string? path = Environment.GetEnvironmentVariable("PATCHSU_DUMP");
            var output = new StringBuilder();

            foreach (string map in new[] { "diffcalc-test", "801165", "1124896" })
            {
                foreach (var (name, createMods) in setups())
                {
                    var working = GetBeatmap(map);
                    var playable = working.GetPlayableBeatmap(new OsuRuleset().RulesetInfo, createMods());

                    output.AppendLine($"== {map} {name} {playable.HitObjects.Count}");

                    foreach (var h in playable.HitObjects.OfType<OsuHitObject>())
                    {
                        output.Append(FormattableString.Invariant($"{h.GetType().Name} {h.StartTime:R} {h.Position.X:R} {h.Position.Y:R} {h.NewCombo}"));

                        if (h is Slider s)
                        {
                            output.Append(FormattableString.Invariant($" r{s.RepeatCount} d{s.Path.ExpectedDistance.Value:R}"));
                            foreach (var p in s.Path.ControlPoints)
                                output.Append(FormattableString.Invariant($" [{p.Position.X:R},{p.Position.Y:R},{p.Type}]"));
                        }

                        output.AppendLine();
                    }
                }
            }

            if (path != null)
                File.WriteAllText(path, output.ToString());

            Assert.That(output.Length, Is.GreaterThan(0));
        }
    }
}

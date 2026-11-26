// Patchsu optimisation pass local test (not part of any patch): 0002 store / exclusions / tags, 0007 perfect-play PP cache and session.
// Copy into osu.Game.Tests/NonVisual and run `dotnet test --filter PatchsuOptimisationTest`.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.LocalPriority;
using osu.Game.Rulesets.Osu;
using osu.Game.Scoring;
using osu.Game.Screens.Select;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class PatchsuOptimisationTest
    {
        private static int osuDifficultyVersion => new OsuRuleset().CreateDifficultyCalculator(new FlatWorkingBeatmap(new Beatmap())).Version;

        [Test]
        public void TestPerfectPerformanceCacheConvertsOldFile()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestPerfectPerformanceCacheConvertsOldFile));
            var dir = storage.GetStorageForDirectory("patchsu-osu");

            string mods = PerfectPerformanceCache.GetModsKey(null);
            var old = new Dictionary<string, double[]>
            {
                [$"1|osu|{osuDifficultyVersion}|hash-a|{mods}"] = new[] { 123.456789, 4.56789 },
                [$"1|osu|{osuDifficultyVersion}|hash-b|{mods}"] = new[] { 50.0, 3.0 },
                [$"1|osu|{osuDifficultyVersion}|hash-c|{mods}"] = new[] { double.NaN, 3.0 },
            };

            using (var stream = dir.CreateFileSafely("perfect-pp-cache.json"))
            using (var writer = new StreamWriter(stream))
                writer.Write(JsonConvert.SerializeObject(old));

            var cache = new PerfectPerformanceCache(storage);
            var lookup = cache.CreateLookup(null!, new OsuRuleset().RulesetInfo, null);

            Assert.That(lookup.TryGet(new BeatmapInfo { Hash = "hash-a" }, out var a), Is.True);
            Assert.That(a.Performance, Is.EqualTo(123.46));
            Assert.That(a.StarRating, Is.EqualTo(4.568));
            Assert.That(lookup.TryGet(new BeatmapInfo { Hash = "hash-b" }, out _), Is.True);
            Assert.That(lookup.TryGet(new BeatmapInfo { Hash = "hash-c" }, out _), Is.False);

            cache.Save();

            Assert.That(dir.Exists("perfect-pp-cache.json"), Is.False, "old file deleted after the new one was written");
            Assert.That(dir.Exists("perfect-pp-cache-v2.json"), Is.True);

            string text = File.ReadAllText(dir.GetFullPath("perfect-pp-cache-v2.json"));
            Assert.That(text, Does.Contain("\"hash-a\": [123.46, 4.568]"));
            Assert.That(JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, double[]>>>(text)!.Single().Value.Count, Is.EqualTo(2));

            // A new instance reads the new file.
            var reloaded = new PerfectPerformanceCache(storage).CreateLookup(null!, new OsuRuleset().RulesetInfo, null);
            Assert.That(reloaded.TryGet(new BeatmapInfo { Hash = "hash-a" }, out var again), Is.True);
            Assert.That(again, Is.EqualTo(a));
            Assert.That(reloaded.TryGet(new BeatmapInfo { Hash = "hash-a" }, out _), Is.True);

            // Other mods are another setup.
            var withMods = new PerfectPerformanceCache(storage).CreateLookup(null!, new OsuRuleset().RulesetInfo, new[] { new osu.Game.Rulesets.Osu.Mods.OsuModHidden() });
            Assert.That(withMods.TryGet(new BeatmapInfo { Hash = "hash-a" }, out _), Is.False);
        }

        [Test]
        public void TestPerfectPerformanceCacheSharedPerStorage()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestPerfectPerformanceCacheSharedPerStorage));
            Assert.That(PerfectPerformanceCache.For(storage), Is.SameAs(PerfectPerformanceCache.For(storage)));
        }

        [Test]
        public void TestSessionLooksUpEditedBeatmapAgain()
        {
            var beatmap = new BeatmapInfo { Hash = "one" };
            var session = new PerfectPerformanceSession(b => b.Hash == "one" ? new PerfectPerformanceValue(100, 5) : new PerfectPerformanceValue(200, 6),
                (_, _) => null, () => { }, _ => { }, false);

            Assert.That(session.Get(beatmap).Value.Performance, Is.EqualTo(100));
            Assert.That(session.Get(beatmap).Value.Performance, Is.EqualTo(100));

            beatmap.Hash = "two";
            Assert.That(session.Get(beatmap).Value.Performance, Is.EqualTo(200));
            Assert.That(session.Progress, Is.EqualTo((1, 1)));
        }

        [Test]
        public void TestExclusionMatcherMatchesGetReason()
        {
            var set = new BeatmapSetInfo();
            var beatmaps = Enumerable.Range(0, 4).Select(_ => new BeatmapInfo { BeatmapSet = set }).ToArray();
            var otherSet = new BeatmapSetInfo();
            var other = new BeatmapInfo { BeatmapSet = otherSet };

            var scores = beatmaps.Append(other).Select(b => new ScoreInfo { BeatmapInfo = b }).Append(new ScoreInfo { BeatmapInfo = null }).ToArray();

            var exclusions = new LocalPerformanceExclusions();

            void check()
            {
                var matcher = exclusions.CreateMatcher();
                foreach (var score in scores)
                    Assert.That(matcher.GetReason(score), Is.EqualTo(exclusions.GetReason(score)));
            }

            check();
            exclusions.Scores.Add(scores[0].ID);
            check();
            exclusions.Beatmaps.Add(beatmaps[1].ID);
            check();
            exclusions.BeatmapSets.Add(otherSet.ID);
            check();
            Assert.That(exclusions.CreateMatcher().GetReason(scores[2]), Is.Null);
        }

        [Test]
        public void TestStoreWritesPerformanceCacheReadably()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestStoreWritesPerformanceCacheReadably));

            var store = new LocalProfileStore(storage);
            var profile = store.CreateProfile("Test");
            var id = Guid.NewGuid();

            store.SetCachedPerformance(new Dictionary<Guid, double?> { [id] = 123.5 });
            Assert.That(store.TryGetCachedPerformance(id, out double? pp) && pp == 123.5);

            store.Flush();

            string cache = File.ReadAllText(storage.GetFullPath("patchsu-osu/performance-cache.json"));
            Assert.That(cache, Does.Contain($"\"{id}\": 123.5"), "indented, readable");

            string profiles = File.ReadAllText(storage.GetFullPath("patchsu-osu/profiles.json"));
            Assert.That(profiles, Does.Contain("\"Name\": \"Test\""));

            var reloaded = new LocalProfileStore(storage);
            Assert.That(reloaded.TryGetCachedPerformance(id, out double? again) && again == 123.5);
            Assert.That(reloaded.GetProfile(profile.Id)?.Name, Is.EqualTo("Test"));
        }

        [Test]
        public void TestBeatmapTags()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestBeatmapTags));

            var tags = new LocalBeatmapTags(storage);
            var beatmap = new BeatmapInfo { MD5Hash = "md5" };

            var before = tags.Get(beatmap);
            tags.Add(beatmap, "Jumps");
            tags.Add(beatmap, "streams");

            Assert.That(before, Is.Empty, "a list handed out earlier doesn't change");
            Assert.That(tags.Get(beatmap), Is.EqualTo(new[] { "jumps", "streams" }));
            Assert.That(tags.AllTags(), Is.EqualTo(new[] { "jumps", "streams" }));

            tags.Remove(beatmap, "jumps");
            Assert.That(tags.Get(beatmap), Is.EqualTo(new[] { "streams" }));

            Assert.That(new LocalBeatmapTags(storage).Get(beatmap), Is.EqualTo(new[] { "streams" }));
            Assert.That(File.ReadAllText(storage.GetFullPath("patchsu-osu/beatmap-tags.json")), Does.Contain("\n"), "indented");
        }
    }
}

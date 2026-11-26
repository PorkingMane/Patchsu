// Patchsu 0009 local test (not part of the patch): the old patch12.json is taken over into PatchsuSettings.json and deleted.
// Copy into osu.Game.Tests/NonVisual and run `dotnet test --filter NoFailSettingsMigrationTest`.

using System.IO;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.LocalPriority;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class NoFailSettingsMigrationTest
    {
        [Test]
        public void TestPatch12JsonTakenOver()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestPatch12JsonTakenOver));

            using (var stream = storage.CreateFileSafely("osu-local/patch12.json"))
            using (var writer = new StreamWriter(stream))
                writer.Write(@"{ ""IgnoreNoFail"": true, ""IgnoreNoFailScore"": true }");

            var settings = PatchsuSettings.Load(storage);
            LocalNoFailPerformance.Load(storage);

            Assert.That(LocalNoFailPerformance.Enabled, Is.True);
            Assert.That(LocalNoFailPerformance.ScoreEnabled, Is.True);
            Assert.That(storage.Exists(PatchsuSettings.DIRECTORY + "/patch12.json"), Is.False, "deleted");

            LocalNoFailPerformance.SetScoreEnabled(false);
            settings.Flush();

            string json = File.ReadAllText(storage.GetFullPath(PatchsuSettings.DIRECTORY + "/PatchsuSettings.json"));
            Assert.That(json, Does.Contain("\"NoFail\"").And.Contain("\"IgnoreNoFail\": true").And.Not.Contain("IgnoreNoFailScore"));

            settings = PatchsuSettings.Load(storage);
            LocalNoFailPerformance.Load(storage);
            Assert.That(LocalNoFailPerformance.Enabled, Is.True);
            Assert.That(LocalNoFailPerformance.ScoreEnabled, Is.False);

            LocalNoFailPerformance.SetEnabled(false);
            settings.Flush();
        }
    }
}

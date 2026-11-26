// Patchsu 0001 local test (not part of the patch): PatchsuSettings.json, the "osu-local" → "patchsu-osu" rename and taking over game.ini values.
// Copy into osu.Game.Tests/NonVisual and run `dotnet test --filter PatchsuSettingsTest`.

using System.IO;
using NUnit.Framework;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.LocalPriority;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class PatchsuSettingsTest
    {
        private const string file = PatchsuSettings.DIRECTORY + "/PatchsuSettings.json";

        [Test]
        public void TestOldFolderIsRenamed()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestOldFolderIsRenamed));
            write(storage, "osu-local/profiles.json", "{}");

            PatchsuSettings.Load(storage).Flush();

            Assert.That(storage.ExistsDirectory("osu-local"), Is.False);
            Assert.That(storage.Exists(PatchsuSettings.DIRECTORY + "/profiles.json"));
            Assert.That(storage.Exists(file), "written on the first start");
        }

        [Test]
        public void TestNewFolderIsNotReplaced()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestNewFolderIsNotReplaced));
            write(storage, "osu-local/profiles.json", "old");
            write(storage, PatchsuSettings.DIRECTORY + "/profiles.json", "new");

            PatchsuSettings.Load(storage);

            Assert.That(read(storage, PatchsuSettings.DIRECTORY + "/profiles.json"), Is.EqualTo("new"));
            Assert.That(storage.Exists("osu-local/profiles.json"), "left alone");
        }

        [Test]
        public void TestGameIniTakenOverOnce()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestGameIniTakenOverOnce));
            write(storage, "game.ini", "SongSelectSortingReversed = True\nSongSelectPerformanceGroupInterval = 250\n");

            var settings = PatchsuSettings.Load(storage);
            Assert.That(settings.GetBindable("Interface", "SongSelectSortingReversed", false, "SongSelectSortingReversed").Value, Is.True);
            Assert.That(settings.GetBindable("PPAwareCarousel", "SongSelectPerformanceGroupInterval", 100, "SongSelectPerformanceGroupInterval").Value, Is.EqualTo(250));
            settings.Flush();

            string json = read(storage, file);
            Assert.That(json, Does.Contain("\"SongSelectSortingReversed\": true"), "indented, readable");

            // game.ini still has the old line (osu! drops it on its next save), but it isn't looked at again.
            settings = PatchsuSettings.Load(storage);
            settings.GetBindable("Interface", "SongSelectSortingReversed", false, "SongSelectSortingReversed").Value = false;
            settings.Flush();

            settings = PatchsuSettings.Load(storage);
            Assert.That(settings.GetBindable("Interface", "SongSelectSortingReversed", false, "SongSelectSortingReversed").Value, Is.False);
        }

        [Test]
        public void TestOnlyChangedValuesAreWritten()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestOnlyChangedValuesAreWritten));
            var settings = PatchsuSettings.Load(storage);

            var a = settings.GetBindable("Test", "Value", 5);
            var b = settings.GetBindable("Test", "Value", 5);
            a.Value = 7;
            Assert.That(b.Value, Is.EqualTo(7), "bindables of one setting stay in sync");

            settings.Flush();
            Assert.That(read(storage, file), Does.Contain("\"Value\": 7"));

            a.Value = 5;
            settings.Flush();
            Assert.That(read(storage, file), Does.Not.Contain("Test"), "back to the default: left out, and so is the empty section");
        }

        [Test]
        public void TestWholeValues()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestWholeValues));
            var settings = PatchsuSettings.Load(storage);

            Assert.That(settings.Get<Options>("Test", "Options"), Is.Null);
            settings.Set("Test", "Options", new Options { Name = "x", Count = 3 });
            settings.Flush();

            var reloaded = PatchsuSettings.Load(storage).Get<Options>("Test", "Options");
            Assert.That(reloaded?.Name, Is.EqualTo("x"));
            Assert.That(reloaded?.Count, Is.EqualTo(3));
        }

        [Test]
        public void TestBrokenFileIsKept()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestBrokenFileIsKept));
            write(storage, file, "{ not json");

            var settings = PatchsuSettings.Load(storage);

            Assert.That(settings.GetBindable("Test", "Value", 5).Value, Is.EqualTo(5));
            Assert.That(storage.Exists(file + ".broken"));
        }

        private class Options
        {
            public string Name { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        private static void write(Storage storage, string path, string text)
        {
            using (var stream = storage.CreateFileSafely(path))
            using (var writer = new StreamWriter(stream))
                writer.Write(text);
        }

        private static string read(Storage storage, string path) => File.ReadAllText(storage.GetFullPath(path));
    }
}

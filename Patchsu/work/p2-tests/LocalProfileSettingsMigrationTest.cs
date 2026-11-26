// Patchsu 0002 local test (not part of the patch): Patch 2's settings move from profiles.json to PatchsuSettings.json.
// Copy into osu.Game.Tests/NonVisual and run `dotnet test --filter LocalProfileSettingsMigrationTest`.

using System.IO;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.LocalPriority;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class LocalProfileSettingsMigrationTest
    {
        [Test]
        public void TestOptionsTakenOverFromProfilesJson()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestOptionsTakenOverFromProfilesJson));

            // A data folder from before: "osu-local" with the settings inside profiles.json.
            using (var stream = storage.CreateFileSafely("osu-local/profiles.json"))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(@"{ ""Version"": 1, ""Profiles"": [], ""LocalProfilesUseOnlineAccount"": true,
                                 ""Options"": { ""LongerLastLook"": true, ""ProfileColour"": ""#123456"" } }");
            }

            var settings = PatchsuSettings.Load(storage);
            var store = new LocalProfileStore(storage);

            Assert.That(store.Options.LongerLastLook, Is.True);
            Assert.That(store.Options.ProfileColour, Is.EqualTo("#123456"));
            Assert.That(store.LocalProfilesUseOnlineAccount, Is.True);

            store.Flush();
            settings.Flush();

            string profiles = File.ReadAllText(storage.GetFullPath(PatchsuSettings.DIRECTORY + "/profiles.json"));
            Assert.That(profiles, Does.Not.Contain("Options").And.Not.Contain("LocalProfilesUseOnlineAccount"), "removed from profiles.json");

            string json = File.ReadAllText(storage.GetFullPath(PatchsuSettings.DIRECTORY + "/PatchsuSettings.json"));
            Assert.That(json, Does.Contain("\"LocalProfiles\"").And.Contain("\"LongerLastLook\": true").And.Contain("\"UseOnlineAccount\": true"));

            // Changed and read back from PatchsuSettings.json only.
            var options = store.Options;
            options.LongerLastLook = false;
            store.SetOptions(options);
            store.SetLocalProfilesUseOnlineAccount(false);
            settings.Flush();

            PatchsuSettings.Load(storage);
            var reloaded = new LocalProfileStore(storage);
            Assert.That(reloaded.Options.LongerLastLook, Is.False);
            Assert.That(reloaded.Options.ProfileColour, Is.EqualTo("#123456"));
            Assert.That(reloaded.LocalProfilesUseOnlineAccount, Is.False);
        }

        [Test]
        public void TestFreshDataFolder()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestFreshDataFolder));

            PatchsuSettings.Load(storage);
            var store = new LocalProfileStore(storage);

            Assert.That(store.Options.SuggestSyncingSameNamePlays, Is.True, "defaults");
            Assert.That(store.LocalProfilesUseOnlineAccount, Is.False);
        }
    }
}

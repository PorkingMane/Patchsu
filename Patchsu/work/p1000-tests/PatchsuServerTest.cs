// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Tests for server patch 1000 (Mamestagram). Copy into osu.Game.Tests/NonVisual and run with
//   dotnet test osu.Game.Tests --filter "FullyQualifiedName~PatchsuServer"
// The [Category("Online")] ones reach the real api.mamesosu.net (read-only: one failed login with a made-up name, one leaderboard).

using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.IO.Network;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Notifications.WebSocket;
using osu.Game.Online.Rooms;
using osu.Game.Online.Solo;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class PatchsuServerTest
    {
        [Test]
        public void TestEndpointsPointAtServer()
        {
            var e = new ProductionEndpointConfiguration();

            Assert.That(e.APIUrl, Is.EqualTo("https://api.mamesosu.net/lazer"));
            Assert.That(e.WebsiteUrl, Is.EqualTo("https://api.mamesosu.net/lazer"));
            Assert.That(e.SpectatorUrl, Is.EqualTo("https://api.mamesosu.net/lazer/spectator"));
            Assert.That(e.MultiplayerUrl, Is.EqualTo("https://api.mamesosu.net/lazer/multiplayer"));
            Assert.That(e.MetadataUrl, Is.EqualTo("https://api.mamesosu.net/lazer/metadata"));
            Assert.That(e.APIClientID, Is.EqualTo("5"));

            foreach (string? url in new[] { e.APIUrl, e.WebsiteUrl, e.SpectatorUrl, e.MultiplayerUrl, e.MetadataUrl, e.BeatmapSubmissionServiceUrl })
                Assert.That(url, Does.Not.Contain("ppy.sh"));
        }

        [TestCase(typeof(GetNotificationsRequest))]
        [TestCase(typeof(ListChannelsRequest))]
        [TestCase(typeof(GetChannelRequest))]
        [TestCase(typeof(JoinChannelRequest))]
        [TestCase(typeof(LeaveChannelRequest))]
        [TestCase(typeof(GetMessagesRequest))]
        [TestCase(typeof(GetUpdatesRequest))]
        [TestCase(typeof(ChatAckRequest))]
        [TestCase(typeof(MarkChannelAsReadRequest))]
        [TestCase(typeof(PostMessageRequest))]
        [TestCase(typeof(CreateChannelRequest))]
        [TestCase(typeof(CreateNewPrivateMessageRequest))]
        [TestCase(typeof(GetMeRequest))]
        [TestCase(typeof(GetScoresRequest))]
        [TestCase(typeof(GetUserRequest))]
        public void TestAllowed(Type type) => Assert.That(isAllowed(type), Is.True);

        // Score submission stays off (server rules), and so does everything beyond login, chat and leaderboards.
        [TestCase(typeof(CreateSoloScoreRequest))]
        [TestCase(typeof(SubmitSoloScoreRequest))]
        [TestCase(typeof(CreateRoomRequest))]
        [TestCase(typeof(PostBeatmapFavouriteRequest))]
        public void TestRefused(Type type) => Assert.That(isAllowed(type), Is.False);

        private static bool isAllowed(Type type) => ((APIRequest)RuntimeHelpers.GetUninitializedObject(type)).IsAllowedOnline;

        [Test]
        public void TestOnlyNotificationsConnectionOpens()
        {
            Assert.That(stripped(typeof(WebSocketNotificationsClientConnector)), Is.False, "chat websocket");
            Assert.That(stripped(typeof(HubClientConnector)), Is.True, "spectator / multiplayer / metadata hubs");
        }

        private static bool stripped(Type type)
        {
            var prop = typeof(PersistentEndpointClientConnector).GetProperty("persistentConnectionsStripped", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (bool)prop.GetValue(RuntimeHelpers.GetUninitializedObject(type))!;
        }

        [TestCase("https://a.mamesosu.net/8748", true)]
        [TestCase("https://img.mamesosu.net/2?79973", true)]
        [TestCase("https://mamesosu.net/x.png", true)]
        [TestCase("https://assets.ppy.sh/x.jpg", true)]
        [TestCase("http://a.mamesosu.net/8748", false)]
        [TestCase("https://evilmamesosu.net/x.png", false)]
        [TestCase("https://a.mamesosu.net.evil.com/x.png", false)]
        [TestCase("https://example.com/x.png", false)]
        public void TestTrustedResourceHosts(string url, bool allowed)
        {
            var store = new TrustedDomainOnlineStore();
            var lookup = typeof(TrustedDomainOnlineStore).GetMethod("GetLookupUrl", BindingFlags.Instance | BindingFlags.NonPublic)!;
            string result = (string)lookup.Invoke(store, new object[] { url })!;
            Assert.That(result, allowed ? Is.EqualTo(url) : Is.Empty);
        }

        [Test]
        public void TestLoginKeptOutOfGameIni()
        {
            using var storage = new TemporaryNativeStorage(nameof(TestLoginKeptOutOfGameIni) + Guid.NewGuid());

            var config = new OsuConfigManager(storage);
            config.SetValue(OsuSetting.Token, "ppy-token");
            config.SetValue(OsuSetting.Username, "ppy-name");

            using (var login = new PatchsuServerLogin(storage, config))
            {
                Assert.That(login.Get<string>(PatchsuServerLoginSetting.Token), Is.Empty, "an osu! token must never be picked up");
                Assert.That(login.Get<string>(PatchsuServerLoginSetting.Username), Is.Empty);

                login.SetValue(PatchsuServerLoginSetting.Token, "server-token");
                login.SetValue(PatchsuServerLoginSetting.Username, "server-name");
            }

            config.Dispose();

            string ini = System.IO.File.ReadAllText(storage.GetFullPath("patchsu-mamestagram.ini"));
            Assert.That(ini, Does.Contain("server-token"));
            Assert.That(ini, Does.Contain("server-name"));

            string gameIni = System.IO.File.ReadAllText(storage.GetFullPath("game.ini"));
            Assert.That(gameIni, Does.Not.Contain("server-token"));
            Assert.That(gameIni, Does.Contain("ppy-token"));

            // Unticking "Stay signed in" / "Remember username" forgets the server login too.
            var config2 = new OsuConfigManager(storage);
            using (var login = new PatchsuServerLogin(storage, config2))
            {
                Assert.That(login.Get<string>(PatchsuServerLoginSetting.Token), Is.EqualTo("server-token"));

                config2.SetValue(OsuSetting.SavePassword, false);
                Assert.That(login.Get<string>(PatchsuServerLoginSetting.Token), Is.Empty);

                config2.SetValue(OsuSetting.SaveUsername, false);
                Assert.That(login.Get<string>(PatchsuServerLoginSetting.Username), Is.Empty);
            }

            config2.Dispose();
        }

        [Test]
        [Category("Online")]
        public void TestLiveLoginReachesServerWithAcceptedClient()
        {
            var e = new ProductionEndpointConfiguration();
            var oauth = new OAuth(e.APIClientID, e.APIClientSecret, e.APIUrl);

            // A made-up account: the server must answer "wrong username or password" (invalid_grant),
            // not "unknown client" (invalid_client), so the URL and client credentials are right.
            var ex = Assert.Catch(() => oauth.AuthenticateWithLogin("patchsu-probe-nonexistent-zz9", "x"));
            TestContext.Out.WriteLine(ex?.ToString());
            Assert.That(ex?.ToString(), Does.Contain("Invalid username or password"));
        }

        [Test]
        [Category("Online")]
        public async Task TestLiveLeaderboard()
        {
            var e = new ProductionEndpointConfiguration();

            // The same URL GetScoresRequest builds (beatmap 75, global, osu!).
            using var req = new OsuJsonWebRequest<APIScoresCollection>($"{e.APIUrl}/api/v2/beatmaps/75/scores?type=global&mode=osu&limit=50");
            await req.PerformAsync().ConfigureAwait(false);

            var scores = req.ResponseObject!.Scores;
            TestContext.Out.WriteLine(string.Join("\n", scores.Select(s => $"{s.User!.Username} {s.TotalScore} {string.Join(",", s.Mods.Select(m => m.Acronym))} {s.User!.AvatarUrl}")));

            Assert.That(scores, Is.Not.Empty);
            Assert.That(scores.All(s => s.User != null && s.User.Id > 0), Is.True);

            // Avatars come from the server's own host, which the patch trusts.
            var store = new TrustedDomainOnlineStore();
            var lookup = typeof(TrustedDomainOnlineStore).GetMethod("GetLookupUrl", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var s in scores.Where(s => !string.IsNullOrEmpty(s.User!.AvatarUrl)))
                Assert.That(lookup.Invoke(store, new object[] { s.User!.AvatarUrl! }), Is.EqualTo(s.User!.AvatarUrl));
        }
    }
}

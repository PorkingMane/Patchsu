// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// End-to-end test for server patch 1000: the patched game (real APIAccess, ChannelManager and notifications websocket)
// against a local mock lazer server (Patchsu/work/p1000-tests/mock_lazer_server.py, run with uv + aiohttp).
// Copy into osu.Game.Tests/NonVisual; set PATCHSU_MOCK_SERVER to the script and PATCHSU_MOCK_SCORES to a leaderboard JSON
// (mame-scores-b75.json: a real Mamestagram response), and OSU_INSECURE_REQUESTS=1 (the mock is plain http).
// Runs in a temporary data folder.

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Framework;
using osu.Framework.Extensions;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Rulesets.Osu;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public partial class PatchsuServerChatTest
    {
        private Process? server;
        private string root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            string? script = Environment.GetEnvironmentVariable("PATCHSU_MOCK_SERVER");
            string? scores = Environment.GetEnvironmentVariable("PATCHSU_MOCK_SCORES");
            if (script == null || scores == null)
                Assert.Ignore("PATCHSU_MOCK_SERVER / PATCHSU_MOCK_SCORES not set");
            if (!FrameworkEnvironment.AllowInsecureRequests)
                Assert.Ignore("OSU_INSECURE_REQUESTS=1 not set");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            root = $"http://127.0.0.1:{port}";
            server = Process.Start(new ProcessStartInfo("uv", $"run --no-project --with aiohttp python -I \"{script}\" {port} \"{scores}\"") { UseShellExecute = false })!;

            using var http = new System.Net.Http.HttpClient();
            waitFor(() =>
            {
                try { return http.GetAsync($"{root}/_log").GetResultSafely().IsSuccessStatusCode; }
                catch { return false; }
            }, "mock server didn't start", 60);
        }

        [TearDown]
        public void TearDown()
        {
            server?.Kill(true);
            server?.Dispose();
        }

        [Test]
        public void TestLoginChatAndLeaderboard()
        {
            using (var host = new CleanRunHeadlessGameHost())
            {
                var game = new MockServerGame(root);
                Task.Factory.StartNew(() => host.Run(game), TaskCreationOptions.LongRunning);
                waitFor(() => game.IsLoaded, "game didn't start");

                try
                {
                    var api = game.API;
                    Assert.That(api, Is.InstanceOf<APIAccess>(), "the real API, not a dummy");
                    waitFor(() => api.State.Value == APIState.Offline, "API not offline at start");

                    // Wrong password: refused by the server.
                    api.Login("someone-else", "x");
                    waitFor(() => api.State.Value == APIState.Offline && api.LastLoginError != null, "bad login not refused");
                    TestContext.Out.WriteLine($"bad login: {api.LastLoginError?.Message}");

                    // A refused login resets the user to Guest a frame later (upstream behaviour); let that happen first.
                    bool settled = false;
                    host.UpdateThread.Scheduler.Add(() => host.UpdateThread.Scheduler.Add(() => host.UpdateThread.Scheduler.Add(() => settled = true)));
                    waitFor(() => settled, "update thread stuck");

                    // Real login.
                    api.Login("patchsu-tester", "secret");
                    waitFor(() => api.State.Value == APIState.Online, "didn't get online");
                    waitFor(() => api.LocalUser.Value?.Username == "patchsu-tester", "local user not set from /me");

                    // The token went to the server's own login file, not game.ini.
                    waitFor(() => host.Storage.Exists("patchsu-mamestagram.ini") && readAll(host.Storage, "patchsu-mamestagram.ini").Contains("mock-refresh"), "login file not written");
                    // (debug builds keep their settings in game.dev.ini)
                    game.GameConfig.Save();
                    string gameIni = game.GameStorage.Exists("game.ini") ? "game.ini" : "game.dev.ini";
                    Assert.That(readAll(game.GameStorage, gameIni), Does.Contain("Username").And.Not.Contain("mock-refresh").And.Not.Contain("patchsu-tester"));

                    // Chat: the channel list loads, #osu is joined, history arrives, a posted message is accepted
                    // and the server's reply comes in over the notifications websocket.
                    ChannelManager channels = null!;
                    host.UpdateThread.Scheduler.Add(() => game.Add(channels = new ChannelManager(api)));
                    waitFor(() => channels?.JoinedChannels.Any(c => c.Name == "#osu") == true, "#osu not joined");

                    var osuChannel = channels.JoinedChannels.Single(c => c.Name == "#osu");
                    waitFor(() => osuChannel.Messages.Any(m => m.Content == "welcome"), "history not loaded");

                    host.UpdateThread.Scheduler.Add(() => channels.PostMessage("hello from patchsu", target: osuChannel));
                    waitFor(() => osuChannel.Messages.Any(m => m.Content == "hello from patchsu" && m.Id != null), "own message not confirmed");
                    waitFor(() => osuChannel.Messages.Any(m => m.Content == "echo: hello from patchsu"), "websocket reply not received");

                    // Leaderboard through GetScoresRequest (the server's JSON is a captured real Mamestagram response).
                    var beatmap = new BeatmapInfo { OnlineID = 75, Ruleset = new OsuRuleset().RulesetInfo };
                    var scoresReq = new GetScoresRequest(beatmap, beatmap.Ruleset);
                    APIScoresCollection? result = null;
                    scoresReq.Success += r => result = r;
                    api.Queue(scoresReq);
                    waitFor(() => result != null, "leaderboard not loaded");
                    Assert.That(result!.Scores, Is.Not.Empty);
                    TestContext.Out.WriteLine($"leaderboard: {result.Scores.Count} scores, #1 {result.Scores[0].User?.Username}");

                    // What the client sent: only login, chat and leaderboard calls; the websocket got chat.start.
                    using var http = new System.Net.Http.HttpClient();
                    var log = JArray.Parse(http.GetStringAsync($"{root}/_log").GetResultSafely());
                    foreach (var entry in log)
                        TestContext.Out.WriteLine($"{entry["method"]} {entry["path"]} {entry["auth"]} {entry["data"]?.ToString(Newtonsoft.Json.Formatting.None)}");

                    Assert.That(log.Any(e => (string?)e["method"] == "WS" && (string?)e["auth"] == "Bearer mock-acc"), "websocket opened with the token");
                    Assert.That(log.Any(e => (string?)e["method"] == "WS-MSG" && (string?)e["data"]?["event"] == "chat.start"), "chat.start sent");
                    Assert.That(log.Where(e => (string?)e["path"] != null).All(e => !((string)e["path"]!).Contains("/solo/scores") && !((string)e["path"]!).Contains("/rooms")), "no score submission or multiplayer");
                    Assert.That(log.Any(e => (string?)e["path"] == "/api/v2/notifications"), "notification endpoint asked for");

                }
                finally
                {
                    TestContext.Out.WriteLine($"DIAG host files: {string.Join(", ", host.Storage.GetFiles(""))} | game files: {string.Join(", ", game.GameStorage.GetFiles(""))} | {game.GameStorage.GetFullPath("")} | config {game.GameConfig.GetType().Name}");
                    TestContext.Out.WriteLine($"DIAG state={game.API.State.Value} user={game.API.LocalUser.Value?.Username}/{game.API.LocalUser.Value?.Id}");
                    using (var diag = new System.Net.Http.HttpClient())
                        TestContext.Out.WriteLine("DIAG " + diag.GetStringAsync($"{root}/_log").GetResultSafely());
                    game.Exit();
                }
            }
        }

        private static string readAll(Storage storage, string file)
        {
            using var s = storage.GetStream(file);
            using var reader = new System.IO.StreamReader(s);
            return reader.ReadToEnd();
        }

        private static void waitFor(Func<bool> condition, string message, int seconds = 30)
        {
            var sw = Stopwatch.StartNew();
            while (!condition())
            {
                if (sw.Elapsed.TotalSeconds > seconds)
                    Assert.Fail(message);
                Thread.Sleep(50);
            }
        }

        private partial class MockServerGame : OsuGameBase
        {
            private readonly string root;

            public MockServerGame(string root)
            {
                this.root = root;
            }

            public new IAPIProvider API => base.API;

            public Storage GameStorage => Storage;

            public OsuConfigManager GameConfig => LocalConfig;

            // Same client id / secret as the patched production endpoints (the mock checks them), only a local URL.
            public override EndpointConfiguration CreateEndpoints()
            {
                var production = new ProductionEndpointConfiguration();

                return new EndpointConfiguration
                {
                    WebsiteUrl = root,
                    APIUrl = root,
                    APIClientID = production.APIClientID,
                    APIClientSecret = production.APIClientSecret,
                    SpectatorUrl = $"{root}/spectator",
                    MultiplayerUrl = $"{root}/multiplayer",
                    MetadataUrl = $"{root}/metadata",
                };
            }
        }
    }
}

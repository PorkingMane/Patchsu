// Patchsu 0002 local test (not part of the patch): the osu! API key client used by "Fetch top plays" for other players,
// against a local mock server. Copy into osu.Game.Tests/Visual/Navigation and run `dotnet test --filter BanchoApiKeySourceTest`.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.LocalPriority;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets.Osu;

namespace osu.Game.Tests.Visual.Navigation
{
    [TestFixture]
    public class BanchoApiKeySourceTest
    {
        private HttpListener listener = null!;
        private string url = null!;
        private readonly ConcurrentQueue<(string Method, string Path, string? Auth, string? Version, string Body)> requests = new ConcurrentQueue<(string, string, string?, string?, string)>();
        private int tokensIssued;
        private bool rejectFirstBearer;
        private bool rejectKey;

        [SetUp]
        public void SetUp()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            url = $"http://127.0.0.1:{port}";
            listener = new HttpListener();
            listener.Prefixes.Add(url + "/");
            listener.Start();
            requests.Clear();
            tokensIssued = 0;
            rejectFirstBearer = rejectKey = false;
            ApiKeyBanchoPlaySource.ForgetToken();
            _ = Task.Run(serve);
        }

        [TearDown]
        public void TearDown() => listener.Close();

        private async Task serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                var request = context.Request;
                string body = new StreamReader(request.InputStream).ReadToEnd();
                requests.Enqueue((request.HttpMethod, request.Url!.PathAndQuery, request.Headers["Authorization"], request.Headers["x-api-version"], body));

                var response = context.Response;
                string path = request.Url.AbsolutePath;
                byte[] output = Array.Empty<byte>();

                if (path == "/oauth/token")
                {
                    if (rejectKey)
                        response.StatusCode = 401;
                    else
                    {
                        int n = Interlocked.Increment(ref tokensIssued);
                        output = Encoding.UTF8.GetBytes($"{{\"token_type\":\"Bearer\",\"expires_in\":86400,\"access_token\":\"token{n}\"}}");
                    }
                }
                else if (rejectFirstBearer && request.Headers["Authorization"] == "Bearer token1")
                    response.StatusCode = 401;
                else if (path == "/api/v2/users/some%20player/osu")
                    output = Encoding.UTF8.GetBytes("{\"id\":5678,\"username\":\"some player\"}");
                else if (path == "/api/v2/users/5678/scores/best")
                    output = Encoding.UTF8.GetBytes("[{\"id\":42,\"beatmap_id\":7,\"ruleset_id\":0,\"total_score\":1000,\"has_replay\":true,\"beatmap\":{\"id\":7,\"checksum\":\"abc\"}}]");
                else if (path == "/api/v2/scores/42/download")
                    output = new byte[] { 1, 2, 3, 4 };
                else
                    response.StatusCode = 404;

                await response.OutputStream.WriteAsync(output);
                response.Close();
            }
        }

        private ApiKeyBanchoPlaySource source() => new ApiKeyBanchoPlaySource(url, 20250101, " 12345 ", "secret");

        [Test]
        public async Task TestFetchesWithOwnKey()
        {
            var ruleset = new OsuRuleset().RulesetInfo;
            var s = source();

            var user = await s.GetUser("some player", ruleset, CancellationToken.None);
            Assert.That(user?.Id, Is.EqualTo(5678));

            var plays = await s.GetBestPlays(5678, ruleset, 100, 100, CancellationToken.None);
            Assert.That(plays?.Single().OnlineID, Is.EqualTo(42));
            Assert.That(plays!.Single().Beatmap?.Checksum, Is.EqualTo("abc"));

            string? replay = await s.DownloadReplay(plays!.Single(), CancellationToken.None);
            Assert.That(replay, Does.EndWith(".osr"));
            Assert.That(File.ReadAllBytes(replay!), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            File.Delete(replay!);

            var all = requests.ToArray();
            Assert.That(tokensIssued, Is.EqualTo(1), "one token, reused");
            Assert.That(all[0].Body, Does.Contain("client_id=12345").And.Contain("client_secret=secret").And.Contain("grant_type=client_credentials").And.Contain("scope=public"));
            Assert.That(all.Skip(1).All(r => r.Auth == "Bearer token1" && r.Version == "20250101"));
            Assert.That(all.Any(r => r.Path == "/api/v2/users/5678/scores/best?mode=osu&limit=100&offset=100"));
        }

        [Test]
        public async Task TestRenewsRejectedToken()
        {
            ApiKeyBanchoPlaySource.ForgetToken();
            rejectFirstBearer = true;

            var user = await source().GetUser("some player", new OsuRuleset().RulesetInfo, CancellationToken.None);
            Assert.That(user?.Id, Is.EqualTo(5678));
            Assert.That(tokensIssued, Is.EqualTo(2));
        }

        [Test]
        public void TestRejectedKey()
        {
            ApiKeyBanchoPlaySource.ForgetToken();
            rejectKey = true;

            Assert.ThrowsAsync<BanchoApiKeyRejectedException>(() => source().GetUser("some player", new OsuRuleset().RulesetInfo, CancellationToken.None));
        }

        [Test]
        public async Task TestRequestsSpacedOut()
        {
            var s = source();
            var ruleset = new OsuRuleset().RulesetInfo;
            await s.GetUser("some player", ruleset, CancellationToken.None);

            long start = Environment.TickCount64;
            await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => s.GetUser("some player", ruleset, CancellationToken.None)));
            Assert.That(Environment.TickCount64 - start, Is.GreaterThanOrEqualTo(2 * BanchoRequestLimiter.INTERVAL_MS - 50));
        }
    }
}

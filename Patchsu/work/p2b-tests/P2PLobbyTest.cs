// Patchsu 0002b local test (not part of any patch): P2P lobby codes and joining over loopback.
// Copy into osu.Game.Tests/NonVisual and run `dotnet test --filter P2PLobbyTest`.

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Online.P2P;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    [NonParallelizable]
    public class P2PLobbyTest
    {
        [TestCase("192.168.0.1")]
        [TestCase("10.0.0.255")]
        [TestCase("255.255.255.255")]
        [TestCase("0.0.0.0")]
        public void TestCodeRoundTrip(string ip)
        {
            string code = P2PLobby.EncodeCode(IPAddress.Parse(ip));

            Assert.That(code, Has.Length.EqualTo(9));
            Assert.That(P2PLobby.TryDecodeCode(code, out var decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(IPAddress.Parse(ip)));

            // Lower case, no dash, spaces.
            Assert.That(P2PLobby.TryDecodeCode($" {code.Replace("-", "").ToLowerInvariant()} ", out decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(IPAddress.Parse(ip)));
        }

        [Test]
        public void TestInvalidCodes()
        {
            string code = P2PLobby.EncodeCode(IPAddress.Parse("192.168.1.20"));
            char changed = code[2] == 'A' ? 'B' : 'A';

            Assert.That(P2PLobby.TryDecodeCode(code[..2] + changed + code[3..], out _), Is.False, "checksum");
            Assert.That(P2PLobby.TryDecodeCode("ABC", out _), Is.False);
            Assert.That(P2PLobby.TryDecodeCode("ABCD-EFG!", out _), Is.False);
            Assert.That(P2PLobby.TryDecodeCode(string.Empty, out _), Is.False);
        }

        [Test]
        public async Task TestJoinAndLeave()
        {
            string code = P2PLobby.EncodeCode(IPAddress.Loopback);

            using var host = P2PLobby.Host(new P2PPeer { Username = "host", UserId = -5, Avatar = new byte[] { 1, 2, 3 } });

            P2PPeer[] hostSees = Array.Empty<P2PPeer>();
            host.PeersChanged += p => hostSees = p;

            var first = await P2PLobby.JoinAsync(code, new P2PPeer { Username = "first", UserId = 2 });
            P2PPeer[] firstSees = Array.Empty<P2PPeer>();
            first.PeersChanged += p => firstSees = p;

            using var second = await P2PLobby.JoinAsync(code, new P2PPeer { Username = "second", UserId = -3 });
            P2PPeer[] secondSees = Array.Empty<P2PPeer>();
            second.PeersChanged += p => secondSees = p;

            await waitFor(() => hostSees.Length == 2 && firstSees.Length == 2 && secondSees.Length == 2);

            Assert.That(hostSees.Select(p => p.Username), Is.EquivalentTo(new[] { "first", "second" }));
            Assert.That(firstSees.Select(p => p.Username), Is.EquivalentTo(new[] { "host", "second" }));
            Assert.That(secondSees.Select(p => p.Username), Is.EquivalentTo(new[] { "host", "first" }));
            Assert.That(secondSees.Single(p => p.Username == "host").Avatar, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(hostSees.Select(p => p.Id).Distinct().Count(), Is.EqualTo(2));
            Assert.That(hostSees.All(p => p.Id > 0));

            bool secondLost = false;
            second.Disconnected += () => secondLost = true;

            first.Dispose();
            await waitFor(() => hostSees.Length == 1 && secondSees.Length == 1);
            Assert.That(secondSees.Single().Username, Is.EqualTo("host"));

            host.Dispose();
            await waitFor(() => secondLost);
        }

        [Test]
        public void TestJoinWithoutHostFails()
        {
            Assert.ThrowsAsync(Is.InstanceOf<Exception>(), () => P2PLobby.JoinAsync(P2PLobby.EncodeCode(IPAddress.Loopback), new P2PPeer()));
        }

        private static async Task waitFor(Func<bool> condition)
        {
            for (int i = 0; i < 100 && !condition(); i++)
                await Task.Delay(50, CancellationToken.None);

            Assert.That(condition(), Is.True);
        }
    }
}

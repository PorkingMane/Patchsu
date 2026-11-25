// Patchsu patch 5b test (not part of the patch): parsing and matching of the "scorewithmod" search keyword.

using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Game.Online.API;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Screens.Select;

namespace osu.Game.Tests.NonVisual.Filtering
{
    [TestFixture]
    public class ScoreWithModFilterTest
    {
        private static string json(params Mod[] mods) => mods.Length == 0 ? string.Empty : JsonConvert.SerializeObject(mods.Select(m => new APIMod(m)).ToArray());

        private static readonly string hd = json(new OsuModHidden());
        private static readonly string hddt = json(new OsuModHidden(), new OsuModDoubleTime { SpeedChange = { Value = 1.3 } });
        private static readonly string dt = json(new OsuModDoubleTime());
        private static readonly string cl = json(new OsuModClassic());
        private static readonly string none = json();
        private static readonly string wu = json(new ModWindUp());

        private static FilterCriteria parse(string query, out string remaining)
        {
            var criteria = new FilterCriteria();
            FilterQueryParser.ApplyQueries(criteria, query);
            remaining = criteria.SearchText;
            return criteria;
        }

        private static bool[] matchAll(string query, params string[] scores)
        {
            var criteria = parse(query, out _);
            Assert.That(criteria.ScoreWithMods, Is.Not.Null);
            var matcher = criteria.ScoreWithMods!.CreateMatcher(new OsuRuleset());
            return scores.Select(matcher).ToArray();
        }

        [Test]
        public void TestNoKeyword()
        {
            var criteria = parse("artist=foo hidden", out string remaining);
            Assert.That(criteria.ScoreWithMods, Is.Null);
            Assert.That(remaining.Trim(), Is.EqualTo("hidden"));
        }

        [Test]
        public void TestKeywordRemovedFromSearchText()
        {
            parse("camellia scorewithmod='Wind Up',HD stars>5 [Insane]", out string remaining);
            Assert.That(remaining, Does.Not.Contain("scorewithmod"));
            Assert.That(remaining, Does.Not.Contain("Wind"));
            Assert.That(remaining, Does.Contain("camellia"));
        }

        [Test]
        public void TestOtherKeywordsStillWork()
        {
            var criteria = parse("scorewithmod=HD stars>5 creator=peppy", out _);
            Assert.That(criteria.StarDifficulty.Min, Is.EqualTo(5).Within(0.01));
            Assert.That(criteria.Creator.SearchTerm, Is.EqualTo("peppy"));
        }

        [TestCase("scorewithmod=HD")]
        [TestCase("scorewithmod:hd")]
        [TestCase("ScoreWithMod=Hd")]
        [TestCase("scorewithmod=hidden")]
        [TestCase("scorewithmod='Hidden'")]
        [TestCase("scorewithmod=\"hidden\"")]
        [TestCase("scoreswithmod=HD")]
        [TestCase("playwithmod=HD")]
        [TestCase("PlayWithMod:'hidden'")]
        public void TestSingleMod(string query)
            => Assert.That(matchAll(query, hd, hddt, dt, cl, none), Is.EqualTo(new[] { true, true, false, false, false }));

        [TestCase("scorewithmod=HD,DT")]
        [TestCase("scorewithmod=HD+DT")]
        [TestCase("scorewithmod=HD scorewithmod=DT")]
        [TestCase("scorewithmod='Hidden','Double Time'")]
        [TestCase("scorewithmod=HD,'double time'")]
        [TestCase("playwithmod=HD scoreswithmod=DT")]
        public void TestSeveralModsOnOneScore(string query)
            => Assert.That(matchAll(query, hd, hddt, dt, cl, none), Is.EqualTo(new[] { false, true, false, false, false }));

        [Test]
        public void TestExcludedMod()
            => Assert.That(matchAll("scorewithmod=HD scorewithmod!=DT", hd, hddt, dt, cl, none), Is.EqualTo(new[] { true, false, false, false, false }));

        [Test]
        public void TestOnlyExcluded()
            => Assert.That(matchAll("scorewithmod!=HD", hd, hddt, dt, cl, none), Is.EqualTo(new[] { false, false, true, true, true }));

        [Test]
        public void TestAliasesExcluded()
            => Assert.That(matchAll("playwithmod=HD scoreswithmod!=DT", hd, hddt, dt, cl, none), Is.EqualTo(new[] { true, false, false, false, false }));

        [TestCase("playwithmod='Wind Up' artist=foo")]
        [TestCase("scoreswithmod='Wind Up' artist=foo")]
        public void TestAliasesRemovedFromSearchText(string query)
        {
            var criteria = parse("camellia " + query, out string remaining);
            Assert.That(remaining.Trim(), Is.EqualTo("camellia"));
            Assert.That(criteria.Artist.SearchTerm, Is.EqualTo("foo"));
        }

        [Test]
        public void TestNoMod()
        {
            Assert.That(matchAll("scorewithmod=NM", hd, hddt, dt, cl, none), Is.EqualTo(new[] { false, false, false, true, true }));
            Assert.That(matchAll("scorewithmod!=NM", hd, hddt, dt, cl, none), Is.EqualTo(new[] { true, true, true, false, false }));
        }

        [Test]
        public void TestNameWithSpaces()
        {
            Assert.That(matchAll("scorewithmod='Wind Up'", wu, hd), Is.EqualTo(new[] { true, false }));
            // a quote left open at the end (while typing).
            Assert.That(matchAll("scorewithmod='wind up", wu, hd), Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public void TestQuotedNameIsExact()
        {
            // a quoted name has to be the whole name, and a quoted acronym is not a name.
            Assert.That(matchAll("scorewithmod='Wind'", wu, hd), Is.EqualTo(new[] { false, false }));
            Assert.That(matchAll("scorewithmod='HD'", wu, hd), Is.EqualTo(new[] { false, false }));
        }

        [Test]
        public void TestUnknownMod()
        {
            Assert.That(matchAll("scorewithmod='No Such Mod'", hd, none), Is.EqualTo(new[] { false, false }));
            Assert.That(matchAll("scorewithmod=HD scorewithmod!='No Such Mod'", hd, none), Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public void TestKeyIdentifiesFilter()
        {
            Assert.That(parse("scorewithmod=hd,dt", out _).ScoreWithMods!.Key, Is.EqualTo(parse("scorewithmod=HD scorewithmod=DT", out _).ScoreWithMods!.Key));
            Assert.That(parse("playwithmod=HD", out _).ScoreWithMods!.Key, Is.EqualTo(parse("scoreswithmod=hd", out _).ScoreWithMods!.Key));
            Assert.That(parse("scorewithmod=HD", out _).ScoreWithMods!.Key, Is.Not.EqualTo(parse("scorewithmod!=HD", out _).ScoreWithMods!.Key));
        }
    }
}

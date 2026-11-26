// Patchsu patch 3b test (not part of the patch): parsing and matching of the "playdate" search keyword.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Screens.Select;

namespace osu.Game.Tests.NonVisual.Filtering
{
    [TestFixture]
    public class ScoreDateFilterTest
    {
        private static readonly DateTimeOffset now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

        private static FilterCriteria parse(string query, out string remaining)
        {
            var criteria = new FilterCriteria();
            FilterQueryParser.ApplyQueries(criteria, query);
            remaining = criteria.SearchText;
            return criteria;
        }

        /// <summary>
        /// Which of the given ages (in days back from <see cref="now"/>) match.
        /// </summary>
        private static bool[] matchDaysAgo(string query, params double[] daysAgo)
        {
            var criteria = parse(query, out _);
            Assert.That(criteria.ScoreDate, Is.Not.Null);
            var matcher = criteria.ScoreDate!.CreateMatcher(now);
            return daysAgo.Select(d => matcher(now.AddDays(-d))).ToArray();
        }

        [Test]
        public void TestNoKeyword()
        {
            var criteria = parse("artist=foo lastplayed<3d", out _);
            Assert.That(criteria.ScoreDate, Is.Null);
            Assert.That(criteria.HasLocalScoreFilter, Is.False);
        }

        [Test]
        public void TestKeywordRemovedFromSearchText()
        {
            var criteria = parse("camellia playdate<=180d stars>5", out string remaining);
            Assert.That(remaining.Trim(), Is.EqualTo("camellia"));
            Assert.That(criteria.StarDifficulty.Min, Is.EqualTo(5).Within(0.01));
            Assert.That(criteria.HasLocalScoreFilter, Is.True);
        }

        [TestCase("playdate<=180d")]
        [TestCase("scoredate<=180d")]
        [TestCase("datescore<=180")]
        [TestCase("PlayDate<:180d")]
        public void TestAtMost(string query)
            => Assert.That(matchDaysAgo(query, 0, 179.9, 180, 180.1, 400), Is.EqualTo(new[] { true, true, true, false, false }));

        [Test]
        public void TestLess() => Assert.That(matchDaysAgo("playdate<180d", 179.9, 180, 180.1), Is.EqualTo(new[] { true, false, false }));

        [Test]
        public void TestMoreThan() => Assert.That(matchDaysAgo("playdate>4w", 0, 27.9, 28, 28.1, 400), Is.EqualTo(new[] { false, false, false, true, true }));

        [Test]
        public void TestAtLeast() => Assert.That(matchDaysAgo("playdate>=4w", 27.9, 28, 28.1), Is.EqualTo(new[] { false, true, true }));

        [Test]
        public void TestEqualUsesSmallestUnit()
        {
            Assert.That(matchDaysAgo("playdate=3d", 2.9, 3, 3.5, 3.9, 4.1), Is.EqualTo(new[] { false, true, true, true, false }));
            Assert.That(matchDaysAgo("playdate=1w", 6.9, 7, 13.9, 14.1), Is.EqualTo(new[] { false, true, true, false }));
            Assert.That(matchDaysAgo("playdate=1w2d", 8.9, 9, 9.9, 10.1), Is.EqualTo(new[] { false, true, true, false }));
        }

        [Test]
        public void TestNotEqual() => Assert.That(matchDaysAgo("playdate!=3d", 2.9, 3.5, 4.1), Is.EqualTo(new[] { true, false, true }));

        [Test]
        public void TestUnits()
        {
            Assert.That(matchDaysAgo("playdate<1y", 364, 366), Is.EqualTo(new[] { true, false }));
            Assert.That(matchDaysAgo("playdate<6M", 180, 185), Is.EqualTo(new[] { true, false }));
            Assert.That(matchDaysAgo("playdate<6mo", 180, 185), Is.EqualTo(new[] { true, false }));
            Assert.That(matchDaysAgo("playdate<1y6M", 540, 550), Is.EqualTo(new[] { true, false }));
            Assert.That(matchDaysAgo("playdate<12h", 0.4, 0.6), Is.EqualTo(new[] { true, false }));
            Assert.That(matchDaysAgo("playdate<30m", 20 / 1440.0, 40 / 1440.0), Is.EqualTo(new[] { true, false }));
            Assert.That(matchDaysAgo("playdate<1.5d", 1.4, 1.6), Is.EqualTo(new[] { true, false }));
            Assert.That(matchDaysAgo("playdate=1M", 20, 40, 70), Is.EqualTo(new[] { false, true, false }));
        }

        [Test]
        public void TestRangeOfConditions()
            => Assert.That(matchDaysAgo("playdate>1w playdate<4w", 3, 10, 30), Is.EqualTo(new[] { false, true, false }));

        [TestCase("playdate<abc")]
        [TestCase("playdate<")]
        [TestCase("playdate<5x")]
        [TestCase("playdate<d")]
        public void TestInvalidValueStaysInSearchText(string query)
        {
            var criteria = parse(query, out string remaining);
            Assert.That(criteria.ScoreDate, Is.Null);
            Assert.That(remaining, Is.EqualTo(query));
        }

        [Test]
        public void TestCombinesWithScoreWithMod()
        {
            var criteria = parse("scorewithmod=HD playdate<4w", out string remaining);
            Assert.That(criteria.ScoreWithMods, Is.Not.Null);
            Assert.That(criteria.ScoreDate, Is.Not.Null);
            Assert.That(remaining.Trim(), Is.Empty);
        }

        [Test]
        public void TestKeyIgnoresTime()
        {
            Assert.That(parse("playdate<4w", out _).ScoreDate!.Key, Is.EqualTo(parse("playdate<4w", out _).ScoreDate!.Key));
            Assert.That(parse("playdate<4w", out _).ScoreDate!.Key, Is.Not.EqualTo(parse("playdate>4w", out _).ScoreDate!.Key));
        }
    }
}

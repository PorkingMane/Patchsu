// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    [NonParallelizable]
    public class ModCompatibilityOverridesTest
    {
        [TearDown]
        public void TearDown() => ModCompatibilityOverrides.HardRockMirror.Enabled = false;

        [Test]
        public void TestHardRockMirrorFollowsToggle()
        {
            var mods = new Mod[] { new OsuModHardRock(), new OsuModMirror() };

            ModCompatibilityOverrides.HardRockMirror.Enabled = false;
            Assert.That(ModUtils.CheckCompatibleSet(mods), Is.False);

            ModCompatibilityOverrides.HardRockMirror.Enabled = true;
            Assert.That(ModUtils.CheckCompatibleSet(mods), Is.True);
            Assert.That(ModUtils.CheckValidForGameplay(mods, out _), Is.True);
        }

        [Test]
        public void TestOtherIncompatibilitiesKept()
        {
            ModCompatibilityOverrides.HardRockMirror.Enabled = true;

            Assert.That(ModUtils.CheckCompatibleSet(new Mod[] { new OsuModHardRock(), new OsuModEasy() }), Is.False);
            Assert.That(ModUtils.CheckCompatibleSet(new Mod[] { new OsuModHardRock(), new OsuModDifficultyAdjust() }), Is.False);
            Assert.That(ModUtils.CheckCompatibleSet(new Mod[] { new OsuModHardRock(), new OsuModMirror(), new OsuModEasy() }), Is.False);
        }

        [Test]
        public void TestAllowAllScope()
        {
            var mods = new Mod[] { new OsuModHardRock(), new OsuModMirror() };

            using (ModCompatibilityOverrides.AllowAll())
                Assert.That(ModUtils.CheckCompatibleSet(mods), Is.True);

            Assert.That(ModUtils.CheckCompatibleSet(mods), Is.False);
        }
    }
}

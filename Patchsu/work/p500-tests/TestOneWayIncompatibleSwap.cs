// Patchsu local test (not part of any patch): 0001's mod swap in UserModSelectOverlay. Patch mods list vanilla mods as incompatible
// without the vanilla mod listing them back; clicking the other mod must still replace them.
// Copy into osu.Game.Rulesets.Osu.Tests/Mods and run `dotnet test --filter TestOneWayIncompatibleSwap`.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;

namespace osu.Game.Rulesets.Osu.Tests.Mods
{
    public class TestOneWayIncompatibleSwap
    {
        [TestCase(typeof(OsuModNoSliding), typeof(OsuModStrictTracking))]
        [TestCase(typeof(OsuModHalfStream), typeof(OsuModRandomV2))]
        [TestCase(typeof(OsuModReversedSliders), typeof(OsuModNoSliding))]
        [TestCase(typeof(OsuModStrictTracking), typeof(OsuModNoSliding))]
        [TestCase(typeof(OsuModHidden), typeof(OsuModHardRock))]
        public void TestClickedModReplacesSelected(Type selected, Type clicked)
        {
            var oldMod = (Mod)Activator.CreateInstance(selected)!;
            var newMod = (Mod)Activator.CreateInstance(clicked)!;
            bool compatible = !oldMod.IncompatibleMods.Any(t => t.IsInstanceOfType(newMod)) && !newMod.IncompatibleMods.Any(t => t.IsInstanceOfType(oldMod));

            var result = new SwapOverlay().Compute(new[] { oldMod }, new[] { oldMod, newMod });

            Assert.That(result, Does.Contain(newMod));
            Assert.That(result.Contains(oldMod), Is.EqualTo(compatible));
        }

        private partial class SwapOverlay : UserModSelectOverlay
        {
            public IReadOnlyList<Mod> Compute(IReadOnlyList<Mod> oldSelection, IReadOnlyList<Mod> newSelection) => ComputeNewModsFromSelection(oldSelection, newSelection);
        }
    }
}

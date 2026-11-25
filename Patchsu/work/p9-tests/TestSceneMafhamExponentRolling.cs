// Patchsu patch 9 test (not part of the patch): the brightness exponent after −∞ rolls like the value (mod select footer
// and song select wedge share AttributeExponentCounter). Copy into osu.Game.Tests/Visual/UserInterface and run
// dotnet test osu.Game.Tests --filter "FullyQualifiedName~TestSceneMafhamExponentRolling"

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Mods;

namespace osu.Game.Tests.Visual.UserInterface
{
    public partial class TestSceneMafhamExponentRolling : OsuTestScene
    {
        private AttributeExponentCounter counter = null!;
        private readonly List<string> seen = new List<string>();

        private string text => counter.ChildrenOfType<OsuSpriteText>().Single().Text.ToString();

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create", () =>
            {
                seen.Clear();
                Child = counter = new AttributeExponentCounter(OsuFont.Default.With(size: 20)) { Anchor = Anchor.Centre, Origin = Anchor.Centre };
                counter.OnUpdate += _ =>
                {
                    if (seen.LastOrDefault() != text)
                        seen.Add(text);
                };
            });
            AddUntilStep("loaded", () => counter.IsLoaded);
        }

        [Test]
        public void TestRollsBetweenValues()
        {
            AddAssert("hidden with none", () => counter.Alpha, () => Is.Zero);
            AddStep("set 0.1", () => counter.SetExponent(0.1f));
            AddUntilStep("shows ^.1", () => text, () => Is.EqualTo("^.1"));
            AddAssert("visible", () => counter.Alpha, () => Is.EqualTo(1));
            AddAssert("rolled through values in between", () => seen.Count(t => t.StartsWith('^')), () => Is.GreaterThan(3));

            AddStep("clear seen", () => seen.Clear());
            AddStep("set 0.5", () => counter.SetExponent(0.5f));
            AddUntilStep("shows ^.5", () => text, () => Is.EqualTo("^.5"));
            AddAssert("rolled up from .1", () => seen.Count, () => Is.GreaterThan(3));

            AddStep("set none", () => counter.SetExponent(null));
            AddUntilStep("hidden again", () => counter.Alpha, () => Is.Zero);
            AddAssert("empty text", () => text, () => Is.Empty);
        }
    }
}

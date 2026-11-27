// Patchsu 0002 local test (not part of the patch): Settings → Patchsu → Profile Colors tints the profile page (not SD / PF ones).
// Copy into osu.Game.Tests/Visual/Navigation and run `dotnet test --filter TestSceneProfileColours`.

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.LocalPriority;
using osu.Game.Overlays;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneProfileColours : OsuGameTestScene
    {
        private LocalProfileManager manager => Game.Dependencies.Get<LocalProfileManager>();
        private LocalProfileOverlay overlay => Game.ChildrenOfType<LocalProfileOverlay>().Single();
        private OverlayColourProvider pageColours => (OverlayColourProvider)typeof(FullscreenOverlay<LocalProfileOverlay.LocalProfileHeader>)
                                                                              .GetField(@"ColourProvider", BindingFlags.Instance | BindingFlags.NonPublic)!
                                                                              .GetValue(overlay)!;

        [Test]
        public void TestProfileColours()
        {
            AddStep("create profile", () => manager.Switch(manager.CreateDefaultProfile().Id));
            AddUntilStep("page ready", () => Game.ChildrenOfType<LocalProfileOverlay>().SingleOrDefault()?.IsLoaded == true);
            AddStep("open page", () => overlay.Show());
            AddUntilStep("pink by default", () => pageColours.Hue == 333 && pageColours.Saturation == 1);

            // Colouring off: the picked colour is ignored.
            AddStep("pick green while off", () => manager.ProfileColour.Value = Colour4.FromHex(@"00FF00"));
            AddWaitStep("wait", 10);
            AddAssert("still pink", () => pageColours.Hue, () => Is.EqualTo(333));

            AddStep("turn colouring on", () => manager.ProfileColouring.Value = true);
            AddUntilStep("green page", () => pageColours.Hue == 120 && pageColours.Saturation == 1);

            AddStep("pick a half-saturated blue", () => manager.ProfileColour.Value = Colour4.FromHSV(240 / 360f, 0.5f, 0.3f));
            AddUntilStep("blue, half strength (brightness ignored)", () => pageColours.Hue == 240 && Precision(pageColours.Saturation, 0.5f));
            AddUntilStep("saved", () => manager.Store.Options.ProfileColour == Colour4.FromHSV(240 / 360f, 0.5f, 0.3f).ToHex()
                                        && manager.Store.Options.ProfileColouring);

            AddStep("switch to Sudden Death profile", () => manager.Switch(manager.CreateDefaultProfile(LocalDeathMode.SuddenDeath).Id));
            AddUntilStep("red", () => pageColours.Hue == 0 && pageColours.Saturation == 1);

            AddStep("switch to Perfect Death profile", () => manager.Switch(manager.CreateDefaultProfile(LocalDeathMode.PerfectDeath).Id));
            AddUntilStep("amber", () => pageColours.Hue == 30 && pageColours.Saturation == 1);

            AddStep("switch to normal profile", () => manager.Switch(manager.CreateDefaultProfile().Id));
            AddUntilStep("blue again", () => pageColours.Hue == 240);

            AddStep("close page, open settings", () =>
            {
                overlay.Hide();
                Game.Settings.Show();
            });
            AddUntilStep("picker shown", () => Game.Settings.ChildrenOfType<LocalProfileColourSettings>().SingleOrDefault()?
                                                   .ChildrenOfType<LocalCompactColourPicker>().SingleOrDefault() is LocalCompactColourPicker p && shown(p));
            AddStep("turn colouring off", () => manager.ProfileColouring.Value = false);
            AddAssert("picker hidden", () => Game.Settings.ChildrenOfType<LocalProfileColourSettings>().Single()
                                                      .ChildrenOfType<LocalCompactColourPicker>().Single() is LocalCompactColourPicker p && !shown(p));
            AddAssert("groups inside Patch 2, in order", () =>
            {
                var patch2 = Game.Settings.ChildrenOfType<LocalProfileBehaviourSettings>().Single();
                var groups = patch2.ChildrenOfType<LocalSettingsGroup>().ToList();
                return patch2.PatchNumber == 2 && groups.Count == 3 && groups[0] is LocalProfileColourSettings && groups[1] is LocalOnlineLookupSettings
                       && groups[2] is LocalBanchoApiKeySettings;
            });
        }

        private static bool shown(Drawable d)
        {
            for (; d != null; d = d.Parent!)
            {
                if (!d.IsPresent)
                    return false;
            }

            return true;
        }

        private static bool Precision(float a, float b) => System.Math.Abs(a - b) < 0.01f;
    }
}

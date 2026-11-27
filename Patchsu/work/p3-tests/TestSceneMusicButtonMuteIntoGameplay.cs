// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Overlays.Toolbar;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using osu.Game.Tests.Beatmaps.IO;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Navigation
{
    /// <summary>
    /// Patchsu patch 3: a music button mute is undone as soon as the player loader is pushed, so it doesn't warn about muted music.
    /// </summary>
    public partial class TestSceneMusicButtonMuteIntoGameplay : OsuGameTestScene
    {
        [Test]
        public void TestButtonMuteGivesNoWarning()
        {
            enterSongSelect();

            AddStep("middle click music button", () =>
            {
                InputManager.MoveMouseTo(Game.Toolbar.ChildrenOfType<ToolbarMusicButton>().Single());
                InputManager.Click(MouseButton.Middle);
            });
            AddAssert("music muted", () => Game.Audio.VolumeTrack.Value, () => Is.EqualTo(0));

            startGameplay();

            AddAssert("music restored", () => Game.Audio.VolumeTrack.Value, () => Is.GreaterThan(0));
            AddWaitStep("wait for warning check", 10);
            AddAssert("no muted warning", () => !mutedWarningPosted());
        }

        [Test]
        public void TestButtonDuckReleasedOnEnteringBeatmap()
        {
            enterSongSelect();
            AddStep("right click music button", () =>
            {
                InputManager.MoveMouseTo(Game.Toolbar.ChildrenOfType<ToolbarMusicButton>().Single());
                InputManager.Click(MouseButton.Right);
            });
            AddAssert("ducked", () => duckCount, () => Is.EqualTo(1));
            AddStep("hide toolbar", () => Game.Toolbar.Hide());
            AddWaitStep("wait", 5);
            AddAssert("still ducked outside gameplay", () => duckCount, () => Is.EqualTo(1));
            startGameplay();
            AddUntilStep("duck released", () => duckCount, () => Is.EqualTo(0));
        }

        private int duckCount => ((System.Collections.IList)typeof(osu.Game.Overlays.MusicController)
                                  .GetField("duckOperations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                                  .GetValue(Game.MusicController)!).Count;

        [Test]
        public void TestSettingsMuteStillWarns()
        {
            enterSongSelect();

            AddStep("set music volume to 0", () => Game.Audio.VolumeTrack.Value = 0);

            startGameplay();

            AddUntilStep("muted warning posted", mutedWarningPosted);
            AddStep("restore music volume", () => Game.Audio.VolumeTrack.Value = 1);
        }

        private void enterSongSelect()
        {
            SoloSongSelect songSelect = null!;

            PushAndConfirm(() => songSelect = new SoloSongSelect());
            AddUntilStep("wait for song select", () => songSelect.CarouselItemsPresented);
            AddStep("import beatmap", () => BeatmapImportHelper.LoadQuickOszIntoOsu(Game).WaitSafely());
            AddUntilStep("wait for selected", () => !Game.Beatmap.IsDefault);
        }

        private void startGameplay()
        {
            // OsuGameTestScene turns the warning off for every test.
            AddStep("allow muted warning", () => Game.Dependencies.Get<SessionStatics>().SetValue(Static.MutedAudioNotificationShownOnce, false));
            AddStep("press enter", () => InputManager.Key(Key.Enter));
            AddUntilStep("player loader entered", () => Game.ScreenStack.CurrentScreen is PlayerLoader loader && loader.IsLoaded);
        }

        private bool mutedWarningPosted() => Game.Dependencies.Get<SessionStatics>().Get<bool>(Static.MutedAudioNotificationShownOnce);
    }
}

// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections;
using System.Linq;
using System.Reflection;
using Moq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Graphics.Containers;
using osu.Game.LocalPriority;
using osu.Game.Overlays;
using osu.Game.Overlays.Toolbar;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Menus
{
    /// <summary>
    /// Patchsu patch 3: middle-click the toolbar music button mutes the music volume (muted icon), right-click ducks the music.
    /// </summary>
    public partial class TestSceneToolbarMusicButtonClicks : OsuManualInputManagerTestScene
    {
        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Cached]
        private readonly NowPlayingOverlay nowPlayingOverlay = new NowPlayingOverlay();

        [Cached]
        private readonly VolumeOverlay volumeOverlay = new VolumeOverlay();

        private readonly Mock<TestSceneToolbar.TestNotificationOverlay> notifications = new Mock<TestSceneToolbar.TestNotificationOverlay>();

        private Toolbar toolbar = null!;
        private ToolbarMusicButton button = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Dependencies.CacheAs<INotificationOverlay>(notifications.Object);
            notifications.SetupGet(n => n.UnreadCount).Returns(new osu.Framework.Bindables.BindableInt());
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("set music volume", () => audio.VolumeTrack.Value = 0.6);
            AddStep("don't swap clicks", () => LocalInterfaceSettings.GetSwapMusicButtonClicks().Value = false);
            AddStep("create toolbar", () =>
            {
                Remove(nowPlayingOverlay, false);
                Remove(volumeOverlay, false);
                Children = new Drawable[]
                {
                    nowPlayingOverlay,
                    volumeOverlay,
                    toolbar = new Toolbar { State = { Value = Visibility.Visible } },
                };
            });
            AddStep("find button", () => button = toolbar.ChildrenOfType<ToolbarMusicButton>().Single());
            AddStep("hover button", () => InputManager.MoveMouseTo(button));
        }

        private IconUsage shownIcon => button.ChildrenOfType<SpriteIcon>().First().Icon;

        private int duckCount => ((IList)typeof(MusicController).GetField("duckOperations", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(MusicController)!).Count;

        [Test]
        public void TestMiddleClickMutesMusicVolume()
        {
            AddStep("middle click", () => InputManager.Click(MouseButton.Middle));
            AddAssert("music volume 0", () => audio.VolumeTrack.Value, () => Is.EqualTo(0));
            AddAssert("muted icon", () => shownIcon, () => Is.EqualTo(FontAwesome.Solid.VolumeMute));
            AddStep("middle click", () => InputManager.Click(MouseButton.Middle));
            AddAssert("music volume restored", () => audio.VolumeTrack.Value, () => Is.EqualTo(0.6).Within(0.001));
            AddAssert("music icon", () => shownIcon, () => Is.EqualTo(nowPlayingOverlay.Icon));
            AddAssert("not ducked", () => duckCount, () => Is.EqualTo(0));
        }

        [Test]
        public void TestRightClickDucksMusic()
        {
            AddStep("right click", () => InputManager.Click(MouseButton.Right));
            AddAssert("ducked", () => duckCount, () => Is.EqualTo(1));
            AddAssert("music volume unchanged", () => audio.VolumeTrack.Value, () => Is.EqualTo(0.6).Within(0.001));
            AddStep("right click", () => InputManager.Click(MouseButton.Right));
            AddUntilStep("not ducked", () => duckCount, () => Is.EqualTo(0));
            AddStep("right click", () => InputManager.Click(MouseButton.Right));
            AddAssert("ducked", () => duckCount, () => Is.EqualTo(1));
            AddStep("dispose toolbar", () => Remove(toolbar, true));
            AddUntilStep("duck released", () => duckCount, () => Is.EqualTo(0));
        }

        [Test]
        public void TestToolbarHidingKeepsDuckAndMute()
        {
            AddStep("right click", () => InputManager.Click(MouseButton.Right));
            AddStep("hide toolbar", () => toolbar.Hide());
            AddWaitStep("wait", 5);
            AddAssert("still ducked", () => duckCount, () => Is.EqualTo(1));
            AddStep("show toolbar", () => toolbar.Show());
            AddStep("middle click", () => InputManager.Click(MouseButton.Middle));
            AddStep("hide toolbar", () => toolbar.Hide());
            AddAssert("still muted", () => audio.VolumeTrack.Value, () => Is.EqualTo(0));
            AddStep("show toolbar", () => toolbar.Show());
            AddStep("middle click", () => InputManager.Click(MouseButton.Middle));
        }

        [Test]
        public void TestSwappedClicks()
        {
            AddStep("swap clicks", () => LocalInterfaceSettings.GetSwapMusicButtonClicks().Value = true);
            AddStep("middle click", () => InputManager.Click(MouseButton.Middle));
            AddAssert("ducked", () => duckCount, () => Is.EqualTo(1));
            AddAssert("music volume unchanged", () => audio.VolumeTrack.Value, () => Is.EqualTo(0.6).Within(0.001));
            AddStep("right click", () => InputManager.Click(MouseButton.Right));
            AddAssert("music volume 0", () => audio.VolumeTrack.Value, () => Is.EqualTo(0));
            AddUntilStep("not ducked", () => duckCount, () => Is.EqualTo(0));
            AddStep("right click", () => InputManager.Click(MouseButton.Right));
            AddAssert("music volume restored", () => audio.VolumeTrack.Value, () => Is.EqualTo(0.6).Within(0.001));
            AddStep("don't swap clicks", () => LocalInterfaceSettings.GetSwapMusicButtonClicks().Value = false);
        }

        [Test]
        public void TestMuteReplacesDuck()
        {
            AddStep("right click", () => InputManager.Click(MouseButton.Right));
            AddAssert("ducked", () => duckCount, () => Is.EqualTo(1));
            AddStep("middle click", () => InputManager.Click(MouseButton.Middle));
            AddAssert("music volume 0", () => audio.VolumeTrack.Value, () => Is.EqualTo(0));
            AddUntilStep("not ducked", () => duckCount, () => Is.EqualTo(0));
            AddStep("middle click", () => InputManager.Click(MouseButton.Middle));
            AddAssert("music volume restored", () => audio.VolumeTrack.Value, () => Is.EqualTo(0.6).Within(0.001));
            AddAssert("still not ducked", () => duckCount, () => Is.EqualTo(0));
            AddUntilStep("icon not dimmed", () => button.ChildrenOfType<ConstrainedIconContainer>().First().Alpha, () => Is.EqualTo(1).Within(0.01f));
        }

        [Test]
        public void TestDuckReplacesMute()
        {
            AddStep("middle click", () => InputManager.Click(MouseButton.Middle));
            AddAssert("music volume 0", () => audio.VolumeTrack.Value, () => Is.EqualTo(0));
            AddStep("right click", () => InputManager.Click(MouseButton.Right));
            AddAssert("music volume restored", () => audio.VolumeTrack.Value, () => Is.EqualTo(0.6).Within(0.001));
            AddAssert("ducked", () => duckCount, () => Is.EqualTo(1));
            AddUntilStep("icon dimmed", () => button.ChildrenOfType<ConstrainedIconContainer>().First().Alpha, () => Is.EqualTo(0.35f).Within(0.01f));
            AddStep("right click", () => InputManager.Click(MouseButton.Right));
            AddUntilStep("not ducked", () => duckCount, () => Is.EqualTo(0));
            AddAssert("music volume unchanged", () => audio.VolumeTrack.Value, () => Is.EqualTo(0.6).Within(0.001));
        }
    }
}

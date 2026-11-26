// Patchsu 0002 local test (not part of the patch): Settings → Patch 2 → "Fetch top 200 plays of … from osu!".
// Copy into osu.Game.Tests/Visual/Navigation and run `dotnet test --filter TestSceneBanchoTopPlays`.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.LocalPriority;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.Navigation
{
    public partial class TestSceneBanchoTopPlays : OsuGameTestScene
    {
        private LocalProfileManager manager => Game.Dependencies.Get<LocalProfileManager>();

        private LocalProfile profile = null!;
        private BeatmapInfo owned = null!;
        private (string Hash, int OnlineID, int SetID) downloaded;
        private int scoreRequests;
        private int downloads;
        private int replayDownloads;
        private bool holdScores;
        private GetUserScoresRequest? heldScores;
        private readonly HashSet<string> seenNotifications = new HashSet<string>();

        /// <summary>
        /// An .osr of a play on the downloaded beatmap, set by the osu! player (as osu! would send it).
        /// </summary>
        private string createReplay(DateTimeOffset date)
        {
            var beatmap = Game.BeatmapManager.QueryBeatmap(b => b.MD5Hash == downloaded.Hash)!;
            var ruleset = new OsuRuleset();
            var working = Game.BeatmapManager.GetWorkingBeatmap(beatmap);
            var info = new ScoreInfo(beatmap, ruleset.RulesetInfo)
            {
                TotalScore = 400_000,
                Accuracy = 0.98,
                MaxCombo = 321,
                Rank = ScoreRank.A,
                Date = date,
                OnlineID = 400_000,
                User = new APIUser { Id = 1234, Username = "osu player" },
                Statistics = new Dictionary<HitResult, int> { [HitResult.Great] = 290, [HitResult.Ok] = 8, [HitResult.Miss] = 2 },
            };

            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"patchsu-test-{Guid.NewGuid()}.osr");

            using (var stream = System.IO.File.Create(path))
                new osu.Game.Scoring.Legacy.LegacyScoreEncoder(new Score { ScoreInfo = info, Replay = new osu.Game.Replays.Replay() }, working.GetPlayableBeatmap(ruleset.RulesetInfo)).Encode(stream);

            return path;
        }

        private void collectNotifications()
        {
            foreach (var n in Game.ChildrenOfType<NotificationOverlay>().Single().ChildrenOfType<osu.Game.Overlays.Notifications.Notification>())
            {
                if (n is not osu.Game.Overlays.Notifications.SimpleNotification)
                    seenNotifications.Add(n.GetType().Name);
            }
        }

        private SoloScoreInfo play(string hash, int beatmapId, int setId, long totalScore, DateTimeOffset date, bool hasReplay = false) => new SoloScoreInfo
        {
            ID = (ulong)totalScore,
            BeatmapID = beatmapId,
            RulesetID = 0,
            Passed = true,
            TotalScore = totalScore,
            Accuracy = 0.98,
            MaxCombo = 321,
            Rank = ScoreRank.A,
            EndedAt = date,
            Mods = new[] { new APIMod { Acronym = "HD" }, new APIMod { Acronym = "CL" } },
            Statistics = new Dictionary<HitResult, int> { [HitResult.Great] = 290, [HitResult.Ok] = 8, [HitResult.Miss] = 2 },
            MaximumStatistics = new Dictionary<HitResult, int> { [HitResult.Great] = 300 },
            HasReplay = hasReplay,
            Beatmap = new APIBeatmap
            {
                OnlineID = beatmapId,
                Checksum = hash,
                BeatmapSet = new APIBeatmapSet { OnlineID = setId, Title = "test", Artist = "test" },
            },
        };

        private int profileScores() => Game.Realm.Run(r => r.All<ScoreInfo>().AsEnumerable().Count(s => s.UserID == profile.UserId && !s.DeletePending));

        private PopupDialog? dialog() => Game.ChildrenOfType<DialogOverlay>().SingleOrDefault()?.CurrentDialog;

        [SetUpSteps]
        public override void SetUpSteps()
        {
            base.SetUpSteps();

            AddStep("create profile", () =>
            {
                profile = manager.CreateDefaultProfile();
                manager.Switch(profile.Id);
                ((DummyAPIAccess)API).LocalUser.Value = profile.ToUser();
            });

            AddStep("import a generated beatmap (owned)", () =>
            {
                var set = TestResources.CreateTestBeatmapSetInfo(1, new[] { new OsuRuleset().RulesetInfo });
                owned = Game.BeatmapManager.Import(set)!.Value.Beatmaps.First().Detach();
            });

            AddStep("import + delete the quick test beatmap (to be downloaded)", () =>
            {
                var live = Game.BeatmapManager.Import(new ImportTask(TestResources.GetQuickTestBeatmapForImport())).GetResultSafely()!;
                var set = live.Value.Detach();
                var beatmap = set.Beatmaps.First(b => b.Ruleset.OnlineID == 0);
                downloaded = (beatmap.MD5Hash, beatmap.OnlineID, set.OnlineID);
                Game.BeatmapManager.Delete(live.Value);
            });

            AddStep("answer osu! requests", () =>
            {
                scoreRequests = 0;
                downloads = 0;
                replayDownloads = 0;
                var date = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);

                ((DummyAPIAccess)API).HandleRequest = request =>
                {
                    switch (request)
                    {
                        case GetUserRequest user:
                            user.TriggerSuccess(new APIUser { Id = 1234, Username = profile.Name });
                            return true;

                        case GetUserScoresRequest scores:
                            if (holdScores)
                            {
                                heldScores = scores;
                                return true;
                            }

                            // Only the first page has plays (fewer than 100, so no second page is asked for).
                            scoreRequests++;
                            scores.TriggerSuccess(new List<SoloScoreInfo>
                            {
                                play(owned.MD5Hash, owned.OnlineID, owned.BeatmapSet!.OnlineID, 500_000, date),
                                play(downloaded.Hash, downloaded.OnlineID, downloaded.SetID, 400_000, date.AddDays(1), hasReplay: true),
                            });
                            return true;

                        case DownloadReplayRequest replay:
                            replayDownloads++;
                            typeof(APIDownloadRequest).GetMethod("TriggerSuccess", BindingFlags.Instance | BindingFlags.NonPublic, new[] { typeof(string) })!
                                                      .Invoke(replay, new object[] { createReplay(date.AddDays(1)) });
                            return true;

                        case DownloadBeatmapSetRequest download:
                            downloads++;
                            typeof(APIDownloadRequest).GetMethod("TriggerSuccess", BindingFlags.Instance | BindingFlags.NonPublic, new[] { typeof(string) })!
                                                      .Invoke(download, new object[] { TestResources.GetQuickTestBeatmapForImport() });
                            return true;
                    }

                    return false;
                };
            });
        }

        [Test]
        public void TestFetchDownloadAndNoDuplicates()
        {
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            AddUntilStep("download asked", () => dialog() != null);
            AddAssert("owned play already added", profileScores, () => Is.EqualTo(1));
            AddStep("clear seen notifications", () => seenNotifications.Clear());
            AddStep("yes, download", () => dialog()!.ChildrenOfType<PopupDialogOkButton>().Single().TriggerClick());
            AddUntilStep("both plays added", () =>
            {
                collectNotifications();
                return profileScores();
            }, () => Is.EqualTo(2));
            AddAssert("one notification only (no per-beatmap download / import ones)", () => seenNotifications,
                () => Is.EquivalentTo(new[] { "BanchoFetchNotification", "ProgressCompletionNotification" }).Or.EquivalentTo(new[] { "BanchoFetchNotification" }));
            AddUntilStep("says how many were downloaded", () => Game.ChildrenOfType<osu.Game.Overlays.Notifications.ProgressNotification>()
                                                                 .Any(n => n.CompletionText.ToString().StartsWith("Added 2 of 2", StringComparison.Ordinal)));
            AddAssert("one replay downloaded", () => replayDownloads, () => Is.EqualTo(1));
            AddAssert("completion counts the replay", () => Game.ChildrenOfType<osu.Game.Overlays.Notifications.ProgressNotification>()
                                                              .Any(n => n.CompletionText.ToString().Contains("1 with their replay") && !n.CompletionText.ToString().Contains("skipped")));
            AddAssert("downloaded play now has its replay, on the profile", () => Game.Realm.Run(r =>
            {
                var s = r.All<ScoreInfo>().AsEnumerable().Single(x => x.UserID == profile.UserId && !x.DeletePending && x.BeatmapInfo!.MD5Hash == downloaded.Hash);
                return s.Files.Count > 0 && s.User.Username == profile.Name;
            }));
            AddAssert("plays link to their beatmaps", () => Game.Realm.Run(r =>
                r.All<ScoreInfo>().AsEnumerable().Where(x => x.UserID == profile.UserId && !x.DeletePending).All(x => x.BeatmapHash == x.BeatmapInfo!.Hash && x.BeatmapHash.Length > 0)));
            AddStep("count unranked test maps", () => manager.CountEverythingAsRanked.Value = true);
            AddAssert("downloaded play gives PP on the profile", () => LocalProfileStatistics.CalculatePerformanceAsync(manager.Store.ActiveProfile!,
                new OsuRuleset().RulesetInfo, Game.Realm, Game.BeatmapManager, manager.Store, System.Threading.CancellationToken.None).GetResultSafely(), () => Is.GreaterThan(0));
            AddAssert("one download", () => downloads, () => Is.EqualTo(1));
            AddAssert("one page asked for", () => scoreRequests, () => Is.EqualTo(1));
            AddAssert("only the play without a replay is marked", () => manager.Store.GetProfile(profile.Id)!.BanchoScores.Count, () => Is.EqualTo(1));
            AddAssert("both plays count (play count, hits, play time)", () =>
            {
                var plays = fullStatistics().Plays;
                return (plays.PlayCount, plays.Count(HitResult.Great), plays.PlayTimeSeconds > 0);
            }, () => Is.EqualTo((2L, 580L, true)));

            AddAssert("play kept its details", () => Game.Realm.Run(r =>
            {
                var s = r.All<ScoreInfo>().AsEnumerable().Single(x => x.UserID == profile.UserId && !x.DeletePending && x.TotalScore == 500_000);
                return s.BeatmapInfo!.ID == owned.ID
                       && s.OnlineID == -1 && !s.HasOnlineReplay && s.Files.Count == 0
                       && s.Accuracy == 0.98 && s.MaxCombo == 321 && s.Rank == ScoreRank.A
                       && s.Date == new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero)
                       && s.User.Username == profile.Name
                       && string.Join(",", s.APIMods.Select(m => m.Acronym).OrderBy(a => a)) == "CL,HD"
                       && s.Statistics[HitResult.Ok] == 8 && s.MaximumStatistics[HitResult.Great] == 300;
            }));

            AddAssert("downloaded play on the re-imported beatmap", () => Game.Realm.Run(r =>
                r.All<ScoreInfo>().AsEnumerable().Single(x => x.UserID == profile.UserId && !x.DeletePending && x.TotalScore == 400_000).BeatmapInfo!.MD5Hash == downloaded.Hash));

            AddUntilStep("first fetch finished", () => !Game.ChildrenOfType<NotificationOverlay>().Single().ChildrenOfType<osu.Game.Overlays.Notifications.ProgressNotification>()
                                                            .Any(n => n.State == osu.Game.Overlays.Notifications.ProgressNotificationState.Active));

            AddStep("fetch again", () => manager.FetchBanchoTopPlays(profile.Id));
            AddUntilStep("second page asked", () => scoreRequests, () => Is.EqualTo(2));
            AddWaitStep("let it finish", 10);
            AddAssert("no download asked", () => dialog(), () => Is.Null);
            AddAssert("no duplicates", profileScores, () => Is.EqualTo(2));
            AddAssert("replay not downloaded again", () => replayDownloads, () => Is.EqualTo(1));
            AddAssert("still counted once each", () => fullStatistics().Plays.PlayCount, () => Is.EqualTo(2));
        }

        private LocalProfileStatistics fullStatistics() => LocalProfileStatistics.CalculateAsync(manager.Store.GetProfile(profile.Id)!, new OsuRuleset().RulesetInfo,
            Game.Realm, Game.BeatmapManager, manager.Store, System.Threading.CancellationToken.None).GetResultSafely();

        [Test]
        public void TestOlderFetchedPlaysCountedOnce()
        {
            Guid fetched = Guid.Empty;

            AddStep("add a play fetched by an older build (no replay, no online ID)", () => Game.Realm.Write(r =>
            {
                var beatmap = r.Find<BeatmapInfo>(owned.ID)!;
                var score = new ScoreInfo(beatmap, r.Find<osu.Game.Rulesets.RulesetInfo>("osu")!, new osu.Game.Models.RealmUser { OnlineID = profile.UserId, Username = profile.Name })
                {
                    TotalScore = 1,
                    OnlineID = -1,
                    BeatmapHash = beatmap.Hash,
                    Statistics = new Dictionary<HitResult, int> { [HitResult.Great] = 10 },
                };
                score.StatisticsJson = Newtonsoft.Json.JsonConvert.SerializeObject(score.Statistics);
                fetched = score.ID;
                r.Add(score);
            }));
            AddAssert("not counted before", () => fullStatistics().Plays.PlayCount, () => Is.EqualTo(0));
            AddStep("forget the migration, run it (runs on load)", () =>
            {
                var store = manager.Store;
                var data = typeof(LocalProfileStore).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
                data.GetType().GetProperty("Version")!.SetValue(data, 1);
                typeof(LocalProfileManager).GetMethod("migrateBanchoScores", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null);
            });
            AddAssert("marked", () => manager.Store.GetProfile(profile.Id)!.BanchoScores, () => Is.EquivalentTo(new[] { fetched }));
            AddAssert("counted", () => (fullStatistics().Plays.PlayCount, fullStatistics().Plays.Count(HitResult.Great)), () => Is.EqualTo((1L, 10L)));
            AddStep("run it again", () => typeof(LocalProfileManager).GetMethod("migrateBanchoScores", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null));
            AddAssert("still once", () => fullStatistics().Plays.PlayCount, () => Is.EqualTo(1));
        }

        [Test]
        public void TestSwipeAwayKeepsRunning()
        {
            osu.Game.Overlays.Notifications.ProgressNotification fetchNotification() =>
                Game.ChildrenOfType<osu.Game.Overlays.Notifications.ProgressNotification>().Single(n => n.GetType().Name == "BanchoFetchNotification");

            AddStep("hold osu!'s answer", () => holdScores = true);
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            AddUntilStep("notification in the toast tray", () => Game.ChildrenOfType<osu.Game.Overlays.Notifications.ProgressNotification>()
                                                                     .Any(n => n.GetType().Name == "BanchoFetchNotification" && n.IsInToastTray));
            AddStep("swipe the notification away", () => fetchNotification().Close(true));
            AddAssert("still running", () => Game.ChildrenOfType<osu.Game.Overlays.Notifications.ProgressNotification>()
                                                .Single(n => n.GetType().Name == "BanchoFetchNotification").State,
                () => Is.EqualTo(osu.Game.Overlays.Notifications.ProgressNotificationState.Active));
            AddUntilStep("moved out of the toast tray", () => !fetchNotification().IsInToastTray);
            AddStep("let osu! answer", () =>
            {
                holdScores = false;
                var held = heldScores!;
                heldScores = null;
                ((DummyAPIAccess)API).HandleRequest!(held);
            });
            AddUntilStep("download asked", () => dialog() != null);
            AddStep("yes, download", () => dialog()!.ChildrenOfType<PopupDialogOkButton>().Single().TriggerClick());
            AddUntilStep("both plays added", profileScores, () => Is.EqualTo(2));
        }

        [Test]
        public void TestSkipDownload()
        {
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            AddUntilStep("download asked", () => dialog() != null);
            AddStep("no, skip", () => dialog()!.ChildrenOfType<PopupDialogCancelButton>().Single().TriggerClick());
            AddWaitStep("wait", 10);
            AddAssert("only the owned play", profileScores, () => Is.EqualTo(1));
            AddAssert("nothing downloaded", () => downloads, () => Is.EqualTo(0));

            // A second fetch works after the first one ended by skipping.
            AddStep("fetch again", () => manager.FetchBanchoTopPlays(profile.Id));
            AddUntilStep("download asked again", () => dialog() != null);
        }

        private SettingsOverlay settings => Game.Settings;

        private SettingsButtonV2? fetchButton() => settings.ChildrenOfType<SettingsButtonV2>().FirstOrDefault(b => b.Text.ToString().StartsWith("Fetch top", StringComparison.Ordinal));

        [Test]
        public void TestButtonLeadsToOnlineLookupsWhileOff()
        {
            AddStep("online lookups off", () => manager.LocalProfilesUseOnlineAccount.Value = false);
            AddStep("open settings", () => settings.Show());
            AddUntilStep("button shown", () => fetchButton()?.IsPresent == true);
            AddAssert("plain text", () => fetchButton()!.Text.ToString(), () => Does.EndWith("from osu!…"));
            AddAssert("clickable", () => fetchButton()!.Enabled.Value);
            AddUntilStep("greyed out", () => fetchButton()!.Colour.TopLeft.Linear.R, () => Is.LessThan(0.6f));
            AddStep("click", () => fetchButton()!.TriggerClick());
            AddUntilStep("scrolled to online lookups", () =>
            {
                var group = settings.ChildrenOfType<LocalOnlineLookupSettings>().Single();
                var panel = settings.SectionsContainer.ScreenSpaceDrawQuad;
                float top = group.ScreenSpaceDrawQuad.TopLeft.Y;
                return top >= panel.TopLeft.Y && top < panel.BottomLeft.Y - 50;
            });
            AddWaitStep("wait", 5);
            AddAssert("nothing fetched", () => scoreRequests, () => Is.EqualTo(0));

            AddStep("online lookups on", () => manager.LocalProfilesUseOnlineAccount.Value = true);
            AddUntilStep("not greyed out", () => fetchButton()!.Colour.TopLeft.Linear.R, () => Is.EqualTo(1f).Within(0.01f));
            AddStep("click", () => fetchButton()!.TriggerClick());
            AddUntilStep("fetched", () => scoreRequests, () => Is.EqualTo(1));
        }

        [Test]
        public void TestRepairsPlaysWithoutBeatmapHash()
        {
            Guid id = Guid.Empty;

            AddStep("add a play saved without its beatmap hash", () => Game.Realm.Write(r =>
            {
                var beatmap = r.Find<BeatmapInfo>(owned.ID)!;
                var score = new ScoreInfo(beatmap, r.Find<osu.Game.Rulesets.RulesetInfo>("osu")!, new osu.Game.Models.RealmUser { OnlineID = profile.UserId, Username = profile.Name })
                {
                    TotalScore = 1,
                    BeatmapHash = string.Empty,
                };
                id = score.ID;
                r.Add(score);
            }));
            AddStep("repair (runs on load)", () => typeof(LocalProfileManager).GetMethod("repairScoresWithoutBeatmapHash", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null));
            AddAssert("hash given back", () => Game.Realm.Run(r => r.Find<ScoreInfo>(id)!.BeatmapHash), () => Is.EqualTo(owned.Hash));
        }

        [Test]
        public void TestNeedsOnlineAccount()
        {
            AddStep("go offline", () => ((DummyAPIAccess)API).SetState(APIState.Offline));
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            AddWaitStep("wait", 5);
            AddAssert("nothing asked", () => scoreRequests, () => Is.EqualTo(0));
            AddAssert("no plays", profileScores, () => Is.EqualTo(0));
            AddStep("back online", () => ((DummyAPIAccess)API).SetState(APIState.Online));
        }
    }
}

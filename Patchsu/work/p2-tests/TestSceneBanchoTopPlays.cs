// Patchsu 0002 local test (not part of the patch): Settings → Patch 2 → "Fetch top 200 plays of … from osu!".
// Copy into osu.Game.Tests/Visual/Navigation and run `dotnet test --filter TestSceneBanchoTopPlays`.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly List<long> requestTimes = new List<long>();
        private bool holdScores;
        private GetUserScoresRequest? heldScores;
        private bool holdUser;
        private GetUserRequest? heldUser;
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
            PP = 500,
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

                // The signed-in account is the player named like the profile (their own plays go through it).
                LocalPriorityPolicy.OnlineAccountUser = new APIUser { Id = 1234, Username = profile.Name };
                manager.BanchoApiClientId.Value = string.Empty;
                manager.BanchoApiClientSecret.Value = string.Empty;
                manager.ApiKeySourceOverride = null;
                apiKeySource = null;
                manager.ResetBanchoFetchCooldown();
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
                userLookups = 0;
                holdScores = holdUser = false;
                heldScores = null;
                heldUser = null;
                downloads = 0;
                replayDownloads = 0;
                var date = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);

                lock (requestTimes)
                    requestTimes.Clear();

                ((DummyAPIAccess)API).HandleRequest = request =>
                {
                    if (request is GetUserRequest or GetUserScoresRequest or DownloadReplayRequest or DownloadBeatmapSetRequest)
                    {
                        lock (requestTimes)
                            requestTimes.Add(Environment.TickCount64);
                    }

                    switch (request)
                    {
                        case GetUserRequest user:
                            if (holdUser)
                            {
                                heldUser = user;
                                return true;
                            }

                            Interlocked.Increment(ref userLookups);
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
            confirmFetch();
            AddUntilStep("download asked", () => dialog()?.GetType().Name == "BanchoDownloadDialog");
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
            AddAssert("osu!'s PP isn't kept (calculated here)", () => Game.Realm.Run(r =>
                r.All<ScoreInfo>().AsEnumerable().Where(x => x.UserID == profile.UserId && !x.DeletePending).All(x => x.PP == null)));
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

            AddStep("wait is over", () => manager.ResetBanchoFetchCooldown());
            AddStep("fetch again", () => manager.FetchBanchoTopPlays(profile.Id));
            confirmFetch();
            AddUntilStep("second page asked", () => scoreRequests, () => Is.EqualTo(2));
            AddWaitStep("let it finish", 10);
            AddAssert("no download asked", () => dialog(), () => Is.Null);
            AddAssert("no duplicates", profileScores, () => Is.EqualTo(2));
            AddAssert("replay not downloaded again", () => replayDownloads, () => Is.EqualTo(1));
            AddAssert("still counted once each", () => fullStatistics().Plays.PlayCount, () => Is.EqualTo(2));
        }

        private LocalProfileStatistics fullStatistics() => LocalProfileStatistics.CalculateAsync(manager.Store.GetProfile(profile.Id)!, new OsuRuleset().RulesetInfo,
            Game.Realm, Game.BeatmapManager, manager.Store, System.Threading.CancellationToken.None).GetResultSafely();

        private void confirmFetch()
        {
            AddUntilStep("fetch confirmation asked", () => dialog()?.GetType().Name == "BanchoConfirmDialog");
            AddStep("yes, fetch", () => dialog()!.ChildrenOfType<PopupDialogOkButton>().Single().TriggerClick());
        }

        private int userLookups;

        [Test]
        public void TestDeclineSendsNothingElse()
        {
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            AddUntilStep("fetch confirmation asked", () => dialog()?.GetType().Name == "BanchoConfirmDialog");
            AddAssert("player looked up", () => userLookups, () => Is.EqualTo(1));
            AddStep("no", () => dialog()!.ChildrenOfType<PopupDialogCancelButton>().Single().TriggerClick());
            AddWaitStep("wait", 10);
            AddAssert("no plays asked for", () => scoreRequests, () => Is.EqualTo(0));
            AddAssert("no plays", profileScores, () => Is.EqualTo(0));
            AddAssert("no wait started", () => manager.BanchoFetchCooldownLeft, () => Is.EqualTo(TimeSpan.Zero));

            AddStep("fetch again", () => manager.FetchBanchoTopPlays(profile.Id));
            AddUntilStep("asked again", () => dialog()?.GetType().Name == "BanchoConfirmDialog");
            AddStep("dismiss (escape)", () => dialog()!.Hide());
            AddWaitStep("wait", 10);
            AddAssert("dismissing says no", () => scoreRequests, () => Is.EqualTo(0));
        }

        [Test]
        public void TestWaitStartsWhenDownloadsFinish()
        {
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            confirmFetch();
            AddUntilStep("download asked", () => dialog()?.GetType().Name == "BanchoDownloadDialog");
            AddAssert("no wait while it runs", () => manager.BanchoFetchCooldownLeft, () => Is.EqualTo(TimeSpan.Zero));
            AddStep("yes, download", () => dialog()!.ChildrenOfType<PopupDialogOkButton>().Single().TriggerClick());
            AddUntilStep("replay downloaded", () => replayDownloads, () => Is.EqualTo(1));
            AddUntilStep("wait started when it finished", () => manager.BanchoFetchCooldownLeft.TotalMinutes, () => Is.GreaterThan(9.9));
            AddAssert("kept in PatchsuSettings.json", () => PatchsuSettings.Current.GetBindable("LocalProfiles", "TopPlaysFetchEndedAt", string.Empty).Value, () => Is.Not.Empty);

            AddStep("count requests from here", () => userLookups = 0);
            AddStep("fetch again", () => manager.FetchBanchoTopPlays(profile.Id));
            AddUntilStep("says when it can fetch again", () => Game.ChildrenOfType<osu.Game.Overlays.Notifications.SimpleNotification>()
                                                                   .Any(n => n.Text.ToString().Contains("once every 10 minutes")
                                                                                     && System.Text.RegularExpressions.Regex.IsMatch(n.Text.ToString(), @"Try again in (10 minutes|9 minutes \d+ seconds?)\.")));
            AddWaitStep("wait", 5);
            AddAssert("nothing sent", () => userLookups, () => Is.EqualTo(0));
            AddAssert("no confirmation", () => dialog(), () => Is.Null);

            AddStep("wait is over", () => manager.ResetBanchoFetchCooldown());
            AddStep("fetch again", () => manager.FetchBanchoTopPlays(profile.Id));
            AddUntilStep("asked again", () => dialog()?.GetType().Name == "BanchoConfirmDialog");
        }

        [Test]
        public void TestButtonGreyedOutWhileWaiting()
        {
            AddStep("online lookups on", () => manager.LocalProfilesUseOnlineAccount.Value = true);
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            confirmFetch();
            AddUntilStep("download asked", () => dialog()?.GetType().Name == "BanchoDownloadDialog");
            AddStep("no, skip", () => dialog()!.ChildrenOfType<PopupDialogCancelButton>().Single().TriggerClick());
            AddUntilStep("waiting", () => manager.BanchoFetchCoolingDown.Value);

            AddStep("open settings", () => settings.Show());
            AddUntilStep("button shown", () => fetchButton()?.IsPresent == true);
            AddUntilStep("greyed out", () => fetchButton()!.Colour.TopLeft.Linear.R, () => Is.LessThan(0.6f));
            AddAssert("still clickable", () => fetchButton()!.Enabled.Value);
            AddStep("count requests from here", () => userLookups = 0);
            AddStep("click", () => fetchButton()!.TriggerClick());
            AddUntilStep("says the time left", () => Game.ChildrenOfType<osu.Game.Overlays.Notifications.SimpleNotification>()
                                                          .Any(n => n.Text.ToString().Contains("Try again in")));
            AddWaitStep("wait", 5);
            AddAssert("nothing sent", () => userLookups, () => Is.EqualTo(0));

            AddStep("wait is over", () => manager.ResetBanchoFetchCooldown());
            AddUntilStep("not waiting", () => !manager.BanchoFetchCoolingDown.Value);
            // Settings items off screen don't animate, so look at the button before checking its colour.
            AddStep("show the button", () =>
            {
                settings.Show();
                settings.SectionsContainer.ScrollTo(fetchButton()!);
            });
            AddUntilStep("not greyed out", () => fetchButton()!.Colour.TopLeft.Linear.R, () => Is.EqualTo(1f).Within(0.01f));
        }

        [TearDown]
        public void ForgetAccount() => LocalPriorityPolicy.OnlineAccountUser = null;

        private FakeApiKeySource? apiKeySource;

        /// <summary>
        /// Stands in for the osu! API reached with the user's own API key.
        /// </summary>
        private class FakeApiKeySource : IBanchoPlaySource
        {
            public int Users, Pages, Replays;
            public Func<List<SoloScoreInfo>> Plays = null!;
            public Func<string> Replay = null!;

            public Task<APIUser?> GetUser(string name, osu.Game.Rulesets.RulesetInfo ruleset, CancellationToken token)
            {
                Interlocked.Increment(ref Users);
                return Task.FromResult<APIUser?>(new APIUser { Id = 5678, Username = name });
            }

            public Task<List<SoloScoreInfo>?> GetBestPlays(int userId, osu.Game.Rulesets.RulesetInfo ruleset, int offset, int limit, CancellationToken token)
            {
                Interlocked.Increment(ref Pages);
                return Task.FromResult<List<SoloScoreInfo>?>(Plays());
            }

            public Task<string?> DownloadReplay(SoloScoreInfo play, CancellationToken token)
            {
                Interlocked.Increment(ref Replays);
                return Task.FromResult<string?>(Replay());
            }
        }

        private void useApiKey()
        {
            AddStep("signed in as someone else, API key set", () =>
            {
                LocalPriorityPolicy.OnlineAccountUser = new APIUser { Id = 999, Username = "someone else" };
                var date = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);
                manager.ApiKeySourceOverride = apiKeySource = new FakeApiKeySource
                {
                    Plays = () => new List<SoloScoreInfo>
                    {
                        play(owned.MD5Hash, owned.OnlineID, owned.BeatmapSet!.OnlineID, 500_000, date),
                        play(downloaded.Hash, downloaded.OnlineID, downloaded.SetID, 400_000, date.AddDays(1), hasReplay: true),
                    },
                    Replay = () => createReplay(date.AddDays(1)),
                };
                manager.BanchoApiClientId.Value = "12345";
                manager.BanchoApiClientSecret.Value = "secret";
            });
        }

        [Test]
        public void TestOtherPlayerNeedsApiKey()
        {
            AddStep("signed in as someone else", () => LocalPriorityPolicy.OnlineAccountUser = new APIUser { Id = 999, Username = "someone else" });
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            AddUntilStep("says an API key is needed", () => Game.ChildrenOfType<osu.Game.Overlays.Notifications.SimpleNotification>()
                                                                .Any(n => n.Text.ToString().Contains("isn't your signed-in osu! account")));
            AddWaitStep("wait", 5);
            AddAssert("account asked nothing", () => scoreRequests, () => Is.EqualTo(0));
            AddAssert("no plays", profileScores, () => Is.EqualTo(0));
        }

        [Test]
        public void TestOtherPlayerUsesApiKey()
        {
            useApiKey();
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            confirmFetch();
            AddUntilStep("download asked", () => dialog()?.GetType().Name == "BanchoDownloadDialog");
            AddStep("yes, download", () => dialog()!.ChildrenOfType<PopupDialogOkButton>().Single().TriggerClick());
            AddUntilStep("both plays added", profileScores, () => Is.EqualTo(2));
            AddUntilStep("replay through the API key", () => apiKeySource!.Replays, () => Is.EqualTo(1));
            AddAssert("player and plays through the API key", () => (apiKeySource!.Users, apiKeySource.Pages), () => Is.EqualTo((1, 1)));
            AddAssert("account asked for no plays or replays", () => (scoreRequests, replayDownloads), () => Is.EqualTo((0, 0)));
            AddAssert("beatmap through the account", () => downloads, () => Is.EqualTo(1));
            AddUntilStep("downloaded play has its replay", () => Game.Realm.Run(r =>
                r.All<ScoreInfo>().AsEnumerable().Single(x => x.UserID == profile.UserId && !x.DeletePending && x.BeatmapInfo!.MD5Hash == downloaded.Hash).Files.Count > 0));
        }

        [Test]
        public void TestApiKeyWithoutSignInSkipsBeatmaps()
        {
            useApiKey();
            AddStep("go offline", () => ((DummyAPIAccess)API).SetState(APIState.Offline));
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            confirmFetch();
            AddUntilStep("says beatmaps need signing in", () => Game.ChildrenOfType<osu.Game.Overlays.Notifications.ProgressNotification>()
                                                                   .Any(n => n.CompletionText.ToString().Contains("1 skipped (sign in to download their beatmaps)")));
            AddAssert("no download asked", () => dialog(), () => Is.Null);
            AddAssert("owned play added", profileScores, () => Is.EqualTo(1));
            AddAssert("nothing downloaded", () => downloads, () => Is.EqualTo(0));
            AddStep("back online", () => ((DummyAPIAccess)API).SetState(APIState.Online));
        }

        [Test]
        public void TestRequestsAreRateLimited()
        {
            AddStep("fetch", () =>
            {
                lock (requestTimes)
                    requestTimes.Clear();

                manager.FetchBanchoTopPlays(profile.Id);
            });
            confirmFetch();
            AddUntilStep("download asked", () => dialog()?.GetType().Name == "BanchoDownloadDialog");
            AddStep("yes, download", () => dialog()!.ChildrenOfType<PopupDialogOkButton>().Single().TriggerClick());
            AddUntilStep("replay downloaded", () => replayDownloads, () => Is.EqualTo(1));
            // The replay's import then looks its player up once (ScoreImporter, cached per player): that one isn't the fetch's request.
            AddAssert("lookup, page, beatmap and replay asked for", () =>
            {
                lock (requestTimes)
                    return requestTimes.Count;
            }, () => Is.GreaterThanOrEqualTo(4));
            AddAssert("at most one request a second (osu! API terms)", () =>
            {
                lock (requestTimes)
                {
                    var fetch = requestTimes.Take(4).ToList();
                    return fetch.Zip(fetch.Skip(1), (a, b) => b - a).Min();
                }
            }, () => Is.GreaterThanOrEqualTo(950));
        }

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

            AddStep("hold osu!'s answer", () => holdUser = true);
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
                holdUser = false;
                var held = heldUser!;
                heldUser = null;
                ((DummyAPIAccess)API).HandleRequest!(held);
            });
            confirmFetch();
            AddUntilStep("download asked", () => dialog()?.GetType().Name == "BanchoDownloadDialog");
            AddStep("yes, download", () => dialog()!.ChildrenOfType<PopupDialogOkButton>().Single().TriggerClick());
            AddUntilStep("both plays added", profileScores, () => Is.EqualTo(2));
        }

        [Test]
        public void TestSkipDownload()
        {
            AddStep("fetch", () => manager.FetchBanchoTopPlays(profile.Id));
            confirmFetch();
            AddUntilStep("download asked", () => dialog()?.GetType().Name == "BanchoDownloadDialog");
            AddStep("no, skip", () => dialog()!.ChildrenOfType<PopupDialogCancelButton>().Single().TriggerClick());
            AddWaitStep("wait", 10);
            AddAssert("only the owned play", profileScores, () => Is.EqualTo(1));
            AddAssert("nothing downloaded", () => downloads, () => Is.EqualTo(0));

            // A second fetch works after the first one ended by skipping.
            AddStep("wait is over", () => manager.ResetBanchoFetchCooldown());
            AddStep("fetch again", () => manager.FetchBanchoTopPlays(profile.Id));
            confirmFetch();
            AddUntilStep("download asked again", () => dialog()?.GetType().Name == "BanchoDownloadDialog");
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
            confirmFetch();
            AddUntilStep("fetched", () => scoreRequests, () => Is.EqualTo(1));
        }

        [Test]
        public void TestOlderFetchedPlaysLoseBanchoPP()
        {
            Guid fetched = Guid.Empty;

            AddStep("add a play fetched by an older build (osu!'s PP, no replay)", () => Game.Realm.Write(r =>
            {
                var beatmap = r.Find<BeatmapInfo>(owned.ID)!;
                var score = new ScoreInfo(beatmap, r.Find<osu.Game.Rulesets.RulesetInfo>("osu")!, new osu.Game.Models.RealmUser { OnlineID = profile.UserId, Username = profile.Name })
                {
                    TotalScore = 1,
                    OnlineID = -1,
                    PP = 500,
                    BeatmapHash = beatmap.Hash,
                };
                fetched = score.ID;
                r.Add(score);
            }));
            AddStep("forget the migration, run it (runs on load)", () =>
            {
                var store = manager.Store;
                var data = typeof(LocalProfileStore).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
                data.GetType().GetProperty("Version")!.SetValue(data, 2);
                typeof(LocalProfileManager).GetMethod("clearBanchoScoresPP", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null);
            });
            AddAssert("PP cleared", () => Game.Realm.Run(r => r.Find<ScoreInfo>(fetched)!.PP), () => Is.Null);
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

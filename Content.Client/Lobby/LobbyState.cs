using Content.Client.Audio;
using Content.Client.Administration.Managers;
using System.Linq;
using Content.Client.Eclipse.Economy;
using Content.Client.GameTicking.Managers;
using Content.Client.LateJoin;
using Content.Client.Lobby.UI;
using Content.Client.Message;
using Content.Client.Players.PlayTimeTracking;
using Content.Client.Playtime;
using Content.Client.Voting;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Eclipse.Achievements;
using Content.Shared.Eclipse.Progression;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client;
using Robust.Client.Console;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Lobby
{
    public sealed class LobbyState : Robust.Client.State.State
    {
        [Dependency] private readonly IBaseClient _baseClient = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IClientConsoleHost _consoleHost = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IVoteManager _voteManager = default!;
        [Dependency] private readonly ClientsidePlaytimeTrackingManager _playtimeTracking = default!;
        [Dependency] private readonly IClientPreferencesManager _preferencesManager = default!;
        [Dependency] private readonly JobRequirementsManager _jobRequirements = default!;
        [Dependency] private readonly IPrototypeManager _protoMan = default!;
        [Dependency] private readonly IClientAdminManager _adminManager = default!;
        [Dependency] private readonly IRobustRandom _random = default!;

        private ClientGameTicker _gameTicker = default!;
        private ContentAudioSystem _contentAudioSystem = default!;
        private EclipseCurrencyClientSystem _currency = default!;
        private float _accountRefreshTimer;

        // Lobby wallpaper slideshow. The folder is read once, shuffled, then cycled in order so every
        // wallpaper is shown before any repeats (a plain random pick can show the same one twice in a row).
        private ResPath[] _lobbyBackgrounds = Array.Empty<ResPath>();
        private int _lobbyBackgroundIndex;
        private float _lobbyBackgroundTimer;
        // Above zero while a cross-fade is running; counts up to LobbyBackgroundFadeSeconds.
        private float _lobbyBackgroundFade;
        private bool _lobbyBackgroundFading;

        // Uniform lobby zoom: the lobby is authored at this virtual resolution and the whole screen
        // (panels, fonts, icons) is scaled as one to keep it constant on any window size.
        private const float LobbyDesignWidth = 1680f;
        private const float LobbyDesignHeight = 945f;
        // Keep the floor low so the design resolution is never squeezed below the design (which would
        // reintroduce overlap on small windows); the UI just gets smaller instead.
        private const float LobbyMinScale = 0.1f;
        private float _originalUiScale;
        private float _baseUiScale = 1f;
        private bool _lobbyScaleApplied;

        private static readonly ResPath AutoLobbyBackgroundDirectory = new("/Textures/Eclipse/MainMenu/LobbyBackgrounds");
        private static readonly ResPath FallbackLobbyBackground = new("/Textures/Eclipse/MainMenu/eclipse_lobby_background.png");
        private static readonly string[] LobbyBackgroundExtensions = new[] {"png", "jpg", "jpeg", "webp"};
        // How long a wallpaper stays fully visible, and how long the cross-fade to the next one takes.
        private const float LobbyBackgroundHoldSeconds = 20f;
        private const float LobbyBackgroundFadeSeconds = 2.5f;

        protected override Type? LinkedScreenType { get; } = typeof(LobbyGui);
        public LobbyGui? Lobby;

        protected override void Startup()
        {
            if (_userInterfaceManager.ActiveScreen == null)
            {
                return;
            }

            Lobby = (LobbyGui) _userInterfaceManager.ActiveScreen;

            _gameTicker = _entityManager.System<ClientGameTicker>();
            _contentAudioSystem = _entityManager.System<ContentAudioSystem>();
            _currency = _entityManager.System<EclipseCurrencyClientSystem>();

            // Remember the player's real UI scale so it can be restored when leaving the lobby, and the
            // native effective scale ("how it looked before") which is the ceiling we never exceed.
            _originalUiScale = _cfg.GetCVar(CVars.DisplayUIScale);
            _baseUiScale = Lobby.UIScale > 0f ? Lobby.UIScale : 1f;
            _lobbyScaleApplied = false;
            _contentAudioSystem.LobbySoundtrackChanged += UpdateLobbySoundtrackInfo;

            Lobby.Chat.Main = true;
            Lobby.Chat.MinWidth = 0f;
            Lobby.Chat.MinHeight = 0f;
            Lobby.Chat.ChatWindowPanel.MinWidth = 0f;
            Lobby.Chat.ChatInput.MinWidth = 0f;
            Lobby.Chat.SafelySelectChannel(ChatSelectChannel.OOC);
            Lobby.Chat.Repopulate();

            _voteManager.SetPopupContainer(Lobby.VoteContainer);
            LayoutContainer.SetAnchorPreset(Lobby, LayoutContainer.LayoutPreset.Wide);

            var lobbyNameCvar = _cfg.GetCVar(CCVars.ServerLobbyName);
            var serverName = _baseClient.GameInfo?.ServerName ?? string.Empty;

            Lobby.ServerName.Text = string.IsNullOrEmpty(lobbyNameCvar)
                ? Loc.GetString("ui-lobby-title", ("serverName", serverName))
                : lobbyNameCvar;

            UpdateLobbyUi();

            Lobby.CharacterPreview.CharacterSetupButton.OnPressed += OnSetupPressed;
            Lobby.AccountCustomizeButton.OnPressed += OnSetupPressed;
            Lobby.ReadyButton.OnPressed += OnReadyPressed;
            Lobby.ReadyButton.OnToggled += OnReadyToggled;
            _adminManager.AdminStatusUpdated += UpdateAdminControls;
            _preferencesManager.OnServerDataLoaded += RefreshAccountCard;
            _jobRequirements.Updated += RefreshAccountCard;
            _currency.BalanceChanged += RefreshAccountCard;
            UpdateAdminControls();
            RefreshAccountCard();

            _gameTicker.InfoBlobUpdated += UpdateLobbyUi;
            _gameTicker.LobbyStatusUpdated += LobbyStatusUpdated;
            _gameTicker.LobbyLateJoinStatusUpdated += LobbyLateJoinStatusUpdated;
        }

        protected override void Shutdown()
        {
            if (Lobby != null)
                Lobby.Chat.Main = false;

            _gameTicker.InfoBlobUpdated -= UpdateLobbyUi;
            _gameTicker.LobbyStatusUpdated -= LobbyStatusUpdated;
            _gameTicker.LobbyLateJoinStatusUpdated -= LobbyLateJoinStatusUpdated;
            _contentAudioSystem.LobbySoundtrackChanged -= UpdateLobbySoundtrackInfo;

            _voteManager.ClearPopupContainer();

            Lobby!.CharacterPreview.CharacterSetupButton.OnPressed -= OnSetupPressed;
            Lobby!.AccountCustomizeButton.OnPressed -= OnSetupPressed;
            Lobby!.ReadyButton.OnPressed -= OnReadyPressed;
            Lobby!.ReadyButton.OnToggled -= OnReadyToggled;
            _adminManager.AdminStatusUpdated -= UpdateAdminControls;
            _preferencesManager.OnServerDataLoaded -= RefreshAccountCard;
            _jobRequirements.Updated -= RefreshAccountCard;
            _currency.BalanceChanged -= RefreshAccountCard;

            // Restore the player's real UI scale for in-game / other screens.
            if (_lobbyScaleApplied)
            {
                _cfg.SetCVar(CVars.DisplayUIScale, _originalUiScale);
                _lobbyScaleApplied = false;
            }

            Lobby = null;
        }

        public void SwitchState(LobbyGui.LobbyGuiState state)
        {
            // Yeah I hate this but LobbyState contains all the badness for now.
            Lobby?.SwitchState(state);
        }

        private void OnSetupPressed(BaseButton.ButtonEventArgs args)
        {
            SetReady(false);
            Lobby?.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);
        }

        private void OnReadyPressed(BaseButton.ButtonEventArgs args)
        {
            if (!_gameTicker.IsGameStarted)
            {
                return;
            }

            new LateJoinGui().OpenCentered();
        }

        private void OnReadyToggled(BaseButton.ButtonToggledEventArgs args)
        {
            SetReady(args.Pressed);
        }

        public override void FrameUpdate(FrameEventArgs e)
        {
            ApplyLobbyScale();
            // Before the early returns below, so the slideshow keeps running once the round has started.
            UpdateLobbyBackgroundFade(e.DeltaSeconds);

            _accountRefreshTimer += e.DeltaSeconds;
            if (_accountRefreshTimer >= 1f)
            {
                _accountRefreshTimer = 0f;
                RefreshAccountCard();
            }

            if (_gameTicker.IsGameStarted)
            {
                Lobby!.StartTime.Text = string.Empty;
                Lobby.SetLaunchStatusVisible(false);
                var roundTime = _gameTiming.CurTime.Subtract(_gameTicker.RoundStartTimeSpan);
                Lobby!.StationTime.Text = Loc.GetString("lobby-state-player-status-round-time", ("hours", roundTime.Hours), ("minutes", roundTime.Minutes));
                return;
            }

            Lobby!.SetLaunchStatusVisible(true);
            Lobby!.StationTime.Text = Loc.GetString("lobby-state-player-status-round-not-started");
            string text;

            if (_gameTicker.Paused)
            {
                text = Loc.GetString("lobby-state-paused");
            }
            else if (_gameTicker.StartTime < _gameTiming.CurTime)
            {
                Lobby!.StartTime.Text = Loc.GetString("lobby-state-soon");
                return;
            }
            else
            {
                var difference = _gameTicker.StartTime - _gameTiming.CurTime;
                var seconds = difference.TotalSeconds;
                if (seconds < 0)
                {
                    text = Loc.GetString(seconds < -5 ? "lobby-state-right-now-question" : "lobby-state-right-now-confirmation");
                }
                else if (difference.TotalHours >= 1)
                {
                    text = $"{Math.Floor(difference.TotalHours)}:{difference.Minutes:D2}:{difference.Seconds:D2}";
                }
                else
                {
                    text = $"{difference.Minutes}:{difference.Seconds:D2}";
                }
            }

            Lobby!.StartTime.Text = text;
        }

        /// <summary>
        /// Keeps the lobby at its native ("how it looked before") size when the window is big enough, and
        /// only scales the whole screen *down* (never up) when the window is too small to fit the design,
        /// so nothing overlaps. Driven through the display UI-scale CVar, applied by the engine to the root.
        /// </summary>
        private void ApplyLobbyScale()
        {
            if (Lobby == null)
                return;

            // PixelSize is the real (physical) window size regardless of the current scale.
            var pixelSize = Lobby.PixelSize;
            if (pixelSize.X <= 0 || pixelSize.Y <= 0)
                return;

            // Scale at which the design resolution exactly fits the window.
            var fitScale = MathF.Min(pixelSize.X / LobbyDesignWidth, pixelSize.Y / LobbyDesignHeight);

            // Window is big enough for the native look: leave the UI scale completely untouched.
            if (fitScale >= _baseUiScale - 0.005f)
            {
                if (_lobbyScaleApplied)
                {
                    _cfg.SetCVar(CVars.DisplayUIScale, _originalUiScale);
                    _lobbyScaleApplied = false;
                }
                return;
            }

            // Too small: shrink the whole screen uniformly so the design still fits.
            var desired = MathF.Max(fitScale, LobbyMinScale);
            if (MathF.Abs(_cfg.GetCVar(CVars.DisplayUIScale) - desired) <= 0.005f)
                return;

            _cfg.SetCVar(CVars.DisplayUIScale, desired);
            _lobbyScaleApplied = true;
        }

        private void LobbyStatusUpdated()
        {
            UpdateLobbyBackground();
            UpdateLobbyUi();
        }

        private void LobbyLateJoinStatusUpdated()
        {
            Lobby!.ReadyButton.Disabled = _gameTicker.DisallowedLateJoin;
        }

        private void UpdateAdminControls()
        {
            Lobby?.SetNewsAdminControlsVisible(_adminManager.HasFlag(AdminFlags.News));
        }

        private void RefreshAccountCard()
        {
            if (Lobby == null)
                return;

            var accountName = _playerManager.LocalSession?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(accountName))
                accountName = _cfg.GetCVar(CCVars.PlayerName).Trim();
            if (string.IsNullOrWhiteSpace(accountName))
                accountName = Loc.GetString("generic-unknown-title");

            var roleName = GetPreferredRoleName(_preferencesManager.Preferences?.SelectedCharacter);
            var totalExperience = GetAccountExperience();
            var progress = EclipseProgression.CalculateProgress(totalExperience);

            // Prefer the authoritative, spendable balance from the server; fall back to the legacy
            // XP-derived value until the server has sent one.
            int merits, shards;
            if (_currency.HasBalance)
            {
                merits = _currency.Merits;
                shards = _currency.Shards;
            }
            else
            {
                merits = EclipseProgression.CalculateMerits(totalExperience);
                shards = EclipseProgression.CalculateShards(totalExperience);
            }

            Lobby.SetAccountInfo(
                accountName,
                roleName,
                progress.Level,
                progress.CurrentExperience,
                progress.NextLevelExperience,
                merits,
                shards);

            Lobby.SetAccountPageData(new LobbyGui.AccountPageData(
                accountName,
                EclipseProgression.GetRankName(progress.Level),
                progress.Level,
                progress.CurrentExperience,
                progress.NextLevelExperience,
                merits,
                shards,
                _jobRequirements.FetchOverallPlaytime(),
                GetTopRoles(),
                BuildAchievements(progress.Level)));
        }

        /// <summary>
        /// The most played roles, for the account page. Roles with no recorded time are skipped.
        /// </summary>
        private List<(string Role, TimeSpan Time)> GetTopRoles()
        {
            return _jobRequirements.FetchPlaytimeByRoles()
                .Where(role => role.Value > TimeSpan.Zero)
                .OrderByDescending(role => role.Value)
                .Take(5)
                .Select(role => (Loc.GetString(role.Key), role.Value))
                .ToList();
        }

        /// <summary>
        /// Evaluates every achievement prototype against the player's playtime and level. Everything is derived
        /// client-side, so achievements need no server support.
        /// </summary>
        private List<LobbyGui.AccountAchievement> BuildAchievements(int level)
        {
            var totalHours = (float) _jobRequirements.FetchOverallPlaytime().TotalHours;
            var rolesOverAnHour = _jobRequirements.FetchPlaytimeByRoles()
                .Count(role => role.Value >= TimeSpan.FromHours(1));

            var result = new List<LobbyGui.AccountAchievement>();

            foreach (var proto in _protoMan.EnumeratePrototypes<EclipseAchievementPrototype>()
                         .OrderBy(a => a.Order)
                         .ThenBy(a => a.ID, StringComparer.Ordinal))
            {
                var current = proto.Kind switch
                {
                    EclipseAchievementKind.Playtime => totalHours,
                    EclipseAchievementKind.Level => level,
                    EclipseAchievementKind.RolePlaytime =>
                        (float) _jobRequirements.FetchPlaytimeTracker(proto.Tracker).TotalHours,
                    EclipseAchievementKind.RoleVariety => rolesOverAnHour,
                    _ => 0f,
                };

                result.Add(new LobbyGui.AccountAchievement(
                    proto.Name,
                    proto.Description,
                    proto.Icon,
                    current,
                    proto.Goal));
            }

            return result;
        }

        private string GetPreferredRoleName(HumanoidCharacterProfile? profile)
        {
            if (profile != null)
            {
                foreach (var (jobId, priority) in profile.JobPriorities.OrderByDescending(p => p.Value))
                {
                    if (priority == JobPriority.Never)
                        continue;

                    if (_protoMan.TryIndex<JobPrototype>(jobId, out var job))
                        return job.LocalizedName;
                }
            }

            return _protoMan.TryIndex<JobPrototype>(SharedGameTicker.FallbackOverflowJob, out var fallback)
                ? fallback.LocalizedName
                : Loc.GetString("generic-unknown-title");
        }

        private int GetAccountExperience()
        {
            var overallPlaytime = _jobRequirements.FetchOverallPlaytime();
            var minutes = Math.Max(overallPlaytime.TotalMinutes, _playtimeTracking.PlaytimeMinutesToday);
            var bonusMinutes = _jobRequirements.FetchPlaytimeTracker(EclipseProgression.BonusExperienceTracker).TotalMinutes;

            return EclipseProgression.CalculateTotalExperience(minutes, bonusMinutes);
        }

        private void UpdateLobbyUi()
        {
            if (_gameTicker.IsGameStarted)
            {
                Lobby!.ReadyButton.Text = Loc.GetString("lobby-state-ready-button-join-state");
                Lobby!.ReadyButton.ToggleMode = false;
                Lobby!.ReadyButton.Pressed = false;
                Lobby!.ObserveButton.Disabled = false;
                Lobby!.SetLaunchStatusVisible(false);
            }
            else
            {
                Lobby!.StartTime.Text = string.Empty;
                Lobby!.ReadyButton.Pressed = _gameTicker.AreWeReady;
                Lobby!.ReadyButton.Text = Loc.GetString(Lobby!.ReadyButton.Pressed ? "lobby-state-player-status-ready": "lobby-state-player-status-not-ready");
                Lobby!.ReadyButton.ToggleMode = true;
                Lobby!.ReadyButton.Disabled = false;
                Lobby!.ObserveButton.Disabled = true;
                Lobby!.SetLaunchStatusVisible(true);
            }

            if (_gameTicker.ServerInfoBlob != null)
            {
                Lobby!.ServerInfo.SetInfoBlob(_gameTicker.ServerInfoBlob);
            }

            var minutesToday = _playtimeTracking.PlaytimeMinutesToday;
            if (minutesToday > 60)
            {
                Lobby!.PlaytimeComment.Visible = true;

                var hoursToday = Math.Round(minutesToday / 60f, 1);

                var chosenString = minutesToday switch
                {
                    < 180 => "lobby-state-playtime-comment-normal",
                    < 360 => "lobby-state-playtime-comment-concerning",
                    < 720 => "lobby-state-playtime-comment-grasstouchless",
                    _ => "lobby-state-playtime-comment-selfdestructive"
                };

                Lobby.PlaytimeComment.SetMarkup(Loc.GetString(chosenString, ("hours", hoursToday)));
            }
            else
                Lobby!.PlaytimeComment.Visible = false;
        }

        private void UpdateLobbySoundtrackInfo(LobbySoundtrackChangedEvent ev)
        {
            if (ev.SoundtrackFilename == null)
            {
                Lobby!.LobbySong.SetMarkup(Loc.GetString("lobby-state-song-no-song-text"));
            }
            else if (
                ev.SoundtrackFilename != null
                && _resourceCache.TryGetResource<AudioResource>(ev.SoundtrackFilename, out var lobbySongResource)
                )
            {
                var lobbyStream = lobbySongResource.AudioStream;

                var title = string.IsNullOrEmpty(lobbyStream.Title)
                    ? Loc.GetString("lobby-state-song-unknown-title")
                    : lobbyStream.Title;

                var artist = string.IsNullOrEmpty(lobbyStream.Artist)
                    ? Loc.GetString("lobby-state-song-unknown-artist")
                    : lobbyStream.Artist;

                var markup = Loc.GetString("lobby-state-song-text",
                    ("songTitle", title),
                    ("songArtist", artist));

                Lobby!.LobbySong.SetMarkup(markup);
            }
        }

        private void UpdateLobbyBackground()
        {
            if (_lobbyBackgrounds.Length == 0)
                _lobbyBackgrounds = LoadLobbyBackgrounds();

            if (_lobbyBackgrounds.Length > 0 &&
                TryApplyLobbyBackground(_lobbyBackgrounds[_lobbyBackgroundIndex], Lobby!.Background))
            {
                return;
            }

            if (_gameTicker.LobbyBackground is { } lobbyBackgroundId &&
                _protoMan.TryIndex(lobbyBackgroundId, out LobbyBackgroundPrototype? lobbyBackground) &&
                _resourceCache.TryGetResource<TextureResource>(lobbyBackground.Background, out var prototypeTexture))
            {
                Lobby!.Background.Texture = prototypeTexture;

                var title = Loc.GetString(lobbyBackground.Title);
                var artist = Loc.GetString(lobbyBackground.Artist);
                Lobby.LobbyBackground.SetMarkup(Loc.GetString("lobby-state-background-text",
                    ("backgroundTitle", title),
                    ("backgroundArtist", artist)));

                return;
            }

            var texture = _resourceCache.GetResource<TextureResource>(FallbackLobbyBackground);
            Lobby!.Background.Texture = texture;
            Lobby.LobbyBackground.SetMarkup(Loc.GetString("lobby-state-background-text",
                ("backgroundTitle", FallbackLobbyBackground.FilenameWithoutExtension),
                ("backgroundArtist", Loc.GetString("lobby-state-background-unknown-artist"))));
        }

        /// <summary>
        /// Every wallpaper in the backgrounds folder, in a random order. Drop files in or remove them to change
        /// the slideshow; nothing here is hardcoded to a specific image.
        /// </summary>
        private ResPath[] LoadLobbyBackgrounds()
        {
            var backgrounds = _resourceCache.ContentFindFiles(AutoLobbyBackgroundDirectory)
                .Where(path => LobbyBackgroundExtensions.Contains(path.Extension))
                .ToArray();

            _random.Shuffle(backgrounds);
            return backgrounds;
        }

        private bool TryApplyLobbyBackground(ResPath path, TextureRect target)
        {
            if (!_resourceCache.TryGetResource<TextureResource>(path, out var texture))
                return false;

            target.Texture = texture;
            Lobby!.LobbyBackground.SetMarkup(Loc.GetString("lobby-state-background-text",
                ("backgroundTitle", path.FilenameWithoutExtension),
                ("backgroundArtist", Loc.GetString("lobby-state-background-unknown-artist"))));

            return true;
        }

        /// <summary>
        /// Advances the wallpaper slideshow: holds the current image, then cross-fades the next one in over it.
        /// </summary>
        private void UpdateLobbyBackgroundFade(float deltaSeconds)
        {
            // Nothing to cross-fade to with a single wallpaper.
            if (Lobby is null || _lobbyBackgrounds.Length < 2)
                return;

            if (!_lobbyBackgroundFading)
            {
                _lobbyBackgroundTimer += deltaSeconds;
                if (_lobbyBackgroundTimer < LobbyBackgroundHoldSeconds)
                    return;

                var next = (_lobbyBackgroundIndex + 1) % _lobbyBackgrounds.Length;
                if (!TryApplyLobbyBackground(_lobbyBackgrounds[next], Lobby.BackgroundNext))
                {
                    // Unreadable file: skip past it rather than retrying it every frame.
                    _lobbyBackgroundIndex = next;
                    _lobbyBackgroundTimer = 0f;
                    return;
                }

                _lobbyBackgroundIndex = next;
                _lobbyBackgroundTimer = 0f;
                _lobbyBackgroundFade = 0f;
                _lobbyBackgroundFading = true;
                Lobby.BackgroundNext.Visible = true;
                Lobby.BackgroundNext.Modulate = Color.White.WithAlpha(0f);
                return;
            }

            _lobbyBackgroundFade += deltaSeconds;
            var progress = Math.Clamp(_lobbyBackgroundFade / LobbyBackgroundFadeSeconds, 0f, 1f);
            // Smoothstep, so the fade eases in and out instead of starting and stopping abruptly.
            var alpha = progress * progress * (3f - 2f * progress);
            Lobby.BackgroundNext.Modulate = Color.White.WithAlpha(alpha);

            if (progress < 1f)
                return;

            // Fade finished: the top layer becomes the base one and goes back to being transparent.
            Lobby.Background.Texture = Lobby.BackgroundNext.Texture;
            Lobby.BackgroundNext.Visible = false;
            Lobby.BackgroundNext.Modulate = Color.White.WithAlpha(0f);
            _lobbyBackgroundFading = false;
            _lobbyBackgroundFade = 0f;
        }

        private void SetReady(bool newReady)
        {
            if (_gameTicker.IsGameStarted)
            {
                return;
            }

            _consoleHost.ExecuteCommand($"toggleready {newReady}");
        }

    }
}

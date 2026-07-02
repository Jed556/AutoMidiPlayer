using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core.Games;
using AutoMidiPlayer.WPF.Dialogs;
using AutoMidiPlayer.WPF.Helpers;
using AutoMidiPlayer.WPF.Services;
using AutoMidiPlayer.WPF.Views;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Stylet;
using StyletIoC;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using AutoMidiPlayer.WPF.Controls.Snackbar;
using AutoSuggestBox = Wpf.Ui.Controls.AutoSuggestBox;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;
using WpfUiAppTheme = Wpf.Ui.Appearance.ApplicationTheme;

namespace AutoMidiPlayer.WPF.ViewModels;

[UsedImplicitly]
public class MainWindowViewModel : Conductor<IScreen>, IHandle<MidiFile>
{
    public static NavigationView? Navigation = null;
    private readonly IEventAggregator _events;
    private static readonly Settings Settings = Settings.Default;

    private static readonly string AppName = "Auto MIDI Player";
    private static readonly string[] MidiExtensions = [".mid", ".midi"];
    private const int InitialStartupSongBatchSize = 50;
    private const int DeferredStartupSongLoadDelayMs = 350;
    private readonly DispatcherTimer _gameStateTimer;
    private readonly TaskCompletionSource _startupLoadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isStartupLocked = true;
    private double _startupLoadProgress;
    private string _startupLoadStatus = "Starting up...";

    // Current page name for breadcrumb display
    private string[] _breadcrumbItems = ["Songs"];
    public string[] BreadcrumbItems
    {
        get => _breadcrumbItems;
        set => SetAndNotify(ref _breadcrumbItems, value);
    }
    public event Action? ActiveGamesChanged;

    // Helper to set selected navigation item safely
    private void SetSelectedNavItem(NavigationViewItem? item)
    {
        if (Navigation == null) return;

        try
        {
            // Deactivate all items first
            foreach (var navItem in Navigation.MenuItems.OfType<NavigationViewItem>())
            {
                try { navItem.IsActive = false; } catch { /* Ignore animation errors */ }
            }
            foreach (var navItem in Navigation.FooterMenuItems.OfType<NavigationViewItem>())
            {
                try { navItem.IsActive = false; } catch { /* Ignore animation errors */ }
            }

            if (item == null)
                return;

            // Activate the selected item
            try { item.IsActive = true; } catch { /* Ignore animation errors */ }
        }
        catch
        {
            // Fallback: ignore visual selection errors
        }
    }

    public MainWindowViewModel(IContainer ioc)
    {
        Title = AppName;

        Ioc = ioc;
        _events = ioc.Get<IEventAggregator>();
        _events.Subscribe(this);

        // Initialize services FIRST - ViewModels depend on these
        // SongService manages per-song settings (key, speed, transpose), editing, and deletion
        SongSettings = new SongService(ioc);

        // FileService handles file operations: adding, removing, scanning MIDI files,
        // and missing/bad file handling
        FileService = new FileService(ioc);

        // PlaybackService (engine) handles backend playback: initialization, note playing, file loading
        PlaybackEngine = new PlaybackEngineService(ioc, this);

        // PlaybackControlsService handles user-facing controls: play/pause, slider, listen mode, UI state
        PlaybackControls = new PlaybackControlsService(ioc, this);

        // Initialize game info from registry
        Games = new BindableCollection<GameInfo>(
            GameRegistry.AllGames.Select(g => new GameInfo(g)));

        // Determine selected game from persisted settings
        SelectedGame = Games.FirstOrDefault(g => g.IsSelected) ?? Games[0];
        foreach (var g in Games) g.IsSelected = g == SelectedGame;
        PersistActiveGames();

        // Initialize ViewModels - order matters for dependencies
        SettingsView = new(ioc, this);
        AboutView = new();
        InstrumentView = new(ioc, this, new Controls.NoSongPlaceholder.NoSongPlaceholderComponent(this));

        // TrackView only handles track list management
        TrackView = new(ioc, this, new Controls.NoSongPlaceholder.NoSongPlaceholderComponent(this));

        // QueueView and SongsView depend on Playback being initialized
        QueueView = new(ioc, this);
        SongsView = new(ioc, this);
        PianoSheetView = new(this, new Controls.NoSongPlaceholder.NoSongPlaceholderComponent(this));

        // OnlineMidiView depends on FileService/SongsView/QueueView for importing downloads
        OnlineMidiView = new(ioc, this);

        _ = AboutViewModel.GetContributorsAsync();

        var initialPage = NormalizePageName(Settings.LastViewedPage);
        BreadcrumbItems = [initialPage];
        ActiveItem = ResolveViewFromName(initialPage);

        // Late-bind back-references so services can access ViewModels
        SongSettings.SetMain(this);
        FileService.SetMain(this);

        _gameStateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _gameStateTimer.Tick += (_, _) => RefreshGameRunningState();

        IsGameSelectorOpen = false;
        RefreshGameRunningState();
    }

    public IContainer Ioc { get; }

    public SongService SongSettings { get; }

    public FileService FileService { get; }

    public PlaybackControlsService PlaybackControls { get; }

    public PlaybackEngineService PlaybackEngine { get; }

    public void Handle(MidiFile message)
    {
        // Title will be updated when playback starts via UpdateTitle()
        UpdateTitle();
    }

    public void UpdateTitle()
    {
        // Only show song title when actively playing, not when paused or stopped
        if (PlaybackControls.IsPlaying && QueueView.OpenedFile is not null)
        {
            var title = QueueView.OpenedFile.Title;
            var artist = QueueView.OpenedFile.Artist;
            Title = string.IsNullOrWhiteSpace(artist) ? title : $"{title} • {artist}";
        }
        else
        {
            Title = AppName;
        }
    }

    public bool ShowUpdate => SettingsView.NeedsUpdate && ActiveItem != SettingsView;

    public SongsViewModel SongsView { get; }

    public OnlineMidiViewModel OnlineMidiView { get; }

    public TrackViewModel TrackView { get; }

    public PianoSheetViewModel PianoSheetView { get; }

    public QueueViewModel QueueView { get; }

    public SettingsPageViewModel SettingsView { get; }

    public AboutViewModel AboutView { get; }

    public InstrumentViewModel InstrumentView { get; }

    private static string NormalizePageName(string? pageName) => pageName switch
    {
        "About" => "About",
        "Tracks" => "Tracks",
        "Sheet" => "Sheet",
        "Instrument" => "Instrument",
        "Queue" => "Queue",
        "Settings" => "Settings",
        "Discover" => "Discover",
        "Songs" => "Songs",
        _ => "Songs"
    };

    private IScreen ResolveViewFromName(string? pageName) => NormalizePageName(pageName) switch
    {
        "About" => AboutView,
        "Tracks" => TrackView,
        "Sheet" => PianoSheetView,
        "Instrument" => InstrumentView,
        "Queue" => QueueView,
        "Settings" => SettingsView,
        "Discover" => OnlineMidiView,
        "Songs" or _ => SongsView
    };

    private bool _isGameSelectorOpen;
    private DateTime _lastGameSelectorCloseTime = DateTime.MinValue;

    public bool IsGameSelectorOpen
    {
        get => _isGameSelectorOpen;
        set
        {
            if (_isGameSelectorOpen && !value)
            {
                _lastGameSelectorCloseTime = DateTime.UtcNow;
            }
            SetAndNotify(ref _isGameSelectorOpen, value);
        }
    }

    /// <summary>Observable collection of all supported games with runtime state</summary>
    public BindableCollection<GameInfo> Games { get; }

    /// <summary>The currently selected/active game</summary>
    public GameInfo SelectedGame { get; set; }

    public IEnumerable<string> ActiveGameNames
    {
        get { yield return SelectedGame?.Definition.InstrumentGameName ?? string.Empty; }
    }

    public string Title { get; set; }

    public bool IsStartupLocked => _isStartupLocked;

    public bool IsControlPanelEnabled => !_isStartupLocked;

    public bool IsStartupProgressVisible => _isStartupLocked;

    public double StartupLoadProgress
    {
        get => _startupLoadProgress;
        private set => SetAndNotify(ref _startupLoadProgress, value);
    }

    public string StartupLoadStatus
    {
        get => _startupLoadStatus;
        private set => SetAndNotify(ref _startupLoadStatus, value);
    }

    public Task WaitForStartupLoadAsync() => _startupLoadCompletion.Task;

    public void CompleteStartupInteractionLock()
    {
        if (!_isStartupLocked)
            return;

        StartupLoadProgress = 100;
        StartupLoadStatus = string.Empty;

        _isStartupLocked = false;
        NotifyOfPropertyChange(nameof(IsStartupLocked));
        NotifyOfPropertyChange(nameof(IsControlPanelEnabled));
        NotifyOfPropertyChange(nameof(IsStartupProgressVisible));
    }

    private void ReportStartupProgress(double progress, string status)
    {
        StartupLoadProgress = Math.Clamp(progress, 0, 100);
        StartupLoadStatus = status;
    }

    public void NavigateToItem(object sender, RoutedEventArgs args)
    {
        if (sender is NavigationViewItem { Tag: IScreen viewModel } item)
        {
            if (ActiveItem == viewModel)
                return;

            // Set selected item for visual indicator BEFORE heavy loading
            SetSelectedNavItem(item);

            ActivateItem(viewModel);

            // Update breadcrumb with current page name
            var pageName = item.Content?.ToString();
            if (!string.IsNullOrEmpty(pageName))
            {
                BreadcrumbItems = [pageName];
                Settings.LastViewedPage = pageName;
                Settings.Save();
                Logger.LogPageVisit(pageName, source: "navigation-click");
            }
        }

        NotifyOfPropertyChange(() => ShowUpdate);
    }

    public void NavigateToSettings()
    {
        if (ActiveItem == SettingsView) return;

        // Find the Settings navigation item and set it as active
        var settingsNavItem = Navigation?.FooterMenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(nav => nav.Tag == SettingsView);
        if (settingsNavItem != null)
        {
            SetSelectedNavItem(settingsNavItem);
        }

        ActivateItem(SettingsView);

        // Update breadcrumb with current page name
        BreadcrumbItems = ["Settings"];
        Settings.LastViewedPage = "Settings";
        Settings.Save();

        // Notify that ShowUpdate property may have changed
        NotifyOfPropertyChange(() => ShowUpdate);

        // Scroll to Version section when coming from update button
        // Defer with a small delay to ensure view is fully activated and rendered
        var dispatcher = (View as FrameworkElement)?.Dispatcher ?? System.Windows.Application.Current?.Dispatcher;
        dispatcher?.InvokeAsync(
            () => SettingsView.ScrollToVersionSection(),
            System.Windows.Threading.DispatcherPriority.Normal);

        Logger.LogPageVisit("Settings", source: "programmatic-navigation");
    }

    public void NavigateToSongs()
    {
        if (ActiveItem == SongsView) return;

        ActivateItem(SongsView);
        var songs = Navigation?.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(nav => nav.Tag == SongsView);
        if (songs != null)
        {
            SetSelectedNavItem(songs);
        }
        BreadcrumbItems = ["Songs"];
        Settings.LastViewedPage = "Songs";
        Settings.Save();
        Logger.LogPageVisit("Songs", source: "programmatic-navigation");
    }

    public void NavigateToQueue()
    {
        if (ActiveItem == QueueView) return;

        ActivateItem(QueueView);
        var queue = Navigation?.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(nav => nav.Tag == QueueView);
        if (queue != null)
        {
            SetSelectedNavItem(queue);
        }
        BreadcrumbItems = ["Queue"];
        Settings.LastViewedPage = "Queue";
        Settings.Save();
        Logger.LogPageVisit("Queue", source: "programmatic-navigation");
    }

    public void NavigateToAbout()
    {
        if (ActiveItem == AboutView) return;

        SetSelectedNavItem(null);
        ActivateItem(AboutView);

        BreadcrumbItems = ["About"];

        NotifyOfPropertyChange(() => ShowUpdate);
        Logger.LogPageVisit("About", source: "titlebar");
    }

    public void ToggleGameSelector()
    {
        if (!IsGameSelectorOpen && (DateTime.UtcNow - _lastGameSelectorCloseTime).TotalMilliseconds < 250)
        {
            // Just closed due to StaysOpen="False" losing focus. Don't reopen.
            return;
        }

        IsGameSelectorOpen = !IsGameSelectorOpen;
        var selectedGameName = SelectedGame?.Definition.DisplayName ?? "none";
        Logger.LogStep("GAME_SELECTOR_TOGGLE", $"opened={IsGameSelectorOpen} | selectedGame='{selectedGameName}'");
    }

    /// <summary>
    /// Select a game from the popup. Called via Stylet action binding with CommandParameter.
    /// </summary>
    public void SelectGame(GameInfo game)
    {
        var previousGameName = SelectedGame?.Definition.DisplayName ?? "none";

        // Skip if re-selecting the already-active game
        if (game == SelectedGame)
        {
            IsGameSelectorOpen = false;
            Logger.LogStep("GAME_SELECTOR_SELECT_SAME", $"game='{previousGameName}'");
            return;
        }

        foreach (var g in Games) g.IsSelected = false;
        game.IsSelected = true;
        SelectedGame = game;
        IsGameSelectorOpen = false;

        Logger.LogStep(
            "GAME_SELECTOR_SELECT",
            $"from='{previousGameName}' | to='{game.Definition.DisplayName}' | gameId='{game.Definition.Id}'");

        PersistActiveGames();
        NotifyActiveGamesChanged();
    }

    public void ToggleTheme()
    {
        var currentTheme = ApplicationThemeManager.GetAppTheme();
        var newTheme = currentTheme switch
        {
            WpfUiAppTheme.Dark => WpfUiAppTheme.Light,
            WpfUiAppTheme.Light => WpfUiAppTheme.Dark,
            _ => WpfUiAppTheme.Dark
        };

        var matchingOption = SettingsPageViewModel.ThemeOptions
            .FirstOrDefault(option => option.Value == newTheme);

        if (matchingOption is not null)
            SettingsView.SelectedTheme = matchingOption;
        else
            SettingsView.OnThemeChanged();
    }

    public void TrayShowWindow()
    {
        var window = View as Window;
        if (window != null)
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }
    }

    public void SearchSong(AutoSuggestBox sender, TextChangedEventArgs e)
    {
        if (ActiveItem != QueueView)
        {
            ActivateItem(QueueView);

            var queue = Navigation?.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(nav => nav.Tag == QueueView);
            if (queue != null)
            {
                SetSelectedNavItem(queue);
                BreadcrumbItems = ["Queue"];
                Logger.LogPageVisit("Queue", source: "search-autonavigate");
            }
        }

        QueueView.FilterText = sender.Text;
    }

    public void OnDragOver(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            var hasMidiFiles = files.Any(f => MidiExtensions.Contains(
                System.IO.Path.GetExtension(f).ToLowerInvariant()));

            e.Effects = hasMidiFiles ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    public async void OnFileDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var midiFiles = files.Where(f => MidiExtensions.Contains(
            System.IO.Path.GetExtension(f).ToLowerInvariant())).ToArray();

        if (midiFiles.Length > 0)
        {
            Logger.LogStep("FILE_DROP", $"midiFiles={midiFiles.Length}");
            await FileService.AddFiles(midiFiles);

            // Navigate to songs view
            ActivateItem(SongsView);
            var songs = Navigation?.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(nav => nav.Tag == SongsView);
            if (songs != null)
            {
                SetSelectedNavItem(songs);
                BreadcrumbItems = ["Songs"];
                Logger.LogPageVisit("Songs", source: "file-drop");
            }
        }
    }

    protected override async void OnViewLoaded()
    {
        Navigation = ((MainWindowView)View).RootNavigation;
        SnackbarService.Container = ((MainWindowView)View).RootSnackbarContainer;

        ReportStartupProgress(4, "Preparing startup...");

        // Let the window finish first paint before heavy startup work.
        await Task.Yield();

        await ShowResetSuccessDialogIfNeededAsync();

        // Show welcome/telemetry opt-in dialog on first launch
        await WelcomeView.ShowIfFirstLaunchAsync();

        try
        {
            // Restore last viewed page (default to Songs if not set)
            var lastPage = NormalizePageName(Settings.LastViewedPage);

            // Search in both MenuItems and FooterMenuItems
            var targetNavItem = Navigation?.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(nav => nav.Content?.ToString() == lastPage)
                ?? Navigation?.FooterMenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(nav => nav.Content?.ToString() == lastPage);

            if (targetNavItem?.Tag is IScreen viewModel)
            {
                ActivateItem(viewModel);
                // Set selected item for visual indicator
                SetSelectedNavItem(targetNavItem);
                // Update breadcrumb with current page name
                BreadcrumbItems = [lastPage];
                Logger.LogPageVisit(lastPage, source: "startup-restore");
            }

            if (SettingsView.AutoCheckUpdates)
            {
                _ = SettingsView.CheckForUpdate()
                    .ContinueWith(_ => { NotifyOfPropertyChange(() => ShowUpdate); });
            }

            ReportStartupProgress(30, "Loading song database...");

            // Load songs from database into Songs library
            await using var db = Ioc.Get<PlayerContext>();
            var startupSongs = await db.Songs
                .AsNoTracking()
                .ToListAsync();

            ReportStartupProgress(42, $"Loading {startupSongs.Count} songs...");
            await LoadStartupSongsStagedAsync(startupSongs);

            ReportStartupProgress(95, "Preparing input hooks...");
            _gameStateTimer.Start();
            NotifyActiveGamesChanged();
        }
        finally
        {
            _startupLoadCompletion.TrySetResult();
        }
    }

    private async Task LoadStartupSongsStagedAsync(IReadOnlyList<AutoMidiPlayer.Data.Entities.Song> startupSongs)
    {
        if (startupSongs.Count == 0)
        {
            ReportStartupProgress(78, "No songs found.");
            FinalizeStartupSongLoad();
            return;
        }

        var firstBatchCount = Math.Min(InitialStartupSongBatchSize, startupSongs.Count);
        var firstBatch = startupSongs.Take(firstBatchCount).ToList();
        ReportStartupProgress(58, $"Loading songs ({firstBatchCount}/{startupSongs.Count})...");
        await FileService.AddFiles(firstBatch);

        if (startupSongs.Count <= firstBatchCount)
        {
            ReportStartupProgress(80, "Song library loaded.");
            FinalizeStartupSongLoad();
            return;
        }

        await LoadDeferredStartupSongsAsync(startupSongs.Skip(firstBatchCount).ToList(), startupSongs.Count, firstBatchCount);
        ReportStartupProgress(80, "Song library loaded.");
        FinalizeStartupSongLoad();
    }

    private async Task LoadDeferredStartupSongsAsync(
        IReadOnlyList<AutoMidiPlayer.Data.Entities.Song> deferredSongs,
        int totalCount,
        int loadedCount)
    {
        try
        {
            // Let initial UI interaction settle before loading the remaining library.
            await Task.Delay(DeferredStartupSongLoadDelayMs);
            ReportStartupProgress(70, $"Loading songs ({loadedCount + deferredSongs.Count}/{totalCount})...");
            await FileService.AddFiles(deferredSongs);
        }
        catch (Exception ex)
        {
            Logger.Log("Deferred startup song load failed.");
            Logger.LogException(ex);
        }
    }

    private void FinalizeStartupSongLoad()
    {
        // Schedule auto-scan in the background so startup navigation remains responsive.
        _ = RunStartupMidiAutoScanAsync();

        // Restore queue from saved state after song list is fully loaded.
        QueueView.RestoreQueue(SongsView.Tracks);

        // Restore previously playing song and position.
        var savedPosition = QueueView.RestoreCurrentSong(SongsView.Tracks);
        var restoredFile = QueueView.OpenedFile;
        if (restoredFile is not null)
            _ = RestoreStartupSongAsync(restoredFile, savedPosition);
    }

    private async Task RestoreStartupSongAsync(MidiFile restoredFile, double? savedPosition)
    {
        try
        {
            await PlaybackEngine.LoadFileAsync(restoredFile, autoPlay: false);

            if (savedPosition.HasValue)
                PlaybackControls.SetSavedPosition(savedPosition.Value);
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to restore the previously playing song during startup.");
            Logger.LogException(ex);
        }
    }

    private async Task RunStartupMidiAutoScanAsync()
    {
        if (!SettingsView.AutoScanMidiFolder || string.IsNullOrWhiteSpace(SettingsView.MidiFolder))
            return;

        var midiFolder = SettingsView.MidiFolder;
        var startedAt = DateTime.UtcNow;
        Logger.LogStep("STARTUP_AUTO_SCAN_BEGIN", $"folder='{midiFolder}'");

        try
        {
            // Allow first-load UI interactions before scanning large libraries.
            await Task.Delay(500);

            if (string.IsNullOrWhiteSpace(midiFolder))
                return;

            var appDispatcher = Application.Current?.Dispatcher;
            if (appDispatcher is null)
                return;

            var scanTask = await appDispatcher.InvokeAsync(
                () => SettingsView.ScanMidiFolder(),
                DispatcherPriority.Background);
            await scanTask;

            var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            Logger.LogStep("STARTUP_AUTO_SCAN_END", $"folder='{midiFolder}' | elapsedMs={elapsedMs:F0}");
        }
        catch (Exception ex)
        {
            Logger.Log("Startup MIDI auto-scan failed.");
            Logger.LogException(ex);
        }
    }

    private static async Task ShowResetSuccessDialogIfNeededAsync()
    {
        await AppStatusView.ShowIfStatusMarkerExistsAsync();
    }
    public void ShowGameInactiveToast(bool listenModeEnabled)
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                ShowGameInactiveToast(listenModeEnabled)));
            return;
        }

        var gameName = SelectedGame?.Definition.DisplayName ?? "Game";
        var content = listenModeEnabled
            ? $"{gameName} is not running.\nEnabled Listen Mode."
            : $"{gameName} is not running.";

        SnackbarService.Warning(
            "Game not running",
            content);
    }

    public void ShowGameFocusLossToast()
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ShowGameFocusLossToast));
            return;
        }

        var gameName = SelectedGame?.Definition.DisplayName ?? "Game";

        SnackbarService.Show(
            "Playback paused",
            $"{gameName} lost focus.",
            SnackbarSeverity.Info,
            iconSymbol: SymbolRegular.PauseCircle24);
    }

    public void ShowPlaybackStoppedGameNotRunningToast()
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == false)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(ShowPlaybackStoppedGameNotRunningToast));
            return;
        }

        var gameName = SelectedGame?.Definition.DisplayName ?? "Game";

        SnackbarService.Show(
            "Playback paused",
            $"{gameName} is not running.",
            SnackbarSeverity.Info,
            iconSymbol: SymbolRegular.PauseCircle24);
    }

    private void RefreshGameRunningState()
    {
        foreach (var game in Games)
        {
            game.IsRunning = GameRegistry.IsGameRunning(game.Definition);
        }
    }

    private void PersistActiveGames()
    {
        foreach (var game in Games)
        {
            game.Definition.SetIsActive(game.IsSelected);
        }
    }

    private void NotifyActiveGamesChanged()
    {
        ActiveGamesChanged?.Invoke();
    }
}

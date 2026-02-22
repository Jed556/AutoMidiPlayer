using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Core.Games;
using AutoMidiPlayer.WPF.Services;
using AutoMidiPlayer.WPF.Views;
using JetBrains.Annotations;
using Stylet;
using StyletIoC;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using AutoSuggestBox = Wpf.Ui.Controls.AutoSuggestBox;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;
using WpfUiAppTheme = Wpf.Ui.Appearance.ApplicationTheme;

namespace AutoMidiPlayer.WPF.ViewModels;

[UsedImplicitly]
public class MainWindowViewModel : Conductor<IScreen>, IHandle<MidiFile>
{
    public static NavigationView? Navigation = null;
    public static SnackbarPresenter? SnackbarPresenter = null;
    private static bool _isGameInactiveSnackbarVisible;
    private readonly IEventAggregator _events;
    private static readonly Settings Settings = Settings.Default;

    private static readonly string AppName = $"Auto MIDI Player {SettingsPageViewModel.ProgramVersion}";
    private static readonly string[] MidiExtensions = { ".mid", ".midi" };
    private readonly DispatcherTimer _gameStateTimer;

    // Current page name for breadcrumb display
    public string[] BreadcrumbItems { get; set; } = { "Tracks" };
    public event Action? ActiveGamesChanged;

    // Helper to set selected navigation item safely
    private void SetSelectedNavItem(NavigationViewItem? item)
    {
        if (Navigation == null || item == null) return;

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
        // SongSettingsService manages per-song settings (key, speed, transpose)
        SongSettings = new SongSettingsService(ioc);

        // PlaybackService handles all playback logic (play/pause, seeking, note scheduling)
        Playback = new PlaybackService(ioc, this);

        // Initialize game info from registry
        Games = new BindableCollection<GameInfo>(
            GameRegistry.AllGames.Select(g => new GameInfo(g)));

        // Determine selected game from persisted settings
        SelectedGame = Games.FirstOrDefault(g => g.IsSelected) ?? Games[0];
        foreach (var g in Games) g.IsSelected = g == SelectedGame;
        PersistActiveGames();

        // Initialize ViewModels - order matters for dependencies
        SettingsView = new(ioc, this);
        InstrumentView = new(ioc, this);

        // TrackView only handles track list management
        ActiveItem = TrackView = new(ioc, this);

        // QueueView and SongsView depend on Playback being initialized
        QueueView = new(ioc, this);
        SongsView = new(ioc, this);
        PianoSheetView = new(this);

        _gameStateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _gameStateTimer.Tick += (_, _) => RefreshGameRunningState();

        IsGameSelectorOpen = false;
        RefreshGameRunningState();
    }

    public IContainer Ioc { get; }

    public SongSettingsService SongSettings { get; }

    public PlaybackService Playback { get; }

    public void Handle(MidiFile message)
    {
        // Title will be updated when playback starts via UpdateTitle()
        UpdateTitle();
    }

    public void UpdateTitle()
    {
        // Only show song title when actively playing, not when paused or stopped
        if (Playback.IsPlaying && QueueView.OpenedFile is not null)
        {
            var title = QueueView.OpenedFile.Title;
            var author = QueueView.OpenedFile.Author;
            Title = string.IsNullOrWhiteSpace(author) ? title : $"{title} • {author}";
        }
        else
        {
            Title = AppName;
        }
    }

    public bool ShowUpdate => SettingsView.NeedsUpdate && ActiveItem != SettingsView;

    public SongsViewModel SongsView { get; }

    public TrackViewModel TrackView { get; }

    public PianoSheetViewModel PianoSheetView { get; }

    public QueueViewModel QueueView { get; }

    public SettingsPageViewModel SettingsView { get; }

    public InstrumentViewModel InstrumentView { get; }

    public bool IsGameSelectorOpen { get; set; }

    /// <summary>Observable collection of all supported games with runtime state</summary>
    public BindableCollection<GameInfo> Games { get; }

    /// <summary>The currently selected/active game</summary>
    public GameInfo SelectedGame { get; set; }

    public IEnumerable<string> ActiveGameNames
    {
        get { yield return SelectedGame?.Definition.InstrumentGameName ?? string.Empty; }
    }

    public string Title { get; set; }

    public void Navigate(NavigationView sender, RoutedEventArgs args)
    {
        // Legacy method - kept for compatibility
        NotifyOfPropertyChange(() => ShowUpdate);
    }

    public void NavigateToItem(object sender, RoutedEventArgs args)
    {
        if (sender is NavigationViewItem { Tag: IScreen viewModel } item)
        {
            ActivateItem(viewModel);

            // Set selected item for visual indicator
            SetSelectedNavItem(item);

            // Update breadcrumb with current page name
            var pageName = item.Content?.ToString();
            if (!string.IsNullOrEmpty(pageName))
            {
                BreadcrumbItems = new[] { pageName };
                Settings.LastViewedPage = pageName;
                Settings.Save();
            }
        }

        NotifyOfPropertyChange(() => ShowUpdate);
    }

    public void NavigateToSettings() => ActivateItem(SettingsView);

    public void ToggleGameSelector() => IsGameSelectorOpen = !IsGameSelectorOpen;

    /// <summary>
    /// Select a game from the popup. Called via Stylet action binding with CommandParameter.
    /// </summary>
    public void SelectGame(GameInfo game)
    {
        // Skip if re-selecting the already-active game
        if (game == SelectedGame)
        {
            IsGameSelectorOpen = false;
            return;
        }

        foreach (var g in Games) g.IsSelected = false;
        game.IsSelected = true;
        SelectedGame = game;
        IsGameSelectorOpen = false;

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

        ApplicationThemeManager.Apply(newTheme, WindowBackdropType.Mica, false);
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
                BreadcrumbItems = new[] { "Queue" };
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
            await SongsView.AddFiles(midiFiles);

            // Navigate to songs view
            ActivateItem(SongsView);
            var songs = Navigation?.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(nav => nav.Tag == SongsView);
            if (songs != null)
            {
                SetSelectedNavItem(songs);
                BreadcrumbItems = new[] { "Songs" };
            }
        }
    }

    protected override async void OnViewLoaded()
    {
        Navigation = ((MainWindowView)View).RootNavigation;
        SnackbarPresenter = ((MainWindowView)View).RootSnackbarPresenter;
        SettingsView.OnThemeChanged();

        // Restore last viewed page (default to Songs if not set)
        var lastPage = Settings.LastViewedPage;
        if (string.IsNullOrEmpty(lastPage)) lastPage = "Songs";

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
            BreadcrumbItems = new[] { lastPage };
        }

        if (!await SettingsView.TryGetLocationAsync()) _ = SettingsView.LocationMissing();
        if (SettingsView.AutoCheckUpdates)
        {
            _ = SettingsView.CheckForUpdate()
                .ContinueWith(_ => { NotifyOfPropertyChange(() => ShowUpdate); });
        }

        // Load songs from database into Songs library
        await using var db = Ioc.Get<LyreContext>();
        await SongsView.AddFiles(db.Songs);

        // Auto-scan MIDI folder if configured
        if (!string.IsNullOrEmpty(SettingsView.MidiFolder))
        {
            await SongsView.ScanFolder(SettingsView.MidiFolder);
        }

        // Restore queue from saved state
        QueueView.RestoreQueue(SongsView.Tracks);

        // Restore previously playing song and position
        var savedPosition = QueueView.RestoreCurrentSong(SongsView.Tracks);
        if (savedPosition.HasValue)
        {
            Playback.SetSavedPosition(savedPosition.Value);
        }

        _gameStateTimer.Start();
        NotifyActiveGamesChanged();
    }

    public void ShowGameInactiveToast(string gameName, bool listenModeEnabled)
    {
        if (SnackbarPresenter is null)
            return;

        if (_isGameInactiveSnackbarVisible)
            return;

        var content = listenModeEnabled
            ? $"{gameName}. Enabled Listen Mode (Speakers) so you can test playback."
            : $"{gameName}. Go to Instrument view and enable Listen Mode (Speakers) if you want to test playback.";

        var snackbar = new Snackbar(SnackbarPresenter)
        {
            Title = "Game isn't active",
            Content = content,
            Appearance = ControlAppearance.Caution,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Warning24 },
            SlideTransform = new TranslateTransform(0, 24),
            Timeout = TimeSpan.FromSeconds(4),
            IsCloseButtonEnabled = true
        };

        snackbar.Opened += (_, _) => _isGameInactiveSnackbarVisible = true;
        snackbar.Closed += (_, _) => _isGameInactiveSnackbarVisible = false;

        snackbar.Show();
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

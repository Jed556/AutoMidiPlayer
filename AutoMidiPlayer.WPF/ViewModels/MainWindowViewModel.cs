using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Properties;
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
    private readonly IEventAggregator _events;
    private static readonly Settings Settings = Settings.Default;

    private static readonly string AppName = $"Auto MIDI Player {SettingsPageViewModel.ProgramVersion}";
    private static readonly string[] MidiExtensions = { ".mid", ".midi" };

    // Current page name for breadcrumb display
    public string[] BreadcrumbItems { get; set; } = { "Tracks" };

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
        // PlaybackService handles all playback logic
        Playback = new PlaybackService(ioc, this);

        // Initialize ViewModels - order matters for dependencies
        SettingsView = new(ioc, this);
        InstrumentView = new(ioc, this);

        // TrackView only handles track list management
        ActiveItem = TrackView = new(ioc, this);

        // QueueView and SongsView depend on Playback being initialized
        QueueView = new(ioc, this);
        SongsView = new(ioc, this);
        PianoSheetView = new(this);
    }

    public IContainer Ioc { get; }

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
    }
}

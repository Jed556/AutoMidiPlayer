using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Views;
using JetBrains.Annotations;
using ModernWpf;
using Stylet;
using StyletIoC;
using Wpf.Ui.Appearance;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Wpf.Ui.Controls.Interfaces;
using Wpf.Ui.Mvvm.Contracts;
using AutoSuggestBox = Wpf.Ui.Controls.AutoSuggestBox;

namespace AutoMidiPlayer.WPF.ViewModels;

[UsedImplicitly]
public class MainWindowViewModel : Conductor<IScreen>
{
    public static NavigationStore Navigation = null!;
    private readonly IContainer _ioc;
    private readonly IThemeService _theme;

    private static readonly string[] MidiExtensions = { ".mid", ".midi" };

    public MainWindowViewModel(IContainer ioc, IThemeService theme)
    {
        Title = $"Auto MIDI Player {SettingsPageViewModel.ProgramVersion}";

        _ioc = ioc;
        _theme = theme;

        QueueView = new(ioc, this);
        SongsView = new(ioc, this);
        SettingsView = new(ioc, this);
        PianoSheetView = new(this);

        ActiveItem = PlayerView = new(ioc, this);
    }

    public bool ShowUpdate => SettingsView.NeedsUpdate && ActiveItem != SettingsView;

    public SongsViewModel SongsView { get; }

    public LyrePlayerViewModel PlayerView { get; }

    public PianoSheetViewModel PianoSheetView { get; }

    public QueueViewModel QueueView { get; }

    public SettingsPageViewModel SettingsView { get; }

    public string Title { get; set; }

    public void Navigate(INavigation sender, RoutedNavigationEventArgs args)
    {
        if ((args.CurrentPage as NavigationItem)?.Tag is IScreen viewModel)
            ActivateItem(viewModel);

        NotifyOfPropertyChange(() => ShowUpdate);
    }

    public void NavigateToSettings() => ActivateItem(SettingsView);

    public void ToggleTheme()
    {
        ThemeManager.Current.ApplicationTheme = _theme.GetTheme() switch
        {
            ThemeType.Unknown => ApplicationTheme.Dark,
            ThemeType.Dark => ApplicationTheme.Light,
            ThemeType.Light => ApplicationTheme.Dark,
            ThemeType.HighContrast => ApplicationTheme.Dark,
            _ => ApplicationTheme.Dark
        };

        SettingsView.OnThemeChanged();
    }

    public void SearchSong(AutoSuggestBox sender, TextChangedEventArgs e)
    {
        if (ActiveItem != QueueView)
        {
            ActivateItem(QueueView);

            var queue = Navigation.Items
                .OfType<NavigationItem>()
                .First(nav => nav.Tag == QueueView);
            var index = Navigation.Items.IndexOf(queue);
            Navigation.SelectedPageIndex = index;
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
            var songs = Navigation.Items
                .OfType<NavigationItem>()
                .First(nav => nav.Tag == SongsView);
            var index = Navigation.Items.IndexOf(songs);
            Navigation.SelectedPageIndex = index;
        }
    }

    protected override async void OnViewLoaded()
    {
        Navigation = ((MainWindowView)View).RootNavigation;
        SettingsView.OnThemeChanged();

        if (!await SettingsView.TryGetLocationAsync()) _ = SettingsView.LocationMissing();
        if (SettingsView.AutoCheckUpdates)
        {
            _ = SettingsView.CheckForUpdate()
                .ContinueWith(_ => { NotifyOfPropertyChange(() => ShowUpdate); });
        }

        // Load songs from database into Songs library
        await using var db = _ioc.Get<LyreContext>();
        await SongsView.AddFiles(db.Songs);

        // Auto-scan MIDI folder if configured
        if (!string.IsNullOrEmpty(SettingsView.MidiFolder))
        {
            await SongsView.ScanFolder(SettingsView.MidiFolder);
        }

        // Restore queue from saved state
        QueueView.RestoreQueue(SongsView.Tracks);
    }
}

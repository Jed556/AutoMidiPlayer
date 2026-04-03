using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Dialogs;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Stylet;
using StyletIoC;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.ViewModels;

public class SongsViewModel : Screen
{
    public sealed class BadMidiFileEntry(Song song, string errorMessage)
    {
        public Song Song { get; } = song;

        public string Path => Song.Path;

        public string Title => string.IsNullOrWhiteSpace(Song.Title)
            ? System.IO.Path.GetFileNameWithoutExtension(Song.Path)
            : Song.Title!;

        public string ErrorMessage { get; } = errorMessage;
    }

    public sealed class DuplicateMidiFileEntry(string existingPath, string existingTitle, string duplicatePath)
    {
        public string ExistingPath { get; } = existingPath;

        public string ExistingTitle { get; } = existingTitle;

        public string ExistingFileName => System.IO.Path.GetFileName(ExistingPath);

        public string DuplicatePath { get; } = duplicatePath;

        public string DuplicateFileName => System.IO.Path.GetFileName(DuplicatePath);

        // Default is false = keep currently imported file.
        public bool UseDuplicate { get; set; }
    }

    public enum SortMode
    {
        CustomOrder,
        Title,
        RecentlyAdded,
        Duration
    }

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;

    public SongsViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _events = ioc.Get<IEventAggregator>();
        _main = main;

        // Load saved sort settings
        CurrentSortMode = (SortMode)Settings.SongsSortMode;
        IsAscending = Settings.SongsSortAscending;

        // Forward IsPlaying changes from Playback so bindings update
        _main.PlaybackControls.PlaybackStateChanged += HandlePlaybackStateChanged;

        // Keep MIDI folder scan button/spinner in sync with Settings page scan state.
        _main.SettingsView.PropertyChanged += HandleSettingsPropertyChanged;
    }

    private void HandleSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPageViewModel.IsScanningMidiFolder))
            NotifyOfPropertyChange(nameof(IsScanningMidiFolder));

        if (e.PropertyName == nameof(SettingsPageViewModel.MidiFolder)
            || e.PropertyName == nameof(SettingsPageViewModel.HasMidiFolder))
            NotifyOfPropertyChange(nameof(HasMidiFolder));

        if (e.PropertyName == nameof(SettingsPageViewModel.MidiFolder)
            || e.PropertyName == nameof(SettingsPageViewModel.HasMidiFolder)
            || e.PropertyName == nameof(SettingsPageViewModel.AutoScanMidiFolder)
            || e.PropertyName == nameof(SettingsPageViewModel.ShowMidiFolderManualRefresh))
            NotifyOfPropertyChange(nameof(CanManuallyScanMidiFolder));
    }

    private void HandlePlaybackStateChanged(object? sender, EventArgs e)
    {
        // Notify that Playback changed so bindings to Playback.IsPlaying re-evaluate
        // Use Dispatcher to avoid collection enumeration issues
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            NotifyOfPropertyChange(() => Playback);
        });
    }

    public QueueViewModel QueueView => _main.QueueView;

    public TrackViewModel TrackView => _main.TrackView;

    public Services.PlaybackControlsService Playback => _main.PlaybackControls;

    public BindableCollection<MidiFile> Tracks { get; } = new();

    public BindableCollection<MidiFile> SortedTracks { get; private set; } = new();

    /// <summary>
    /// Collection of songs that couldn't be loaded because the file is missing
    /// </summary>
    public BindableCollection<Song> MissingSongs { get; } = new();

    public BindableCollection<BadMidiFileEntry> BadMidiFiles { get; } = new();

    public BindableCollection<DuplicateMidiFileEntry> DuplicateMidiFiles { get; } = new();

    /// <summary>
    /// Whether there are any missing song files
    /// </summary>
    public bool HasMissingSongs => MissingSongs.Count > 0;

    public bool HasNonDuplicateFileErrors => MissingSongs.Count > 0
                                             || BadMidiFiles.Count > 0
                                             || _main.FileService.GetRemovedExistingMidiFiles().Count > 0;

    public bool HasDuplicateFileConflicts => DuplicateMidiFiles.Count > 0;

    public bool HasFileErrors => HasNonDuplicateFileErrors || HasDuplicateFileConflicts;

    public SymbolRegular FileIssuesSymbol => HasNonDuplicateFileErrors
        ? SymbolRegular.Warning24
        : SymbolRegular.Info16;

    public double FileIssuesIconSize => HasNonDuplicateFileErrors ? 24d : 16d;

    public bool FileIssuesIconFilled => HasNonDuplicateFileErrors;

    public string FileIssuesButtonToolTip => HasNonDuplicateFileErrors
        ? "File errors found"
        : "Duplicate file information";

    public bool HasMidiFolder => _main.SettingsView.HasMidiFolder;

    public bool CanManuallyScanMidiFolder => _main.SettingsView.ShowMidiFolderManualRefresh;

    public bool IsScanningMidiFolder => _main.SettingsView.IsScanningMidiFolder;

    public MidiFile? SelectedFile { get; set; }

    public string SearchText { get; set; } = string.Empty;

    public SortMode CurrentSortMode { get; set; } = SortMode.CustomOrder;

    public bool IsAscending { get; set; } = true;

    public int SortModeIndex
    {
        get => (int)CurrentSortMode;
        set => CurrentSortMode = (SortMode)value;
    }

    public string SortModeDisplay => CurrentSortMode switch
    {
        SortMode.CustomOrder => "Custom order",
        SortMode.Title => "Title",
        SortMode.RecentlyAdded => "Recently added",
        SortMode.Duration => "Duration",
        _ => "Custom order"
    };

    public string SortDirectionIcon => IsAscending ? "\xE74A" : "\xE74B";

    public bool IsCustomSort => CurrentSortMode == SortMode.CustomOrder;

    public void OnCurrentSortModeChanged()
    {
        Settings.SongsSortMode = (int)CurrentSortMode;
        Settings.Save();
        ApplySort();
    }

    public void OnIsAscendingChanged()
    {
        Settings.SongsSortAscending = IsAscending;
        Settings.Save();
        ApplySort();
    }

    public void OnSearchTextChanged() => ApplySort();

    public void SetSort(SortMode mode)
    {
        if (CurrentSortMode == mode)
        {
            // Toggle direction if same mode clicked
            IsAscending = !IsAscending;
        }
        else
        {
            CurrentSortMode = mode;
            IsAscending = true;
        }
    }

    public void ToggleSortDirection()
    {
        IsAscending = !IsAscending;
    }

    public void ApplySort()
    {
        var searchTerm = SearchText?.Trim() ?? string.Empty;

        // First, filter by search text
        IEnumerable<MidiFile> filtered = string.IsNullOrWhiteSpace(searchTerm)
            ? Tracks
            : Tracks.Where(t =>
                t.Title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(t.Song.Album) && t.Song.Album.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(t.Song.Artist) && t.Song.Artist.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)));

        // Then, apply sorting
        IEnumerable<MidiFile> sorted = CurrentSortMode switch
        {
            SortMode.CustomOrder => filtered,
            SortMode.Title => filtered.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase),
            SortMode.RecentlyAdded => filtered.OrderByDescending(t => t.Song.DateAdded ?? DateTime.MinValue), // Newer first by default
            SortMode.Duration => filtered.OrderBy(t => t.Duration),
            _ => filtered
        };

        if (CurrentSortMode != SortMode.CustomOrder)
        {
            // For RecentlyAdded, ascending means oldest first
            if (CurrentSortMode == SortMode.RecentlyAdded)
            {
                sorted = IsAscending
                    ? filtered.OrderBy(t => t.Song.DateAdded ?? DateTime.MinValue)
                    : filtered.OrderByDescending(t => t.Song.DateAdded ?? DateTime.MinValue);
            }
            else if (!IsAscending)
            {
                sorted = sorted.Reverse();
            }
        }

        SortedTracks = new BindableCollection<MidiFile>(sorted);
        NotifyOfPropertyChange(nameof(SortedTracks));
        RefreshPositions();
    }

    public async Task OpenFile()
    {
        var openFileDialog = new OpenFileDialog
        {
            Filter = "MIDI file|*.mid;*.midi|All files (*.*)|*.*",
            Multiselect = true
        };

        if (openFileDialog.ShowDialog() != true)
            return;

        await _main.FileService.AddFiles(openFileDialog.FileNames);
    }

    public async Task ScanMidiFolder()
    {
        await _main.SettingsView.ScanMidiFolder();
    }

    /// <summary>
    /// Show the missing files dialog with individual delete buttons
    /// </summary>
    public async Task ShowMissingFilesDialog()
    {
        await _main.FileService.ResolveDuplicateFallbacksAsync();
        _main.FileService.RemoveStaleDuplicateMidiFileEntries(notify: false);

        var duplicateMidiFiles = DuplicateMidiFiles.ToList();
        var removedExistingMidiFiles = _main.FileService.GetRemovedExistingMidiFiles().ToList();

        bool PruneMissingSongsForRemovedExisting(IReadOnlyCollection<Services.FileService.RemovedExistingMidiFileEntry> removedEntries)
        {
            if (removedEntries.Count == 0)
                return false;

            var removedPaths = removedEntries
                .Select(entry => entry.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var staleMissingSongs = MissingSongs
                .Where(song => removedPaths.Contains(song.Path))
                .ToList();

            if (staleMissingSongs.Count == 0)
                return false;

            foreach (var staleSong in staleMissingSongs)
                MissingSongs.Remove(staleSong);

            return true;
        }

        if (PruneMissingSongsForRemovedExisting(removedExistingMidiFiles))
            NotifyFileErrorsChanged();

        if (!HasFileErrors && removedExistingMidiFiles.Count == 0 && duplicateMidiFiles.Count == 0) return;

        var hasDatabaseFileErrors = MissingSongs.Count > 0 || BadMidiFiles.Count > 0;
        var hasDuplicateConflicts = duplicateMidiFiles.Count > 0;

        var content = new System.Windows.Controls.StackPanel { MinWidth = 460 };
        content.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");

        var headerText = new System.Windows.Controls.TextBlock
        {
            Text = "File errors were found:",
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        headerText.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        content.Children.Add(headerText);

        System.Windows.Controls.ItemsControl? missingItemsControl = null;
        System.Windows.Controls.ItemsControl? badMidiItemsControl = null;
        System.Windows.Controls.StackPanel? duplicateGroupsPanel = null;
        System.Windows.Controls.ItemsControl? removedExistingItemsControl = null;
        var dialogOpen = true;
        var refreshInProgress = false;

        void SelectDuplicateVersion(string existingPath, string? selectedDuplicatePath)
        {
            var groupEntries = DuplicateMidiFiles
                .Where(entry => string.Equals(entry.ExistingPath, existingPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var entry in groupEntries)
            {
                entry.UseDuplicate = !string.IsNullOrWhiteSpace(selectedDuplicatePath)
                                     && string.Equals(entry.DuplicatePath, selectedDuplicatePath, StringComparison.OrdinalIgnoreCase);
            }
        }

        void RebuildDuplicateGroups()
        {
            if (duplicateGroupsPanel is null)
                return;

            duplicateGroupsPanel.Children.Clear();

            var groupedDuplicates = DuplicateMidiFiles
                .GroupBy(entry => entry.ExistingPath, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var duplicateGroup in groupedDuplicates)
            {
                var existingPath = duplicateGroup.Key;
                var groupEntries = duplicateGroup
                    .OrderBy(entry => entry.DuplicateFileName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string? selectedDuplicatePath = null;
                foreach (var entry in groupEntries)
                {
                    if (!entry.UseDuplicate)
                        continue;

                    if (selectedDuplicatePath is null)
                    {
                        selectedDuplicatePath = entry.DuplicatePath;
                        continue;
                    }

                    entry.UseDuplicate = false;
                }

                var groupBorder = new System.Windows.Controls.Border
                {
                    Padding = new Thickness(10, 8, 10, 8),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                groupBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

                var groupContent = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Vertical
                };

                var groupHeader = new System.Windows.Controls.TextBlock
                {
                    Text = System.IO.Path.GetFileName(existingPath),
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                groupHeader.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
                groupContent.Children.Add(groupHeader);

                var groupPath = new System.Windows.Controls.TextBlock
                {
                    Text = existingPath,
                    FontSize = 11,
                    Opacity = 0.72,
                    Margin = new Thickness(0, 0, 0, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                groupPath.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
                groupContent.Children.Add(groupPath);

                var currentVersionCheck = new System.Windows.Controls.CheckBox
                {
                    Content = "Current in library",
                    IsChecked = string.IsNullOrWhiteSpace(selectedDuplicatePath),
                    Margin = new Thickness(0, 4, 0, 0)
                };
                currentVersionCheck.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
                currentVersionCheck.Click += (_, _) =>
                {
                    SelectDuplicateVersion(existingPath, null);
                    RebuildDuplicateGroups();
                };
                groupContent.Children.Add(currentVersionCheck);

                foreach (var duplicateEntry in groupEntries)
                {
                    var duplicateVersionCheck = new System.Windows.Controls.CheckBox
                    {
                        Content = duplicateEntry.DuplicateFileName,
                        IsChecked = string.Equals(selectedDuplicatePath, duplicateEntry.DuplicatePath, StringComparison.OrdinalIgnoreCase),
                        Margin = new Thickness(0, 4, 0, 0)
                    };
                    duplicateVersionCheck.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
                    duplicateVersionCheck.Click += (_, _) =>
                    {
                        SelectDuplicateVersion(existingPath, duplicateEntry.DuplicatePath);
                        RebuildDuplicateGroups();
                    };
                    groupContent.Children.Add(duplicateVersionCheck);

                    var duplicatePath = new System.Windows.Controls.TextBlock
                    {
                        Text = duplicateEntry.DuplicatePath,
                        FontSize = 11,
                        Opacity = 0.72,
                        Margin = new Thickness(24, 0, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    duplicatePath.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
                    groupContent.Children.Add(duplicatePath);
                }

                groupBorder.Child = groupContent;
                duplicateGroupsPanel.Children.Add(groupBorder);
            }
        }

        async Task RefreshListsAsync(bool resolveDuplicateFallbacks = true)
        {
            if (!dialogOpen || refreshInProgress)
                return;

            refreshInProgress = true;

            try
            {
                if (resolveDuplicateFallbacks)
                    await _main.FileService.ResolveDuplicateFallbacksAsync();

                _main.FileService.RemoveStaleDuplicateMidiFileEntries(notify: false);

                var refreshedRemovedExistingMidiFiles = _main.FileService.GetRemovedExistingMidiFiles().ToList();
                if (PruneMissingSongsForRemovedExisting(refreshedRemovedExistingMidiFiles))
                    NotifyFileErrorsChanged();

                if (missingItemsControl != null)
                    missingItemsControl.ItemsSource = MissingSongs.ToList();
                if (badMidiItemsControl != null)
                    badMidiItemsControl.ItemsSource = BadMidiFiles.ToList();
                if (duplicateGroupsPanel != null)
                    RebuildDuplicateGroups();
                if (removedExistingItemsControl != null)
                    removedExistingItemsControl.ItemsSource = refreshedRemovedExistingMidiFiles;
            }
            finally
            {
                refreshInProgress = false;
            }
        }

        if (MissingSongs.Count > 0)
        {
            var missingHeader = new System.Windows.Controls.TextBlock
            {
                Text = "Missing files",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            missingHeader.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            content.Children.Add(missingHeader);

            missingItemsControl = new System.Windows.Controls.ItemsControl
            {
                ItemsSource = MissingSongs.ToList()
            };

            var missingTemplate = new DataTemplate();
            var missingRowBorderFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            missingRowBorderFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            missingRowBorderFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            missingRowBorderFactory.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

            var missingGridFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
            var missingCol1 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
            missingCol1.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var missingCol2 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
            missingCol2.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, GridLength.Auto);
            missingGridFactory.AppendChild(missingCol1);
            missingGridFactory.AppendChild(missingCol2);

            var missingTextFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            missingTextFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding("Title") { FallbackValue = "Unknown" });
            missingTextFactory.SetValue(System.Windows.Controls.TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            missingTextFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            missingTextFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 0);
            missingTextFactory.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            missingGridFactory.AppendChild(missingTextFactory);

            var missingButtonFactory = new FrameworkElementFactory(typeof(Button));
            missingButtonFactory.SetValue(System.Windows.Controls.Button.ContentProperty, "✕");
            missingButtonFactory.SetResourceReference(FrameworkElement.StyleProperty, "GhostIconButton");
            missingButtonFactory.SetValue(System.Windows.Controls.Button.PaddingProperty, new Thickness(6, 2, 6, 2));
            missingButtonFactory.SetValue(System.Windows.Controls.Button.MarginProperty, new Thickness(8, 0, 0, 0));
            missingButtonFactory.SetValue(System.Windows.Controls.Button.ToolTipProperty, "Remove from database");
            missingButtonFactory.SetValue(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent);
            missingButtonFactory.SetValue(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0));
            missingButtonFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 1);
            missingButtonFactory.AddHandler(System.Windows.Controls.Button.ClickEvent,
                new RoutedEventHandler(async (s, _) =>
                {
                    if (s is System.Windows.Controls.Button btn && btn.DataContext is Song song)
                    {
                        await _main.FileService.RemoveMissingSong(song);
                        await RefreshListsAsync(resolveDuplicateFallbacks: false);
                    }
                }));
            missingGridFactory.AppendChild(missingButtonFactory);

            missingRowBorderFactory.AppendChild(missingGridFactory);
            missingTemplate.VisualTree = missingRowBorderFactory;
            missingItemsControl.ItemTemplate = missingTemplate;

            var missingScrollViewer = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 200,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                Content = missingItemsControl
            };
            missingScrollViewer.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlFillColorDefaultBrush");
            missingScrollViewer.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            content.Children.Add(missingScrollViewer);
        }

        if (BadMidiFiles.Count > 0)
        {
            var badMidiHeader = new System.Windows.Controls.TextBlock
            {
                Text = "Bad MIDI files",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, MissingSongs.Count > 0 ? 14 : 0, 0, 8)
            };
            badMidiHeader.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            content.Children.Add(badMidiHeader);

            badMidiItemsControl = new System.Windows.Controls.ItemsControl
            {
                ItemsSource = BadMidiFiles.ToList()
            };

            var badMidiTemplate = new DataTemplate();
            var badMidiRowBorderFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            badMidiRowBorderFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            badMidiRowBorderFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            badMidiRowBorderFactory.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

            var badMidiGridFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
            var badMidiCol1 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
            badMidiCol1.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var badMidiCol2 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
            badMidiCol2.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, GridLength.Auto);
            badMidiGridFactory.AppendChild(badMidiCol1);
            badMidiGridFactory.AppendChild(badMidiCol2);

            var detailsPanelFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.StackPanel));
            detailsPanelFactory.SetValue(System.Windows.Controls.StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Vertical);
            detailsPanelFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 0);

            var badMidiTitleFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            badMidiTitleFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding("Title") { FallbackValue = "Unknown" });
            badMidiTitleFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            badMidiTitleFactory.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            detailsPanelFactory.AppendChild(badMidiTitleFactory);

            var badMidiPathFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            badMidiPathFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding("Path") { FallbackValue = string.Empty });
            badMidiPathFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            badMidiPathFactory.SetValue(System.Windows.Controls.TextBlock.FontSizeProperty, 11.0);
            badMidiPathFactory.SetValue(System.Windows.Controls.TextBlock.OpacityProperty, 0.72);
            badMidiPathFactory.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            detailsPanelFactory.AppendChild(badMidiPathFactory);

            badMidiGridFactory.AppendChild(detailsPanelFactory);

            var badMidiButtonFactory = new FrameworkElementFactory(typeof(Button));
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.ContentProperty, "✕");
            badMidiButtonFactory.SetResourceReference(FrameworkElement.StyleProperty, "GhostIconButton");
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.PaddingProperty, new Thickness(6, 2, 6, 2));
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.MarginProperty, new Thickness(8, 0, 0, 0));
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.ToolTipProperty, "Remove from database");
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent);
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0));
            badMidiButtonFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 1);
            badMidiButtonFactory.AddHandler(System.Windows.Controls.Button.ClickEvent,
                new RoutedEventHandler(async (s, _) =>
                {
                    if (s is System.Windows.Controls.Button btn && btn.DataContext is BadMidiFileEntry badMidiFile)
                    {
                        await _main.FileService.RemoveBadMidiSong(badMidiFile);
                        await RefreshListsAsync(resolveDuplicateFallbacks: false);
                    }
                }));
            badMidiGridFactory.AppendChild(badMidiButtonFactory);

            badMidiRowBorderFactory.AppendChild(badMidiGridFactory);
            badMidiTemplate.VisualTree = badMidiRowBorderFactory;
            badMidiItemsControl.ItemTemplate = badMidiTemplate;

            var badMidiScrollViewer = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 220,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                Content = badMidiItemsControl
            };
            badMidiScrollViewer.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlFillColorDefaultBrush");
            badMidiScrollViewer.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            content.Children.Add(badMidiScrollViewer);
        }

        if (hasDuplicateConflicts)
        {
            var duplicateHeader = new System.Windows.Controls.TextBlock
            {
                Text = "Duplicate files",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, MissingSongs.Count > 0 || BadMidiFiles.Count > 0 ? 14 : 0, 0, 8)
            };
            duplicateHeader.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            content.Children.Add(duplicateHeader);

            var duplicateInfo = new System.Windows.Controls.TextBlock
            {
                Text = "Choose exactly one version per duplicate group. These selections stay available when reopening this conflicts menu.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72,
                Margin = new Thickness(0, 0, 0, 8)
            };
            duplicateInfo.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            content.Children.Add(duplicateInfo);

            duplicateGroupsPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Vertical
            };

            RebuildDuplicateGroups();

            var duplicateScrollViewer = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 220,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                Content = duplicateGroupsPanel
            };
            duplicateScrollViewer.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlFillColorDefaultBrush");
            duplicateScrollViewer.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            content.Children.Add(duplicateScrollViewer);
        }

        if (removedExistingMidiFiles.Count > 0)
        {
            var removedHeader = new System.Windows.Controls.TextBlock
            {
                Text = "Removed but still in MIDI folder",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, MissingSongs.Count > 0 || BadMidiFiles.Count > 0 ? 14 : 0, 0, 8)
            };
            removedHeader.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            content.Children.Add(removedHeader);

            removedExistingItemsControl = new System.Windows.Controls.ItemsControl
            {
                ItemsSource = removedExistingMidiFiles
            };

            var removedTemplate = new DataTemplate();
            var removedRowBorderFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            removedRowBorderFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            removedRowBorderFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            removedRowBorderFactory.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

            var removedGridFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
            var removedCol1 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
            removedCol1.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var removedCol2 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
            removedCol2.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, GridLength.Auto);
            removedGridFactory.AppendChild(removedCol1);
            removedGridFactory.AppendChild(removedCol2);

            var removedDetailsPanelFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.StackPanel));
            removedDetailsPanelFactory.SetValue(System.Windows.Controls.StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Vertical);
            removedDetailsPanelFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 0);

            var removedTitleFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            removedTitleFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding("Title") { FallbackValue = "Unknown" });
            removedTitleFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            removedTitleFactory.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            removedDetailsPanelFactory.AppendChild(removedTitleFactory);

            var removedPathFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            removedPathFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding("Path") { FallbackValue = string.Empty });
            removedPathFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            removedPathFactory.SetValue(System.Windows.Controls.TextBlock.FontSizeProperty, 11.0);
            removedPathFactory.SetValue(System.Windows.Controls.TextBlock.OpacityProperty, 0.72);
            removedPathFactory.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            removedDetailsPanelFactory.AppendChild(removedPathFactory);

            removedGridFactory.AppendChild(removedDetailsPanelFactory);

            var removedActionsFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.StackPanel));
            removedActionsFactory.SetValue(System.Windows.Controls.StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Horizontal);
            removedActionsFactory.SetValue(System.Windows.Controls.StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
            removedActionsFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 1);
            removedActionsFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

            var removedRestoreButtonFactory = new FrameworkElementFactory(typeof(Button));
            removedRestoreButtonFactory.SetResourceReference(FrameworkElement.StyleProperty, "GhostIconButton");
            removedRestoreButtonFactory.SetValue(System.Windows.Controls.Button.PaddingProperty, new Thickness(4));
            removedRestoreButtonFactory.SetValue(System.Windows.Controls.Button.ToolTipProperty, "Restore this MIDI file to the song list");
            removedRestoreButtonFactory.SetValue(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent);
            removedRestoreButtonFactory.SetValue(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0));
            var removedRestoreIconFactory = new FrameworkElementFactory(typeof(SymbolIcon));
            removedRestoreIconFactory.SetValue(SymbolIcon.SymbolProperty, SymbolRegular.ArrowClockwise24);
            removedRestoreButtonFactory.AppendChild(removedRestoreIconFactory);
            removedRestoreButtonFactory.AddHandler(System.Windows.Controls.Button.ClickEvent,
                new RoutedEventHandler(async (s, _) =>
                {
                    if (s is not System.Windows.Controls.Button btn ||
                        btn.DataContext is not Services.FileService.RemovedExistingMidiFileEntry removedEntry)
                    {
                        return;
                    }

                    await _main.FileService.RestoreExcludedFileToLibraryAsync(removedEntry.Path);
                    await RefreshListsAsync(resolveDuplicateFallbacks: false);
                }));
            removedActionsFactory.AppendChild(removedRestoreButtonFactory);

            var removedDeleteButtonFactory = new FrameworkElementFactory(typeof(Button));
            removedDeleteButtonFactory.SetResourceReference(FrameworkElement.StyleProperty, "GhostIconButton");
            removedDeleteButtonFactory.SetValue(System.Windows.Controls.Button.PaddingProperty, new Thickness(4));
            removedDeleteButtonFactory.SetValue(System.Windows.Controls.Button.MarginProperty, new Thickness(6, 0, 0, 0));
            removedDeleteButtonFactory.SetValue(System.Windows.Controls.Button.ToolTipProperty, "Delete this MIDI file from disk");
            removedDeleteButtonFactory.SetValue(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent);
            removedDeleteButtonFactory.SetValue(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0));
            var removedDeleteIconFactory = new FrameworkElementFactory(typeof(SymbolIcon));
            removedDeleteIconFactory.SetValue(SymbolIcon.SymbolProperty, SymbolRegular.Delete24);
            removedDeleteButtonFactory.AppendChild(removedDeleteIconFactory);
            removedDeleteButtonFactory.AddHandler(System.Windows.Controls.Button.ClickEvent,
                new RoutedEventHandler(async (s, _) =>
                {
                    if (s is not System.Windows.Controls.Button btn ||
                        btn.DataContext is not Services.FileService.RemovedExistingMidiFileEntry removedEntry)
                    {
                        return;
                    }

                    var fileDeleted = false;
                    var result = await DialogHelper.ShowActionDialogAsync(new DialogActionRequest
                    {
                        Title = "Delete MIDI file from folder?",
                        Icon = SymbolRegular.Delete24,
                        Body = $"This permanently deletes the file from disk:\n\n{removedEntry.Path}",
                        ConfirmButton = new DialogActionButton
                        {
                            Text = "Delete",
                            Appearance = ControlAppearance.Danger,
                            CallbackAsync = () =>
                            {
                                fileDeleted = _main.FileService.DeleteExcludedFileFromDisk(removedEntry.Path);
                                return Task.CompletedTask;
                            }
                        },
                        CancelButton = new DialogActionButton
                        {
                            Text = "Cancel"
                        }
                    });

                    if (result == DialogActionOutcome.Confirmed)
                    {
                        await RefreshListsAsync(resolveDuplicateFallbacks: false);

                        if (!fileDeleted && System.IO.File.Exists(removedEntry.Path))
                        {
                            var errorDialog = DialogHelper.CreateDialog();
                            errorDialog.Title = "Unable to delete file";
                            errorDialog.Content = "The file could not be deleted. It may be in use, read-only, or protected by permissions.";
                            errorDialog.CloseButtonText = "OK";
                            await errorDialog.ShowAsync();
                        }
                    }
                }));
            removedActionsFactory.AppendChild(removedDeleteButtonFactory);

            removedGridFactory.AppendChild(removedActionsFactory);

            removedRowBorderFactory.AppendChild(removedGridFactory);
            removedTemplate.VisualTree = removedRowBorderFactory;
            removedExistingItemsControl.ItemTemplate = removedTemplate;

            var removedScrollViewer = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 200,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                Content = removedExistingItemsControl
            };
            removedScrollViewer.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlFillColorDefaultBrush");
            removedScrollViewer.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            content.Children.Add(removedScrollViewer);
        }

        var dialog = DialogHelper.CreateDialog();
        dialog.Title = "File Issues";
        dialog.Content = content;
        if (hasDatabaseFileErrors)
        {
            dialog.PrimaryButtonText = "Remove All";
            dialog.PrimaryButtonAppearance = ControlAppearance.Danger;
        }

        dialog.CloseButtonText = "Close";

        var liveRefreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };

        liveRefreshTimer.Tick += async (_, _) =>
        {
            await RefreshListsAsync();
        };

        liveRefreshTimer.Start();

        ContentDialogResult result;
        try
        {
            await RefreshListsAsync();
            result = await dialog.ShowAsync();
        }
        finally
        {
            dialogOpen = false;
            liveRefreshTimer.Stop();
        }

        if (hasDatabaseFileErrors && result == ContentDialogResult.Primary)
            await _main.FileService.RemoveAllFileErrors();

        if (hasDuplicateConflicts)
            await _main.FileService.ApplyDuplicateSelectionsAsync(DuplicateMidiFiles.ToList());
    }

    public void NotifyFileErrorsChanged()
    {
        NotifyOfPropertyChange(nameof(HasMissingSongs));
        NotifyOfPropertyChange(nameof(MissingSongs));
        NotifyOfPropertyChange(nameof(BadMidiFiles));
        NotifyOfPropertyChange(nameof(DuplicateMidiFiles));
        NotifyOfPropertyChange(nameof(HasNonDuplicateFileErrors));
        NotifyOfPropertyChange(nameof(HasDuplicateFileConflicts));
        NotifyOfPropertyChange(nameof(HasFileErrors));
        NotifyOfPropertyChange(nameof(FileIssuesSymbol));
        NotifyOfPropertyChange(nameof(FileIssuesIconSize));
        NotifyOfPropertyChange(nameof(FileIssuesIconFilled));
        NotifyOfPropertyChange(nameof(FileIssuesButtonToolTip));
    }

    public void MoveUp()
    {
        if (SelectedFile is null || CurrentSortMode != SortMode.CustomOrder) return;

        var index = Tracks.IndexOf(SelectedFile);
        if (index > 0)
        {
            Tracks.Move(index, index - 1);
            ApplySort();
        }
    }

    public void MoveDown()
    {
        if (SelectedFile is null || CurrentSortMode != SortMode.CustomOrder) return;

        var index = Tracks.IndexOf(SelectedFile);
        if (index < Tracks.Count - 1)
        {
            Tracks.Move(index, index + 1);
            ApplySort();
        }
    }

    public void OnFileDoubleClick(object sender, EventArgs e)
    {
        if (SelectedFile is not null)
        {
            // Add to queue and play
            _main.QueueView.AddFile(SelectedFile);
            _events.Publish(SelectedFile);
        }
    }

    public void PlaySong(MidiFile? file)
    {
        if (file is not null)
        {
            // Add to queue if not already there and play
            _main.QueueView.AddFile(file);
            _events.Publish(file);
        }
    }

    public async void PlayPauseFromSongs(MidiFile? file)
    {
        if (file is null) return;

        // If this is the currently opened file, toggle play/pause
        if (QueueView.OpenedFile == file)
        {
            await _main.PlaybackControls.PlayPause();
        }
        else
        {
            // Otherwise, add to queue and play this song
            PlaySong(file);
            await _main.PlaybackControls.PlayPause();
        }
    }

    public void AddSelectedToQueue(IEnumerable<MidiFile> selectedFiles)
    {
        var files = selectedFiles.Any() ? selectedFiles : (SelectedFile != null ? [SelectedFile] : Array.Empty<MidiFile>());
        foreach (var file in files)
            _main.QueueView.AddFile(file);
    }

    public async Task DeleteSelected(IEnumerable<MidiFile> selectedFiles)
    {
        var filesToDelete = selectedFiles.Any()
            ? selectedFiles.ToList()
            : (SelectedFile != null ? new List<MidiFile> { SelectedFile } : new List<MidiFile>());

        await _main.SongSettings.DeleteSongsAsync(filesToDelete);
    }

    public async Task EditSelected(IEnumerable<MidiFile> selectedFiles)
    {
        // Edit only works on single selection
        var filesList = selectedFiles.ToList();
        var file = filesList.Count == 1 ? filesList[0] : SelectedFile;
        if (file is null) return;

        await _main.SongSettings.EditSongAsync(file);
    }

    private void RefreshPositions()
    {
        for (int i = 0; i < SortedTracks.Count; i++)
        {
            SortedTracks[i].Position = i + 1; // 1-based for display
        }
    }

    /// <summary>
    /// Refresh the currently playing song in the list to reflect property changes
    /// </summary>
    public void RefreshCurrentSong()
    {
        // Force a complete list refresh to show updated values
        ApplySort();
    }

    protected override void OnDeactivate()
    {
        base.OnDeactivate();
        // Clear the semi-active (single-clicked) row when switching tabs
        SelectedFile = null;
    }
}

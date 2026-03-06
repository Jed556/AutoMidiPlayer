using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Dialogs;
using Melanchall.DryWetMidi.Core;
using Microsoft.EntityFrameworkCore;
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
        _main.Playback.PlaybackStateChanged += HandlePlaybackStateChanged;
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

    public Services.PlaybackService Playback => _main.Playback;

    public BindableCollection<MidiFile> Tracks { get; } = new();

    public BindableCollection<MidiFile> SortedTracks { get; private set; } = new();

    /// <summary>
    /// Collection of songs that couldn't be loaded because the file is missing
    /// </summary>
    public BindableCollection<Song> MissingSongs { get; } = new();

    public BindableCollection<BadMidiFileEntry> BadMidiFiles { get; } = new();

    /// <summary>
    /// Whether there are any missing song files
    /// </summary>
    public bool HasMissingSongs => MissingSongs.Count > 0;

    public bool HasFileErrors => MissingSongs.Count > 0 || BadMidiFiles.Count > 0;

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

    public void SetSortCustomOrder() => SetSort(SortMode.CustomOrder);
    public void SetSortTitle() => SetSort(SortMode.Title);
    public void SetSortRecentlyAdded() => SetSort(SortMode.RecentlyAdded);
    public void SetSortDateAdded() => SetSort(SortMode.RecentlyAdded); // Alias for Date Added column
    public void SetSortDuration() => SetSort(SortMode.Duration);

    public void ToggleSortDirection()
    {
        IsAscending = !IsAscending;
    }

    public void ApplySort()
    {
        // First, filter by search text
        IEnumerable<MidiFile> filtered = string.IsNullOrWhiteSpace(SearchText)
            ? Tracks
            : Tracks.Where(t => t.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

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

        await AddFiles(openFileDialog.FileNames);
    }

    public async Task AddFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            await AddFile(file);
        }

        ApplySort();
    }

    public async Task AddFiles(IEnumerable<Song> files)
    {
        foreach (var file in files)
        {
            // Check if file exists before trying to add
            if (!File.Exists(file.Path))
            {
                // Add to missing songs collection if not already there
                if (!MissingSongs.Any(s => s.Id == file.Id))
                {
                    MissingSongs.Add(file);
                }

                RemoveBadMidiFileEntries(file.Path, false);
                continue;
            }
            await AddFile(file);
        }

        // Notify UI about missing songs status change
        NotifyOfPropertyChange(nameof(HasMissingSongs));
        NotifyOfPropertyChange(nameof(MissingSongs));
        NotifyOfPropertyChange(nameof(HasFileErrors));
        NotifyOfPropertyChange(nameof(BadMidiFiles));

        ApplySort();
    }

    /// <summary>
    /// Show the missing files dialog with individual delete buttons
    /// </summary>
    public async Task ShowMissingFilesDialog()
    {
        if (!HasFileErrors) return;

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

        void RefreshLists()
        {
            if (missingItemsControl != null)
                missingItemsControl.ItemsSource = MissingSongs.ToList();
            if (badMidiItemsControl != null)
                badMidiItemsControl.ItemsSource = BadMidiFiles.ToList();
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
                        await RemoveMissingSong(song);
                        RefreshLists();
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
                        await RemoveBadMidiSong(badMidiFile);
                        RefreshLists();
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

        var dialog = DialogHelper.CreateDialog();
        dialog.Title = "File Errors";
        dialog.Content = content;
        dialog.PrimaryButtonText = "Remove All";
        dialog.PrimaryButtonAppearance = ControlAppearance.Danger;
        dialog.CloseButtonText = "Close";

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
            await RemoveAllFileErrors();
    }

    /// <summary>
    /// Remove a single missing song from the database
    /// </summary>
    public async Task RemoveMissingSong(Song song)
    {
        await using var db = _ioc.Get<LyreContext>();

        // Remove all rows that point to the same missing path to avoid stale duplicates.
        var songsToRemove = db.Songs.Where(s => s.Path == song.Path).ToList();
        if (songsToRemove.Count == 0)
            songsToRemove.Add(song);

        db.Songs.RemoveRange(songsToRemove);
        await db.SaveChangesAsync();

        foreach (var missingSong in MissingSongs.Where(s => s.Path == song.Path).ToList())
            MissingSongs.Remove(missingSong);

        RemoveBadMidiFileEntries(song.Path, false);

        NotifyFileErrorsChanged();
    }

    /// <summary>
    /// Remove all missing songs from the database
    /// </summary>
    public async Task RemoveAllMissingSongs()
    {
        await RemoveAllFileErrors();
    }

    public async Task RemoveBadMidiSong(BadMidiFileEntry badMidiFile)
    {
        await using var db = _ioc.Get<LyreContext>();

        var songsToRemove = db.Songs.Where(s => s.Path == badMidiFile.Path).ToList();
        if (songsToRemove.Count > 0)
        {
            db.Songs.RemoveRange(songsToRemove);
            await db.SaveChangesAsync();
        }

        RemoveBadMidiFileEntries(badMidiFile.Path, false);

        foreach (var missingSong in MissingSongs.Where(s => s.Path == badMidiFile.Path).ToList())
            MissingSongs.Remove(missingSong);

        NotifyFileErrorsChanged();
    }

    public async Task RemoveAllFileErrors()
    {
        if (!HasFileErrors) return;

        var allPaths = MissingSongs.Select(s => s.Path)
            .Concat(BadMidiFiles.Select(s => s.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var db = _ioc.Get<LyreContext>();

        if (allPaths.Count > 0)
        {
            var rows = db.Songs.Where(song => allPaths.Contains(song.Path)).ToList();
            if (rows.Count > 0)
            {
                db.Songs.RemoveRange(rows);
                await db.SaveChangesAsync();
            }
        }

        MissingSongs.Clear();
        BadMidiFiles.Clear();

        NotifyFileErrorsChanged();
    }

    /// <summary>
    /// Mark a song as missing at runtime (for example, file deleted while app is running).
    /// </summary>
    public void MarkSongAsMissing(Song song)
    {
        if (!MissingSongs.Any(s => s.Id == song.Id))
            MissingSongs.Add(song);

        foreach (var track in Tracks.Where(t => t.Song.Id == song.Id).ToList())
            Tracks.Remove(track);

        if (SelectedFile is not null && SelectedFile.Song.Id == song.Id)
            SelectedFile = null;

        RemoveBadMidiFileEntries(song.Path, false);

        NotifyFileErrorsChanged();
        ApplySort();
    }

    private void AddBadMidiFile(Song song, Exception exception)
    {
        if (BadMidiFiles.Any(b => string.Equals(b.Path, song.Path, StringComparison.OrdinalIgnoreCase)))
            return;

        foreach (var missingSong in MissingSongs.Where(s => string.Equals(s.Path, song.Path, StringComparison.OrdinalIgnoreCase)).ToList())
            MissingSongs.Remove(missingSong);

        BadMidiFiles.Add(new BadMidiFileEntry(song, exception.Message));

        NotifyFileErrorsChanged();
    }

    private void RemoveBadMidiFileEntries(string path, bool notify = true)
    {
        var removed = false;
        foreach (var badMidiSong in BadMidiFiles.Where(b => string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            BadMidiFiles.Remove(badMidiSong);
            removed = true;
        }

        if (removed && notify)
            NotifyFileErrorsChanged();
    }

    private void NotifyFileErrorsChanged()
    {
        NotifyOfPropertyChange(nameof(HasMissingSongs));
        NotifyOfPropertyChange(nameof(MissingSongs));
        NotifyOfPropertyChange(nameof(BadMidiFiles));
        NotifyOfPropertyChange(nameof(HasFileErrors));
    }

    public async Task ScanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        var midiFiles = Directory.GetFiles(folderPath, "*.mid", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folderPath, "*.midi", SearchOption.AllDirectories));

        await AddFiles(midiFiles);
    }

    private async Task<bool> AddFile(Song song, ReadingSettings? settings = null)
    {
        try
        {
            // Check if file already exists in library by path
            if (Tracks.Any(t => t.Song.Path == song.Path))
                return false;

            // Check if file already exists by hash (duplicate content)
            if (song.FileHash != null && Tracks.Any(t => t.Song.FileHash == song.FileHash))
                return false;

            // If song doesn't have a hash yet (migrated from old DB), compute it
            if (song.FileHash == null && File.Exists(song.Path))
            {
                song.FileHash = Song.ComputeFileHash(song.Path);

                // Check again for duplicates after computing hash
                if (song.FileHash != null && Tracks.Any(t => t.Song.FileHash == song.FileHash))
                    return false;
            }

            Tracks.Add(new(song, settings));

            RemoveBadMidiFileEntries(song.Path, false);
            NotifyFileErrorsChanged();

            return true;
        }
        catch (Exception e)
        {
            settings ??= new();
            if (await MidiReadDialogHandler.TryHandleAsync(e, settings, song.Path))
                return await AddFile(song, settings);

            AddBadMidiFile(song, e);

            return false;
        }
    }

    private async Task AddFile(string fileName)
    {
        // Check if file already exists by path
        if (Tracks.Any(t => t.Song.Path == fileName))
            return;

        // Compute hash for duplicate detection
        var fileHash = Song.ComputeFileHash(fileName);

        // Check if a file with the same hash already exists (duplicate content)
        if (fileHash != null)
        {
            var missingByHash = MissingSongs.FirstOrDefault(song => song.FileHash == fileHash);
            if (missingByHash != null)
            {
                await RestoreMissingSong(missingByHash, fileName, fileHash);
                return;
            }

            var existingByHash = Tracks.FirstOrDefault(t => t.Song.FileHash == fileHash);
            if (existingByHash != null)
            {
                // Show warning dialog about duplicate
                var dialog = DialogHelper.CreateDialog();
                dialog.Title = "Duplicate File Detected";
                dialog.Content = $"This MIDI file appears to be a duplicate of:\n\n" +
                              $"'{existingByHash.Song.Title ?? existingByHash.Song.Path}'\n\n" +
                              $"The existing file will be used and this duplicate will be ignored.";
                dialog.CloseButtonText = "OK";
                await dialog.ShowAsync();
                return;
            }
        }

        // Get default title from filename
        var defaultTitle = Path.GetFileNameWithoutExtension(fileName);

        // Add with defaults (no dialog)
        var song = new Song(fileName, _main.SongSettings.KeyOffset)
        {
            Title = defaultTitle,
            Transpose = Transpose.Ignore
        };

        var added = await AddFile(song);
        if (!added)
            return;

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Add(song);
        await db.SaveChangesAsync();
    }

    private async Task RestoreMissingSong(Song missingSong, string newPath, string fileHash)
    {
        var oldPath = missingSong.Path;
        var oldHash = missingSong.FileHash;

        missingSong.Path = newPath;
        missingSong.FileHash = fileHash;

        var restored = await AddFile(missingSong);
        if (!restored)
        {
            missingSong.Path = oldPath;
            missingSong.FileHash = oldHash;
            return;
        }

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Update(missingSong);
        await db.SaveChangesAsync();

        MissingSongs.Remove(missingSong);
        RemoveBadMidiFileEntries(missingSong.Path, false);
        NotifyFileErrorsChanged();
    }

    public async Task RemoveTrack()
    {
        if (SelectedFile is not null)
        {
            await using var db = _ioc.Get<LyreContext>();
            db.Songs.Remove(SelectedFile.Song);
            await db.SaveChangesAsync();

            Tracks.Remove(SelectedFile);
            ApplySort();
        }
    }

    public async Task ClearSongs()
    {
        await using var db = _ioc.Get<LyreContext>();
        foreach (var track in Tracks)
        {
            db.Songs.Remove(track.Song);
        }
        await db.SaveChangesAsync();

        Tracks.Clear();
        SortedTracks.Clear();
        SelectedFile = null;
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
            await _main.Playback.PlayPause();
        }
        else
        {
            // Otherwise, add to queue and play this song
            PlaySong(file);
            await _main.Playback.PlayPause();
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
        var filesToDelete = selectedFiles.Any() ? selectedFiles.ToList() : (SelectedFile != null ? new List<MidiFile> { SelectedFile } : new List<MidiFile>());
        if (filesToDelete.Count == 0) return;

        var songIdsToDelete = filesToDelete
            .Select(file => file.Song.Id)
            .Distinct()
            .ToList();

        if (songIdsToDelete.Count == 0) return;

        // If deleting the currently opened song, close playback first so no stale
        // per-song updates continue while deletion is in progress.
        if (QueueView.OpenedFile is not null && songIdsToDelete.Contains(QueueView.OpenedFile.Song.Id))
        {
            _main.Playback.CloseFile();
            QueueView.ClearSavedSong();
            _main.Playback.UpdateButtons();
        }

        RemoveSongsFromCollections(songIdsToDelete);

        await using var db = _ioc.Get<LyreContext>();

        var existingSongs = await db.Songs
            .Where(song => songIdsToDelete.Contains(song.Id))
            .ToListAsync();

        if (existingSongs.Count > 0)
        {
            db.Songs.RemoveRange(existingSongs);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another operation already removed one or more rows.
                // Collections are already updated, so this is safe to ignore.
            }
        }

        QueueView.OnQueueModified();
        ApplySort();
    }

    private void RemoveSongsFromCollections(IReadOnlyCollection<Guid> songIds)
    {
        foreach (var track in Tracks.Where(track => songIds.Contains(track.Song.Id)).ToList())
        {
            Tracks.Remove(track);
        }

        foreach (var queueTrack in QueueView.Tracks.Where(track => songIds.Contains(track.Song.Id)).ToList())
        {
            QueueView.Tracks.Remove(queueTrack);
        }

        if (SelectedFile is not null && songIds.Contains(SelectedFile.Song.Id))
        {
            SelectedFile = null;
        }

        if (QueueView.SelectedFile is not null && songIds.Contains(QueueView.SelectedFile.Song.Id))
        {
            QueueView.SelectedFile = null;
        }
    }

    public async Task EditSelected(IEnumerable<MidiFile> selectedFiles)
    {
        // Edit only works on single selection
        var filesList = selectedFiles.ToList();
        var file = filesList.Count == 1 ? filesList[0] : SelectedFile;
        if (file is null) return;

        // Get native BPM from MIDI file
        var nativeBpm = file.GetNativeBpm();

        var dialog = new ImportDialog(
            file.Song.Title ?? Path.GetFileNameWithoutExtension(file.Path),
            file.Song.Key,
            file.Song.Transpose ?? Transpose.Ignore,
            file.Song.Author,
            file.Song.Album,
            file.Song.DateAdded,
            nativeBpm,
            file.Song.Bpm,
            file.Song.MergeNotes,
            file.Song.MergeMilliseconds,
            file.Song.HoldNotes,
            file.Song.Speed);

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        // Update song properties
        file.Song.Title = string.IsNullOrWhiteSpace(dialog.SongTitle) ? Path.GetFileNameWithoutExtension(file.Path) : dialog.SongTitle;
        file.Song.Author = string.IsNullOrWhiteSpace(dialog.SongAuthor) ? null : dialog.SongAuthor;
        file.Song.Album = string.IsNullOrWhiteSpace(dialog.SongAlbum) ? null : dialog.SongAlbum;
        file.Song.DateAdded = dialog.SongDateAdded;
        file.Song.Key = dialog.SongKey;
        file.Song.Transpose = dialog.SongTranspose;
        file.Song.Bpm = dialog.SongBpm;
        file.Song.MergeNotes = dialog.SongMergeNotes;
        file.Song.MergeMilliseconds = dialog.SongMergeMilliseconds;
        file.Song.HoldNotes = dialog.SongHoldNotes;
        file.Song.Speed = dialog.SongSpeed;

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Update(file.Song);
        await db.SaveChangesAsync();

        // Refresh the display
        ApplySort();
    }

    public void AddAllToQueue()
    {
        foreach (var track in SortedTracks)
            _main.QueueView.AddFile(track);
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

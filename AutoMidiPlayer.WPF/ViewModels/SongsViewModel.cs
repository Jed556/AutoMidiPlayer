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
using AutoMidiPlayer.WPF.Errors;
using Melanchall.DryWetMidi.Core;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Stylet;
using StyletIoC;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.ViewModels;

public class SongsViewModel : Screen
{
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

    /// <summary>
    /// Whether there are any missing song files
    /// </summary>
    public bool HasMissingSongs => MissingSongs.Count > 0;

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
                continue;
            }
            await AddFile(file);
        }

        // Notify UI about missing songs status change
        NotifyOfPropertyChange(nameof(HasMissingSongs));
        NotifyOfPropertyChange(nameof(MissingSongs));

        ApplySort();
    }

    /// <summary>
    /// Show the missing files dialog with individual delete buttons
    /// </summary>
    public async Task ShowMissingFilesDialog()
    {
        if (MissingSongs.Count == 0) return;

        var content = new System.Windows.Controls.StackPanel { MinWidth = 400 };
        content.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");

        var headerText = new System.Windows.Controls.TextBlock
        {
            Text = $"The following {MissingSongs.Count} song(s) could not be found:",
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        headerText.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        content.Children.Add(headerText);

        var scrollViewer = new System.Windows.Controls.ScrollViewer
        {
            MaxHeight = 300,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled
        };
        scrollViewer.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlFillColorDefaultBrush");
        scrollViewer.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        var itemsControl = new System.Windows.Controls.ItemsControl
        {
            ItemsSource = MissingSongs.ToList()
        };

        // Create item template with delete button
        var template = new DataTemplate();

        var rowBorderFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
        rowBorderFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(10, 8, 10, 8));
        rowBorderFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        rowBorderFactory.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        var gridFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));

        var col1 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
        col1.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        var col2 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
        col2.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, GridLength.Auto);

        gridFactory.AppendChild(col1);
        gridFactory.AppendChild(col2);

        // Title text
        var textFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
        textFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
            new System.Windows.Data.Binding("Title") { FallbackValue = "Unknown" });
        textFactory.SetValue(System.Windows.Controls.TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        textFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        textFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 0);
        textFactory.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        gridFactory.AppendChild(textFactory);

        // Delete button
        var buttonFactory = new FrameworkElementFactory(typeof(Button));
        buttonFactory.SetValue(System.Windows.Controls.Button.ContentProperty, "✕");
        buttonFactory.SetResourceReference(FrameworkElement.StyleProperty, "GhostIconButton");
        buttonFactory.SetValue(System.Windows.Controls.Button.PaddingProperty, new Thickness(6, 2, 6, 2));
        buttonFactory.SetValue(System.Windows.Controls.Button.MarginProperty, new Thickness(8, 0, 0, 0));
        buttonFactory.SetValue(System.Windows.Controls.Button.ToolTipProperty, "Remove from database");
        buttonFactory.SetValue(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent);
        buttonFactory.SetValue(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0));
        buttonFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 1);
        buttonFactory.AddHandler(System.Windows.Controls.Button.ClickEvent,
            new RoutedEventHandler(async (s, e) =>
            {
                if (s is System.Windows.Controls.Button btn && btn.DataContext is Song song)
                {
                    await RemoveMissingSong(song);
                    // Refresh the list
                    itemsControl.ItemsSource = MissingSongs.ToList();
                }
            }));
        gridFactory.AppendChild(buttonFactory);

        rowBorderFactory.AppendChild(gridFactory);
        template.VisualTree = rowBorderFactory;
        itemsControl.ItemTemplate = template;

        scrollViewer.Content = itemsControl;

        content.Children.Add(scrollViewer);

        var dialog = DialogHelper.CreateDialog();
        dialog.Title = "Missing Files";
        dialog.Content = content;
        dialog.PrimaryButtonText = "Remove All";
        dialog.PrimaryButtonAppearance = ControlAppearance.Danger;
        dialog.CloseButtonText = "Close";

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await RemoveAllMissingSongs();
        }
    }

    /// <summary>
    /// Remove a single missing song from the database
    /// </summary>
    public async Task RemoveMissingSong(Song song)
    {
        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Remove(song);
        await db.SaveChangesAsync();

        MissingSongs.Remove(song);
        NotifyOfPropertyChange(nameof(HasMissingSongs));
        NotifyOfPropertyChange(nameof(MissingSongs));
    }

    /// <summary>
    /// Remove all missing songs from the database
    /// </summary>
    public async Task RemoveAllMissingSongs()
    {
        if (MissingSongs.Count == 0) return;

        await using var db = _ioc.Get<LyreContext>();
        foreach (var song in MissingSongs.ToList())
        {
            db.Songs.Remove(song);
        }
        await db.SaveChangesAsync();

        MissingSongs.Clear();
        NotifyOfPropertyChange(nameof(HasMissingSongs));
        NotifyOfPropertyChange(nameof(MissingSongs));
    }

    public async Task ScanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        var midiFiles = Directory.GetFiles(folderPath, "*.mid", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folderPath, "*.midi", SearchOption.AllDirectories));

        await AddFiles(midiFiles);
    }

    private async Task AddFile(Song song, ReadingSettings? settings = null)
    {
        try
        {
            // Check if file already exists in library by path
            if (Tracks.Any(t => t.Song.Path == song.Path))
                return;

            // Check if file already exists by hash (duplicate content)
            if (song.FileHash != null && Tracks.Any(t => t.Song.FileHash == song.FileHash))
                return;

            // If song doesn't have a hash yet (migrated from old DB), compute it
            if (song.FileHash == null && File.Exists(song.Path))
            {
                song.FileHash = Song.ComputeFileHash(song.Path);

                // Check again for duplicates after computing hash
                if (song.FileHash != null && Tracks.Any(t => t.Song.FileHash == song.FileHash))
                    return;
            }

            Tracks.Add(new(song, settings));
        }
        catch (Exception e)
        {
            settings ??= new();
            if (await ExceptionHandler.TryHandleException(e, settings))
                await AddFile(song, settings);
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

        await AddFile(song);

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Add(song);
        await db.SaveChangesAsync();
    }

    private async Task RestoreMissingSong(Song missingSong, string newPath, string fileHash)
    {
        missingSong.Path = newPath;
        missingSong.FileHash = fileHash;

        await AddFile(missingSong);

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Update(missingSong);
        await db.SaveChangesAsync();

        MissingSongs.Remove(missingSong);
        NotifyOfPropertyChange(nameof(HasMissingSongs));
        NotifyOfPropertyChange(nameof(MissingSongs));
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
        var files = selectedFiles.Any() ? selectedFiles : (SelectedFile != null ? new[] { SelectedFile } : Array.Empty<MidiFile>());
        foreach (var file in files)
            _main.QueueView.AddFile(file);
    }

    public async Task DeleteSelected(IEnumerable<MidiFile> selectedFiles)
    {
        var filesToDelete = selectedFiles.Any() ? selectedFiles.ToList() : (SelectedFile != null ? new List<MidiFile> { SelectedFile } : new List<MidiFile>());
        if (filesToDelete.Count == 0) return;

        await using var db = _ioc.Get<LyreContext>();
        foreach (var file in filesToDelete)
        {
            db.Songs.Remove(file.Song);
            Tracks.Remove(file);
            _main.QueueView.Tracks.Remove(file);
        }
        await db.SaveChangesAsync();
        ApplySort();
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

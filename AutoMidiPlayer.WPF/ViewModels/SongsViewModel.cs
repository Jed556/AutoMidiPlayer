using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.ModernWPF;
using AutoMidiPlayer.WPF.ModernWPF.Errors;
using Melanchall.DryWetMidi.Core;
using Microsoft.Win32;
using ModernWpf.Controls;
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

        // Forward IsPlaying changes from TrackView so bindings update
        _main.TrackView.PropertyChanged += OnTrackViewPropertyChanged;
    }

    private void OnTrackViewPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrackViewModel.IsPlaying))
        {
            // Notify that TrackView changed so bindings to TrackView.IsPlaying re-evaluate
            // Use Dispatcher to avoid collection enumeration issues
            System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                NotifyOfPropertyChange(() => TrackView);
            });
        }
    }

    public QueueViewModel QueueView => _main.QueueView;

    public TrackViewModel TrackView => _main.TrackView;

    public BindableCollection<MidiFile> Tracks { get; } = new();

    public BindableCollection<MidiFile> SortedTracks { get; private set; } = new();

    public MidiFile? SelectedFile { get; set; }

    public BindableCollection<MidiFile> SelectedFiles { get; } = new();

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

    /// <summary>
    /// Gets the currently selected files (multi-select or single select fallback)
    /// </summary>
    private List<MidiFile> GetSelectedFiles() =>
        SelectedFiles.Count > 0
            ? SelectedFiles.ToList()
            : (SelectedFile != null ? new List<MidiFile> { SelectedFile } : new List<MidiFile>());

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
        var missingSongs = new List<Song>();

        foreach (var file in files)
        {
            // Check if file exists before trying to add
            if (!File.Exists(file.Path))
            {
                missingSongs.Add(file);
                continue;
            }
            await AddFile(file);
        }

        // Show single dialog for all missing files
        if (missingSongs.Count > 0)
        {
            await ShowMissingFilesDialog(missingSongs);
        }

        ApplySort();
    }

    private async Task ShowMissingFilesDialog(List<Song> missingSongs)
    {
        var fileList = string.Join("\n", missingSongs.Select(s => $"• {s.Title ?? s.Path}"));
        var content = new System.Windows.Controls.StackPanel();

        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = $"The following {missingSongs.Count} song(s) could not be found:",
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var scrollViewer = new System.Windows.Controls.ScrollViewer
        {
            MaxHeight = 200,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = fileList,
                TextWrapping = System.Windows.TextWrapping.Wrap
            }
        };
        content.Children.Add(scrollViewer);

        content.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "\nWould you like to remove them from the database?",
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var dialog = new ContentDialog
        {
            Title = "Missing Files",
            Content = content,
            PrimaryButtonText = "Remove from Database",
            CloseButtonText = "Keep in Database"
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            await using var db = _ioc.Get<LyreContext>();
            foreach (var song in missingSongs)
            {
                db.Songs.Remove(song);
            }
            await db.SaveChangesAsync();
        }
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
            var existingByHash = Tracks.FirstOrDefault(t => t.Song.FileHash == fileHash);
            if (existingByHash != null)
            {
                // Show warning dialog about duplicate
                var dialog = new ContentDialog
                {
                    Title = "Duplicate File Detected",
                    Content = $"This MIDI file appears to be a duplicate of:\n\n" +
                              $"'{existingByHash.Song.Title ?? existingByHash.Song.Path}'\n\n" +
                              $"The existing file will be used and this duplicate will be ignored.",
                    CloseButtonText = "OK"
                };
                await dialog.ShowAsync();
                return;
            }
        }

        // Get default title from filename
        var defaultTitle = Path.GetFileNameWithoutExtension(fileName);

        // Add with defaults (no dialog)
        var song = new Song(fileName, _main.SettingsView.KeyOffset)
        {
            Title = defaultTitle,
            Transpose = Transpose.Ignore
        };

        await AddFile(song);

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Add(song);
        await db.SaveChangesAsync();
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
            await TrackView.PlayPause();
        }
        else
        {
            // Otherwise, add to queue and play this song
            PlaySong(file);
            await TrackView.PlayPause();
        }
    }

    public void AddSelectedToQueue()
    {
        foreach (var file in GetSelectedFiles())
            _main.QueueView.AddFile(file);
    }

    public async Task DeleteSelected()
    {
        var filesToDelete = GetSelectedFiles();
        if (filesToDelete.Count == 0) return;

        await using var db = _ioc.Get<LyreContext>();
        foreach (var file in filesToDelete)
        {
            db.Songs.Remove(file.Song);
            Tracks.Remove(file);
            _main.QueueView.Tracks.Remove(file);
        }
        await db.SaveChangesAsync();
        SelectedFiles.Clear();
        ApplySort();
    }

    public async Task EditSelected()
    {
        // Edit only works on single selection
        var file = SelectedFiles.Count == 1 ? SelectedFiles[0] : SelectedFile;
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
            file.Song.HoldNotes);

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

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Update(file.Song);
        await db.SaveChangesAsync();

        // Refresh the display
        ApplySort();
    }

    public void AddToQueue()
    {
        AddSelectedToQueue();
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

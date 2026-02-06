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
using AutoMidiPlayer.WPF.ModernWPF;
using AutoMidiPlayer.WPF.ModernWPF.Errors;
using Melanchall.DryWetMidi.Core;
using PropertyChanged;
using Stylet;
using StyletIoC;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;
using AutoMidiPlayer.Data.Notification;
using AutoMidiPlayer.WPF.Core;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.ViewModels;

public class QueueViewModel : Screen, IHandle<AccentColorChangedNotification>
{
    public enum LoopMode
    {
        Off,    // Stop when queue finishes
        Queue,  // Loop back to first song when queue finishes
        Track   // Loop current song
    }

    private static readonly Settings Settings = Settings.Default;
    private readonly IContainer _ioc;
    private readonly IEventAggregator _events;
    private readonly MainWindowViewModel _main;

    public QueueViewModel(IContainer ioc, MainWindowViewModel main)
    {
        _ioc = ioc;
        _events = ioc.Get<IEventAggregator>();
        _main = main;

        // Load saved queue settings
        Shuffle = Settings.QueueShuffle;
        Loop = (LoopMode)Settings.QueueLoopMode;

        // Forward IsPlaying changes from Playback so bindings update
        _main.Playback.PlaybackStateChanged += HandlePlaybackStateChanged;

        // Subscribe to accent color changes
        _events.Subscribe(this);
    }

    public void Handle(AccentColorChangedNotification notification)
    {
        // Refresh color properties when accent color changes
        NotifyOfPropertyChange(() => ShuffleStateColor);
        NotifyOfPropertyChange(() => LoopStateColor);
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

    private BindableCollection<MidiFile> _filteredTracks = new();
    public BindableCollection<MidiFile> FilteredTracks
    {
        get => _filteredTracks;
        private set => SetAndNotify(ref _filteredTracks, value);
    }

    public BindableCollection<MidiFile> Tracks { get; } = new();

    public bool Shuffle { get; set; }

    public IEnumerable<string> TrackTitles => Tracks.Select(t => t.Title);

    public LoopMode Loop { get; set; } = LoopMode.Queue;

    public MidiFile? OpenedFile { get; set; }

    public MidiFile? SelectedFile { get; set; }

    public TrackViewModel TrackView => _main.TrackView;

    public Services.PlaybackService Playback => _main.Playback;

    public SolidColorBrush ShuffleStateColor => Shuffle
        ? new SolidColorBrush(AccentColorHelper.GetAccentColor())
        : Brushes.Gray;

    public Stack<MidiFile> History { get; } = new();

    public string LoopStateString =>
        Loop switch
        {
            LoopMode.Off => "\xF5E7",    // No repeat icon
            LoopMode.Queue => "\xE8EE",  // Repeat all icon
            LoopMode.Track => "\xE8ED",  // Repeat one icon
            _ => string.Empty
        };

    public string LoopSvgSource =>
        Loop switch
        {
            LoopMode.Off => "/Icons/Controls/Repeat.svg",
            LoopMode.Queue => "/Icons/Controls/Repeat.svg",
            LoopMode.Track => "/Icons/Controls/Repeat One.svg",
            _ => "/Icons/Controls/Repeat.svg"
        };

    public Geometry LoopGeometry =>
        Loop switch
        {
            LoopMode.Track => (Geometry)Application.Current.FindResource("RepeatOneIconGeometry"),
            _ => (Geometry)Application.Current.FindResource("RepeatIconGeometry")
        };

    public SolidColorBrush LoopStateColor =>
        Loop switch
        {
            LoopMode.Off => Brushes.Gray,
            _ => new SolidColorBrush(AccentColorHelper.GetAccentColor())
        };

    public string LoopTooltip =>
        Loop switch
        {
            LoopMode.Off => "Loop: Off",
            LoopMode.Queue => "Loop: Queue",
            LoopMode.Track => "Loop: Track",
            _ => "Loop"
        };

    public string? FilterText { get; set; }

    private BindableCollection<MidiFile> ShuffledTracks { get; set; } = new();

    public BindableCollection<MidiFile> GetPlaylist() => Shuffle ? ShuffledTracks : Tracks;

    /// <summary>
    /// Get the next song to play.
    /// </summary>
    /// <param name="userInitiated">True if user clicked Next, false if auto-triggered by song finish</param>
    /// <returns>The next song to play, or null if none</returns>
    public MidiFile? Next(bool userInitiated = true)
    {
        var playlist = GetPlaylist().ToList();
        if (OpenedFile is null) return playlist.FirstOrDefault();

        switch (Loop)
        {
            case LoopMode.Off:
                // Off mode: play through queue once, stop at end
                break; // Fall through to get next song (returns null at end)
            case LoopMode.Track:
                // Track loop mode:
                // - If song finished naturally (not user initiated), loop same song
                // - If user clicked Next, go to next song (which will then loop when it finishes)
                if (!userInitiated)
                    return OpenedFile;
                break; // Fall through to get next song
            case LoopMode.Queue:
                // Queue loop: wrap to first song when reaching end
                var nextIndex = playlist.IndexOf(OpenedFile) + 1;
                return playlist.ElementAtOrDefault(nextIndex % playlist.Count);
        }

        var next = playlist.IndexOf(OpenedFile) + 1;
        return playlist.ElementAtOrDefault(next);
    }

    public void AddFiles(IEnumerable<MidiFile> files)
    {
        foreach (var file in files)
        {
            if (!Tracks.Contains(file))
                Tracks.Add(file);
        }

        ShuffledTracks = new(Tracks.OrderBy(_ => Guid.NewGuid()));
        OnQueueModified();

        var next = Next();
        if (OpenedFile is null && Tracks.Count > 0 && next is not null)
            _events.Publish(Next());
    }

    public void AddFile(MidiFile file)
    {
        if (!Tracks.Contains(file))
        {
            Tracks.Add(file);
            ShuffledTracks = new(Tracks.OrderBy(_ => Guid.NewGuid()));
            OnQueueModified();
        }
    }

    public void PlayFromQueue(MidiFile? file)
    {
        if (file is not null)
        {
            _events.Publish(file);
        }
    }

    public async void PlayPauseFromQueue(MidiFile? file)
    {
        if (file is null) return;

        // If this is the currently opened file, toggle play/pause
        if (OpenedFile == file)
        {
            await _main.Playback.PlayPause();
        }
        else
        {
            // Otherwise, load and play this song
            _events.Publish(file);
            await _main.Playback.PlayPause();
        }
    }

    public void ClearQueue()
    {
        Tracks.Clear();
        History.Clear();

        OpenedFile = null;
        SelectedFile = null;
        SaveQueue();
        ApplyFilter();
    }

    public void RemoveTrack(IEnumerable<MidiFile> selectedFiles)
    {
        // Get files to remove - either multi-select or single select
        var filesToRemove = selectedFiles.Any()
            ? selectedFiles.ToList()
            : (SelectedFile is not null ? new List<MidiFile> { SelectedFile } : new List<MidiFile>());

        foreach (var file in filesToRemove)
        {
            if (OpenedFile == file)
                OpenedFile = null;
            Tracks.Remove(file);
        }

        if (filesToRemove.Count > 0)
        {
            OnQueueModified();
        }
    }

    public async Task DeleteSongs(IEnumerable<MidiFile> selectedFiles)
    {
        var filesToDelete = selectedFiles.Any()
            ? selectedFiles.ToList()
            : (SelectedFile is not null ? new List<MidiFile> { SelectedFile } : new List<MidiFile>());

        if (filesToDelete.Count == 0) return;

        await using var db = _ioc.Get<LyreContext>();
        foreach (var file in filesToDelete)
        {
            db.Songs.Remove(file.Song);
            Tracks.Remove(file);
            _main.SongsView.Tracks.Remove(file);
        }
        await db.SaveChangesAsync();
        OnQueueModified();
    }

    public async Task EditSong(MidiFile file)
    {
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
    }

    public void MoveUp()
    {
        if (SelectedFile is null) return;

        var index = Tracks.IndexOf(SelectedFile);
        if (index > 0)
        {
            Tracks.Move(index, index - 1);
            OnQueueModified();
        }
    }

    public void MoveDown()
    {
        if (SelectedFile is null) return;

        var index = Tracks.IndexOf(SelectedFile);
        if (index < Tracks.Count - 1)
        {
            Tracks.Move(index, index + 1);
            OnQueueModified();
        }
    }

    [SuppressPropertyChangedWarnings]
    public void OnFileChanged(object sender, EventArgs e)
    {
        if (SelectedFile is not null)
            _events.Publish(SelectedFile);
    }

    public void OnOpenedFileChanged()
    {
        if (OpenedFile is null) return;

    }

    public void Previous()
    {
        History.Pop();
        _events.Publish(History.Pop());
    }

    public void ToggleLoop()
    {
        var loopState = (int)Loop;
        var loopStates = Enum.GetValues(typeof(LoopMode)).Length;

        var newState = (loopState + 1) % loopStates;
        Loop = (LoopMode)newState;

        // Save to settings
        Settings.QueueLoopMode = (int)Loop;
        Settings.Save();
    }

    public void ToggleShuffle()
    {
        Shuffle = !Shuffle;

        if (Shuffle)
            ShuffledTracks = new(Tracks.OrderBy(_ => Guid.NewGuid()));

        // Save to settings
        Settings.QueueShuffle = Shuffle;
        Settings.Save();

        RefreshQueue();
    }

    /// <summary>
    /// Save queue song IDs to settings
    /// </summary>
    public void SaveQueue()
    {
        var songIds = Tracks.Select(t => t.Song.Id.ToString());
        Settings.QueueSongIds = string.Join(",", songIds);
        Settings.Save();
    }

    /// <summary>
    /// Restore queue from saved song IDs
    /// </summary>
    public void RestoreQueue(IEnumerable<MidiFile> availableTracks)
    {
        if (string.IsNullOrEmpty(Settings.QueueSongIds)) return;

        var savedIds = Settings.QueueSongIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();

        var trackDict = availableTracks.ToDictionary(t => t.Song.Id);

        foreach (var id in savedIds)
        {
            if (trackDict.TryGetValue(id, out var track) && !Tracks.Contains(track))
            {
                Tracks.Add(track);
            }
        }

        if (Shuffle)
            ShuffledTracks = new(Tracks.OrderBy(_ => Guid.NewGuid()));

        RefreshQueue();
        ApplyFilter();
    }

    /// <summary>
    /// Save the currently playing song ID and position
    /// </summary>
    public void SaveCurrentSong(double positionSeconds)
    {
        if (OpenedFile is not null)
        {
            Settings.CurrentSongId = OpenedFile.Song.Id.ToString();
            Settings.CurrentSongPosition = positionSeconds;
        }
        else
        {
            Settings.CurrentSongId = string.Empty;
            Settings.CurrentSongPosition = 0;
        }
        Settings.Save();
    }

    /// <summary>
    /// Restore the previously playing song from saved state
    /// Returns the position in seconds to seek to, or null if no song to restore
    /// </summary>
    public double? RestoreCurrentSong(IEnumerable<MidiFile> availableTracks)
    {
        if (string.IsNullOrEmpty(Settings.CurrentSongId)) return null;

        if (!Guid.TryParse(Settings.CurrentSongId, out var savedId))
        {
            ClearSavedSong();
            return null;
        }

        var track = availableTracks.FirstOrDefault(t => t.Song.Id == savedId);
        if (track is null)
        {
            // Song no longer exists, clear persistence
            ClearSavedSong();
            return null;
        }

        // Make sure the song is in the queue
        if (!Tracks.Contains(track))
        {
            Tracks.Insert(0, track);
            RefreshQueue();
        }

        OpenedFile = track;
        _events.Publish(track);

        return Settings.CurrentSongPosition;
    }

    /// <summary>
    /// Clear the saved song persistence
    /// </summary>
    public void ClearSavedSong()
    {
        Settings.CurrentSongId = string.Empty;
        Settings.CurrentSongPosition = 0;
        Settings.Save();
    }

    /// <summary>
    /// Called when queue is modified to auto-save
    /// </summary>
    public void OnQueueModified()
    {
        SaveQueue();
        RefreshQueue();
        ApplyFilter();
    }

    private void RefreshQueue()
    {
        var playlist = GetPlaylist();
        foreach (var file in playlist)
        {
            file.Position = playlist.IndexOf(file);
        }
    }

    /// <summary>
    /// Apply filter to create a new FilteredTracks collection
    /// </summary>
    public void ApplyFilter()
    {
        IEnumerable<MidiFile> filtered = string.IsNullOrWhiteSpace(FilterText)
            ? Tracks
            : Tracks.Where(t => t.Title.Contains(FilterText, StringComparison.OrdinalIgnoreCase));

        FilteredTracks = new BindableCollection<MidiFile>(filtered);
    }

    /// <summary>
    /// Called by PropertyChanged.Fody when FilterText changes
    /// </summary>
    private void OnFilterTextChanged() => ApplyFilter();

    /// <summary>
    /// Refresh the currently playing song in the list to reflect property changes
    /// </summary>
    public void RefreshCurrentSong()
    {
        // Force UI to refresh by recreating the filtered collection
        // This ensures the ListView re-renders items when Song properties change
        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            ApplyFilter();
        });
    }

    protected override void OnDeactivate()
    {
        base.OnDeactivate();
        // Clear the semi-active (single-clicked) row when switching tabs
        SelectedFile = null;
    }
}

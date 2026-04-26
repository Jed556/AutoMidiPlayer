using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.WPF.Dialogs;
using AutoMidiPlayer.WPF.ViewModels;
using Microsoft.EntityFrameworkCore;
using Stylet;
using StyletIoC;
using Wpf.Ui.Controls;
using static AutoMidiPlayer.Data.Entities.Transpose;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.Services;

/// <summary>
/// Central service for song operations: per-song settings (key, speed, transpose),
/// editing, deleting, and persistence.
/// </summary>
public class SongService(IContainer ioc) : PropertyChangedBase
{
    private readonly IContainer _ioc = ioc;
    private readonly IEventAggregator _events = ioc.Get<IEventAggregator>();
    private MainWindowViewModel? _main;

    private int _keyOffset;
    private double _speed = 1.0;
    private MusicConstants.KeyOption? _selectedKeyOption;
    private MusicConstants.SpeedOption? _selectedSpeedOption;
    private bool _suppressSongPersistenceAndEvents;

    #region Static Data

    public static Dictionary<Transpose, string> TransposeNames => MusicConstants.TransposeNames;

    public Dictionary<int, string> KeyOffsets => MusicConstants.KeyOffsets;

    #endregion

    #region Options (for ComboBox binding)

    public List<MusicConstants.KeyOption> KeyOptions { get; private set; } = MusicConstants.GenerateKeyOptions();

    public List<MusicConstants.SpeedOption> SpeedOptions { get; } = MusicConstants.GenerateSpeedOptions();

    #endregion

    #region Current File

    /// <summary>
    /// The currently loaded file. Set by PlaybackService when a new song is opened.
    /// </summary>
    public MidiFile? CurrentFile { get; set; }

    #endregion

    #region Properties

    public int KeyOffset
    {
        get => _keyOffset;
        set
        {
            var minKeyOffset = MusicConstants.GetRelativeMinKeyOffset(CurrentFile?.Song.BaseKey);
            var maxKeyOffset = MusicConstants.GetRelativeMaxKeyOffset(CurrentFile?.Song.BaseKey);

            if (SetAndNotify(ref _keyOffset, Math.Clamp(value, minKeyOffset, maxKeyOffset)))
            {
                _selectedKeyOption = KeyOptions.FirstOrDefault(k => k.Value == _keyOffset);
                NotifyOfPropertyChange(nameof(SelectedKeyOption));
                NotifyOfPropertyChange(nameof(KeyDisplayText));
                NotifyOfPropertyChange(nameof(EffectiveKeyOffset));

                // Persist + notify for playback rebuild
                SaveCurrentSongKey();
            }
        }
    }

    public MusicConstants.KeyOption? SelectedKeyOption
    {
        get => _selectedKeyOption ??= KeyOptions.FirstOrDefault(k => k.Value == KeyOffset);
        set
        {
            if (value != null && SetAndNotify(ref _selectedKeyOption, value))
                KeyOffset = value.Value;
        }
    }

    public double Speed
    {
        get => _speed;
        set
        {
            if (SetAndNotify(ref _speed, Math.Round(Math.Clamp(value, 0.1, 4.0), 1)))
            {
                _selectedSpeedOption = SpeedOptions.FirstOrDefault(s => Math.Abs(s.Value - _speed) < 0.01)
                    ?? SpeedOptions.First(s => s.Value == 1.0);
                NotifyOfPropertyChange(nameof(SelectedSpeedOption));
                NotifyOfPropertyChange(nameof(SpeedDisplayText));
                NotifyOfPropertyChange(nameof(IsDefaultSpeed));
                NotifyOfPropertyChange(nameof(IsSpeedActive));

                if (!_suppressSongPersistenceAndEvents)
                {
                    // Notify PlaybackService to update live playback speed
                    SpeedChanged?.Invoke(_speed);

                    // Persist to current song
                    SaveCurrentSongSpeed();
                }
            }
        }
    }

    public MusicConstants.SpeedOption? SelectedSpeedOption
    {
        get => _selectedSpeedOption ??= SpeedOptions.FirstOrDefault(s => Math.Abs(s.Value - Speed) < 0.01)
            ?? SpeedOptions.First(s => s.Value == 1.0);
        set
        {
            if (value != null && SetAndNotify(ref _selectedSpeedOption, value))
                Speed = value.Value;
        }
    }

    public KeyValuePair<Transpose, string>? Transpose { get; set; }

    public int EffectiveKeyOffset => GetEffectiveKeyOffset();

    public string KeyDisplayText => MusicConstants.GetNoteName(EffectiveKeyOffset);

    public string SpeedDisplayText => $"{Speed:0.##}x";

    public bool IsDefaultSpeed => Math.Abs(Speed - 1.0) < 0.01;

    public bool IsSpeedActive => !IsDefaultSpeed;

    public string TransposeDisplayText => Transpose?.Value ?? MusicConstants.TransposeNames[Ignore];

    public Transpose TransposeMode => Transpose?.Key ?? Ignore;

    public bool IsTransposeActive => TransposeMode != Ignore;

    #endregion

    #region Events

    /// <summary>
    /// Fired when speed changes so PlaybackService can update Playback.Speed
    /// without a full rebuild.
    /// </summary>
    public event Action<double>? SpeedChanged;

    /// <summary>
    /// Fired when key offset or transpose changes, requiring a playback rebuild.
    /// </summary>
    public event Action? SettingsRebuildRequired;

    #endregion

    #region Methods

    public void IncreaseSpeed() => Speed = Math.Round(Speed + 0.1, 1);

    public void DecreaseSpeed() => Speed = Math.Round(Speed - 0.1, 1);

    /// <summary>
    /// Apply per-song settings (key, speed, transpose) when a new song is loaded.
    /// </summary>
    public void ApplyPerSongSettings(MidiFile file)
    {
        CurrentFile = file;
        UpdateKeyOptionsForCurrentSong();

        // Speed: per-song or default 1.0
        Speed = file.Song.Speed ?? 1.0;

        // Key offset: always from song
        KeyOffset = file.Song.Key;

        // Transpose: from song or null
        var transpose = TransposeNames
            .FirstOrDefault(e => e.Key == file.Song.Transpose);
        Transpose = file.Song.Transpose is not null ? transpose : null;

        NotifyOfPropertyChange(nameof(TransposeDisplayText));
    }

    /// <summary>
    /// Clear settings when file is closed.
    /// </summary>
    public void ClearSettings()
    {
        CurrentFile = null;
        UpdateKeyOptionsForCurrentSong();
        Transpose = null;
        NotifyOfPropertyChange(nameof(TransposeDisplayText));
    }

    /// <summary>
    /// Synchronizes in-memory song settings for the currently opened song after an edit dialog save,
    /// without triggering persistence writes or extra rebuild events.
    /// </summary>
    public void SyncFromEditedSong(Song song)
    {
        if (CurrentFile is null || CurrentFile.Song.Id != song.Id)
            return;

        _suppressSongPersistenceAndEvents = true;
        try
        {
            if (CurrentFile is not null)
                CurrentFile.Song.BaseKey = song.BaseKey;

            UpdateKeyOptionsForCurrentSong();
            Speed = song.Speed ?? 1.0;
            KeyOffset = song.Key;

            var transpose = TransposeNames
                .FirstOrDefault(e => e.Key == song.Transpose);
            Transpose = song.Transpose is not null ? transpose : null;
        }
        finally
        {
            _suppressSongPersistenceAndEvents = false;
        }

        NotifyOfPropertyChange(nameof(SelectedKeyOption));
        NotifyOfPropertyChange(nameof(SelectedSpeedOption));
        NotifyOfPropertyChange(nameof(TransposeDisplayText));
        NotifyOfPropertyChange(nameof(TransposeMode));
        NotifyOfPropertyChange(nameof(EffectiveKeyOffset));
        NotifyOfPropertyChange(nameof(IsSpeedActive));
        NotifyOfPropertyChange(nameof(IsTransposeActive));
    }

    /// <summary>
    /// Gets the effective key offset used by playback and note conversion.
    /// </summary>
    public int GetEffectiveKeyOffset(Song? song = null)
    {
        if (song is not null)
            return MusicConstants.GetEffectiveKeyOffset(song.Key, song.BaseKey);

        return MusicConstants.GetEffectiveKeyOffset(KeyOffset, CurrentFile?.Song.BaseKey);
    }

    private void UpdateKeyOptionsForCurrentSong()
    {
        var baseKeyOffset = CurrentFile?.Song.BaseKey;
        KeyOptions = MusicConstants.GenerateKeyOptions(baseKeyOffset);

        _selectedKeyOption = KeyOptions.FirstOrDefault(k => k.Value == _keyOffset)
                             ?? KeyOptions.FirstOrDefault();

        NotifyOfPropertyChange(nameof(KeyOptions));
        NotifyOfPropertyChange(nameof(SelectedKeyOption));
        NotifyOfPropertyChange(nameof(KeyDisplayText));
        NotifyOfPropertyChange(nameof(EffectiveKeyOffset));
    }

    #endregion

    #region Persistence

    private async void SaveCurrentSongKey()
    {
        if (_suppressSongPersistenceAndEvents || CurrentFile is null) return;
        CurrentFile.Song.Key = KeyOffset;
        await SaveSongAsync(CurrentFile.Song);

        // Key change requires playback rebuild
        SettingsRebuildRequired?.Invoke();
    }

    private async void SaveCurrentSongSpeed()
    {
        if (_suppressSongPersistenceAndEvents || CurrentFile is null) return;
        CurrentFile.Song.Speed = _speed;
        await SaveSongAsync(CurrentFile.Song);
    }

    // Called by Fody when Transpose property changes
    private void OnTransposeChanged()
    {
        NotifyOfPropertyChange(nameof(TransposeDisplayText));
        NotifyOfPropertyChange(nameof(TransposeMode));
        NotifyOfPropertyChange(nameof(IsTransposeActive));

        if (_suppressSongPersistenceAndEvents) return;
        if (CurrentFile is null) return;
        CurrentFile.Song.Transpose = Transpose?.Key;
        _ = SaveSongAsync(CurrentFile.Song);

        // Transpose change requires playback rebuild
        SettingsRebuildRequired?.Invoke();
    }

    private async Task SaveSongAsync(Song song)
    {
        try
        {
            await using var db = _ioc.Get<PlayerContext>();
            db.Songs.Update(song);
            await db.SaveChangesAsync();
        }
        catch { /* Ignore save errors */ }
    }

    #endregion

    #region Late Initialization

    /// <summary>
    /// Called by MainWindowViewModel after construction to provide the back-reference
    /// needed for cross-ViewModel operations (edit, delete).
    /// </summary>
    public void SetMain(MainWindowViewModel main) => _main = main;

    #endregion

    #region Song Operations

    /// <summary>
    /// Show the edit dialog for a song and persist changes.
    /// Shared by both Queue and Songs views.
    /// </summary>
    public async Task EditSongAsync(MidiFile file, string source = "unknown")
    {
        if (_main is null) return;

        var currentTitle = file.Song.Title ?? Path.GetFileNameWithoutExtension(file.Path);
        Logger.LogStep("SONG_EDIT_DIALOG_OPEN", $"source={source} | title='{currentTitle}' | path='{file.Path}'");

        var nativeBpm = file.GetNativeBpm();

        var dialog = new EditDialog(
            file.Song.Title ?? Path.GetFileNameWithoutExtension(file.Path),
            file.Path,
            file.Song.Key,
            file.Song.BaseKey,
            file.Song.Transpose ?? Data.Entities.Transpose.Ignore,
            file.Song.Artist,
            file.Song.Album,
            file.Song.DateAdded,
            nativeBpm,
            file.Song.Bpm,
            file.Song.MergeNotes,
            file.Song.MergeMilliseconds,
            file.Song.HoldNotes,
            file.Song.Speed);

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            Logger.LogStep("SONG_EDIT_DIALOG_CANCEL", $"source={source} | result={result} | title='{currentTitle}' | path='{file.Path}'");
            return;
        }

        var updatedTitle = string.IsNullOrWhiteSpace(dialog.SongTitle)
            ? Path.GetFileNameWithoutExtension(file.Path)
            : dialog.SongTitle;

        Logger.LogStep(
            "SONG_EDIT_DIALOG_SAVE",
            $"source={source} | oldTitle='{currentTitle}' | newTitle='{updatedTitle}' | path='{file.Path}' | key={dialog.SongKey} | transpose={dialog.SongTranspose} | bpm={dialog.SongBpm:0.###} | speed={dialog.SongSpeed:0.###}");

        file.Song.Title = updatedTitle;
        file.Song.Artist = string.IsNullOrWhiteSpace(dialog.SongArtist) ? null : dialog.SongArtist;
        file.Song.Album = string.IsNullOrWhiteSpace(dialog.SongAlbum) ? null : dialog.SongAlbum;
        file.Song.DateAdded = dialog.SongDateAdded;
        // Preserve the existing song-specific base key unless the dialog provides a detected value.
        file.Song.BaseKey = dialog.SongBaseKey ?? file.Song.BaseKey;
        file.Song.Key = dialog.SongKey;
        file.Song.Transpose = dialog.SongTranspose;
        file.Song.Bpm = dialog.SongBpm;
        file.Song.MergeNotes = dialog.SongMergeNotes;
        file.Song.MergeMilliseconds = dialog.SongMergeMilliseconds;
        file.Song.HoldNotes = dialog.SongHoldNotes;
        file.Song.Speed = dialog.SongSpeed;

        await using var db = _ioc.Get<PlayerContext>();
        db.Songs.Update(file.Song);
        await db.SaveChangesAsync();

        Logger.LogStep("SONG_EDIT_DIALOG_SAVE_COMPLETED", $"source={source} | title='{updatedTitle}' | songId={file.Song.Id}");

        if (_main.QueueView.OpenedFile?.Song.Id == file.Song.Id)
        {
            // Queue/Songs entries can point to different MidiFile/Song instances for the same Id.
            // Keep the currently opened song instance in sync so bound Instrument toggles update immediately.
            var openedSong = _main.QueueView.OpenedFile.Song;
            if (!ReferenceEquals(openedSong, file.Song))
                CopyEditableSongFields(file.Song, openedSong);

            SyncFromEditedSong(openedSong);
            _main.InstrumentView.UpdateFromCurrentSong();
            await _main.PlaybackEngine.RefreshCurrentSongRealtimeAsync();
        }

        _main.SongsView.ApplySort();
        _main.QueueView.ApplyFilter();

        Logger.LogStep("SONG_EDIT_DIALOG_APPLIED", $"source={source} | title='{updatedTitle}'");
    }

    private static void CopyEditableSongFields(Song source, Song target)
    {
        target.Title = source.Title;
        target.Artist = source.Artist;
        target.Album = source.Album;
        target.DateAdded = source.DateAdded;
        target.BaseKey = source.BaseKey;
        target.Key = source.Key;
        target.Transpose = source.Transpose;
        target.Bpm = source.Bpm;
        target.MergeNotes = source.MergeNotes;
        target.MergeMilliseconds = source.MergeMilliseconds;
        target.HoldNotes = source.HoldNotes;
        target.Speed = source.Speed;
    }

    /// <summary>
    /// Delete songs from the database and remove from all collections.
    /// Shared by both Queue and Songs views.
    /// </summary>
    public async Task DeleteSongsAsync(IEnumerable<MidiFile> filesToDelete)
    {
        if (_main is null) return;

        var files = filesToDelete.ToList();
        if (files.Count == 0) return;

        var songIdsToDelete = files
            .Select(file => file.Song.Id)
            .Distinct()
            .ToList();

        if (songIdsToDelete.Count == 0) return;

        var midiFolder = _main.SettingsView.MidiFolder;

        foreach (var file in files)
        {
            var songPath = file.Song.Path;
            if (string.IsNullOrWhiteSpace(songPath))
                continue;

            if (!AutoImportExclusionStore.IsMidiFilePath(songPath))
                continue;

            if (!AutoImportExclusionStore.IsPathWithinFolder(songPath, midiFolder))
                continue;

            if (!File.Exists(songPath))
                continue;

            AutoImportExclusionStore.Add(songPath);
        }

        if (_main.QueueView.OpenedFile is not null &&
            songIdsToDelete.Contains(_main.QueueView.OpenedFile.Song.Id))
        {
            _main.PlaybackControls.CloseFile();
            _main.QueueView.ClearSavedSong();
            _main.PlaybackControls.UpdateButtons();
        }

        RemoveSongsFromCollections(songIdsToDelete);

        await using var db = _ioc.Get<PlayerContext>();

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
            }
        }

        _main.QueueView.OnQueueModified();
        _main.SongsView.ApplySort();
        _main.SongsView.NotifyFileErrorsChanged();
    }

    /// <summary>
    /// Remove songs from all in-memory collections (Queue, Songs library, selections).
    /// </summary>
    public void RemoveSongsFromCollections(IReadOnlyCollection<Guid> songIds)
    {
        if (_main is null) return;

        foreach (var track in _main.SongsView.Tracks.Where(t => songIds.Contains(t.Song.Id)).ToList())
            _main.SongsView.Tracks.Remove(track);

        foreach (var track in _main.QueueView.Tracks.Where(t => songIds.Contains(t.Song.Id)).ToList())
            _main.QueueView.Tracks.Remove(track);

        if (_main.SongsView.SelectedFile is not null && songIds.Contains(_main.SongsView.SelectedFile.Song.Id))
            _main.SongsView.SelectedFile = null;

        if (_main.QueueView.SelectedFile is not null && songIds.Contains(_main.QueueView.SelectedFile.Song.Id))
            _main.QueueView.SelectedFile = null;

        if (_main.QueueView.OpenedFile is not null && songIds.Contains(_main.QueueView.OpenedFile.Song.Id))
            _main.QueueView.OpenedFile = null;

        _main.QueueView.RemoveSongsFromHistory(songIds);
    }

    #endregion
}

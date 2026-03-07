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

    public List<MusicConstants.KeyOption> KeyOptions { get; } = MusicConstants.GenerateKeyOptions();

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
            if (SetAndNotify(ref _keyOffset, Math.Clamp(value,
                    MusicConstants.MinKeyOffset, MusicConstants.MaxKeyOffset)))
            {
                _selectedKeyOption = KeyOptions.FirstOrDefault(k => k.Value == _keyOffset);
                NotifyOfPropertyChange(nameof(SelectedKeyOption));

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

        // Speed: per-song or default 1.0
        Speed = file.Song.Speed ?? 1.0;

        // Key offset: always from song
        KeyOffset = file.Song.Key;

        // Transpose: from song or null
        var transpose = TransposeNames
            .FirstOrDefault(e => e.Key == file.Song.Transpose);
        Transpose = file.Song.Transpose is not null ? transpose : null;
    }

    /// <summary>
    /// Clear settings when file is closed.
    /// </summary>
    public void ClearSettings()
    {
        CurrentFile = null;
        Transpose = null;
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
            await using var db = _ioc.Get<LyreContext>();
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
    public async Task EditSongAsync(MidiFile file)
    {
        if (_main is null) return;

        var nativeBpm = file.GetNativeBpm();

        var dialog = new ImportDialog(
            file.Song.Title ?? Path.GetFileNameWithoutExtension(file.Path),
            file.Song.Key,
            file.Song.Transpose ?? Data.Entities.Transpose.Ignore,
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

        file.Song.Title = string.IsNullOrWhiteSpace(dialog.SongTitle)
            ? Path.GetFileNameWithoutExtension(file.Path)
            : dialog.SongTitle;
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

        if (_main.QueueView.OpenedFile?.Song.Id == file.Song.Id)
        {
            SyncFromEditedSong(file.Song);
            await _main.Playback.RefreshCurrentSongRealtimeAsync();
        }

        _main.SongsView.ApplySort();
        _main.QueueView.ApplyFilter();
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

        if (_main.QueueView.OpenedFile is not null &&
            songIdsToDelete.Contains(_main.QueueView.OpenedFile.Song.Id))
        {
            _main.Playback.CloseFile();
            _main.QueueView.ClearSavedSong();
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
            }
        }

        _main.QueueView.OnQueueModified();
        _main.SongsView.ApplySort();
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

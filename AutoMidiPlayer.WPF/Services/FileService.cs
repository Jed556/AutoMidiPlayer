using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.WPF.Dialogs;
using AutoMidiPlayer.WPF.ViewModels;
using Melanchall.DryWetMidi.Core;
using StyletIoC;
using MidiFile = AutoMidiPlayer.Data.Midi.MidiFile;

namespace AutoMidiPlayer.WPF.Services;

/// <summary>
/// Service responsible for file operations: adding, removing, scanning for MIDI files,
/// and handling missing/bad file scenarios.
/// </summary>
public class FileService(IContainer ioc)
{
    private readonly IContainer _ioc = ioc;
    private MainWindowViewModel? _main;

    /// <summary>
    /// Late-bind the main ViewModel reference (avoids circular dependency at construction time).
    /// </summary>
    public void SetMain(MainWindowViewModel main) => _main = main;

    #region Missing File Handling

    /// <summary>
    /// Show a dialog informing the user that a MIDI file is missing.
    /// </summary>
    public static async Task ShowMissingSongFileDialogAsync(string filePath)
    {
        var message =
            "The selected MIDI file could not be found:\n\n" +
            filePath +
            "\n\nIt will be moved to the Missing files list.";

        try
        {
            var dialog = DialogHelper.CreateDialog();
            dialog.Title = "Missing MIDI file";
            dialog.Content = message;
            dialog.CloseButtonText = "OK";

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                await dialog.ShowAsync();
                return;
            }

            CrashLogger.Log("DialogHost was not ready while showing missing MIDI file dialog. Falling back to MessageBox.");
            System.Windows.MessageBox.Show(
                message,
                "Missing MIDI file",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
        catch (Exception dialogError)
        {
            CrashLogger.Log("Failed to display missing MIDI file dialog.");
            CrashLogger.LogException(dialogError);
            System.Windows.MessageBox.Show(
                message,
                "Missing MIDI file",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Handle a song whose file is missing during playback: show dialog, mark as missing, remove from queue.
    /// </summary>
    public async Task HandleMissingSongFileAsync(MidiFile file)
    {
        await ShowMissingSongFileDialogAsync(file.Path);

        if (_main is null) return;

        MarkSongAsMissing(file.Song);
        _main.QueueView.RemoveSong(new[] { file });
    }

    #endregion

    #region File Error Management

    /// <summary>
    /// Remove a single missing song from the database.
    /// </summary>
    public async Task RemoveMissingSong(Song song)
    {
        if (_main is null) return;
        var songs = _main.SongsView;

        await using var db = _ioc.Get<LyreContext>();

        var songsToRemove = db.Songs.Where(s => s.Path == song.Path).ToList();
        if (songsToRemove.Count == 0)
            songsToRemove.Add(song);

        db.Songs.RemoveRange(songsToRemove);
        await db.SaveChangesAsync();

        foreach (var missingSong in songs.MissingSongs.Where(s => s.Path == song.Path).ToList())
            songs.MissingSongs.Remove(missingSong);

        RemoveBadMidiFileEntries(song.Path, false);

        songs.NotifyFileErrorsChanged();
    }

    /// <summary>
    /// Remove a single bad MIDI file from the database.
    /// </summary>
    public async Task RemoveBadMidiSong(SongsViewModel.BadMidiFileEntry badMidiFile)
    {
        if (_main is null) return;
        var songs = _main.SongsView;

        await using var db = _ioc.Get<LyreContext>();

        var songsToRemove = db.Songs.Where(s => s.Path == badMidiFile.Path).ToList();
        if (songsToRemove.Count > 0)
        {
            db.Songs.RemoveRange(songsToRemove);
            await db.SaveChangesAsync();
        }

        RemoveBadMidiFileEntries(badMidiFile.Path, false);

        foreach (var missingSong in songs.MissingSongs.Where(s => s.Path == badMidiFile.Path).ToList())
            songs.MissingSongs.Remove(missingSong);

        songs.NotifyFileErrorsChanged();
    }

    /// <summary>
    /// Remove all missing and bad MIDI files from the database.
    /// </summary>
    public async Task RemoveAllFileErrors()
    {
        if (_main is null) return;
        var songs = _main.SongsView;

        if (!songs.HasFileErrors) return;

        var allPaths = songs.MissingSongs.Select(s => s.Path)
            .Concat(songs.BadMidiFiles.Select(s => s.Path))
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

        songs.MissingSongs.Clear();
        songs.BadMidiFiles.Clear();

        songs.NotifyFileErrorsChanged();
    }

    /// <summary>
    /// Mark a song as missing at runtime (e.g. file deleted while app is running).
    /// </summary>
    public void MarkSongAsMissing(Song song)
    {
        if (_main is null) return;
        var songs = _main.SongsView;

        if (!songs.MissingSongs.Any(s => s.Id == song.Id))
            songs.MissingSongs.Add(song);

        foreach (var track in songs.Tracks.Where(t => t.Song.Id == song.Id).ToList())
            songs.Tracks.Remove(track);

        if (songs.SelectedFile is not null && songs.SelectedFile.Song.Id == song.Id)
            songs.SelectedFile = null;

        RemoveBadMidiFileEntries(song.Path, false);

        songs.NotifyFileErrorsChanged();
        songs.ApplySort();
    }

    #endregion

    #region File Adding

    /// <summary>
    /// Add MIDI files by path (e.g. from file dialog or drag-drop).
    /// </summary>
    public async Task AddFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            await AddFile(file);
        }

        _main?.SongsView.ApplySort();
    }

    /// <summary>
    /// Add songs from the database (e.g. on startup).
    /// </summary>
    public async Task AddFiles(IEnumerable<Song> files)
    {
        if (_main is null) return;
        var songs = _main.SongsView;

        foreach (var file in files)
        {
            if (!File.Exists(file.Path))
            {
                if (!songs.MissingSongs.Any(s => s.Id == file.Id))
                    songs.MissingSongs.Add(file);

                RemoveBadMidiFileEntries(file.Path, false);
                continue;
            }

            await AddFile(file);
        }

        songs.NotifyFileErrorsChanged();
        songs.ApplySort();
    }

    /// <summary>
    /// Scan a folder recursively for MIDI files and add them.
    /// </summary>
    public async Task ScanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;

        var midiFiles = Directory.GetFiles(folderPath, "*.mid", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folderPath, "*.midi", SearchOption.AllDirectories));

        await AddFiles(midiFiles);
    }

    #endregion

    #region Private Helpers

    private async Task<bool> AddFile(Song song, ReadingSettings? settings = null)
    {
        if (_main is null) return false;
        var songs = _main.SongsView;

        try
        {
            if (songs.Tracks.Any(t => t.Song.Path == song.Path))
                return false;

            if (song.FileHash != null && songs.Tracks.Any(t => t.Song.FileHash == song.FileHash))
                return false;

            if (song.FileHash == null && File.Exists(song.Path))
            {
                song.FileHash = Song.ComputeFileHash(song.Path);

                if (song.FileHash != null && songs.Tracks.Any(t => t.Song.FileHash == song.FileHash))
                    return false;
            }

            songs.Tracks.Add(new(song, settings));

            RemoveBadMidiFileEntries(song.Path, false);
            songs.NotifyFileErrorsChanged();

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
        if (_main is null) return;
        var songs = _main.SongsView;

        if (songs.Tracks.Any(t => t.Song.Path == fileName))
            return;

        var fileHash = Song.ComputeFileHash(fileName);

        if (fileHash != null)
        {
            var missingByHash = songs.MissingSongs.FirstOrDefault(song => song.FileHash == fileHash);
            if (missingByHash != null)
            {
                await RestoreMissingSong(missingByHash, fileName, fileHash);
                return;
            }

            var existingByHash = songs.Tracks.FirstOrDefault(t => t.Song.FileHash == fileHash);
            if (existingByHash != null)
            {
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

        var defaultTitle = Path.GetFileNameWithoutExtension(fileName);

        var song = new Song(fileName, _main.SongSettings.KeyOffset)
        {
            Title = defaultTitle,
            Transpose = Transpose.Ignore,
            HoldNotes = true
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
        if (_main is null) return;
        var songs = _main.SongsView;

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

        songs.MissingSongs.Remove(missingSong);
        RemoveBadMidiFileEntries(missingSong.Path, false);
        songs.NotifyFileErrorsChanged();
    }

    private void AddBadMidiFile(Song song, Exception exception)
    {
        if (_main is null) return;
        var songs = _main.SongsView;

        if (songs.BadMidiFiles.Any(b => string.Equals(b.Path, song.Path, StringComparison.OrdinalIgnoreCase)))
            return;

        foreach (var missingSong in songs.MissingSongs.Where(s => string.Equals(s.Path, song.Path, StringComparison.OrdinalIgnoreCase)).ToList())
            songs.MissingSongs.Remove(missingSong);

        songs.BadMidiFiles.Add(new SongsViewModel.BadMidiFileEntry(song, exception.Message));

        songs.NotifyFileErrorsChanged();
    }

    private void RemoveBadMidiFileEntries(string path, bool notify = true)
    {
        if (_main is null) return;
        var songs = _main.SongsView;

        var removed = false;
        foreach (var badMidiSong in songs.BadMidiFiles.Where(b => string.Equals(b.Path, path, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            songs.BadMidiFiles.Remove(badMidiSong);
            removed = true;
        }

        if (removed && notify)
            songs.NotifyFileErrorsChanged();
    }

    #endregion
}

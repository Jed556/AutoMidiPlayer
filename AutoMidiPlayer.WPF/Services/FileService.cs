using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Dialogs;
using AutoMidiPlayer.WPF.ViewModels;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
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
    private readonly SemaphoreSlim _addFilesLock = new(1, 1);
    private static readonly Settings Settings = Settings.Default;
    private const int StartupSongLoadYieldInterval = 12;
    private static readonly string[] NoiseTagKeywords =
    [
        "official", "audio", "video", "lyrics", "lyric", "karaoke", "instrumental",
        "midi", "mid", "mp3", "hq", "hd", "remaster", "remastered", "slowed",
        "reverb", "nightcore", "sped up", "version", "ver", "full"
    ];

    private static readonly string[] ArtistIndicators =
    [
        "feat", "ft", "featuring", "prod", "producer", "by", "x", "vs", "&", ","
    ];

    private static readonly string[] TitleIndicators =
    [
        "ost", "theme", "soundtrack", "opening", "ending", "op", "ed"
    ];

    private readonly record struct ParsedSongMetadata(string Title, string? Artist);

    private static readonly double[] MajorKeyProfile =
    [
        6.35, 2.23, 3.48, 2.33, 4.38, 4.09,
        2.52, 5.19, 2.39, 3.66, 2.29, 2.88
    ];

    private static readonly double[] MinorKeyProfile =
    [
        6.33, 2.68, 3.52, 5.38, 2.60, 3.53,
        2.54, 4.75, 3.98, 2.69, 3.34, 3.17
    ];

    public readonly record struct MidiAnalysisResult(double NativeBpm, int? DetectedDefaultKeyOffset, DateTime FileDate);
    public readonly record struct RemovedExistingMidiFileEntry(string Path, string Title);

    /// <summary>
    /// Best-effort analysis for a MIDI file path used by UI flows that need native metadata refresh.
    /// </summary>
    public static bool TryAnalyzeMidiFile(string filePath, out MidiAnalysisResult result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            var midi = Melanchall.DryWetMidi.Core.MidiFile.Read(filePath);
            var tempo = midi.GetTempoMap().GetTempoAtTime(new MetricTimeSpan(0));
            var nativeBpm = tempo.BeatsPerMinute;
            var fileDate = File.GetLastWriteTime(filePath);
            var hasDetectedKey = TryDetectSongKeyOffset(midi, out var detectedDefaultKeyOffset);

            result = new MidiAnalysisResult(nativeBpm, hasDetectedKey ? detectedDefaultKeyOffset : null, fileDate);
            return true;
        }
        catch
        {
            return false;
        }
    }

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
        if (songsToRemove.Count > 0)
        {
            db.Songs.RemoveRange(songsToRemove);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                // Another operation already removed one or more rows.
            }
        }

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
        await _addFilesLock.WaitAsync();
        try
        {
            if (_main is null) return;
            var fileList = files as IList<string> ?? files.ToList();
            CrashLogger.LogStep("ADD_FILES_BEGIN", $"source=paths | requested={fileList.Count}");

            var songs = _main.SongsView;
            var loadedFileCount = 0;
            var existingPaths = songs.Tracks
                .Select(track => track.Song.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingHashes = songs.Tracks
                .Select(track => track.Song.FileHash)
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .Select(hash => hash!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingSongsByHash = songs.MissingSongs
                .Where(song => !string.IsNullOrWhiteSpace(song.FileHash))
                .GroupBy(song => song.FileHash!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var file in fileList)
            {
                await AddFile(
                    file,
                    notifyFileErrors: false,
                    trackedPaths: existingPaths,
                    trackedHashes: existingHashes,
                    missingSongsByHash: missingSongsByHash);
                loadedFileCount++;

                if (loadedFileCount % StartupSongLoadYieldInterval == 0)
                    await YieldStartupWorkAsync();
            }

            songs.NotifyFileErrorsChanged();
            songs.ApplySort();
            CrashLogger.LogStep("ADD_FILES_END", $"source=paths | processed={loadedFileCount}");
        }
        finally
        {
            _addFilesLock.Release();
        }
    }

    /// <summary>
    /// Add songs from the database (e.g. on startup).
    /// </summary>
    public async Task AddFiles(IEnumerable<Song> songsFromDatabase)
    {
        await _addFilesLock.WaitAsync();
        try
        {
            if (_main is null) return;
            var songList = songsFromDatabase as IList<Song> ?? songsFromDatabase.ToList();
            CrashLogger.LogStep("ADD_FILES_BEGIN", $"source=database | requested={songList.Count}");

            var songs = _main.SongsView;
            var loadedSongCount = 0;
            var (existingPaths, existingHashes, missingSongIds) = BuildStartupSongLookupSets(
                songs.Tracks.Select(track => track.Song),
                songs.MissingSongs);

            foreach (var song in songList)
            {
                if (!File.Exists(song.Path))
                {
                    if (missingSongIds.Add(song.Id))
                        songs.MissingSongs.Add(song);

                    RemoveBadMidiFileEntries(song.Path, false);
                    continue;
                }

                await AddFile(
                    song,
                    notifyFileErrors: false,
                    trackedPaths: existingPaths,
                    trackedHashes: existingHashes,
                    computeMissingHash: false);
                loadedSongCount++;

                // Keep startup responsive while loading large libraries.
                if (loadedSongCount % StartupSongLoadYieldInterval == 0)
                    await YieldStartupWorkAsync();
            }

            songs.NotifyFileErrorsChanged();
            songs.ApplySort();
            CrashLogger.LogStep("ADD_FILES_END", $"source=database | processed={loadedSongCount}");
        }
        finally
        {
            _addFilesLock.Release();
        }
    }

    private static (HashSet<string> ExistingPaths, HashSet<string> ExistingHashes, HashSet<Guid> MissingSongIds)
        BuildStartupSongLookupSets(IEnumerable<Song> existingSongs, IEnumerable<Song> missingSongs)
    {
        var hasExistingCount = existingSongs.TryGetNonEnumeratedCount(out var existingCount);
        var existingPaths = hasExistingCount
            ? new HashSet<string>(existingCount, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingHashes = hasExistingCount
            ? new HashSet<string>(existingCount, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var song in existingSongs)
        {
            existingPaths.Add(song.Path);

            var fileHash = song.FileHash;
            if (!string.IsNullOrWhiteSpace(fileHash))
                existingHashes.Add(fileHash);
        }

        var hasMissingCount = missingSongs.TryGetNonEnumeratedCount(out var missingCount);
        var missingSongIds = hasMissingCount
            ? new HashSet<Guid>(missingCount)
            : new HashSet<Guid>();

        foreach (var song in missingSongs)
            missingSongIds.Add(song.Id);

        return (existingPaths, existingHashes, missingSongIds);
    }

    /// <summary>
    /// Scan a folder recursively for MIDI files and reconcile add/remove changes.
    /// </summary>
    public async Task ScanFolder(string folderPath)
    {
        var startedAt = DateTime.UtcNow;
        var folderExists = Directory.Exists(folderPath);
        var midiFileCount = 0;
        CrashLogger.LogStep("SCAN_FOLDER_BEGIN", $"folder='{folderPath}' | exists={folderExists}");

        if (folderExists)
        {
            List<string> midiFiles;
            try
            {
                // File-system enumeration can be expensive on large libraries.
                midiFiles = await GetMidiFilesInFolderAsync(folderPath);
            }
            catch (Exception error)
            {
                CrashLogger.Log($"Failed to enumerate MIDI files in folder '{folderPath}'.");
                CrashLogger.LogException(error);
                midiFiles = [];
            }

            midiFileCount = midiFiles.Count;

            if (midiFiles.Count > 0)
                await AddFiles(midiFiles);
        }

        if (_main is null)
            return;

        var songs = _main.SongsView;
        var duplicateFallbackApplied = await ResolveDuplicateFallbacksAsync(folderPath);
        var staleDuplicatesRemoved = RemoveStaleDuplicateMidiFileEntries(notify: false);

        var missingInFolder = songs.Tracks
            .Select(track => track.Song)
            .Where(song => AutoImportExclusionStore.IsMidiFilePath(song.Path)
                           && AutoImportExclusionStore.IsPathWithinFolder(song.Path, folderPath)
                           && !AutoImportExclusionStore.IsExcluded(song.Path)
                           && !File.Exists(song.Path))
            .DistinctBy(song => song.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingInFolder.Count == 0 && !duplicateFallbackApplied && !staleDuplicatesRemoved)
        {
            var elapsedMsEarly = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            CrashLogger.LogStep(
                "SCAN_FOLDER_END",
                $"folder='{folderPath}' | midiFiles={midiFileCount} | missingInFolder=0 | duplicateFallbackApplied={duplicateFallbackApplied} | staleDuplicatesRemoved={staleDuplicatesRemoved} | elapsedMs={elapsedMsEarly:F0}");
            return;
        }

        foreach (var missingSong in missingInFolder)
        {
            if (!songs.MissingSongs.Any(existing => string.Equals(existing.Path, missingSong.Path, StringComparison.OrdinalIgnoreCase)))
                songs.MissingSongs.Add(missingSong);

            RemoveBadMidiFileEntries(missingSong.Path, false);
            RemoveDuplicateMidiFileEntries(missingSong.Path, false);
        }

        var removableIds = missingInFolder
            .Select(song => song.Id)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (removableIds.Count > 0)
            _main.SongSettings.RemoveSongsFromCollections(removableIds);

        _main.QueueView.OnQueueModified();
        songs.NotifyFileErrorsChanged();
        songs.ApplySort();

        var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;
        CrashLogger.LogStep(
            "SCAN_FOLDER_END",
            $"folder='{folderPath}' | midiFiles={midiFileCount} | missingInFolder={missingInFolder.Count} | duplicateFallbackApplied={duplicateFallbackApplied} | staleDuplicatesRemoved={staleDuplicatesRemoved} | elapsedMs={elapsedMs:F0}");
    }

    public IReadOnlyList<RemovedExistingMidiFileEntry> GetRemovedExistingMidiFiles()
    {
        AutoImportExclusionStore.PruneMissingPaths();

        var existingExcludedFiles = AutoImportExclusionStore.GetExistingExcludedMidiFiles(Settings.MidiFolder).ToList();

        if (_main is not null)
        {
            var importedHashes = _main.SongsView.Tracks
                .Select(track => track.Song.FileHash)
                .Where(hash => !string.IsNullOrWhiteSpace(hash))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (importedHashes.Count > 0)
            {
                existingExcludedFiles = existingExcludedFiles
                    .Where(path =>
                    {
                        var hash = Song.ComputeFileHash(path);
                        return string.IsNullOrWhiteSpace(hash) || !importedHashes.Contains(hash);
                    })
                    .ToList();
            }
        }

        return existingExcludedFiles
            .Select(path => new RemovedExistingMidiFileEntry(path, Path.GetFileNameWithoutExtension(path)))
            .ToList();
    }

    public async Task<bool> RestoreExcludedFileToLibraryAsync(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
            return false;

        AutoImportExclusionStore.Remove(normalizedPath);

        if (!File.Exists(normalizedPath))
            return false;

        var importedBefore = _main?.SongsView.Tracks.Any(track =>
            string.Equals(track.Song.Path, normalizedPath, StringComparison.OrdinalIgnoreCase)) ?? false;

        await AddFiles([normalizedPath]);

        if (_main is not null)
        {
            var songs = _main.SongsView;

            foreach (var missingSong in songs.MissingSongs.Where(song =>
                         string.Equals(song.Path, normalizedPath, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                songs.MissingSongs.Remove(missingSong);
            }

            var importedAfter = songs.Tracks.Any(track =>
                string.Equals(track.Song.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (importedAfter)
                RemoveDuplicateMidiFileEntries(normalizedPath, false);

            songs.NotifyFileErrorsChanged();

            return importedAfter || importedBefore;
        }

        return true;
    }

    public bool DeleteExcludedFileFromDisk(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var fileExists = File.Exists(path);
        try
        {
            if (fileExists)
                File.Delete(path);

            AutoImportExclusionStore.Remove(path);

            if (_main is not null)
            {
                var songs = _main.SongsView;

                foreach (var missingSong in songs.MissingSongs.Where(song => string.Equals(song.Path, path, StringComparison.OrdinalIgnoreCase)).ToList())
                    songs.MissingSongs.Remove(missingSong);

                RemoveBadMidiFileEntries(path, false);
                RemoveDuplicateMidiFileEntries(path, false);
                songs.NotifyFileErrorsChanged();
            }

            return fileExists;
        }
        catch (Exception error)
        {
            CrashLogger.Log($"Failed to delete excluded MIDI file from disk: {path}");
            CrashLogger.LogException(error);
            return false;
        }
    }

    public bool RemoveStaleDuplicateMidiFileEntries(bool notify = true)
    {
        if (_main is null)
            return false;

        var songs = _main.SongsView;
        var removed = false;

        foreach (var duplicateEntry in songs.DuplicateMidiFiles
                     .Where(entry => string.IsNullOrWhiteSpace(entry.DuplicatePath)
                                     || !File.Exists(entry.DuplicatePath))
                     .ToList())
        {
            songs.DuplicateMidiFiles.Remove(duplicateEntry);
            removed = true;
        }

        if (removed && notify)
            songs.NotifyFileErrorsChanged();

        return removed;
    }

    public async Task<bool> ResolveDuplicateFallbacksAsync(string? folderPath = null)
    {
        if (_main is null)
            return false;

        var songs = _main.SongsView;
        var changed = RemoveStaleDuplicateMidiFileEntries(notify: false);

        var duplicateGroups = songs.DuplicateMidiFiles
            .GroupBy(entry => entry.ExistingPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                ExistingPath = group.Key,
                ExistingTitle = group
                    .Select(entry => entry.ExistingTitle)
                    .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title))
                               ?? Path.GetFileNameWithoutExtension(group.Key),
                DuplicatePaths = group
                    .Select(entry => entry.DuplicatePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        foreach (var duplicateGroup in duplicateGroups)
        {
            if (!string.IsNullOrWhiteSpace(folderPath)
                && !AutoImportExclusionStore.IsPathWithinFolder(duplicateGroup.ExistingPath, folderPath))
            {
                continue;
            }

            if (File.Exists(duplicateGroup.ExistingPath))
                continue;

            var availableDuplicatePaths = duplicateGroup.DuplicatePaths
                .Where(File.Exists)
                .ToList();

            if (availableDuplicatePaths.Count == 0)
            {
                RemoveDuplicateMidiFileEntries(duplicateGroup.ExistingPath, notify: false);
                changed = true;
                continue;
            }

            var fallbackPath = SelectBestDuplicateFallbackPath(duplicateGroup.ExistingPath, availableDuplicatePaths);
            var fallbackEntry = new SongsViewModel.DuplicateMidiFileEntry(
                duplicateGroup.ExistingPath,
                duplicateGroup.ExistingTitle,
                fallbackPath);

            var promoted = await PromoteDuplicateFileAsync(fallbackEntry, duplicateGroup.DuplicatePaths);
            if (promoted)
                changed = true;
        }

        if (changed)
            songs.NotifyFileErrorsChanged();

        return changed;
    }

    public async Task ApplyDuplicateSelectionsAsync(IReadOnlyCollection<SongsViewModel.DuplicateMidiFileEntry> duplicateEntries)
    {
        if (_main is null)
            return;

        var songs = _main.SongsView;
        var changed = RemoveStaleDuplicateMidiFileEntries(notify: false);
        var selectedEntries = duplicateEntries
            .Where(entry => entry.UseDuplicate)
            .GroupBy(entry => entry.ExistingPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (selectedEntries.Count == 0)
        {
            if (changed)
                songs.NotifyFileErrorsChanged();

            return;
        }

        foreach (var entry in selectedEntries)
        {
            var groupDuplicatePaths = duplicateEntries
                .Where(candidate => string.Equals(candidate.ExistingPath, entry.ExistingPath, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => candidate.DuplicatePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groupDuplicatePaths.Count == 0)
                continue;

            var selectedPath = entry.DuplicatePath;
            if (!File.Exists(selectedPath))
            {
                var availableDuplicatePaths = groupDuplicatePaths.Where(File.Exists).ToList();
                if (availableDuplicatePaths.Count == 0)
                    continue;

                selectedPath = SelectBestDuplicateFallbackPath(entry.ExistingPath, availableDuplicatePaths);
            }

            var selection = string.Equals(selectedPath, entry.DuplicatePath, StringComparison.OrdinalIgnoreCase)
                ? entry
                : new SongsViewModel.DuplicateMidiFileEntry(entry.ExistingPath, entry.ExistingTitle, selectedPath)
                {
                    UseDuplicate = true
                };

            var promoted = await PromoteDuplicateFileAsync(selection, groupDuplicatePaths);
            if (!promoted)
                continue;

            changed = true;
        }

        if (changed)
            songs.NotifyFileErrorsChanged();
    }

    #endregion

    #region Private Helpers

    private static Task<List<string>> GetMidiFilesInFolderAsync(string folderPath) =>
        Task.Run(() =>
            Directory.EnumerateFiles(folderPath, "*.mid", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(folderPath, "*.midi", SearchOption.AllDirectories))
                .Where(path => !AutoImportExclusionStore.IsExcluded(path))
                .ToList());

    private static Task<string?> ComputeFileHashAsync(string filePath) =>
        Task.Run(() => Song.ComputeFileHash(filePath));

    private static Task<MidiFile> CreateMidiFileAsync(Song song, ReadingSettings? settings) =>
        Task.Run(() => new MidiFile(song, settings));

    private static async Task YieldStartupWorkAsync()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            await Task.Yield();
            return;
        }

        await dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private async Task<bool> AddFile(
        Song song,
        ReadingSettings? settings = null,
        bool notifyFileErrors = true,
        HashSet<string>? trackedPaths = null,
        HashSet<string>? trackedHashes = null,
        bool computeMissingHash = true)
    {
        if (_main is null) return false;
        var songs = _main.SongsView;
        HashSet<string>? effectiveTrackedPaths = trackedPaths;
        HashSet<string>? effectiveTrackedHashes = trackedHashes;

        try
        {
            if (effectiveTrackedPaths is null || effectiveTrackedHashes is null)
            {
                var (existingPaths, existingHashes) = BuildTrackLookupSets(songs.Tracks);
                effectiveTrackedPaths ??= existingPaths;
                effectiveTrackedHashes ??= existingHashes;
            }

            if (effectiveTrackedPaths.Contains(song.Path))
                return false;

            var fileHash = song.FileHash;
            if (!string.IsNullOrWhiteSpace(fileHash)
                && effectiveTrackedHashes.Contains(fileHash))
                return false;

            if (computeMissingHash && string.IsNullOrWhiteSpace(fileHash) && File.Exists(song.Path))
            {
                fileHash = await ComputeFileHashAsync(song.Path);
                song.FileHash = fileHash;

                if (!string.IsNullOrWhiteSpace(fileHash)
                    && effectiveTrackedHashes.Contains(fileHash))
                    return false;
            }

            // Parse MIDI away from the UI thread; only collection updates stay on UI.
            var loadedFile = await CreateMidiFileAsync(song, settings);
            songs.Tracks.Add(loadedFile);
            trackedPaths?.Add(song.Path);
            if (!string.IsNullOrWhiteSpace(fileHash))
                trackedHashes?.Add(fileHash);

            await ApplyDetectedSongKeyAsync(song, loadedFile);

            RemoveBadMidiFileEntries(song.Path, false);
            RemoveDuplicateMidiFileEntries(song.Path, false);
            if (notifyFileErrors)
                songs.NotifyFileErrorsChanged();

            return true;
        }
        catch (Exception e)
        {
            settings ??= new();
            if (await MidiReadDialogHandler.TryHandleAsync(e, settings, song.Path))
                return await AddFile(
                    song,
                    settings,
                    notifyFileErrors,
                    trackedPaths ?? effectiveTrackedPaths,
                    trackedHashes ?? effectiveTrackedHashes,
                    computeMissingHash);

            AddBadMidiFile(song, e, notifyFileErrors);

            return false;
        }
    }

    private static (HashSet<string> ExistingPaths, HashSet<string> ExistingHashes) BuildTrackLookupSets(IEnumerable<MidiFile> tracks)
    {
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in tracks)
        {
            var song = track.Song;
            existingPaths.Add(song.Path);

            var fileHash = song.FileHash;
            if (!string.IsNullOrWhiteSpace(fileHash))
                existingHashes.Add(fileHash);
        }

        return (existingPaths, existingHashes);
    }

    private async Task ApplyDetectedSongKeyAsync(Song song, MidiFile loadedFile)
    {
        if (!ShouldAutoDetectSongKey(song))
            return;

        if (!TryDetectSongKeyOffset(loadedFile.Midi, out var detectedKey))
            return;

        if (song.DefaultKey == detectedKey && song.Key == 0)
            return;

        // Keep key offset relative to the detected song center.
        song.DefaultKey = detectedKey;
        song.Key = 0;

        // For songs already in the DB, persist once so future loads use the detected key.
        if (song.Id == Guid.Empty)
            return;

        try
        {
            await using var db = _ioc.Get<LyreContext>();
            db.Songs.Update(song);
            await db.SaveChangesAsync();
        }
        catch
        {
            // Detection is best-effort; never fail file loading because persistence failed.
        }
    }

    private static bool ShouldAutoDetectSongKey(Song song) =>
        song.Id == Guid.Empty && Settings.AutoDetectDefaultKey;

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = path.Trim();

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return trimmed;
        }
    }

    private static string SelectBestDuplicateFallbackPath(string existingPath, IReadOnlyCollection<string> candidatePaths)
    {
        var existingFileName = Path.GetFileNameWithoutExtension(existingPath);

        return candidatePaths
            .OrderByDescending(path => ScoreDuplicateNameSimilarity(existingFileName, Path.GetFileNameWithoutExtension(path)))
            .ThenBy(path => Math.Abs(Path.GetFileNameWithoutExtension(path).Length - existingFileName.Length))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int ScoreDuplicateNameSimilarity(string existingFileName, string candidateFileName)
    {
        if (string.IsNullOrWhiteSpace(existingFileName) || string.IsNullOrWhiteSpace(candidateFileName))
            return 0;

        var normalizedExisting = existingFileName.Trim().ToLowerInvariant();
        var normalizedCandidate = candidateFileName.Trim().ToLowerInvariant();

        var sharedPrefixLength = GetCommonPrefixLength(normalizedExisting, normalizedCandidate);
        var sharedTokenCount = normalizedExisting
            .Split([' ', '-', '_', '.', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Intersect(
                normalizedCandidate.Split([' ', '-', '_', '.', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal)
            .Count();

        var containsBoost = normalizedExisting.Contains(normalizedCandidate, StringComparison.Ordinal)
                           || normalizedCandidate.Contains(normalizedExisting, StringComparison.Ordinal)
            ? 10
            : 0;

        return (sharedPrefixLength * 4) + (sharedTokenCount * 8) + containsBoost;
    }

    private static int GetCommonPrefixLength(string left, string right)
    {
        var maxLength = Math.Min(left.Length, right.Length);
        var index = 0;

        while (index < maxLength && left[index] == right[index])
            index++;

        return index;
    }

    private static bool TryDetectSongKeyOffset(Melanchall.DryWetMidi.Core.MidiFile midi, out int keyOffset)
    {
        if (TryDetectFromKeySignature(midi, out keyOffset))
            return true;

        return TryDetectFromPitchClassProfile(midi, out keyOffset);
    }

    private static bool TryDetectFromKeySignature(Melanchall.DryWetMidi.Core.MidiFile midi, out int keyOffset)
    {
        var keySignature = midi
            .GetTimedEvents()
            .Select(timed => timed.Event)
            .OfType<KeySignatureEvent>()
            .FirstOrDefault();

        if (keySignature is null)
        {
            keyOffset = 0;
            return false;
        }

        var isMinor = keySignature.Scale == 1;
        var tonicPitchClass = Mod12((isMinor ? 9 : 0) + (keySignature.Key * 7));

        keyOffset = NormalizeDetectedKeyOffset(tonicPitchClass);
        return true;
    }

    private static bool TryDetectFromPitchClassProfile(Melanchall.DryWetMidi.Core.MidiFile midi, out int keyOffset)
    {
        var notes = midi.GetNotes().ToList();
        if (notes.Count == 0)
        {
            keyOffset = 0;
            return false;
        }

        var histogram = new double[12];
        foreach (var note in notes)
        {
            var pitchClass = Mod12(note.NoteNumber);
            var durationWeight = Math.Max(1L, note.Length);
            histogram[pitchClass] += durationWeight;
        }

        var bestScore = double.NegativeInfinity;
        var bestTonic = 0;

        for (var tonic = 0; tonic < 12; tonic++)
        {
            var majorScore = 0.0;
            var minorScore = 0.0;

            for (var interval = 0; interval < 12; interval++)
            {
                var weight = histogram[Mod12(tonic + interval)];
                majorScore += weight * MajorKeyProfile[interval];
                minorScore += weight * MinorKeyProfile[interval];
            }

            if (majorScore > bestScore)
            {
                bestScore = majorScore;
                bestTonic = tonic;
            }

            if (minorScore > bestScore)
            {
                bestScore = minorScore;
                bestTonic = tonic;
            }
        }

        keyOffset = NormalizeDetectedKeyOffset(bestTonic);
        return true;
    }

    private static int NormalizeDetectedKeyOffset(int pitchClass)
    {
        var tonicPitchClass = Mod12(pitchClass);

        // Convert detected tonic into the transposition needed to move the song
        // center toward C, matching the positive-up key offset convention.
        var transpositionToC = Mod12(-tonicPitchClass);

        return transpositionToC >= 6
            ? transpositionToC - 12
            : transpositionToC;
    }

    private static int Mod12(int value) => ((value % 12) + 12) % 12;

    private async Task AddFile(
        string fileName,
        bool notifyFileErrors = true,
        HashSet<string>? trackedPaths = null,
        HashSet<string>? trackedHashes = null,
        Dictionary<string, Song>? missingSongsByHash = null)
    {
        if (_main is null) return;
        var songs = _main.SongsView;

        var pathExists = trackedPaths?.Contains(fileName)
            ?? songs.Tracks.Any(track => string.Equals(track.Song.Path, fileName, StringComparison.OrdinalIgnoreCase));
        if (pathExists)
            return;

        var metadata = ParseSongMetadataFromFileName(fileName);
        var defaultSongDefaultKey = Math.Clamp(Settings.DefaultSongDefaultKey, MusicConstants.MinKeyOffset, MusicConstants.MaxKeyOffset);
        var defaultSongKey = Math.Clamp(
            Settings.DefaultSongKey,
            MusicConstants.GetRelativeMinKeyOffset(defaultSongDefaultKey),
            MusicConstants.GetRelativeMaxKeyOffset(defaultSongDefaultKey));
        var defaultSongTranspose = Enum.IsDefined(typeof(Transpose), Settings.DefaultSongTranspose)
            ? (Transpose)Settings.DefaultSongTranspose
            : Transpose.Ignore;
        var defaultSongMergeMilliseconds = Math.Clamp(Settings.DefaultSongMergeMilliseconds, 1u, 1000u);

        // Key offset is stored relative to DefaultKey for new songs.
        var song = new Song(fileName, defaultSongKey)
        {
            Title = metadata.Title,
            Artist = metadata.Artist,
            DefaultKey = defaultSongDefaultKey,
            Transpose = defaultSongTranspose,
            MergeNotes = Settings.DefaultSongMergeNotes,
            MergeMilliseconds = defaultSongMergeMilliseconds,
            HoldNotes = Settings.DefaultSongHoldNotes
        };

        var fileHash = song.FileHash;
        if (!string.IsNullOrWhiteSpace(fileHash))
        {
            Song? missingByHash = null;
            if (missingSongsByHash is not null)
                missingSongsByHash.TryGetValue(fileHash, out missingByHash);
            else
                missingByHash = songs.MissingSongs.FirstOrDefault(existingSong =>
                    string.Equals(existingSong.FileHash, fileHash, StringComparison.OrdinalIgnoreCase));

            if (missingByHash != null)
            {
                await RestoreMissingSong(
                    missingByHash,
                    fileName,
                    fileHash,
                    notifyFileErrors,
                    trackedPaths,
                    trackedHashes);

                missingSongsByHash?.Remove(fileHash);
                return;
            }

            Song? existingByHash = null;
            var hashExists = trackedHashes?.Contains(fileHash)
                ?? songs.Tracks.Any(track => string.Equals(track.Song.FileHash, fileHash, StringComparison.OrdinalIgnoreCase));

            if (hashExists)
            {
                existingByHash = songs.Tracks.FirstOrDefault(track =>
                    string.Equals(track.Song.FileHash, fileHash, StringComparison.OrdinalIgnoreCase))?.Song;
            }

            if (existingByHash != null)
            {
                AddDuplicateMidiFile(existingByHash, fileName, notifyFileErrors);
                return;
            }
        }

        var added = await AddFile(
            song,
            notifyFileErrors: notifyFileErrors,
            trackedPaths: trackedPaths,
            trackedHashes: trackedHashes);
        if (!added)
            return;

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Add(song);
        await db.SaveChangesAsync();

        // Manual imports should immediately become eligible for future auto-imports again.
        AutoImportExclusionStore.Remove(fileName);
        if (notifyFileErrors)
            songs.NotifyFileErrorsChanged();
    }

    private static ParsedSongMetadata ParseSongMetadataFromFileName(string filePath)
    {
        var rawName = Path.GetFileNameWithoutExtension(filePath);
        var normalizedName = NormalizeFileName(rawName);
        if (string.IsNullOrWhiteSpace(normalizedName))
            return new ParsedSongMetadata(rawName, null);

        var byMatch = Regex.Match(
            normalizedName,
            "^(?<title>.+?)\\s+by\\s+(?<artist>.+)$",
            RegexOptions.IgnoreCase);

        if (TryBuildMetadata(byMatch.Groups["title"].Value, byMatch.Groups["artist"].Value, out var byMetadata))
            return byMetadata;

        var splitParts = SplitByCommonSeparators(normalizedName);
        if (splitParts.Count >= 2)
        {
            var first = splitParts[0];
            var last = splitParts[^1];
            var firstArtistScore = GetArtistLikelihood(first);
            var lastArtistScore = GetArtistLikelihood(last);

            // If one side looks clearly more like an artist, treat that side as Artist.
            if (firstArtistScore >= lastArtistScore + 2)
            {
                if (TryBuildMetadata(string.Join(" - ", splitParts.Skip(1)), first, out var reversedMetadata))
                    return reversedMetadata;
            }
            else if (lastArtistScore >= firstArtistScore + 2)
            {
                if (TryBuildMetadata(string.Join(" - ", splitParts.Take(splitParts.Count - 1)), last, out var standardMetadata))
                    return standardMetadata;
            }
            else
            {
                if (TryBuildMetadata(splitParts[0], string.Join(" - ", splitParts.Skip(1)), out var defaultMetadata))
                    return defaultMetadata;
            }
        }

        var trailingArtistMatch = Regex.Match(
            normalizedName,
            "^(?<title>.+?)\\s*[\\(\\[](?<artist>[^\\)\\]]+)[\\)\\]]$",
            RegexOptions.IgnoreCase);

        if (TryBuildMetadata(
                trailingArtistMatch.Groups["title"].Value,
                trailingArtistMatch.Groups["artist"].Value,
                out var trailingArtistMetadata))
        {
            return trailingArtistMetadata;
        }

        return new ParsedSongMetadata(normalizedName, null);
    }

    private static List<string> SplitByCommonSeparators(string value)
    {
        var spacedSplit = Regex.Split(value, "\\s+(?:-|–|—|~|\\|)\\s+")
            .Select(part => part.Trim())
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();

        if (spacedSplit.Count >= 2)
            return spacedSplit;

        // Compact patterns such as Artist-Title are handled only for one delimiter to avoid splitting hyphenated words.
        var compactMatch = Regex.Match(value, "^(?<left>[^-~|]{2,})[-~|](?<right>[^-~|]{2,})$");
        if (compactMatch.Success)
        {
            var left = compactMatch.Groups["left"].Value.Trim();
            var right = compactMatch.Groups["right"].Value.Trim();

            if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
                return [left, right];
        }

        return [];
    }

    private static string NormalizeFileName(string value)
    {
        var normalized = value.Replace('_', ' ');
        normalized = Regex.Replace(normalized, "\\s+", " ").Trim();
        normalized = Regex.Replace(normalized, "^\\d{1,3}[\\)\\].\\-_ ]+", string.Empty).Trim();

        // Remove trailing bracket tags that are usually source/noise markers.
        while (TryRemoveTrailingBracketTag(normalized, out var cleaned))
            normalized = cleaned;

        return normalized.Trim('-', ' ', '|', '~');
    }

    private static bool TryRemoveTrailingBracketTag(string value, out string cleaned)
    {
        cleaned = value;

        var tagMatch = Regex.Match(value, "^(?<head>.+?)\\s*[\\(\\[\\{](?<tag>[^\\)\\]\\}]+)[\\)\\]\\}]\\s*$");
        if (!tagMatch.Success)
            return false;

        var tag = tagMatch.Groups["tag"].Value;
        if (!LooksLikeNoiseTag(tag))
            return false;

        cleaned = tagMatch.Groups["head"].Value.Trim();
        return !string.IsNullOrWhiteSpace(cleaned);
    }

    private static bool LooksLikeNoiseTag(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lowered))
            return false;

        if (Regex.IsMatch(lowered, "^\\d{3,4}p$"))
            return true;

        return NoiseTagKeywords.Any(keyword => lowered.Contains(keyword, StringComparison.Ordinal));
    }

    private static int GetArtistLikelihood(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return 0;

        var lowered = segment.ToLowerInvariant();
        var score = 0;

        if (ArtistIndicators.Any(indicator => Regex.IsMatch(lowered, $"\\b{Regex.Escape(indicator)}\\b")))
            score += 2;

        if (segment.Contains(',') || segment.Contains('&') || segment.Contains('/'))
            score += 1;

        var wordCount = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount is > 0 and <= 4)
            score += 1;
        else if (wordCount >= 8)
            score -= 1;

        if (Regex.IsMatch(segment, "\\d"))
            score -= 1;

        if (TitleIndicators.Any(indicator => Regex.IsMatch(lowered, $"\\b{Regex.Escape(indicator)}\\b")))
            score -= 2;

        return score;
    }

    private static bool TryBuildMetadata(string titleCandidate, string artistCandidate, out ParsedSongMetadata metadata)
    {
        metadata = default;

        var title = CleanMetadataSegment(titleCandidate);
        var artist = CleanMetadataSegment(artistCandidate);

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return false;

        if (string.Equals(title, artist, StringComparison.OrdinalIgnoreCase))
            return false;

        metadata = new ParsedSongMetadata(title, artist);
        return true;
    }

    private static string CleanMetadataSegment(string value)
    {
        var cleaned = value.Trim();
        cleaned = cleaned.Trim('-', ' ', '|', '~');
        cleaned = Regex.Replace(cleaned, "\\s+", " ");
        return cleaned;
    }

    private async Task RestoreMissingSong(
        Song missingSong,
        string newPath,
        string fileHash,
        bool notifyFileErrors = true,
        HashSet<string>? trackedPaths = null,
        HashSet<string>? trackedHashes = null)
    {
        if (_main is null) return;
        var songs = _main.SongsView;

        var oldPath = missingSong.Path;
        var oldHash = missingSong.FileHash;

        missingSong.Path = newPath;
        missingSong.FileHash = fileHash;

        var restored = await AddFile(
            missingSong,
            notifyFileErrors: notifyFileErrors,
            trackedPaths: trackedPaths,
            trackedHashes: trackedHashes);
        if (!restored)
        {
            missingSong.Path = oldPath;
            missingSong.FileHash = oldHash;
            return;
        }

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Update(missingSong);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            // Another operation already removed or changed this row.
        }

        songs.MissingSongs.Remove(missingSong);
        AutoImportExclusionStore.Remove(newPath);
        RemoveBadMidiFileEntries(missingSong.Path, false);

        if (notifyFileErrors)
            songs.NotifyFileErrorsChanged();
    }

    private void AddBadMidiFile(Song song, Exception exception, bool notifyFileErrors = true)
    {
        if (_main is null) return;
        var songs = _main.SongsView;

        if (songs.BadMidiFiles.Any(b => string.Equals(b.Path, song.Path, StringComparison.OrdinalIgnoreCase)))
            return;

        foreach (var missingSong in songs.MissingSongs.Where(s => string.Equals(s.Path, song.Path, StringComparison.OrdinalIgnoreCase)).ToList())
            songs.MissingSongs.Remove(missingSong);

        songs.BadMidiFiles.Add(new SongsViewModel.BadMidiFileEntry(song, exception.Message));

        if (notifyFileErrors)
            songs.NotifyFileErrorsChanged();
    }

    private void AddDuplicateMidiFile(Song existingSong, string duplicatePath, bool notifyFileErrors = true)
    {
        if (_main is null)
            return;

        var songs = _main.SongsView;
        var existingPath = existingSong.Path;

        if (string.IsNullOrWhiteSpace(existingPath) || string.IsNullOrWhiteSpace(duplicatePath))
            return;

        if (string.Equals(existingPath, duplicatePath, StringComparison.OrdinalIgnoreCase))
            return;

        var alreadyTracked = songs.DuplicateMidiFiles.Any(entry =>
            string.Equals(entry.ExistingPath, existingPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.DuplicatePath, duplicatePath, StringComparison.OrdinalIgnoreCase));

        if (alreadyTracked)
            return;

        songs.DuplicateMidiFiles.Add(new SongsViewModel.DuplicateMidiFileEntry(
            existingPath,
            existingSong.Title ?? Path.GetFileName(existingPath),
            duplicatePath));

        if (notifyFileErrors)
            songs.NotifyFileErrorsChanged();
    }

    private async Task<bool> PromoteDuplicateFileAsync(SongsViewModel.DuplicateMidiFileEntry entry, IReadOnlyCollection<string> duplicatePathsInGroup)
    {
        if (_main is null)
            return false;

        var existingPath = entry.ExistingPath;
        var selectedPath = entry.DuplicatePath;

        if (!File.Exists(selectedPath))
            return false;

        var songs = _main.SongsView;

        var existingTrackIds = songs.Tracks
            .Where(track => string.Equals(track.Song.Path, existingPath, StringComparison.OrdinalIgnoreCase))
            .Select(track => track.Song.Id)
            .Distinct()
            .ToList();

        if (existingTrackIds.Count > 0)
            _main.SongSettings.RemoveSongsFromCollections(existingTrackIds);

        await using var db = _ioc.Get<LyreContext>();
        var existingSongs = db.Songs
            .Where(song => song.Path == existingPath)
            .ToList();

        if (existingSongs.Count > 0)
        {
            db.Songs.RemoveRange(existingSongs);
            await db.SaveChangesAsync();
        }

        foreach (var missingSong in songs.MissingSongs
                     .Where(song => string.Equals(song.Path, existingPath, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            songs.MissingSongs.Remove(missingSong);
        }

        RemoveBadMidiFileEntries(existingPath, false);

        // Ensure both current and alternate versions remain available for future switching.
        AutoImportExclusionStore.Remove(existingPath);
        AutoImportExclusionStore.Remove(selectedPath);

        await AddFiles([selectedPath]);

        RemoveDuplicateMidiFileEntries(existingPath, false);
        RemoveDuplicateMidiFileEntries(selectedPath, false);

        var selectedTrack = songs.Tracks.FirstOrDefault(track =>
            string.Equals(track.Song.Path, selectedPath, StringComparison.OrdinalIgnoreCase));

        if (selectedTrack is null)
            return false;

        var alternatePaths = duplicatePathsInGroup
            .Append(existingPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(path => !string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .ToList();

        foreach (var alternatePath in alternatePaths)
            AddDuplicateMidiFile(selectedTrack.Song, alternatePath, notifyFileErrors: false);

        return true;
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

    private void RemoveDuplicateMidiFileEntries(string path, bool notify = true)
    {
        if (_main is null)
            return;

        var songs = _main.SongsView;
        var removed = false;

        foreach (var duplicateEntry in songs.DuplicateMidiFiles
                     .Where(entry => string.Equals(entry.ExistingPath, path, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(entry.DuplicatePath, path, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            songs.DuplicateMidiFiles.Remove(duplicateEntry);
            removed = true;
        }

        if (removed && notify)
            songs.NotifyFileErrorsChanged();
    }

    #endregion
}

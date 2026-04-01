using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    private static readonly Settings Settings = Settings.Default;
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

            var loadedFile = songs.Tracks.FirstOrDefault(track => ReferenceEquals(track.Song, song));
            if (loadedFile is not null)
                await ApplyDetectedSongKeyAsync(song, loadedFile);

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
        var normalizedPitchClass = Mod12(pitchClass);

        // Prefer the nearest signed offset around C3 so A/B/G detect as A2/B2/G2.
        return normalizedPitchClass >= 6
            ? normalizedPitchClass - 12
            : normalizedPitchClass;
    }

    private static int Mod12(int value) => ((value % 12) + 12) % 12;

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

        var metadata = ParseSongMetadataFromFileName(fileName);

        // Key offset is stored relative to DefaultKey for new songs.
        // Start at 0 so detected DefaultKey becomes the playback base.
        var song = new Song(fileName, 0)
        {
            Title = metadata.Title,
            Artist = metadata.Artist,
            Transpose = Transpose.Ignore,
            HoldNotes = false
        };

        if (!Settings.AutoDetectDefaultKey)
            song.DefaultKey = 0;

        var added = await AddFile(song);
        if (!added)
            return;

        await using var db = _ioc.Get<LyreContext>();
        db.Songs.Add(song);
        await db.SaveChangesAsync();
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

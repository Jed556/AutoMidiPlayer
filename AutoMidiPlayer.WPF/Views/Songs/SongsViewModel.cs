using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Dialogs;
using AutoMidiPlayer.WPF.Helpers;
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

        async Task<IReadOnlyList<Services.FileService.RemovedExistingMidiFileEntry>> RefreshSourceListsAsync(bool resolveDuplicateFallbacks = true)
        {
            if (resolveDuplicateFallbacks)
                await _main.FileService.ResolveDuplicateFallbacksAsync();

            _main.FileService.RemoveStaleDuplicateMidiFileEntries(notify: false);

            var refreshedRemovedExistingMidiFiles = _main.FileService.GetRemovedExistingMidiFiles().ToList();
            if (PruneMissingSongsForRemovedExisting(refreshedRemovedExistingMidiFiles))
                NotifyFileErrorsChanged();

            return refreshedRemovedExistingMidiFiles;
        }

        var initialRemovedExistingMidiFiles = await RefreshSourceListsAsync(resolveDuplicateFallbacks: false);

        if (!HasFileErrors && initialRemovedExistingMidiFiles.Count == 0)
            return;

        var hasDatabaseFileErrors = MissingSongs.Count > 0 || BadMidiFiles.Count > 0;
        var dialogOpen = true;
        var refreshInProgress = false;
        FileIssuesDialog? dialog = null;

        async Task RefreshListsAsync(bool resolveDuplicateFallbacks = true)
        {
            if (!dialogOpen || refreshInProgress || dialog is null)
                return;

            refreshInProgress = true;

            try
            {
                var refreshedRemovedExistingMidiFiles = await RefreshSourceListsAsync(resolveDuplicateFallbacks);

                dialog.SetData(
                    MissingSongs.ToList(),
                    BadMidiFiles.ToList(),
                    DuplicateMidiFiles.ToList(),
                    refreshedRemovedExistingMidiFiles);
            }
            finally
            {
                refreshInProgress = false;
            }
        }

        dialog = new FileIssuesDialog(
            hasDatabaseFileErrors,
            async song =>
            {
                await _main.FileService.RemoveMissingSong(song);
                await RefreshListsAsync(resolveDuplicateFallbacks: false);
            },
            async badMidiFile =>
            {
                await _main.FileService.RemoveBadMidiSong(badMidiFile);
                await RefreshListsAsync(resolveDuplicateFallbacks: false);
            },
            async removedEntry =>
            {
                await _main.FileService.RestoreExcludedFileToLibraryAsync(removedEntry.Path);
                await RefreshListsAsync(resolveDuplicateFallbacks: false);
            },
            async removedEntry =>
            {
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

                if (result != DialogActionOutcome.Confirmed)
                    return;

                await RefreshListsAsync(resolveDuplicateFallbacks: false);

                if (!fileDeleted && System.IO.File.Exists(removedEntry.Path))
                    await UnableToDeleteFileDialog.ShowDeleteFailedAsync();
            });

        dialog.SetData(
            MissingSongs.ToList(),
            BadMidiFiles.ToList(),
            DuplicateMidiFiles.ToList(),
            initialRemovedExistingMidiFiles);

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

        if (DuplicateMidiFiles.Count > 0)
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
            // Add to queue and then load with auto-play to avoid publish/toggle races.
            _main.QueueView.AddFile(file);
            await _main.PlaybackEngine.LoadFileAsync(file, autoPlay: true);
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

        await _main.SongSettings.EditSongAsync(file, source: "songs-view");
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

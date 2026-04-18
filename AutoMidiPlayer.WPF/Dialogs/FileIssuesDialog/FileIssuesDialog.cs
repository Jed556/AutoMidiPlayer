using System;
using System.Windows;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.WPF.Helpers;
using AutoMidiPlayer.WPF.Services;
using AutoMidiPlayer.WPF.ViewModels;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class FileIssuesDialog : ContentDialog, INotifyPropertyChanged
{
    public sealed class DuplicateOptionViewModel
    {
        public string ExistingPath { get; init; } = string.Empty;

        public string DuplicatePath { get; init; } = string.Empty;

        public string DuplicateFileName { get; init; } = string.Empty;

        public bool IsSelected { get; init; }
    }

    public sealed class DuplicateGroupViewModel
    {
        public string ExistingPath { get; init; } = string.Empty;

        public string ExistingFileName { get; init; } = string.Empty;

        public bool UseCurrentVersion { get; init; }

        public ObservableCollection<DuplicateOptionViewModel> DuplicateOptions { get; } = new();
    }

    private readonly Func<Song, Task> _removeMissingSongAsync;
    private readonly Func<SongsViewModel.BadMidiFileEntry, Task> _removeBadMidiSongAsync;
    private readonly Func<FileService.RemovedExistingMidiFileEntry, Task> _restoreRemovedExistingAsync;
    private readonly Func<FileService.RemovedExistingMidiFileEntry, Task> _deleteRemovedExistingAsync;

    private List<SongsViewModel.DuplicateMidiFileEntry> _duplicateEntries = new();

    static FileIssuesDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(FileIssuesDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public FileIssuesDialog(
        bool hasDatabaseFileErrors,
        Func<Song, Task> removeMissingSongAsync,
        Func<SongsViewModel.BadMidiFileEntry, Task> removeBadMidiSongAsync,
        Func<FileService.RemovedExistingMidiFileEntry, Task> restoreRemovedExistingAsync,
        Func<FileService.RemovedExistingMidiFileEntry, Task> deleteRemovedExistingAsync)
    {
        _removeMissingSongAsync = removeMissingSongAsync;
        _removeBadMidiSongAsync = removeBadMidiSongAsync;
        _restoreRemovedExistingAsync = restoreRemovedExistingAsync;
        _deleteRemovedExistingAsync = deleteRemovedExistingAsync;

        InitializeComponent();

        DataContext = this;

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

        if (!hasDatabaseFileErrors)
            return;

        PrimaryButtonText = "Remove All";
        PrimaryButtonAppearance = ControlAppearance.Danger;
    }

    public ObservableCollection<Song> MissingSongs { get; } = new();

    public ObservableCollection<SongsViewModel.BadMidiFileEntry> BadMidiFiles { get; } = new();

    public ObservableCollection<FileService.RemovedExistingMidiFileEntry> RemovedExistingMidiFiles { get; } = new();

    public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = new();

    public bool HasMissingSongs => MissingSongs.Count > 0;

    public bool HasBadMidiFiles => BadMidiFiles.Count > 0;

    public bool HasDuplicateConflicts => DuplicateGroups.Count > 0;

    public bool HasRemovedExistingMidiFiles => RemovedExistingMidiFiles.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetData(
        IReadOnlyCollection<Song> missingSongs,
        IReadOnlyCollection<SongsViewModel.BadMidiFileEntry> badMidiFiles,
        IReadOnlyCollection<SongsViewModel.DuplicateMidiFileEntry> duplicateEntries,
        IReadOnlyCollection<FileService.RemovedExistingMidiFileEntry> removedExistingMidiFiles)
    {
        ReplaceCollection(MissingSongs, missingSongs);
        ReplaceCollection(BadMidiFiles, badMidiFiles);
        ReplaceCollection(RemovedExistingMidiFiles, removedExistingMidiFiles);

        _duplicateEntries = duplicateEntries.ToList();
        RebuildDuplicateGroups();

        NotifyVisibilityStateChanged();
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private void NotifyVisibilityStateChanged()
    {
        NotifyOfPropertyChange(nameof(HasMissingSongs));
        NotifyOfPropertyChange(nameof(HasBadMidiFiles));
        NotifyOfPropertyChange(nameof(HasDuplicateConflicts));
        NotifyOfPropertyChange(nameof(HasRemovedExistingMidiFiles));
    }

    private void RebuildDuplicateGroups()
    {
        DuplicateGroups.Clear();

        var groupedDuplicates = _duplicateEntries
            .GroupBy(entry => entry.ExistingPath, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var duplicateGroup in groupedDuplicates)
        {
            string? selectedDuplicatePath = null;
            foreach (var entry in duplicateGroup)
            {
                if (!entry.UseDuplicate)
                    continue;

                if (selectedDuplicatePath is null)
                {
                    selectedDuplicatePath = entry.DuplicatePath;
                    continue;
                }

                entry.UseDuplicate = false;
            }

            var groupViewModel = new DuplicateGroupViewModel
            {
                ExistingPath = duplicateGroup.Key,
                ExistingFileName = System.IO.Path.GetFileName(duplicateGroup.Key),
                UseCurrentVersion = string.IsNullOrWhiteSpace(selectedDuplicatePath)
            };

            foreach (var duplicateEntry in duplicateGroup.OrderBy(entry => entry.DuplicateFileName, StringComparer.OrdinalIgnoreCase))
            {
                groupViewModel.DuplicateOptions.Add(new DuplicateOptionViewModel
                {
                    ExistingPath = duplicateGroup.Key,
                    DuplicatePath = duplicateEntry.DuplicatePath,
                    DuplicateFileName = duplicateEntry.DuplicateFileName,
                    IsSelected = string.Equals(selectedDuplicatePath, duplicateEntry.DuplicatePath, StringComparison.OrdinalIgnoreCase)
                });
            }

            DuplicateGroups.Add(groupViewModel);
        }

        NotifyOfPropertyChange(nameof(HasDuplicateConflicts));
    }

    private void SelectDuplicateVersion(string existingPath, string? selectedDuplicatePath)
    {
        var groupEntries = _duplicateEntries
            .Where(entry => string.Equals(entry.ExistingPath, existingPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var entry in groupEntries)
        {
            entry.UseDuplicate = !string.IsNullOrWhiteSpace(selectedDuplicatePath)
                                 && string.Equals(entry.DuplicatePath, selectedDuplicatePath, StringComparison.OrdinalIgnoreCase);
        }

        RebuildDuplicateGroups();
    }

    private async void OnRemoveMissingSongClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Song song })
            return;

        await _removeMissingSongAsync(song);
    }

    private async void OnRemoveBadMidiSongClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SongsViewModel.BadMidiFileEntry badMidiFile })
            return;

        await _removeBadMidiSongAsync(badMidiFile);
    }

    private void OnUseCurrentVersionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DuplicateGroupViewModel duplicateGroup })
            return;

        SelectDuplicateVersion(duplicateGroup.ExistingPath, null);
    }

    private void OnUseDuplicateVersionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DuplicateOptionViewModel duplicateOption })
            return;

        SelectDuplicateVersion(duplicateOption.ExistingPath, duplicateOption.DuplicatePath);
    }

    private async void OnRestoreRemovedExistingClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FileService.RemovedExistingMidiFileEntry removedEntry })
            return;

        await _restoreRemovedExistingAsync(removedEntry);
    }

    private async void OnDeleteRemovedExistingClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FileService.RemovedExistingMidiFileEntry removedEntry })
            return;

        await _deleteRemovedExistingAsync(removedEntry);
    }

    private void NotifyOfPropertyChange([CallerMemberName] string? propertyName = null)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

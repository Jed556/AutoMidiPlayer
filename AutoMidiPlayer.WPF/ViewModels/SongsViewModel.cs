using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AutoMidiPlayer.Data.Entities;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Dialogs;
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

    /// <summary>
    /// Whether there are any missing song files
    /// </summary>
    public bool HasMissingSongs => MissingSongs.Count > 0;

    public bool HasFileErrors => MissingSongs.Count > 0 || BadMidiFiles.Count > 0;

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

        await _main.FileService.AddFiles(openFileDialog.FileNames);
    }

    /// <summary>
    /// Show the missing files dialog with individual delete buttons
    /// </summary>
    public async Task ShowMissingFilesDialog()
    {
        if (!HasFileErrors) return;

        var content = new System.Windows.Controls.StackPanel { MinWidth = 460 };
        content.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");

        var headerText = new System.Windows.Controls.TextBlock
        {
            Text = "File errors were found:",
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        headerText.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        content.Children.Add(headerText);

        System.Windows.Controls.ItemsControl? missingItemsControl = null;
        System.Windows.Controls.ItemsControl? badMidiItemsControl = null;

        void RefreshLists()
        {
            if (missingItemsControl != null)
                missingItemsControl.ItemsSource = MissingSongs.ToList();
            if (badMidiItemsControl != null)
                badMidiItemsControl.ItemsSource = BadMidiFiles.ToList();
        }

        if (MissingSongs.Count > 0)
        {
            var missingHeader = new System.Windows.Controls.TextBlock
            {
                Text = "Missing files",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            missingHeader.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            content.Children.Add(missingHeader);

            missingItemsControl = new System.Windows.Controls.ItemsControl
            {
                ItemsSource = MissingSongs.ToList()
            };

            var missingTemplate = new DataTemplate();
            var missingRowBorderFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            missingRowBorderFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            missingRowBorderFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            missingRowBorderFactory.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

            var missingGridFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
            var missingCol1 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
            missingCol1.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var missingCol2 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
            missingCol2.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, GridLength.Auto);
            missingGridFactory.AppendChild(missingCol1);
            missingGridFactory.AppendChild(missingCol2);

            var missingTextFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            missingTextFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding("Title") { FallbackValue = "Unknown" });
            missingTextFactory.SetValue(System.Windows.Controls.TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            missingTextFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            missingTextFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 0);
            missingTextFactory.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            missingGridFactory.AppendChild(missingTextFactory);

            var missingButtonFactory = new FrameworkElementFactory(typeof(Button));
            missingButtonFactory.SetValue(System.Windows.Controls.Button.ContentProperty, "✕");
            missingButtonFactory.SetResourceReference(FrameworkElement.StyleProperty, "GhostIconButton");
            missingButtonFactory.SetValue(System.Windows.Controls.Button.PaddingProperty, new Thickness(6, 2, 6, 2));
            missingButtonFactory.SetValue(System.Windows.Controls.Button.MarginProperty, new Thickness(8, 0, 0, 0));
            missingButtonFactory.SetValue(System.Windows.Controls.Button.ToolTipProperty, "Remove from database");
            missingButtonFactory.SetValue(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent);
            missingButtonFactory.SetValue(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0));
            missingButtonFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 1);
            missingButtonFactory.AddHandler(System.Windows.Controls.Button.ClickEvent,
                new RoutedEventHandler(async (s, _) =>
                {
                    if (s is System.Windows.Controls.Button btn && btn.DataContext is Song song)
                    {
                        await _main.FileService.RemoveMissingSong(song);
                        RefreshLists();
                    }
                }));
            missingGridFactory.AppendChild(missingButtonFactory);

            missingRowBorderFactory.AppendChild(missingGridFactory);
            missingTemplate.VisualTree = missingRowBorderFactory;
            missingItemsControl.ItemTemplate = missingTemplate;

            var missingScrollViewer = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 200,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                Content = missingItemsControl
            };
            missingScrollViewer.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlFillColorDefaultBrush");
            missingScrollViewer.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            content.Children.Add(missingScrollViewer);
        }

        if (BadMidiFiles.Count > 0)
        {
            var badMidiHeader = new System.Windows.Controls.TextBlock
            {
                Text = "Bad MIDI files",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, MissingSongs.Count > 0 ? 14 : 0, 0, 8)
            };
            badMidiHeader.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            content.Children.Add(badMidiHeader);

            badMidiItemsControl = new System.Windows.Controls.ItemsControl
            {
                ItemsSource = BadMidiFiles.ToList()
            };

            var badMidiTemplate = new DataTemplate();
            var badMidiRowBorderFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            badMidiRowBorderFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(10, 8, 10, 8));
            badMidiRowBorderFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
            badMidiRowBorderFactory.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

            var badMidiGridFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Grid));
            var badMidiCol1 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
            badMidiCol1.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var badMidiCol2 = new FrameworkElementFactory(typeof(System.Windows.Controls.ColumnDefinition));
            badMidiCol2.SetValue(System.Windows.Controls.ColumnDefinition.WidthProperty, GridLength.Auto);
            badMidiGridFactory.AppendChild(badMidiCol1);
            badMidiGridFactory.AppendChild(badMidiCol2);

            var detailsPanelFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.StackPanel));
            detailsPanelFactory.SetValue(System.Windows.Controls.StackPanel.OrientationProperty, System.Windows.Controls.Orientation.Vertical);
            detailsPanelFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 0);

            var badMidiTitleFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            badMidiTitleFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding("Title") { FallbackValue = "Unknown" });
            badMidiTitleFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            badMidiTitleFactory.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            detailsPanelFactory.AppendChild(badMidiTitleFactory);

            var badMidiPathFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            badMidiPathFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty,
                new System.Windows.Data.Binding("Path") { FallbackValue = string.Empty });
            badMidiPathFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            badMidiPathFactory.SetValue(System.Windows.Controls.TextBlock.FontSizeProperty, 11.0);
            badMidiPathFactory.SetValue(System.Windows.Controls.TextBlock.OpacityProperty, 0.72);
            badMidiPathFactory.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextFillColorPrimaryBrush");
            detailsPanelFactory.AppendChild(badMidiPathFactory);

            badMidiGridFactory.AppendChild(detailsPanelFactory);

            var badMidiButtonFactory = new FrameworkElementFactory(typeof(Button));
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.ContentProperty, "✕");
            badMidiButtonFactory.SetResourceReference(FrameworkElement.StyleProperty, "GhostIconButton");
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.PaddingProperty, new Thickness(6, 2, 6, 2));
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.MarginProperty, new Thickness(8, 0, 0, 0));
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.ToolTipProperty, "Remove from database");
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.BackgroundProperty, Brushes.Transparent);
            badMidiButtonFactory.SetValue(System.Windows.Controls.Button.BorderThicknessProperty, new Thickness(0));
            badMidiButtonFactory.SetValue(System.Windows.Controls.Grid.ColumnProperty, 1);
            badMidiButtonFactory.AddHandler(System.Windows.Controls.Button.ClickEvent,
                new RoutedEventHandler(async (s, _) =>
                {
                    if (s is System.Windows.Controls.Button btn && btn.DataContext is BadMidiFileEntry badMidiFile)
                    {
                        await _main.FileService.RemoveBadMidiSong(badMidiFile);
                        RefreshLists();
                    }
                }));
            badMidiGridFactory.AppendChild(badMidiButtonFactory);

            badMidiRowBorderFactory.AppendChild(badMidiGridFactory);
            badMidiTemplate.VisualTree = badMidiRowBorderFactory;
            badMidiItemsControl.ItemTemplate = badMidiTemplate;

            var badMidiScrollViewer = new System.Windows.Controls.ScrollViewer
            {
                MaxHeight = 220,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Disabled,
                Content = badMidiItemsControl
            };
            badMidiScrollViewer.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "ControlFillColorDefaultBrush");
            badMidiScrollViewer.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
            content.Children.Add(badMidiScrollViewer);
        }

        var dialog = DialogHelper.CreateDialog();
        dialog.Title = "File Errors";
        dialog.Content = content;
        dialog.PrimaryButtonText = "Remove All";
        dialog.PrimaryButtonAppearance = ControlAppearance.Danger;
        dialog.CloseButtonText = "Close";

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
            await _main.FileService.RemoveAllFileErrors();
    }

    public void NotifyFileErrorsChanged()
    {
        NotifyOfPropertyChange(nameof(HasMissingSongs));
        NotifyOfPropertyChange(nameof(MissingSongs));
        NotifyOfPropertyChange(nameof(BadMidiFiles));
        NotifyOfPropertyChange(nameof(HasFileErrors));
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
            // Otherwise, add to queue and play this song
            PlaySong(file);
            await _main.PlaybackControls.PlayPause();
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

        await _main.SongSettings.EditSongAsync(file);
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

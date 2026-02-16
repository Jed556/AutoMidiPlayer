using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.WPF.Controls;
using AutoMidiPlayer.WPF.Helpers;
using AutoMidiPlayer.WPF.ViewModels;

namespace AutoMidiPlayer.WPF.Views;

public partial class QueueView : UserControl
{
    private ListViewDragDropHelper? _dragDropHelper;

    public QueueView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is QueueViewModel viewModel && _dragDropHelper == null)
        {
            _dragDropHelper = new ListViewDragDropHelper(
                SongList.ListView,
                viewModel.Tracks,
                viewModel.OnQueueModified);
        }
    }

    /// <summary>
    /// Handle double-click on a track - plays the song
    /// </summary>
    private void TrackList_ItemDoubleClick(object sender, RoutedEventArgs e)
    {
        if (e is SongListEventArgs args && DataContext is QueueViewModel viewModel)
        {
            viewModel.PlayPauseFromQueue(args.File);
        }
    }

    /// <summary>
    /// Handle play/pause button click from SongListControl
    /// </summary>
    private void TrackList_PlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (e is SongListEventArgs args && DataContext is QueueViewModel viewModel)
        {
            viewModel.PlayPauseFromQueue(args.File);
        }
    }

    /// <summary>
    /// Handle menu button click from SongListControl
    /// </summary>
    private void TrackList_MenuClick(object sender, RoutedEventArgs e)
    {
        // Menu is automatically opened by SongListControl
    }

    /// <summary>
    /// Remove selected songs from queue
    /// </summary>
    private void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is QueueViewModel viewModel)
        {
            viewModel.RemoveTrack(SongList.SelectedFiles);
        }
    }

    /// <summary>
    /// Edit selected song (single selection only)
    /// </summary>
    private async void EditSong_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is QueueViewModel viewModel)
        {
            var file = SongList.SelectedFiles.Count == 1 ? SongList.SelectedFiles[0] : viewModel.SelectedFile;
            if (file is not null)
                await viewModel.EditSong(file);
        }
    }

    /// <summary>
    /// Delete selected songs from library
    /// </summary>
    private async void DeleteSong_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is QueueViewModel viewModel)
        {
            await viewModel.DeleteSongs(SongList.SelectedFiles);
        }
    }
}

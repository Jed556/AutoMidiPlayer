using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.WPF.ModernWPF;
using AutoMidiPlayer.WPF.ViewModels;

namespace AutoMidiPlayer.WPF.Views;

public partial class SongsView : UserControl
{
    private ListViewDragDropHelper? _dragDropHelper;

    public SongsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SongsViewModel viewModel && _dragDropHelper == null)
        {
            _dragDropHelper = new ListViewDragDropHelper(
                TrackList.ListView,
                viewModel.Tracks,
                viewModel.ApplySort);
        }
    }

    /// <summary>
    /// Handle play/pause button click from TrackListControl
    /// </summary>
    private void TrackList_PlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (e is TrackListEventArgs args && DataContext is SongsViewModel viewModel)
        {
            viewModel.PlayPauseFromSongs(args.File);
        }
    }

    /// <summary>
    /// Handle menu button click from TrackListControl
    /// </summary>
    private void TrackList_MenuClick(object sender, RoutedEventArgs e)
    {
        // Menu is automatically opened by TrackListControl
    }

    /// <summary>
    /// Handle double-click on a track - plays the song
    /// </summary>
    private void TrackList_ItemDoubleClick(object sender, RoutedEventArgs e)
    {
        if (e is TrackListEventArgs args && DataContext is SongsViewModel viewModel)
        {
            viewModel.PlayPauseFromSongs(args.File);
        }
    }

    /// <summary>
    /// Add selected songs to queue
    /// </summary>
    private void AddToQueue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SongsViewModel viewModel)
        {
            viewModel.AddSelectedToQueue(TrackList.SelectedFiles);
        }
    }

    /// <summary>
    /// Edit selected song (single selection only)
    /// </summary>
    private async void EditSong_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SongsViewModel viewModel)
        {
            await viewModel.EditSelected(TrackList.SelectedFiles);
        }
    }

    /// <summary>
    /// Delete selected songs
    /// </summary>
    private async void DeleteSong_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SongsViewModel viewModel)
        {
            await viewModel.DeleteSelected(TrackList.SelectedFiles);
        }
    }
}

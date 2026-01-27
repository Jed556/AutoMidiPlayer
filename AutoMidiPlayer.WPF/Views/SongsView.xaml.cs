using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.Data.Midi;
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
                SongsListView,
                viewModel.Tracks,
                viewModel.ApplySort);
        }
    }

    /// <summary>
    /// Sync selected items to the ViewModel's SelectedFiles collection
    /// </summary>
    private void SongsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is SongsViewModel viewModel)
        {
            viewModel.SelectedFiles.Clear();
            foreach (var item in SongsListView.SelectedItems)
            {
                if (item is MidiFile file)
                {
                    viewModel.SelectedFiles.Add(file);
                }
            }
        }
    }

    /// <summary>
    /// Open context menu when 3-dot menu button is clicked
    /// </summary>
    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            DataContext is SongsViewModel viewModel)
        {
            // Select the clicked item if not already selected
            if (button.Tag is MidiFile file && !viewModel.SelectedFiles.Contains(file))
            {
                SongsListView.SelectedItem = file;
            }

            // Open the ListView's context menu
            SongsListView.ContextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// Add selected songs to queue
    /// </summary>
    private void AddToQueue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SongsViewModel viewModel)
        {
            viewModel.AddSelectedToQueue();
        }
    }

    /// <summary>
    /// Edit selected song (single selection only)
    /// </summary>
    private async void EditSong_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SongsViewModel viewModel)
        {
            await viewModel.EditSelected();
        }
    }

    /// <summary>
    /// Delete selected songs
    /// </summary>
    private async void DeleteSong_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SongsViewModel viewModel)
        {
            await viewModel.DeleteSelected();
        }
    }
}

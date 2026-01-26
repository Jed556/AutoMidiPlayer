using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    /// Show play button and menu button when row is hovered
    /// </summary>
    private void ListViewItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is ListViewItem item)
        {
            var playButton = FindChildByName<Button>(item, "PlayButton");
            var menuButton = FindChildByName<Button>(item, "MenuButton");
            var positionText = FindChildByName<TextBlock>(item, "PositionText");

            if (playButton != null) playButton.Visibility = Visibility.Visible;
            if (menuButton != null) menuButton.Visibility = Visibility.Visible;
            if (positionText != null) positionText.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Hide play button and menu button when row is no longer hovered
    /// </summary>
    private void ListViewItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is ListViewItem item)
        {
            var playButton = FindChildByName<Button>(item, "PlayButton");
            var menuButton = FindChildByName<Button>(item, "MenuButton");
            var positionText = FindChildByName<TextBlock>(item, "PositionText");

            if (playButton != null) playButton.Visibility = Visibility.Collapsed;
            if (menuButton != null) menuButton.Visibility = Visibility.Collapsed;
            if (positionText != null) positionText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Play the specific song when play button is clicked
    /// </summary>
    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is MidiFile file &&
            DataContext is SongsViewModel viewModel)
        {
            viewModel.PlaySong(file);
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

    /// <summary>
    /// Helper method to find a child element by name
    /// </summary>
    private static T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T frameworkElement && frameworkElement.Name == name)
            {
                return frameworkElement;
            }

            var result = FindChildByName<T>(child, name);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }
}

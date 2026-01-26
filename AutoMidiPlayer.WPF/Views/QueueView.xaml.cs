using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.WPF.ModernWPF;
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
                QueueListView,
                viewModel.Tracks,
                viewModel.OnQueueModified);
        }
    }
}

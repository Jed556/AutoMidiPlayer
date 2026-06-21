using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoMidiPlayer.WPF.Services.MidiShow;
using AutoMidiPlayer.WPF.ViewModels;

namespace AutoMidiPlayer.WPF.Views;

public partial class OnlineMidiView : UserControl
{
    public OnlineMidiView()
    {
        InitializeComponent();
    }

    private OnlineMidiViewModel? ViewModel => DataContext as OnlineMidiViewModel;

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is { } vm)
        {
            e.Handled = true;
            _ = vm.Search();
        }
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ViewModel is { } vm)
        {
            e.Handled = true;
            _ = vm.AddPasswordAccount();
        }
    }

    private void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is FrameworkElement { DataContext: MidiShowAccountRow row })
            vm.RemoveAccount(row);
    }

    private void CopyCookies_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is FrameworkElement { DataContext: MidiShowAccountRow row })
            vm.CopyCookies(row);
    }

    private void AddToSongs_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        if (sender is FrameworkElement { DataContext: MidiShowItem item })
            _ = vm.AddToSongsAsync(item);
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        if (sender is FrameworkElement { DataContext: MidiShowItem item })
            _ = vm.PreviewAsync(item);
    }

    private void PreviewSeek_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        => ViewModel?.BeginPreviewScrub();

    private void PreviewSeek_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => ViewModel?.EndPreviewScrub();

    private void PreviewSeek_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => ViewModel?.EndPreviewScrub();

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is MenuItem { Tag: string key })
            _ = vm.SetSort(key);
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && sender is MenuItem { Tag: string slug } item)
            _ = vm.SetCategory(slug, item.Header?.ToString() ?? "");
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        if (sender is FrameworkElement { DataContext: MidiShowItem item })
            _ = vm.ShowDetailsAsync(item);
    }

}

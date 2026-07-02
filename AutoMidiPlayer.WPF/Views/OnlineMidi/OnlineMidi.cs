using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoMidiPlayer.WPF.Helpers;
using AutoMidiPlayer.WPF.Services.MidiShow;
using AutoMidiPlayer.WPF.ViewModels;

namespace AutoMidiPlayer.WPF.Views;

public partial class OnlineMidiView : UserControl
{
    private ScrollViewer? _resultsScrollViewer;
    private SmoothScrollAnimator? _scrollAnimator;

    public OnlineMidiView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        ResultsList.Loaded += ResultsList_Loaded;
    }

    private void ResultsList_Loaded(object sender, RoutedEventArgs e)
    {
        if (_resultsScrollViewer != null)
        {
            _resultsScrollViewer.PreviewMouseWheel -= ResultsScrollViewer_PreviewMouseWheel;
            _resultsScrollViewer.PreviewMouseDown -= ResultsScrollViewer_PreviewMouseDown;
        }

        _resultsScrollViewer = FindVisualChild<ScrollViewer>(ResultsList);
        if (_resultsScrollViewer is null)
            return;

        // Apply the custom scrollbar and smooth scroll behavior programmatically,
        // because the WPF UI library's ListBox template prevents the global implicit
        // ScrollViewer style (from BaseStyles.xaml) from reaching the internal ScrollViewer.
        ScrollViewerAutoFadeBehavior.SetIsEnabled(_resultsScrollViewer, true);
        ScrollEdgeFadeBehavior.SetIsEnabled(_resultsScrollViewer, true);
        _resultsScrollViewer.Padding = new Thickness(0, 0, 12, 0);

        _scrollAnimator = new SmoothScrollAnimator(_resultsScrollViewer, SmoothScrollAnimatorOptions.Default);
        _resultsScrollViewer.PreviewMouseWheel += ResultsScrollViewer_PreviewMouseWheel;
        _resultsScrollViewer.PreviewMouseDown += ResultsScrollViewer_PreviewMouseDown;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is OnlineMidiViewModel oldVm)
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
        if (e.NewValue is OnlineMidiViewModel newVm)
            newVm.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OnlineMidiViewModel.CurrentPage))
        {
            ScrollToTop();
        }
    }

    private void ScrollToTop()
    {
        if (_scrollAnimator != null && _resultsScrollViewer != null)
        {
            _scrollAnimator.SyncTargetToCurrentOffset();
            _scrollAnimator.SetTargetOffset(0, startIfNeeded: true, immediateStep: false);
        }
        else
        {
            _resultsScrollViewer?.ScrollToTop();
        }
    }

    private void ResultsScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        _scrollAnimator?.Stop();
    }

    private void ResultsScrollViewer_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _scrollAnimator?.Stop();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;

            var descendent = FindVisualChild<T>(child);
            if (descendent != null)
                return descendent;
        }
        return null;
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

    private void Card_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
            return;

        if (sender is FrameworkElement { DataContext: MidiShowItem item })
            _ = vm.ToggleDetailsAsync(item);
    }


}

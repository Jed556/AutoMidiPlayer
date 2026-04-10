using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoMidiPlayer.WPF.Controls;

public partial class SelectorPopupButton : UserControl
{
    public static readonly DependencyProperty ButtonContentProperty =
        DependencyProperty.Register(nameof(ButtonContent), typeof(object), typeof(SelectorPopupButton));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SelectorPopupButton));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(SelectorPopupButton),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedItemChanged));

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(SelectorPopupButton));

    public static readonly DependencyProperty PopupMaxWidthProperty =
        DependencyProperty.Register(nameof(PopupMaxWidth), typeof(double), typeof(SelectorPopupButton), new PropertyMetadata(220.0));

    public static readonly DependencyProperty PopupMaxHeightProperty =
        DependencyProperty.Register(nameof(PopupMaxHeight), typeof(double), typeof(SelectorPopupButton), new PropertyMetadata(320.0));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(SelectorPopupButton), new PropertyMetadata(false));

    public SelectorPopupButton()
    {
        InitializeComponent();
    }

    public object? ButtonContent
    {
        get => GetValue(ButtonContentProperty);
        set => SetValue(ButtonContentProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public double PopupMaxWidth
    {
        get => (double)GetValue(PopupMaxWidthProperty);
        set => SetValue(PopupMaxWidthProperty, value);
    }

    public double PopupMaxHeight
    {
        get => (double)GetValue(PopupMaxHeightProperty);
        set => SetValue(PopupMaxHeightProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private void SelectorButton_Click(object sender, RoutedEventArgs e)
    {
        SelectorPopup.IsOpen = !SelectorPopup.IsOpen;
        e.Handled = true;
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SelectorPopupButton selector)
            selector.QueueCenterSelectedItem();
    }

    private void SelectorPopup_Opened(object sender, EventArgs e)
    {
        QueueCenterSelectedItem();
    }

    private void QueueCenterSelectedItem()
    {
        if (!SelectorPopup.IsOpen)
            return;

        _ = Dispatcher.BeginInvoke(CenterSelectedItemIfScrollable, DispatcherPriority.Loaded);
    }

    private void CenterSelectedItemIfScrollable()
    {
        if (!SelectorPopup.IsOpen || SelectorListBox.SelectedItem is null)
            return;

        SelectorListBox.UpdateLayout();
        SelectorListBox.ScrollIntoView(SelectorListBox.SelectedItem);
        SelectorListBox.UpdateLayout();

        if (FindDescendant<ScrollViewer>(SelectorListBox) is not ScrollViewer scrollViewer)
            return;

        if (scrollViewer.ScrollableHeight <= 0 || scrollViewer.ViewportHeight <= 0)
            return;

        int selectedIndex = SelectorListBox.SelectedIndex;
        if (selectedIndex < 0)
            return;

        // With logical scrolling enabled, ScrollViewer offsets are measured in item units.
        if (ScrollViewer.GetCanContentScroll(SelectorListBox))
        {
            double logicalOffset = selectedIndex - (scrollViewer.ViewportHeight / 2) + 0.5;
            double clampedLogicalOffset = Math.Clamp(logicalOffset, 0, scrollViewer.ScrollableHeight);
            scrollViewer.ScrollToVerticalOffset(clampedLogicalOffset);
            return;
        }

        if (SelectorListBox.ItemContainerGenerator.ContainerFromIndex(selectedIndex) is not FrameworkElement itemContainer)
            return;

        if (FindDescendant<ScrollContentPresenter>(scrollViewer) is not ScrollContentPresenter presenter)
            return;

        Point itemTop = itemContainer.TransformToAncestor(presenter).Transform(new Point(0, 0));
        double itemCenter = itemTop.Y + (itemContainer.ActualHeight / 2);
        double viewportCenter = presenter.ActualHeight / 2;
        double delta = itemCenter - viewportCenter;
        double targetOffset = Math.Clamp(scrollViewer.VerticalOffset + delta, 0, scrollViewer.ScrollableHeight);

        scrollViewer.ScrollToVerticalOffset(targetOffset);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childrenCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T target)
                return target;

            T? nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void SelectorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectorPopup.IsOpen && e.AddedItems.Count > 0)
            SelectorPopup.IsOpen = false;
    }
}

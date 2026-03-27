using System.Collections;
using System.Windows;
using System.Windows.Controls;

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
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

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

    private void SelectorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectorPopup.IsOpen && e.AddedItems.Count > 0)
            SelectorPopup.IsOpen = false;
    }
}

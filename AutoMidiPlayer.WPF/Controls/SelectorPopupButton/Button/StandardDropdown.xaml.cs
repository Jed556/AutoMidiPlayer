using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Globalization;
using System.Windows.Threading;

namespace AutoMidiPlayer.WPF.Controls.SelectorPopupButton.Button;

public class DisplayMemberConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var item = values.Length > 0 ? values[0] : null;
        var path = values.Length > 1 ? values[1] as string : null;

        if (item == null)
            return string.Empty;

        if (ReferenceEquals(item, DependencyProperty.UnsetValue))
            return string.Empty;

        if (string.IsNullOrEmpty(path))
            return item.ToString() ?? string.Empty;

        var resolved = ResolvePath(item, path);
        return resolved?.ToString() ?? item.ToString() ?? string.Empty;
    }

    private static object? ResolvePath(object source, string path)
    {
        object? current = source;

        foreach (var segment in path.Split('.'))
        {
            if (current is null)
                return null;

            if (current is IDictionary dictionary)
            {
                if (!dictionary.Contains(segment))
                    return null;

                current = dictionary[segment];
                continue;
            }

            var currentType = current.GetType();
            var property = currentType.GetProperty(segment);
            if (property is not null)
            {
                current = property.GetValue(current, null);
                continue;
            }

            var field = currentType.GetField(segment);
            if (field is not null)
            {
                current = field.GetValue(current);
                continue;
            }

            return null;
        }

        return current;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class TemplateFallbackConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is DataTemplate selectedTemplate)
            return selectedTemplate;

        if (values.Length > 1 && values[1] is DataTemplate itemTemplate)
            return itemTemplate;

        if (values.Length > 2 && values[2] is DataTemplate defaultTemplate)
            return defaultTemplate;

        return DependencyProperty.UnsetValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class NullToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public partial class StandardDropdown : UserControl
{
    public static readonly DependencyProperty ButtonContentProperty =
        DependencyProperty.Register(nameof(ButtonContent), typeof(object), typeof(StandardDropdown));

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(StandardDropdown));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(
            nameof(SelectedItem),
            typeof(object),
            typeof(StandardDropdown),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty ItemTemplateProperty =
        DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(StandardDropdown));

    public static readonly DependencyProperty SelectedItemTemplateProperty =
        DependencyProperty.Register(nameof(SelectedItemTemplate), typeof(DataTemplate), typeof(StandardDropdown));

    public static readonly DependencyProperty PopupMaxWidthProperty =
        DependencyProperty.Register(nameof(PopupMaxWidth), typeof(double), typeof(StandardDropdown), new PropertyMetadata(400.0));

    public static readonly DependencyProperty PopupMaxHeightProperty =
        DependencyProperty.Register(nameof(PopupMaxHeight), typeof(double), typeof(StandardDropdown), new PropertyMetadata(320.0));

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(StandardDropdown), new PropertyMetadata(null));

    public static readonly DependencyProperty IsGlowActiveProperty = DependencyProperty.Register(
        nameof(IsGlowActive),
        typeof(bool),
        typeof(StandardDropdown),
        new PropertyMetadata(false));

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedIndex),
            typeof(int),
            typeof(StandardDropdown),
            new FrameworkPropertyMetadata(
                -1,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly RoutedEvent SelectionChangedEvent = EventManager.RegisterRoutedEvent(
        nameof(SelectionChanged), RoutingStrategy.Bubble, typeof(SelectionChangedEventHandler), typeof(StandardDropdown));

    private readonly DispatcherTimer _glowTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(450)
    };
    private bool _isLoaded = false;

    public event SelectionChangedEventHandler SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    public StandardDropdown()
    {
        InitializeComponent();

        _glowTimer.Tick += (_, _) =>
        {
            _glowTimer.Stop();
            IsGlowActive = false;
        };

        Loaded += (_, _) => _isLoaded = true;
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

    public DataTemplate? SelectedItemTemplate
    {
        get => (DataTemplate?)GetValue(SelectedItemTemplateProperty);
        set => SetValue(SelectedItemTemplateProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public bool IsGlowActive
    {
        get => (bool)GetValue(IsGlowActiveProperty);
        set => SetValue(IsGlowActiveProperty, value);
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

    private void SelectorButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!MenuPopup.IsOpen)
            return;

        MenuPopup.IsOpen = false;
        e.Handled = true;
    }

    private void SelectorButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePopup();
        e.Handled = true;
    }

    private void TogglePopup()
    {
        MenuPopup.IsOpen = !MenuPopup.IsOpen;
    }

    private void MenuPopup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RaiseEvent(new SelectionChangedEventArgs(SelectionChangedEvent, e.RemovedItems, e.AddedItems));
        if (_isLoaded)
            TriggerGlow();
    }

    private void TriggerGlow()
    {
        IsGlowActive = true;
        _glowTimer.Stop();
        _glowTimer.Start();
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (MenuPopup.IsOpen && !MenuPopup.IsMouseOverPopup(e))
        {
            MenuPopup.IsOpen = false;
            base.OnPreviewMouseWheel(e);
            return;
        }

        base.OnPreviewMouseWheel(e);
    }
}

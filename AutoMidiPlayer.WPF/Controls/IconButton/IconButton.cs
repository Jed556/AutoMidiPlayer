using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoMidiPlayer.WPF.Controls;

public class IconButton : Button
{
    static IconButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(IconButton),
            new FrameworkPropertyMetadata(typeof(IconButton))
        );
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(IconButton),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(IconButton),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ActiveColorBrushProperty =
        DependencyProperty.Register(
            nameof(ActiveColorBrush),
            typeof(Brush),
            typeof(IconButton),
            new PropertyMetadata(null));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Brush? ActiveColorBrush
    {
        get => (Brush?)GetValue(ActiveColorBrushProperty);
        set => SetValue(ActiveColorBrushProperty, value);
    }
}

using System.Windows;
using System.Windows.Controls;

namespace AutoMidiPlayer.WPF.Controls;

/// <summary>
/// A reusable control that wraps a WPF-UI ToggleSwitch with an optional header above it.
/// WPF-UI's ToggleSwitch only supports Content on left/right of the toggle,
/// so this control adds a header label positioned above for section grouping.
/// </summary>
public partial class HeaderToggleSwitch : UserControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(HeaderToggleSwitch),
            new PropertyMetadata(string.Empty, OnHeaderChanged));

    public static readonly DependencyProperty HeaderFontSizeProperty =
        DependencyProperty.Register(nameof(HeaderFontSize), typeof(double), typeof(HeaderToggleSwitch),
            new PropertyMetadata(14.0));

    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(HeaderToggleSwitch),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty HasHeaderProperty =
        DependencyProperty.Register(nameof(HasHeader), typeof(bool), typeof(HeaderToggleSwitch),
            new PropertyMetadata(false));

    public HeaderToggleSwitch()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Header text displayed above the toggle switch.
    /// </summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Font size for the header text. Defaults to 14.
    /// </summary>
    public double HeaderFontSize
    {
        get => (double)GetValue(HeaderFontSizeProperty);
        set => SetValue(HeaderFontSizeProperty, value);
    }

    /// <summary>
    /// Whether the toggle switch is checked.
    /// </summary>
    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>
    /// Whether the header is visible (auto-set when Header is non-empty).
    /// </summary>
    public bool HasHeader
    {
        get => (bool)GetValue(HasHeaderProperty);
        private set => SetValue(HasHeaderProperty, value);
    }

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeaderToggleSwitch control)
            control.HasHeader = !string.IsNullOrEmpty(e.NewValue as string);
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Controls;

/// <summary>
/// A button that displays an icon and smoothly rotates it
/// by <see cref="RotationAngle"/> degrees when <see cref="IsRotated"/> is true.
/// Useful for accordion chevrons, sort-direction arrows, and filter toggles.
/// Supports both <see cref="SymbolIcon"/> (via <see cref="IconSymbol"/>) and
/// <see cref="FontIcon"/> (via <see cref="Glyph"/>/<see cref="GlyphFontFamily"/>).
/// </summary>
public partial class RotateToggleButton : UserControl
{
    // ── Dependency Properties ──────────────────────────────────────────

    public static readonly DependencyProperty IsRotatedProperty =
        DependencyProperty.Register(
            nameof(IsRotated),
            typeof(bool),
            typeof(RotateToggleButton),
            new FrameworkPropertyMetadata(false, OnIsRotatedChanged));

    public static readonly DependencyProperty RotationAngleProperty =
        DependencyProperty.Register(
            nameof(RotationAngle),
            typeof(double),
            typeof(RotateToggleButton),
            new PropertyMetadata(180.0));

    public static readonly DependencyProperty AnimationDurationProperty =
        DependencyProperty.Register(
            nameof(AnimationDuration),
            typeof(Duration),
            typeof(RotateToggleButton),
            new PropertyMetadata(new Duration(TimeSpan.FromSeconds(0.18))));

    public static readonly DependencyProperty IconSymbolProperty =
        DependencyProperty.Register(
            nameof(IconSymbol),
            typeof(SymbolRegular?),
            typeof(RotateToggleButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IconFontSizeProperty =
        DependencyProperty.Register(
            nameof(IconFontSize),
            typeof(double),
            typeof(RotateToggleButton),
            new PropertyMetadata(18.0));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            nameof(Glyph),
            typeof(string),
            typeof(RotateToggleButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty GlyphFontFamilyProperty =
        DependencyProperty.Register(
            nameof(GlyphFontFamily),
            typeof(FontFamily),
            typeof(RotateToggleButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty GlyphFontSizeProperty =
        DependencyProperty.Register(
            nameof(GlyphFontSize),
            typeof(double),
            typeof(RotateToggleButton),
            new PropertyMetadata(12.0));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(System.Windows.Input.ICommand),
            typeof(RotateToggleButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(
            nameof(CommandParameter),
            typeof(object),
            typeof(RotateToggleButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ButtonToolTipProperty =
        DependencyProperty.Register(
            nameof(ButtonToolTip),
            typeof(object),
            typeof(RotateToggleButton),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ButtonPaddingProperty =
        DependencyProperty.Register(
            nameof(ButtonPadding),
            typeof(Thickness),
            typeof(RotateToggleButton),
            new PropertyMetadata(new Thickness(0)));

    // ── CLR Properties ─────────────────────────────────────────────────

    /// <summary>Whether the icon is currently in the rotated state.</summary>
    public bool IsRotated
    {
        get => (bool)GetValue(IsRotatedProperty);
        set => SetValue(IsRotatedProperty, value);
    }

    /// <summary>The angle (in degrees) to rotate to when <see cref="IsRotated"/> is true. Defaults to 180.</summary>
    public double RotationAngle
    {
        get => (double)GetValue(RotationAngleProperty);
        set => SetValue(RotationAngleProperty, value);
    }

    /// <summary>Duration of the rotation animation. Defaults to 0.18s.</summary>
    public Duration AnimationDuration
    {
        get => (Duration)GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    /// <summary>SymbolRegular icon to display. If set, takes priority over <see cref="Glyph"/>.</summary>
    public SymbolRegular? IconSymbol
    {
        get => (SymbolRegular?)GetValue(IconSymbolProperty);
        set => SetValue(IconSymbolProperty, value);
    }

    /// <summary>The font size of the SymbolIcon. Defaults to 18.</summary>
    public double IconFontSize
    {
        get => (double)GetValue(IconFontSizeProperty);
        set => SetValue(IconFontSizeProperty, value);
    }

    /// <summary>FontIcon glyph string (e.g. "&#xE74A;"). Used when <see cref="IconSymbol"/> is null.</summary>
    public string? Glyph
    {
        get => (string?)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>FontFamily for the glyph. Required when using <see cref="Glyph"/>.</summary>
    public FontFamily? GlyphFontFamily
    {
        get => (FontFamily?)GetValue(GlyphFontFamilyProperty);
        set => SetValue(GlyphFontFamilyProperty, value);
    }

    /// <summary>The font size of the FontIcon glyph. Defaults to 12.</summary>
    public double GlyphFontSize
    {
        get => (double)GetValue(GlyphFontSizeProperty);
        set => SetValue(GlyphFontSizeProperty, value);
    }

    /// <summary>Command to execute when the button is clicked.</summary>
    public System.Windows.Input.ICommand? Command
    {
        get => (System.Windows.Input.ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>Parameter passed to the command.</summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>ToolTip for the inner button.</summary>
    public object? ButtonToolTip
    {
        get => GetValue(ButtonToolTipProperty);
        set => SetValue(ButtonToolTipProperty, value);
    }

    /// <summary>Padding for the inner button.</summary>
    public Thickness ButtonPadding
    {
        get => (Thickness)GetValue(ButtonPaddingProperty);
        set => SetValue(ButtonPaddingProperty, value);
    }

    // ── Constructor ────────────────────────────────────────────────────

    public RotateToggleButton()
    {
        InitializeComponent();
        Unloaded += (_, _) => 
        {
            IconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        };
    }

    // ── XAML event handlers ────────────────────────────────────────────

    private void Icon_Loaded(object sender, RoutedEventArgs e)
    {
        // Show FontIcon when Glyph is provided, otherwise show SymbolIcon
        var useGlyph = !string.IsNullOrEmpty(Glyph);
        SymbolIconElement.Visibility = useGlyph ? Visibility.Collapsed : Visibility.Visible;
        GlyphIconElement.Visibility = useGlyph ? Visibility.Visible : Visibility.Collapsed;

        // Sync initial state without animating
        IconRotation.BeginAnimation(RotateTransform.AngleProperty, null);
        IconRotation.Angle = IsRotated ? RotationAngle : 0.0;
    }

    // ── Animation callback ─────────────────────────────────────────────

    private static void OnIsRotatedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RotateToggleButton btn)
            btn.AnimateRotation((bool)e.NewValue);
    }

    private void AnimateRotation(bool rotated)
    {
        var targetAngle = rotated ? RotationAngle : 0.0;
        var animation = new DoubleAnimation
        {
            To = targetAngle,
            Duration = AnimationDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        IconRotation.BeginAnimation(RotateTransform.AngleProperty, animation);
    }
}

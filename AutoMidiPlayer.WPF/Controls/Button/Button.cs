using System;
using System.Windows;
using Wpf.Ui.Controls; // For SymbolRegular

namespace AutoMidiPlayer.WPF.Controls;

public enum ButtonVariant
{
    Filled,
    Outlined,
    Ghost,
    GhostDimmed
}

public enum ButtonColorMode
{
    Default,
    Accent,
    Warning,
    Danger
}

public enum IconPlacement
{
    Left,
    Right
}

public class Button : System.Windows.Controls.Button
{
    static Button()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Button), new FrameworkPropertyMetadata(typeof(Button)));
    }

    public static readonly DependencyProperty VariantProperty =
        DependencyProperty.Register(
            nameof(Variant), 
            typeof(ButtonVariant), 
            typeof(Button), 
            new PropertyMetadata(ButtonVariant.Filled, OnStylePropertyChanged));

    public static readonly DependencyProperty ColorModeProperty =
        DependencyProperty.Register(
            nameof(ColorMode), 
            typeof(ButtonColorMode), 
            typeof(Button), 
            new PropertyMetadata(ButtonColorMode.Default));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon), 
            typeof(SymbolRegular?), 
            typeof(Button), 
            new PropertyMetadata(null));

    public static readonly DependencyProperty IconPlacementProperty =
        DependencyProperty.Register(
            nameof(IconPlacement), 
            typeof(IconPlacement), 
            typeof(Button), 
            new PropertyMetadata(IconPlacement.Left));

    public static readonly DependencyProperty IconFilledProperty =
        DependencyProperty.Register(
            nameof(IconFilled), 
            typeof(bool), 
            typeof(Button), 
            new PropertyMetadata(false));

    public static readonly DependencyProperty IconFontSizeProperty =
        DependencyProperty.Register(
            nameof(IconFontSize), 
            typeof(double), 
            typeof(Button), 
            new PropertyMetadata(16.0));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive), 
            typeof(bool), 
            typeof(Button), 
            new PropertyMetadata(false));

    public ButtonVariant Variant
    {
        get => (ButtonVariant)GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public ButtonColorMode ColorMode
    {
        get => (ButtonColorMode)GetValue(ColorModeProperty);
        set => SetValue(ColorModeProperty, value);
    }

    public SymbolRegular? Icon
    {
        get => (SymbolRegular?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public IconPlacement IconPlacement
    {
        get => (IconPlacement)GetValue(IconPlacementProperty);
        set => SetValue(IconPlacementProperty, value);
    }

    public bool IconFilled
    {
        get => (bool)GetValue(IconFilledProperty);
        set => SetValue(IconFilledProperty, value);
    }

    public double IconFontSize
    {
        get => (double)GetValue(IconFontSizeProperty);
        set => SetValue(IconFontSizeProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static void OnStylePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Button btn)
        {
            btn.UpdateStyle();
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateStyle();
    }

    private void UpdateStyle()
    {
        var styleKey = Variant switch
        {
            ButtonVariant.Filled => "ButtonFilled",
            ButtonVariant.Outlined => "ButtonOutlined",
            ButtonVariant.Ghost => "ButtonGhost",
            ButtonVariant.GhostDimmed => "ButtonGhostDimmed",
            _ => "ButtonFilled"
        };

        if (Application.Current.TryFindResource(styleKey) is Style style)
        {
            Style = style;
        }
    }
}

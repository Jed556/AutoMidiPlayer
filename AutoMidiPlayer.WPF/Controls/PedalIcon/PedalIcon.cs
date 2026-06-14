using System.Windows;
using System.Windows.Controls;

namespace AutoMidiPlayer.WPF.Controls;

public partial class PedalIcon : UserControl
{
    public static readonly DependencyProperty IsSoftActiveProperty =
        DependencyProperty.Register(
            nameof(IsSoftActive),
            typeof(bool),
            typeof(PedalIcon),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsSostenutoActiveProperty =
        DependencyProperty.Register(
            nameof(IsSostenutoActive),
            typeof(bool),
            typeof(PedalIcon),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsSustainActiveProperty =
        DependencyProperty.Register(
            nameof(IsSustainActive),
            typeof(bool),
            typeof(PedalIcon),
            new PropertyMetadata(false));

    public static readonly DependencyProperty OutlineOpacityProperty =
        DependencyProperty.Register(
            nameof(OutlineOpacity),
            typeof(double),
            typeof(PedalIcon),
            new PropertyMetadata(0.5));

    public static readonly DependencyProperty AccentStrokeThicknessProperty =
        DependencyProperty.Register(
            nameof(AccentStrokeThickness),
            typeof(double),
            typeof(PedalIcon),
            new PropertyMetadata(8.0));

    public bool IsSoftActive
    {
        get => (bool)GetValue(IsSoftActiveProperty);
        set => SetValue(IsSoftActiveProperty, value);
    }

    public bool IsSostenutoActive
    {
        get => (bool)GetValue(IsSostenutoActiveProperty);
        set => SetValue(IsSostenutoActiveProperty, value);
    }

    public bool IsSustainActive
    {
        get => (bool)GetValue(IsSustainActiveProperty);
        set => SetValue(IsSustainActiveProperty, value);
    }

    public double OutlineOpacity
    {
        get => (double)GetValue(OutlineOpacityProperty);
        set => SetValue(OutlineOpacityProperty, value);
    }

    public double AccentStrokeThickness
    {
        get => (double)GetValue(AccentStrokeThicknessProperty);
        set => SetValue(AccentStrokeThicknessProperty, value);
    }

    public PedalIcon()
    {
        InitializeComponent();
    }
}

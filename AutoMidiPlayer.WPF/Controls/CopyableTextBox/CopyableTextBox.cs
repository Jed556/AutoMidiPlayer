using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace AutoMidiPlayer.WPF.Controls;

public partial class CopyableTextBox : UserControl
{
    private const double HoverOpacity = 0.64;
    private const double ActiveOpacity = 0.8;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(CopyableTextBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(CopyableTextBox),
        new PropertyMetadata(true));

    public static readonly DependencyProperty TextWrappingProperty = DependencyProperty.Register(
        nameof(TextWrapping),
        typeof(TextWrapping),
        typeof(CopyableTextBox),
        new PropertyMetadata(System.Windows.TextWrapping.NoWrap));

    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(VerticalScrollBarVisibility),
        typeof(ScrollBarVisibility),
        typeof(CopyableTextBox),
        new PropertyMetadata(ScrollBarVisibility.Auto));

    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty = DependencyProperty.Register(
        nameof(HorizontalScrollBarVisibility),
        typeof(ScrollBarVisibility),
        typeof(CopyableTextBox),
        new PropertyMetadata(ScrollBarVisibility.Auto));

    public static readonly DependencyProperty TextBoxMaxHeightProperty = DependencyProperty.Register(
        nameof(TextBoxMaxHeight),
        typeof(double),
        typeof(CopyableTextBox),
        new PropertyMetadata(double.PositiveInfinity));

    public static readonly DependencyProperty CopyToolTipProperty = DependencyProperty.Register(
        nameof(CopyToolTip),
        typeof(object),
        typeof(CopyableTextBox),
        new PropertyMetadata("Copy to clipboard"));

    public CopyableTextBox()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty);
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    public double TextBoxMaxHeight
    {
        get => (double)GetValue(TextBoxMaxHeightProperty);
        set => SetValue(TextBoxMaxHeightProperty, value);
    }

    public object? CopyToolTip
    {
        get => GetValue(CopyToolTipProperty);
        set => SetValue(CopyToolTipProperty, value);
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(Text ?? string.Empty);
        e.Handled = true;
    }

    private void OnRootGridMouseEnter(object sender, MouseEventArgs e)
    {
        AnimateCopyButtonOpacity(HoverOpacity);
    }

    private void OnRootGridMouseLeave(object sender, MouseEventArgs e)
    {
        AnimateCopyButtonOpacity(0);
    }

    private void OnCopyButtonMouseEnter(object sender, MouseEventArgs e)
    {
        AnimateCopyButtonOpacity(ActiveOpacity);
    }

    private void OnCopyButtonMouseLeave(object sender, MouseEventArgs e)
    {
        AnimateCopyButtonOpacity(RootGrid.IsMouseOver ? HoverOpacity : 0);
    }

    private void AnimateCopyButtonOpacity(double targetOpacity)
    {
        var animation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        CopyButton.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}

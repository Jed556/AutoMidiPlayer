using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AutoMidiPlayer.WPF.Controls;

public partial class Slider : UserControl
{
    private bool _suppressValuePropagation;
    private bool _suppressAnimation;
    private Thumb? _thumb;

    public Slider()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        SliderHost.PreviewMouseLeftButtonDown += OnSliderPreviewMouseLeftButtonDown;
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Slider), new PropertyMetadata(0d));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Slider), new PropertyMetadata(2d));

    public static readonly DependencyProperty TickFrequencyProperty = DependencyProperty.Register(
        nameof(TickFrequency), typeof(double), typeof(Slider), new PropertyMetadata(1d));

    public static readonly DependencyProperty IsSnapToTickEnabledProperty = DependencyProperty.Register(
        nameof(IsSnapToTickEnabled), typeof(bool), typeof(Slider), new PropertyMetadata(true));

    public static readonly DependencyProperty TickPlacementProperty = DependencyProperty.Register(
        nameof(TickPlacement), typeof(TickPlacement), typeof(Slider), new PropertyMetadata(TickPlacement.BottomRight));

    public static readonly DependencyProperty AnimateThumbTransitionsProperty = DependencyProperty.Register(
        nameof(AnimateThumbTransitions), typeof(bool), typeof(Slider), new PropertyMetadata(true));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(Slider),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.Register(
        nameof(AnimatedValue), typeof(double), typeof(Slider),
        new PropertyMetadata(0d, OnAnimatedValueChanged));

    public static readonly DependencyProperty ThumbToolTipOptionsProperty = DependencyProperty.Register(
        nameof(ThumbToolTipOptions), typeof(string), typeof(Slider), new PropertyMetadata(string.Empty, OnThumbToolTipOptionsChanged));

    public static readonly DependencyProperty ThumbToolTipFallbackProperty = DependencyProperty.Register(
        nameof(ThumbToolTipFallback), typeof(string), typeof(Slider), new PropertyMetadata("Value: {0}"));

    public static readonly DependencyProperty AnimationDurationMsProperty = DependencyProperty.Register(
        nameof(AnimationDurationMs), typeof(double), typeof(Slider), new PropertyMetadata(180d));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double TickFrequency
    {
        get => (double)GetValue(TickFrequencyProperty);
        set => SetValue(TickFrequencyProperty, value);
    }

    public bool IsSnapToTickEnabled
    {
        get => (bool)GetValue(IsSnapToTickEnabledProperty);
        set => SetValue(IsSnapToTickEnabledProperty, value);
    }

    public TickPlacement TickPlacement
    {
        get => (TickPlacement)GetValue(TickPlacementProperty);
        set => SetValue(TickPlacementProperty, value);
    }

    public bool AnimateThumbTransitions
    {
        get => (bool)GetValue(AnimateThumbTransitionsProperty);
        set => SetValue(AnimateThumbTransitionsProperty, value);
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double AnimatedValue
    {
        get => (double)GetValue(AnimatedValueProperty);
        set => SetValue(AnimatedValueProperty, value);
    }

    public string ThumbToolTipOptions
    {
        get => (string)GetValue(ThumbToolTipOptionsProperty);
        set => SetValue(ThumbToolTipOptionsProperty, value);
    }

    public string ThumbToolTipFallback
    {
        get => (string)GetValue(ThumbToolTipFallbackProperty);
        set => SetValue(ThumbToolTipFallbackProperty, value);
    }

    public double AnimationDurationMs
    {
        get => (double)GetValue(AnimationDurationMsProperty);
        set => SetValue(AnimationDurationMsProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressAnimation = true;
        AnimatedValue = Value;
        _suppressAnimation = false;

        ResolveThumb();
        UpdateThumbToolTip();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Slider)d;
        if (control._suppressValuePropagation)
            return;

        var targetValue = (int)e.NewValue;
        control.AnimateTo(targetValue);
    }

    private static void OnAnimatedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Slider)d;
        control.UpdateThumbToolTip();

        if (control._suppressValuePropagation)
            return;

        var rounded = (int)Math.Round((double)e.NewValue);
        if (control.Value != rounded)
            control.Value = rounded;
    }

    private static void OnThumbToolTipOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Slider)d;
        control.UpdateThumbToolTip();
    }

    private void AnimateTo(int targetValue)
    {
        var clamped = Math.Clamp(targetValue, (int)Math.Round(Minimum), (int)Math.Round(Maximum));

        if (!AnimateThumbTransitions || _suppressAnimation || !IsLoaded)
        {
            _suppressValuePropagation = true;
            AnimatedValue = clamped;
            _suppressValuePropagation = false;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = clamped,
            Duration = TimeSpan.FromMilliseconds(Math.Max(1, AnimationDurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            _suppressValuePropagation = true;
            AnimatedValue = clamped;
            _suppressValuePropagation = false;
            BeginAnimation(AnimatedValueProperty, null);
            UpdateThumbToolTip();
        };

        BeginAnimation(AnimatedValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnSliderPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Thumb>(source) is not null)
            return;

        var track = SliderHost.Template?.FindName("PART_Track", SliderHost) as Track;
        if (track is null)
            return;

        var position = e.GetPosition(track);
        var ratio = track.ActualWidth <= 0
            ? 0
            : Math.Clamp(position.X / track.ActualWidth, 0, 1);

        var raw = Minimum + (Maximum - Minimum) * ratio;
        var target = Snap(raw);

        _suppressValuePropagation = true;
        Value = target;
        _suppressValuePropagation = false;

        AnimateTo(target);

        e.Handled = true;
    }

    private int Snap(double raw)
    {
        var min = Minimum;
        var max = Maximum;

        if (!IsSnapToTickEnabled || TickFrequency <= 0)
            return (int)Math.Round(Math.Clamp(raw, min, max));

        var steps = Math.Round((raw - min) / TickFrequency);
        var snapped = min + (steps * TickFrequency);
        return (int)Math.Round(Math.Clamp(snapped, min, max));
    }

    private void ResolveThumb()
    {
        if (_thumb is not null)
            return;

        _thumb = FindDescendant<Thumb>(SliderHost);
    }

    private void UpdateThumbToolTip()
    {
        ResolveThumb();
        if (_thumb is null)
            return;

        var text = ResolveThumbToolTipText();
        ToolTipService.SetToolTip(_thumb, text);
    }

    private string ResolveThumbToolTipText()
    {
        var options = (ThumbToolTipOptions ?? string.Empty)
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (options.Length > 0)
        {
            var index = (int)Math.Round(AnimatedValue - Minimum);
            if (index >= 0 && index < options.Length)
                return options[index];
        }

        return string.Format(ThumbToolTipFallback, (int)Math.Round(AnimatedValue));
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T typed)
                return typed;

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;

            var nested = FindDescendant<T>(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}

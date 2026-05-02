using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Data;

namespace AutoMidiPlayer.WPF.Controls;

public partial class Slider : UserControl
{
    private bool _suppressValuePropagation;
    private bool _suppressAnimation;
    private bool _isTrackPressed;
    private bool _isTrackDragging;
    private Thumb? _thumb;
    private Track? _track;
    private Point _trackPressPoint;
    private Binding? _twoWayValueBinding;
    private int _lastAnimationTarget = int.MinValue;

    public Slider()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        SliderHost.PreviewMouseLeftButtonDown += OnSliderPreviewMouseLeftButtonDown;
        SliderHost.PreviewMouseMove += OnSliderPreviewMouseMove;
        SliderHost.PreviewMouseLeftButtonUp += OnSliderPreviewMouseLeftButtonUp;
        SliderHost.LostMouseCapture += OnSliderLostMouseCapture;
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
        nameof(AnimationDurationMs), typeof(double), typeof(Slider), new PropertyMetadata(140d));

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
        {
            control._suppressValuePropagation = true;
            control.Value = rounded;
            control._suppressValuePropagation = false;
        }
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
            _lastAnimationTarget = clamped;
            return;
        }

        // If already animating to the same target, don't interrupt the animation
        if (_lastAnimationTarget == clamped)
            return;

        _lastAnimationTarget = clamped;

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
        // If clicking on the thumb itself, let default behavior handle it
        if (e.OriginalSource is DependencyObject source && FindAncestor<Thumb>(source) is not null)
            return;

        ResolveTrack();
        if (_track is null)
            return;

        _isTrackPressed = true;
        _isTrackDragging = false;
        _trackPressPoint = e.GetPosition(_track);
        _lastAnimationTarget = int.MinValue; // Reset to allow new animation

        SliderHost.CaptureMouse();
        SliderHost.Focus();

        UpdateValueFromMousePosition(_trackPressPoint, animate: true);
        e.Handled = true;
    }

    private void OnSliderPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isTrackPressed || Mouse.LeftButton != MouseButtonState.Pressed)
            return;

        ResolveTrack();
        if (_track is null)
            return;

        var currentPoint = e.GetPosition(_track);

        if (!_isTrackDragging)
        {
            if (!HasExceededDragThreshold(currentPoint, _trackPressPoint))
                return;

            _isTrackDragging = true;
        }

        UpdateValueFromMousePosition(currentPoint, animate: true);
        e.Handled = true;
    }

    private void OnSliderPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isTrackPressed)
            return;

        ResetTrackInteraction();
        e.Handled = true;
    }

    private void OnSliderLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isTrackPressed)
            return;

        ResetTrackInteraction();
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

    private void ResolveTrack()
    {
        if (_track is not null)
            return;

        _track = SliderHost.Template?.FindName("PART_Track", SliderHost) as Track;
    }

    private void UpdateValueFromMousePosition(Point position, bool animate)
    {
        ResolveTrack();
        if (_track is null)
            return;

        var isVertical = SliderHost.Orientation == Orientation.Vertical;

        double ratio;
        if (isVertical)
        {
            var trackHeight = _track.ActualHeight;
            ratio = trackHeight <= 0
                ? 0
                : Math.Clamp(1.0 - (position.Y / trackHeight), 0, 1);
        }
        else
        {
            var trackWidth = _track.ActualWidth;
            ratio = trackWidth <= 0
                ? 0
                : Math.Clamp(position.X / trackWidth, 0, 1);
        }

        var raw = Minimum + (Maximum - Minimum) * ratio;
        var target = Snap(raw);

        CommitValue(target, animate);
    }

    private void CommitValue(int target, bool animate)
    {
        if (animate)
        {
            AnimateTo(target);
            return;
        }

        _suppressValuePropagation = true;
        Value = target;
        _suppressValuePropagation = false;

        _suppressAnimation = true;
        AnimatedValue = target;
        _suppressAnimation = false;

        UpdateThumbToolTip();
    }

    private void ResetTrackInteraction()
    {
        _isTrackPressed = false;
        _isTrackDragging = false;

        if (SliderHost.IsMouseCaptured)
            SliderHost.ReleaseMouseCapture();
    }

    private static bool HasExceededDragThreshold(Point currentPoint, Point startPoint)
    {
        return Math.Abs(currentPoint.X - startPoint.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(currentPoint.Y - startPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void ResolveThumb()
    {
        if (_thumb is not null)
            return;

        _thumb = FindDescendant<Thumb>(SliderHost);
        if (_thumb is not null)
        {
            _thumb.DragStarted += OnThumbDragStarted;
            _thumb.DragDelta += OnThumbDragDelta;
            _thumb.DragCompleted += OnThumbDragCompleted;
        }
    }

    private void OnThumbDragStarted(object? sender, DragStartedEventArgs e)
    {
        _lastAnimationTarget = int.MinValue; // Reset to allow new animation

        if (_twoWayValueBinding is null)
        {
            _twoWayValueBinding = new Binding(nameof(AnimatedValue)) { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
        }

        var oneWay = new Binding(nameof(AnimatedValue)) { Source = this, Mode = BindingMode.OneWay };
        BindingOperations.ClearBinding(SliderHost, RangeBase.ValueProperty);
        BindingOperations.SetBinding(SliderHost, RangeBase.ValueProperty, oneWay);
    }

    private void OnThumbDragDelta(object? sender, DragDeltaEventArgs e)
    {
        ResolveTrack();
        if (_track is null)
            return;

        var currentPoint = Mouse.GetPosition(_track);
        UpdateValueFromMousePosition(currentPoint, animate: true);
        e.Handled = true;
    }

    private void OnThumbDragCompleted(object? sender, DragCompletedEventArgs e)
    {
        if (_twoWayValueBinding is null)
            _twoWayValueBinding = new Binding(nameof(AnimatedValue)) { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };

        BindingOperations.ClearBinding(SliderHost, RangeBase.ValueProperty);
        BindingOperations.SetBinding(SliderHost, RangeBase.ValueProperty, _twoWayValueBinding);
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

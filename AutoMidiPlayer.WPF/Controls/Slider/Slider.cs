using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Data;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

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

    // Popup-based drag tooltip (mirrors Seekbar behavior)
    private Popup? _dragPopup;
    private TextBlock? _dragPopupText;
    private Window? _parentWindow;

    // Fixed pixel offset for tooltip Y position (negative = above track).
    private const double TooltipFixedYOffset = -24.0;
    private int _lastAnimationTarget = int.MinValue;

    public Slider()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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
        nameof(ThumbToolTipFallback), typeof(string), typeof(Slider), new PropertyMetadata("{0}"));

    public static readonly DependencyProperty AnimationDurationMsProperty = DependencyProperty.Register(
        nameof(AnimationDurationMs), typeof(double), typeof(Slider), new PropertyMetadata(140d));

    public static readonly DependencyProperty CustomTicksProperty = DependencyProperty.Register(
        nameof(CustomTicks), typeof(int[]), typeof(Slider), new PropertyMetadata(null, OnCustomTicksChanged));

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

    /// <summary>
    /// When set, provides non-uniform tick values. The slider will range from min to max
    /// of these values and snap to the nearest one. Ticks are rendered at proportional positions.
    /// </summary>
    public int[]? CustomTicks
    {
        get => (int[]?)GetValue(CustomTicksProperty);
        set => SetValue(CustomTicksProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressAnimation = true;
        AnimatedValue = Value;
        _suppressAnimation = false;

        ApplyCustomTicksToHost();
        ResolveThumb();
        EnsureDragPopup();
        UpdateThumbToolTip();

        _parentWindow = Window.GetWindow(this);
        if (_parentWindow is not null)
        {
            _parentWindow.LocationChanged += ParentWindow_LocationChanged;
            _parentWindow.StateChanged += ParentWindow_StateChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_dragPopup is not null)
            _dragPopup.IsOpen = false;

        if (_parentWindow is not null)
        {
            _parentWindow.LocationChanged -= ParentWindow_LocationChanged;
            _parentWindow.StateChanged -= ParentWindow_StateChanged;
            _parentWindow = null;
        }
    }

    private void ParentWindow_LocationChanged(object? sender, EventArgs e)
    {
        HideDragPopup();
    }

    private void ParentWindow_StateChanged(object? sender, EventArgs e)
    {
        HideDragPopup();
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Slider)d;
        if (control._suppressValuePropagation)
            return;

        // External value change (e.g. ViewModel binding) — animate the visual to match
        var targetValue = (int)e.NewValue;
        control.AnimateTo(targetValue);
    }

    private static void OnAnimatedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // AnimatedValue is purely visual — only update the tooltip, never touch Value
        var control = (Slider)d;
        control.UpdateThumbToolTip();
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
            AnimatedValue = clamped;
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
            AnimatedValue = clamped;
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

        // Show drag popup at clicked position
        ShowDragPopupAt(_trackPressPoint.X);

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

        // Update drag popup position during track drag
        ShowDragPopupAt(currentPoint.X);

        e.Handled = true;
    }

    private void OnSliderPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isTrackPressed)
            return;

        ResetTrackInteraction();
        HideDragPopup();
        e.Handled = true;
    }

    private void OnSliderLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_isTrackPressed)
            return;

        ResetTrackInteraction();
        HideDragPopup();
    }

    private int Snap(double raw)
    {
        var ticks = CustomTicks;
        if (ticks is { Length: > 0 })
        {
            // Snap to the nearest value in the custom ticks array
            var closest = ticks[0];
            var closestDist = Math.Abs(raw - closest);
            for (var i = 1; i < ticks.Length; i++)
            {
                var dist = Math.Abs(raw - ticks[i]);
                if (dist < closestDist)
                {
                    closest = ticks[i];
                    closestDist = dist;
                }
            }
            return closest;
        }

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
        // Always set Value immediately — this is the "data" side
        _suppressValuePropagation = true;
        Value = target;
        _suppressValuePropagation = false;

        // Then handle the visual side
        if (animate)
        {
            AnimateTo(target);
        }
        else
        {
            _suppressAnimation = true;
            AnimatedValue = target;
            _suppressAnimation = false;
            _lastAnimationTarget = target;
            UpdateThumbToolTip();
        }
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

        // Show drag popup at thumb position
        ResolveTrack();
        if (_track is not null && _thumb is not null)
        {
            var thumbCenter = _thumb.TranslatePoint(new Point(_thumb.ActualWidth / 2.0, _thumb.ActualHeight / 2.0), _track);
            UpdateThumbToolTip();
            ShowDragPopupAt(thumbCenter.X);
        }
    }

    private void OnThumbDragDelta(object? sender, DragDeltaEventArgs e)
    {
        ResolveTrack();
        if (_track is null)
            return;

        var currentPoint = Mouse.GetPosition(_track);
        UpdateValueFromMousePosition(currentPoint, animate: true);

        // Update drag popup position to follow thumb
        if (_thumb is not null)
        {
            var thumbCenter = _thumb.TranslatePoint(new Point(_thumb.ActualWidth / 2.0, _thumb.ActualHeight / 2.0), _track);
            ShowDragPopupAt(thumbCenter.X);
        }

        e.Handled = true;
    }

    private void OnThumbDragCompleted(object? sender, DragCompletedEventArgs e)
    {
        if (_twoWayValueBinding is null)
            _twoWayValueBinding = new Binding(nameof(AnimatedValue)) { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };

        BindingOperations.ClearBinding(SliderHost, RangeBase.ValueProperty);
        BindingOperations.SetBinding(SliderHost, RangeBase.ValueProperty, _twoWayValueBinding);

        HideDragPopup();
    }

    private void UpdateThumbToolTip()
    {
        ResolveThumb();
        if (_thumb is null)
            return;

        var text = ResolveThumbToolTipText();
        ToolTipService.SetToolTip(_thumb, text);

        if (_dragPopupText is not null)
            _dragPopupText.Text = text;
    }

    #region Drag Popup

    private void EnsureDragPopup()
    {
        if (_dragPopup is not null)
            return;

        _dragPopupText = new TextBlock { Padding = new Thickness(8, 4, 8, 4) };
        var dragBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Child = _dragPopupText,
            Padding = new Thickness(0),
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 1.0),
            RenderTransform = new ScaleTransform(0.95, 0.95)
        };

        ApplyPopupTheme(dragBorder, _dragPopupText);

        _dragPopup = new Popup
        {
            Child = dragBorder,
            Placement = PlacementMode.Relative,
            StaysOpen = true,
            AllowsTransparency = true,
            IsHitTestVisible = false
        };
    }

    private void ApplyPopupTheme(Border border, TextBlock textBlock)
    {
        var theme = ApplicationThemeManager.GetAppTheme();
        var bg = theme == ApplicationTheme.Dark
            ? new SolidColorBrush(Color.FromArgb(232, 48, 48, 52))
            : new SolidColorBrush(Color.FromArgb(236, 250, 250, 252));
        var stroke = TryFindResource("ControlStrokeColorDefaultBrush") as Brush ?? Brushes.Transparent;
        var fg = theme == ApplicationTheme.Dark
            ? new SolidColorBrush(Color.FromRgb(245, 245, 245))
            : new SolidColorBrush(Color.FromRgb(26, 26, 26));

        border.SetCurrentValue(Border.BackgroundProperty, bg);
        border.SetCurrentValue(Border.BorderBrushProperty, stroke);
        border.SetCurrentValue(Border.BorderThicknessProperty, new Thickness(1));
        textBlock.SetCurrentValue(TextBlock.ForegroundProperty, fg);
    }

    private (double left, double top) ComputePopupOffsets(double x, FrameworkElement? child)
    {
        if (_track is null || RootGrid is null)
            return (0, 0);

        var trackOrigin = _track.TranslatePoint(new Point(0, 0), RootGrid);

        var halfW = 30.0;
        double measuredW = 0.0;
        if (child is FrameworkElement fe)
        {
            fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            measuredW = fe.ActualWidth > 0 ? fe.ActualWidth : fe.DesiredSize.Width;
            if (measuredW > 0)
                halfW = measuredW / 2.0;
        }

        var left = trackOrigin.X + x - halfW;

        var containerWidth = RootGrid.ActualWidth;
        if (measuredW <= 0)
            measuredW = halfW * 2.0;

        var minLeft = trackOrigin.X;
        var maxLeft = Math.Max(minLeft, containerWidth - measuredW);
        left = Math.Clamp(left, minLeft, maxLeft);

        var top = trackOrigin.Y + TooltipFixedYOffset;
        return (left, top);
    }

    private void ShowDragPopupAt(double x)
    {
        if (_dragPopup is null || _track is null || RootGrid is null)
            return;

        if (_dragPopup.Child is Border border && _dragPopupText is not null)
            ApplyPopupTheme(border, _dragPopupText);

        var (left, top) = ComputePopupOffsets(x, _dragPopup.Child as FrameworkElement);

        _dragPopup.PlacementTarget = RootGrid;
        _dragPopup.HorizontalOffset = left;
        _dragPopup.VerticalOffset = top;

        if (!_dragPopup.IsOpen)
        {
            _dragPopup.IsOpen = true;
            if (_dragPopup.Child is FrameworkElement childFe)
            {
                var sb = new Storyboard();
                var scaleX = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, From = 0.85 };
                var scaleY = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100))
                    { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, From = 0.85 };
                var opacity = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100)) { From = 0.0 };

                Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.ScaleX"));
                Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.ScaleY"));
                Storyboard.SetTargetProperty(opacity, new PropertyPath("Opacity"));

                sb.Children.Add(scaleX);
                sb.Children.Add(scaleY);
                sb.Children.Add(opacity);

                childFe.SetCurrentValue(UIElement.OpacityProperty, 0.0);
                var anim = new AutoMidiPlayer.WPF.Animation.Animation(childFe, sb);
                anim.Begin();
            }
        }

        // Correct placement after first render so measured sizes are accurate.
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (_dragPopup.Child is FrameworkElement fe && _track is not null && RootGrid is not null)
                {
                    fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    fe.Arrange(new Rect(fe.DesiredSize));

                    var (correctedLeft, correctedTop) = ComputePopupOffsets(x, fe);
                    _dragPopup.HorizontalOffset = correctedLeft;
                    _dragPopup.VerticalOffset = correctedTop;
                }
            }
            catch
            {
                // Best-effort correction.
            }
        }, DispatcherPriority.Render);
    }

    private void HideDragPopup()
    {
        if (_dragPopup is null)
            return;

        if (_dragPopup.Child is FrameworkElement fe)
        {
            var sb = new Storyboard();
            var scaleX = new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(90))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var scaleY = new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(90))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var opacity = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(90));

            Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.ScaleX"));
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.ScaleY"));
            Storyboard.SetTargetProperty(opacity, new PropertyPath("Opacity"));

            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
            sb.Children.Add(opacity);

            var anim = new AutoMidiPlayer.WPF.Animation.Animation(fe, sb);
            anim.Completed += (_, _) => _dragPopup.IsOpen = false;
            anim.Begin();
        }
        else
        {
            _dragPopup.IsOpen = false;
        }
    }

    #endregion

    private string ResolveThumbToolTipText()
    {
        var options = (ThumbToolTipOptions ?? string.Empty)
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (options.Length > 0)
        {
            var ticks = CustomTicks;
            int index;
            if (ticks is { Length: > 0 })
            {
                // Map AnimatedValue to the index in the custom ticks array
                var snappedValue = (int)Math.Round(AnimatedValue);
                index = Array.IndexOf(ticks, snappedValue);
                if (index < 0)
                {
                    // Find nearest tick index
                    index = 0;
                    var closestDist = Math.Abs(snappedValue - ticks[0]);
                    for (var i = 1; i < ticks.Length; i++)
                    {
                        var dist = Math.Abs(snappedValue - ticks[i]);
                        if (dist < closestDist)
                        {
                            index = i;
                            closestDist = dist;
                        }
                    }
                }
            }
            else
            {
                index = (int)Math.Round(AnimatedValue - Minimum);
            }

            if (index >= 0 && index < options.Length)
                return options[index];
        }

        return string.Format(ThumbToolTipFallback, (int)Math.Round(AnimatedValue));
    }

    #region Custom Ticks

    private static void OnCustomTicksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Slider)d;
        control.ApplyCustomTicksToHost();
    }

    /// <summary>
    /// Applies the CustomTicks array to the inner SliderHost, setting Min, Max, and Ticks.
    /// </summary>
    private void ApplyCustomTicksToHost()
    {
        var ticks = CustomTicks;
        if (ticks is not { Length: > 0 })
            return;

        var sorted = ticks.OrderBy(v => v).ToArray();
        var min = (double)sorted[0];
        var max = (double)sorted[^1];

        // Update our own Minimum/Maximum so internal calculations use the real range
        Minimum = min;
        Maximum = max;

        // Set the WPF Slider's Ticks property for non-uniform tick rendering
        var tickCollection = new DoubleCollection(sorted.Select(v => (double)v));
        SliderHost.Ticks = tickCollection;

        // Use TickFrequency of 0 so only explicit Ticks are shown (no auto-generated ticks)
        SliderHost.TickFrequency = 0;
    }

    #endregion

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

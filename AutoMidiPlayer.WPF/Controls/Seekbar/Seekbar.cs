using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AutoMidiPlayer.WPF.Animation;
using Wpf.Ui.Appearance;

namespace AutoMidiPlayer.WPF.Controls;

public partial class Seekbar : UserControl
{
    private enum SeekInteractionMode
    {
        None,
        Track,
        Thumb
    }

    private bool _suppressValuePropagation;
    private bool _suppressAnimation;
    private SeekInteractionMode _interactionMode = SeekInteractionMode.None;
    private Thumb? _thumb;
    private Track? _track;
    private Point _trackPressPoint;

    // Use Popup-based tooltips to avoid attaching ToolTip elements (StaysOpen issues).
    private Popup? _hoverPopup;
    private TextBlock? _hoverPopupText;
    private Popup? _dragPopup;
    private TextBlock? _dragPopupText;
    private bool _isHoverFillVisible;
    private readonly DispatcherTimer _hoverHideTimer;
    private Window? _parentWindow;

    private const int HoverHideDelayMs = 80;
    // Fixed pixel offset for tooltip Y position (negative = above track).
    private const double TooltipFixedYOffset = -24.0;

    public Seekbar()
    {
        InitializeComponent();

        _hoverHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(HoverHideDelayMs)
        };
        _hoverHideTimer.Tick += OnHoverHideTimerTick;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SliderHost.PreviewMouseLeftButtonDown += OnSliderPreviewMouseLeftButtonDown;
        SliderHost.PreviewMouseMove += OnSliderPreviewMouseMove;
        SliderHost.PreviewMouseLeftButtonUp += OnSliderPreviewMouseLeftButtonUp;
        SliderHost.LostMouseCapture += OnSliderLostMouseCapture;
        SliderHost.MouseMove += OnSliderMouseMove;
        SliderHost.MouseLeave += OnSliderMouseLeave;
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Seekbar), new PropertyMetadata(0d));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Seekbar), new PropertyMetadata(2d));

    public static readonly DependencyProperty TickFrequencyProperty = DependencyProperty.Register(
        nameof(TickFrequency), typeof(double), typeof(Seekbar), new PropertyMetadata(1d));

    public static readonly DependencyProperty IsSnapToTickEnabledProperty = DependencyProperty.Register(
        nameof(IsSnapToTickEnabled), typeof(bool), typeof(Seekbar), new PropertyMetadata(true));

    public static readonly DependencyProperty TickPlacementProperty = DependencyProperty.Register(
        nameof(TickPlacement), typeof(TickPlacement), typeof(Seekbar), new PropertyMetadata(TickPlacement.BottomRight));

    public static readonly DependencyProperty AnimateThumbTransitionsProperty = DependencyProperty.Register(
        nameof(AnimateThumbTransitions), typeof(bool), typeof(Seekbar), new PropertyMetadata(true));

    public static readonly DependencyProperty SliderStyleProperty = DependencyProperty.Register(
        nameof(SliderStyle), typeof(Style), typeof(Seekbar), new PropertyMetadata(null));

    public static readonly DependencyProperty PreviewValueProperty = DependencyProperty.Register(
        nameof(PreviewValue), typeof(double), typeof(Seekbar), new PropertyMetadata(0d));

    public static readonly DependencyProperty IsPreviewActiveProperty = DependencyProperty.Register(
        nameof(IsPreviewActive), typeof(bool), typeof(Seekbar), new PropertyMetadata(false));

    public static readonly DependencyProperty IsSelectionRangeEnabledProperty = DependencyProperty.Register(
        nameof(IsSelectionRangeEnabled), typeof(bool), typeof(Seekbar), new PropertyMetadata(false));

    public static readonly DependencyProperty SelectionStartProperty = DependencyProperty.Register(
        nameof(SelectionStart), typeof(double), typeof(Seekbar), new PropertyMetadata(0d));

    public static readonly DependencyProperty SelectionEndProperty = DependencyProperty.Register(
        nameof(SelectionEnd), typeof(double), typeof(Seekbar), new PropertyMetadata(0d));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(Seekbar),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty AnimatedValueProperty = DependencyProperty.Register(
        nameof(AnimatedValue), typeof(double), typeof(Seekbar),
        new PropertyMetadata(0d, OnAnimatedValueChanged));

    public static readonly DependencyProperty ThumbToolTipOptionsProperty = DependencyProperty.Register(
        nameof(ThumbToolTipOptions), typeof(string), typeof(Seekbar), new PropertyMetadata(string.Empty, OnThumbToolTipOptionsChanged));

    public static readonly DependencyProperty ThumbToolTipFallbackProperty = DependencyProperty.Register(
        nameof(ThumbToolTipFallback), typeof(string), typeof(Seekbar), new PropertyMetadata("Value: {0}"));

    public static readonly DependencyProperty AnimationDurationMsProperty = DependencyProperty.Register(
        nameof(AnimationDurationMs), typeof(double), typeof(Seekbar), new PropertyMetadata(140d));

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

    public Style? SliderStyle
    {
        get => (Style?)GetValue(SliderStyleProperty);
        set => SetValue(SliderStyleProperty, value);
    }

    public double PreviewValue
    {
        get => (double)GetValue(PreviewValueProperty);
        set => SetValue(PreviewValueProperty, value);
    }

    public bool IsPreviewActive
    {
        get => (bool)GetValue(IsPreviewActiveProperty);
        set => SetValue(IsPreviewActiveProperty, value);
    }

    public bool IsSelectionRangeEnabled
    {
        get => (bool)GetValue(IsSelectionRangeEnabledProperty);
        set => SetValue(IsSelectionRangeEnabledProperty, value);
    }

    public double SelectionStart
    {
        get => (double)GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public double SelectionEnd
    {
        get => (double)GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
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
        ResolveThumb();
        EnsurePopups();
        UpdateThumbToolTip();

        // Ensure tooltip/window-level mouse tracking works even if popups appear.
        _parentWindow = Window.GetWindow(this);
        if (_parentWindow is not null)
        {
            _parentWindow.MouseMove += ParentWindow_MouseMove;
            _parentWindow.LocationChanged += ParentWindow_LocationChanged;
            _parentWindow.StateChanged += ParentWindow_StateChanged;
        }

        // Sync visual thumb (AnimatedValue) to current Value after layout/render so visuals reflect restored state.
        // Use Render priority to ensure bindings and layout are complete before syncing.
        Dispatcher.BeginInvoke(() =>
        {
            _suppressAnimation = true;
            AnimatedValue = Value;
            _suppressAnimation = false;
        }, DispatcherPriority.Render);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Seekbar)d;

        // When value is programmatically changed (not during interactions), animate visual.
        if (control._suppressValuePropagation)
        {
            // noop: suppressed propagation path
        }

        control.UpdateThumbToolTip();

        if (control._interactionMode != SeekInteractionMode.None)
        {
            // If interacting, don't start an animation to avoid fighting user input.
            return;
        }

        var targetValue = (int)e.NewValue;
        control.AnimateTo(targetValue);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _hoverHideTimer.Stop();

        if (_hoverPopup is not null)
            _hoverPopup.IsOpen = false;

        if (_dragPopup is not null)
            _dragPopup.IsOpen = false;

        HoverFillOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        HoverFillOverlay.SetCurrentValue(UIElement.OpacityProperty, 0d);
        _isHoverFillVisible = false;

        if (_parentWindow is not null)
        {
            _parentWindow.MouseMove -= ParentWindow_MouseMove;
            _parentWindow.LocationChanged -= ParentWindow_LocationChanged;
            _parentWindow.StateChanged -= ParentWindow_StateChanged;
            _parentWindow = null;
        }
    }

    private static void OnAnimatedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Seekbar)d;
        control.UpdateThumbToolTip();

        if (control._interactionMode != SeekInteractionMode.None)
        {
            control._suppressValuePropagation = true;
            control.PreviewValue = (double)e.NewValue;
            control._suppressValuePropagation = false;
        }
    }

    private static void OnThumbToolTipOptionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Seekbar)d;
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

    private void AnimatePreviewToMousePosition(Point position)
    {
        var target = ResolveTargetValueFromMousePosition(position);
        AnimateTo(target);
    }

    private void OnSliderPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _interactionMode = e.OriginalSource is DependencyObject source && FindAncestor<Thumb>(source) is not null
            ? SeekInteractionMode.Thumb
            : SeekInteractionMode.Track;

        if (_interactionMode == SeekInteractionMode.Track)
        {
            ResolveTrack();
            if (_track is null)
            {
                _interactionMode = SeekInteractionMode.None;
                return;
            }

            _trackPressPoint = e.GetPosition(_track);

            IsPreviewActive = true;
            SliderHost.CaptureMouse();
            SliderHost.Focus();

            AnimatePreviewToMousePosition(_trackPressPoint);
            e.Handled = true;
        }
    }

    private void OnSliderPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_interactionMode != SeekInteractionMode.Track || Mouse.LeftButton != MouseButtonState.Pressed)
            return;

        ResolveTrack();
        if (_track is null)
            return;

        var currentPoint = e.GetPosition(_track);

        if (!HasExceededDragThreshold(currentPoint, _trackPressPoint))
            return;

        UpdatePreviewValueFromMousePosition(currentPoint);
        e.Handled = true;
    }

    private void OnSliderPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_interactionMode == SeekInteractionMode.None)
            return;

        int? releaseTarget = null;
        if (_interactionMode == SeekInteractionMode.Track && _track is not null)
            releaseTarget = ResolveTargetValueFromMousePosition(e.GetPosition(_track));

        CommitPreviewToValue(releaseTarget);
        ResetTrackInteraction();
        e.Handled = true;
    }

    private void OnSliderLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_interactionMode != SeekInteractionMode.Track)
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

    private void ResolveThumb()
    {
        if (_thumb is not null)
            return;
        _thumb = FindDescendant<Thumb>(SliderHost);

        if (_thumb is not null)
        {
            _thumb.DragStarted += Thumb_DragStarted;
            _thumb.DragDelta += Thumb_DragDelta;
            _thumb.DragCompleted += Thumb_DragCompleted;
        }
    }

    private void EnsurePopups()
    {
        if (_hoverPopup is null)
        {
            _hoverPopupText = new TextBlock { Padding = new Thickness(8, 4, 8, 4) };
            var hoverBorder = new Border { CornerRadius = new CornerRadius(8), Child = _hoverPopupText, Padding = new Thickness(0) };

            var bg = GetPopupBackgroundBrush();
            var stroke = TryFindResource("ControlStrokeColorDefaultBrush") as Brush ?? Brushes.Transparent;
            var fg = GetPopupForegroundBrush();
            hoverBorder.SetCurrentValue(Border.BackgroundProperty, bg);
            hoverBorder.SetCurrentValue(Border.BorderBrushProperty, stroke);
            hoverBorder.SetCurrentValue(Border.BorderThicknessProperty, new Thickness(1));
            _hoverPopupText.SetCurrentValue(TextBlock.ForegroundProperty, fg);
            hoverBorder.IsHitTestVisible = false;

            hoverBorder.RenderTransformOrigin = new Point(0.5, 1.0);
            hoverBorder.RenderTransform = new ScaleTransform(0.95, 0.95);

            _hoverPopup = new Popup
            {
                Child = hoverBorder,
                Placement = PlacementMode.Relative,
                StaysOpen = true,
                AllowsTransparency = true,
                IsHitTestVisible = false
            };
        }

        if (_dragPopup is null)
        {
            _dragPopupText = new TextBlock { Padding = new Thickness(8, 4, 8, 4) };
            var dragBorder = new Border { CornerRadius = new CornerRadius(8), Child = _dragPopupText, Padding = new Thickness(0) };

            var dbg = GetPopupBackgroundBrush();
            var dstroke = TryFindResource("ControlStrokeColorDefaultBrush") as Brush ?? Brushes.Transparent;
            var dfg = GetPopupForegroundBrush();
            dragBorder.SetCurrentValue(Border.BackgroundProperty, dbg);
            dragBorder.SetCurrentValue(Border.BorderBrushProperty, dstroke);
            dragBorder.SetCurrentValue(Border.BorderThicknessProperty, new Thickness(1));
            _dragPopupText.SetCurrentValue(TextBlock.ForegroundProperty, dfg);
            dragBorder.IsHitTestVisible = false;

            dragBorder.RenderTransformOrigin = new Point(0.5, 1.0);
            dragBorder.RenderTransform = new ScaleTransform(0.95, 0.95);

            _dragPopup = new Popup
            {
                Child = dragBorder,
                Placement = PlacementMode.Relative,
                StaysOpen = true,
                AllowsTransparency = true,
                IsHitTestVisible = false
            };
        }

        HoverFillOverlay.SetCurrentValue(Border.BackgroundProperty, GetHoverFillBrush());
    }

    private Brush GetPopupBackgroundBrush()
    {
        var theme = ApplicationThemeManager.GetAppTheme();
        return theme == ApplicationTheme.Dark
            ? new SolidColorBrush(Color.FromArgb(232, 48, 48, 52))
            : new SolidColorBrush(Color.FromArgb(236, 250, 250, 252));
    }

    private Brush GetPopupForegroundBrush()
    {
        var theme = ApplicationThemeManager.GetAppTheme();
        return theme == ApplicationTheme.Dark
            ? new SolidColorBrush(Color.FromRgb(245, 245, 245))
            : new SolidColorBrush(Color.FromRgb(26, 26, 26));
    }

    private Brush GetHoverFillBrush()
    {
        var theme = ApplicationThemeManager.GetAppTheme();
        return theme == ApplicationTheme.Dark
            ? new SolidColorBrush(Color.FromArgb(128, 255, 255, 255))
            : new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
    }

    private void RefreshPopupTheme()
    {
        if (_hoverPopup?.Child is Border hoverBorder)
            hoverBorder.SetCurrentValue(Border.BackgroundProperty, GetPopupBackgroundBrush());
        if (_dragPopup?.Child is Border dragBorder)
            dragBorder.SetCurrentValue(Border.BackgroundProperty, GetPopupBackgroundBrush());
        if (_hoverPopupText is not null)
            _hoverPopupText.SetCurrentValue(TextBlock.ForegroundProperty, GetPopupForegroundBrush());
        if (_dragPopupText is not null)
            _dragPopupText.SetCurrentValue(TextBlock.ForegroundProperty, GetPopupForegroundBrush());
        HoverFillOverlay.SetCurrentValue(Border.BackgroundProperty, GetHoverFillBrush());
    }

    private (double left, double top) ComputePopupOffsets(double x, FrameworkElement? child)
    {
        if (_track is null || RootGrid is null)
            return (0, 0);

        var trackOrigin = _track.TranslatePoint(new Point(0, 0), RootGrid);

        var halfW = 30.0; // default half-width for centering
        double measuredW = 0.0;
        if (child is FrameworkElement fe)
        {
            fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            measuredW = fe.ActualWidth > 0 ? fe.ActualWidth : fe.DesiredSize.Width;
            if (measuredW > 0)
                halfW = measuredW / 2.0;
        }

        var left = trackOrigin.X + x - halfW;

        // Clamp left so popup stays within RootGrid horizontal bounds
        var containerWidth = RootGrid.ActualWidth;
        if (measuredW <= 0)
            measuredW = halfW * 2.0;

        var minLeft = trackOrigin.X;
        var maxLeft = Math.Max(minLeft, containerWidth - measuredW);
        left = Math.Clamp(left, minLeft, maxLeft);

        var top = trackOrigin.Y + TooltipFixedYOffset;
        return (left, top);
    }

    private void CancelPendingHoverHide()
    {
        if (_hoverHideTimer.IsEnabled)
            _hoverHideTimer.Stop();
    }

    private void ScheduleHoverHide()
    {
        _hoverHideTimer.Stop();
        _hoverHideTimer.Start();
    }

    private void OnHoverHideTimerTick(object? sender, EventArgs e)
    {
        _hoverHideTimer.Stop();

        ResolveTrack();
        if (_track is not null)
        {
            var pos = Mouse.GetPosition(_track);
            // Require pointer to be strictly inside track bounds to remain hovered.
            if (pos.X >= 0 && pos.X <= _track.ActualWidth && pos.Y >= 0 && pos.Y <= _track.ActualHeight)
                return;
        }

        HideHoverPopup();
        HideHoverFill();
    }

    private void UpdateThumbToolTip()
    {
        ResolveThumb();
        if (_thumb is null)
            return;

        var text = ResolveThumbToolTipText();
        if (_dragPopupText is not null)
            _dragPopupText.Text = text;
        // keep ToolTipService untouched to avoid prior exception — rely on our popups instead
    }

    private void ShowHoverPopupAt(double x)
    {
        if (_hoverPopup is null || _track is null || RootGrid is null)
            return;

        CancelPendingHoverHide();
        RefreshPopupTheme();

        var (left, top) = ComputePopupOffsets(x, _hoverPopup.Child as FrameworkElement);

        _hoverPopup.PlacementTarget = RootGrid;
        _hoverPopup.HorizontalOffset = left;
        _hoverPopup.VerticalOffset = top;
        if (!_hoverPopup.IsOpen)
        {
            _hoverPopup.IsOpen = true;
            if (_hoverPopup.Child is FrameworkElement childFe)
            {
                var sb = new Storyboard();
                var scaleX = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, From = 0.8 };
                var scaleY = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, From = 0.8 };
                var opacity = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160)) { From = 0.0 };

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
        CorrectPopupPositionAfterOpen(_hoverPopup, x);
    }

    private void HideHoverPopup()
    {
        if (_hoverPopup is null)
            return;
        if (_hoverPopup.Child is FrameworkElement fe)
        {
            var sb = new Storyboard();
            var scaleX = new DoubleAnimation(0.8, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var scaleY = new DoubleAnimation(0.8, TimeSpan.FromMilliseconds(120)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var opacity = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120));

            Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.ScaleX"));
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.ScaleY"));
            Storyboard.SetTargetProperty(opacity, new PropertyPath("Opacity"));

            sb.Children.Add(scaleX);
            sb.Children.Add(scaleY);
            sb.Children.Add(opacity);

            var anim = new AutoMidiPlayer.WPF.Animation.Animation(fe, sb);
            anim.Completed += (_, _) => _hoverPopup.IsOpen = false;
            anim.Begin();
        }
        else
        {
            _hoverPopup.IsOpen = false;
        }
    }

    private void ShowHoverFillAt(double x)
    {
        if (_track is null)
            return;

        CancelPendingHoverHide();
        RefreshPopupTheme();

        var trackOrigin = _track.TranslatePoint(new Point(0, 0), RootGrid);
        var fillWidth = Math.Clamp(x, 0, _track.ActualWidth);

        HoverFillOverlay.SetCurrentValue(FrameworkElement.MarginProperty, new Thickness(trackOrigin.X, 0, 0, 0));
        HoverFillOverlay.SetCurrentValue(FrameworkElement.WidthProperty, fillWidth);

        if (_isHoverFillVisible)
            return;

        _isHoverFillVisible = true;
        HoverFillOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        HoverFillOverlay.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.55, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void HideHoverFill()
    {
        if (!_isHoverFillVisible)
            return;

        _isHoverFillVisible = false;
        HoverFillOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        HoverFillOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ShowDragPopupAt(double x)
    {
        if (_dragPopup is null || _track is null || RootGrid is null)
            return;

        CancelPendingHoverHide();
        RefreshPopupTheme();

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
                var scaleX = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, From = 0.85 };
                var scaleY = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(100)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, From = 0.85 };
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
        CorrectPopupPositionAfterOpen(_dragPopup, x);
    }

    private void CorrectPopupPositionAfterOpen(Popup popup, double x)
    {
        // Schedule a render-priority correction so ActualWidth/ActualHeight are available.
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (popup.Child is FrameworkElement fe && _track is not null && RootGrid is not null)
                {
                    // Force a measure/arrange pass if needed
                    fe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    fe.Arrange(new Rect(fe.DesiredSize));

                    // Keep a single, stable vertical position and only correct horizontal clamping.
                    var (left, top) = ComputePopupOffsets(x, fe);
                    popup.HorizontalOffset = left;
                    popup.VerticalOffset = top;
                }
            }
            catch
            {
                // Swallow any transient errors silently — best-effort correction.
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
            var scaleX = new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(90)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var scaleY = new DoubleAnimation(0.85, TimeSpan.FromMilliseconds(90)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
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

    private void ResolveTrack()
    {
        if (_track is not null)
            return;

        _track = SliderHost.Template?.FindName("PART_Track", SliderHost) as Track;
    }

    private void UpdatePreviewValueFromMousePosition(Point position)
    {
        var target = ResolveTargetValueFromMousePosition(position);

        _suppressValuePropagation = true;
        AnimatedValue = target;
        PreviewValue = target;
        _suppressValuePropagation = false;

        // update hover popup text/position
        if (_hoverPopupText is not null)
            _hoverPopupText.Text = FormatPreviewText(target);
        ShowHoverPopupAt(position.X);
        ShowHoverFillAt(position.X);
    }

    private void CommitPreviewToValue(int? explicitTarget = null)
    {
        var target = explicitTarget ?? (int)Math.Round(AnimatedValue);

        // Stop any in-flight easing before committing to avoid latching an intermediate animated value.
        BeginAnimation(AnimatedValueProperty, null);

        _suppressValuePropagation = true;
        AnimatedValue = target;
        PreviewValue = target;
        _suppressValuePropagation = false;

        _suppressAnimation = true;
        Value = target;
        _suppressAnimation = false;

        IsPreviewActive = false;
    }

    private int ResolveTargetValueFromMousePosition(Point position)
    {
        ResolveTrack();
        if (_track is null)
            return (int)Math.Round(AnimatedValue);

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
        return Snap(raw);
    }

    private string FormatPreviewText(double value)
    {
        // Format the preview text similarly to ResolveThumbToolTipText but using provided value
        var options = (ThumbToolTipOptions ?? string.Empty)
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (options.Length > 0)
        {
            var index = (int)Math.Round(value - Minimum);
            if (index >= 0 && index < options.Length)
                return options[index];
        }

        var seconds = (int)Math.Round(value);
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.ToString(seconds >= 3600 ? "h\\:mm\\:ss" : "m\\:ss");
    }

    private void OnSliderMouseMove(object sender, MouseEventArgs e)
    {
        if (_interactionMode == SeekInteractionMode.Thumb)
        {
            ResolveTrack();
            if (_track is null)
                return;

            HideHoverFill();
            HideHoverPopup();

            // when dragging the thumb, update drag popup position and text
            if (_thumb is null)
                return;

            var thumbCenter = _thumb.TranslatePoint(new Point(_thumb.ActualWidth / 2.0, _thumb.ActualHeight / 2.0), _track);
            var currentTarget = ResolveTargetValueFromMousePosition(thumbCenter);
            _suppressValuePropagation = true;
            AnimatedValue = currentTarget;
            PreviewValue = currentTarget;
            _suppressValuePropagation = false;

            if (_dragPopupText is not null)
                _dragPopupText.Text = FormatPreviewText(currentTarget);
            ShowDragPopupAt(thumbCenter.X);
            return;
        }
    }

    private void OnSliderMouseLeave(object sender, MouseEventArgs e)
    {
        // Defer hiding so popup overlays do not cause a false leave/re-enter flicker.
        ScheduleHoverHide();
    }

    private void ParentWindow_MouseMove(object? sender, MouseEventArgs e)
    {
        // Window-level mouse tracking so popups being displayed don't prevent hover detection.
        ResolveTrack();
        if (_track is null)
            return;

        if (_interactionMode == SeekInteractionMode.Thumb)
            return; // drag is handled via Thumb events

        var pos = e.GetPosition(_track);
        // Only treat as hover when pointer is strictly over the track area
        if (pos.X >= 0 && pos.X <= _track.ActualWidth && pos.Y >= 0 && pos.Y <= _track.ActualHeight)
        {
            CancelPendingHoverHide();
            var target = ResolveTargetValueFromMousePosition(pos);
            _suppressValuePropagation = true;
            PreviewValue = target;
            _suppressValuePropagation = false;

            if (_hoverPopupText is not null)
                _hoverPopupText.Text = FormatPreviewText(target);

            ShowHoverPopupAt(pos.X);
            ShowHoverFillAt(pos.X);
        }
        else
        {
            // Immediately hide when pointer leaves track area so popups don't block controls.
            HideHoverPopup();
            HideHoverFill();
        }
    }

    private void ParentWindow_LocationChanged(object? sender, EventArgs e)
    {
        // Reposition or hide popups when the window moves to avoid visual detachment.
        HideHoverPopup();
        HideDragPopup();
        HideHoverFill();
    }

    private void ParentWindow_StateChanged(object? sender, EventArgs e)
    {
        // Hide overlays when window minimized/changed
        HideHoverPopup();
        HideDragPopup();
        HideHoverFill();
    }

    private void Thumb_DragStarted(object? sender, DragStartedEventArgs e)
    {
        _interactionMode = SeekInteractionMode.Thumb;
        _suppressAnimation = true;

        // If a previous click animation is running, cancel it so thumb dragging is fully direct.
        BeginAnimation(AnimatedValueProperty, null);

        ResolveTrack();
        if (_track is null || _thumb is null)
            return;

        var thumbCenter = _thumb.TranslatePoint(new Point(_thumb.ActualWidth / 2.0, _thumb.ActualHeight / 2.0), _track);
        var currentTarget = ResolveTargetValueFromMousePosition(thumbCenter);

        _suppressValuePropagation = true;
        AnimatedValue = currentTarget;
        PreviewValue = currentTarget;
        _suppressValuePropagation = false;

        if (_dragPopupText is not null)
            _dragPopupText.Text = FormatPreviewText(currentTarget);

        HideHoverPopup();
        HideHoverFill();
        ShowDragPopupAt(thumbCenter.X);
    }

    private void Thumb_DragDelta(object? sender, DragDeltaEventArgs e)
    {
        if (_thumb is null)
            return;

        ResolveTrack();
        if (_track is null)
            return;

        var thumbCenter = _thumb.TranslatePoint(new Point(_thumb.ActualWidth / 2.0, _thumb.ActualHeight / 2.0), _track);
        var target = ResolveTargetValueFromMousePosition(thumbCenter);

        _suppressValuePropagation = true;
        AnimatedValue = target;
        PreviewValue = target;
        _suppressValuePropagation = false;

        if (_dragPopupText is not null)
            _dragPopupText.Text = FormatPreviewText(target);
        ShowDragPopupAt(thumbCenter.X);
    }

    private void Thumb_DragCompleted(object? sender, DragCompletedEventArgs e)
    {
        int? releaseTarget = null;
        if (_track is not null && _thumb is not null)
        {
            var thumbCenter = _thumb.TranslatePoint(new Point(_thumb.ActualWidth / 2.0, _thumb.ActualHeight / 2.0), _track);
            releaseTarget = ResolveTargetValueFromMousePosition(thumbCenter);
        }

        CommitPreviewToValue(releaseTarget);

        HideDragPopup();
        _interactionMode = SeekInteractionMode.None;
    }

    private void ResetTrackInteraction()
    {
        _interactionMode = SeekInteractionMode.None;

        if (SliderHost.IsMouseCaptured)
            SliderHost.ReleaseMouseCapture();
    }

    private static bool HasExceededDragThreshold(Point currentPoint, Point startPoint)
    {
        return Math.Abs(currentPoint.X - startPoint.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(currentPoint.Y - startPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }
}

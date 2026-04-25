using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AutoMidiPlayer.WPF.Helpers;

public static class ScrollViewerAutoFadeBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ScrollViewerAutoFadeBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(AutoFadeController),
            typeof(ScrollViewerAutoFadeBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static AutoFadeController? GetController(DependencyObject obj) => (AutoFadeController?)obj.GetValue(ControllerProperty);

    private static void SetController(DependencyObject obj, AutoFadeController? value) => obj.SetValue(ControllerProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
            return;

        var enabled = (bool)e.NewValue;
        var existingController = GetController(viewer);

        if (!enabled)
        {
            existingController?.Detach();
            SetController(viewer, null);
            return;
        }

        if (existingController != null)
            return;

        var controller = new AutoFadeController(viewer);
        SetController(viewer, controller);
    }

    private sealed class AutoFadeController
    {
        private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(140);
        private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(260);
        private static readonly TimeSpan InactivityDelay = TimeSpan.FromMilliseconds(1500);
        private const int WmMouseHWheel = 0x020E;
        private const double WheelStep = 40d;
        private const double LineButtonStepFactor = 0.18d;
        private const double MinLineButtonStep = 32d;
        private const double MaxLineButtonStep = 112d;

        private readonly ScrollViewer _viewer;
        private readonly DispatcherTimer _fadeTimer;
        private readonly SmoothScrollAnimator _smoothScrollAnimator;
        private readonly SmoothScrollAnimator _horizontalSmoothScrollAnimator;
        private ScrollBar? _verticalScrollBar;
        private ScrollBar? _horizontalScrollBar;
        private RepeatButton? _lineUpButton;
        private RepeatButton? _lineDownButton;
        private RepeatButton? _lineLeftButton;
        private RepeatButton? _lineRightButton;
        private ICommand? _lineUpOriginalCommand;
        private ICommand? _lineDownOriginalCommand;
        private ICommand? _lineLeftOriginalCommand;
        private ICommand? _lineRightOriginalCommand;
        private object? _lineUpOriginalCommandParameter;
        private object? _lineDownOriginalCommandParameter;
        private object? _lineLeftOriginalCommandParameter;
        private object? _lineRightOriginalCommandParameter;
        private IInputElement? _lineUpOriginalCommandTarget;
        private IInputElement? _lineDownOriginalCommandTarget;
        private IInputElement? _lineLeftOriginalCommandTarget;
        private IInputElement? _lineRightOriginalCommandTarget;
        private int? _verticalScrollBarOriginalZIndex;
        private int? _horizontalScrollBarOriginalZIndex;
        private bool _verticalScrollBarWired;
        private bool _horizontalScrollBarWired;
        private bool _isRetryScheduled;
        private HwndSource? _hwndSource;

        public AutoFadeController(ScrollViewer viewer)
        {
            _viewer = viewer;
            _fadeTimer = new DispatcherTimer { Interval = InactivityDelay };
            _fadeTimer.Tick += OnFadeTimerTick;
            _smoothScrollAnimator = new SmoothScrollAnimator(_viewer, SmoothScrollAnimatorOptions.Default);
            _horizontalSmoothScrollAnimator = new SmoothScrollAnimator(_viewer, SmoothScrollAnimatorOptions.Default, axis: SmoothScrollAxis.Horizontal);

            _viewer.Loaded += OnViewerLoaded;
            _viewer.Unloaded += OnViewerUnloaded;
            _viewer.ScrollChanged += OnViewerScrollChanged;
            _viewer.PreviewMouseWheel += OnViewerPreviewMouseWheel;

            if (_viewer.IsLoaded)
            {
                WireScrollBarsAndInitialize();
                AttachWindowMessageHook();
            }
        }

        public void Detach()
        {
            _fadeTimer.Stop();
            _fadeTimer.Tick -= OnFadeTimerTick;
            _smoothScrollAnimator.Dispose();
            _horizontalSmoothScrollAnimator.Dispose();

            _viewer.Loaded -= OnViewerLoaded;
            _viewer.Unloaded -= OnViewerUnloaded;
            _viewer.ScrollChanged -= OnViewerScrollChanged;
            _viewer.PreviewMouseWheel -= OnViewerPreviewMouseWheel;
            DetachWindowMessageHook();

            DetachVerticalScrollBar();
            DetachHorizontalScrollBar();
        }

        private void OnViewerLoaded(object sender, RoutedEventArgs e)
        {
            WireScrollBarsAndInitialize();
            AttachWindowMessageHook();
        }

        private void OnViewerUnloaded(object sender, RoutedEventArgs e)
        {
            _fadeTimer.Stop();
            _smoothScrollAnimator.Stop();
            _horizontalSmoothScrollAnimator.Stop();
            DetachWindowMessageHook();
        }

        private void OnViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (TryHandleHorizontalWheel(e.Delta, useNativeHorizontalConvention: false))
                    e.Handled = true;

                return;
            }

            if (_viewer.ComputedVerticalScrollBarVisibility != Visibility.Visible)
                return;

            if (_viewer.ScrollableHeight <= 0)
                return;

            if (!IsSmoothScrollingEnabled())
            {
                _smoothScrollAnimator.Stop();
                _smoothScrollAnimator.SyncTargetToCurrentOffset();
                ShowScrollBars();
                RestartFadeTimer();
                return;
            }

            e.Handled = true;

            if (IsLogicalScrollMode())
            {
                _smoothScrollAnimator.Stop();

                ApplyLogicalWheelScroll(e.Delta);
                _smoothScrollAnimator.SyncTargetToCurrentOffset();

                ShowScrollBars();
                RestartFadeTimer();
                return;
            }

            var step = GetWheelStep();
            var deltaOffset = -(e.Delta / 120d) * step;
            ApplySmoothDelta(deltaOffset);
        }

        private bool TryHandleHorizontalWheel(int wheelDelta, bool useNativeHorizontalConvention)
        {
            if (_viewer.ComputedHorizontalScrollBarVisibility != Visibility.Visible)
                return false;

            if (_viewer.ScrollableWidth <= 0)
                return false;

            if (!IsSmoothScrollingEnabled() || IsLogicalScrollMode())
            {
                _horizontalSmoothScrollAnimator.Stop();
                ApplyLogicalHorizontalWheelScroll(wheelDelta, useNativeHorizontalConvention);
                _horizontalSmoothScrollAnimator.SyncTargetToCurrentOffset();
                ShowScrollBars();
                RestartFadeTimer();
                return true;
            }

            var step = GetHorizontalWheelStep();
            var directionFactor = useNativeHorizontalConvention ? 1d : -1d;
            var deltaOffset = (wheelDelta / 120d) * step * directionFactor;
            ApplyHorizontalSmoothDelta(deltaOffset);
            return true;
        }

        private void OnViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0 &&
                e.HorizontalChange == 0 && e.ExtentWidthChange == 0 && e.ViewportWidthChange == 0)
                return;

            WireScrollBarsAndInitialize();

            UpdateScrollBarVisibility(_verticalScrollBar, _viewer.ScrollableHeight <= 0.5);
            UpdateScrollBarVisibility(_horizontalScrollBar, _viewer.ScrollableWidth <= 0.5);

            if (_smoothScrollAnimator.IsRunning || _horizontalSmoothScrollAnimator.IsRunning)
                return;

            if (HasScrollableArea())
            {
                ShowScrollBars();
                RestartFadeTimer();
            }
        }

        private void OnScrollBarMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _fadeTimer.Stop();
            ShowScrollBars();
        }

        private void OnScrollBarMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            RestartFadeTimer();
        }

        private void OnLineUpButtonClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            HandleLineButtonScroll(-1);
        }

        private void OnLineDownButtonClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            HandleLineButtonScroll(1);
        }

        private void OnLineLeftButtonClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            HandleHorizontalLineButtonScroll(-1);
        }

        private void OnLineRightButtonClick(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            HandleHorizontalLineButtonScroll(1);
        }

        private void OnFadeTimerTick(object? sender, EventArgs e)
        {
            _fadeTimer.Stop();
            FadeOutScrollBars();
        }

        private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (handled || msg != WmMouseHWheel)
                return IntPtr.Zero;

            if (!_viewer.IsLoaded || !_viewer.IsVisible)
                return IntPtr.Zero;

            if (!IsPointerOverCurrentScrollViewer())
                return IntPtr.Zero;

            var wheelDelta = GetWheelDelta(wParam);
            if (wheelDelta == 0)
                return IntPtr.Zero;

            if (!TryHandleHorizontalWheel(wheelDelta, useNativeHorizontalConvention: true))
                return IntPtr.Zero;

            handled = true;
            return IntPtr.Zero;
        }

        private void ApplySmoothDelta(double deltaOffset)
        {
            if (_viewer.ScrollableHeight <= 0)
                return;

            if (!_smoothScrollAnimator.IsRunning)
                _smoothScrollAnimator.SyncTargetToCurrentOffset();

            var currentOffset = _viewer.VerticalOffset;
            var step = GetWheelStep();
            var maxLead = Math.Max(step * 10d, _viewer.ViewportHeight * 1.15d);
            var minTarget = Math.Max(0d, currentOffset - maxLead);
            var maxTarget = Math.Min(_viewer.ScrollableHeight, currentOffset + maxLead);

            _smoothScrollAnimator.ApplyDelta(deltaOffset, minTarget, maxTarget, resetOnDirectionChange: true);

            ShowScrollBars();
            RestartFadeTimer();
        }

        private void HandleLineButtonScroll(int direction)
        {
            if (direction == 0)
                return;

            if (!IsSmoothScrollingEnabled() || IsLogicalScrollMode())
            {
                if (direction > 0)
                    _viewer.LineDown();
                else
                    _viewer.LineUp();

                _smoothScrollAnimator.SyncTargetToCurrentOffset();
                ShowScrollBars();
                RestartFadeTimer();
                return;
            }

            ApplySmoothDelta(direction * GetLineButtonStep());
        }

        private void HandleHorizontalLineButtonScroll(int direction)
        {
            if (direction == 0)
                return;

            if (!IsSmoothScrollingEnabled() || IsLogicalScrollMode())
            {
                if (direction > 0)
                    _viewer.LineRight();
                else
                    _viewer.LineLeft();

                _horizontalSmoothScrollAnimator.SyncTargetToCurrentOffset();
                ShowScrollBars();
                RestartFadeTimer();
                return;
            }

            ApplyHorizontalSmoothDelta(direction * GetHorizontalLineButtonStep());
        }

        private double GetHorizontalLineButtonStep()
        {
            var basedOnViewport = _viewer.ViewportWidth * LineButtonStepFactor;
            return Math.Clamp(basedOnViewport, MinLineButtonStep, MaxLineButtonStep);
        }

        private void ApplyHorizontalSmoothDelta(double deltaOffset)
        {
            if (_viewer.ScrollableWidth <= 0)
                return;

            if (!_horizontalSmoothScrollAnimator.IsRunning)
                _horizontalSmoothScrollAnimator.SyncTargetToCurrentOffset();

            var currentOffset = _viewer.HorizontalOffset;
            var step = GetHorizontalLineButtonStep();
            var maxLead = Math.Max(step * 10d, _viewer.ViewportWidth * 1.15d);
            var minTarget = Math.Max(0d, currentOffset - maxLead);
            var maxTarget = Math.Min(_viewer.ScrollableWidth, currentOffset + maxLead);

            _horizontalSmoothScrollAnimator.ApplyDelta(deltaOffset, minTarget, maxTarget, resetOnDirectionChange: true);

            ShowScrollBars();
            RestartFadeTimer();
        }

        private void ApplyLogicalHorizontalWheelScroll(int wheelDelta, bool useNativeHorizontalConvention)
        {
            var directionSource = useNativeHorizontalConvention ? wheelDelta : -wheelDelta;
            var direction = Math.Sign(directionSource);
            if (direction == 0)
                return;

            var linesPerNotch = Math.Max(1, SystemParameters.WheelScrollLines);
            var notches = Math.Abs(wheelDelta) / 120d;
            var steps = (int)Math.Ceiling(notches * linesPerNotch);
            steps = Math.Clamp(steps, 1, 8);

            if (direction > 0)
            {
                for (var i = 0; i < steps; i++)
                    _viewer.LineRight();
            }
            else
            {
                for (var i = 0; i < steps; i++)
                    _viewer.LineLeft();
            }
        }

        private double GetWheelStep()
        {
            return WheelStep;
        }

        private double GetHorizontalWheelStep()
        {
            return WheelStep;
        }

        private double GetLineButtonStep()
        {
            var basedOnViewport = _viewer.ViewportHeight * LineButtonStepFactor;
            return Math.Clamp(basedOnViewport, MinLineButtonStep, MaxLineButtonStep);
        }

        private bool IsLogicalScrollMode()
        {
            if (!ScrollViewer.GetCanContentScroll(_viewer))
                return false;

            var owner = ResolveOwningItemsControl();
            if (owner is null)
                return false;

            return VirtualizingPanel.GetScrollUnit(owner) != ScrollUnit.Pixel;
        }

        private static bool IsSmoothScrollingEnabled()
        {
            if (Application.Current?.Resources["SmoothScrollingEnabled"] is bool isEnabled)
                return isEnabled;

            return true;
        }

        private void ApplyLogicalWheelScroll(int wheelDelta)
        {
            var direction = Math.Sign(-wheelDelta);
            if (direction == 0)
                return;

            var linesPerNotch = Math.Max(1, SystemParameters.WheelScrollLines);
            var notches = Math.Abs(wheelDelta) / 120d;
            var steps = (int)Math.Ceiling(notches * linesPerNotch);
            steps = Math.Clamp(steps, 1, 8);

            if (direction > 0)
            {
                for (var i = 0; i < steps; i++)
                    _viewer.LineDown();
            }
            else
            {
                for (var i = 0; i < steps; i++)
                    _viewer.LineUp();
            }
        }

        private void WireScrollBarsAndInitialize()
        {
            WireVerticalScrollBar();
            WireHorizontalScrollBar();
            AttachWindowMessageHook();

            if (_isRetryScheduled || !NeedsRetry())
                return;

            _isRetryScheduled = true;
            _viewer.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _isRetryScheduled = false;
                WireScrollBarsAndInitialize();
            }));
        }

        private void WireVerticalScrollBar()
        {
            if (_verticalScrollBar == null || !IsDescendantOf(_viewer, _verticalScrollBar))
                _verticalScrollBar = FindDescendant<ScrollBar>(_viewer, bar => bar.Orientation == Orientation.Vertical);

            if (_verticalScrollBar == null)
                return;

            _verticalScrollBar.ApplyTemplate();

            if (!_verticalScrollBarWired)
            {
                _verticalScrollBar.MouseEnter += OnScrollBarMouseEnter;
                _verticalScrollBar.MouseLeave += OnScrollBarMouseLeave;
                _verticalScrollBarWired = true;
            }

            if (_verticalScrollBarOriginalZIndex is null && VisualTreeHelper.GetParent(_verticalScrollBar) is Panel)
            {
                _verticalScrollBarOriginalZIndex = Panel.GetZIndex(_verticalScrollBar);
                Panel.SetZIndex(_verticalScrollBar, _verticalScrollBarOriginalZIndex.Value + 100);
            }

            WireLineButtons();
        }

        private void WireHorizontalScrollBar()
        {
            if (_horizontalScrollBar == null || !IsDescendantOf(_viewer, _horizontalScrollBar))
                _horizontalScrollBar = FindDescendant<ScrollBar>(_viewer, bar => bar.Orientation == Orientation.Horizontal);

            if (_horizontalScrollBar == null)
                return;

            _horizontalScrollBar.ApplyTemplate();

            if (!_horizontalScrollBarWired)
            {
                _horizontalScrollBar.MouseEnter += OnScrollBarMouseEnter;
                _horizontalScrollBar.MouseLeave += OnScrollBarMouseLeave;
                _horizontalScrollBarWired = true;
            }

            if (_horizontalScrollBarOriginalZIndex is null && VisualTreeHelper.GetParent(_horizontalScrollBar) is Panel)
            {
                _horizontalScrollBarOriginalZIndex = Panel.GetZIndex(_horizontalScrollBar);
                Panel.SetZIndex(_horizontalScrollBar, _horizontalScrollBarOriginalZIndex.Value + 100);
            }

            WireHorizontalLineButtons();
        }

        private bool NeedsRetry()
        {
            return (_viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible && _verticalScrollBar == null)
                || (_viewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible && _horizontalScrollBar == null);
        }

        private void DetachVerticalScrollBar()
        {
            if (_verticalScrollBar == null)
                return;

            UnwireLineButtons();

            if (_verticalScrollBarWired)
            {
                _verticalScrollBar.MouseEnter -= OnScrollBarMouseEnter;
                _verticalScrollBar.MouseLeave -= OnScrollBarMouseLeave;
                _verticalScrollBarWired = false;
            }

            _verticalScrollBar.BeginAnimation(UIElement.OpacityProperty, null);

            if (_verticalScrollBarOriginalZIndex.HasValue && VisualTreeHelper.GetParent(_verticalScrollBar) is Panel)
                Panel.SetZIndex(_verticalScrollBar, _verticalScrollBarOriginalZIndex.Value);

            _verticalScrollBarOriginalZIndex = null;
            _verticalScrollBar = null;
        }

        private void DetachHorizontalScrollBar()
        {
            if (_horizontalScrollBar == null)
                return;

            UnwireHorizontalLineButtons();

            if (_horizontalScrollBarWired)
            {
                _horizontalScrollBar.MouseEnter -= OnScrollBarMouseEnter;
                _horizontalScrollBar.MouseLeave -= OnScrollBarMouseLeave;
                _horizontalScrollBarWired = false;
            }

            _horizontalScrollBar.BeginAnimation(UIElement.OpacityProperty, null);

            if (_horizontalScrollBarOriginalZIndex.HasValue && VisualTreeHelper.GetParent(_horizontalScrollBar) is Panel)
                Panel.SetZIndex(_horizontalScrollBar, _horizontalScrollBarOriginalZIndex.Value);

            _horizontalScrollBarOriginalZIndex = null;
            _horizontalScrollBar = null;
        }

        private void WireLineButtons()
        {
            if (_verticalScrollBar == null)
                return;

            var template = _verticalScrollBar.Template;
            if (template == null)
                return;

            var lineUp = template.FindName("LineUpButton", _verticalScrollBar) as RepeatButton;
            var lineDown = template.FindName("LineDownButton", _verticalScrollBar) as RepeatButton;

            if (ReferenceEquals(lineUp, _lineUpButton) && ReferenceEquals(lineDown, _lineDownButton))
                return;

            UnwireLineButtons();

            if (lineUp != null)
            {
                _lineUpButton = lineUp;
                _lineUpOriginalCommand = lineUp.Command;
                _lineUpOriginalCommandParameter = lineUp.CommandParameter;
                _lineUpOriginalCommandTarget = lineUp.CommandTarget;
                lineUp.Command = null;
                lineUp.CommandParameter = null;
                lineUp.CommandTarget = null;
                lineUp.Click += OnLineUpButtonClick;
            }

            if (lineDown != null)
            {
                _lineDownButton = lineDown;
                _lineDownOriginalCommand = lineDown.Command;
                _lineDownOriginalCommandParameter = lineDown.CommandParameter;
                _lineDownOriginalCommandTarget = lineDown.CommandTarget;
                lineDown.Command = null;
                lineDown.CommandParameter = null;
                lineDown.CommandTarget = null;
                lineDown.Click += OnLineDownButtonClick;
            }
        }

        private void WireHorizontalLineButtons()
        {
            if (_horizontalScrollBar == null)
                return;

            var template = _horizontalScrollBar.Template;
            if (template == null)
                return;

            var lineLeft = template.FindName("LineLeftButton", _horizontalScrollBar) as RepeatButton;
            var lineRight = template.FindName("LineRightButton", _horizontalScrollBar) as RepeatButton;

            if (ReferenceEquals(lineLeft, _lineLeftButton) && ReferenceEquals(lineRight, _lineRightButton))
                return;

            UnwireHorizontalLineButtons();

            if (lineLeft != null)
            {
                _lineLeftButton = lineLeft;
                _lineLeftOriginalCommand = lineLeft.Command;
                _lineLeftOriginalCommandParameter = lineLeft.CommandParameter;
                _lineLeftOriginalCommandTarget = lineLeft.CommandTarget;
                lineLeft.Command = null;
                lineLeft.CommandParameter = null;
                lineLeft.CommandTarget = null;
                lineLeft.Click += OnLineLeftButtonClick;
            }

            if (lineRight != null)
            {
                _lineRightButton = lineRight;
                _lineRightOriginalCommand = lineRight.Command;
                _lineRightOriginalCommandParameter = lineRight.CommandParameter;
                _lineRightOriginalCommandTarget = lineRight.CommandTarget;
                lineRight.Command = null;
                lineRight.CommandParameter = null;
                lineRight.CommandTarget = null;
                lineRight.Click += OnLineRightButtonClick;
            }
        }

        private void UnwireLineButtons()
        {
            if (_lineUpButton != null)
            {
                _lineUpButton.Click -= OnLineUpButtonClick;
                _lineUpButton.Command = _lineUpOriginalCommand;
                _lineUpButton.CommandParameter = _lineUpOriginalCommandParameter;
                _lineUpButton.CommandTarget = _lineUpOriginalCommandTarget;
                _lineUpButton = null;
            }

            if (_lineDownButton != null)
            {
                _lineDownButton.Click -= OnLineDownButtonClick;
                _lineDownButton.Command = _lineDownOriginalCommand;
                _lineDownButton.CommandParameter = _lineDownOriginalCommandParameter;
                _lineDownButton.CommandTarget = _lineDownOriginalCommandTarget;
                _lineDownButton = null;
            }

            _lineUpOriginalCommand = null;
            _lineDownOriginalCommand = null;
            _lineUpOriginalCommandParameter = null;
            _lineDownOriginalCommandParameter = null;
            _lineUpOriginalCommandTarget = null;
            _lineDownOriginalCommandTarget = null;
        }

        private void UnwireHorizontalLineButtons()
        {
            if (_lineLeftButton != null)
            {
                _lineLeftButton.Click -= OnLineLeftButtonClick;
                _lineLeftButton.Command = _lineLeftOriginalCommand;
                _lineLeftButton.CommandParameter = _lineLeftOriginalCommandParameter;
                _lineLeftButton.CommandTarget = _lineLeftOriginalCommandTarget;
                _lineLeftButton = null;
            }

            if (_lineRightButton != null)
            {
                _lineRightButton.Click -= OnLineRightButtonClick;
                _lineRightButton.Command = _lineRightOriginalCommand;
                _lineRightButton.CommandParameter = _lineRightOriginalCommandParameter;
                _lineRightButton.CommandTarget = _lineRightOriginalCommandTarget;
                _lineRightButton = null;
            }

            _lineLeftOriginalCommand = null;
            _lineRightOriginalCommand = null;
            _lineLeftOriginalCommandParameter = null;
            _lineRightOriginalCommandParameter = null;
            _lineLeftOriginalCommandTarget = null;
            _lineRightOriginalCommandTarget = null;
        }

        private void ShowScrollBars()
        {
            ShowScrollBar(_verticalScrollBar, _viewer.ScrollableHeight > 0.5);
            ShowScrollBar(_horizontalScrollBar, _viewer.ScrollableWidth > 0.5);
        }

        private static void ShowScrollBar(ScrollBar? scrollBar, bool shouldShow)
        {
            if (scrollBar == null || !shouldShow)
                return;

            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = FadeInDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            scrollBar.BeginAnimation(UIElement.OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
        }

        private void FadeOutScrollBars()
        {
            FadeOutScrollBar(_verticalScrollBar, _viewer.ScrollableHeight > 0.5);
            FadeOutScrollBar(_horizontalScrollBar, _viewer.ScrollableWidth > 0.5);
        }

        private static void FadeOutScrollBar(ScrollBar? scrollBar, bool shouldShow)
        {
            if (scrollBar == null || !shouldShow)
                return;

            if (scrollBar.IsMouseOver || scrollBar.IsMouseCaptureWithin)
                return;

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = FadeOutDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            scrollBar.BeginAnimation(UIElement.OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        }

        private void UpdateScrollBarVisibility(ScrollBar? scrollBar, bool shouldHide)
        {
            if (scrollBar == null || !shouldHide)
                return;

            scrollBar.BeginAnimation(UIElement.OpacityProperty, null);
            scrollBar.Opacity = 0;
        }

        private bool HasScrollableArea()
        {
            return _viewer.ScrollableHeight > 0.5 || _viewer.ScrollableWidth > 0.5;
        }

        private void RestartFadeTimer()
        {
            _fadeTimer.Stop();
            _fadeTimer.Start();
        }

        private ItemsControl? ResolveOwningItemsControl()
        {
            if (_viewer.TemplatedParent is ItemsControl templatedItemsControl)
                return templatedItemsControl;

            var itemsHost = FindDescendant<Panel>(_viewer, panel => ItemsControl.GetItemsOwner(panel) is not null);
            if (itemsHost is null)
                return null;

            return ItemsControl.GetItemsOwner(itemsHost);
        }

        private void AttachWindowMessageHook()
        {
            var source = PresentationSource.FromVisual(_viewer) as HwndSource;
            if (source is null)
                return;

            if (ReferenceEquals(_hwndSource, source))
                return;

            DetachWindowMessageHook();
            _hwndSource = source;
            _hwndSource.AddHook(OnWindowMessage);
        }

        private void DetachWindowMessageHook()
        {
            if (_hwndSource is null)
                return;

            _hwndSource.RemoveHook(OnWindowMessage);
            _hwndSource = null;
        }

        private bool IsPointerOverCurrentScrollViewer()
        {
            if (Mouse.DirectlyOver is not DependencyObject hovered)
                return false;

            var nearestViewer = FindAncestorOrSelf<ScrollViewer>(hovered);
            if (nearestViewer != null)
                return ReferenceEquals(nearestViewer, _viewer);

            return IsDescendantOf(_viewer, hovered);
        }

        private static int GetWheelDelta(IntPtr wParam)
        {
            var wParamValue = wParam.ToInt64();
            return unchecked((short)((wParamValue >> 16) & 0xFFFF));
        }

        private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject element)
        {
            var current = element;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;

                current = GetParent(current);
            }

            return false;
        }

        private static T? FindAncestorOrSelf<T>(DependencyObject start)
            where T : DependencyObject
        {
            DependencyObject? current = start;
            while (current != null)
            {
                if (current is T typed)
                    return typed;

                current = GetParent(current);
            }

            return null;
        }

        private static DependencyObject? GetParent(DependencyObject element)
        {
            if (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
                return VisualTreeHelper.GetParent(element);

            return LogicalTreeHelper.GetParent(element);
        }

        private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool>? predicate = null)
            where T : DependencyObject
        {
            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild && (predicate == null || predicate(typedChild)))
                    return typedChild;

                var result = FindDescendant(child, predicate);
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}

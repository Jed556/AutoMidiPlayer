using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
                WireScrollBarsAndInitialize();
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

            DetachVerticalScrollBar();
            DetachHorizontalScrollBar();
        }

        private void OnViewerLoaded(object sender, RoutedEventArgs e)
        {
            WireScrollBarsAndInitialize();
        }

        private void OnViewerUnloaded(object sender, RoutedEventArgs e)
        {
            _fadeTimer.Stop();
            _smoothScrollAnimator.Stop();
            _horizontalSmoothScrollAnimator.Stop();
        }

        private void OnViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_viewer.ComputedVerticalScrollBarVisibility != Visibility.Visible)
                return;

            if (_viewer.ScrollableHeight <= 0 || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
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

        private double GetWheelStep()
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

            // For templated controls (e.g. ListView), the ScrollViewer usually has
            // the owning ItemsControl as its TemplatedParent.
            if (_viewer.TemplatedParent is ItemsControl templatedItemsControl)
                return VirtualizingPanel.GetScrollUnit(templatedItemsControl) != ScrollUnit.Pixel;

            // Prefer the owning ItemsControl setting (e.g. ListView) because the
            // realized panel can still report item scrolling while template values
            // are being applied.
            var itemsControl = FindAncestor<ItemsControl>(_viewer);
            if (itemsControl != null)
                return VirtualizingPanel.GetScrollUnit(itemsControl) != ScrollUnit.Pixel;

            var virtualizingPanel = FindDescendant<VirtualizingPanel>(_viewer);
            if (virtualizingPanel != null)
            {
                var owner = ItemsControl.GetItemsOwner(virtualizingPanel);
                if (owner != null)
                    return VirtualizingPanel.GetScrollUnit(owner) != ScrollUnit.Pixel;

                // When owner resolution fails, prefer pixel smoothing to avoid
                // falling back to item-based line scrolling in virtualized lists.
                return false;
            }

            return false;
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
            if (_verticalScrollBar == null)
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
            if (_horizontalScrollBar == null)
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

        private static T? FindAncestor<T>(DependencyObject start)
            where T : DependencyObject
        {
            var current = VisualTreeHelper.GetParent(start);
            while (current != null)
            {
                if (current is T typed)
                    return typed;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}

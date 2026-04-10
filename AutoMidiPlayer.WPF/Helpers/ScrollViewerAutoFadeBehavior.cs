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
        private static readonly TimeSpan SmoothScrollFrameInterval = TimeSpan.FromMilliseconds(16);
        private const double WheelStep = 40d;
        private const double SmoothingFactor = 0.32d;
        private const double SnapThreshold = 2.0d;
        private const double MaxSmoothStep = 96d;

        private readonly ScrollViewer _viewer;
        private readonly DispatcherTimer _fadeTimer;
        private readonly DispatcherTimer _smoothScrollTimer;
        private ScrollBar? _verticalScrollBar;
        private double _targetVerticalOffset;
        private bool _isSmoothScrolling;

        public AutoFadeController(ScrollViewer viewer)
        {
            _viewer = viewer;
            _fadeTimer = new DispatcherTimer { Interval = InactivityDelay };
            _fadeTimer.Tick += OnFadeTimerTick;
            _smoothScrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = SmoothScrollFrameInterval };
            _smoothScrollTimer.Tick += OnSmoothScrollTick;
            _targetVerticalOffset = _viewer.VerticalOffset;

            _viewer.Loaded += OnViewerLoaded;
            _viewer.Unloaded += OnViewerUnloaded;
            _viewer.ScrollChanged += OnViewerScrollChanged;
            _viewer.PreviewMouseWheel += OnViewerPreviewMouseWheel;

            if (_viewer.IsLoaded)
                WireScrollBarAndInitialize();
        }

        public void Detach()
        {
            _fadeTimer.Stop();
            _fadeTimer.Tick -= OnFadeTimerTick;
            _smoothScrollTimer.Stop();
            _smoothScrollTimer.Tick -= OnSmoothScrollTick;

            _viewer.Loaded -= OnViewerLoaded;
            _viewer.Unloaded -= OnViewerUnloaded;
            _viewer.ScrollChanged -= OnViewerScrollChanged;
            _viewer.PreviewMouseWheel -= OnViewerPreviewMouseWheel;

            if (_verticalScrollBar != null)
            {
                _verticalScrollBar.MouseEnter -= OnScrollBarMouseEnter;
                _verticalScrollBar.MouseLeave -= OnScrollBarMouseLeave;
                _verticalScrollBar.BeginAnimation(UIElement.OpacityProperty, null);
                _verticalScrollBar = null;
            }
        }

        private void OnViewerLoaded(object sender, RoutedEventArgs e)
        {
            WireScrollBarAndInitialize();
        }

        private void OnViewerUnloaded(object sender, RoutedEventArgs e)
        {
            _fadeTimer.Stop();
            _smoothScrollTimer.Stop();
        }

        private void OnViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_viewer.ComputedVerticalScrollBarVisibility != Visibility.Visible)
                return;

            if (_viewer.ScrollableHeight <= 0 || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                return;

            if (!IsSmoothScrollingEnabled())
            {
                _smoothScrollTimer.Stop();
                _isSmoothScrolling = false;
                _targetVerticalOffset = _viewer.VerticalOffset;
                ShowScrollBar();
                RestartFadeTimer();
                return;
            }

            e.Handled = true;

            if (IsLogicalScrollMode())
            {
                _smoothScrollTimer.Stop();
                _isSmoothScrolling = false;

                ApplyLogicalWheelScroll(e.Delta);
                _targetVerticalOffset = _viewer.VerticalOffset;

                ShowScrollBar();
                RestartFadeTimer();
                return;
            }

            var step = GetWheelStep();
            var deltaOffset = -(e.Delta / 120d) * step;
            if (!_smoothScrollTimer.IsEnabled)
                _targetVerticalOffset = _viewer.VerticalOffset;

            var currentOffset = _viewer.VerticalOffset;
            var currentDirection = Math.Sign(_targetVerticalOffset - currentOffset);
            var incomingDirection = Math.Sign(deltaOffset);
            if (_smoothScrollTimer.IsEnabled && currentDirection != 0 && incomingDirection != 0 && currentDirection != incomingDirection)
                _targetVerticalOffset = currentOffset;

            var maxLead = Math.Max(step * 10d, _viewer.ViewportHeight * 1.15d);
            var minTarget = Math.Max(0d, currentOffset - maxLead);
            var maxTarget = Math.Min(_viewer.ScrollableHeight, currentOffset + maxLead);

            _targetVerticalOffset = Math.Clamp(_targetVerticalOffset + deltaOffset, minTarget, maxTarget);
            if (!_smoothScrollTimer.IsEnabled)
            {
                AdvanceSmoothScroll();
                _smoothScrollTimer.Start();
            }

            ShowScrollBar();
            RestartFadeTimer();
        }

        private void OnViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
                return;

            WireScrollBarAndInitialize();
            if (_verticalScrollBar == null)
                return;

            if (_viewer.ExtentHeight <= _viewer.ViewportHeight)
            {
                _fadeTimer.Stop();
                _smoothScrollTimer.Stop();
                _isSmoothScrolling = false;
                _targetVerticalOffset = 0;
                _verticalScrollBar.BeginAnimation(UIElement.OpacityProperty, null);
                _verticalScrollBar.Opacity = 0;
                return;
            }

            if (_isSmoothScrolling)
                return;

            _targetVerticalOffset = _viewer.VerticalOffset;

            ShowScrollBar();
            RestartFadeTimer();
        }

        private void OnScrollBarMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _fadeTimer.Stop();
            ShowScrollBar();
        }

        private void OnScrollBarMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            RestartFadeTimer();
        }

        private void OnFadeTimerTick(object? sender, EventArgs e)
        {
            _fadeTimer.Stop();
            FadeOutScrollBar();
        }

        private void OnSmoothScrollTick(object? sender, EventArgs e)
        {
            AdvanceSmoothScroll();
        }

        private void AdvanceSmoothScroll()
        {
            var currentOffset = _viewer.VerticalOffset;
            var delta = _targetVerticalOffset - currentOffset;

            if (Math.Abs(delta) <= SnapThreshold)
            {
                _smoothScrollTimer.Stop();
                _isSmoothScrolling = false;
                _viewer.ScrollToVerticalOffset(_targetVerticalOffset);
                return;
            }

            _isSmoothScrolling = true;
            var smoothStep = Math.Clamp(delta * SmoothingFactor, -MaxSmoothStep, MaxSmoothStep);

            var nextOffset = currentOffset + smoothStep;
            _viewer.ScrollToVerticalOffset(Math.Clamp(nextOffset, 0d, _viewer.ScrollableHeight));
        }

        private double GetWheelStep()
        {
            return WheelStep;
        }

        private bool IsLogicalScrollMode()
        {
            if (!ScrollViewer.GetCanContentScroll(_viewer))
                return false;

            // Prefer the owning ItemsControl setting (e.g. ListView) because the
            // realized panel can still report item scrolling while template values
            // are being applied.
            var itemsControl = FindAncestor<ItemsControl>(_viewer);
            if (itemsControl != null)
                return VirtualizingPanel.GetScrollUnit(itemsControl) != ScrollUnit.Pixel;

            var virtualizingPanel = FindDescendant<VirtualizingPanel>(_viewer);
            if (virtualizingPanel != null)
                return VirtualizingPanel.GetScrollUnit(virtualizingPanel) != ScrollUnit.Pixel;

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

        private void WireScrollBarAndInitialize()
        {
            if (_verticalScrollBar == null)
            {
                _verticalScrollBar = FindDescendant<ScrollBar>(_viewer, bar => bar.Orientation == Orientation.Vertical);
                if (_verticalScrollBar != null)
                {
                    _verticalScrollBar.MouseEnter += OnScrollBarMouseEnter;
                    _verticalScrollBar.MouseLeave += OnScrollBarMouseLeave;
                }
            }

            if (_verticalScrollBar == null)
            {
                _viewer.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (_verticalScrollBar != null)
                        return;

                    _verticalScrollBar = FindDescendant<ScrollBar>(_viewer, bar => bar.Orientation == Orientation.Vertical);
                    if (_verticalScrollBar == null)
                        return;

                    _verticalScrollBar.MouseEnter += OnScrollBarMouseEnter;
                    _verticalScrollBar.MouseLeave += OnScrollBarMouseLeave;
                }));
            }
        }

        private void ShowScrollBar()
        {
            if (_verticalScrollBar == null)
                return;

            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = FadeInDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            _verticalScrollBar.BeginAnimation(UIElement.OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
        }

        private void FadeOutScrollBar()
        {
            if (_verticalScrollBar == null)
                return;

            if (_verticalScrollBar.IsMouseOver || _verticalScrollBar.IsMouseCaptureWithin)
                return;

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = FadeOutDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            _verticalScrollBar.BeginAnimation(UIElement.OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
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

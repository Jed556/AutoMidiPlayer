using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoMidiPlayer.WPF.Helpers;

public static class ScrollEdgeFadeBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ScrollEdgeFadeBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty FadeRampDistanceProperty =
        DependencyProperty.RegisterAttached(
            "FadeRampDistance",
            typeof(double),
            typeof(ScrollEdgeFadeBehavior),
            new PropertyMetadata(70d, OnFadeSettingChanged));

    public static readonly DependencyProperty EdgeFadeThicknessProperty =
        DependencyProperty.RegisterAttached(
            "EdgeFadeThickness",
            typeof(double),
            typeof(ScrollEdgeFadeBehavior),
            new PropertyMetadata(0.24d, OnFadeSettingChanged));

    public static readonly DependencyProperty MaxFadeOpacityProperty =
        DependencyProperty.RegisterAttached(
            "MaxFadeOpacity",
            typeof(double),
            typeof(ScrollEdgeFadeBehavior),
            new PropertyMetadata(1d, OnFadeSettingChanged));

    public static readonly DependencyProperty TopInsetProperty =
        DependencyProperty.RegisterAttached(
            "TopInset",
            typeof(double),
            typeof(ScrollEdgeFadeBehavior),
            new PropertyMetadata(0d, OnFadeSettingChanged));

    public static readonly DependencyProperty BottomInsetProperty =
        DependencyProperty.RegisterAttached(
            "BottomInset",
            typeof(double),
            typeof(ScrollEdgeFadeBehavior),
            new PropertyMetadata(0d, OnFadeSettingChanged));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(FadeController),
            typeof(ScrollEdgeFadeBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static double GetFadeRampDistance(DependencyObject obj) => (double)obj.GetValue(FadeRampDistanceProperty);

    public static void SetFadeRampDistance(DependencyObject obj, double value) => obj.SetValue(FadeRampDistanceProperty, value);

    public static double GetEdgeFadeThickness(DependencyObject obj) => (double)obj.GetValue(EdgeFadeThicknessProperty);

    public static void SetEdgeFadeThickness(DependencyObject obj, double value) => obj.SetValue(EdgeFadeThicknessProperty, value);

    public static double GetMaxFadeOpacity(DependencyObject obj) => (double)obj.GetValue(MaxFadeOpacityProperty);

    public static void SetMaxFadeOpacity(DependencyObject obj, double value) => obj.SetValue(MaxFadeOpacityProperty, value);

    public static double GetTopInset(DependencyObject obj) => (double)obj.GetValue(TopInsetProperty);

    public static void SetTopInset(DependencyObject obj, double value) => obj.SetValue(TopInsetProperty, value);

    public static double GetBottomInset(DependencyObject obj) => (double)obj.GetValue(BottomInsetProperty);

    public static void SetBottomInset(DependencyObject obj, double value) => obj.SetValue(BottomInsetProperty, value);

    private static FadeController? GetController(DependencyObject obj) => (FadeController?)obj.GetValue(ControllerProperty);

    private static void SetController(DependencyObject obj, FadeController? value) => obj.SetValue(ControllerProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement host)
            return;

        var enabled = (bool)e.NewValue;
        var existingController = GetController(host);

        if (!enabled)
        {
            existingController?.Detach();
            SetController(host, null);
            return;
        }

        if (existingController is not null)
            return;

        var controller = new FadeController(host);
        SetController(host, controller);
    }

    private static void OnFadeSettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        GetController(d)?.Update();
    }

    private sealed class FadeController
    {
        private readonly FrameworkElement _host;
        private readonly LinearGradientBrush _mask;
        private readonly GradientStop _topOutsideStop;
        private readonly GradientStop _topEdgeStop;
        private readonly GradientStop _topSolidStop;
        private readonly GradientStop _bottomSolidStop;
        private readonly GradientStop _bottomEdgeStop;
        private readonly GradientStop _bottomOutsideStop;

        private ScrollViewer? _viewer;
        private FrameworkElement? _maskTarget;
        private Brush? _maskTargetOriginalMask;
        private ScrollBar? _verticalScrollBar;
        private int? _verticalScrollBarOriginalZIndex;
        private bool _isRetryScheduled;

        public FadeController(FrameworkElement host)
        {
            _host = host;
            _host.Loaded += OnHostLoaded;
            _host.Unloaded += OnHostUnloaded;
            _host.SizeChanged += OnHostSizeChanged;

            _topOutsideStop = new GradientStop(Colors.White, 0);
            _topEdgeStop = new GradientStop(Colors.White, 0);
            _topSolidStop = new GradientStop(Colors.White, 0.24);
            _bottomSolidStop = new GradientStop(Colors.White, 0.76);
            _bottomEdgeStop = new GradientStop(Colors.White, 1);
            _bottomOutsideStop = new GradientStop(Colors.White, 1);

            _mask = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1)
            };

            _mask.GradientStops.Add(_topOutsideStop);
            _mask.GradientStops.Add(_topEdgeStop);
            _mask.GradientStops.Add(_topSolidStop);
            _mask.GradientStops.Add(_bottomSolidStop);
            _mask.GradientStops.Add(_bottomEdgeStop);
            _mask.GradientStops.Add(_bottomOutsideStop);

            if (_host.IsLoaded)
                TryAttachViewer();
        }

        public void Detach()
        {
            _host.Loaded -= OnHostLoaded;
            _host.Unloaded -= OnHostUnloaded;
            _host.SizeChanged -= OnHostSizeChanged;
            DetachViewer();
        }

        public void Update()
        {
            var viewer = _viewer;
            if (viewer is null)
            {
                TryAttachViewer();
                return;
            }

            EnsureVerticalScrollBarLayerOrder();

            var fadeRampDistance = Math.Max(1, GetFadeRampDistance(_host));
            var maxFadeOpacity = Math.Clamp(GetMaxFadeOpacity(_host), 0, 1);
            var edgeFadeThickness = Math.Clamp(GetEdgeFadeThickness(_host), 0.01, 0.45);

            var targetHeight = Math.Max(1d, _maskTarget?.ActualHeight ?? viewer.ViewportHeight);
            var topInset = Math.Max(0d, GetTopInset(_host));
            var bottomInset = Math.Max(0d, GetBottomInset(_host));

            var topEdgeOffset = Math.Clamp(topInset / targetHeight, 0d, 0.9d);
            var bottomEdgeOffset = Math.Clamp(1d - (bottomInset / targetHeight), 0.1d, 1d);

            if (bottomEdgeOffset <= topEdgeOffset)
                bottomEdgeOffset = Math.Min(1d, topEdgeOffset + 0.01d);

            var availableRange = Math.Max(0.01d, bottomEdgeOffset - topEdgeOffset);
            var edgeFadeOffset = Math.Clamp(edgeFadeThickness * availableRange, 0.005d, (availableRange / 2d) - 0.0005d);

            _topOutsideStop.Offset = topEdgeOffset;
            _topEdgeStop.Offset = topEdgeOffset;
            _topSolidStop.Offset = topEdgeOffset + edgeFadeOffset;
            _bottomSolidStop.Offset = bottomEdgeOffset - edgeFadeOffset;
            _bottomEdgeStop.Offset = bottomEdgeOffset;
            _bottomOutsideStop.Offset = bottomEdgeOffset;

            if (viewer.ScrollableHeight <= 0.5 || maxFadeOpacity <= 0)
            {
                _topOutsideStop.Color = Colors.White;
                _topEdgeStop.Color = Colors.White;
                _topSolidStop.Color = Colors.White;
                _bottomSolidStop.Color = Colors.White;
                _bottomEdgeStop.Color = Colors.White;
                _bottomOutsideStop.Color = Colors.White;
                return;
            }

            var topFactor = Math.Clamp(viewer.VerticalOffset / fadeRampDistance, 0, 1);
            var remainingToBottom = viewer.ScrollableHeight - viewer.VerticalOffset;
            var bottomFactor = Math.Clamp(remainingToBottom / fadeRampDistance, 0, 1);

            var topTransparency = Math.Pow(topFactor, 0.55) * maxFadeOpacity;
            var bottomTransparency = Math.Pow(bottomFactor, 0.55) * maxFadeOpacity;
            var topAlpha = (byte)(Math.Clamp(1d - topTransparency, 0, 1) * 255d);
            var bottomAlpha = (byte)(Math.Clamp(1d - bottomTransparency, 0, 1) * 255d);

            _topOutsideStop.Color = Colors.White;
            _topEdgeStop.Color = Color.FromArgb(topAlpha, 255, 255, 255);
            _topSolidStop.Color = Colors.White;
            _bottomSolidStop.Color = Colors.White;
            _bottomEdgeStop.Color = Color.FromArgb(bottomAlpha, 255, 255, 255);
            _bottomOutsideStop.Color = Colors.White;
        }

        private void OnHostLoaded(object sender, RoutedEventArgs e)
        {
            TryAttachViewer();
        }

        private void OnHostUnloaded(object sender, RoutedEventArgs e)
        {
            DetachViewer();
        }

        private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_viewer is null)
                TryAttachViewer();
            else
                Update();
        }

        private void TryAttachViewer(bool allowScheduleRetry = true)
        {
            if (_host is ScrollViewer hostViewer)
            {
                AttachViewer(hostViewer);
                return;
            }

            var viewer = FindDescendant<ScrollViewer>(_host);
            if (viewer is not null)
            {
                AttachViewer(viewer);
                return;
            }

            if (_isRetryScheduled || !allowScheduleRetry)
                return;

            _isRetryScheduled = true;
            _ = _host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _isRetryScheduled = false;

                // Avoid an endless loaded-priority retry chain when a host has
                // no ScrollViewer in its visual tree.
                TryAttachViewer(allowScheduleRetry: false);
            }));
        }

        private void AttachViewer(ScrollViewer viewer)
        {
            if (ReferenceEquals(_viewer, viewer))
            {
                Update();
                return;
            }

            DetachViewer();

            _viewer = viewer;
            _viewer.ApplyTemplate();

            _maskTarget = ResolveMaskTarget(viewer);
            _maskTargetOriginalMask = _maskTarget.OpacityMask;
            _maskTarget.OpacityMask = _mask;

            EnsureVerticalScrollBarLayerOrder();

            _viewer.ScrollChanged += OnViewerScrollChanged;
            _viewer.SizeChanged += OnViewerSizeChanged;
            _maskTarget.SizeChanged += OnMaskTargetSizeChanged;

            Update();
        }

        private void DetachViewer()
        {
            if (_viewer is null)
                return;

            _viewer.ScrollChanged -= OnViewerScrollChanged;
            _viewer.SizeChanged -= OnViewerSizeChanged;

            if (_maskTarget is not null)
            {
                _maskTarget.SizeChanged -= OnMaskTargetSizeChanged;

                if (ReferenceEquals(_maskTarget.OpacityMask, _mask))
                    _maskTarget.OpacityMask = _maskTargetOriginalMask;
            }

            if (_verticalScrollBar is not null && _verticalScrollBarOriginalZIndex.HasValue && VisualTreeHelper.GetParent(_verticalScrollBar) is Panel)
                Panel.SetZIndex(_verticalScrollBar, _verticalScrollBarOriginalZIndex.Value);

            _maskTargetOriginalMask = null;
            _maskTarget = null;
            _verticalScrollBarOriginalZIndex = null;
            _verticalScrollBar = null;
            _viewer = null;
        }

        private void OnViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
                return;

            Update();
        }

        private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            Update();
        }

        private void OnMaskTargetSizeChanged(object sender, SizeChangedEventArgs e)
        {
            Update();
        }

        private void EnsureVerticalScrollBarLayerOrder()
        {
            var viewer = _viewer;
            if (viewer is null)
                return;

            if (_verticalScrollBar is null || !IsDescendantOf(viewer, _verticalScrollBar))
                _verticalScrollBar = ResolveVerticalScrollBar(viewer);

            if (_verticalScrollBar is null || VisualTreeHelper.GetParent(_verticalScrollBar) is not Panel)
                return;

            if (_verticalScrollBarOriginalZIndex is null)
                _verticalScrollBarOriginalZIndex = Panel.GetZIndex(_verticalScrollBar);

            Panel.SetZIndex(_verticalScrollBar, _verticalScrollBarOriginalZIndex.Value + 100);
        }

        private static FrameworkElement ResolveMaskTarget(ScrollViewer viewer)
        {
            if (viewer.Template?.FindName("PART_ScrollContentPresenter", viewer) is FrameworkElement templatePart)
                return templatePart;

            // When template part names differ, pick the largest non-scrollbar sibling
            // in the same panel as the vertical scrollbar (typically the viewport host).
            var verticalBar = ResolveVerticalScrollBar(viewer);
            if (verticalBar is not null && VisualTreeHelper.GetParent(verticalBar) is Panel parentPanel)
            {
                FrameworkElement? bestCandidate = null;
                var bestArea = 0d;

                var childCount = parentPanel.Children.Count;
                for (var i = 0; i < childCount; i++)
                {
                    if (parentPanel.Children[i] is not FrameworkElement child)
                        continue;

                    if (ReferenceEquals(child, verticalBar) || child is ScrollBar)
                        continue;

                    if (child.Visibility != Visibility.Visible)
                        continue;

                    var area = Math.Max(0d, child.ActualWidth) * Math.Max(0d, child.ActualHeight);
                    if (area <= bestArea)
                        continue;

                    bestArea = area;
                    bestCandidate = child;
                }

                if (bestCandidate is not null)
                    return bestCandidate;
            }

            FrameworkElement? presenter = FindDescendant<ScrollContentPresenter>(viewer);
            return presenter ?? viewer;
        }

        private static ScrollBar? ResolveVerticalScrollBar(ScrollViewer viewer)
        {
            if (viewer.Template?.FindName("PART_VerticalScrollBar", viewer) is ScrollBar templateBar)
                return templateBar;

            return FindDescendant<ScrollBar>(viewer, bar =>
                bar.Orientation == Orientation.Vertical && ReferenceEquals(bar.TemplatedParent, viewer))
                ?? FindDescendant<ScrollBar>(viewer, bar => bar.Orientation == Orientation.Vertical);
        }

        private static T? FindDescendant<T>(DependencyObject parent, Func<T, bool>? predicate = null)
            where T : DependencyObject
        {
            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild && (predicate is null || predicate(typedChild)))
                    return typedChild;

                var result = FindDescendant(child, predicate);
                if (result is not null)
                    return result;
            }

            return null;
        }

        private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject element)
        {
            DependencyObject? current = element;
            while (current is not null)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;

                current = GetParent(current);
            }

            return false;
        }

        private static DependencyObject? GetParent(DependencyObject element)
        {
            if (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
                return VisualTreeHelper.GetParent(element);

            return LogicalTreeHelper.GetParent(element);
        }
    }
}

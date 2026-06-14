using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AutoMidiPlayer.WPF.Controls.Snackbar;

/// <summary>
/// Hosts and stacks multiple <see cref="SnackbarItem"/> instances with hover-to-expand behaviour,
/// a count badge, and configurable placement within the content area.
/// </summary>
public partial class SnackbarContainer : UserControl
{
    // --- Constants for the collapsed-stack visual ---
    private const double StackYOffsetPerLevel = 8;       // px each older card peeks up
    private const double StackScalePerLevel = 0.04;      // scale reduction per level (0.96×, 0.92×, …)
    private const double StackOpacityPerLevel = 0.15;    // opacity reduction per level
    private const int MaxVisibleStacked = 4;             // max cards visible behind the newest
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(300);

    private readonly List<SnackbarItem> _items = new();
    private bool _isExpanded;

    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(
        nameof(Placement), typeof(SnackbarPlacement), typeof(SnackbarContainer),
        new PropertyMetadata(SnackbarPlacement.BottomRight, OnPlacementChanged));

    public static readonly DependencyProperty CountProperty = DependencyProperty.Register(
        nameof(Count), typeof(int), typeof(SnackbarContainer),
        new PropertyMetadata(0));

    public SnackbarPlacement Placement
    {
        get => (SnackbarPlacement)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    public int Count
    {
        get => (int)GetValue(CountProperty);
        private set => SetValue(CountProperty, value);
    }

    public Visibility BadgeVisibility => Count > 1 ? Visibility.Visible : Visibility.Collapsed;

    public SnackbarContainer()
    {
        InitializeComponent();
        DataContext = this;
        MouseEnter += (_, _) => ExpandStack();
        MouseLeave += (_, _) => CollapseStack();
        Loaded += (_, _) => ApplyPlacement();
        LayoutUpdated += (_, _) => UpdateBadgePosition();
    }

    // --- Public API ---

    /// <summary>Add a snackbar item, display it, and update the stack layout.</summary>
    public void Show(SnackbarItem item)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => Show(item)));
            return;
        }

        item.Closing += OnItemClosing;
        item.Closed += OnItemClosed;
        _items.Add(item);

        var isTop = Placement is SnackbarPlacement.TopLeft or SnackbarPlacement.TopCenter or SnackbarPlacement.TopRight;
        item.VerticalAlignment = isTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        item.HorizontalAlignment = HorizontalAlignment;

        ItemsHost.Children.Add(item);
        Count = _items.Count;
        NotifyBadgeChanged();
        UpdateBadgePosition();

        if (_isExpanded)
            ArrangeExpanded(animate: true);
        else
            ArrangeCollapsed(animate: true);
    }

    /// <summary>Manually dismiss a specific snackbar item.</summary>
    public void Dismiss(SnackbarItem item)
    {
        item.Close();
    }

    /// <summary>Dismiss all active snackbar items.</summary>
    public void DismissAll()
    {
        foreach (var item in _items.ToList())
            item.Close();
    }

    // --- Item lifecycle ---

    private void OnItemClosing(object? sender, EventArgs e)
    {
        if (sender is not SnackbarItem item)
            return;

        item.Closing -= OnItemClosing;
        _items.Remove(item);
        Count = _items.Count;
        NotifyBadgeChanged();
        UpdateBadgePosition();

        if (_isExpanded)
            ArrangeExpanded(animate: true);
        else
            ArrangeCollapsed(animate: true);
    }

    private void OnItemClosed(object? sender, EventArgs e)
    {
        if (sender is not SnackbarItem item)
            return;

        item.Closed -= OnItemClosed;
        ItemsHost.Children.Remove(item);
    }

    // --- Stack arrangement ---

    private void ExpandStack()
    {
        _isExpanded = true;
        ArrangeExpanded(animate: true);
        AnimateBadgeScale(expanded: true);
    }

    private void CollapseStack()
    {
        _isExpanded = false;
        ArrangeCollapsed(animate: true);
        AnimateBadgeScale(expanded: false);
    }

    private void ArrangeExpanded(bool animate)
    {
        if (_items.Count == 0)
            return;

        var placement = Placement;
        var isTop = placement is SnackbarPlacement.TopLeft or SnackbarPlacement.TopCenter or SnackbarPlacement.TopRight;

        double accumulatedMargin = 0;

        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            Panel.SetZIndex(item, i);

            // Ensure the item has been measured so we know its ActualHeight
            if (item.ActualHeight == 0)
                item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            
            var itemHeight = item.ActualHeight > 0 ? item.ActualHeight : item.DesiredSize.Height;

            var scale = (ScaleTransform)((TransformGroup)item.RenderTransform).Children[0];

            Thickness targetMargin = isTop
                ? new Thickness(0, accumulatedMargin, 0, 0)
                : new Thickness(0, 0, 0, accumulatedMargin);

            if (animate)
            {
                AnimateDouble(scale, ScaleTransform.ScaleXProperty, 1.0);
                AnimateDouble(scale, ScaleTransform.ScaleYProperty, 1.0);
                AnimateThickness(item, MarginProperty, targetMargin);
                AnimateDouble(item, OpacityProperty, 1.0);
            }
            else
            {
                scale.ScaleX = 1.0;
                scale.ScaleY = 1.0;
                item.Margin = targetMargin;
                item.Opacity = 1.0;
            }

            item.Visibility = Visibility.Visible;
            item.IsHitTestVisible = true;

            accumulatedMargin += itemHeight + 8; // 8px gap between items when expanded
        }
    }

    private void ArrangeCollapsed(bool animate)
    {
        if (_items.Count == 0)
            return;

        var placement = Placement;
        var isTop = placement is SnackbarPlacement.TopLeft or SnackbarPlacement.TopCenter or SnackbarPlacement.TopRight;
        var newestIndex = _items.Count - 1;

        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var levelsBehind = newestIndex - i; // 0 for newest, 1 for next, …

            Panel.SetZIndex(item, i);
            var scale = (ScaleTransform)((TransformGroup)item.RenderTransform).Children[0];

            if (levelsBehind > MaxVisibleStacked)
            {
                // Too deep — hide completely.
                item.Visibility = Visibility.Collapsed;
                item.IsHitTestVisible = false;
                continue;
            }

            item.Visibility = Visibility.Visible;
            item.IsHitTestVisible = levelsBehind == 0; // Only newest is interactive when collapsed.

            var targetScaleX = 1.0 - (StackScalePerLevel * levelsBehind);
            var targetScaleY = 1.0 - (StackScalePerLevel * levelsBehind);
            var targetOpacity = Math.Max(0, 1.0 - (StackOpacityPerLevel * levelsBehind));

            double marginVal = StackYOffsetPerLevel * levelsBehind;
            Thickness targetMargin = isTop
                ? new Thickness(0, marginVal, 0, 0)
                : new Thickness(0, 0, 0, marginVal);

            if (animate)
            {
                AnimateDouble(scale, ScaleTransform.ScaleXProperty, targetScaleX);
                AnimateDouble(scale, ScaleTransform.ScaleYProperty, targetScaleY);
                AnimateThickness(item, MarginProperty, targetMargin);
                AnimateDouble(item, OpacityProperty, targetOpacity);
            }
            else
            {
                scale.ScaleX = targetScaleX;
                scale.ScaleY = targetScaleY;
                item.Margin = targetMargin;
                item.Opacity = targetOpacity;
            }
        }
    }

    // --- Badge ---

    private void AnimateBadgeScale(bool expanded)
    {
        if (BadgeBorder == null) return;

        var scale = (ScaleTransform)BadgeBorder.RenderTransform;
        var targetScale = expanded ? 0.0 : 1.0;

        var animation = new DoubleAnimation(targetScale, AnimationDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void UpdateBadgePosition()
    {
        if (_items.Count == 0 || BadgeBorder == null || BadgeBorder.Visibility != Visibility.Visible) return;
        var newestItem = _items[_items.Count - 1];

        if (newestItem.ActualWidth == 0 || newestItem.ActualHeight == 0)
            return;

        try
        {
            var transform = newestItem.TransformToVisual(Root);
            var bounds = transform.TransformBounds(new Rect(0, 0, newestItem.ActualWidth, newestItem.ActualHeight));

            var placement = Placement;
            var isRight = placement is SnackbarPlacement.TopRight or SnackbarPlacement.BottomRight;
            var isLeft = placement is SnackbarPlacement.TopLeft or SnackbarPlacement.BottomLeft;

            var vertOffset = (bounds.Height / 2) - 9; 

            BadgeBorder.HorizontalAlignment = HorizontalAlignment.Left;
            BadgeBorder.VerticalAlignment = VerticalAlignment.Top;

            if (isRight)
            {
                BadgeBorder.Margin = new Thickness(bounds.Left - 9, bounds.Top + vertOffset, 0, 0);
            }
            else if (isLeft)
            {
                BadgeBorder.Margin = new Thickness(bounds.Right - 9, bounds.Top + vertOffset, 0, 0);
            }
            else 
            {
                BadgeBorder.Margin = new Thickness(bounds.Right - 9, bounds.Top + vertOffset, 0, 0);
            }
        }
        catch 
        {
            // Ignore during visual tree setup
        }
    }

    private void NotifyBadgeChanged()
    {
        // Force re-evaluation of BadgeVisibility CLR property.
        DataContext = null;
        DataContext = this;
    }

    // --- Placement ---

    private static void OnPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SnackbarContainer container)
            container.ApplyPlacement();
    }

    private void ApplyPlacement()
    {
        switch (Placement)
        {
            case SnackbarPlacement.TopLeft:
                HorizontalAlignment = HorizontalAlignment.Left;
                VerticalAlignment = VerticalAlignment.Top;
                break;
            case SnackbarPlacement.TopCenter:
                HorizontalAlignment = HorizontalAlignment.Center;
                VerticalAlignment = VerticalAlignment.Top;
                break;
            case SnackbarPlacement.TopRight:
                HorizontalAlignment = HorizontalAlignment.Right;
                VerticalAlignment = VerticalAlignment.Top;
                break;
            case SnackbarPlacement.BottomLeft:
                HorizontalAlignment = HorizontalAlignment.Left;
                VerticalAlignment = VerticalAlignment.Bottom;
                break;
            case SnackbarPlacement.BottomCenter:
                HorizontalAlignment = HorizontalAlignment.Center;
                VerticalAlignment = VerticalAlignment.Bottom;
                break;
            case SnackbarPlacement.BottomRight:
            default:
                HorizontalAlignment = HorizontalAlignment.Right;
                VerticalAlignment = VerticalAlignment.Bottom;
                break;
        }

        var isTop = Placement is SnackbarPlacement.TopLeft or SnackbarPlacement.TopCenter or SnackbarPlacement.TopRight;
        foreach (var item in _items)
        {
            item.VerticalAlignment = isTop ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            item.HorizontalAlignment = HorizontalAlignment;
        }

        UpdateBadgePosition();

        if (_items.Count > 0)
        {
            if (_isExpanded)
                ArrangeExpanded(animate: false);
            else
                ArrangeCollapsed(animate: false);
        }
    }

    // --- Animation helpers ---

    private static void AnimateDouble(IAnimatable target, DependencyProperty property, double to)
    {
        var animation = new DoubleAnimation(to, AnimationDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd
        };

        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateThickness(IAnimatable target, DependencyProperty property, Thickness to)
    {
        var animation = new ThicknessAnimation(to, AnimationDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd
        };

        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}

using System;
using System.Windows;
using System.Windows.Controls;

namespace AutoMidiPlayer.WPF.Controls;

public class SimpleStackPanel : StackPanel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing),
        typeof(double),
        typeof(SimpleStackPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var size = base.MeasureOverride(constraint);
        if (Spacing > 0)
        {
            var visibleCount = 0;

            foreach (UIElement child in InternalChildren)
            {
                if (child == null || child.Visibility == Visibility.Collapsed) continue;
                visibleCount++;
            }

            if (visibleCount > 1)
            {
                if (Orientation == Orientation.Horizontal)
                    size.Width += Spacing * (visibleCount - 1);
                else
                    size.Height += Spacing * (visibleCount - 1);
            }
        }

        return size;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var children = InternalChildren;
        double offset = 0;

        if (Orientation == Orientation.Horizontal)
        {
            foreach (UIElement child in children)
            {
                if (child == null) continue;
                if (child.Visibility == Visibility.Collapsed)
                {
                    child.Arrange(new Rect(0, 0, 0, 0));
                    continue;
                }
                var desired = child.DesiredSize;
                child.Arrange(new Rect(offset, 0, desired.Width, Math.Max(arrangeSize.Height, desired.Height)));
                offset += desired.Width + Spacing;
            }
        }
        else
        {
            foreach (UIElement child in children)
            {
                if (child == null) continue;
                if (child.Visibility == Visibility.Collapsed)
                {
                    child.Arrange(new Rect(0, 0, 0, 0));
                    continue;
                }
                var desired = child.DesiredSize;
                child.Arrange(new Rect(0, offset, Math.Max(arrangeSize.Width, desired.Width), desired.Height));
                offset += desired.Height + Spacing;
            }
        }

        return arrangeSize;
    }
}

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
        var count = InternalChildren.Count;

        if (count > 1 && Spacing > 0)
        {
            if (Orientation == Orientation.Horizontal)
                size.Width += Spacing * (count - 1);
            else
                size.Height += Spacing * (count - 1);
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
                var desired = child.DesiredSize;
                child.Arrange(new Rect(0, offset, Math.Max(arrangeSize.Width, desired.Width), desired.Height));
                offset += desired.Height + Spacing;
            }
        }

        return arrangeSize;
    }
}

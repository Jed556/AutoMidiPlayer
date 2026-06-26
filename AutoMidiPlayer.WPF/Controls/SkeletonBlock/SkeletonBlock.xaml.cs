using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AutoMidiPlayer.WPF.Controls;

public partial class SkeletonBlock : UserControl
{
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(SkeletonBlock),
            new PropertyMetadata(new CornerRadius(4.0)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public SkeletonBlock()
    {
        InitializeComponent();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild("ShimmerBorder") is Border shimmerBorder &&
            shimmerBorder.Background is LinearGradientBrush brush)
        {
            // The brush and its transform from XAML are frozen/sealed.
            // We must clone the brush to get a mutable copy we can animate.
            var mutableBrush = brush.Clone();
            shimmerBorder.Background = mutableBrush;

            if (mutableBrush.RelativeTransform is TranslateTransform transform)
            {
                var anim = new DoubleAnimation(-1.2, 1.2, new Duration(TimeSpan.FromSeconds(1.0)))
                {
                    RepeatBehavior = RepeatBehavior.Forever
                };
                transform.BeginAnimation(TranslateTransform.XProperty, anim);
            }
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AutoMidiPlayer.WPF.Controls;

public class IconButton : Button
{
    static IconButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(IconButton),
            new FrameworkPropertyMetadata(typeof(IconButton))
        );
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(IconButton),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(
            nameof(IsActive),
            typeof(bool),
            typeof(IconButton),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ActiveColorBrushProperty =
        DependencyProperty.Register(
            nameof(ActiveColorBrush),
            typeof(Brush),
            typeof(IconButton),
            new PropertyMetadata(null));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public Brush? ActiveColorBrush
    {
        get => (Brush?)GetValue(ActiveColorBrushProperty);
        set => SetValue(ActiveColorBrushProperty, value);
    }

    private static readonly IEasingFunction HoverInEasing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction HoverOutEasing = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

    public IconButton()
    {
        Unloaded += OnUnloaded;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);

        if (!IsEnabled || IconButtonProperties.GetIsGhost(this))
            return;

        var useAccent = IconButtonProperties.GetUseAccentColor(this);
        var secondary = useAccent 
            ? GetResourceColor("SystemAccentColorSecondary", "SystemAccentColorSecondaryBrush", Colors.DeepSkyBlue)
            : GetResourceColor("TextFillColorSecondary", "TextFillColorSecondaryBrush", Colors.LightGray);

        AnimateForeground(
            AdjustLightness(secondary, 0.08),
            TimeSpan.FromMilliseconds(140),
            HoverInEasing,
            clearForegroundWhenComplete: false);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (!IsEnabled || IconButtonProperties.GetIsGhost(this))
            return;

        var useAccent = IconButtonProperties.GetUseAccentColor(this);
        var color = useAccent 
            ? GetResourceColor("SystemAccentColorPrimary", "SystemAccentColorPrimaryBrush", Colors.DodgerBlue)
            : GetResourceColor("TextFillColorPrimary", "TextFillColorPrimaryBrush", Colors.White);

        AnimateForeground(
            color,
            TimeSpan.FromMilliseconds(220),
            HoverOutEasing,
            clearForegroundWhenComplete: true);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (!IsEnabled || IconButtonProperties.GetIsGhost(this))
            return;

        var useAccent = IconButtonProperties.GetUseAccentColor(this);
        var tertiary = useAccent 
            ? GetResourceColor("SystemAccentColorTertiary", "SystemAccentColorTertiaryBrush", Colors.DarkBlue)
            : GetResourceColor("TextFillColorTertiary", "TextFillColorTertiaryBrush", Colors.DarkGray);

        AnimateForeground(
            AdjustLightness(tertiary, 0.12),
            TimeSpan.FromMilliseconds(100),
            HoverInEasing,
            clearForegroundWhenComplete: false);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!IsEnabled || IconButtonProperties.GetIsGhost(this))
            return;

        var useAccent = IconButtonProperties.GetUseAccentColor(this);
            
        Color color;
        if (IsMouseOver)
        {
            var secondary = useAccent 
                ? GetResourceColor("SystemAccentColorSecondary", "SystemAccentColorSecondaryBrush", Colors.DeepSkyBlue)
                : GetResourceColor("TextFillColorSecondary", "TextFillColorSecondaryBrush", Colors.LightGray);
            color = AdjustLightness(secondary, 0.08);
        }
        else
        {
            color = useAccent
                ? GetResourceColor("SystemAccentColorPrimary", "SystemAccentColorPrimaryBrush", Colors.DodgerBlue)
                : GetResourceColor("TextFillColorPrimary", "TextFillColorPrimaryBrush", Colors.White);
        }

        AnimateForeground(
            color,
            TimeSpan.FromMilliseconds(140),
            HoverOutEasing,
            clearForegroundWhenComplete: !IsMouseOver);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (Foreground is SolidColorBrush brush)
            TryStopColorAnimation(brush);

        ClearValue(ForegroundProperty);
    }

    private void AnimateForeground(
        Color targetColor,
        TimeSpan duration,
        IEasingFunction easing,
        bool clearForegroundWhenComplete)
    {
        var sourceColor = Foreground is SolidColorBrush brush
            ? brush.Color
            : targetColor;

        var animatedBrush = new SolidColorBrush(sourceColor);
        Foreground = animatedBrush;

        var animation = new ColorAnimation(sourceColor, targetColor, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            TryStopColorAnimation(animatedBrush);

            if (clearForegroundWhenComplete && !IsMouseOver)
            {
                ClearValue(ForegroundProperty);
                return;
            }

            animatedBrush.Color = targetColor;
        };

        animatedBrush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private Color GetResourceColor(string colorResourceKey, string brushResourceKey, Color fallback)
    {
        if (TryFindResource(colorResourceKey) is Color color)
            return color;

        if (TryFindResource(brushResourceKey) is SolidColorBrush brush)
            return brush.Color;

        return fallback;
    }

    private Color AdjustLightness(Color color, double amount)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));

        double h = 0, s = 0, l = (max + min) / 2.0;

        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

            if (max == r) h = (g - b) / d + (g < b ? 6.0 : 0.0);
            else if (max == g) h = (b - r) / d + 2.0;
            else if (max == b) h = (r - g) / d + 4.0;

            h /= 6.0;
        }

        l = Math.Clamp(l + amount, 0.0, 1.0);

        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
            double p = 2.0 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3.0);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3.0);
        }

        return Color.FromArgb(color.A, (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private double HueToRgb(double p, double q, double t)
    {
        if (t < 0.0) t += 1.0;
        if (t > 1.0) t -= 1.0;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }

    private static void TryStopColorAnimation(SolidColorBrush brush)
    {
        try
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        }
        catch (InvalidOperationException)
        {
            // Dynamic resource brushes may be sealed/frozen during theme transitions.
        }
    }
}

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AutoMidiPlayer.Data;

namespace AutoMidiPlayer.WPF.Controls;

public class Hyperlink : System.Windows.Controls.Button
{
    private const double TooltipFontSize = 10d;
    private const double TooltipMaxWidth = 380d;

    public static readonly DependencyProperty NavigateUriProperty = DependencyProperty.Register(
        nameof(NavigateUri),
        typeof(Uri),
        typeof(Hyperlink),
        new PropertyMetadata(null, OnPreviewToolTipPropertyChanged));

    public static readonly DependencyProperty IsPreviewToolTipEnabledProperty = DependencyProperty.Register(
        nameof(IsPreviewToolTipEnabled),
        typeof(bool),
        typeof(Hyperlink),
        new PropertyMetadata(true, OnPreviewToolTipPropertyChanged));

    public static readonly DependencyProperty PreviewToolTipProperty = DependencyProperty.Register(
        nameof(PreviewToolTip),
        typeof(object),
        typeof(Hyperlink),
        new PropertyMetadata(null, OnPreviewToolTipPropertyChanged));

    public static readonly DependencyProperty NormalOpacityProperty = DependencyProperty.Register(
        nameof(NormalOpacity),
        typeof(double),
        typeof(Hyperlink),
        new PropertyMetadata(1d, OnOpacityStatePropertyChanged));

    public static readonly DependencyProperty PointerOverOpacityProperty = DependencyProperty.Register(
        nameof(PointerOverOpacity),
        typeof(double),
        typeof(Hyperlink),
        new PropertyMetadata(1d, OnOpacityStatePropertyChanged));

    private static readonly IEasingFunction HoverInEasing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction HoverOutEasing = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
    private bool _ownsPreviewToolTip;

    public Hyperlink()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public Uri? NavigateUri
    {
        get => (Uri?)GetValue(NavigateUriProperty);
        set => SetValue(NavigateUriProperty, value);
    }

    public bool IsPreviewToolTipEnabled
    {
        get => (bool)GetValue(IsPreviewToolTipEnabledProperty);
        set => SetValue(IsPreviewToolTipEnabledProperty, value);
    }

    public object? PreviewToolTip
    {
        get => GetValue(PreviewToolTipProperty);
        set => SetValue(PreviewToolTipProperty, value);
    }

    public double NormalOpacity
    {
        get => (double)GetValue(NormalOpacityProperty);
        set => SetValue(NormalOpacityProperty, value);
    }

    public double PointerOverOpacity
    {
        get => (double)GetValue(PointerOverOpacityProperty);
        set => SetValue(PointerOverOpacityProperty, value);
    }

    protected override void OnClick()
    {
        base.OnClick();

        if (NavigateUri is null)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(NavigateUri.ToString())
            {
                UseShellExecute = true
            });
        }
        catch (Exception error)
        {
            Logger.LogException(error);
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);

        if (!IsEnabled)
            return;

        var secondary = GetResourceColor("SystemAccentColorSecondary", "SystemAccentColorSecondaryBrush", Colors.DeepSkyBlue);

        AnimateOpacity(GetClampedOpacity(PointerOverOpacity), TimeSpan.FromMilliseconds(140), HoverInEasing);
        AnimateForeground(
            AdjustLightness(secondary, 0.08), // Make it slightly lighter without losing saturation
            TimeSpan.FromMilliseconds(140),
            HoverInEasing,
            clearForegroundWhenComplete: false);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);

        if (!IsEnabled)
            return;

        AnimateOpacity(GetClampedOpacity(NormalOpacity), TimeSpan.FromMilliseconds(220), HoverOutEasing);
        AnimateForeground(
            GetResourceColor("SystemAccentColorPrimary", "SystemAccentColorPrimaryBrush", Colors.DodgerBlue),
            TimeSpan.FromMilliseconds(220),
            HoverOutEasing,
            clearForegroundWhenComplete: true);
    }

    private static void OnPreviewToolTipPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Hyperlink hyperlink)
            hyperlink.UpdatePreviewToolTip();
    }

    private static void OnOpacityStatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Hyperlink hyperlink && !hyperlink.IsMouseOver)
            hyperlink.SetCurrentValue(OpacityProperty, GetClampedOpacity(hyperlink.NormalOpacity));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetCurrentValue(OpacityProperty, GetClampedOpacity(IsMouseOver ? PointerOverOpacity : NormalOpacity));
        UpdatePreviewToolTip();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        BeginAnimation(OpacityProperty, null);

        if (Foreground is SolidColorBrush brush)
            TryStopColorAnimation(brush);

        ClearValue(ForegroundProperty);
    }

    private void UpdatePreviewToolTip()
    {
        var resolvedToolTip = GetResolvedPreviewToolTip();
        if (resolvedToolTip is null)
        {
            if (_ownsPreviewToolTip)
            {
                ToolTip = null;
                _ownsPreviewToolTip = false;
            }

            return;
        }

        if (!_ownsPreviewToolTip && ToolTip is not null && PreviewToolTip is null)
            return;

        ToolTip = CreateTooltipContent(resolvedToolTip);
        _ownsPreviewToolTip = true;
    }

    private static object? CreateTooltipContent(object? content)
    {
        if (content is not string text)
            return content;

        return new TextBlock
        {
            Text = text,
            FontSize = TooltipFontSize,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = TooltipMaxWidth
        };
    }

    private object? GetResolvedPreviewToolTip()
    {
        if (!IsPreviewToolTipEnabled)
            return null;

        if (PreviewToolTip is not null)
            return PreviewToolTip;

        return NavigateUri is not null
            ? $"Open {NavigateUri}"
            : null;
    }

    private void AnimateOpacity(double targetOpacity, TimeSpan duration, IEasingFunction easing)
    {
        var animation = new DoubleAnimation
        {
            To = targetOpacity,
            Duration = duration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = targetOpacity;
        };

        BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
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

    private static double GetClampedOpacity(double opacity)
        => Math.Clamp(opacity, 0d, 1d);

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

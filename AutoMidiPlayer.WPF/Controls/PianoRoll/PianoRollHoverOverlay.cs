using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AutoMidiPlayer.WPF.Controls.PianoRoll;

/// <summary>
/// A lightweight, independent overlay layer that renders only the hover guide line and timestamp pill.
/// By keeping this in a separate visual element, hover updates never trigger redraws of PianoRollCanvas
/// or its thousands of notes, ensuring 100% fluid, zero-overhead mouse interactions.
/// </summary>
public class PianoRollHoverOverlay : FrameworkElement
{
    private static readonly Typeface HoverTypeface;

    static PianoRollHoverOverlay()
    {
        HoverTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private double? _hoverX;
    private long _hoverMs;
    private double _keyboardWidth = 64;
    private long _totalDurationMs;
    private double _cachedDpi = -1;

    private SolidColorBrush _hoverPillBrush = null!;
    private Pen _hoverPillPen = null!;
    private SolidColorBrush _hoverTextBrush = null!;
    private Pen _hoverLinePen = null!;
    private bool _lastIsDark;
    private bool _themeInitialized;

    private readonly Dictionary<int, (FormattedText Text, double Width, double Height)> _hoverLabelCache = new(256);
    private double _hoverLabelCacheDpi = -1;

    public PianoRollHoverOverlay()
    {
        EnsureThemeResources();
        Loaded += (_, _) =>
        {
            AutoMidiPlayer.WPF.Services.SystemThemeService.ThemeResourcesChanged += OnThemeResourcesChanged;
            EnsureThemeResources();
            InvalidateVisual();
        };
        Unloaded += (_, _) =>
        {
            AutoMidiPlayer.WPF.Services.SystemThemeService.ThemeResourcesChanged -= OnThemeResourcesChanged;
        };
    }

    private void OnThemeResourcesChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            EnsureThemeResources();
            InvalidateVisual();
        });
    }

    private void EnsureThemeResources()
    {
        var theme = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme();
        var isDark = theme switch
        {
            Wpf.Ui.Appearance.ApplicationTheme.Dark => true,
            Wpf.Ui.Appearance.ApplicationTheme.Light => false,
            _ => AutoMidiPlayer.WPF.Services.SystemThemeService.GetSystemTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark
        };

        if (_themeInitialized && isDark == _lastIsDark)
            return;

        _lastIsDark = isDark;
        _themeInitialized = true;

        if (isDark)
        {
            _hoverLinePen = new Pen(CreateFrozenBrush(Color.FromArgb(120, 255, 255, 255)), 1.2);
            _hoverLinePen.Freeze();

            _hoverPillBrush = CreateFrozenBrush(Color.FromArgb(220, 20, 20, 24));
            _hoverPillPen = new Pen(CreateFrozenBrush(Color.FromArgb(80, 255, 255, 255)), 1);
            _hoverPillPen.Freeze();
            _hoverTextBrush = CreateFrozenBrush(Color.FromArgb(235, 255, 255, 255));
        }
        else
        {
            _hoverLinePen = new Pen(CreateFrozenBrush(Color.FromArgb(120, 0, 0, 0)), 1.2);
            _hoverLinePen.Freeze();

            _hoverPillBrush = CreateFrozenBrush(Color.FromArgb(230, 250, 250, 252));
            _hoverPillPen = new Pen(CreateFrozenBrush(Color.FromArgb(80, 0, 0, 0)), 1);
            _hoverPillPen.Freeze();
            _hoverTextBrush = CreateFrozenBrush(Color.FromArgb(235, 20, 20, 24));
        }

        _hoverLabelCache.Clear();
    }

    public void SetHover(double? hoverX, long hoverMs, double keyboardWidth, long totalDurationMs)
    {
        if (_hoverX == hoverX && _hoverMs == hoverMs && Math.Abs(_keyboardWidth - keyboardWidth) < 0.1 && _totalDurationMs == totalDurationMs)
            return;

        _hoverX = hoverX;
        _hoverMs = hoverMs;
        _keyboardWidth = keyboardWidth;
        _totalDurationMs = totalDurationMs;

        InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _cachedDpi = newDpi.PixelsPerDip;
        _hoverLabelCache.Clear();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= _keyboardWidth + 10 || height <= 20)
            return;

        if (!_hoverX.HasValue || _hoverX.Value < _keyboardWidth || _hoverX.Value > width)
            return;

        EnsureThemeResources();

        var hx = Math.Round(_hoverX.Value);
        dc.DrawLine(_hoverLinePen, new Point(hx, 0), new Point(hx, height));

        var hoverMs = _hoverMs;
        if (_totalDurationMs > 0) hoverMs = Math.Clamp(hoverMs, 0, _totalDurationMs);
        else hoverMs = Math.Max(0, hoverMs);

        if (_cachedDpi <= 0)
        {
            _cachedDpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        }

        var cachedHover = GetOrCreateHoverLabel(hoverMs, _cachedDpi);

        var pillW = Math.Round(cachedHover.Width + 8);
        var pillH = Math.Round(cachedHover.Height + 2);
        var pillX = Math.Round(Math.Clamp(hx - (pillW / 2), _keyboardWidth + 2, width - pillW - 2));
        var pillY = Math.Round(height - pillH - 3);
        var pillRect = new Rect(pillX, pillY, pillW, pillH);

        dc.DrawRoundedRectangle(_hoverPillBrush, _hoverPillPen, pillRect, 3, 3);
        dc.DrawText(cachedHover.Text, new Point(pillX + 4, pillY + 1));
    }

    private (FormattedText Text, double Width, double Height) GetOrCreateHoverLabel(long hoverMs, double dpi)
    {
        if (Math.Abs(_hoverLabelCacheDpi - dpi) > 0.001)
        {
            _hoverLabelCache.Clear();
            _hoverLabelCacheDpi = dpi;
        }

        var totalSeconds = (int)Math.Max(0, hoverMs / 1000L);
        if (_hoverLabelCache.TryGetValue(totalSeconds, out var cached))
        {
            return cached;
        }

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        var hours = minutes / 60;
        minutes %= 60;

        string timeText = hours > 0
            ? $"{hours}:{minutes:D2}:{seconds:D2}"
            : $"{minutes}:{seconds:D2}";

        var formatted = new FormattedText(
            timeText,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            HoverTypeface,
            9.5,
            _hoverTextBrush,
            dpi);

        var item = (formatted, formatted.Width, formatted.Height);

        if (_hoverLabelCache.Count >= 1000)
        {
            _hoverLabelCache.Clear();
        }

        _hoverLabelCache[totalSeconds] = item;
        return item;
    }
}

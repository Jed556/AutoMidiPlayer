using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Properties;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Appearance;

namespace AutoMidiPlayer.WPF.Services;

public static class SystemThemeService
{
    private static CancellationTokenSource? _cts;
    private static int _lastObserved = -999;
    private static bool _isThemeAnimationRunning;
    private const int PollDelayMs = 1000;

    public static event Action? ThemeResourcesChanged;

    // Easy-to-tune theme animation controls.
    public static bool ThemeAnimationEnabled { get; set; } = true;
    public static int ThemeAnimationDurationMs { get; set; } = 420;
    public static double ThemeAnimationTargetOpacity { get; set; } = 0.55;

    private static readonly IEasingFunction ThemeFadeOutEasing = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
    private static readonly IEasingFunction ThemeFadeInEasing = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

    public static void Start()
    {
        if (_cts != null)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _lastObserved = ReadAppsUseLightTheme();

        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(PollDelayMs, token);
                    if (token.IsCancellationRequested)
                        break;

                    var current = ReadAppsUseLightTheme();
                    if (current != _lastObserved)
                    {
                        _lastObserved = current;
                        if (Settings.Default.AppTheme == -1)
                        {
                            var theme = current == 0 ? ApplicationTheme.Dark : ApplicationTheme.Light;
                            try
                            {
                                Application.Current?.Dispatcher?.BeginInvoke(new Action(() => ApplySystemThemeNow(theme)));
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }, token);
    }

    public static void Stop()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        catch { }
    }

    public static ApplicationTheme GetSystemTheme()
    {
        try
        {
            var v = ReadAppsUseLightTheme();
            if (v == 0)
                return ApplicationTheme.Dark;
            if (v == 1)
                return ApplicationTheme.Light;
        }
        catch { }

        return ApplicationThemeManager.GetAppTheme();
    }

    public static void ApplySystemThemeNow(ApplicationTheme? themeOverride = null)
    {
        var theme = themeOverride ?? GetSystemTheme();

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => ApplySystemThemeNow(theme)));
            return;
        }

        var window = Application.Current?.MainWindow;
        var animationTarget = (window?.Content as UIElement) ?? window;
        var durationMs = Math.Max(0, ThemeAnimationDurationMs);
        var shouldAnimate = ThemeAnimationEnabled
                            && durationMs > 0
                            && animationTarget is not null
                            && window is not null
                            && window.IsLoaded
                            && !_isThemeAnimationRunning;

        if (!shouldAnimate)
        {
            ApplyThemeAndAccent(theme);
            return;
        }

        _isThemeAnimationRunning = true;
        var target = animationTarget!;
        var originalOpacity = target.Opacity;
        var originalCacheMode = target.CacheMode;
        target.CacheMode = new BitmapCache();

        var clampedTargetOpacity = Math.Clamp(ThemeAnimationTargetOpacity, 0.15, 0.95);

        var fadeOut = new DoubleAnimation(originalOpacity, clampedTargetOpacity, TimeSpan.FromMilliseconds(durationMs * 0.85))
        {
            EasingFunction = ThemeFadeOutEasing
        };

        fadeOut.Completed += (_, _) =>
        {
            ApplyThemeAndAccent(theme);

            var fadeIn = new DoubleAnimation(clampedTargetOpacity, originalOpacity, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = ThemeFadeInEasing
            };

            fadeIn.Completed += (_, _) =>
            {
                target.Opacity = originalOpacity;
                target.CacheMode = originalCacheMode;
                _isThemeAnimationRunning = false;
            };

            target.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        target.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    public static void ApplyAccentColorNow(string hexColor, bool scheduleDeferredRefresh = true)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            ApplyAccentColorNow(color, scheduleDeferredRefresh);
        }
        catch
        {
            var fallbackColor = (Color)ColorConverter.ConvertFromString("#1DB954");
            ApplyAccentColorNow(fallbackColor, scheduleDeferredRefresh);
        }
    }

    public static void ApplyAccentColorNow(Color color, bool scheduleDeferredRefresh = true)
    {
        ApplyAccentColorResources(color);

        var currentTheme = ApplicationThemeManager.GetAppTheme();
        ApplicationAccentColorManager.Apply(color, currentTheme, true);
        ApplyAccentColorResources(color);

        if (scheduleDeferredRefresh)
        {
            Application.Current?.Dispatcher?.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() =>
                {
                    ApplyAccentColorResources(color);
                    ApplicationThemeManager.Apply(currentTheme, Wpf.Ui.Controls.WindowBackdropType.Mica, false);
                    ApplicationAccentColorManager.Apply(color, currentTheme, true);
                    ApplyAccentColorResources(color);
                    ThemeResourcesChanged?.Invoke();
                }));
            return;
        }

        ThemeResourcesChanged?.Invoke();
    }

    private static void ApplyThemeAndAccent(ApplicationTheme theme)
    {
        ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.Mica, false);

        try
        {
            var accentHex = Settings.Default.AccentColor ?? "#1DB954";
            var color = (Color)ColorConverter.ConvertFromString(accentHex);
            ApplicationAccentColorManager.Apply(color, theme, true);
            ApplyAccentColorResources(color);
        }
        catch { }

        ThemeResourcesChanged?.Invoke();
    }

    private static void ApplyAccentColorResources(Color color)
    {
        var accentBrush = new SolidColorBrush(color);
        accentBrush.Freeze();

        var secondary = AdjustColorBrightness(color, 0.15f);
        var tertiary = AdjustColorBrightness(color, 0.30f);

        var secondaryBrush = new SolidColorBrush(secondary);
        var tertiaryBrush = new SolidColorBrush(tertiary);
        secondaryBrush.Freeze();
        tertiaryBrush.Freeze();

        SetOrUpdateResource("SystemAccentColor", color);
        SetOrUpdateResource("SystemAccentColorBrush", accentBrush);
        SetOrUpdateResource("SystemAccentColorPrimary", color);
        SetOrUpdateResource("SystemAccentColorPrimaryBrush", accentBrush);
        SetOrUpdateResource("SystemAccentColorSecondary", secondary);
        SetOrUpdateResource("SystemAccentColorSecondaryBrush", secondaryBrush);
        SetOrUpdateResource("SystemAccentColorTertiary", tertiary);
        SetOrUpdateResource("SystemAccentColorTertiaryBrush", tertiaryBrush);
    }

    private static void SetOrUpdateResource(string key, object value)
    {
        var resources = Application.Current?.Resources;
        if (resources is null)
            return;

        if (resources.Contains(key))
            resources[key] = value;
        else
            resources.Add(key, value);
    }

    private static int ReadAppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", false);
            if (key is null)
                return -1;

            var v = key.GetValue("AppsUseLightTheme");
            if (v is int i)
                return i;
            if (v is string s && int.TryParse(s, out var parsed))
                return parsed;
        }
        catch { }

        return -1;
    }

    private static Color AdjustColorBrightness(Color color, float factor)
    {
        float r = color.R / 255f;
        float g = color.G / 255f;
        float b = color.B / 255f;

        if (factor > 0)
        {
            r = r + (1 - r) * factor;
            g = g + (1 - g) * factor;
            b = b + (1 - b) * factor;
        }
        else
        {
            r = r * (1 + factor);
            g = g * (1 + factor);
            b = b * (1 + factor);
        }

        return Color.FromArgb(
            color.A,
            (byte)Math.Clamp(r * 255, 0, 255),
            (byte)Math.Clamp(g * 255, 0, 255),
            (byte)Math.Clamp(b * 255, 0, 255));
    }
}

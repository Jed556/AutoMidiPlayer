using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Linq;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AutoMidiPlayer.WPF.MessageBox;

/// <summary>
/// A themed crash/error message box with an error icon, clickable log path,
/// and a readonly error text box with a hover copy button.
/// </summary>
public partial class CrashMessageBox : Wpf.Ui.Controls.MessageBox
{
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int DwmWindowCornerPreferenceRound = 2;
    private const int WindowLongStyle = -16;
    private const int WindowMessageNonClientHitTest = 0x0084;
    private const int WindowMessageSystemCommand = 0x0112;
    private const int HitTestClient = 1;
    private const int HitTestLeft = 10;
    private const int HitTestRight = 11;
    private const int HitTestTop = 12;
    private const int HitTestTopLeft = 13;
    private const int HitTestTopRight = 14;
    private const int HitTestBottom = 15;
    private const int HitTestBottomLeft = 16;
    private const int HitTestBottomRight = 17;
    private const long SystemCommandSize = 0xF000L;
    private const long SystemCommandMaximize = 0xF030L;
    private const long SystemCommandMask = 0xFFF0L;
    private const long WindowStyleMinimizeBox = 0x00020000L;
    private const long WindowStyleMaximizeBox = 0x00010000L;
    private const long WindowStyleThickFrame = 0x00040000L;
    private const uint SetWindowPositionNoSize = 0x0001;
    private const uint SetWindowPositionNoMove = 0x0002;
    private const uint SetWindowPositionNoZOrder = 0x0004;
    private const uint SetWindowPositionFrameChanged = 0x0020;

    private const double MaxDialogScreenRatio = 0.5;
    private const double DialogChromeReserve = 140;
    private const double MinimumScrollableBodyHeight = 240;

    private readonly string _logPath;
    private HwndSource? _hwndSource;

    static CrashMessageBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CrashMessageBox),
            new FrameworkPropertyMetadata(typeof(Wpf.Ui.Controls.MessageBox))
        );
    }

    public CrashMessageBox(Exception exception, string logPath)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logPath);

        _logPath = logPath;

        InitializeComponent();

        if (Application.Current.TryFindResource(typeof(Wpf.Ui.Controls.MessageBox)) is Style messageBoxStyle)
            Style = messageBoxStyle;

        ApplyHeightCap();

        TitleTextBlock.Text = $"{GetProductName()} Error";
        VersionTextBlock.Text = $"Version {GetAppVersion()}";
        ErrorTextBox.Text = exception.Message;
        LogPathRun.Text = logPath;

        if (Application.Current.TryFindResource("AppHyperlinkStyle") is Style hyperlinkStyle)
            LogPathHyperlink.Style = hyperlinkStyle;

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        Loaded += OnLoaded;
    }

    public static void Show(Exception exception, string logPath)
    {
        var messageBox = new CrashMessageBox(exception, logPath);

        var owner = ResolveOwnerWindow();
        if (owner != null && owner != messageBox)
        {
            messageBox.Owner = owner;
            messageBox.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        _ = messageBox.ShowDialogAsync(showAsDialog: true).GetAwaiter().GetResult();
    }

    private void ApplyHeightCap()
    {
        var maxDialogHeight = SystemParameters.WorkArea.Height * MaxDialogScreenRatio;
        MaxHeight = maxDialogHeight;
        BodyScrollViewer.MaxHeight = Math.Max(MinimumScrollableBodyHeight, maxDialogHeight - DialogChromeReserve);
    }

    private static string GetProductName()
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyProductAttribute>()?
            .Product ?? "Auto MIDI Player";
    }

    private static string GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null)
            return "unknown";

        return version.Build >= 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : $"{version.Major}.{version.Minor}";
    }

    private static Window? ResolveOwnerWindow()
    {
        var app = Application.Current;
        if (app == null)
            return null;

        var windows = app.Windows.OfType<Window>().ToList();
        return windows.FirstOrDefault(window => window.IsActive)
               ?? app.MainWindow
               ?? windows.FirstOrDefault(window => window.IsVisible)
               ?? windows.FirstOrDefault();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyFixedWindowStyle();
        ApplyWindowCornerPreference();
        RemoveTemplateTitleBarChrome(this);
        Dispatcher.BeginInvoke(
            () =>
            {
                LockRenderedWindowSize();
                ApplyFixedWindowStyle();
                ApplyWindowCornerPreference();
                RemoveTemplateTitleBarChrome(this);
            },
            DispatcherPriority.Render);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);
        }

        ApplyFixedWindowStyle();
        ApplyWindowCornerPreference();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
    }

    private void LockRenderedWindowSize()
    {
        UpdateLayout();

        var lockedWidth = Width;
        var lockedHeight = Math.Min(ActualHeight, SystemParameters.WorkArea.Height * MaxDialogScreenRatio);

        if (double.IsNaN(lockedHeight) || lockedHeight <= 0)
            return;

        SizeToContent = SizeToContent.Manual;
        Width = lockedWidth;
        MinWidth = lockedWidth;
        MaxWidth = lockedWidth;
        Height = lockedHeight;
        MinHeight = lockedHeight;
        MaxHeight = lockedHeight;
    }

    private void ApplyFixedWindowStyle()
    {
        ResizeMode = ResizeMode.NoResize;
        MinWidth = Width;
        MaxWidth = Width;
        MinHeight = Height > 0 ? Height : MinHeight;
        MaxHeight = Height > 0 ? Height : MaxHeight;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var style = GetWindowLongPtr(handle, WindowLongStyle).ToInt64();
        style &= ~(WindowStyleThickFrame | WindowStyleMaximizeBox | WindowStyleMinimizeBox);

        _ = SetWindowLongPtr(handle, WindowLongStyle, new IntPtr(style));
        _ = SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SetWindowPositionNoMove
            | SetWindowPositionNoSize
            | SetWindowPositionNoZOrder
            | SetWindowPositionFrameChanged);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WindowMessageNonClientHitTest:
                {
                    var hitTest = DefWindowProc(hwnd, msg, wParam, lParam).ToInt32();
                    if (!IsResizeHitTest(hitTest))
                        return IntPtr.Zero;

                    handled = true;
                    return new IntPtr(HitTestClient);
                }

            case WindowMessageSystemCommand:
                {
                    var command = wParam.ToInt64() & SystemCommandMask;
                    if (command != SystemCommandSize && command != SystemCommandMaximize)
                        return IntPtr.Zero;

                    handled = true;
                    return IntPtr.Zero;
                }
        }

        return IntPtr.Zero;
    }

    private static bool IsResizeHitTest(int hitTest)
    {
        return hitTest is
            HitTestLeft or
            HitTestRight or
            HitTestTop or
            HitTestTopLeft or
            HitTestTopRight or
            HitTestBottom or
            HitTestBottomLeft or
            HitTestBottomRight;
    }

    private void ApplyWindowCornerPreference()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        var preference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(
            handle,
            DwmWindowCornerPreferenceAttribute,
            ref preference,
            Marshal.SizeOf<int>());
    }

    private void OnDialogPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed || IsInteractiveDragSource(e.OriginalSource as DependencyObject))
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove can throw if the mouse button state changes during event routing.
        }
    }

    private static void RemoveTemplateTitleBarChrome(DependencyObject root)
    {
        foreach (var element in EnumerateVisualDescendants(root))
        {
            if (element is Wpf.Ui.Controls.TitleBar titleBar)
            {
                titleBar.Visibility = Visibility.Collapsed;
                titleBar.Height = 0;
                titleBar.MinHeight = 0;
                titleBar.Margin = new Thickness(0);
                continue;
            }

            if (element is FrameworkElement { Name: "PART_CloseButton" } closeButton)
                closeButton.Visibility = Visibility.Collapsed;
        }
    }

    private static IEnumerable<DependencyObject> EnumerateVisualDescendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;

            foreach (var descendant in EnumerateVisualDescendants(child))
                yield return descendant;
        }
    }

    private static bool IsInteractiveDragSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase or ScrollBar or Thumb or TextBoxBase or Hyperlink)
                return true;

            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnLogPathClick(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_logPath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_logPath}\"")
            {
                UseShellExecute = true
            });
        }
        else
        {
            var folder = Path.GetDirectoryName(_logPath) ?? _logPath;
            Process.Start(new ProcessStartInfo("explorer.exe", folder)
            {
                UseShellExecute = true
            });
        }

        e.Handled = true;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoMidiPlayer.WPF.MessageBox;

/// <summary>
/// A customizable themed message box with native window-chrome overrides, supporting custom body content.
/// Mirrors the CrashMessageBox visual treatment (no X button, no resize, drag-anywhere) for consistency.
/// </summary>
public partial class StandardMessageBox : Wpf.Ui.Controls.MessageBox
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

    private HwndSource? _hwndSource;

    public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(
        nameof(IconSource), typeof(ImageSource), typeof(StandardMessageBox),
        new PropertyMetadata(null, OnVisibilityRelatedPropertyChanged));

    public static readonly DependencyProperty IconSymbolProperty = DependencyProperty.Register(
        nameof(IconSymbol), typeof(Wpf.Ui.Controls.SymbolRegular), typeof(StandardMessageBox),
        new PropertyMetadata(Wpf.Ui.Controls.SymbolRegular.Info24));

    public static readonly DependencyProperty DialogTitleProperty = DependencyProperty.Register(
        nameof(DialogTitle), typeof(string), typeof(StandardMessageBox),
        new PropertyMetadata(string.Empty, OnVisibilityRelatedPropertyChanged));

    public static readonly DependencyProperty DialogSubtitleProperty = DependencyProperty.Register(
        nameof(DialogSubtitle), typeof(string), typeof(StandardMessageBox),
        new PropertyMetadata(string.Empty, OnVisibilityRelatedPropertyChanged));

    public static readonly DependencyProperty BodyContentProperty = DependencyProperty.Register(
        nameof(BodyContent), typeof(object), typeof(StandardMessageBox), new PropertyMetadata(null));

    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public Wpf.Ui.Controls.SymbolRegular IconSymbol
    {
        get => (Wpf.Ui.Controls.SymbolRegular)GetValue(IconSymbolProperty);
        set => SetValue(IconSymbolProperty, value);
    }

    public string DialogTitle
    {
        get => (string)GetValue(DialogTitleProperty);
        set => SetValue(DialogTitleProperty, value);
    }

    public string DialogSubtitle
    {
        get => (string)GetValue(DialogSubtitleProperty);
        set => SetValue(DialogSubtitleProperty, value);
    }

    public object? BodyContent
    {
        get => GetValue(BodyContentProperty);
        set => SetValue(BodyContentProperty, value);
    }

    public Visibility IconSourceVisibility => IconSource != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IconSymbolVisibility => IconSource == null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DialogTitleVisibility => string.IsNullOrEmpty(DialogTitle) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DialogSubtitleVisibility => string.IsNullOrEmpty(DialogSubtitle) ? Visibility.Collapsed : Visibility.Visible;

    static StandardMessageBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(StandardMessageBox),
            new FrameworkPropertyMetadata(typeof(Wpf.Ui.Controls.MessageBox))
        );
    }

    public StandardMessageBox()
    {
        InitializeComponent();

        if (Application.Current.TryFindResource(typeof(Wpf.Ui.Controls.MessageBox)) is Style messageBoxStyle)
            Style = messageBoxStyle;

        ApplyHeightCap();

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        Loaded += OnLoaded;
    }

    private static void OnVisibilityRelatedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not StandardMessageBox box)
            return;

        // Refresh the XAML-bound visibility properties by notifying the UI of their new values.
        box.IconSymbolElement.Visibility = box.IconSymbolVisibility;
        box.IconImageElement.Visibility = box.IconSourceVisibility;
        box.TitleTextBlock.Visibility = box.DialogTitleVisibility;
        box.SubtitleTextBlock.Visibility = box.DialogSubtitleVisibility;
    }

    private void ApplyHeightCap()
    {
        var maxDialogHeight = SystemParameters.WorkArea.Height * MaxDialogScreenRatio;
        MaxHeight = maxDialogHeight;
        BodyScrollViewer.MaxHeight = Math.Max(MinimumScrollableBodyHeight, maxDialogHeight - DialogChromeReserve);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyFixedWindowStyle();
        ApplyWindowCornerPreference();
        RemoveTemplateTitleBarChrome(this);

        Dispatcher.BeginInvoke(
            () =>
            {
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
            if (source is System.Windows.Controls.Primitives.ButtonBase or
                System.Windows.Controls.Primitives.ScrollBar or
                System.Windows.Controls.Primitives.Thumb or
                System.Windows.Controls.Primitives.TextBoxBase)
                return true;

            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
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

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AutoMidiPlayer.WPF.Core;
using AutoMidiPlayer.WPF.Helpers;
using AutoMidiPlayer.WPF.Services;
using AutoMidiPlayer.WPF.ViewModels;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Views;

public partial class MainWindowView : FluentWindow
{
    private GlobalHotkeyService? _hotkeyService;
    private MainWindowViewModel? _boundVm;
    private bool _isFullscreen;
    private WindowState _windowStateBeforeFullscreen = WindowState.Normal;
    private ScrollViewer? _navigationScrollViewer;
    private SmoothScrollAnimator? _navigationSmoothAnimator;
    private bool _isNavigationScrollInitialized;
    private bool _isNavigationScrollUpVisible;
    private bool _isNavigationScrollDownVisible;
    private DateTime _navigationScrollUpShowBlockedUntilUtc;
    private DateTime _navigationScrollDownShowBlockedUntilUtc;

    private const double NavigationScrollStepFactor = 0.72;
    private const double NavigationWheelStep = 40;
    private const double NavigationScrollSmoothingFactor = 0.24;
    private const double NavigationScrollSnapThreshold = 1.25;
    private const double NavigationScrollMaxStep = 72;
    private const double NavigationScrollDirectionThreshold = 1.5;
    private const double NavigationScrollButtonHiddenScale = 0.62;
    private const double NavigationScrollButtonIntroOpacityMs = 220;
    private const double NavigationScrollButtonIntroScaleMs = 320;
    private const double NavigationScrollButtonOutroOpacityMs = 210;
    private const double NavigationScrollButtonOutroScaleMs = 280;
    private static readonly SmoothScrollAnimatorOptions NavigationSmoothScrollOptions = new()
    {
        SmoothingFactor = NavigationScrollSmoothingFactor,
        SnapThreshold = NavigationScrollSnapThreshold,
        MaxStep = NavigationScrollMaxStep,
        ReferenceFrameRate = 60d,
        MinFrameSeconds = 1d / 240d,
        MaxFrameSeconds = 1d / 24d
    };

    public MainWindowView()
    {
        InitializeComponent();
        Closing += OnClosing;
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;

        UpdateWindowButtonState();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeNavigationScrollUi();

        if (DataContext is MainWindowViewModel vm)
        {
            AttachStartupProgress(vm);
            await vm.WaitForStartupLoadAsync();

            // Initialize global hotkey service with window handle after startup song loading.
            _hotkeyService = vm.Ioc.Get<GlobalHotkeyService>();
            _hotkeyService.Initialize(this);

            // Wire up hotkey events to playback controls
            _hotkeyService.PlayPausePressed += async (_, _) => await vm.PlaybackControls.PlayPause();
            _hotkeyService.NextPressed += async (_, _) => await vm.PlaybackControls.Next();
            _hotkeyService.PreviousPressed += (_, _) => vm.PlaybackControls.Previous();
            _hotkeyService.SpeedUpPressed += (_, _) => vm.SongSettings.IncreaseSpeed();
            _hotkeyService.SpeedDownPressed += (_, _) => vm.SongSettings.DecreaseSpeed();
            _hotkeyService.MouseStopRequested += async (_, _) =>
            {
                if (!vm.PlaybackControls.IsPlaying)
                    return;

                if (!WindowHelper.IsGameFocused())
                    return;

                await Dispatcher.InvokeAsync(vm.PlaybackControls.PlayPause);
            };
            _hotkeyService.PanicPressed += (_, _) =>
            {
                _hotkeyService?.Dispose();
                TrayIcon?.Dispose();
                Application.Current.Shutdown();
            };

            vm.CompleteStartupInteractionLock();
        }
    }

    private void AttachStartupProgress(MainWindowViewModel vm)
    {
        if (_boundVm == vm)
            return;

        if (_boundVm is not null)
            _boundVm.PropertyChanged -= OnViewModelPropertyChanged;

        _boundVm = vm;
        _boundVm.PropertyChanged += OnViewModelPropertyChanged;

        UpdateStartupProgressValue(vm.StartupLoadProgress, immediate: true);
        UpdateStartupProgressVisibility(vm.IsStartupProgressVisible, immediate: true);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel vm)
            return;

        if (e.PropertyName == nameof(MainWindowViewModel.StartupLoadProgress))
        {
            Dispatcher.Invoke(() => UpdateStartupProgressValue(vm.StartupLoadProgress, immediate: false));
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsStartupProgressVisible))
        {
            Dispatcher.Invoke(() => UpdateStartupProgressVisibility(vm.IsStartupProgressVisible, immediate: false));
        }
    }

    private void UpdateStartupProgressValue(double value, bool immediate)
    {
        var progressBar = FindName("StartupProgressBar") as System.Windows.Controls.ProgressBar;
        if (progressBar is null)
            return;

        var target = Math.Clamp(value, 0, 100);
        if (immediate)
        {
            progressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            progressBar.Value = target;
            return;
        }

        var animation = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };

        progressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void UpdateStartupProgressVisibility(bool isVisible, bool immediate)
    {
        var progressHost = FindName("StartupProgressHost") as FrameworkElement;
        if (progressHost is null)
            return;

        if (isVisible)
        {
            progressHost.Visibility = Visibility.Visible;
            if (immediate)
            {
                progressHost.BeginAnimation(OpacityProperty, null);
                progressHost.Opacity = 1;
                return;
            }

            var fadeIn = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(120),
                FillBehavior = FillBehavior.HoldEnd
            };

            progressHost.BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
            return;
        }

        if (immediate)
        {
            progressHost.BeginAnimation(OpacityProperty, null);
            progressHost.Opacity = 0;
            progressHost.Visibility = Visibility.Collapsed;
            return;
        }

        var fadeOut = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };

        fadeOut.Completed += (_, _) =>
        {
            progressHost.Visibility = Visibility.Collapsed;
        };

        progressHost.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_boundVm is not null)
            _boundVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (_isNavigationScrollInitialized)
        {
            RootNavigation.Loaded -= OnRootNavigationLoaded;
            RootNavigation.SizeChanged -= OnRootNavigationSizeChanged;
            _isNavigationScrollInitialized = false;
        }

        DetachNavigationScrollViewer();
        StopNavigationSmoothScroll();

        // Dispose the hotkey service and tray icon when closing
        _hotkeyService?.Dispose();
        TrayIcon?.Dispose();
    }

    private void InitializeNavigationScrollUi()
    {
        if (_isNavigationScrollInitialized)
            return;

        _isNavigationScrollInitialized = true;
        RootNavigation.Loaded += OnRootNavigationLoaded;
        RootNavigation.SizeChanged += OnRootNavigationSizeChanged;

        TryAttachNavigationScrollViewer();
    }

    private void OnRootNavigationLoaded(object sender, RoutedEventArgs e)
    {
        TryAttachNavigationScrollViewer();
    }

    private void OnRootNavigationSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TryAttachNavigationScrollViewer();
        UpdateNavigationScrollButtons();
    }

    private void TryAttachNavigationScrollViewer(bool allowScheduleRetry = true)
    {
        var viewer = FindDescendant<ScrollViewer>(RootNavigation);
        if (ReferenceEquals(_navigationScrollViewer, viewer))
        {
            UpdateNavigationScrollButtons();
            return;
        }

        DetachNavigationScrollViewer();

        _navigationScrollViewer = viewer;
        if (_navigationScrollViewer is null)
        {
            if (allowScheduleRetry)
            {
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    // Avoid an endless loaded-priority retry chain when no internal
                    // ScrollViewer exists for the current template/state.
                    TryAttachNavigationScrollViewer(allowScheduleRetry: false);
                }));
            }

            UpdateNavigationScrollButtons();
            return;
        }

        _navigationScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        ScrollViewerAutoFadeBehavior.SetIsEnabled(_navigationScrollViewer, false);
        _navigationScrollViewer.ScrollChanged += OnNavigationScrollChanged;
        _navigationScrollViewer.SizeChanged += OnNavigationScrollViewerSizeChanged;
        _navigationScrollViewer.PreviewMouseWheel += OnNavigationPreviewMouseWheel;
        _navigationSmoothAnimator = new SmoothScrollAnimator(_navigationScrollViewer, NavigationSmoothScrollOptions, UpdateNavigationScrollButtons);
        _navigationSmoothAnimator.SyncTargetToCurrentOffset();

        UpdateNavigationScrollButtons();
    }

    private void DetachNavigationScrollViewer()
    {
        if (_navigationScrollViewer is null)
            return;

        StopNavigationSmoothScroll();
        _navigationSmoothAnimator?.Dispose();
        _navigationSmoothAnimator = null;
        _navigationScrollViewer.ScrollChanged -= OnNavigationScrollChanged;
        _navigationScrollViewer.SizeChanged -= OnNavigationScrollViewerSizeChanged;
        _navigationScrollViewer.PreviewMouseWheel -= OnNavigationPreviewMouseWheel;
        _navigationScrollViewer = null;
    }

    private void OnNavigationScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_navigationSmoothAnimator is { IsRunning: false })
            _navigationSmoothAnimator.SyncTargetToCurrentOffset();

        UpdateNavigationScrollButtons();
    }

    private void OnNavigationScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateNavigationScrollButtons();
    }

    private void OnNavigationPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_navigationScrollViewer is null || _navigationScrollViewer.ScrollableHeight <= 0)
            return;

        var animator = _navigationSmoothAnimator;
        if (animator is null)
            return;

        if (System.Windows.Input.Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        if (!IsNavigationSmoothScrollingEnabled())
        {
            StopNavigationSmoothScroll();
            animator.SyncTargetToCurrentOffset();
            return;
        }

        e.Handled = true;

        var deltaOffset = -(e.Delta / 120d) * NavigationWheelStep;
        if (!animator.IsRunning)
            animator.SyncTargetToCurrentOffset();

        var currentOffset = _navigationScrollViewer.VerticalOffset;
        var maxLead = Math.Max(NavigationWheelStep * 10d, _navigationScrollViewer.ViewportHeight * 1.15d);
        var minTarget = Math.Max(0d, currentOffset - maxLead);
        var maxTarget = Math.Min(_navigationScrollViewer.ScrollableHeight, currentOffset + maxLead);
        animator.ApplyDelta(deltaOffset, minTarget, maxTarget, resetOnDirectionChange: true);

        UpdateNavigationScrollButtons();
    }

    private static bool IsNavigationSmoothScrollingEnabled()
    {
        if (Application.Current?.Resources["SmoothScrollingEnabled"] is bool isEnabled)
            return isEnabled;

        return true;
    }

    private void NavigationScrollUp_Click(object sender, RoutedEventArgs e)
    {
        _navigationScrollUpShowBlockedUntilUtc = DateTime.UtcNow.AddMilliseconds(NavigationScrollButtonOutroScaleMs + 40);
        SetNavigationScrollButtonVisibility(NavigationScrollUpButton, shouldShow: false, ref _isNavigationScrollUpVisible);
        ScrollNavigationBy(-GetNavigationScrollStep());
    }

    private void NavigationScrollDown_Click(object sender, RoutedEventArgs e)
    {
        _navigationScrollDownShowBlockedUntilUtc = DateTime.UtcNow.AddMilliseconds(NavigationScrollButtonOutroScaleMs + 40);
        SetNavigationScrollButtonVisibility(NavigationScrollDownButton, shouldShow: false, ref _isNavigationScrollDownVisible);
        ScrollNavigationBy(GetNavigationScrollStep());
    }

    private double GetNavigationScrollStep()
    {
        if (_navigationScrollViewer is null)
            return 96;

        var viewportStep = _navigationScrollViewer.ViewportHeight * NavigationScrollStepFactor;
        return Math.Clamp(viewportStep, 70, 220);
    }

    private void ScrollNavigationBy(double delta)
    {
        if (_navigationScrollViewer is null || _navigationScrollViewer.ScrollableHeight <= 0)
            return;

        var animator = _navigationSmoothAnimator;
        if (animator is null)
            return;

        if (!animator.IsRunning)
            animator.SyncTargetToCurrentOffset();

        var targetOffset = Math.Clamp(
            animator.TargetOffset + delta,
            0,
            _navigationScrollViewer.ScrollableHeight);

        animator.SetTargetOffset(targetOffset, startIfNeeded: true, immediateStep: true);

        UpdateNavigationScrollButtons();
    }

    private void StopNavigationSmoothScroll()
    {
        _navigationSmoothAnimator?.Stop();
    }

    private void UpdateNavigationScrollButtons()
    {
        if (NavigationScrollUpButton is null || NavigationScrollDownButton is null)
            return;

        if (_navigationScrollViewer is null)
        {
            SetNavigationScrollButtonVisibility(NavigationScrollUpButton, shouldShow: false, ref _isNavigationScrollUpVisible);
            SetNavigationScrollButtonVisibility(NavigationScrollDownButton, shouldShow: false, ref _isNavigationScrollDownVisible);
            return;
        }

        var canScroll = _navigationScrollViewer.ScrollableHeight > 0.5;
        if (!canScroll)
        {
            SetNavigationScrollButtonVisibility(NavigationScrollUpButton, shouldShow: false, ref _isNavigationScrollUpVisible);
            SetNavigationScrollButtonVisibility(NavigationScrollDownButton, shouldShow: false, ref _isNavigationScrollDownVisible);
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var canScrollUp = _navigationScrollViewer.VerticalOffset > NavigationScrollDirectionThreshold;
        var canScrollDown = _navigationScrollViewer.VerticalOffset < _navigationScrollViewer.ScrollableHeight - NavigationScrollDirectionThreshold;

        var showUp = canScrollUp && nowUtc >= _navigationScrollUpShowBlockedUntilUtc;
        var showDown = canScrollDown && nowUtc >= _navigationScrollDownShowBlockedUntilUtc;

        SetNavigationScrollButtonVisibility(NavigationScrollUpButton, showUp, ref _isNavigationScrollUpVisible);
        SetNavigationScrollButtonVisibility(NavigationScrollDownButton, showDown, ref _isNavigationScrollDownVisible);
    }

    private static void SetNavigationScrollButtonVisibility(System.Windows.Controls.Button button, bool shouldShow, ref bool isVisible)
    {
        if (isVisible == shouldShow)
            return;

        isVisible = shouldShow;

        ScaleTransform scale;
        if (button.RenderTransform is ScaleTransform existingScale)
        {
            // Style-created transforms can be frozen shared instances; clone before animating.
            if (existingScale.IsFrozen)
            {
                scale = existingScale.CloneCurrentValue();
                button.RenderTransform = scale;
            }
            else
            {
                scale = existingScale;
            }
        }
        else
        {
            scale = new ScaleTransform(NavigationScrollButtonHiddenScale, NavigationScrollButtonHiddenScale);
            button.RenderTransform = scale;
        }

        button.RenderTransformOrigin = new Point(0.5, 0.5);

        var currentOpacity = Math.Clamp(button.Opacity, 0, 1);
        var currentScaleX = Math.Clamp(scale.ScaleX, NavigationScrollButtonHiddenScale, 1);
        var currentScaleY = Math.Clamp(scale.ScaleY, NavigationScrollButtonHiddenScale, 1);

        button.BeginAnimation(UIElement.OpacityProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (!shouldShow)
        {
            button.IsEnabled = false;
            button.IsHitTestVisible = false;
            button.Visibility = Visibility.Visible;

            var hideFromOpacity = currentOpacity > 0.01 ? currentOpacity : 1d;
            var hideFromScaleX = currentScaleX > NavigationScrollButtonHiddenScale + 0.01 ? currentScaleX : 1d;
            var hideFromScaleY = currentScaleY > NavigationScrollButtonHiddenScale + 0.01 ? currentScaleY : 1d;

            button.Opacity = hideFromOpacity;
            scale.ScaleX = hideFromScaleX;
            scale.ScaleY = hideFromScaleY;

            var hideOpacityAnimation = new DoubleAnimation
            {
                From = hideFromOpacity,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(NavigationScrollButtonOutroOpacityMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };

            var hideScaleAnimationX = new DoubleAnimation
            {
                From = hideFromScaleX,
                To = NavigationScrollButtonHiddenScale,
                Duration = TimeSpan.FromMilliseconds(NavigationScrollButtonOutroScaleMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop
            };

            var hideScaleAnimationY = new DoubleAnimation
            {
                From = hideFromScaleY,
                To = NavigationScrollButtonHiddenScale,
                Duration = TimeSpan.FromMilliseconds(NavigationScrollButtonOutroScaleMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.Stop
            };

            hideOpacityAnimation.Completed += (_, _) =>
            {
                button.BeginAnimation(UIElement.OpacityProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                button.Opacity = 0;
                scale.ScaleX = NavigationScrollButtonHiddenScale;
                scale.ScaleY = NavigationScrollButtonHiddenScale;
                button.Visibility = Visibility.Collapsed;
            };

            button.BeginAnimation(UIElement.OpacityProperty, hideOpacityAnimation, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, hideScaleAnimationX, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, hideScaleAnimationY, HandoffBehavior.SnapshotAndReplace);
            return;
        }

        button.Visibility = Visibility.Visible;
        button.IsEnabled = true;
        button.IsHitTestVisible = true;

        button.Opacity = currentOpacity;
        scale.ScaleX = currentScaleX;
        scale.ScaleY = currentScaleY;

        var opacityAnimation = new DoubleAnimation
        {
            From = currentOpacity,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(NavigationScrollButtonIntroOpacityMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        var scaleAnimationX = new DoubleAnimation
        {
            From = currentScaleX,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(NavigationScrollButtonIntroScaleMs),
            EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        var scaleAnimationY = new DoubleAnimation
        {
            From = currentScaleY,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(NavigationScrollButtonIntroScaleMs),
            EasingFunction = new BackEase { Amplitude = 0.7, EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        opacityAnimation.Completed += (_, _) =>
        {
            button.BeginAnimation(UIElement.OpacityProperty, null);
            button.Opacity = 1;
        };

        scaleAnimationY.Completed += (_, _) =>
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        };

        button.BeginAnimation(UIElement.OpacityProperty, opacityAnimation, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimationX, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimationY, HandoffBehavior.SnapshotAndReplace);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateWindowButtonState();
    }

    private void UpdateWindowButtonState()
    {
        if (MaximizeRestoreGlyph is null || MaximizeRestoreButton is null)
            return;

        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeRestoreGlyph.Text = isMaximized ? "\uE923" : "\uE922";
        MaximizeRestoreButton.ToolTip = isMaximized ? "Restore down" : "Maximize";

        // Fullscreen button isn't necessary but keep this feature
        // if (FullscreenGlyph is not null && FullscreenButton is not null)
        // {
        //     FullscreenGlyph.Text = _isFullscreen ? "\uE73F" : "\uE740";
        //     FullscreenButton.ToolTip = _isFullscreen ? "Exit full screen" : "Enter full screen";
        // }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleMaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {

    }

    private void ToggleFullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (_isFullscreen)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _windowStateBeforeFullscreen == WindowState.Minimized
                ? WindowState.Normal
                : _windowStateBeforeFullscreen;
            _isFullscreen = false;
        }
        else
        {
            _windowStateBeforeFullscreen = WindowState == WindowState.Minimized
                ? WindowState.Normal
                : WindowState;

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
        }

        UpdateWindowButtonState();
    }

    private void GameDrawerItem_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not NavigationViewItem navigationViewItem)
            return;

        // LeftFluent NavigationView assigns ItemTemplate internally; set the template locally for the game drawer item.
        if (TryFindResource("GameDrawerNavigationItemTemplate") is System.Windows.Controls.ControlTemplate template)
        {
            navigationViewItem.Template = template;
        }

        UpdateNavigationScrollButtons();
    }

    private void TrayPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (!vm.IsControlPanelEnabled)
                return;

            _ = vm.PlaybackControls.PlayPause();
        }
    }

    private async void TrayNext_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (!vm.IsControlPanelEnabled)
                return;

            await vm.PlaybackControls.Next();
        }
    }

    private void TrayShowWindow_Click(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        TrayIcon?.Dispose();
        Application.Current.Shutdown();
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T typedChild)
                return typedChild;

            var result = FindDescendant<T>(child);
            if (result != null)
                return result;
        }

        return null;
    }

}

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;
using AutoMidiPlayer.WPF.Core;
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

        // Dispose the hotkey service and tray icon when closing
        _hotkeyService?.Dispose();
        TrayIcon?.Dispose();
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

}

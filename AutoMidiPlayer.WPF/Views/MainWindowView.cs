using System.ComponentModel;
using System.Windows;
using AutoMidiPlayer.WPF.Services;
using AutoMidiPlayer.WPF.ViewModels;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Views;

public partial class MainWindowView : FluentWindow
{
    private GlobalHotkeyService? _hotkeyService;

    public MainWindowView()
    {
        InitializeComponent();
        Closing += OnClosing;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // Initialize global hotkey service with window handle
            _hotkeyService = vm.Ioc.Get<GlobalHotkeyService>();
            _hotkeyService.Initialize(this);

            // Wire up hotkey events to playback controls
            _hotkeyService.PlayPausePressed += async (_, _) => await vm.Playback.PlayPause();
            _hotkeyService.NextPressed += (_, _) => vm.Playback.Next();
            _hotkeyService.PreviousPressed += (_, _) => vm.Playback.Previous();
            _hotkeyService.SpeedUpPressed += (_, _) => vm.SongSettings.IncreaseSpeed();
            _hotkeyService.SpeedDownPressed += (_, _) => vm.SongSettings.DecreaseSpeed();
            _hotkeyService.PanicPressed += (_, _) =>
            {
                _hotkeyService?.Dispose();
                TrayIcon?.Dispose();
                Application.Current.Shutdown();
            };
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Dispose the hotkey service and tray icon when closing
        _hotkeyService?.Dispose();
        TrayIcon?.Dispose();
    }

    private void TrayPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            _ = vm.Playback.PlayPause();
        }
    }

    private void TrayNext_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Playback.Next();
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

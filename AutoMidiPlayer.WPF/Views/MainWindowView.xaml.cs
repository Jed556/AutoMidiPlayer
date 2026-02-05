using System.ComponentModel;
using System.Windows;
using AutoMidiPlayer.WPF.ViewModels;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Views;

public partial class MainWindowView : FluentWindow
{
    public MainWindowView()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Dispose the tray icon when closing
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

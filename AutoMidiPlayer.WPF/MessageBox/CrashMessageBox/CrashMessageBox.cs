using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AutoMidiPlayer.WPF.MessageBox;

/// <summary>
/// A themed crash/error message box with an error icon, clickable log path,
/// and a readonly error text box with a hover copy button.
/// </summary>
public partial class CrashMessageBox : Wpf.Ui.Controls.MessageBox
{
    private readonly string _logPath;

    public CrashMessageBox(Exception exception, string logPath)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logPath);

        _logPath = logPath;

        InitializeComponent();

        ErrorTextBox.Text = exception.Message;
        LogPathRun.Text = logPath;

        if (Application.Current.TryFindResource("AppHyperlinkStyle") is Style hyperlinkStyle)
            LogPathHyperlink.Style = hyperlinkStyle;
    }

    public static void Show(Exception exception, string logPath)
    {
        var messageBox = new CrashMessageBox(exception, logPath);
        _ = messageBox.ShowDialogAsync();
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
}

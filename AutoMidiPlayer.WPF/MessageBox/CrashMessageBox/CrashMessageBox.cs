using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

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

        if (Application.Current.TryFindResource("GhostIconButton") is Style ghostStyle)
            CopyButton.Style = ghostStyle;
    }

    public static void Show(Exception exception, string logPath)
    {
        var messageBox = new CrashMessageBox(exception, logPath);
        _ = messageBox.ShowDialogAsync();
    }

    private void OnLogPathClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_logPath}\"")
        {
            UseShellExecute = true
        });

        e.Handled = true;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ErrorTextBox.Text ?? string.Empty);
        e.Handled = true;
    }

    private void OnErrorGridMouseEnter(object sender, MouseEventArgs e)
    {
        CopyButton.Opacity = 0.6;
    }

    private void OnErrorGridMouseLeave(object sender, MouseEventArgs e)
    {
        CopyButton.Opacity = 0;
    }

    private void OnCopyButtonMouseEnter(object sender, MouseEventArgs e)
    {
        CopyButton.Opacity = 1;
    }
}

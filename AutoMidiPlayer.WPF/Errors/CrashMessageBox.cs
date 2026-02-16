using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Errors;

/// <summary>
/// A themed crash/error message box with an error icon, clickable log path,
/// and a readonly error text box with a hover copy button.
/// </summary>
public static class CrashMessageBox
{
    public static void Show(Exception exception, string logPath)
    {
        var logFolder = Path.GetDirectoryName(logPath) ?? logPath;
        var errorMessage = exception.Message;

        // --- Title row with error icon ---
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        headerRow.Children.Add(new SymbolIcon
        {
            Symbol = SymbolRegular.ErrorCircle12,
            FontSize = 24,
            Margin = new Thickness(0, 0, 8, 0),
            Filled = true
        });
        headerRow.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "AutoMidiPlayer Error",
            FontWeight = FontWeights.Bold,
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center
        });

        // --- Log path link ---
        var logText = new System.Windows.Controls.TextBlock
        {
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        logText.Inlines.Add(new Run("An error occurred. Log saved to:\n"));
        var logLink = new Hyperlink(new Run(logPath))
        {
            NavigateUri = new Uri(logFolder)
        };

        if (Application.Current.TryFindResource("AppHyperlinkStyle") is Style hyperlinkStyle)
            logLink.Style = hyperlinkStyle;

        logLink.RequestNavigate += (_, args) =>
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{logPath}\"")
            {
                UseShellExecute = true
            });
            args.Handled = true;
        };
        logText.Inlines.Add(logLink);

        // --- Error message textbox with copy overlay ---
        var errorTextBox = new System.Windows.Controls.TextBox
        {
            Text = errorMessage,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 36, 6),
            FontSize = 14
        };

        var copyIcon = new SymbolIcon
        {
            Symbol = SymbolRegular.Copy16,
            FontSize = 18
        };
        var copyButton = new System.Windows.Controls.Button
        {
            Content = copyIcon,
            ToolTip = "Copy error message",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4),
            Cursor = Cursors.Hand,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 4, 4, 0)
        };

        if (Application.Current.TryFindResource("GhostIconButton") is Style ghostStyle)
            copyButton.Style = ghostStyle;

        copyButton.Click += (_, _) => Clipboard.SetText(errorMessage);

        var errorGrid = new Grid();
        errorGrid.Children.Add(errorTextBox);
        errorGrid.Children.Add(copyButton);

        // Show copy button on hover
        errorGrid.MouseEnter += (_, _) => copyButton.Opacity = 0.6;
        errorGrid.MouseLeave += (_, _) => copyButton.Opacity = 0;
        copyButton.MouseEnter += (_, _) => copyButton.Opacity = 1;

        // --- Assemble content ---
        var content = new StackPanel();
        content.Children.Add(headerRow);
        content.Children.Add(logText);
        content.Children.Add(errorGrid);

        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = string.Empty,
            ShowTitle = false,
            Content = content,
            CloseButtonText = "OK",
            CloseButtonAppearance = ControlAppearance.Secondary
        };

        _ = messageBox.ShowDialogAsync();
    }
}

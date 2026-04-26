using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Helpers;

public static class MessageBoxHelper
{
    public static System.Windows.MessageBoxResult Show(
        string message,
        string title,
        System.Windows.MessageBoxButton button = System.Windows.MessageBoxButton.OK,
        System.Windows.MessageBoxImage image = System.Windows.MessageBoxImage.None)
    {
        message ??= string.Empty;
        title ??= string.Empty;

        try
        {
            var app = Application.Current;
            var dispatcher = app?.Dispatcher;

            if (dispatcher == null)
                return ShowNative(message, title, button, image);

            if (dispatcher.CheckAccess())
                return ShowThemedOnUiThread(message, title, button, image);

            return dispatcher.Invoke(
                () => ShowThemedOnUiThread(message, title, button, image),
                DispatcherPriority.Send);
        }
        catch
        {
            return ShowNativeSafe(message, title, button, image);
        }
    }

    public static void ShowError(string message, string title)
    {
        _ = Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }

    public static void ShowWarning(string message, string title)
    {
        _ = Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    }

    public static void ShowInformation(string message, string title)
    {
        _ = Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    public static bool ConfirmOkCancel(string message, string title, System.Windows.MessageBoxImage image)
    {
        return Show(message, title, System.Windows.MessageBoxButton.OKCancel, image)
            == System.Windows.MessageBoxResult.OK;
    }

    private static System.Windows.MessageBoxResult ShowThemedOnUiThread(
        string message,
        string title,
        System.Windows.MessageBoxButton button,
        System.Windows.MessageBoxImage image)
    {
        var (appearance, iconSymbol) = ResolveVisualStyle(image);
        var messageBox = CreateThemedMessageBox(message, title, button, appearance, iconSymbol);

        var owner = ResolveOwnerWindow();
        if (owner != null && owner != messageBox)
        {
            messageBox.Owner = owner;
            messageBox.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var result = messageBox.ShowDialogAsync(showAsDialog: true).GetAwaiter().GetResult();
        return MapResult(button, result);
    }

    private static Wpf.Ui.Controls.MessageBox CreateThemedMessageBox(
        string message,
        string title,
        System.Windows.MessageBoxButton button,
        ControlAppearance appearance,
        SymbolRegular? iconSymbol)
    {
        var messageText = new System.Windows.Controls.TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 640,
            VerticalAlignment = VerticalAlignment.Center
        };

        object content = messageText;
        if (iconSymbol.HasValue)
        {
            var contentPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal
            };

            contentPanel.Children.Add(new SymbolIcon
            {
                Symbol = iconSymbol.Value,
                FontSize = 24,
                Filled = true,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Top
            });
            contentPanel.Children.Add(messageText);
            content = contentPanel;
        }

        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            ShowTitle = true,
            Content = content,
            PrimaryButtonAppearance = appearance,
            SecondaryButtonAppearance = ControlAppearance.Secondary,
            CloseButtonAppearance = ControlAppearance.Secondary
        };

        ConfigureButtons(messageBox, button, appearance);
        return messageBox;
    }

    private static void ConfigureButtons(
        Wpf.Ui.Controls.MessageBox messageBox,
        System.Windows.MessageBoxButton button,
        ControlAppearance emphasisAppearance)
    {
        messageBox.IsPrimaryButtonEnabled = false;
        messageBox.IsSecondaryButtonEnabled = false;
        messageBox.IsCloseButtonEnabled = false;

        messageBox.PrimaryButtonText = string.Empty;
        messageBox.SecondaryButtonText = string.Empty;
        messageBox.CloseButtonText = string.Empty;

        switch (button)
        {
            case System.Windows.MessageBoxButton.OK:
                messageBox.IsCloseButtonEnabled = true;
                messageBox.CloseButtonText = "OK";
                messageBox.CloseButtonAppearance = emphasisAppearance;
                break;

            case System.Windows.MessageBoxButton.OKCancel:
                messageBox.IsPrimaryButtonEnabled = true;
                messageBox.IsCloseButtonEnabled = true;
                messageBox.PrimaryButtonText = "OK";
                messageBox.CloseButtonText = "Cancel";
                break;

            case System.Windows.MessageBoxButton.YesNo:
                messageBox.IsPrimaryButtonEnabled = true;
                messageBox.IsSecondaryButtonEnabled = true;
                messageBox.IsCloseButtonEnabled = false;
                messageBox.PrimaryButtonText = "Yes";
                messageBox.SecondaryButtonText = "No";
                break;

            case System.Windows.MessageBoxButton.YesNoCancel:
                messageBox.IsPrimaryButtonEnabled = true;
                messageBox.IsSecondaryButtonEnabled = true;
                messageBox.IsCloseButtonEnabled = true;
                messageBox.PrimaryButtonText = "Yes";
                messageBox.SecondaryButtonText = "No";
                messageBox.CloseButtonText = "Cancel";
                break;

            default:
                messageBox.IsCloseButtonEnabled = true;
                messageBox.CloseButtonText = "OK";
                messageBox.CloseButtonAppearance = emphasisAppearance;
                break;
        }
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

    private static (ControlAppearance Appearance, SymbolRegular? IconSymbol) ResolveVisualStyle(
        System.Windows.MessageBoxImage image)
    {
        return image switch
        {
            System.Windows.MessageBoxImage.Error => (ControlAppearance.Danger, SymbolRegular.ErrorCircle24),
            System.Windows.MessageBoxImage.Warning => (ControlAppearance.Caution, SymbolRegular.Warning24),
            System.Windows.MessageBoxImage.Information => (ControlAppearance.Info, SymbolRegular.Info24),
            System.Windows.MessageBoxImage.Question => (ControlAppearance.Secondary, SymbolRegular.QuestionCircle24),
            _ => (ControlAppearance.Secondary, null)
        };
    }

    private static System.Windows.MessageBoxResult MapResult(
        System.Windows.MessageBoxButton button,
        Wpf.Ui.Controls.MessageBoxResult result)
    {
        return button switch
        {
            System.Windows.MessageBoxButton.OK => System.Windows.MessageBoxResult.OK,
            System.Windows.MessageBoxButton.OKCancel => result == Wpf.Ui.Controls.MessageBoxResult.Primary
                ? System.Windows.MessageBoxResult.OK
                : System.Windows.MessageBoxResult.Cancel,
            System.Windows.MessageBoxButton.YesNo => result switch
            {
                Wpf.Ui.Controls.MessageBoxResult.Primary => System.Windows.MessageBoxResult.Yes,
                Wpf.Ui.Controls.MessageBoxResult.Secondary => System.Windows.MessageBoxResult.No,
                _ => System.Windows.MessageBoxResult.None
            },
            System.Windows.MessageBoxButton.YesNoCancel => result switch
            {
                Wpf.Ui.Controls.MessageBoxResult.Primary => System.Windows.MessageBoxResult.Yes,
                Wpf.Ui.Controls.MessageBoxResult.Secondary => System.Windows.MessageBoxResult.No,
                _ => System.Windows.MessageBoxResult.Cancel
            },
            _ => result == Wpf.Ui.Controls.MessageBoxResult.Primary
                ? System.Windows.MessageBoxResult.OK
                : System.Windows.MessageBoxResult.None
        };
    }

    private static System.Windows.MessageBoxResult ShowNativeSafe(
        string message,
        string title,
        System.Windows.MessageBoxButton button,
        System.Windows.MessageBoxImage image)
    {
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                return dispatcher.Invoke(
                    () => ShowNative(message, title, button, image),
                    DispatcherPriority.Send);
            }

            return ShowNative(message, title, button, image);
        }
        catch
        {
            return System.Windows.MessageBoxResult.None;
        }
    }

    private static System.Windows.MessageBoxResult ShowNative(
        string message,
        string title,
        System.Windows.MessageBoxButton button,
        System.Windows.MessageBoxImage image)
    {
        return System.Windows.MessageBox.Show(message, title, button, image);
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public enum DialogActionOutcome
{
    Cancelled,
    Confirmed,
    Custom
}

public sealed class DialogActionButton
{
    public string Text { get; init; } = string.Empty;

    public ControlAppearance Appearance { get; init; } = ControlAppearance.Secondary;

    public Func<Task>? CallbackAsync { get; init; }
}

public sealed class DialogActionRequest
{
    public string Title { get; init; } = string.Empty;

    public SymbolRegular? Icon { get; init; }

    public string? Body { get; init; }

    public object? Content { get; init; }

    public DialogActionButton? ConfirmButton { get; init; } = new()
    {
        Text = "Confirm",
        Appearance = ControlAppearance.Primary
    };

    public DialogActionButton? CancelButton { get; init; } = new()
    {
        Text = "Cancel",
        Appearance = ControlAppearance.Secondary
    };

    public DialogActionButton? CustomButton { get; init; }
}

/// <summary>
/// Helper class for creating ContentDialogs with proper DialogHost setup.
/// </summary>
public static class DialogHelper
{
    /// <summary>
    /// Creates a new ContentDialog with the DialogHostEx property already set.
    /// </summary>
    public static ContentDialog CreateDialog()
    {
        var dialog = new ContentDialog();
        SetupDialogHost(dialog);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            dialog.Style = dialogStyle;

        return dialog;
    }

    /// <summary>
    /// Sets up the DialogHostEx property for an existing ContentDialog.
    /// </summary>
    public static bool SetupDialogHost(ContentDialog dialog)
    {
        if (dialog.DialogHostEx != null)
            return true;

        var app = Application.Current;
        if (app == null)
            return false;

        var windows = app.Windows.OfType<Window>().ToList();
        var activeWindow = windows.FirstOrDefault(w => w.IsActive)
                           ?? app.MainWindow
                           ?? windows.FirstOrDefault(w => w.IsVisible)
                           ?? windows.FirstOrDefault();

        if (activeWindow == null)
            return false;

        var dialogHost = ContentDialogHost.GetForWindow(activeWindow);

        if (dialogHost == null)
        {
            foreach (var window in windows)
            {
                dialogHost = ContentDialogHost.GetForWindow(window);
                if (dialogHost != null)
                    break;
            }
        }

        if (dialogHost == null)
            return false;

        dialog.DialogHostEx = dialogHost;
        return true;
    }

    /// <summary>
    /// Waits briefly for a <see cref="ContentDialogHost" /> to become available.
    /// Useful during startup when the main window visual tree isn't fully ready yet.
    /// </summary>
    public static async Task<bool> EnsureDialogHostAsync(ContentDialog dialog, int attempts = 20, int delayMilliseconds = 50)
    {
        if (attempts < 1)
            attempts = 1;

        if (delayMilliseconds < 1)
            delayMilliseconds = 1;

        for (var index = 0; index < attempts; index++)
        {
            if (SetupDialogHost(dialog))
                return true;

            await Task.Delay(delayMilliseconds);
        }

        return false;
    }

    /// <summary>
    /// Shows a configurable action dialog with optional confirm, cancel and custom buttons.
    /// </summary>
    public static async Task<DialogActionOutcome> ShowActionDialogAsync(DialogActionRequest request)
    {
        var dialog = CreateDialog();

        dialog.Title = BuildTitleContent(request.Title, request.Icon);
        dialog.Content = BuildDialogContent(request.Body, request.Content);

        if (request.ConfirmButton is not null)
        {
            dialog.PrimaryButtonText = request.ConfirmButton.Text;
            dialog.PrimaryButtonAppearance = request.ConfirmButton.Appearance;
        }

        if (request.CustomButton is not null)
        {
            dialog.SecondaryButtonText = request.CustomButton.Text;
            dialog.SecondaryButtonAppearance = request.CustomButton.Appearance;
        }

        if (request.CancelButton is not null)
            dialog.CloseButtonText = request.CancelButton.Text;

        var hostReady = await EnsureDialogHostAsync(dialog);
        if (!hostReady)
        {
            if (request.CancelButton?.CallbackAsync is not null)
                await request.CancelButton.CallbackAsync();

            return DialogActionOutcome.Cancelled;
        }

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary && request.ConfirmButton is not null)
        {
            if (request.ConfirmButton.CallbackAsync is not null)
                await request.ConfirmButton.CallbackAsync();

            return DialogActionOutcome.Confirmed;
        }

        if (result == ContentDialogResult.Secondary && request.CustomButton is not null)
        {
            if (request.CustomButton.CallbackAsync is not null)
                await request.CustomButton.CallbackAsync();

            return DialogActionOutcome.Custom;
        }

        if (request.CancelButton?.CallbackAsync is not null)
            await request.CancelButton.CallbackAsync();

        return DialogActionOutcome.Cancelled;
    }

    private static object BuildDialogContent(string? body, object? content)
    {
        if (string.IsNullOrWhiteSpace(body))
            return content ?? string.Empty;

        if (content is null)
            return body;

        var panel = new StackPanel();

        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        if (content is UIElement element)
        {
            panel.Children.Add(element);
        }
        else
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = content.ToString() ?? string.Empty,
                TextWrapping = TextWrapping.Wrap
            });
        }

        return panel;
    }

    private static object BuildTitleContent(string title, SymbolRegular? icon)
    {
        if (icon is null)
            return title;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(new SymbolIcon
        {
            Symbol = icon.Value,
            Margin = new Thickness(0, 0, 8, 0)
        });

        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center
        });

        return panel;
    }
}

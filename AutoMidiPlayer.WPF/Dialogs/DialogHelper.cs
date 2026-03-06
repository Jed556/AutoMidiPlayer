using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

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
}

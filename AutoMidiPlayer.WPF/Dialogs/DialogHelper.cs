using System.Linq;
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
        return dialog;
    }

    /// <summary>
    /// Sets up the DialogHostEx property for an existing ContentDialog.
    /// </summary>
    public static void SetupDialogHost(ContentDialog dialog)
    {
        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                           ?? Application.Current.MainWindow;
        if (activeWindow != null)
        {
            var dialogHost = ContentDialogHost.GetForWindow(activeWindow);
            if (dialogHost != null)
                dialog.DialogHostEx = dialogHost;
        }
    }
}

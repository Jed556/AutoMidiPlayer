using System;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class UnableToDeleteFileDialog : ContentDialog
{
    private const string FallbackTitle = "Unable to delete file";
    private const string FallbackMessage = "The file could not be deleted. It may be in use, read-only, or protected by permissions.";

    static UnableToDeleteFileDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(UnableToDeleteFileDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public UnableToDeleteFileDialog()
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;
    }

    public static async Task ShowDeleteFailedAsync()
    {
        try
        {
            var dialog = new UnableToDeleteFileDialog();

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                await dialog.ShowAsync();
                return;
            }

            Logger.Log("DialogHost was not ready while showing unable-to-delete dialog. Falling back to MessageBox.");
            MessageBoxHelper.ShowWarning(FallbackMessage, FallbackTitle);
        }
        catch (Exception dialogError)
        {
            Logger.Log("Failed to display unable-to-delete dialog.");
            Logger.LogException(dialogError);
            MessageBoxHelper.ShowWarning(FallbackMessage, FallbackTitle);
        }
    }
}

using System;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class UnableToResetAppDataDialog : ContentDialog
{
    private const string FallbackTitle = "Unable to reset app data";

    static UnableToResetAppDataDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(UnableToResetAppDataDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public UnableToResetAppDataDialog(string message)
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

        MessageTextBlock.Text = message;
    }

    public static async Task ShowErrorAsync(string message)
    {
        try
        {
            var dialog = new UnableToResetAppDataDialog(message);

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                await dialog.ShowAsync();
                return;
            }

            Logger.Log("DialogHost was not ready while showing unable-to-reset dialog. Falling back to MessageBox.");
            MessageBoxHelper.ShowError(message, FallbackTitle);
        }
        catch (Exception dialogError)
        {
            Logger.Log("Failed to display unable-to-reset dialog.");
            Logger.LogException(dialogError);
            MessageBoxHelper.ShowError(message, FallbackTitle);
        }
    }
}

using System;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class ResetAppDataConfirmationDialog : ContentDialog
{
    private const string FallbackTitle = "Reset app data?";
    private const string FallbackMessage = "This clears user settings and local app data, then restarts the app automatically.";

    static ResetAppDataConfirmationDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ResetAppDataConfirmationDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public ResetAppDataConfirmationDialog()
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;
    }

    public static async Task<bool> ConfirmAsync()
    {
        try
        {
            var dialog = new ResetAppDataConfirmationDialog();

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
                return await dialog.ShowAsync() == ContentDialogResult.Primary;

            Logger.Log("DialogHost was not ready while showing reset confirmation dialog. Falling back to MessageBox.");
        }
        catch (Exception dialogError)
        {
            Logger.Log("Failed to display reset confirmation dialog.");
            Logger.LogException(dialogError);
        }

        return MessageBoxHelper.ConfirmOkCancel(
            FallbackMessage,
            FallbackTitle,
            System.Windows.MessageBoxImage.Warning);
    }
}

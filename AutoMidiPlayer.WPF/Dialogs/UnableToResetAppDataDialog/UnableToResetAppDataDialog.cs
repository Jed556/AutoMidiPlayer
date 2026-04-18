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

            CrashLogger.Log("DialogHost was not ready while showing unable-to-reset dialog. Falling back to MessageBox.");
            System.Windows.MessageBox.Show(
                message,
                FallbackTitle,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        catch (Exception dialogError)
        {
            CrashLogger.Log("Failed to display unable-to-reset dialog.");
            CrashLogger.LogException(dialogError);
            System.Windows.MessageBox.Show(
                message,
                FallbackTitle,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}

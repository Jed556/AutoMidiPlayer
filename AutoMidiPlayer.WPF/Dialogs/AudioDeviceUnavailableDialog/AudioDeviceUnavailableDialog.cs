using System;
using System.Windows;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class AudioDeviceUnavailableDialog : ContentDialog
{
    static AudioDeviceUnavailableDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AudioDeviceUnavailableDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public AudioDeviceUnavailableDialog(string message)
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

        MessageTextBlock.Text = message;
    }

    public static async Task ShowInitializationErrorAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = $"Audio output device initialization failed.\n\nError:\n{exception.Message}";

        try
        {
            var dialog = new AudioDeviceUnavailableDialog(message);

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                await dialog.ShowAsync();
                return;
            }

            Logger.Log("DialogHost was not ready while showing audio initialization error. Falling back to MessageBox.");
            MessageBoxHelper.ShowWarning(message, "Audio device unavailable");
        }
        catch (Exception dialogError)
        {
            Logger.Log("Failed to display audio initialization error dialog.");
            Logger.LogException(dialogError);
            MessageBoxHelper.ShowWarning(message, "Audio device unavailable");
        }
    }
}

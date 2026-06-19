using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class AudioDeviceUnavailableView : UserControl
{
    public AudioDeviceUnavailableView(string message)
    {
        InitializeComponent();
        MessageTextBlock.Text = message;
    }

    public static async Task ShowInitializationErrorAsync(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = $"Audio output device initialization failed.\n\nError:\n{exception.Message}";

        try
        {
            var view = new AudioDeviceUnavailableView(message);
            var request = new DialogActionRequest
            {
                Title = "Audio device unavailable",
                Content = view,
                CancelButton = new DialogActionButton
                {
                    Text = "Ignore",
                    Appearance = ControlAppearance.Secondary
                }
            };

            await DialogHelper.ShowActionDialogAsync(request);
        }
        catch (Exception dialogError)
        {
            Logger.Log("Failed to display audio initialization error dialog.");
            Logger.LogException(dialogError);
            MessageBoxHelper.ShowWarning(message, "Audio device unavailable");
        }
    }
}

using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class ResetAppDataConfirmationView : UserControl
{
    private const string FallbackTitle = "Reset app data?";
    private const string FallbackMessage = "This clears user settings and local app data, then restarts the app automatically.";

    public ResetAppDataConfirmationView()
    {
        InitializeComponent();
    }

    public static async Task<bool> ConfirmAsync()
    {
        try
        {
            var view = new ResetAppDataConfirmationView();
            var request = new DialogActionRequest
            {
                Title = FallbackTitle,
                Content = view,
                ConfirmButton = new DialogActionButton
                {
                    Text = "Reset",
                    Appearance = ControlAppearance.Danger
                },
                CancelButton = new DialogActionButton
                {
                    Text = "Cancel"
                }
            };

            var result = await DialogHelper.ShowActionDialogAsync(request);
            if (result != DialogActionOutcome.None)
                return result == DialogActionOutcome.Confirmed;

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

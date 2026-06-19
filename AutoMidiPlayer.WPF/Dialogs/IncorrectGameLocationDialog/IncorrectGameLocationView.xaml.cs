using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class IncorrectGameLocationView : UserControl
{
    private const string FallbackTitle = "Incorrect Location";
    private const string FallbackMessage = "launcher.exe is not the game executable. Please select the actual game executable.";

    public IncorrectGameLocationView()
    {
        InitializeComponent();
    }

    public static async Task ShowLauncherWarningAsync()
    {
        try
        {
            var view = new IncorrectGameLocationView();
            var request = new DialogActionRequest
            {
                Title = FallbackTitle,
                Content = view,
                CancelButton = new DialogActionButton
                {
                    Text = "OK"
                }
            };

            await DialogHelper.ShowActionDialogAsync(request);
        }
        catch (Exception dialogError)
        {
            Logger.Log("Failed to display incorrect game location dialog.");
            Logger.LogException(dialogError);
            MessageBoxHelper.ShowWarning(FallbackMessage, FallbackTitle);
        }
    }
}

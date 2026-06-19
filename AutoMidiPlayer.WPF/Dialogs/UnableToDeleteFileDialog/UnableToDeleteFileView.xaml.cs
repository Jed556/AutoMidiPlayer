using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class UnableToDeleteFileView : UserControl
{
    private const string FallbackTitle = "Unable to delete file";
    private const string FallbackMessage = "The file could not be deleted. It may be in use, read-only, or protected by permissions.";

    public UnableToDeleteFileView()
    {
        InitializeComponent();
    }

    public static async Task ShowDeleteFailedAsync()
    {
        try
        {
            var view = new UnableToDeleteFileView();
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
            Logger.Log("Failed to display unable-to-delete dialog.");
            Logger.LogException(dialogError);
            MessageBoxHelper.ShowWarning(FallbackMessage, FallbackTitle);
        }
    }
}

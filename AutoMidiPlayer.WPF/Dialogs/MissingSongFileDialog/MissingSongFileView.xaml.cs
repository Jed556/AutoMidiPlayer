using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class MissingSongFileView : UserControl
{
    public MissingSongFileView(string message)
    {
        InitializeComponent();
        MessageTextBlock.Text = message;
    }

    public static async Task ShowMissingFileAsync(string filePath)
    {
        var message =
            "The selected MIDI file could not be found:\n\n" +
            filePath +
            "\n\nIt will be moved to the Missing files list.";

        try
        {
            var view = new MissingSongFileView(message);
            var request = new DialogActionRequest
            {
                Title = "Missing MIDI file",
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
            Logger.Log("Failed to display missing MIDI file dialog.");
            Logger.LogException(dialogError);
            MessageBoxHelper.ShowWarning(message, "Missing MIDI file");
        }
    }
}

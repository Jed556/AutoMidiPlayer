using System;
using System.Windows;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class MissingSongFileDialog : ContentDialog
{
    static MissingSongFileDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MissingSongFileDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public MissingSongFileDialog(string message)
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

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
            var dialog = new MissingSongFileDialog(message);

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                await dialog.ShowAsync();
                return;
            }

            CrashLogger.Log("DialogHost was not ready while showing missing MIDI file dialog. Falling back to MessageBox.");
            MessageBoxHelper.ShowWarning(message, "Missing MIDI file");
        }
        catch (Exception dialogError)
        {
            CrashLogger.Log("Failed to display missing MIDI file dialog.");
            CrashLogger.LogException(dialogError);
            MessageBoxHelper.ShowWarning(message, "Missing MIDI file");
        }
    }
}

using System;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class IncorrectGameLocationDialog : ContentDialog
{
    private const string FallbackTitle = "Incorrect Location";
    private const string FallbackMessage = "launcher.exe is not the game executable. Please select the actual game executable.";

    static IncorrectGameLocationDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(IncorrectGameLocationDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public IncorrectGameLocationDialog()
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;
    }

    public static async Task ShowLauncherWarningAsync()
    {
        try
        {
            var dialog = new IncorrectGameLocationDialog();

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                await dialog.ShowAsync();
                return;
            }

            CrashLogger.Log("DialogHost was not ready while showing incorrect game location dialog. Falling back to MessageBox.");
            MessageBoxHelper.ShowWarning(FallbackMessage, FallbackTitle);
        }
        catch (Exception dialogError)
        {
            CrashLogger.Log("Failed to display incorrect game location dialog.");
            CrashLogger.LogException(dialogError);
            MessageBoxHelper.ShowWarning(FallbackMessage, FallbackTitle);
        }
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class ResetCompleteDialog : ContentDialog
{
    private const string FallbackTitle = "Reset complete";
    private const string FallbackMessage = "App data reset finished successfully.";

    static ResetCompleteDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ResetCompleteDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public ResetCompleteDialog()
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;
    }

    public static async Task ShowIfResetMarkerExistsAsync()
    {
        if (!File.Exists(AppPaths.ResetCompletedMarkerPath))
            return;

        try
        {
            File.Delete(AppPaths.ResetCompletedMarkerPath);
        }
        catch (IOException ex)
        {
            Logger.LogStep("RESET_MARKER_DELETE_IO_ERROR", $"path='{AppPaths.ResetCompletedMarkerPath}' | message='{ex.Message}'");
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogStep("RESET_MARKER_DELETE_AUTH_ERROR", $"path='{AppPaths.ResetCompletedMarkerPath}' | message='{ex.Message}'");
        }

        try
        {
            var dialog = new ResetCompleteDialog();

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                await dialog.ShowAsync();
                return;
            }

            Logger.Log("DialogHost was not ready while showing reset-complete dialog. Falling back to MessageBox.");
            MessageBoxHelper.ShowInformation(FallbackMessage, FallbackTitle);
        }
        catch (Exception dialogError)
        {
            Logger.Log("Failed to display reset-complete dialog.");
            Logger.LogException(dialogError);
            MessageBoxHelper.ShowInformation(FallbackMessage, FallbackTitle);
        }
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class AppStatusDialog : ContentDialog
{
    public static readonly DependencyProperty StatusTitleProperty = DependencyProperty.Register(
        nameof(StatusTitle), typeof(string), typeof(AppStatusDialog), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusMessageProperty = DependencyProperty.Register(
        nameof(StatusMessage), typeof(string), typeof(AppStatusDialog), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusIconProperty = DependencyProperty.Register(
        nameof(StatusIcon), typeof(SymbolRegular), typeof(AppStatusDialog), new PropertyMetadata(SymbolRegular.Checkmark24));

    public static readonly DependencyProperty IsUpdateStatusProperty = DependencyProperty.Register(
        nameof(IsUpdateStatus), typeof(bool), typeof(AppStatusDialog), new PropertyMetadata(false));

    public static readonly DependencyProperty OldVersionProperty = DependencyProperty.Register(
        nameof(OldVersion), typeof(string), typeof(AppStatusDialog), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty NewVersionProperty = DependencyProperty.Register(
        nameof(NewVersion), typeof(string), typeof(AppStatusDialog), new PropertyMetadata(string.Empty));

    public string StatusTitle
    {
        get => (string)GetValue(StatusTitleProperty);
        set => SetValue(StatusTitleProperty, value);
    }

    public string StatusMessage
    {
        get => (string)GetValue(StatusMessageProperty);
        set => SetValue(StatusMessageProperty, value);
    }

    public SymbolRegular StatusIcon
    {
        get => (SymbolRegular)GetValue(StatusIconProperty);
        set => SetValue(StatusIconProperty, value);
    }

    public bool IsUpdateStatus
    {
        get => (bool)GetValue(IsUpdateStatusProperty);
        set => SetValue(IsUpdateStatusProperty, value);
    }

    public string OldVersion
    {
        get => (string)GetValue(OldVersionProperty);
        set => SetValue(OldVersionProperty, value);
    }

    public string NewVersion
    {
        get => (string)GetValue(NewVersionProperty);
        set => SetValue(NewVersionProperty, value);
    }

    static AppStatusDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AppStatusDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public AppStatusDialog()
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;
            
        DialogHelper.HookButtonToPreventClose(this, Wpf.Ui.Controls.ContentDialogButton.Secondary, OnReleaseNotesPreviewClick);
    }

    private void OnReleaseNotesPreviewClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true; // Prevent the dialog from closing

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/Jed556/AutoMidiPlayer/releases",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }



    public static async Task ShowIfStatusMarkerExistsAsync()
    {
        if (!File.Exists(AppPaths.AppStatusFilePath))
            return;

        string status = string.Empty;
        try
        {
            var encryptedStatus = File.ReadAllText(AppPaths.AppStatusFilePath).Trim();
            status = Crypt.DecryptFromBase64(encryptedStatus);
            File.Delete(AppPaths.AppStatusFilePath);
        }
        catch (IOException ex)
        {
            Logger.LogStep("STATUS_MARKER_IO_ERROR", $"path='{AppPaths.AppStatusFilePath}' | message='{ex.Message}'");
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogStep("STATUS_MARKER_AUTH_ERROR", $"path='{AppPaths.AppStatusFilePath}' | message='{ex.Message}'");
            return;
        }

        var dialog = new AppStatusDialog();

        if (status.Contains("RESET", StringComparison.OrdinalIgnoreCase))
        {
            dialog.StatusTitle = "Reset";
            dialog.StatusMessage = "App Data cleared";
            dialog.StatusIcon = SymbolRegular.ArrowClockwise24;
            dialog.IsUpdateStatus = false;
        }
        else if (status.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            dialog.StatusTitle = "Updated";
            dialog.StatusIcon = SymbolRegular.ArrowDownload24;
            dialog.IsUpdateStatus = true;
            dialog.SecondaryButtonText = "Release Notes";
            dialog.SecondaryButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            
            var updateIndex = status.IndexOf("UPDATE:", StringComparison.OrdinalIgnoreCase);
            if (updateIndex >= 0)
            {
                var versionText = status.Substring(updateIndex + 7).Trim();
                // E.g. "v1.0.0 -> v2.0.0"
                var parts = versionText.Split("->");
                if (parts.Length == 2)
                {
                    dialog.OldVersion = parts[0].Trim();
                    dialog.NewVersion = parts[1].Trim();
                }
                else
                {
                    dialog.IsUpdateStatus = false;
                    dialog.StatusMessage = versionText;
                }
            }
            else
            {
                dialog.IsUpdateStatus = false;
                dialog.StatusMessage = "The application was updated to the latest version.";
            }
        }
        else
        {
            return;
        }

        try
        {
            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                await dialog.ShowAsync();
                return;
            }

            Logger.Log("DialogHost was not ready while showing app status dialog. Falling back to MessageBox.");
            MessageBoxHelper.ShowInformation(dialog.StatusMessage, dialog.StatusTitle);
        }
        catch (Exception dialogError)
        {
            Logger.Log("Failed to display app status dialog.");
            Logger.LogException(dialogError);
            MessageBoxHelper.ShowInformation(dialog.StatusMessage, dialog.StatusTitle);
        }
    }
}

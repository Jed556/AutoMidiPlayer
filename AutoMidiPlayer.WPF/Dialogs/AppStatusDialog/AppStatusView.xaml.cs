using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class AppStatusView : UserControl
{
    public static readonly DependencyProperty StatusTitleProperty = DependencyProperty.Register(
        nameof(StatusTitle), typeof(string), typeof(AppStatusView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusMessageProperty = DependencyProperty.Register(
        nameof(StatusMessage), typeof(string), typeof(AppStatusView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StatusIconProperty = DependencyProperty.Register(
        nameof(StatusIcon), typeof(SymbolRegular), typeof(AppStatusView), new PropertyMetadata(SymbolRegular.Checkmark24));

    public static readonly DependencyProperty IsUpdateStatusProperty = DependencyProperty.Register(
        nameof(IsUpdateStatus), typeof(bool), typeof(AppStatusView), new PropertyMetadata(false));

    public static readonly DependencyProperty OldVersionProperty = DependencyProperty.Register(
        nameof(OldVersion), typeof(string), typeof(AppStatusView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty NewVersionProperty = DependencyProperty.Register(
        nameof(NewVersion), typeof(string), typeof(AppStatusView), new PropertyMetadata(string.Empty));

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

    public AppStatusView()
    {
        InitializeComponent();
        DataContext = this;
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

        var view = new AppStatusView();
        DialogActionButton? secondaryAction = null;

        if (status.Contains("RESET", StringComparison.OrdinalIgnoreCase))
        {
            view.StatusTitle = "Reset";
            view.StatusMessage = "App Data cleared";
            view.StatusIcon = SymbolRegular.ArrowClockwise24;
            view.IsUpdateStatus = false;
        }
        else if (status.Contains("UPDATE", StringComparison.OrdinalIgnoreCase))
        {
            view.StatusTitle = "Updated";
            view.StatusIcon = SymbolRegular.ArrowDownload24;
            view.IsUpdateStatus = true;
            
            secondaryAction = new DialogActionButton
            {
                Text = "Release Notes",
                Appearance = ControlAppearance.Secondary,
                CallbackAsync = () =>
                {
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
                    return Task.FromResult(false); // don't close
                }
            };
            
            var updateIndex = status.IndexOf("UPDATE:", StringComparison.OrdinalIgnoreCase);
            if (updateIndex >= 0)
            {
                var versionText = status.Substring(updateIndex + 7).Trim();
                // E.g. "v1.0.0 -> v2.0.0"
                var parts = versionText.Split("->");
                if (parts.Length == 2)
                {
                    view.OldVersion = parts[0].Trim();
                    view.NewVersion = parts[1].Trim();
                }
                else
                {
                    view.IsUpdateStatus = false;
                    view.StatusMessage = versionText;
                }
            }
            else
            {
                view.IsUpdateStatus = false;
                view.StatusMessage = "The application was updated to the latest version.";
            }
        }
        else
        {
            return;
        }

        try
        {
            var request = new DialogActionRequest
            {
                Content = view,
                ConfirmButton = null,
                CancelButton = new DialogActionButton
                {
                    Text = "OK",
                    Appearance = ControlAppearance.Primary
                },
                CustomButton = secondaryAction
            };

            await DialogHelper.ShowActionDialogAsync(request);
        }
        catch (Exception dialogError)
        {
            Logger.Log("Failed to display app status dialog.");
            Logger.LogException(dialogError);
            MessageBoxHelper.ShowInformation(view.StatusMessage, view.StatusTitle);
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Git;
using AutoMidiPlayer.WPF.Helpers;
using AutoMidiPlayer.WPF.Services;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class UpdateDialog : ContentDialog, INotifyPropertyChanged
{
    private readonly GitVersion _latestVersion;
    private readonly UpdateService _updateService;
    private bool _isUpdating;
    private bool _clearAppData;
    private bool _forceRedownload;
    private string _selectedVersionType = "Portable";
    private string _progressText = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string UpdateMessage
    {
        get
        {
            var assetNameSearch = SelectedVersionType == "Portable" ? "win-x64-portable.zip" : "win-x64-net-install.zip";
            var asset = _latestVersion.Assets.FirstOrDefault(a => a.Name.Contains(assetNameSearch, StringComparison.OrdinalIgnoreCase));
            
            var msg = $"This will restart the app and update to v{_latestVersion.Version}.";
            if (asset != null && asset.Size > 0)
            {
                var mb = asset.Size / 1024.0 / 1024.0;
                msg += $"\n\nDownload size: {mb:F1} MB.";
            }
            msg += "\nIf you wish to download the update manually, click \"Release Notes\" below.";
            return msg;
        }
    }

    public ObservableCollection<string> VersionTypes { get; } = new() { "Portable", "Net-Install" };

    public string SelectedVersionType
    {
        get => _selectedVersionType;
        set
        {
            if (_selectedVersionType != value)
            {
                _selectedVersionType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UpdateMessage));
            }
        }
    }

    public bool ClearAppData
    {
        get => _clearAppData;
        set
        {
            if (_clearAppData != value)
            {
                _clearAppData = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasCachedUpdate => AutoMidiPlayer.Data.Properties.Settings.Default.AutoDownloadUpdates && File.Exists(Path.Combine(AppPaths.UpdateCacheDirectory, "update.zip")) && File.Exists(Path.Combine(AppPaths.UpdateCacheDirectory, "checksums.txt"));

    public bool ForceRedownload
    {
        get => _forceRedownload;
        set
        {
            if (_forceRedownload != value)
            {
                _forceRedownload = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsUpdating
    {
        get => _isUpdating;
        set
        {
            if (_isUpdating != value)
            {
                _isUpdating = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotUpdating));
            }
        }
    }

    public bool IsNotUpdating => !IsUpdating;

    public string ProgressText
    {
        get => _progressText;
        set
        {
            if (_progressText != value)
            {
                _progressText = value;
                OnPropertyChanged();
            }
        }
    }

    static UpdateDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(UpdateDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    private void OnReleaseNotesPreviewClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true; // Prevent the dialog from closing
        
        try
        {
            var url = "https://github.com/Jed556/AutoMidiPlayer/releases";
            if (_latestVersion != null && !string.IsNullOrEmpty(_latestVersion.Url))
                url = _latestVersion.Url;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }

    public UpdateDialog(GitVersion latestVersion, UpdateService updateService)
    {
        _latestVersion = latestVersion;
        _updateService = updateService;
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (System.Windows.Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;
            
        DataContext = this;
        
        PrimaryButtonText = "Install update";
        SecondaryButtonText = "Release Notes";
        CloseButtonText = "Cancel";

        PrimaryButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        SecondaryButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        CloseButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Secondary;

        DefaultButton = Wpf.Ui.Controls.ContentDialogButton.Primary;
        
        DialogHelper.HookButtonToPreventClose(this, Wpf.Ui.Controls.ContentDialogButton.Secondary, OnReleaseNotesPreviewClick);
        
        // Auto-detect default if possible (rudimentary check: if there is no setup/installer sign, assume portable)
        var exeName = Path.GetFileName(Environment.ProcessPath ?? "");
        if (exeName.Contains("net-install", StringComparison.OrdinalIgnoreCase))
            SelectedVersionType = "Net-Install";
    }

    public static async Task ShowAsync(GitVersion latestVersion, UpdateService updateService)
    {
        try
        {
            var dialog = new UpdateDialog(latestVersion, updateService);

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await dialog.RunUpdateAsync();
                }
            }
        }
        catch (Exception dialogError)
        {
            Logger.Log("Failed to display update dialog.");
            Logger.LogException(dialogError);
        }
    }



    private async Task RunUpdateAsync()
    {
        UpdateProgressDialog? progressDialog = null;
        try
        {
            progressDialog = new UpdateProgressDialog();
            _ = progressDialog.ShowAsync();

            var progress = new Progress<UpdateProgressInfo>(info => 
            {
                if (progressDialog != null)
                {
                    if (info.ProgressText != null) progressDialog.ProgressText = info.ProgressText;
                    if (info.ProgressDetailText != null) progressDialog.ProgressDetailText = info.ProgressDetailText;
                    progressDialog.ProgressPercentage = info.ProgressPercentage;
                    progressDialog.IsProgressIndeterminate = info.IsProgressIndeterminate;
                }
            });

            await _updateService.RunUpdateAsync(_latestVersion, SelectedVersionType, ClearAppData, ForceRedownload, progress);

            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            
            progressDialog?.CloseDialog();
            MessageBoxHelper.ShowError(ex.Message, "Update Error");
            IsUpdating = false;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

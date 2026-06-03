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
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class UpdateDialog : ContentDialog, INotifyPropertyChanged
{
    private readonly GitVersion _latestVersion;
    private bool _isUpdating;
    private bool _clearAppData;
    private string _selectedVersionType = "Portable";
    private string _progressText = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string UpdateMessage => $"This will restart the app and upgrade to v{_latestVersion.Version}.";

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

    public UpdateDialog(GitVersion latestVersion)
    {
        _latestVersion = latestVersion;
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

    public static async Task ShowAsync(GitVersion latestVersion)
    {
        try
        {
            var dialog = new UpdateDialog(latestVersion);

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
            var assetNameSearch = SelectedVersionType == "Portable" ? "win-x64-portable.zip" : "win-x64-net-install.zip";
            var asset = _latestVersion.Assets.FirstOrDefault(a => a.Name.Contains(assetNameSearch, StringComparison.OrdinalIgnoreCase));

            if (asset == null)
            {
                // Note: The dialog already closed, so we could show an error dialog instead
                System.Windows.MessageBox.Show($"Error: Could not find {SelectedVersionType} zip in the release assets.", "Update Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                IsUpdating = false;
                return;
            }

            progressDialog = new UpdateProgressDialog();
            _ = progressDialog.ShowAsync();

            progressDialog.ProgressText = "Downloading update...";
            
            var tempDir = Path.Combine(Path.GetTempPath(), "AutoMidiPlayerUpdate");
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);
            
            var zipPath = Path.Combine(tempDir, "update.zip");
            using var client = new HttpClient();
            var productInfo = new ProductInfoHeaderValue("AutoMidiPlayer", AutoMidiPlayer.WPF.ViewModels.SettingsPageViewModel.ProgramVersion.ToString());
            client.DefaultRequestHeaders.UserAgent.Add(productInfo);
            
            var response = await client.GetAsync(asset.DownloadUrl);
            response.EnsureSuccessStatusCode();
            
            await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
            {
                await response.Content.CopyToAsync(fs);
            }

            progressDialog.ProgressText = "Extracting update...";
            ZipFile.ExtractToDirectory(zipPath, tempDir, true);

            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var executablePath = Environment.ProcessPath;
            var currentProcessId = Environment.ProcessId;

            var escapedTempPath = tempDir.Replace("'", "''");
            var escapedAppPath = appDir.Replace("'", "''");
            var escapedExecutablePath = executablePath?.Replace("'", "''") ?? "";
            var escapedStatusFilePath = AppPaths.AppStatusFilePath.Replace("'", "''");
            var escapedAppDataPath = AppPaths.AppDataDirectory.Replace("'", "''");
            
            var clearDataCommand = ClearAppData 
                ? $"Remove-Item -LiteralPath '{escapedAppDataPath}' -Recurse -Force -ErrorAction SilentlyContinue; " +
                  $"New-Item -ItemType Directory -Path '{escapedAppDataPath}' -Force | Out-Null; "
                : "";

            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
            var updateStatusString = $"[{DateTime.Now:HH:mm:ss}] UPDATE: v{currentVersion} -> v{_latestVersion.Version}";

            var updateCommand = $"Start-Sleep -Seconds 1; " +
                                $"Wait-Process -Id {currentProcessId} -ErrorAction SilentlyContinue; " +
                                $"Remove-Item -Path '{escapedTempPath}\\update.zip' -Force; " +
                                clearDataCommand +
                                $"Copy-Item -Path '{escapedTempPath}\\*' -Destination '{escapedAppPath}' -Recurse -Force; " +
                                $"New-Item -ItemType Directory -Path '{escapedAppDataPath}' -Force -ErrorAction SilentlyContinue | Out-Null; " +
                                $"Set-Content -Path '{escapedStatusFilePath}' -Value '{updateStatusString}' -Force; " +
                                $"Start-Process -FilePath '{escapedExecutablePath}'; " +
                                $"Start-Sleep -Seconds 2; " +
                                $"Remove-Item -LiteralPath '{escapedTempPath}' -Recurse -Force -ErrorAction SilentlyContinue;";

            var arguments = $"-NoProfile -WindowStyle Hidden -Command \"{updateCommand}\"";

            try
            {
                StartHelperProcess("pwsh.exe", arguments);
            }
            catch (Win32Exception)
            {
                try
                {
                    StartHelperProcess("powershell.exe", arguments);
                }
                catch (Win32Exception)
                {
                    progressDialog?.CloseDialog();
                    System.Windows.MessageBox.Show("Failed to start PowerShell for update.", "Update Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    IsUpdating = false;
                    return;
                }
            }

            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            
            progressDialog?.CloseDialog();
            System.Windows.MessageBox.Show("Update failed: " + ex.Message, "Update Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            IsUpdating = false;
        }
    }

    private static void StartHelperProcess(string shellPath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = shellPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Failed to start helper process.");
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

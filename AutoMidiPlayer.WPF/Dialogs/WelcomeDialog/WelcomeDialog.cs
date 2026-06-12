using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class WelcomeDialog : ContentDialog
{
    public string AppVersion => $"Version {ViewModels.SettingsPageViewModel.ProgramVersionDisplay}";

    static WelcomeDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WelcomeDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public WelcomeDialog()
    {
        InitializeComponent();

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;
            
        // Default to true as before
        TelemetryToggle.IsChecked = true;
    }

    private void OnWikiClicked(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/Jed556/AutoMidiPlayer/wiki",
            UseShellExecute = true
        });
    }

    public bool ReopenRequested { get; private set; }
    public ViewModels.ThirdPartyLicense? PendingLicense { get; private set; }

    private void OnLicenseClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string licenseText = "";
            var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE");
            if (System.IO.File.Exists(path))
                licenseText = System.IO.File.ReadAllText(path);
            else
            {
                path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LICENSE");
                if (System.IO.File.Exists(path))
                    licenseText = System.IO.File.ReadAllText(path);
            }

            PendingLicense = new ViewModels.ThirdPartyLicense("Auto MIDI Player", ViewModels.SettingsPageViewModel.ProgramVersionDisplay, "GNU GPL v3.0", licenseText);
            
            ReopenRequested = true;
            this.Hide();
        }
        catch (Exception ex)
        {
            Data.Logger.LogException(ex);
        }
    }

    private void OnStartPlayingClicked(object sender, RoutedEventArgs e)
    {
        this.Hide();
    }

    /// <summary>
    /// Shows the Welcome dialog if it has not been shown before.
    /// Returns true if the dialog was shown, false if it was skipped.
    /// </summary>
    public static async System.Threading.Tasks.Task<bool> ShowIfFirstLaunchAsync()
    {
        if (Settings.Default.HasShownFirstLaunch)
            return false;

        var dialog = new WelcomeDialog();

        // Ensure host is ready during early startup
        if (!await DialogHelper.EnsureDialogHostAsync(dialog))
        {
            // If we can't show it, fall back to assuming they opted in (the default)
            Settings.Default.HasShownFirstLaunch = true;
            Settings.Default.Save();
            return false;
        }

        while (true)
        {
            dialog.ReopenRequested = false;
            await dialog.ShowAsync();

            if (dialog.ReopenRequested && dialog.PendingLicense != null)
            {
                await ThirdPartyLicenseDialog.ShowAsync(dialog.PendingLicense);
                continue; // Loop back and await dialog.ShowAsync() again!
            }

            break; // The user clicked "Start Playing!" or otherwise closed the dialog
        }

        Settings.Default.TelemetryOptIn = dialog.TelemetryToggle.IsChecked ?? true;
        Settings.Default.HasShownFirstLaunch = true;
        Settings.Default.Save();

        try
        {
            new AutoMidiPlayer.WPF.Services.SentryService().SetTelemetryEnabled(Settings.Default.TelemetryOptIn);
        }
        catch { }

        return true;
    }
}

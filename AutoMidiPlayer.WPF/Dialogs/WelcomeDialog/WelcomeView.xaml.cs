using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class WelcomeView : UserControl
{
    public string AppVersion => $"Version {ViewModels.SettingsPageViewModel.ProgramVersionDisplay}";

    public WelcomeView()
    {
        InitializeComponent();
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
        }
        catch (Exception ex)
        {
            Data.Logger.LogException(ex);
        }
    }

    /// <summary>
    /// Shows the Welcome dialog if it has not been shown before.
    /// Returns true if the dialog was shown, false if it was skipped.
    /// </summary>
    public static async Task<bool> ShowIfFirstLaunchAsync()
    {
        if (Settings.Default.HasShownFirstLaunch)
            return false;

        var view = new WelcomeView();

        while (true)
        {
            view.ReopenRequested = false;

            var request = new DialogActionRequest
            {
                Content = view,
                DialogMaxHeight = 600,
                ConfirmButton = new DialogActionButton
                {
                    Text = "Start Playing!",
                    Appearance = ControlAppearance.Primary,
                    CallbackAsync = () => Task.FromResult(true) // success, allow close
                },
                CancelButton = null,
                HideFooter = false
            };

            var outcome = await DialogHelper.ShowActionDialogAsync(request);

            if (view.ReopenRequested && view.PendingLicense != null)
            {
                await ThirdPartyLicenseView.ShowAsync(view.PendingLicense);
                continue;
            }

            break;
        }

        Settings.Default.TelemetryOptIn = view.TelemetryToggle.IsChecked ?? true;
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

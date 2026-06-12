using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using AutoMidiPlayer.WPF.ViewModels;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class ThirdPartyLicenseDialog : ContentDialog
{
    static ThirdPartyLicenseDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ThirdPartyLicenseDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public ThirdPartyLicenseDialog(ThirdPartyLicense license)
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                           ?? Application.Current.MainWindow;

        if (activeWindow != null)
        {
            void UpdateDialogBounds()
            {
                var maxHeight = Math.Max(0, activeWindow.ActualHeight * 0.80);
                var maxWidth = Math.Max(0, activeWindow.ActualWidth * 0.60);
                DialogMaxHeight = maxHeight;
                DialogMaxWidth = maxWidth;
                MaxHeight = maxHeight;
                MaxWidth = maxWidth;
                Width = maxWidth;
                DialogMargin = new Thickness(24);
            }

            UpdateDialogBounds();
            SizeChangedEventHandler? sizeChangedHandler = (_, _) => UpdateDialogBounds();
            activeWindow.SizeChanged += sizeChangedHandler;
            EventHandler? stateChangedHandler = (_, _) => UpdateDialogBounds();
            activeWindow.StateChanged += stateChangedHandler;

            Closed += (_, _) =>
            {
                activeWindow.SizeChanged -= sizeChangedHandler;
                activeWindow.StateChanged -= stateChangedHandler;
            };
        }

        DataContext = license;
    }

    public static async Task ShowAsync(ThirdPartyLicense license)
    {
        try
        {
            var dialog = new ThirdPartyLicenseDialog(license);

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
                await dialog.ShowAsync();
        }
        catch (Exception error)
        {
            Logger.LogException(error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}

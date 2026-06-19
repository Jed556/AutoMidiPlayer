using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using AutoMidiPlayer.WPF.ViewModels;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class ThirdPartyLicenseView : UserControl
{
    public ThirdPartyLicenseView(ThirdPartyLicense license)
    {
        InitializeComponent();
        DataContext = license;
    }

    public static async Task ShowAsync(ThirdPartyLicense license)
    {
        try
        {
            var view = new ThirdPartyLicenseView(license);
            var request = new DialogActionRequest
            {
                Title = license.Name,
                Subtitle = license.DialogSubtitle,
                Content = view,
                CancelButton = null,
                ConfirmButton = null,
                HideFooter = true, // No footer buttons needed
                TopRightButton = new DialogTopRightButton
                {
                    Icon = Wpf.Ui.Controls.SymbolRegular.Dismiss24,
                    CallbackAsync = () => Task.FromResult(true)
                }
            };
            await DialogHelper.ShowActionDialogAsync(request);
        }
        catch (Exception error)
        {
            Logger.LogException(error);
        }
    }
}

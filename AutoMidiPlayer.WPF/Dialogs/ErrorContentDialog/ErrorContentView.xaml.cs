using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class ErrorContentView : UserControl
{
    public ErrorContentView(Exception e)
    {
        InitializeComponent();
        MessageTextBlock.Text = e.Message;
    }

    public static async Task<DialogActionOutcome> ShowAsync(Exception e, IReadOnlyCollection<Enum>? options = null, string? closeText = null)
    {
        var view = new ErrorContentView(e);
        var request = new DialogActionRequest
        {
            Title = e.GetType().Name,
            Content = view
        };

        var primaryText = options?.ElementAtOrDefault(0)?.ToString()?.Humanize() ?? string.Empty;
        var secondaryText = options?.ElementAtOrDefault(1)?.ToString()?.Humanize() ?? string.Empty;

        if (!string.IsNullOrEmpty(primaryText))
        {
            request.ConfirmButton = new DialogActionButton
            {
                Text = primaryText,
                Appearance = ControlAppearance.Primary
            };
        }
        else
        {
            request.ConfirmButton = null;
        }

        if (!string.IsNullOrEmpty(secondaryText))
        {
            request.CustomButton = new DialogActionButton
            {
                Text = secondaryText,
                Appearance = ControlAppearance.Secondary
            };
        }

        request.CancelButton = new DialogActionButton
        {
            Text = closeText ?? "Abort",
            Appearance = ControlAppearance.Secondary
        };

        return await DialogHelper.ShowActionDialogAsync(request);
    }
}

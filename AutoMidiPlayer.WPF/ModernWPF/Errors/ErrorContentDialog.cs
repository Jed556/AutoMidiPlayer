using System;
using System.Collections.Generic;
using System.Linq;
using Humanizer;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.ModernWPF.Errors;

public class ErrorContentDialog : ContentDialog
{
    public ErrorContentDialog(Exception e, IReadOnlyCollection<Enum>? options = null, string? closeText = null)
    {
        Title = e.GetType().Name;
        Content = e.Message;

        PrimaryButtonText = options?.ElementAtOrDefault(0)?.ToString()?.Humanize() ?? string.Empty;
        SecondaryButtonText = options?.ElementAtOrDefault(1)?.ToString()?.Humanize() ?? string.Empty;

        CloseButtonText = closeText ?? "Abort";
    }
}

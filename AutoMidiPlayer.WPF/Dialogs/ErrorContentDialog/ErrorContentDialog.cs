using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Humanizer;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class ErrorContentDialog : ContentDialog
{
    static ErrorContentDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ErrorContentDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public ErrorContentDialog(Exception e, IReadOnlyCollection<Enum>? options = null, string? closeText = null)
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

        Title = e.GetType().Name;
        MessageTextBlock.Text = e.Message;

        PrimaryButtonText = options?.ElementAtOrDefault(0)?.ToString()?.Humanize() ?? string.Empty;
        SecondaryButtonText = options?.ElementAtOrDefault(1)?.ToString()?.Humanize() ?? string.Empty;
        CloseButtonText = closeText ?? "Abort";
    }
}

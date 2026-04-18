using System;
using System.Windows;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class ActionDialog : ContentDialog
{
    static ActionDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ActionDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public ActionDialog(DialogActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

        HeaderText = request.Title;
        DialogBody = request.Body;
        AdditionalContent = request.Content;
        DialogIcon = request.Icon;

        if (DialogIcon.HasValue)
            HeaderIcon.Symbol = DialogIcon.Value;

        DataContext = this;
    }

    public string HeaderText { get; }

    public string? DialogBody { get; }

    public object? AdditionalContent { get; }

    public SymbolRegular? DialogIcon { get; }

    public bool HasHeader => !string.IsNullOrWhiteSpace(HeaderText) || HasIcon;

    public bool HasBody => !string.IsNullOrWhiteSpace(DialogBody);

    public bool HasAdditionalContent => AdditionalContent is not null;

    public bool HasIcon => DialogIcon.HasValue;
}

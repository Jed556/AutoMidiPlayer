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

        if (request.ConfirmButton is not null)
        {
            ConfirmBtn.Content = request.ConfirmButton.Text;
            ConfirmBtn.Visibility = Visibility.Visible;
            ConfirmBtn.Click += (_, _) => { Result = DialogActionOutcome.Confirmed; Hide(); };
        }
        else
            ConfirmBtn.Visibility = Visibility.Collapsed;

        if (request.CustomButton is not null)
        {
            CustomBtn.Content = request.CustomButton.Text;
            CustomBtn.Visibility = Visibility.Visible;
            CustomBtn.Click += (_, _) => { Result = DialogActionOutcome.Custom; Hide(); };
        }
        else
            CustomBtn.Visibility = Visibility.Collapsed;

        if (request.CancelButton is not null)
        {
            CancelBtn.Content = request.CancelButton.Text;
            CancelBtn.Visibility = Visibility.Visible;
            CancelBtn.Click += (_, _) => { Result = DialogActionOutcome.Cancelled; Hide(); };
        }
        else
            CancelBtn.Visibility = Visibility.Collapsed;

        DataContext = this;
    }

    public DialogActionOutcome Result { get; private set; } = DialogActionOutcome.Cancelled;

    public string HeaderText { get; }

    public string? DialogBody { get; }

    public object? AdditionalContent { get; }

    public SymbolRegular? DialogIcon { get; }

    public bool HasHeader => !string.IsNullOrWhiteSpace(HeaderText) || HasIcon;

    public bool HasBody => !string.IsNullOrWhiteSpace(DialogBody);

    public bool HasAdditionalContent => AdditionalContent is not null;

    public bool HasIcon => DialogIcon.HasValue;
}

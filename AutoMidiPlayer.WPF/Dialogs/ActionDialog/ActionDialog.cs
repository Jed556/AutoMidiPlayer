using System;
using System.Windows;
using System.Windows.Controls;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

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

        DialogBody = request.Body;
        AdditionalContent = request.Content;

        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
        if (request.Icon.HasValue)
        {
            titlePanel.Children.Add(new SymbolIcon
            {
                Symbol = request.Icon.Value,
                Margin = new Thickness(0, 0, 8, 0)
            });
        }
        
        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            titlePanel.Children.Add(new TextBlock
            {
                Text = request.Title,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
        }
        
        if (titlePanel.Children.Count > 0)
        {
            Title = titlePanel;
        }

        if (request.ConfirmButton is not null)
        {
            PrimaryButtonText = request.ConfirmButton.Text;
            PrimaryButtonAppearance = request.ConfirmButton.Appearance;
        }

        if (request.CustomButton is not null)
        {
            SecondaryButtonText = request.CustomButton.Text;
            SecondaryButtonAppearance = request.CustomButton.Appearance;
        }

        if (request.CancelButton is not null)
        {
            CloseButtonText = request.CancelButton.Text;
            CloseButtonAppearance = request.CancelButton.Appearance;
        }

        DataContext = this;
    }

    public string? DialogBody { get; }

    public object? AdditionalContent { get; }

    public bool HasBody => !string.IsNullOrWhiteSpace(DialogBody);

    public bool HasAdditionalContent => AdditionalContent is not null;
}


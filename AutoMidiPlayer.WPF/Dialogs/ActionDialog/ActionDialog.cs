using System;
using System.Linq;
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

        DialogHelper.SetupDialogHost(this);
        InitializeComponent();

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

        var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                           ?? Application.Current.MainWindow;

        if (activeWindow != null)
        {
            void UpdateDialogBounds()
            {
                if (request.DialogMaxHeight.HasValue)
                {
                    DialogMaxHeight = request.DialogMaxHeight.Value + 140;
                }
                else
                {
                    DialogMaxHeight = Math.Max(0, activeWindow.ActualHeight * 0.7);
                }

                if (request.DialogMaxWidth.HasValue)
                {
                    DialogMaxWidth = request.DialogMaxWidth.Value;
                }
                else
                {
                    DialogMaxWidth = Math.Max(0, activeWindow.ActualWidth * 0.5);
                }
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

        DialogBody = request.Body;
        AdditionalContent = request.Content;

        var titleGrid = new Grid();
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        leftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        leftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        
        if (request.Icon.HasValue)
        {
            var icon = new SymbolIcon
            {
                Symbol = request.Icon.Value,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 2),
                FontSize = 20
            };
            Grid.SetRow(icon, 0);
            Grid.SetColumn(icon, 0);
            leftGrid.Children.Add(icon);
        }

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            var title = new TextBlock
            {
                Text = request.Title,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 20
            };
            Grid.SetRow(title, 0);
            Grid.SetColumn(title, request.Icon.HasValue ? 1 : 0);
            if (!request.Icon.HasValue) Grid.SetColumnSpan(title, 2);
            leftGrid.Children.Add(title);
        }

        if (!string.IsNullOrWhiteSpace(request.Subtitle))
        {
            var subtitle = new TextBlock
            {
                Text = request.Subtitle,
                FontSize = 13,
                Opacity = 0.6,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(subtitle, 1);
            Grid.SetColumn(subtitle, 0);
            Grid.SetColumnSpan(subtitle, 2);
            leftGrid.Children.Add(subtitle);
        }

        Grid.SetColumn(leftGrid, 0);
        titleGrid.Children.Add(leftGrid);

        if (request.TopRightButton is not null)
        {
            var trButton = new Wpf.Ui.Controls.Button
            {
                Icon = new SymbolIcon { Symbol = request.TopRightButton.Icon },
                Appearance = ControlAppearance.Transparent,
                Margin = new Thickness(16, -16, -16, 0),
                VerticalAlignment = VerticalAlignment.Top
            };

            trButton.Click += async (s, e) =>
            {
                if (request.TopRightButton.CallbackAsync is not null)
                {
                    bool shouldClose = await request.TopRightButton.CallbackAsync();
                    if (!shouldClose) return;
                }
                Hide();
            };

            Grid.SetColumn(trButton, 1);
            titleGrid.Children.Add(trButton);
        }

        Title = titleGrid;

        var buttonsToHide = new System.Collections.Generic.List<ContentDialogButton>();

        if (request.ConfirmButton is not null && !request.HideFooter)
        {
            PrimaryButtonText = request.ConfirmButton.Text;
            PrimaryButtonAppearance = request.ConfirmButton.Appearance;
        }
        else
        {
            PrimaryButtonText = string.Empty;
            buttonsToHide.Add(ContentDialogButton.Primary);
        }

        if (request.CustomButton is not null && !request.HideFooter)
        {
            SecondaryButtonText = request.CustomButton.Text;
            SecondaryButtonAppearance = request.CustomButton.Appearance;
        }
        else
        {
            SecondaryButtonText = string.Empty;
            buttonsToHide.Add(ContentDialogButton.Secondary);
        }

        if (request.CancelButton is not null && !request.HideFooter)
        {
            CloseButtonText = request.CancelButton.Text;
            CloseButtonAppearance = request.CancelButton.Appearance;
        }
        else
        {
            CloseButtonText = string.Empty;
            buttonsToHide.Add(ContentDialogButton.Close);
        }

        if (buttonsToHide.Count > 0)
        {
            Loaded += (_, _) =>
            {
                foreach (var buttonType in buttonsToHide)
                    DialogHelper.CollapseDialogButton(this, buttonType);
            };
        }

        DataContext = this;
    }

    public string? DialogBody { get; }

    public object? AdditionalContent { get; }

    public bool HasBody => !string.IsNullOrWhiteSpace(DialogBody);

    public bool HasAdditionalContent => AdditionalContent is not null;
}


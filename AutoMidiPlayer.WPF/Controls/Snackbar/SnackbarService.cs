using System;
using System.Collections.Generic;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Controls.Snackbar;

/// <summary>
/// Static convenience API for showing snackbar notifications from anywhere in the application.
/// Assign <see cref="Container"/> once during startup (from MainWindow's code-behind or ViewModel).
/// </summary>
public static class SnackbarService
{
    /// <summary>The active snackbar container. Must be set before any Show calls.</summary>
    public static SnackbarContainer? Container { get; set; }

    public static TimeSpan DefaultDuration { get; set; } = TimeSpan.FromSeconds(4);

    public static void Show(
        string primaryText,
        string? secondaryText = null,
        SnackbarSeverity severity = SnackbarSeverity.Info,
        TimeSpan? duration = null,
        bool showProgressBar = true,
        bool showCloseButton = true,
        SymbolRegular? iconSymbol = null,
        ImageSource? iconSource = null,
        IList<SnackbarActionButton>? actionButtons = null)
    {
        if (Container is null)
            return;

        var item = new SnackbarItem
        {
            Severity = severity,
            PrimaryText = primaryText,
            SecondaryText = secondaryText ?? string.Empty,
            Duration = duration ?? DefaultDuration,
            ShowProgressBar = showProgressBar,
            ShowCloseButton = showCloseButton,
            ActionButtons = actionButtons
        };

        if (iconSymbol.HasValue)
            item.IconSymbol = iconSymbol.Value;

        if (iconSource is not null)
            item.IconSource = iconSource;

        Container.Show(item);
    }

    /// <summary>Show an informational snackbar.</summary>
    public static void Info(string text, string? secondary = null, TimeSpan? duration = null,
        bool showProgressBar = true, bool showCloseButton = true,
        IList<SnackbarActionButton>? actionButtons = null)
        => Show(text, secondary, SnackbarSeverity.Info, duration, showProgressBar, showCloseButton,
            actionButtons: actionButtons);

    /// <summary>Show a success snackbar.</summary>
    public static void Success(string text, string? secondary = null, TimeSpan? duration = null,
        bool showProgressBar = true, bool showCloseButton = true,
        IList<SnackbarActionButton>? actionButtons = null)
        => Show(text, secondary, SnackbarSeverity.Success, duration, showProgressBar, showCloseButton,
            actionButtons: actionButtons);

    /// <summary>Show a warning snackbar.</summary>
    public static void Warning(string text, string? secondary = null, TimeSpan? duration = null,
        bool showProgressBar = true, bool showCloseButton = true,
        IList<SnackbarActionButton>? actionButtons = null)
        => Show(text, secondary, SnackbarSeverity.Warning, duration, showProgressBar, showCloseButton,
            actionButtons: actionButtons);

    /// <summary>Show a danger/error snackbar.</summary>
    public static void Danger(string text, string? secondary = null, TimeSpan? duration = null,
        bool showProgressBar = true, bool showCloseButton = true,
        IList<SnackbarActionButton>? actionButtons = null)
        => Show(text, secondary, SnackbarSeverity.Danger, duration, showProgressBar, showCloseButton,
            actionButtons: actionButtons);
}

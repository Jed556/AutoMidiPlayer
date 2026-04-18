using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class MissingGameLocationsDialog : ContentDialog
{
    static MissingGameLocationsDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(MissingGameLocationsDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public MissingGameLocationsDialog(IEnumerable<string> missingGames)
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

        var normalizedGames = NormalizeMissingGames(missingGames);
        MissingGamesTextBlock.Text = BuildMissingGamesList(normalizedGames);
    }

    public static async Task<bool> ShowForMissingGamesAsync(IEnumerable<string> missingGames)
    {
        var normalizedGames = NormalizeMissingGames(missingGames);
        if (normalizedGames.Count == 0)
            return false;

        var gameList = string.Join(", ", normalizedGames);
        var message = $"Could not find game executable locations for: {gameList}. You can set game paths in Settings.";

        try
        {
            var dialog = new MissingGameLocationsDialog(normalizedGames);

            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (hostReady)
            {
                var result = await dialog.ShowAsync();
                return result == ContentDialogResult.Primary;
            }

            CrashLogger.Log("DialogHost was not ready while showing missing game location dialog. Falling back to MessageBox.");
        }
        catch (Exception dialogError)
        {
            CrashLogger.Log("Failed to display missing game location dialog.");
            CrashLogger.LogException(dialogError);
        }

        return MessageBoxHelper.ConfirmOkCancel(
            message,
            "Error",
            System.Windows.MessageBoxImage.Warning);
    }

    private static List<string> NormalizeMissingGames(IEnumerable<string> missingGames)
    {
        return missingGames
            .Where(game => !string.IsNullOrWhiteSpace(game))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildMissingGamesList(IEnumerable<string> missingGames)
    {
        var normalizedGames = NormalizeMissingGames(missingGames);

        return normalizedGames.Count == 0
            ? "- Unknown game"
            : string.Join(Environment.NewLine, normalizedGames.Select(game => $"- {game}"));
    }
}

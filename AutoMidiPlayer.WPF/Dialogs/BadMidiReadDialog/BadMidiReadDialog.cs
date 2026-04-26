using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Humanizer;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class BadMidiReadDialog : ContentDialog
{
    private static readonly object DialogDedupLock = new();
    private static readonly HashSet<string> ShownErrorKeys = new(StringComparer.Ordinal);

    private static readonly Dictionary<Type, List<Enum>> ExceptionOptions = new()
    {
        [typeof(InvalidChannelEventParameterValueException)] =
        [
            InvalidChannelEventParameterValuePolicy.SnapToLimits,
            InvalidChannelEventParameterValuePolicy.ReadValid
        ],
        [typeof(InvalidMetaEventParameterValueException)] =
        [
            InvalidMetaEventParameterValuePolicy.SnapToLimits
        ],
        [typeof(InvalidSystemCommonEventParameterValueException)] =
        [
            InvalidSystemCommonEventParameterValuePolicy.SnapToLimits
        ],
        [typeof(UnknownChunkException)] =
        [
            UnknownChunkIdPolicy.ReadAsUnknownChunk,
            UnknownChunkIdPolicy.Skip
        ],
        [typeof(InvalidChunkSizeException)] =
        [
            InvalidChunkSizePolicy.Ignore
        ],
        [typeof(MissedEndOfTrackEventException)] =
        [
            MissedEndOfTrackPolicy.Ignore
        ],
        [typeof(NoHeaderChunkException)] =
        [
            NoHeaderChunkPolicy.Ignore
        ],
        [typeof(NotEnoughBytesException)] =
        [
            NotEnoughBytesPolicy.Ignore
        ],
        [typeof(UnexpectedTrackChunksCountException)] =
        [
            UnexpectedTrackChunksCountPolicy.Ignore
        ],
        [typeof(UnknownChannelEventException)] =
        [
            UnknownChannelEventPolicy.SkipStatusByte
        ],
        [typeof(UnknownFileFormatException)] =
        [
            UnknownFileFormatPolicy.Ignore
        ]
    };

    private static readonly IReadOnlyList<Type> FatalExceptions =
    [
        typeof(InvalidMidiTimeCodeComponentException),
        typeof(TooManyTrackChunksException),
        typeof(UnexpectedRunningStatusException)
    ];

    static BadMidiReadDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(BadMidiReadDialog),
            new FrameworkPropertyMetadata(typeof(ContentDialog))
        );
    }

    public BadMidiReadDialog(string message, string primaryButtonText, string secondaryButtonText)
    {
        InitializeComponent();

        DialogHelper.SetupDialogHost(this);

        if (Application.Current.TryFindResource(typeof(ContentDialog)) is Style dialogStyle)
            Style = dialogStyle;

        MessageTextBlock.Text = message;
        PrimaryButtonText = primaryButtonText;
        SecondaryButtonText = secondaryButtonText;
    }

    internal static async Task<bool> TryHandleAsync(Exception e, ReadingSettings settings, string? filePath = null)
    {
        Logger.Log($"Bad MIDI read error{(string.IsNullOrWhiteSpace(filePath) ? string.Empty : $" for '{filePath}'")}");
        Logger.LogException(e);

        var errorKey = BuildErrorKey(e);
        if (IsDuplicateError(errorKey))
        {
            Logger.Log("Duplicate bad MIDI error detected; suppressing additional dialog.");
            return false;
        }

        var command = ExceptionOptions
            .FirstOrDefault(type => type.Key.Equals(e.GetType())).Value;

        var dialog = new BadMidiReadDialog(
            BuildContent(e, filePath),
            command?.ElementAtOrDefault(0)?.ToString()?.Humanize() ?? string.Empty,
            command?.ElementAtOrDefault(1)?.ToString()?.Humanize() ?? string.Empty);

        ContentDialogResult result;

        try
        {
            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (!hostReady)
            {
                Logger.Log("DialogHost was not ready for bad MIDI dialog; skipping file.");
                return false;
            }

            result = await dialog.ShowAsync();
        }
        catch (Exception dialogException)
        {
            Logger.Log("Failed to show bad MIDI dialog.");
            Logger.LogException(dialogException);
            return false;
        }

        if (result == ContentDialogResult.None || FatalExceptions.Contains(e.GetType()))
            return false;

        var option = result switch
        {
            ContentDialogResult.Primary => command?.ElementAtOrDefault(0),
            ContentDialogResult.Secondary => command?.ElementAtOrDefault(1),
            _ => null
        };

        if (option is null) return false;

        switch (e)
        {
            case InvalidChannelEventParameterValueException:
                settings.InvalidChannelEventParameterValuePolicy = (InvalidChannelEventParameterValuePolicy)option;
                break;
            case InvalidMetaEventParameterValueException:
                settings.InvalidMetaEventParameterValuePolicy = (InvalidMetaEventParameterValuePolicy)option;
                break;
            case InvalidSystemCommonEventParameterValueException:
                settings.InvalidSystemCommonEventParameterValuePolicy
                    = (InvalidSystemCommonEventParameterValuePolicy)option;
                break;
            case UnknownChannelEventException:
                settings.UnknownChannelEventPolicy = (UnknownChannelEventPolicy)option;
                break;
            case UnknownChunkException:
                settings.UnknownChunkIdPolicy = (UnknownChunkIdPolicy)option;
                break;
            case InvalidChunkSizeException:
                settings.InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore;
                break;
            case MissedEndOfTrackEventException:
                settings.MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore;
                break;
            case NoHeaderChunkException:
                settings.NoHeaderChunkPolicy = NoHeaderChunkPolicy.Ignore;
                break;
            case NotEnoughBytesException:
                settings.NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore;
                break;
            case UnexpectedTrackChunksCountException:
                settings.UnexpectedTrackChunksCountPolicy = UnexpectedTrackChunksCountPolicy.Ignore;
                break;
            case UnknownFileFormatException:
                settings.UnknownFileFormatPolicy = UnknownFileFormatPolicy.Ignore;
                break;
            case InvalidMidiTimeCodeComponentException:
            case TooManyTrackChunksException:
            case UnexpectedRunningStatusException:
                return false;
        }

        return true;
    }

    private static string BuildErrorKey(Exception e)
    {
        return $"{e.GetType().FullName}|{e.Message}";
    }

    private static bool IsDuplicateError(string errorKey)
    {
        lock (DialogDedupLock)
        {
            return !ShownErrorKeys.Add(errorKey);
        }
    }

    private static string BuildContent(Exception e, string? filePath)
    {
        var fileText = string.IsNullOrWhiteSpace(filePath)
            ? "The MIDI file could not be read."
            : $"File:\n{filePath}";

        return $"{fileText}\n\nError:\n{e.Message}";
    }
}

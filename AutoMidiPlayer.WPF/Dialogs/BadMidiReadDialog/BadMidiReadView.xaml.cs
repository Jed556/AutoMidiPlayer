using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Helpers;
using Humanizer;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

public partial class BadMidiReadView : UserControl
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

    public BadMidiReadView(string message)
    {
        InitializeComponent();
        MessageTextBlock.Text = message;
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

        var view = new BadMidiReadView(BuildContent(e, filePath));

        string primaryButtonText = command?.ElementAtOrDefault(0)?.ToString()?.Humanize() ?? string.Empty;
        string secondaryButtonText = command?.ElementAtOrDefault(1)?.ToString()?.Humanize() ?? string.Empty;

        var request = new DialogActionRequest
        {
            Title = "Bad MIDI file",
            Content = view,
            CancelButton = new DialogActionButton
            {
                Text = "Skip",
                Appearance = ControlAppearance.Secondary
            }
        };

        if (!string.IsNullOrEmpty(primaryButtonText))
        {
            request.ConfirmButton = new DialogActionButton
            {
                Text = primaryButtonText,
                Appearance = ControlAppearance.Primary
            };
        }

        if (!string.IsNullOrEmpty(secondaryButtonText))
        {
            request.CustomButton = new DialogActionButton
            {
                Text = secondaryButtonText,
                Appearance = ControlAppearance.Secondary
            };
        }

        DialogActionOutcome result;

        try
        {
            result = await DialogHelper.ShowActionDialogAsync(request);
        }
        catch (Exception dialogException)
        {
            Logger.Log("Failed to show bad MIDI dialog.");
            Logger.LogException(dialogException);
            return false;
        }

        if (result == DialogActionOutcome.Cancelled || FatalExceptions.Contains(e.GetType()))
            return false;

        var option = result switch
        {
            DialogActionOutcome.Confirmed => command?.ElementAtOrDefault(0),
            DialogActionOutcome.Custom => command?.ElementAtOrDefault(1),
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

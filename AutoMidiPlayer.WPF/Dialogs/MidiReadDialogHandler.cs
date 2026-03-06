using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using Humanizer;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF.Dialogs;

internal static class MidiReadDialogHandler
{
    private static readonly object DialogDedupLock = new();
    private static readonly HashSet<string> ShownErrorKeys = new(StringComparer.Ordinal);

    private static readonly Dictionary<Type, List<Enum>> ExceptionOptions = new()
    {
        [typeof(InvalidChannelEventParameterValueException)] = new()
        {
            InvalidChannelEventParameterValuePolicy.SnapToLimits,
            InvalidChannelEventParameterValuePolicy.ReadValid
        },
        [typeof(InvalidMetaEventParameterValueException)] = new()
        {
            InvalidMetaEventParameterValuePolicy.SnapToLimits
        },
        [typeof(InvalidSystemCommonEventParameterValueException)] = new()
        {
            InvalidSystemCommonEventParameterValuePolicy.SnapToLimits
        },
        [typeof(UnknownChunkException)] = new()
        {
            UnknownChunkIdPolicy.ReadAsUnknownChunk,
            UnknownChunkIdPolicy.Skip
        },
        [typeof(InvalidChunkSizeException)] = new()
        {
            InvalidChunkSizePolicy.Ignore
        },
        [typeof(MissedEndOfTrackEventException)] = new()
        {
            MissedEndOfTrackPolicy.Ignore
        },
        [typeof(NoHeaderChunkException)] = new()
        {
            NoHeaderChunkPolicy.Ignore
        },
        [typeof(NotEnoughBytesException)] = new()
        {
            NotEnoughBytesPolicy.Ignore
        },
        [typeof(UnexpectedTrackChunksCountException)] = new()
        {
            UnexpectedTrackChunksCountPolicy.Ignore
        },
        [typeof(UnknownChannelEventException)] = new()
        {
            UnknownChannelEventPolicy.SkipStatusByte
        },
        [typeof(UnknownFileFormatException)] = new()
        {
            UnknownFileFormatPolicy.Ignore
        }
    };

    private static readonly IReadOnlyList<Type> FatalExceptions = new List<Type>
    {
        typeof(InvalidMidiTimeCodeComponentException),
        typeof(TooManyTrackChunksException),
        typeof(UnexpectedRunningStatusException)
    };

    public static async Task<bool> TryHandleAsync(Exception e, ReadingSettings settings, string? filePath = null)
    {
        CrashLogger.Log($"Bad MIDI read error{(string.IsNullOrWhiteSpace(filePath) ? string.Empty : $" for '{filePath}'")}");
        CrashLogger.LogException(e);

        var errorKey = BuildErrorKey(e);
        if (IsDuplicateError(errorKey))
        {
            CrashLogger.Log("Duplicate bad MIDI error detected; suppressing additional dialog.");
            return false;
        }

        var command = ExceptionOptions
            .FirstOrDefault(type => type.Key.Equals(e.GetType())).Value;

        var dialog = DialogHelper.CreateDialog();
        dialog.Title = "Bad MIDI file";
        dialog.Content = BuildContent(e, filePath);
        dialog.CloseButtonText = "Skip";
        dialog.PrimaryButtonText = command?.ElementAtOrDefault(0)?.ToString()?.Humanize() ?? string.Empty;
        dialog.SecondaryButtonText = command?.ElementAtOrDefault(1)?.ToString()?.Humanize() ?? string.Empty;

        ContentDialogResult result;

        try
        {
            var hostReady = await DialogHelper.EnsureDialogHostAsync(dialog);
            if (!hostReady)
            {
                CrashLogger.Log("DialogHost was not ready for bad MIDI dialog; skipping file.");
                return false;
            }

            result = await dialog.ShowAsync();
        }
        catch (Exception dialogException)
        {
            CrashLogger.Log("Failed to show bad MIDI dialog.");
            CrashLogger.LogException(dialogException);
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

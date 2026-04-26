using System;
using System.IO;
using System.Runtime.CompilerServices;
using AutoMidiPlayer.Data.Properties;

namespace AutoMidiPlayer.Data;

public static class Logger
{
    public const int ErrorsOnlyVerbosity = 0;
    public const int WarningsAndErrorsVerbosity = 1;
    public const int AllStepsVerbosity = 2;

    private static readonly string AppLogPath = AppPaths.AppLogPath;
    private static readonly string MidiParserLogPath = AppPaths.MidiParserLogPath;
    private static readonly string PlaybackLogPath = AppPaths.PlaybackLogPath;
    private static readonly string SchedulerLogPath = AppPaths.SchedulerLogPath;
    private static readonly string InputOutputLogPath = AppPaths.InputOutputLogPath;
    private static readonly string MappingLogPath = AppPaths.MappingLogPath;
    private static readonly string PerformanceLogPath = AppPaths.PerformanceLogPath;
    private static readonly string ErrorsLogPath = AppPaths.ErrorsLogPath;

    private static readonly object _lock = new();
    private static readonly string[] ErrorKeywords =
    [
        "error",
        "exception",
        "failed",
        "unable",
        "could not",
        "fatal"
    ];

    private static readonly string[] WarningKeywords =
    [
        "warning",
        "not ready",
        "fallback",
        "missing"
    ];

    static Logger()
    {
        AppPaths.EnsureDirectoryExists();
    }

    public static void Log(string message, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        if (!ShouldWriteGeneralMessage(message, GetVerbosity()))
            return;

        WriteLogLine(AppLogPath, message, caller, file, line);
    }

    public static void LogException(Exception ex, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        var stackTrace = string.IsNullOrWhiteSpace(ex.StackTrace)
            ? string.Empty
            : $"\n{ex.StackTrace}";

        var exceptionMessage = $"EXCEPTION: {ex.GetType().Name}: {ex.Message}{stackTrace}";
        WriteLogLine(ErrorsLogPath, exceptionMessage, caller, file, line);
        WriteLogLine(AppLogPath, exceptionMessage, caller, file, line);
    }

    public static void LogStep(string step, string? details = null, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        if (GetVerbosity() < AllStepsVerbosity)
            return;

        if (string.IsNullOrWhiteSpace(details))
        {
            WriteStepToChannel(step, $"STEP: {step}", caller, file, line);
            return;
        }

        WriteStepToChannel(step, $"STEP: {step} | {details}", caller, file, line);
    }

    public static void LogPageVisit(string pageName, string? source = null, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        if (GetVerbosity() < AllStepsVerbosity)
            return;

        var details = string.IsNullOrWhiteSpace(source)
            ? $"page={pageName}"
            : $"page={pageName} | source={source}";

        WriteLogLine(AppLogPath, $"PAGE_VISIT: {details}", caller, file, line);
    }

    public static void LogApp(string message, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        WriteLogLine(AppLogPath, message, caller, file, line);
    }

    public static void LogMidiParser(string message, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        if (GetVerbosity() < AllStepsVerbosity)
            return;

        WriteLogLine(MidiParserLogPath, message, caller, file, line);
    }

    public static void LogPlayback(string message, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        if (GetVerbosity() < AllStepsVerbosity)
            return;

        WriteLogLine(PlaybackLogPath, message, caller, file, line);
    }

    public static void LogScheduler(string message, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        if (GetVerbosity() < AllStepsVerbosity)
            return;

        WriteLogLine(SchedulerLogPath, message, caller, file, line);
    }

    public static void LogInputOutput(string message, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        if (GetVerbosity() < AllStepsVerbosity)
            return;

        WriteLogLine(InputOutputLogPath, message, caller, file, line);
    }

    public static void LogMapping(string message, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        if (GetVerbosity() < AllStepsVerbosity)
            return;

        WriteLogLine(MappingLogPath, message, caller, file, line);
    }

    public static void LogPerformance(string message, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        if (GetVerbosity() < AllStepsVerbosity)
            return;

        WriteLogLine(PerformanceLogPath, message, caller, file, line);
    }

    public static void LogError(string message, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        WriteLogLine(ErrorsLogPath, message, caller, file, line);
    }

    public static string GetPrimaryLogPath() => AppLogPath;

    public static string GetLogsDirectoryPath() => AppPaths.LogsDirectory;

    public static void LogStartup(string productName, string appVersionDisplay, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        WriteLogLine(AppLogPath, $"{productName} v{appVersionDisplay} Starting", caller, file, line);
    }

    public static void ClearLog()
    {
        try
        {
            lock (_lock)
            {
                DeleteIfExists(AppLogPath);
                DeleteIfExists(MidiParserLogPath);
                DeleteIfExists(PlaybackLogPath);
                DeleteIfExists(SchedulerLogPath);
                DeleteIfExists(InputOutputLogPath);
                DeleteIfExists(MappingLogPath);
                DeleteIfExists(PerformanceLogPath);
                DeleteIfExists(ErrorsLogPath);
            }
        }
        catch
        {
            // Ignore
        }
    }

    public static string GetLogPath() => ErrorsLogPath;

    private static int GetVerbosity()
    {
        try
        {
            // Outside debug mode, force full diagnostics regardless of saved slider value.
            if (!Settings.Default.DebugModeEnabled)
                return AllStepsVerbosity;

            return Math.Clamp(Settings.Default.CrashLogVerbosity, ErrorsOnlyVerbosity, AllStepsVerbosity);
        }
        catch
        {
            return AllStepsVerbosity;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void WriteStepToChannel(string step, string message, string? caller, string? file, int line)
    {
        var path = ResolveStepPath(step);
        WriteLogLine(path, message, caller, file, line);
    }

    private static string ResolveStepPath(string step)
    {
        if (step.Contains("MIDI_", StringComparison.OrdinalIgnoreCase)
            || step.Contains("TRACK", StringComparison.OrdinalIgnoreCase)
            || step.Contains("TEMPO", StringComparison.OrdinalIgnoreCase)
            || step.Contains("PARSE", StringComparison.OrdinalIgnoreCase))
            return MidiParserLogPath;

        if (step.Contains("PLAYBACK", StringComparison.OrdinalIgnoreCase)
            || step.Contains("LISTEN_MODE", StringComparison.OrdinalIgnoreCase)
            || step.Contains("GAME_FOCUS", StringComparison.OrdinalIgnoreCase)
            || step.Contains("SEEK", StringComparison.OrdinalIgnoreCase))
            return PlaybackLogPath;

        if (step.Contains("MAPPING", StringComparison.OrdinalIgnoreCase))
            return MappingLogPath;

        if (step.Contains("PERF", StringComparison.OrdinalIgnoreCase))
            return PerformanceLogPath;

        return AppLogPath;
    }

    private static bool ShouldWriteGeneralMessage(string? message, int verbosity)
    {
        return verbosity switch
        {
            >= AllStepsVerbosity => true,
            <= ErrorsOnlyVerbosity => IsLikelyErrorMessage(message),
            _ => IsLikelyWarningOrErrorMessage(message)
        };
    }

    private static bool IsLikelyErrorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return ContainsAny(message, ErrorKeywords);
    }

    private static bool IsLikelyWarningOrErrorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        return ContainsAny(message, ErrorKeywords) || ContainsAny(message, WarningKeywords);
    }

    private static bool ContainsAny(string source, string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (source.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void WriteLogLine(string path, string message, string? caller, string? file, int line)
    {
        try
        {
            var fileName = Path.GetFileName(file);
            var logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] [{fileName}:{line}] [{caller}] {message}";

            lock (_lock)
            {
                File.AppendAllText(path, logMessage + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore logging errors
        }
    }
}

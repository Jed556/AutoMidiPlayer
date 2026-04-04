using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace AutoMidiPlayer.Data;

public static class CrashLogger
{
    private static readonly string LogPath = AppPaths.CrashLogPath;

    private static readonly object _lock = new();

    static CrashLogger()
    {
        AppPaths.EnsureDirectoryExists();
    }

    public static void Log(string message, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        try
        {
            var fileName = Path.GetFileName(file);
            var logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] [{fileName}:{line}] [{caller}] {message}";

            lock (_lock)
            {
                File.AppendAllText(LogPath, logMessage + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore logging errors
        }
    }

    public static void LogException(Exception ex, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        Log($"EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", caller, file, line);
    }

    public static void LogStep(string step, string? details = null, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            Log($"STEP: {step}", caller, file, line);
            return;
        }

        Log($"STEP: {step} | {details}", caller, file, line);
    }

    public static void LogPageVisit(string pageName, string? source = null, [CallerMemberName] string? caller = null, [CallerFilePath] string? file = null, [CallerLineNumber] int line = 0)
    {
        var details = string.IsNullOrWhiteSpace(source)
            ? $"page={pageName}"
            : $"page={pageName} | source={source}";

        Log($"PAGE_VISIT: {details}", caller, file, line);
    }

    public static void ClearLog()
    {
        try
        {
            lock (_lock)
            {
                if (File.Exists(LogPath))
                    File.Delete(LogPath);
            }
        }
        catch
        {
            // Ignore
        }
    }

    public static string GetLogPath() => LogPath;
}

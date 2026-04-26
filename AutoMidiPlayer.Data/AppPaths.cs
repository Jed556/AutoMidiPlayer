using System;
using System.IO;

namespace AutoMidiPlayer.Data;

/// <summary>
/// Centralized paths for application data storage.
/// All app data is stored in %LocalAppData%\AutoMidiPlayer
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Base application data directory: %LocalAppData%\AutoMidiPlayer
    /// </summary>
    public static readonly string AppDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoMidiPlayer");

    /// <summary>
    /// Path to the SQLite database file
    /// </summary>
    public static readonly string DatabasePath = Path.Combine(AppDataDirectory, "AutoMidiPlayer.db");

    /// <summary>
    /// Path to the logs directory
    /// </summary>
    public static readonly string LogsDirectory = Path.Combine(AppDataDirectory, "logs");

    /// <summary>
    /// Path to the general application log file
    /// </summary>
    public static readonly string AppLogPath = Path.Combine(LogsDirectory, "app.log");

    /// <summary>
    /// Path to the MIDI parser log file
    /// </summary>
    public static readonly string MidiParserLogPath = Path.Combine(LogsDirectory, "midi-parser.log");

    /// <summary>
    /// Path to the playback log file
    /// </summary>
    public static readonly string PlaybackLogPath = Path.Combine(LogsDirectory, "playback.log");

    /// <summary>
    /// Path to the scheduler log file
    /// </summary>
    public static readonly string SchedulerLogPath = Path.Combine(LogsDirectory, "scheduler.log");

    /// <summary>
    /// Path to the input/output log file
    /// </summary>
    public static readonly string InputOutputLogPath = Path.Combine(LogsDirectory, "input-output.log");

    /// <summary>
    /// Path to the mapping log file
    /// </summary>
    public static readonly string MappingLogPath = Path.Combine(LogsDirectory, "mapping.log");

    /// <summary>
    /// Path to the performance log file
    /// </summary>
    public static readonly string PerformanceLogPath = Path.Combine(LogsDirectory, "performance.log");

    /// <summary>
    /// Path to the errors log file
    /// </summary>
    public static readonly string ErrorsLogPath = Path.Combine(LogsDirectory, "errors.log");

    /// <summary>
    /// Backward-compatible crash log alias that now points to the centralized errors log.
    /// </summary>
    public static readonly string CrashLogPath = ErrorsLogPath;

    /// <summary>
    /// Path to the user settings file (user.config)
    /// </summary>
    public static readonly string UserConfigPath = Path.Combine(AppDataDirectory, "user.config");

    /// <summary>
    /// Marker file used to notify the next app launch that a reset just completed.
    /// </summary>
    public static readonly string ResetCompletedMarkerPath = Path.Combine(AppDataDirectory, ".reset-complete");

    /// <summary>
    /// Ensures the app data directory exists
    /// </summary>
    public static void EnsureDirectoryExists()
    {
        if (!Directory.Exists(AppDataDirectory))
            Directory.CreateDirectory(AppDataDirectory);

        if (!Directory.Exists(LogsDirectory))
            Directory.CreateDirectory(LogsDirectory);
    }
}

using System;
using System.IO;

namespace AutoMidiPlayer.Data;

/// <summary>
/// Centralized paths for application data storage.
/// All app data is stored in %LocalAppData%\AutoMidiPlayer
/// </summary>
public static class AppPaths
{
    public static string DistributionType
    {
        get
        {
#if DEBUG
            return "Development";
#else
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (exeDir != null && File.Exists(Path.Combine(exeDir, "AutoMidiPlayer.Data.dll")))
            {
                return "Net-Install";
            }
            return "Portable";
#endif
        }
    }

    public static bool IsNetInstall => DistributionType == "Net-Install" || DistributionType == "Development";

    private static string GetAppDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppDomain.CurrentDomain.BaseDirectory;
        }
        return Path.Combine(localAppData, "AutoMidiPlayer");
    }

    /// <summary>
    /// Base application data directory: %LocalAppData%\AutoMidiPlayer
    /// </summary>
    public static readonly string AppDataDirectory = GetAppDataDirectory();

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
    /// Path to the status file used to track app events like reset and updates across restarts.
    /// </summary>
    public static readonly string AppStatusFilePath = Path.Combine(AppDataDirectory, "status");

    /// <summary>
    /// Path to the update cache directory (cache/update)
    /// </summary>
    public static readonly string UpdateCacheDirectory = Path.Combine(AppDataDirectory, "cache", "update");

    /// <summary>
    /// Directory where MIDI files downloaded from online sources (e.g. MidiShow) are stored.
    /// These files are referenced by the song library, so the location must be persistent.
    /// </summary>
    public static readonly string OnlineMidiDirectory = Path.Combine(AppDataDirectory, "OnlineMidi");

    /// <summary>
    /// Root directory for Discover page caching: %LocalAppData%\AutoMidiPlayer\cache\discover\MidiShow
    /// </summary>
    public static readonly string DiscoverCacheDirectory =
        Path.Combine(AppDataDirectory, "cache", "discover", "MidiShow");

    public static readonly string NanoMidiCacheDirectory =
        Path.Combine(AppDataDirectory, "cache", "discover", "NanoMidi");

    /// <summary>
    /// Directory for cached per-MIDI data (summary.json, details.json, file.mid) keyed by MIDI id.
    /// </summary>
    public static readonly string DiscoverMidiCacheDirectory =
        Path.Combine(DiscoverCacheDirectory, "midi");

    /// <summary>
    /// Directory for cached user avatar images, keyed by URL hash.
    /// </summary>
    public static readonly string DiscoverAvatarCacheDirectory =
        Path.Combine(DiscoverCacheDirectory, "avatar");

    /// <summary>
    /// Path to the encrypted MidiShow account credentials file (per-user, DPAPI protected).
    /// Legacy single-account store; superseded by <see cref="MidiShowAccountsPath"/> and only
    /// read once for migration.
    /// </summary>
    public static readonly string MidiShowCredentialsPath = Path.Combine(AppDataDirectory, "midishow.cred");

    /// <summary>
    /// Path to the encrypted MidiShow account pool file (per-user, DPAPI protected). Holds the
    /// list of configured accounts (password- or cookie-based) used for download rotation.
    /// </summary>
    public static readonly string MidiShowAccountsPath = Path.Combine(AppDataDirectory, "midishow.accounts");

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

    /// <summary>
    /// Ensures that the directory containing the SQLite database exists.
    /// </summary>
    public static void EnsureDatabaseDirectoryExists()
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    /// <summary>
    /// Ensures the online MIDI download directory exists and returns its path.
    /// </summary>
    public static string EnsureOnlineMidiDirectory(string? providerName = null)
    {
        if (!Directory.Exists(OnlineMidiDirectory))
            Directory.CreateDirectory(OnlineMidiDirectory);

        if (!string.IsNullOrWhiteSpace(providerName))
        {
            var providerDir = Path.Combine(OnlineMidiDirectory, providerName);
            if (!Directory.Exists(providerDir))
                Directory.CreateDirectory(providerDir);
            return providerDir;
        }

        return OnlineMidiDirectory;
    }

    /// <summary>
    /// Ensures the Discover page cache directories (midi/ and avatar/) exist and returns the root path.
    /// </summary>
    public static string EnsureDiscoverCacheDirectories()
    {
        if (!Directory.Exists(DiscoverMidiCacheDirectory))
            Directory.CreateDirectory(DiscoverMidiCacheDirectory);

        if (!Directory.Exists(DiscoverAvatarCacheDirectory))
            Directory.CreateDirectory(DiscoverAvatarCacheDirectory);

        return DiscoverCacheDirectory;
    }
}

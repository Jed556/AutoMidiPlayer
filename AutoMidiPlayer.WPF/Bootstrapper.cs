using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Helpers;
using AutoMidiPlayer.WPF.MessageBox;
using AutoMidiPlayer.WPF.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Sentry;
using Sentry.Profiling;
using Stylet;
using StyletIoC;
using System.Reflection;
using Wpf.Ui.Appearance;
using System.Threading;
using System.Windows.Media;

namespace AutoMidiPlayer.WPF;

public class Bootstrapper : Bootstrapper<MainWindowViewModel>
{
    private static readonly object DatabaseInitializationLock = new();
    private static bool _databaseInitialized;

    private static readonly (string ColumnName, string SqlType)[] SongColumnMigrations =
    [
        ("ImagePath", "TEXT NULL"),
        ("FileHash", "TEXT NULL"),
        ("MergeNotes", "INTEGER NULL"),
        ("MergeMilliseconds", "INTEGER NULL"),
        ("HoldNotes", "INTEGER NULL"),
        ("Speed", "REAL NULL"),
        ("Bpm", "REAL NULL"),
        ("BaseKey", "INTEGER NULL")
    ];

    public Bootstrapper()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // Suppress benign Storyboard animation warnings from WPF-UI (idk why this happens XD)
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Critical;
        System.Diagnostics.PresentationTraceSources.ResourceDictionarySource.Switch.Level = System.Diagnostics.SourceLevels.Critical;
        System.Diagnostics.PresentationTraceSources.AnimationSource.Switch.Level = System.Diagnostics.SourceLevels.Critical;

        // ensure version retrieval helper is available by referencing Reflection
        _ = GetAppVersion();

        // Ensure queue loop mode is always a valid enum value before ViewModels read settings.
        EnsureQueueLoopModeSetting();

        // Clear log on startup
        Logger.ClearLog();

        // log application start along with the product name and current version
        string productName = GetProductName();
        string appVersionDisplay = GetAppVersion();
        Logger.LogStartup(productName, appVersionDisplay);
        Logger.LogApp($"Logs directory: {Logger.GetLogsDirectoryPath()}");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Logger.LogApp($"{GetProductName()} v{GetAppVersion()} Stopping");
            try
            {
                // Best-effort: stop the system theme watcher if running.
                AutoMidiPlayer.WPF.Services.SystemThemeService.Stop();
            }
            catch { }

            // Flush Sentry events and shut down cleanly
            SentrySdk.Close();
        };

        // Handle unhandled exceptions
        Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

    }

    protected override void OnLaunch()
    {
        base.OnLaunch();

        // Initialize Sentry early, after DI container is fully built and app is launching
        try
        {
            var sentryService = Container.Get<AutoMidiPlayer.WPF.Services.ISentryService>();
            sentryService.Initialize();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }

    private static void EnsureQueueLoopModeSetting()
    {
        if (Enum.IsDefined(typeof(QueueViewModel.LoopMode), Settings.Default.QueueLoopMode))
            return;

        Settings.Default.QueueLoopMode = (int)QueueViewModel.LoopMode.Off;
        Settings.Default.Save();
    }

    private static string GetAppVersion()
    {
        // assembly version should be kept in sync with project version
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null)
            return "unknown";

        if (version.Revision == 0 && version.Build >= 0)
            return $"{version.Major}.{version.Minor}.{version.Build}";

        if (version.Build < 0)
            return $"{version.Major}.{version.Minor}";

        return version.ToString();
    }

    private static string GetProductName()
    {
        // read product attribute from assembly (populated from csproj <Product>)
        var attr = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyProductAttribute>();
        return attr?.Product ?? "Unknown Product";
    }

    private static void EnsureDatabaseInitialized(PlayerContext db)
    {
        if (_databaseInitialized)
            return;

        lock (DatabaseInitializationLock)
        {
            if (_databaseInitialized)
                return;

            db.Database.EnsureCreated();

            var existingSongColumns = GetSongTableColumns(db);
            if (existingSongColumns.Count > 0)
            {
                RenameSongColumn(db, existingSongColumns, "Author", "Artist");
                RenameSongColumn(db, existingSongColumns, "DefaultKey", "BaseKey");

                foreach (var (columnName, sqlType) in SongColumnMigrations)
                {
                    if (!existingSongColumns.Contains(columnName))
                        AddSongColumnIfMissing(db, columnName, sqlType);
                }
            }

            _databaseInitialized = true;
        }
    }

    private static HashSet<string> GetSongTableColumns(PlayerContext db)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var connection = db.Database.GetDbConnection();
        var shouldCloseConnection = connection.State == System.Data.ConnectionState.Closed;
        if (shouldCloseConnection)
            connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(Songs);";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var name = reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(name))
                    columns.Add(name);
            }
        }
        finally
        {
            if (shouldCloseConnection)
                connection.Close();
        }

        return columns;
    }

// #pragma warning disable EF1003

    private static void RenameSongColumn(PlayerContext db, HashSet<string> existingSongColumns, string oldColumnName, string newColumnName)
    {
        if (!existingSongColumns.Contains(oldColumnName) || existingSongColumns.Contains(newColumnName))
            return;

        try
        {
            db.Database.ExecuteSqlRaw(@"
                ALTER TABLE Songs RENAME COLUMN " + oldColumnName + @" TO " + newColumnName + @";
            ");

            existingSongColumns.Remove(oldColumnName);
            existingSongColumns.Add(newColumnName);
        }
        catch
        {
            ExecuteSqlIgnoringErrors(db, @"
                ALTER TABLE Songs ADD COLUMN " + newColumnName + @" TEXT NULL;
            ");

            ExecuteSqlIgnoringErrors(db, @"
                UPDATE Songs
                SET " + newColumnName + @" = " + oldColumnName + @"
                WHERE " + newColumnName + @" IS NULL;
            ");

            existingSongColumns.Remove(oldColumnName);
            existingSongColumns.Add(newColumnName);
        }
    }

    private static void AddSongColumnIfMissing(PlayerContext db, string columnName, string sqlType)
    {
        ExecuteSqlIgnoringErrors(db, $@"
            ALTER TABLE Songs ADD COLUMN {columnName} {sqlType};
        ");
    }

    private static void ExecuteSqlIgnoringErrors(PlayerContext db, string sql)
    {
        try
        {
            db.Database.ExecuteSqlRaw(sql);
        }
        catch
        {
            // Best-effort migration: schema already updated or not applicable.
        }
    }

#pragma warning restore EF1003

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Log("=== DISPATCHER UNHANDLED EXCEPTION ===");
        Logger.LogException(e.Exception);
        SentrySdk.CaptureException(e.Exception);

        // Flush to ensure the crash event reaches Sentry before the app may terminate
        SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();

        try
        {
            CrashMessageBox.Show(e.Exception, Logger.GetPrimaryLogPath());
        }
        catch
        {
            // Fallback if the themed dialog itself fails
            MessageBoxHelper.ShowError(
                $"An error occurred. Logs saved in:\n{Logger.GetLogsDirectoryPath()}\n\nError: {e.Exception.Message}",
                "AutoMidiPlayer Error");
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Logger.Log("=== UNHANDLED EXCEPTION ===");
        if (e.ExceptionObject is Exception ex)
        {
            Logger.LogException(ex);
            SentrySdk.CaptureException(ex);
        }
        else
            Logger.Log($"Non-exception object: {e.ExceptionObject}");

        // Terminal crash — flush synchronously before the process dies
        SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.Log("=== UNOBSERVED TASK EXCEPTION ===");
        Logger.LogException(e.Exception);
        SentrySdk.CaptureException(e.Exception);
    }

    protected override void ConfigureIoC(IStyletIoCBuilder builder)
    {
        // Bind Sentry service first
        builder.Bind<AutoMidiPlayer.WPF.Services.ISentryService>().To<AutoMidiPlayer.WPF.Services.SentryService>().InSingletonScope();
        // Use centralized app data path
        AppPaths.EnsureDirectoryExists();

        // Use cached view manager to avoid rebuilding heavy views on navigation
        builder.Bind<IViewManager>().To<CachedViewManager>().InSingletonScope();

        builder.Bind<PlayerContext>().ToFactory(_ =>
        {
            var source = AppPaths.DatabasePath;

            var options = new DbContextOptionsBuilder<PlayerContext>()
                .UseSqlite($"Data Source={source}")
                .Options;

            var db = new PlayerContext(options);
            EnsureDatabaseInitialized(db);

            return db;
        });

        builder.Bind<Windows.Media.Playback.MediaPlayer>().ToFactory(_ =>
        {
            var player = new Windows.Media.Playback.MediaPlayer();
            var controls = player.SystemMediaTransportControls;

            controls.IsEnabled = true;
            controls.DisplayUpdater.Type = MediaPlaybackType.Music;

            Task.Run(async () =>
            {
                await Task.Yield();
                controls.DisplayUpdater.Thumbnail =
                    RandomAccessStreamReference.CreateFromUri(new Uri("ms-appx:///Resources/logo.png"));
            });

            return player;
        }).InSingletonScope();

        // Register GlobalHotkeyService as singleton
        builder.Bind<Services.GlobalHotkeyService>().ToSelf().InSingletonScope();
        
        // Register UpdateService as singleton
        builder.Bind<Services.UpdateService>().ToSelf().InSingletonScope();

        // Theme service removed in WPF-UI 3.x - use centralized SystemThemeService
        AutoMidiPlayer.WPF.Services.SystemThemeService.Start();
    }
}

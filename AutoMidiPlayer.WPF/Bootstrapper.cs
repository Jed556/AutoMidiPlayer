using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Windows.Threading;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage.Streams;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.Errors;
using AutoMidiPlayer.WPF.ViewModels;
using Microsoft.EntityFrameworkCore;
using Stylet;
using StyletIoC;
using System.Reflection;
using Wpf.Ui.Controls;

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
        ("DefaultKey", "INTEGER NULL")
    ];

    public Bootstrapper()
    {
        // ensure version retrieval helper is available by referencing Reflection
        _ = GetAppVersion();

        // Ensure queue loop mode is always a valid enum value before ViewModels read settings.
        EnsureQueueLoopModeSetting();

        // Clear log on startup
        CrashLogger.ClearLog();

        // log application start along with the product name and current version
        CrashLogger.Log($"{GetProductName()} v{GetAppVersion()} Starting");

        // Handle unhandled exceptions
        Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Make Hyperlinks handle themselves
        EventManager.RegisterClassHandler(
            typeof(Hyperlink), Hyperlink.RequestNavigateEvent,
            new RequestNavigateEventHandler((_, e) =>
            {
                var url = e.Uri.ToString();
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            })
        );
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
        return version?.ToString() ?? "unknown";
    }

    private static string GetProductName()
    {
        // read product attribute from assembly (populated from csproj <Product>)
        var attr = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyProductAttribute>();
        return attr?.Product ?? "Unknown Product";
    }

    private static void EnsureDatabaseInitialized(LyreContext db)
    {
        if (_databaseInitialized)
            return;

        lock (DatabaseInitializationLock)
        {
            if (_databaseInitialized)
                return;

            db.Database.EnsureCreated();
            RenameAuthorColumnToArtist(db);

            foreach (var (columnName, sqlType) in SongColumnMigrations)
                AddSongColumnIfMissing(db, columnName, sqlType);

            _databaseInitialized = true;
        }
    }

    private static void RenameAuthorColumnToArtist(LyreContext db)
    {
        try
        {
            db.Database.ExecuteSqlRaw(@"
                ALTER TABLE Songs RENAME COLUMN Author TO Artist;
            ");
        }
        catch
        {
            ExecuteSqlIgnoringErrors(db, @"
                ALTER TABLE Songs ADD COLUMN Artist TEXT NULL;
            ");

            ExecuteSqlIgnoringErrors(db, @"
                UPDATE Songs
                SET Artist = Author
                WHERE Artist IS NULL;
            ");
        }
    }

    private static void AddSongColumnIfMissing(LyreContext db, string columnName, string sqlType)
    {
        ExecuteSqlIgnoringErrors(db, $@"
            ALTER TABLE Songs ADD COLUMN {columnName} {sqlType};
        ");
    }

    private static void ExecuteSqlIgnoringErrors(LyreContext db, string sql)
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

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLogger.Log("=== DISPATCHER UNHANDLED EXCEPTION ===");
        CrashLogger.LogException(e.Exception);

        try
        {
            CrashMessageBox.Show(e.Exception, CrashLogger.GetLogPath());
        }
        catch
        {
            // Fallback if the themed dialog itself fails
            System.Windows.MessageBox.Show(
                $"An error occurred. Log saved to:\n{CrashLogger.GetLogPath()}\n\nError: {e.Exception.Message}",
                "AutoMidiPlayer Error",
                System.Windows.MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        CrashLogger.Log("=== UNHANDLED EXCEPTION ===");
        if (e.ExceptionObject is Exception ex)
            CrashLogger.LogException(ex);
        else
            CrashLogger.Log($"Non-exception object: {e.ExceptionObject}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogger.Log("=== UNOBSERVED TASK EXCEPTION ===");
        CrashLogger.LogException(e.Exception);
    }

    protected override void ConfigureIoC(IStyletIoCBuilder builder)
    {
        // Use centralized app data path
        AppPaths.EnsureDirectoryExists();

        builder.Bind<LyreContext>().ToFactory(_ =>
        {
            var source = AppPaths.DatabasePath;

            var options = new DbContextOptionsBuilder<LyreContext>()
                .UseSqlite($"Data Source={source}")
                .Options;

            var db = new LyreContext(options);
            EnsureDatabaseInitialized(db);

            return db;
        });

        builder.Bind<MediaPlayer>().ToFactory(_ =>
        {
            var player = new MediaPlayer();
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

        // Theme service removed in WPF-UI 3.x - use ApplicationThemeManager directly
    }
}

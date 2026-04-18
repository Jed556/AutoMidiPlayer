using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
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
        // Suppress benign Storyboard animation warnings from WPF-UI (idk why this happens XD)
        System.Diagnostics.PresentationTraceSources.AnimationSource.Switch.Level = System.Diagnostics.SourceLevels.Critical;

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

        EventManager.RegisterClassHandler(
            typeof(Hyperlink),
            Mouse.MouseEnterEvent,
            new MouseEventHandler(OnHyperlinkMouseEnter)
        );

        EventManager.RegisterClassHandler(
            typeof(Hyperlink),
            Mouse.MouseLeaveEvent,
            new MouseEventHandler(OnHyperlinkMouseLeave)
        );
    }

    private static void OnHyperlinkMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Hyperlink hyperlink)
            AnimateHyperlinkForeground(hyperlink, isHover: true);
    }

    private static void OnHyperlinkMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Hyperlink hyperlink)
            AnimateHyperlinkForeground(hyperlink, isHover: false);
    }

    private static void AnimateHyperlinkForeground(Hyperlink hyperlink, bool isHover)
    {
        var primaryColor = GetResourceColor("SystemAccentColorPrimary", System.Windows.Media.Colors.DodgerBlue);
        var secondaryColor = GetResourceColor("SystemAccentColorSecondary", primaryColor);
        var targetColor = isHover ? secondaryColor : primaryColor;

        if (hyperlink.Foreground is not System.Windows.Media.SolidColorBrush brush || brush.IsFrozen)
        {
            var startingColor = hyperlink.Foreground is System.Windows.Media.SolidColorBrush existingBrush
                ? existingBrush.Color
                : primaryColor;

            brush = new System.Windows.Media.SolidColorBrush(startingColor);
            hyperlink.Foreground = brush;
        }

        brush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, null);

        var animation = new System.Windows.Media.Animation.ColorAnimation
        {
            To = targetColor,
            Duration = isHover
                ? TimeSpan.FromMilliseconds(150)
                : TimeSpan.FromMilliseconds(240),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase
            {
                EasingMode = isHover
                    ? System.Windows.Media.Animation.EasingMode.EaseOut
                    : System.Windows.Media.Animation.EasingMode.EaseInOut
            },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
        };

        animation.Completed += (_, _) => brush.Color = targetColor;
        brush.BeginAnimation(
            System.Windows.Media.SolidColorBrush.ColorProperty,
            animation,
            System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
    }

    private static System.Windows.Media.Color GetResourceColor(string key, System.Windows.Media.Color fallback)
    {
        var value = Application.Current?.TryFindResource(key);
        return value switch
        {
            System.Windows.Media.Color color => color,
            System.Windows.Media.SolidColorBrush brush => brush.Color,
            _ => fallback
        };
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
                RenameAuthorColumnToArtist(db, existingSongColumns);

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

    private static void RenameAuthorColumnToArtist(PlayerContext db, HashSet<string> existingSongColumns)
    {
        if (!existingSongColumns.Contains("Author") || existingSongColumns.Contains("Artist"))
            return;

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
            MessageBoxHelper.ShowError(
                $"An error occurred. Log saved to:\n{CrashLogger.GetLogPath()}\n\nError: {e.Exception.Message}",
                "AutoMidiPlayer Error");
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

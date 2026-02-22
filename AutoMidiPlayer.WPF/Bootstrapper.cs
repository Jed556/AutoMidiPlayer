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
using Wpf.Ui.Controls;

namespace AutoMidiPlayer.WPF;

public class Bootstrapper : Bootstrapper<MainWindowViewModel>
{
    public Bootstrapper()
    {
        // Clear log on startup
        CrashLogger.ClearLog();
        CrashLogger.Log("Application starting");

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
            db.Database.EnsureCreated();

            // Add ImagePath column if it doesn't exist (migration for existing databases)
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE Songs ADD COLUMN ImagePath TEXT NULL;
                ");
            }
            catch
            {
                // Column already exists or other error - ignore
            }

            // Add FileHash column if it doesn't exist (migration for duplicate detection)
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE Songs ADD COLUMN FileHash TEXT NULL;
                ");
            }
            catch
            {
                // Column already exists or other error - ignore
            }

            // Add MergeNotes column if it doesn't exist (migration for per-song settings)
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE Songs ADD COLUMN MergeNotes INTEGER NULL;
                ");
            }
            catch
            {
                // Column already exists or other error - ignore
            }

            // Add MergeMilliseconds column if it doesn't exist (migration for per-song settings)
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE Songs ADD COLUMN MergeMilliseconds INTEGER NULL;
                ");
            }
            catch
            {
                // Column already exists or other error - ignore
            }

            // Add HoldNotes column if it doesn't exist (migration for per-song settings)
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE Songs ADD COLUMN HoldNotes INTEGER NULL;
                ");
            }
            catch
            {
                // Column already exists or other error - ignore
            }

            // Add Speed column if it doesn't exist (migration for per-song speed)
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE Songs ADD COLUMN Speed REAL NULL;
                ");
            }
            catch
            {
                // Column already exists or other error - ignore
            }

            // Add Bpm column if it doesn't exist (migration for per-song BPM)
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    ALTER TABLE Songs ADD COLUMN Bpm REAL NULL;
                ");
            }
            catch
            {
                // Column already exists or other error - ignore
            }

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

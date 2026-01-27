using System;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;
using System.Windows.Threading;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Properties;
using AutoMidiPlayer.WPF.ViewModels;
using Microsoft.EntityFrameworkCore;
using Stylet;
using StyletIoC;
using Wpf.Ui.Mvvm.Contracts;
using Wpf.Ui.Mvvm.Services;

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

        // Show message box with log path
        MessageBox.Show(
            $"An error occurred. Log saved to:\n{CrashLogger.GetLogPath()}\n\nError: {e.Exception.Message}",
            "AutoMidiPlayer Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
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
        var config = ConfigurationManager
            .OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);

        var path = Path.GetDirectoryName(config.FilePath);
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path!);

        builder.Bind<LyreContext>().ToFactory(_ =>
        {
            var source = Path.Combine(path!, Settings.Default.SqliteConnection);

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
                const string name = "item_windsong_lyre.png";
                var location = Path.Combine(path!, name);

                var uri = new Uri($"pack://application:,,,/{name}");
                var resource = Application.GetResourceStream(uri)!.Stream;
                Image.FromStream(resource).Save(location);

                var file = await StorageFile.GetFileFromPathAsync(location);
                controls.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromFile(file);
            });

            return player;
        }).InSingletonScope();

        builder.Bind<IThemeService>().To<ThemeService>().InSingletonScope();
    }
}

using System;
using System.IO;
using System.Reflection;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Properties;
using Sentry;
using Sentry.Profiling;

namespace AutoMidiPlayer.WPF.Services;

public interface ISentryService
{
    void Initialize();
    void SetTelemetryEnabled(bool enabled);
}

public class SentryService : ISentryService
{
    private static string GetAppVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version?.ToString() ?? "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

    /// <summary>
    /// Reads the last <paramref name="maxLines"/> lines from a log file.
    /// Returns the tail as a single string for Sentry extras.
    /// </summary>
    private static string ReadLogTail(string path, int maxLines)
    {
        try
        {
            if (!File.Exists(path))
                return string.Empty;

            var lines = File.ReadAllLines(path);
            if (lines.Length <= maxLines)
                return string.Join(Environment.NewLine, lines);

            var tail = new string[maxLines];
            Array.Copy(lines, lines.Length - maxLines, tail, 0, maxLines);
            return string.Join(Environment.NewLine, tail);
        }
        catch
        {
            return "Unable to read log file.";
        }
    }

    public void Initialize()
    {
        if (Settings.Default.TelemetryOptIn && Settings.Default.HasShownFirstLaunch)
        {
            StartSentry();
        }
    }

    public void SetTelemetryEnabled(bool enabled)
    {
        if (enabled && !SentrySdk.IsEnabled)
        {
            StartSentry();
        }
        else if (!enabled && SentrySdk.IsEnabled)
        {
            SentrySdk.Close();
        }
    }

    private void StartSentry()
    {
        SentrySdk.Init(options =>
        {
            options.Dsn = "https://b023bd031b673f35306771bae355120d@o4511550890967040.ingest.us.sentry.io/4511550899355648";

            // Tag events with the app version and environment
            options.Release = $"auto-midi-player@{GetAppVersion()}";
#if DEBUG
            options.Environment = "debug";
#else
            options.Environment = "production";
#endif

            // Enable debug logging only for troubleshooting; disable in production
            options.Debug = true;

            // Release Health: track session stability
            options.AutoSessionTracking = true;

            // Capture 100% of transactions for tracing (adjust in production)
            options.TracesSampleRate = 1.0;

            // Profile 100% of captured transactions (adjust in production)
            options.ProfilesSampleRate = 1.0;
            options.AddIntegration(new ProfilingIntegration(
                // Wait up to 500ms to profile app startup code
                TimeSpan.FromMilliseconds(500)
            ));

            // Enable structured logs
            options.EnableLogs = true;

            // Attach local log file contents to crash reports
            options.SetBeforeSend((sentryEvent, hint) =>
            {
                if (sentryEvent.Level == SentryLevel.Error || sentryEvent.Level == SentryLevel.Fatal || sentryEvent.Exception != null)
                {
                    try
                    {
                        var appLog = Logger.GetPrimaryLogPath();
                        if (File.Exists(appLog))
                            sentryEvent.SetExtra("app.log", ReadLogTail(appLog, 100));
                    }
                    catch
                    {
                        // Best-effort: don't let log reading break event submission
                    }
                }
                return sentryEvent;
            });
        });

        if (!Settings.Default.HasReportedMachineInfo)
        {
            SentrySdk.CaptureMessage("User opted into telemetry", SentryLevel.Info);
            Settings.Default.HasReportedMachineInfo = true;
            Settings.Default.Save();
        }
    }
}

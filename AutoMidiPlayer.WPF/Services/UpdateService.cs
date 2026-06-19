using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.Data.Git;
using AutoMidiPlayer.WPF.Helpers;

namespace AutoMidiPlayer.WPF.Services;

public class UpdateProgressInfo
{
    public string ProgressText { get; set; } = string.Empty;
    public string ProgressDetailText { get; set; } = string.Empty;
    public double ProgressPercentage { get; set; }
    public bool IsProgressIndeterminate { get; set; }
}

public class UpdateService
{
    public event EventHandler<(bool Success, string Version, string ErrorMessage)>? BackgroundDownloadCompleted;
    public event EventHandler<GitVersion>? UpdateAvailable;

    private System.Timers.Timer? _updateCheckTimer;
    private bool _isCheckingUpdate;

    public void ConfigureUpdateCheckTimer()
    {
        _updateCheckTimer?.Stop();
        _updateCheckTimer?.Dispose();
        _updateCheckTimer = null;

        var autoCheck = AutoMidiPlayer.Data.Properties.Settings.Default.AutoCheckUpdates;
        var frequency = AutoMidiPlayer.Data.Properties.Settings.Default.UpdateCheckFrequency;

        if (!autoCheck || frequency == 4) return;

        double intervalMs = frequency switch
        {
            0 => 10 * 60 * 1000,
            1 => 60 * 60 * 1000,
            2 => 24 * 60 * 60 * 1000,
            3 => 7 * 24 * 60 * 60 * 1000,
            _ => 10 * 60 * 1000
        };

        _updateCheckTimer = new System.Timers.Timer(intervalMs);
        _updateCheckTimer.Elapsed += async (_, _) => await PerformBackgroundUpdateCheckAsync();
        _updateCheckTimer.Start();
    }

    private async Task PerformBackgroundUpdateCheckAsync()
    {
        if (_isCheckingUpdate) return;
        _isCheckingUpdate = true;
        try
        {
            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            var includeBeta = AutoMidiPlayer.Data.Properties.Settings.Default.IncludeBetaUpdates;

            var newVersion = await CheckForUpdatesAsync(includeBeta, currentVersion);
            if (newVersion != null && newVersion.Version > currentVersion)
            {
                UpdateAvailable?.Invoke(this, newVersion);

                if (AutoMidiPlayer.Data.Properties.Settings.Default.AutoDownloadUpdates)
                {
                    await BackgroundDownloadUpdateAsyncWrapper(newVersion);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
        finally
        {
            _isCheckingUpdate = false;
        }
    }

    public async Task BackgroundDownloadUpdateAsyncWrapper(GitVersion version)
    {
        try
        {
            var isNetInstall = Path.GetFileName(Environment.ProcessPath ?? "").Contains("net-install", StringComparison.OrdinalIgnoreCase);
            var versionType = isNetInstall ? "Net-Install" : "Portable";

            var result = await BackgroundDownloadUpdateAsync(version, versionType);
            BackgroundDownloadCompleted?.Invoke(this, (result.Success, version.Version.ToString(), result.ErrorMessage));
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            BackgroundDownloadCompleted?.Invoke(this, (false, version.Version.ToString(), ex.Message));
        }
    }
    public async Task<GitVersion?> CheckForUpdatesAsync(bool includeBeta, Version programVersion)
    {
        try
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/Jed556/AutoMidiPlayer/releases");

            var productInfo = new ProductInfoHeaderValue("AutoMidiPlayer", programVersion.ToString());
            request.Headers.UserAgent.Add(productInfo);

            var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return null;

            var versions = await response.Content.ReadFromJsonAsync<List<GitVersion>>();

            return versions?
                .OrderByDescending(v => v.Version)
                .FirstOrDefault(v => (!v.Draft && !v.Prerelease) || includeBeta);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            return null;
        }
    }

    public async Task<(bool Success, string ErrorMessage)> BackgroundDownloadUpdateAsync(GitVersion latestVersion, string selectedVersionType)
    {
        try
        {
            var assetNameSearch = selectedVersionType == "Portable" ? "win-x64-portable.zip" : "win-x64-net-install.zip";
            var asset = latestVersion.Assets.FirstOrDefault(a => a.Name.Contains(assetNameSearch, StringComparison.OrdinalIgnoreCase));
            var checksumAsset = latestVersion.Assets.FirstOrDefault(a => a.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase));

            if (asset == null) return (false, "Missing update package in GitHub release.");
            if (checksumAsset == null) return (false, "Missing checksums.txt in GitHub release.");

            var tempDir = AppPaths.UpdateCacheDirectory;
            if (!Directory.Exists(tempDir))
                Directory.CreateDirectory(tempDir);
            
            var zipPath = Path.Combine(tempDir, "update.zip");
            var checksumPath = Path.Combine(tempDir, "checksums.txt");

            using var client = new HttpClient();
            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
            var productInfo = new ProductInfoHeaderValue("AutoMidiPlayer", currentVersion);
            client.DefaultRequestHeaders.UserAgent.Add(productInfo);
            
            var checksumContent = await client.GetStringAsync(checksumAsset.DownloadUrl);
            await File.WriteAllTextAsync(checksumPath, Crypt.EncryptToBase64(checksumContent));

            using var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            
            await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            {
                await contentStream.CopyToAsync(fs);
            }

            var expectedHash = GetHashFromChecksumFile(checksumPath, assetNameSearch);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(zipPath);
            var computedHashBytes = await sha256.ComputeHashAsync(stream);
            var computedHash = BitConverter.ToString(computedHashBytes).Replace("-", "").ToLowerInvariant();

            if (expectedHash == null || !expectedHash.Equals(computedHash, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zipPath);
                return (false, "Checksum mismatch! Downloaded file may be corrupted.");
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            return (false, ex.Message);
        }
    }

    private string? GetHashFromChecksumFile(string checksumPath, string assetNameSearch)
    {
        if (!File.Exists(checksumPath)) return null;
        var encryptedContent = File.ReadAllText(checksumPath);
        var decryptedContent = Crypt.DecryptFromBase64(encryptedContent);
        var lines = decryptedContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (line.Contains(assetNameSearch, StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1) return parts[0];
            }
        }
        return null;
    }

    public async Task RunUpdateAsync(GitVersion latestVersion, string selectedVersionType, bool clearAppData, bool forceRedownload, IProgress<UpdateProgressInfo>? progress)
    {
        var assetNameSearch = selectedVersionType == "Portable" ? "win-x64-portable.zip" : "win-x64-net-install.zip";
        var tempDir = AppPaths.UpdateCacheDirectory;
        var zipPath = Path.Combine(tempDir, "update.zip");
        var checksumPath = Path.Combine(tempDir, "checksums.txt");
        
        bool skipDownload = false;
        if (!forceRedownload && File.Exists(zipPath) && File.Exists(checksumPath))
        {
            progress?.Report(new UpdateProgressInfo { ProgressText = "Verifying cached download...", IsProgressIndeterminate = true });
            var expectedHash = GetHashFromChecksumFile(checksumPath, assetNameSearch);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(zipPath);
            var computedHashBytes = sha256.ComputeHash(stream);
            var computedHash = BitConverter.ToString(computedHashBytes).Replace("-", "").ToLowerInvariant();

            if (expectedHash != null && expectedHash.Equals(computedHash, StringComparison.OrdinalIgnoreCase))
            {
                skipDownload = true;
            }
        }

        using var client = new HttpClient();
        var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "Unknown";
        var productInfo = new ProductInfoHeaderValue("AutoMidiPlayer", currentVersion);
        client.DefaultRequestHeaders.UserAgent.Add(productInfo);

        if (!skipDownload)
        {
            var asset = latestVersion.Assets.FirstOrDefault(a => a.Name.Contains(assetNameSearch, StringComparison.OrdinalIgnoreCase));
            if (asset == null)
                throw new Exception($"Could not find {selectedVersionType} zip in the release assets.");

            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);
            
            var checksumAsset = latestVersion.Assets.FirstOrDefault(a => a.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase));
            if (checksumAsset == null)
            {
                throw new Exception("Security check failed: checksums.txt is missing from the GitHub release. Refusing to install unverified update.");
            }

            var checksumContent = await client.GetStringAsync(checksumAsset.DownloadUrl);
            await File.WriteAllTextAsync(checksumPath, Crypt.EncryptToBase64(checksumContent));

            progress?.Report(new UpdateProgressInfo { ProgressText = "Downloading update...", ProgressDetailText = "", ProgressPercentage = 0 });
        
        using var response = await client.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? -1L;

        await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        await using (var contentStream = await response.Content.ReadAsStreamAsync())
        {
            var buffer = new byte[81920];
            var isMoreToRead = true;
            long totalRead = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lastUpdate = stopwatch.Elapsed;

            do
            {
                var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    isMoreToRead = false;
                }
                else
                {
                    await fs.WriteAsync(buffer, 0, read);
                    totalRead += read;

                    // Only update UI every 50ms to prevent lag
                    if ((stopwatch.Elapsed - lastUpdate).TotalMilliseconds > 50 || !isMoreToRead)
                    {
                        lastUpdate = stopwatch.Elapsed;
                        var progressInfo = new UpdateProgressInfo { ProgressText = "Downloading update..." };
                        if (totalBytes != -1)
                        {
                            progressInfo.ProgressPercentage = Math.Round((double)totalRead / totalBytes * 100, 1);

                            var mbDownloaded = totalRead / 1048576.0;
                            var mbTotal = totalBytes / 1048576.0;
                            var mbPerSec = mbDownloaded / stopwatch.Elapsed.TotalSeconds;

                            progressInfo.ProgressDetailText = $"{mbDownloaded:F1} / {mbTotal:F1} MB ({mbPerSec:F1} MB/s)";
                        }
                        else
                        {
                            var mbDownloaded = totalRead / 1048576.0;
                            var mbPerSec = mbDownloaded / stopwatch.Elapsed.TotalSeconds;
                            progressInfo.ProgressDetailText = $"{mbDownloaded:F1} MB downloaded ({mbPerSec:F1} MB/s)";
                        }
                        progress?.Report(progressInfo);
                    }
                }
            }
            while (isMoreToRead);
        }
        }

        progress?.Report(new UpdateProgressInfo { ProgressText = "Verifying download...", IsProgressIndeterminate = true });

        await Task.Run(() =>
        {
            var expectedHash = GetHashFromChecksumFile(checksumPath, assetNameSearch);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(zipPath);
            var computedHashBytes = sha256.ComputeHash(stream);
            var computedHash = BitConverter.ToString(computedHashBytes).Replace("-", "").ToLowerInvariant();

            if (expectedHash == null)
            {
                throw new Exception("Hash verification failed. Missing checksums.txt in the downloaded update.");
            }

            if (!expectedHash.Equals(computedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("Hash verification failed. The downloaded file is corrupt.");
            }
        });

        progress?.Report(new UpdateProgressInfo { ProgressText = "Extracting update...", IsProgressIndeterminate = false });
        
        await Task.Run(() => 
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var totalEntries = archive.Entries.Count;
            var extractedCount = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lastUpdate = stopwatch.Elapsed;

            foreach (var entry in archive.Entries)
            {
                var destinationPath = Path.GetFullPath(Path.Combine(tempDir, entry.FullName));
                if (!destinationPath.StartsWith(Path.GetFullPath(tempDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                }
                else
                {
                    var dir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                        
                    entry.ExtractToFile(destinationPath, true);
                }

                extractedCount++;

                if ((stopwatch.Elapsed - lastUpdate).TotalMilliseconds > 50 || extractedCount == totalEntries)
                {
                    lastUpdate = stopwatch.Elapsed;
                    var progressInfo = new UpdateProgressInfo 
                    { 
                        ProgressText = "Extracting update...",
                        ProgressPercentage = Math.Round((double)extractedCount / totalEntries * 100, 1),
                        ProgressDetailText = $"{extractedCount} / {totalEntries} files"
                    };
                    progress?.Report(progressInfo);
                }
            }
        });

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var executablePath = Environment.ProcessPath;
        var currentProcessId = Environment.ProcessId;

        var escapedTempPath = tempDir.Replace("'", "''");
        var escapedAppPath = appDir.Replace("'", "''");
        var escapedExecutablePath = executablePath?.Replace("'", "''") ?? "";
        var escapedStatusFilePath = AppPaths.AppStatusFilePath.Replace("'", "''");
        var escapedAppDataPath = AppPaths.AppDataDirectory.Replace("'", "''");
        
        var clearDataCommand = clearAppData 
            ? $"Get-ChildItem -LiteralPath '{escapedAppDataPath}' -Exclude 'cache' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; " +
                $"if (Test-Path '{escapedAppDataPath}\\cache') {{ Get-ChildItem -LiteralPath '{escapedAppDataPath}\\cache' -Exclude 'update' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue; }} "
            : "";

        var updateStatusString = $"[{DateTime.Now:HH:mm:ss}] UPDATE: v{currentVersion} -> v{latestVersion.Version}";
        var encryptedStatusString = Crypt.EncryptToBase64(updateStatusString);

        var updateCommand = $"Start-Sleep -Seconds 1; " +
                            $"Wait-Process -Id {currentProcessId} -ErrorAction SilentlyContinue; " +
                            $"Remove-Item -Path '{escapedTempPath}\\update.zip' -Force -ErrorAction SilentlyContinue; " +
                            clearDataCommand +
                            $"Copy-Item -Path '{escapedTempPath}\\*' -Destination '{escapedAppPath}' -Recurse -Force; " +
                            $"New-Item -ItemType Directory -Path '{escapedAppDataPath}' -Force -ErrorAction SilentlyContinue | Out-Null; " +
                            $"Set-Content -Path '{escapedStatusFilePath}' -Value '{encryptedStatusString}' -Force; " +
                            $"Start-Process -FilePath '{escapedExecutablePath}'; " +
                            $"Start-Sleep -Seconds 2; " +
                            $"Remove-Item -LiteralPath '{escapedTempPath}' -Recurse -Force -ErrorAction SilentlyContinue;";

        var arguments = $"-NoProfile -WindowStyle Hidden -Command \"{updateCommand}\"";

        try
        {
            StartHelperProcess("pwsh.exe", arguments);
        }
        catch (Win32Exception)
        {
            try
            {
                StartHelperProcess("powershell.exe", arguments);
            }
            catch (Win32Exception)
            {
                throw new Exception("Failed to start PowerShell for update.");
            }
        }
    }

    private static void StartHelperProcess(string shellPath, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = shellPath,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Failed to start helper process.");
    }
}

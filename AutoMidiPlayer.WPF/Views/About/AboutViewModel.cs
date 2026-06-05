using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using AutoMidiPlayer.Data;
using AutoMidiPlayer.WPF.Dialogs;
using Stylet;

namespace AutoMidiPlayer.WPF.ViewModels;

public sealed class ThirdPartyLicense
{
    public ThirdPartyLicense(string name, string version, string licenseName, string licenseText, string url = "")
    {
        Name = name;
        Version = version;
        LicenseName = licenseName;
        LicenseText = licenseText;
        Url = url;
    }

    public string Name { get; }

    public string Version { get; }

    public string LicenseName { get; }

    public string LicenseText { get; }

    public string Url { get; }

    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    public string DisplayName => string.IsNullOrWhiteSpace(Version) ? Name : $"{Name} {Version}";

    public string DialogSubtitle => string.IsNullOrWhiteSpace(Version) ? LicenseName : $"{LicenseName} • {Version}";
}

public sealed class Contributor : PropertyChangedBase
{
    public Contributor(string name, string profileUrl, string avatarUrl)
    {
        Name = name;
        ProfileUrl = profileUrl;
        AvatarUrl = avatarUrl;
    }

    public string Name { get; }

    public string ProfileUrl { get; }

    public string AvatarUrl { get; }

    public string? AvatarPath { get; set; }
}

public sealed class LinkItem
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Tooltip { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public Wpf.Ui.Controls.SymbolRegular IconSymbol => Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(Icon, out var symbol) ? symbol : Wpf.Ui.Controls.SymbolRegular.Link24;
}

public class AboutViewModel : Screen
{
    private const string AppDisplayName = "Auto MIDI Player";
    private static readonly HttpClient _httpClient = new();

    public string AppName => AppDisplayName;

    public string VersionDisplay => SettingsPageViewModel.ProgramVersionDisplay;

    public BindableCollection<LinkItem> Links { get; } = new();

    public BindableCollection<Contributor> Contributors { get; } = new();

    public BindableCollection<ThirdPartyLicense> ThirdPartyLicenses { get; } = new();

    protected override async void OnInitialActivate()
    {
        base.OnInitialActivate();

        LoadLinks();
        LoadLicenses();
        await LoadContributorsAsync();
    }

    private void LoadLinks()
    {
        Links.Clear();
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "about-links.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var links = JsonSerializer.Deserialize<List<LinkItem>>(json);
                if (links != null)
                {
                    Links.AddRange(links);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }

    private void LoadLicenses()
    {
        ThirdPartyLicenses.Clear();
        try
        {
            ThirdPartyLicenses.Add(new ThirdPartyLicense(AppDisplayName, SettingsPageViewModel.ProgramVersionDisplay, "GNU GPL v3.0", GetAppLicense()));

            var referencedAssemblies = System.Reflection.Assembly.GetExecutingAssembly().GetReferencedAssemblies();
            var packageVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Try to read .csproj for versions (especially for development-time only packages like Fody)
            try
            {
                var searchPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "AutoMidiPlayer.WPF.csproj"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "AutoMidiPlayer.WPF", "AutoMidiPlayer.WPF.csproj")
                };

                foreach (var csprojPath in searchPaths)
                {
                    if (File.Exists(csprojPath))
                    {
                        var csprojText = File.ReadAllText(csprojPath);
                        var matches = System.Text.RegularExpressions.Regex.Matches(csprojText, @"<PackageReference\s+Include=""([^""]+)""\s+Version=""([^""]+)""");
                        foreach (System.Text.RegularExpressions.Match match in matches)
                        {
                            if (match.Groups.Count >= 3)
                            {
                                packageVersions[match.Groups[1].Value] = match.Groups[2].Value;
                            }
                        }
                    }
                }
            }
            catch { }

            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "THIRD-PARTY-LICENSES.md");
            if (!File.Exists(path))
                return;

            var lines = File.ReadAllLines(path);
            string currentName = string.Empty;
            string currentVersion = string.Empty;
            string currentLicenseName = "MIT License";
            string currentUrl = string.Empty;
            List<string> currentText = new();

            void AddCurrentLicense()
            {
                if (!string.IsNullOrWhiteSpace(currentName) && currentText.Count > 0)
                {
                    string lName = currentLicenseName;
                    foreach (var line in currentText)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            lName = line;
                            break;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(currentVersion))
                    {
                        var normalizedCurrentName = currentName.Replace(" ", "").Replace(".", "");
                        
                        // First try to find in referenced assemblies
                        foreach (var asm in referencedAssemblies)
                        {
                            if (asm.Name == null) continue;
                            var normalizedAsmName = asm.Name.Replace(" ", "").Replace(".", "");
                            
                            if (normalizedAsmName.Equals(normalizedCurrentName, StringComparison.OrdinalIgnoreCase) || 
                                normalizedAsmName.Contains(normalizedCurrentName, StringComparison.OrdinalIgnoreCase) ||
                                normalizedCurrentName.Contains(normalizedAsmName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (asm.Version != null)
                                {
                                    currentVersion = $"{asm.Version.Major}.{asm.Version.Minor}.{asm.Version.Build}";
                                }
                                break;
                            }
                        }

                        // If still empty, try to find in parsed csproj packages
                        if (string.IsNullOrWhiteSpace(currentVersion))
                        {
                            foreach (var pkg in packageVersions)
                            {
                                var normalizedPkgName = pkg.Key.Replace(" ", "").Replace(".", "");
                                if (normalizedPkgName.Equals(normalizedCurrentName, StringComparison.OrdinalIgnoreCase) || 
                                    normalizedPkgName.Contains(normalizedCurrentName, StringComparison.OrdinalIgnoreCase) ||
                                    normalizedCurrentName.Contains(normalizedPkgName, StringComparison.OrdinalIgnoreCase))
                                {
                                    currentVersion = pkg.Value;
                                    break;
                                }
                            }
                        }
                    }

                    ThirdPartyLicenses.Add(new ThirdPartyLicense(currentName, currentVersion, lName, string.Join("\n", currentText).Trim(), currentUrl));
                }
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("# "))
                {
                    AddCurrentLicense();

                    var title = line.Substring(2).Trim();
                    currentName = title;
                    currentVersion = "";
                    currentLicenseName = "License";
                    currentUrl = "";
                    currentText.Clear();
                }
                else if (line.StartsWith(">"))
                {
                    var text = line.Length > 1 ? (line.StartsWith("> ") ? line.Substring(2) : line.Substring(1)) : "";
                    currentText.Add(text);
                }
                else if (!string.IsNullOrWhiteSpace(line) && line.Contains("](") && currentText.Count == 0)
                {
                    var bracketStart = line.IndexOf('[');
                    var bracketEnd = line.IndexOf(']');
                    var parenStart = line.IndexOf('(', bracketEnd);
                    var parenEnd = line.IndexOf(')', parenStart);

                    if (bracketStart >= 0 && bracketEnd > bracketStart)
                    {
                        var extractedName = line.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                        if (extractedName.Contains("/"))
                        {
                            var slashParts = extractedName.Split('/');
                            extractedName = slashParts[slashParts.Length - 1];
                        }
                        currentName = extractedName;

                        if (parenStart > bracketEnd && parenEnd > parenStart)
                        {
                            currentUrl = line.Substring(parenStart + 1, parenEnd - parenStart - 1);
                        }

                        var parts = line.Split(')');
                        if (parts.Length > 1)
                        {
                            currentVersion = parts[1].Trim();
                        }
                    }
                }
            }
            AddCurrentLicense();
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
        }
    }

    private static string GetAppLicense()
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE");
            if (File.Exists(path))
                return File.ReadAllText(path);

            path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LICENSE");
            if (File.Exists(path))
                return File.ReadAllText(path);
        }
        catch
        {
        }
        return "GNU General Public License v3.0\n\nPlease see the LICENSE file or visit https://www.gnu.org/licenses/gpl-3.0.html";
    }

    private async Task LoadContributorsAsync()
    {
        Contributors.Clear();
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Jed556/AutoMidiPlayer/contributors");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AutoMidiPlayer", SettingsPageViewModel.ProgramVersionDisplay));

            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                var avatarCacheDir = Path.Combine(AppPaths.AppDataDirectory, "cache", "avatars");
                if (!Directory.Exists(avatarCacheDir))
                    Directory.CreateDirectory(avatarCacheDir);

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var login = element.GetProperty("login").GetString() ?? "Unknown";

                    if (login.Equals("dependabot[bot]", StringComparison.OrdinalIgnoreCase) || 
                        login.Equals("github-actions[bot]", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var htmlUrl = element.GetProperty("html_url").GetString() ?? "";
                    var avatarUrl = element.GetProperty("avatar_url").GetString() ?? "";

                    var contributor = new Contributor(login, htmlUrl, avatarUrl);
                    Contributors.Add(contributor);

                    if (!string.IsNullOrWhiteSpace(avatarUrl))
                    {
                        var avatarPath = Path.Combine(avatarCacheDir, $"{login}.png");
                        if (!File.Exists(avatarPath))
                        {
                            try
                            {
                                var imageBytes = await _httpClient.GetByteArrayAsync(avatarUrl);
                                await File.WriteAllBytesAsync(avatarPath, imageBytes);
                            }
                            catch (Exception e)
                            {
                                Logger.LogException(e);
                            }
                        }

                        if (File.Exists(avatarPath))
                        {
                            contributor.AvatarPath = avatarPath;
                        }
                    }
                }
            }
            else
            {
                AddFallbackContributors();
            }
        }
        catch (Exception ex)
        {
            Logger.LogException(ex);
            if (Contributors.Count == 0)
            {
                AddFallbackContributors();
            }
        }
    }

    private void AddFallbackContributors()
    {
        Contributors.Add(new Contributor("sabihoshi", "https://github.com/sabihoshi", ""));
        Contributors.Add(new Contributor("Jed556", "https://github.com/Jed556", ""));

        var avatarCacheDir = Path.Combine(AppPaths.AppDataDirectory, "cache", "avatars");
        foreach (var c in Contributors)
        {
            var avatarPath = Path.Combine(avatarCacheDir, $"{c.Name}.png");
            if (File.Exists(avatarPath))
                c.AvatarPath = avatarPath;
        }
    }

    public void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception error)
        {
            Logger.LogException(error);
        }
    }

    public async Task ShowThirdPartyLicense(ThirdPartyLicense license)
    {
        if (license is null)
            return;

        try
        {
            await ThirdPartyLicenseDialog.ShowAsync(license);
        }
        catch (Exception error)
        {
            Logger.LogException(error);
        }
    }
}

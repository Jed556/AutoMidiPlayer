using System;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace AutoMidiPlayer.Data.Git;

[UsedImplicitly(ImplicitUseTargetFlags.Members)]
public class GitVersion
{
    [JsonPropertyName("draft")] public bool Draft { get; set; }

    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = "Unknown";

    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "0.0";

    [JsonPropertyName("html_url")] public string Url { get; set; } = null!;

    [JsonPropertyName("assets")] public System.Collections.Generic.List<GitAsset> Assets { get; set; } = new();

    public Version Version => new(TagName.Replace("v", string.Empty));
}

[UsedImplicitly(ImplicitUseTargetFlags.Members)]
public class GitAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("size")] public long Size { get; set; }
}

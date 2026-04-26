using System.ComponentModel;
using System.Configuration;

namespace AutoMidiPlayer.Data.Properties;

/// <summary>
/// Partial class extension for Settings.
/// Uses PortableSettingsProvider to store all user settings in %LocalAppData%\AutoMidiPlayer\user.config
/// instead of the versioned .NET default path, allowing settings to persist across updates.
/// </summary>
[SettingsProvider(typeof(PortableSettingsProvider))]
public sealed partial class Settings
{
    // Backward-compatible aliases used throughout the WPF layer.
    public bool AutoDetectBaseKey
    {
        get => AutoDetectDefaultKey;
        set => AutoDetectDefaultKey = value;
    }

    public int DefaultSongBaseKey
    {
        get => DefaultSongDefaultKey;
        set => DefaultSongDefaultKey = value;
    }

    protected override void OnPropertyChanged(object sender, PropertyChangedEventArgs e) => Save();

    protected override void OnSettingsLoaded(object sender, SettingsLoadedEventArgs e)
    {
        // Note: Upgrade() is not needed with PortableSettingsProvider since
        // settings are stored in a fixed location that persists across versions.
        // The UpgradeRequired logic is kept for backwards compatibility but won't trigger.
        if (Default.UpgradeRequired)
        {
            Default.UpgradeRequired = false;
            Default.Save();
        }
    }
}

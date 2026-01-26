using System;

namespace AutoMidiPlayer.Data.Properties;

public static class SettingsExtensions
{
    public static void Modify(this Settings settings, Action<Settings> action)
    {
        action.Invoke(settings);
        settings.Save();
    }
}

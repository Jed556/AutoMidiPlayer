using System.Collections.Generic;

namespace AutoMidiPlayer.WPF.Theme;

public class AppThemes : List<AppTheme>
{
    public AppThemes()
    {
        Add(AppTheme.Light);
        Add(AppTheme.Dark);
        Add(AppTheme.Default);
    }
}

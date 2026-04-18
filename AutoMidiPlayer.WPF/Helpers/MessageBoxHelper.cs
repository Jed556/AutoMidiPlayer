namespace AutoMidiPlayer.WPF.Helpers;

public static class MessageBoxHelper
{
    public static System.Windows.MessageBoxResult Show(
        string message,
        string title,
        System.Windows.MessageBoxButton button = System.Windows.MessageBoxButton.OK,
        System.Windows.MessageBoxImage image = System.Windows.MessageBoxImage.None)
    {
        return System.Windows.MessageBox.Show(message, title, button, image);
    }

    public static void ShowError(string message, string title)
    {
        _ = Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }

    public static void ShowWarning(string message, string title)
    {
        _ = Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    }

    public static void ShowInformation(string message, string title)
    {
        _ = Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    public static bool ConfirmOkCancel(string message, string title, System.Windows.MessageBoxImage image)
    {
        return Show(message, title, System.Windows.MessageBoxButton.OKCancel, image)
            == System.Windows.MessageBoxResult.OK;
    }
}

using System.Text.RegularExpressions;

namespace AutoMidiPlayer.WPF.Helpers;

public static class StringExtensions
{
    public static string Humanize(this string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var humanized = Regex.Replace(input, "([a-z])([A-Z])", "$1 $2").ToLower();
        return char.ToUpper(humanized[0]) + humanized.Substring(1);
    }
}
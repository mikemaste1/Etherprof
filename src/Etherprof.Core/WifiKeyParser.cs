namespace Etherprof.Core;

using System.Text.RegularExpressions;

public static class WifiKeyParser
{
    private static readonly Regex KeyContentRegex = new(
        @"(?i)(?:Key Content|Содержимое ключа)\s*:\s*(.+)",
        RegexOptions.Compiled);

    private static readonly Regex OpenNetworkRegex = new(
        @"(?i)(?:Security key\s*:\s*Absent|Ключ безопасности\s*:\s*Отсутствует)",
        RegexOptions.Compiled);

    public const string OpenNetworkDisplay = "(Open network - No password)";

    /// <summary>
    /// Extracts the cleartext Wi-Fi password or open network indicator from 'netsh wlan show profile ... key=clear' output.
    /// Supports English and localized Russian Windows output.
    /// </summary>
    public static string? ExtractKeyFromNetshOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var match = KeyContentRegex.Match(output);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        if (OpenNetworkRegex.IsMatch(output))
        {
            return OpenNetworkDisplay;
        }

        return null;
    }
}

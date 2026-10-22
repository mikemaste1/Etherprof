namespace Etherprof.Core;

using System.Text;

public static class WifiQrFormatter
{
    /// <summary>
    /// Formats a Wi-Fi connection string according to the standard WIFI: QR code protocol.
    /// Example: WIFI:S:MyNetwork;T:WPA;P:MySecretPass;;
    /// </summary>
    public static string Format(string ssid, string? password, string authType = "WPA", bool isHidden = false)
    {
        if (string.IsNullOrWhiteSpace(ssid))
            return "";

        string escapedSsid = Escape(ssid.Trim());
        bool isOpen = string.IsNullOrWhiteSpace(password) || password.StartsWith("(");

        var sb = new StringBuilder("WIFI:S:");
        sb.Append(escapedSsid);
        sb.Append(";T:");
        sb.Append(isOpen ? "nopass" : authType);

        if (!isOpen && !string.IsNullOrEmpty(password))
        {
            sb.Append(";P:");
            sb.Append(Escape(password));
        }

        if (isHidden)
        {
            sb.Append(";H:true");
        }

        sb.Append(";;");
        return sb.ToString();
    }

    private static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 4);
        foreach (char c in text)
        {
            if (c is '\\' or ';' or ',' or ':' or '"')
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}

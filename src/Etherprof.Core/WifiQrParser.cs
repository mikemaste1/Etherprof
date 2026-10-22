namespace Etherprof.Core;

using System.Text;

public record WifiQrInfo(string Ssid, string? Password, string AuthType, bool IsHidden);

public static class WifiQrParser
{
    /// <summary>
    /// Parses a standard WIFI: QR code payload.
    /// Format: WIFI:S:<SSID>;T:<auth>;P:<password>;H:<hidden>;;
    /// Handles escaped characters (\;, \:, \,, \\).
    /// </summary>
    public static bool TryParse(string? text, out WifiQrInfo? info)
    {
        info = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();
        if (!text.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase)) return false;

        string content = text[5..]; // skip "WIFI:"
        string? ssid = null;
        string? password = null;
        string authType = "WPA";
        bool isHidden = false;

        int i = 0;
        while (i < content.Length)
        {
            if (content[i] == ';')
            {
                i++;
                continue;
            }

            int colon = -1;
            int j = i;
            while (j < content.Length)
            {
                if (content[j] == ':' && colon == -1)
                {
                    colon = j;
                    break;
                }
                if (content[j] == ';') break;
                j++;
            }

            if (colon == -1) break;

            string key = content[i..colon];
            int valStart = colon + 1;
            var valSb = new StringBuilder();
            j = valStart;
            bool escaped = false;
            while (j < content.Length)
            {
                char c = content[j];
                if (escaped)
                {
                    valSb.Append(c);
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == ';')
                {
                    j++;
                    break;
                }
                else
                {
                    valSb.Append(c);
                }
                j++;
            }
            i = j;

            string value = valSb.ToString();
            switch (key.ToUpperInvariant())
            {
                case "S":
                    ssid = value;
                    break;
                case "P":
                    password = value;
                    break;
                case "T":
                    authType = value;
                    break;
                case "H":
                    isHidden = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(ssid)) return false;

        info = new WifiQrInfo(ssid, password, authType, isHidden);
        return true;
    }
}

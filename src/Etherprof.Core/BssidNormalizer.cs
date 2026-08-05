namespace Etherprof.Core;

using System.Text.RegularExpressions;

public static class BssidNormalizer
{
    private static readonly Regex MacCleanRegex = new("^[0-9A-Fa-f]{12}$", RegexOptions.Compiled);

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("BSSID cannot be null or empty.", nameof(input));

        var clean = input.Replace(":", "").Replace("-", "").Trim();
        if (!MacCleanRegex.IsMatch(clean))
            throw new ArgumentException($"Invalid BSSID format: '{input}'.", nameof(input));

        clean = clean.ToUpperInvariant();
        return $"{clean[0..2]}:{clean[2..4]}:{clean[4..6]}:{clean[6..8]}:{clean[8..10]}:{clean[10..12]}";
    }

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var clean = input.Replace(":", "").Replace("-", "").Trim();
        if (!MacCleanRegex.IsMatch(clean))
            return false;

        clean = clean.ToUpperInvariant();
        normalized = $"{clean[0..2]}:{clean[2..4]}:{clean[4..6]}:{clean[6..8]}:{clean[8..10]}:{clean[10..12]}";
        return true;
    }
}

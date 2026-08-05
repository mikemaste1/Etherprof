namespace Etherprof.Core;

using System.Net;

public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public string? Address { get; init; }
    public byte? PrefixLength { get; init; }
    public string? ErrorMessage { get; init; }

    public static ValidationResult Valid(string address, byte prefixLength) =>
        new() { IsValid = true, Address = address, PrefixLength = prefixLength };

    public static ValidationResult Invalid(string error) =>
        new() { IsValid = false, ErrorMessage = error };
}

public static class IPv4Validator
{
    /// <summary>
    /// Validates a raw IPv4 address string (no CIDR).
    /// </summary>
    public static bool IsValidAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        return IPAddress.TryParse(address.Trim(), out var ip)
               && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
               && address.Trim().Split('.').Length == 4; // reject shorthand like "10.1"
    }

    /// <summary>
    /// Validates prefix length (0-32 inclusive, full CIDR range).
    /// </summary>
    public static bool IsValidPrefixLength(int prefixLength)
    {
        return prefixLength >= 0 && prefixLength <= 32;
    }

    /// <summary>
    /// Parses CIDR input like "192.168.1.50" or "192.168.1.50/24".
    /// If no prefix is specified, defaults to /24.
    /// </summary>
    public static ValidationResult ParseCidr(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return ValidationResult.Invalid("Address is required");

        var trimmed = input.Trim();
        string addressPart;
        byte prefixLength = 24; // default

        var slashIndex = trimmed.IndexOf('/');
        if (slashIndex >= 0)
        {
            addressPart = trimmed[..slashIndex];
            var prefixPart = trimmed[(slashIndex + 1)..];

            if (!int.TryParse(prefixPart, out var pl))
                return ValidationResult.Invalid($"Invalid prefix length: '{prefixPart}'");

            if (pl < 0 || pl > 32)
                return ValidationResult.Invalid($"Prefix length must be 0-32, got {pl}");

            prefixLength = (byte)pl;
        }
        else
        {
            addressPart = trimmed;
        }

        if (!IsValidAddress(addressPart))
            return ValidationResult.Invalid($"Invalid IPv4 address: '{addressPart}'");

        return ValidationResult.Valid(addressPart, prefixLength);
    }

    /// <summary>
    /// Validates a hostname or IP address for use as a test target.
    /// </summary>
    public static bool IsValidHostOrAddress(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var trimmed = host.Trim();
        // Accept valid IP or hostname (basic check)
        if (IsValidAddress(trimmed)) return true;
        // Basic hostname validation
        return trimmed.Length <= 253
               && !trimmed.StartsWith('-')
               && !trimmed.EndsWith('-')
               && trimmed.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '.');
    }
}

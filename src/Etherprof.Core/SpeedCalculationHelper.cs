namespace Etherprof.Core;

public static class SpeedCalculationHelper
{
    public const long Size1MiB = 1_048_576L;      // 1 MiB
    public const long Size10MiB = 10_485_760L;    // 10 MiB
    public const long Size100MiB = 104_857_600L;  // 100 MiB

    public static double CalculateMbps(long bytes, TimeSpan elapsed)
    {
        if (bytes <= 0 || elapsed.TotalSeconds <= 0)
            return 0.0;

        double bits = bytes * 8.0;
        return bits / elapsed.TotalSeconds / 1_000_000.0;
    }

    public static string FormatMbps(double mbps)
    {
        if (mbps <= 0) return "0 Mbps";
        if (mbps < 10.0) return $"{mbps:F2} Mbps";
        return $"{mbps:F1} Mbps";
    }

    public static string FormatSizeMiB(long bytes)
    {
        double mib = bytes / (1024.0 * 1024.0);
        if (Math.Abs(mib - 1.0) < 0.01) return "1 MiB";
        if (Math.Abs(mib - 10.0) < 0.01) return "10 MiB";
        if (Math.Abs(mib - 100.0) < 0.01) return "100 MiB";
        return $"{mib:F1} MiB";
    }
}

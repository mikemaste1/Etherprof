namespace Etherprof.Core;

using Etherprof.Contracts.Models;

public static class WifiBandResolver
{
    public static WifiBand Resolve(int? centerFrequencyMhz, int? channel)
    {
        if (centerFrequencyMhz.HasValue && centerFrequencyMhz.Value > 0)
        {
            var freq = centerFrequencyMhz.Value;
            if (freq >= 2400 && freq <= 2500)
                return WifiBand.GHz2_4;
            if (freq >= 4900 && freq <= 5899)
                return WifiBand.GHz5;
            if (freq >= 5925 && freq <= 7125)
                return WifiBand.GHz6;
        }

        if (channel.HasValue && channel.Value > 0)
        {
            var ch = channel.Value;
            if (ch >= 1 && ch <= 14)
                return WifiBand.GHz2_4;
            if ((ch >= 32 && ch <= 177) || (ch >= 180 && ch <= 196))
                return WifiBand.GHz5;
        }

        return WifiBand.Unknown;
    }
}

namespace Etherprof.StreamTest.Tests;

using Etherprof.StreamTest.Metrics;
using Xunit;

public class JitterCalculatorTests
{
    [Fact]
    public void JitterCalculator_ConstantLatency_ZeroJitter()
    {
        var calc = new JitterCalculator();

        // Constant transit time of 10,000 microseconds (10 ms)
        for (int i = 0; i < 10; i++)
        {
            long sendMicro = i * 20_000;
            long recvMicro = sendMicro + 10_000;
            calc.AddSample(sendMicro, recvMicro);
        }

        Assert.Equal(0.0, calc.CurrentJitterMs, precision: 3);
    }

    [Fact]
    public void JitterCalculator_VariableLatency_ComputesJitter()
    {
        var calc = new JitterCalculator();

        calc.AddSample(0, 10_000);         // sample 1: transit 10ms
        calc.AddSample(20_000, 40_000);    // sample 2: transit 20ms (transit diff 10ms)

        Assert.True(calc.CurrentJitterMs > 0);
    }
}

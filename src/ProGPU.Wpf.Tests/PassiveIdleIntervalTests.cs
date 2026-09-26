using System.Diagnostics;
using ProGPU.Wpf.ShowcaseApp;
using Xunit;

namespace ProGPU.Wpf.Tests;

public class PassiveIdleIntervalTests
{
    [Fact]
    public void ExactZeroPresentationsAllowsReportedCpuAllocationAndCollections()
    {
        var result = PassiveIdleInterval.Difference(
            new(12, 0, 50, 100, 1, 2, 3),
            new(12, Stopwatch.Frequency * 2, 10050, 500, 2, 3, 4));
        result.RequireIdle();
        Assert.Equal(0, result.ExtraPresentations);
        Assert.Equal(2000, result.WallMilliseconds);
        Assert.Equal(1, result.CpuMilliseconds);
        Assert.Equal(400, result.AllocatedBytes);
        Assert.Equal(1, result.Gen0Collections);
        Assert.Equal(1, result.Gen1Collections);
        Assert.Equal(1, result.Gen2Collections);
    }

    [Theory]
    [InlineData(12, 13)]
    [InlineData(12, 11)]
    [InlineData(0, 0)]
    public void ExtraMissingOrRegressedPresentationsFail(long before, long after)
    {
        var result = new PassiveIdleInterval.Result(before, after, 2000, 0, 0, 0, 0, 0);
        Assert.Throws<InvalidOperationException>(() => result.RequireIdle());
    }

    [Fact]
    public async Task ObservationReadsOnlyTwoEndpoints()
    {
        using var process = Process.GetCurrentProcess();
        int reads = 0;
        var result = await PassiveIdleInterval.ObserveAsync(process, () => { reads++; return 3; },
            TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, reads);
        result.RequireIdle();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5001)]
    public async Task InvalidOrUnboundedDurationFailsBeforeObservation(int milliseconds)
    {
        using var process = Process.GetCurrentProcess();
        int reads = 0;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => PassiveIdleInterval.ObserveAsync(
            process, () => { reads++; return 3; }, TimeSpan.FromMilliseconds(milliseconds)));
        Assert.Equal(0, reads);
    }
}

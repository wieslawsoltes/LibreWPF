using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace ProGPU.Wpf.ShowcaseApp;

// BCL-only endpoint observation. No dispatcher, layout, query, render request,
// memory inventory, status output, forced collection or quiet-until-pass loop.
internal static class PassiveIdleInterval
{
    internal readonly record struct Sample(long Frames, long Timestamp, long CpuTicks,
        long AllocatedBytes, int Gen0, int Gen1, int Gen2);

    internal readonly record struct Result(long FramesBefore, long FramesAfter,
        double WallMilliseconds, double CpuMilliseconds, long AllocatedBytes,
        int Gen0Collections, int Gen1Collections, int Gen2Collections)
    {
        public long ExtraPresentations => FramesAfter - FramesBefore;

        internal void RequireIdle()
        {
            if (FramesBefore <= 0 || ExtraPresentations != 0 ||
                !double.IsFinite(WallMilliseconds) || WallMilliseconds <= 0 ||
                !double.IsFinite(CpuMilliseconds) || CpuMilliseconds < 0 ||
                AllocatedBytes < 0 || Gen0Collections < 0 || Gen1Collections < 0 || Gen2Collections < 0)
                throw new InvalidOperationException($"Invalid or non-idle presentation interval: {this}.");
        }
    }

    internal static Result Difference(Sample before, Sample after) => new(
        before.Frames, after.Frames,
        (after.Timestamp - before.Timestamp) * 1000.0 / Stopwatch.Frequency,
        (after.CpuTicks - before.CpuTicks) / (double)TimeSpan.TicksPerMillisecond,
        after.AllocatedBytes - before.AllocatedBytes,
        after.Gen0 - before.Gen0, after.Gen1 - before.Gen1, after.Gen2 - before.Gen2);

    internal static async Task<Result> ObserveAsync(Process process, Func<long> readPresentedFrames,
        TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds(5))
            throw new ArgumentOutOfRangeException(nameof(duration));

        Sample before = Capture(process, readPresentedFrames);
        await Task.Delay(duration).ConfigureAwait(false);
        Sample after = Capture(process, readPresentedFrames);
        return Difference(before, after);
    }

    private static Sample Capture(Process process, Func<long> readPresentedFrames)
    {
        process.Refresh();
        long cpu = process.TotalProcessorTime.Ticks;
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        return new Sample(readPresentedFrames(), Stopwatch.GetTimestamp(), cpu, allocated, gen0, gen1, gen2);
    }
}

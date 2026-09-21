using System.Diagnostics;
using System.Windows.Media.ProGPU;
using ProGPU.Backend;
using Silk.NET.WebGPU;

internal static class NativeMilHostDeviceRecoverySmoke
{
    public static async Task RunAsync(
        ProGpuWpfWindowHost host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var initialMemory = await PollMemoryAsync(host, timeout, cancellationToken).ConfigureAwait(false);
        var injected = new TaskCompletionSource<WgpuContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        object? root = host.WpfRootVisual;
        var window = host.SilkWindow;
        host.PlatformServices.Dispatcher.Post(() =>
        {
            try
            {
                if (!host.HasPresentedFrame || host.CompositionTarget is not { } target)
                    throw new InvalidOperationException("Recovery requires an initial presented native frame.");
                target.Context.ReportDeviceLost(DeviceLostReason.Unknown,
                    "Native MIL host device-loss gate injection.");
                injected.SetResult(target.Context);
            }
            catch (Exception error)
            {
                injected.SetException(error);
            }
        });
        WgpuContext previous = await injected.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

        var elapsed = Stopwatch.StartNew();
        while ((host.RenderDeviceRecoveryCount == 0 || !host.HasPresentedFrame) && elapsed.Elapsed < timeout)
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.PlatformServices.Dispatcher.Post(() =>
        {
            try
            {
                if (host.RenderDeviceRecoveryCount != 1 || !host.HasPresentedFrame ||
                    host.CompositionTarget is not { } replacement ||
                    ReferenceEquals(previous, replacement.Context) || !previous.IsDisposed ||
                    replacement.Context.IsDeviceLost ||
                    !ReferenceEquals(root, host.WpfRootVisual) || !ReferenceEquals(window, host.SilkWindow) ||
                    host.LastNativeMilFrameMetrics.DrawCallCount == 0 ||
                    host.LastNativeMilFrameMetrics.SubmissionCount == 0)
                    throw new InvalidOperationException(
                        "Native MIL did not rebuild and present its retained root on a replacement device in the same window.");
                Console.WriteLine("Native MIL host device recovery rebuilt the target and presented in the existing window.");
                completed.SetResult();
            }
            catch (Exception error)
            {
                completed.SetException(error);
            }
        });
        await completed.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        var recoveredMemory = await PollMemoryAsync(host, timeout, cancellationToken).ConfigureAwait(false);
        if (initialMemory.CompletedMemory.EngineId == recoveredMemory.CompletedMemory.EngineId ||
            recoveredMemory.PresentedFrame.DeviceRecoveryCount != 1)
            throw new InvalidOperationException("Completed native memory must belong to the recovered engine, not its predecessor.");
        Console.WriteLine("Native memory checkpoints completed on both live engines without replacing submitted-frame snapshots.");
    }

    private static async Task<ProGpuWpfDiagnostics.NativeMemoryCheckpoint> PollMemoryAsync(
        ProGpuWpfWindowHost host, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            var pending = new TaskCompletionSource<ProGpuWpfDiagnostics.NativeMemoryCheckpoint?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            host.PlatformServices.Dispatcher.Post(() =>
            {
                try
                {
                    if (!ProGpuWpfDiagnostics.TryPollNativeMemoryCheckpoint(host, out var checkpoint))
                    {
                        pending.SetResult(null);
                        return;
                    }
                    if (!ProGpuWpfDiagnostics.TryGetNativePerformanceSnapshot(host, out var submitted) ||
                        submitted != checkpoint.PresentedFrame ||
                        checkpoint.CompletedMemory.RetainedSubmissionBatchCount != 0 ||
                        checkpoint.CompletedMemory.OwnedBufferBytes == 0)
                        throw new InvalidOperationException("Completion must retain the published snapshot and quantify the live engine.");
                    pending.SetResult(checkpoint);
                }
                catch (Exception error) { pending.SetException(error); }
            });
            var result = await pending.Task.WaitAsync(timeout - elapsed.Elapsed, cancellationToken).ConfigureAwait(false);
            if (result is { } checkpoint) return checkpoint;
            await Task.Delay(2, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("The live native engine did not reach its completed memory checkpoint.");
    }
}

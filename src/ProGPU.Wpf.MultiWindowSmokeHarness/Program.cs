using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows.Media.ProGPU;

// Multi-window native smoke for the ProGPU WPF window host, covering LibreWPF issue #102:
// opening a second top-level window on a headless X server aborted the process inside wgpu's
// GLES/EGL backend. The abort is a non-unwinding Rust panic across the native boundary, so no
// managed handler can observe it - the test is that this process runs to completion.
//
// Every window after the first is transparent, which is the shape that reproduced the report:
// AvalonDock's drop-target overlay. Transparent windows ask GLFW for a client API, and GLFW
// leaves that context current on the creating thread. Closing the first window and opening
// another then covers the second half of the problem, a new WebGPU instance whose adapter
// enumeration touches the GLES backend that live instances already hold.
internal static class Program
{
    private const string TimeoutEnvironmentVariable = "PROGPU_WPF_MULTI_WINDOW_SMOKE_TIMEOUT_SECONDS";
    private const string DisableSharingEnvironmentVariable = "PROGPU_WPF_DISABLE_RENDER_DEVICE_SHARING";
    private const int WindowCount = 3;
    private const int RequiredFrames = 3;

    private static int Main()
    {
        int timeoutSeconds = ReadTimeoutSeconds();
        Console.WriteLine(
            $"ProGPU WPF multi-window smoke starting: windows={WindowCount}, " +
            $"timeoutSeconds={timeoutSeconds}, renderDeviceSharing={!IsRenderDeviceSharingDisabled()}");

        var hosts = new List<ProGpuWpfWindowHost>(WindowCount);
        try
        {
            for (int index = 0; index < WindowCount; index++)
            {
                // Show and pump each window before creating the next one: a live, presenting
                // window and then another top-level surface is the reported sequence.
                ShowWindow(hosts, $"smoke {index + 1}", transparent: index > 0, timeoutSeconds);
            }

            AssertExactlyOneRenderDeviceOwner(hosts);

            // The windows still open keep the render device alive, so a window opened after the
            // owner closes must borrow the surviving device rather than build its own.
            ProGpuWpfWindowHost firstHost = hosts[0];
            hosts.RemoveAt(0);
            firstHost.Close();
            firstHost.Dispose();
            ProGpuWpfWindowHost reopenedHost =
                ShowWindow(hosts, "smoke reopened", transparent: true, timeoutSeconds);
            if (!IsRenderDeviceSharingDisabled() && !reopenedHost.UsesSharedRenderDevice)
            {
                throw new InvalidOperationException(
                    "Expected the window opened after the render device owner closed to borrow " +
                    "the surviving device.");
            }

            Console.WriteLine("ProGPU WPF multi-window smoke succeeded.");
            Console.Out.Flush();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            Console.Error.Flush();
            return 1;
        }
        finally
        {
            for (int index = hosts.Count - 1; index >= 0; index--)
            {
                try
                {
                    hosts[index].Dispose();
                }
                catch (Exception disposeException)
                {
                    Console.Error.WriteLine($"Window {index + 1} disposal failed: {disposeException}");
                }
            }
        }
    }

    private static ProGpuWpfWindowHost ShowWindow(
        List<ProGpuWpfWindowHost> hosts,
        string title,
        bool transparent,
        int timeoutSeconds)
    {
        var host = new ProGpuWpfWindowHost(new ProGpuWpfWindowOptions
        {
            Title = $"ProGPU WPF multi-window {title}",
            Width = 320,
            Height = 240,
            Left = 40 + (hosts.Count * 48),
            Top = 40 + (hosts.Count * 48),
            IsEventDriven = false,
            TransparentFramebuffer = transparent
        });
        hosts.Add(host);
        host.Show();
        PumpUntilPresented(hosts, timeoutSeconds);

        Console.WriteLine(
            $"'{title}' presented {host.PresentedFrameCount} frame(s): " +
            $"shared={host.UsesSharedRenderDevice}, adapter='{DescribeAdapter(host)}'");
        return host;
    }

    private static void AssertExactlyOneRenderDeviceOwner(List<ProGpuWpfWindowHost> hosts)
    {
        if (IsRenderDeviceSharingDisabled())
        {
            return;
        }

        // Exactly one window owns the process render device; every other one borrows it.
        int ownerCount = 0;
        for (int index = 0; index < hosts.Count; index++)
        {
            if (!hosts[index].UsesSharedRenderDevice)
            {
                ownerCount++;
            }
        }

        if (ownerCount != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one of {hosts.Count} windows to own the process WebGPU render " +
                $"device, but {ownerCount} created their own.");
        }
    }

    private static void PumpUntilPresented(List<ProGpuWpfWindowHost> hosts, int timeoutSeconds)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            bool presented = true;
            for (int index = 0; index < hosts.Count; index++)
            {
                // Nothing here invalidates a visual tree, so ask for each frame explicitly.
                // Presenting repeatedly exercises every window's swap chain rather than only
                // its first surface configuration.
                ProGpuWpfDiagnostics.TryRequestRender(hosts[index]);
                hosts[index].DoEvents();
                presented &= hosts[index].PresentedFrameCount >= RequiredFrames;
            }

            if (presented)
            {
                return;
            }

            Thread.Sleep(8);
        }

        throw new TimeoutException(
            $"Expected {hosts.Count} window(s) to present {RequiredFrames} frame(s) within " +
            $"{timeoutSeconds} seconds.");
    }

    private static string DescribeAdapter(ProGpuWpfWindowHost host)
    {
        // The selected backend is neither configurable nor predictable, so report it: a run that
        // behaves differently from another usually picked a different one.
        var context = host.CompositionTarget?.Context;
        return context == null
            ? "unavailable"
            : $"{context.AdapterName} ({context.AdapterBackendType})";
    }

    private static bool IsRenderDeviceSharingDisabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(DisableSharingEnvironmentVariable),
            "1",
            StringComparison.Ordinal);
    }

    private static int ReadTimeoutSeconds()
    {
        string? value = Environment.GetEnvironmentVariable(TimeoutEnvironmentVariable);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : 120;
    }
}

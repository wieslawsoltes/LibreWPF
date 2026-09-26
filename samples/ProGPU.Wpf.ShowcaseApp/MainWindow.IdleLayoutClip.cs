using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Threading;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;

namespace ProGPU.Wpf.ShowcaseApp;

public partial class MainWindow
{
    internal const string IdleLayoutClipEnvironmentVariable = "PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_VALIDATE";
    internal const string IdleLayoutClipStatusEnvironmentVariable = "PROGPU_WPF_SHOWCASE_IDLE_LAYOUT_CLIP_STATUS_PATH";
    private static readonly TimeSpan IdleSettlingTime = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleObservationTime = TimeSpan.FromSeconds(2);

    internal static void ValidateIdleLayoutClipConfiguration()
    {
        if (Environment.GetEnvironmentVariable(IdleLayoutClipEnvironmentVariable) != "1") return;
        foreach (string name in new[] { "PROGPU_WPF_SHOWCASE_VALIDATE", "PROGPU_WPF_SHOWCASE_RUN_VALIDATE",
            LiveValidationEnvironmentVariable, LivePerformanceValidationEnvironmentVariable })
            if (Environment.GetEnvironmentVariable(name) == "1")
                throw new InvalidOperationException($"Passive idle validation cannot run concurrently with {name}.");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(IdleLayoutClipStatusEnvironmentVariable)))
            throw new InvalidOperationException($"Passive idle validation requires {IdleLayoutClipStatusEnvironmentVariable}.");
    }

    private bool StartIdleLayoutClipValidationIfRequested()
    {
        if (Environment.GetEnvironmentVariable(IdleLayoutClipEnvironmentVariable) != "1") return false;
        ValidateIdleLayoutClipConfiguration();
        _liveValidationStarted = true;
        _ = Task.Run(async () =>
        {
            var receipt = new IdleLayoutClipReceipt();
            int exitCode = 1;
            try
            {
                // Caller-owned files are never replaced, including failed receipts.
                using var output = new FileStream(Environment.GetEnvironmentVariable(IdleLayoutClipStatusEnvironmentVariable)!,
                    FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                try
                {
                    receipt.AssemblyIdentities = CaptureIdleAssemblyIdentities();
                    await ValidatePassiveLayoutClipAsync(receipt).ConfigureAwait(false);
                    if (!receipt.AssemblyIdentities.SequenceEqual(CaptureIdleAssemblyIdentities()))
                        throw new InvalidOperationException("The loaded managed closure changed during idle validation.");
                    receipt.Success = true;
                    exitCode = 0;
                }
                catch (Exception ex)
                {
                    receipt.Error = receipt.Error is null ? ex.ToString() : $"{receipt.Error}\nCompletion: {ex}";
                    Console.Error.WriteLine(ex);
                }
                JsonSerializer.Serialize(output, receipt, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
                output.Flush(flushToDisk: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                exitCode = 1;
            }
            Console.WriteLine($"Showcase passive layout clip validation {(exitCode == 0 ? "succeeded" : "failed")}.");
            Environment.Exit(exitCode);
        });
        return true;
    }

    private async Task ValidatePassiveLayoutClipAsync(IdleLayoutClipReceipt receipt)
    {
        ProGpuWpfWindowHost? host = null;
        long started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(30))
        {
            if (ProGpuWpfDiagnostics.TryGetWindowHost(this, out host) && host is not null && host.HasPresentedFrame)
                break;
            await Task.Delay(LiveValidationRetryDelay).ConfigureAwait(false);
        }
        if (host is null || !host.HasPresentedFrame)
            throw new InvalidOperationException("No actual Showcase native presentation within 30 seconds.");

        using var process = Process.GetCurrentProcess();
        Func<long> readFrames = () => host.PresentedFrameCount;
        IdleFixture? fixture = null;
        try
        {
            var frameBefore = await CaptureLivePresentedFrameStateAsync(host);
            await InvokeWithLiveHostWakeAsync(host, () =>
            {
                RequireIdleNativeOwner(host);
                if (!ProGpuWpfDiagnostics.TryGetNativePerformanceSnapshot(this, out var initialNative))
                    throw new InvalidOperationException("Expected an actual native presentation snapshot.");
                var geometry = ReadLiveRenderSurfaceGeometry(host);
                var viewer = Require<ScrollViewer>(FindName("SelectorScrollViewer"), "actual Showcase ScrollViewer");
                var text = Require<TextBlock>(FindName("SelectorScrollText"), "actual Showcase scroll text");
                var panel = Require<StackPanel>(viewer.Content, "actual Showcase scroll content");
                var zero = new Border { Width = 80, Height = 0, ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.SteelBlue };
                fixture = new IdleFixture(viewer, text, panel, zero, text.Text, viewer.VerticalOffset,
                    ShowcaseTabControl.SelectedIndex, SelectorExpander.IsExpanded,
                    checked((int)geometry.LogicalWidth), checked((int)geometry.LogicalHeight),
                    host.NativeWindowHandle, host.PortablePresentationSource!, host.PortablePresentationSourceBridge!,
                    initialNative.DeviceRecoveryCount);
                ShowcaseTabControl.SelectedIndex = 3;
                SelectorExpander.IsExpanded = true;
                var paragraphs = new string[12];
                Array.Fill(paragraphs, text.Text);
                text.Text = string.Join("\n", paragraphs);
                // A real, attached FrameworkElement publishes a zero-height
                // rectangle LayoutClip. It is not collapsed or a fake DTO.
                panel.Children.Insert(0, zero);
                viewer.ScrollToTop();
                UpdateLayout();
            }, DispatcherPriority.Send);
            await WaitForLiveInputPresentedFrameAsync(host, frameBefore, "passive clipped scroll preparation");

            var initial = await ObserveIdlePhaseAsync(host, fixture!, process, readFrames, "initial", receipt);
            frameBefore = await CaptureLivePresentedFrameStateAsync(host);
            await InvokeWithLiveHostWakeAsync(host, () =>
            {
                fixture!.Viewer.ScrollToVerticalOffset(Math.Min(fixture.Viewer.ViewportHeight / 2,
                    fixture.Viewer.ScrollableHeight / 2));
                UpdateLayout();
            }, DispatcherPriority.Send);
            await WaitForLiveInputPresentedFrameAsync(host, frameBefore, "passive clipped content scroll");
            var scrolled = await ObserveIdlePhaseAsync(host, fixture!, process, readFrames, "scrolled", receipt);
            if (initial.ScrollOffset != 0 || scrolled.ScrollOffset <= 0 ||
                Math.Abs(scrolled.TextTop - initial.TextTop + scrolled.ScrollOffset) > 0.01)
                throw new InvalidOperationException("Actual source scroll placement did not follow its pixel offset.");

            int resizedWidth = checked(fixture!.OriginalWidth + 140);
            int resizedHeight = checked(fixture.OriginalHeight + 80);
            await InvokeWithLiveHostWakeAsync(host, () => SetLiveNativeWindowSize(host, resizedWidth, resizedHeight), DispatcherPriority.Send);
            await WaitForLiveNativeResizeAsync(host, (uint)resizedWidth, (uint)resizedHeight, "passive native resize",
                layout => layout.ContentWidth >= initial.ContentWidth + 80 && layout.ContentHeight >= initial.ContentHeight + 40);
            var resized = await ObserveIdlePhaseAsync(host, fixture, process, readFrames, "native-resized", receipt);
            if (resized.Geometry == initial.Geometry)
                throw new InvalidOperationException("The native resize did not change the actual surface geometry.");

            await InvokeWithLiveHostWakeAsync(host, () =>
            {
                fixture.Viewer.ScrollToTop();
                SetLiveNativeWindowSize(host, fixture.OriginalWidth, fixture.OriginalHeight);
                UpdateLayout();
            }, DispatcherPriority.Send);
            await WaitForLiveNativeResizeAsync(host, (uint)fixture.OriginalWidth, (uint)fixture.OriginalHeight, "passive native restore",
                layout => layout.ContentWidth <= resized.ContentWidth - 80 && layout.ContentHeight <= resized.ContentHeight - 40);
            var restored = await ObserveIdlePhaseAsync(host, fixture, process, readFrames, "restored", receipt);
            if (restored.Geometry != initial.Geometry || restored.ScrollOffset != 0 ||
                restored.ContentWidth != initial.ContentWidth || restored.ContentHeight != initial.ContentHeight)
                throw new InvalidOperationException("Restored native surface and source viewport differ from the initial phase.");
        }
        catch (Exception ex)
        {
            receipt.Error = ex.ToString();
            throw;
        }
        finally
        {
            if (fixture is not null)
            {
                await InvokeWithLiveHostWakeAsync(host, () =>
                {
                    fixture.Panel.Children.Remove(fixture.Zero);
                    fixture.Text.Text = fixture.OriginalText;
                    fixture.Viewer.ScrollToVerticalOffset(fixture.OriginalOffset);
                    SelectorExpander.IsExpanded = fixture.OriginalExpanded;
                    ShowcaseTabControl.SelectedIndex = fixture.OriginalTab;
                    SetLiveNativeWindowSize(host, fixture.OriginalWidth, fixture.OriginalHeight);
                    UpdateLayout();
                    if (fixture.Panel.Children.Contains(fixture.Zero) || fixture.Text.Text != fixture.OriginalText ||
                        SelectorExpander.IsExpanded != fixture.OriginalExpanded || ShowcaseTabControl.SelectedIndex != fixture.OriginalTab)
                        throw new InvalidOperationException("The original Showcase UI was not restored.");
                    receipt.UiRestored = true;
                }, DispatcherPriority.Send);
            }
        }
    }

    private async Task<IdleSourceState> ObserveIdlePhaseAsync(ProGpuWpfWindowHost host, IdleFixture fixture,
        Process process, Func<long> readFrames, string name, IdleLayoutClipReceipt receipt)
    {
        // Fixed settling, not a retry-until-quiet loop. A continuously redrawing
        // baseline still produces bounded metrics and then fails exact zero.
        await Task.Delay(IdleSettlingTime).ConfigureAwait(false);
        // Wake only the native event loop to read source state, never request a
        // render immediately before measuring. This remains outside the interval.
        IdleSourceState before = await InvokeWithLiveNativeLoopWakeAsync(host,
            () => ReadIdleSourceState(host, fixture), DispatcherPriority.Send);
        PassiveIdleInterval.Result interval = await PassiveIdleInterval.ObserveAsync(process, readFrames,
            IdleObservationTime).ConfigureAwait(false);
        var phase = new IdlePhaseReceipt(name, interval, before);
        receipt.Phases.Add(phase); // Preserve measured failure evidence before validation.
        IdleSourceState after = await InvokeWithLiveNativeLoopWakeAsync(host,
            () => ReadIdleSourceState(host, fixture), DispatcherPriority.Send);
        phase.StableIdentity = before == after;
        if (!phase.StableIdentity)
            throw new InvalidOperationException($"{name}: source/native identity, geometry or recovery changed during observation.");
        interval.RequireIdle();
        return after;
    }

    private void RequireIdleNativeOwner(ProGpuWpfWindowHost host)
    {
        if (!string.Equals(AppContext.GetData("LibreWPF.RequestedRendererMode") as string, "NativeMilWgpu", StringComparison.Ordinal) ||
            PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable || !PortableWpfRuntime.IsMediaBackendFrozen ||
            !ProGpuWpfDiagnostics.TryGetWindowHost(this, out var actual) || !ReferenceEquals(actual, host) ||
            !ReferenceEquals(host.WpfRootVisual, this) || !ReferenceEquals(host.PortablePresentationSourceBridge?.RootVisual, this) ||
            host.PortablePresentationSource is null || !host.NativeWindowHandle.IsValid ||
            !host.HasPresentedFrame || host.LastNativeMilSessionFrame is null)
            throw new InvalidOperationException("Passive validation requires the actual frozen portable Window root and its native MIL owner; no fallback.");
    }

    private IdleSourceState ReadIdleSourceState(ProGpuWpfWindowHost host, IdleFixture fixture)
    {
        RequireIdleNativeOwner(host);
        if (host.NativeWindowHandle != fixture.NativeWindow || !ReferenceEquals(host.PortablePresentationSource, fixture.Source) ||
            !ReferenceEquals(host.PortablePresentationSourceBridge, fixture.Bridge) ||
            !ReferenceEquals(fixture.Zero.Parent, fixture.Panel) || !ReferenceEquals(fixture.Viewer.Content, fixture.Panel) ||
            !fixture.Zero.IsArrangeValid || !fixture.Viewer.IsArrangeValid ||
            fixture.Viewer.CanContentScroll || fixture.Viewer.ScrollableHeight <= fixture.Viewer.ViewportHeight)
            throw new InvalidOperationException("Passive Showcase source/native ownership or actual overflowing pixel-scroll fixture changed.");
        var presenter = Require<ScrollContentPresenter>(fixture.Viewer.Template.FindName("PART_ScrollContentPresenter", fixture.Viewer),
            "actual Showcase ScrollContentPresenter");
        IdleRect clip = ReadIdleRectangleClip(presenter);
        IdleRect zero = ReadIdleRectangleClip(fixture.Zero);
        if (clip.Width <= 0 || clip.Height <= 0 || clip != new IdleRect(0, 0, presenter.ActualWidth, presenter.ActualHeight, false) ||
            zero.IsEmpty || zero != new IdleRect(0, 0, 80, 0, false))
            throw new InvalidOperationException("Actual source clips must include the viewport and a non-Empty zero-height rectangle.");
        if (!ProGpuWpfDiagnostics.TryGetNativePerformanceSnapshot(this, out var native) || native.PresentedFrameCount <= 0 ||
            native.Frame.CommandCount == 0 || native.Frame.DrawCallCount == 0 || native.Frame.SubmissionCount == 0 ||
            native.DeviceRecoveryCount != fixture.OriginalRecovery)
            throw new InvalidOperationException("No genuine native command/draw/submission frame for passive Showcase observation.");
        var content = Require<FrameworkElement>(Content, "Showcase root content");
        var geometry = ReadLiveRenderSurfaceGeometry(host);
        var presented = ReadLivePresentedFrameState(host);
        if (!presented.HasPresentedFrame || presented.LogicalWidth != geometry.LogicalWidth ||
            presented.LogicalHeight != geometry.LogicalHeight || presented.PixelWidth != geometry.PixelWidth ||
            presented.PixelHeight != geometry.PixelHeight || presented.DpiScale != geometry.DpiScale)
            throw new InvalidOperationException("The actual native presented frame does not match the current surface extent.");
        return new(geometry, presented, fixture.Text.Text, fixture.Viewer.VerticalOffset,
            fixture.Text.TranslatePoint(new Point(), presenter).Y, content.ActualWidth, content.ActualHeight,
            clip, zero, native.DeviceRecoveryCount, native.Frame.CommandCount, native.Frame.DrawCallCount, native.Frame.SubmissionCount);
    }

    private static IdleRect ReadIdleRectangleClip(FrameworkElement element)
    {
        if (!((IPortableVisualLayoutStateSource)element).TryGetPortableVisualLayoutState(out var state) || !state.HasLayoutClip ||
            state.LayoutClip is not IPortablePrimitiveGeometrySource source || !source.TryGetPortablePrimitiveGeometry(out var primitive) ||
            primitive.Kind != PortablePrimitiveGeometryKind.Rectangle || primitive.Rect.IsEmpty ||
            !primitive.Transform.IsIdentity)
            throw new InvalidOperationException("Expected actual source identity-transformed rectangular LayoutClip.");
        return new(primitive.Rect.X, primitive.Rect.Y, primitive.Rect.Width, primitive.Rect.Height, primitive.Rect.IsEmpty);
    }

    private static IdleAssemblyIdentity[] CaptureIdleAssemblyIdentities() =>
        new[] { typeof(MainWindow).Assembly, typeof(Window).Assembly, typeof(FrameworkElement).Assembly,
            typeof(Visual).Assembly, typeof(ProGpuWpfWindowHost).Assembly, typeof(IPortableVisualLayoutStateSource).Assembly }
        .Distinct().OrderBy(assembly => assembly.FullName, StringComparer.Ordinal).Select(assembly =>
        {
            using var stream = File.OpenRead(assembly.Location);
            return new IdleAssemblyIdentity(assembly.GetName().Name!, assembly.ManifestModule.ModuleVersionId.ToString(),
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        }).ToArray();

    private sealed record IdleFixture(ScrollViewer Viewer, TextBlock Text, StackPanel Panel, Border Zero,
        string OriginalText, double OriginalOffset, int OriginalTab, bool OriginalExpanded,
        int OriginalWidth, int OriginalHeight, NativeWindowHandle NativeWindow, object Source,
        WpfPortablePresentationSourceBridge Bridge, long OriginalRecovery);

    private readonly record struct IdleSourceState(LiveRenderSurfaceGeometry Geometry, LivePresentedFrameState Presented,
        string Text, double ScrollOffset,
        double TextTop, double ContentWidth, double ContentHeight, IdleRect Clip, IdleRect Zero,
        long Recovery, uint Commands, uint Draws, ulong Submissions);

    private readonly record struct IdleRect(double X, double Y, double Width, double Height, bool IsEmpty);

    private sealed record IdleAssemblyIdentity(string Name, string Mvid, string Sha256);

    private sealed class IdleLayoutClipReceipt
    {
        public int SchemaVersion => 1;
        public bool Success { get; set; }
        public bool UiRestored { get; set; }
        public string Mode => "NativeMilWgpu";
        public string SourceRoot => "Window";
        public IdleAssemblyIdentity[] AssemblyIdentities { get; set; } = [];
        public List<IdlePhaseReceipt> Phases { get; } = new(4);
        public string? Error { get; set; }
    }

    private sealed class IdlePhaseReceipt(string name, PassiveIdleInterval.Result interval, IdleSourceState state)
    {
        public string Name => name;
        public PassiveIdleInterval.Result Interval => interval;
        public bool StableIdentity { get; set; }
        public uint LogicalWidth => state.Geometry.LogicalWidth;
        public uint LogicalHeight => state.Geometry.LogicalHeight;
        public uint PixelWidth => state.Geometry.PixelWidth;
        public uint PixelHeight => state.Geometry.PixelHeight;
        public double DpiScale => state.Geometry.DpiScale;
        public double ScrollOffset => state.ScrollOffset;
        public double ZeroClipWidth => state.Zero.Width;
        public double ZeroClipHeight => state.Zero.Height;
        public bool ZeroClipIsEmpty => state.Zero.IsEmpty;
        public long DeviceRecoveryCount => state.Recovery;
        public uint CommandCount => state.Commands;
        public uint DrawCallCount => state.Draws;
        public ulong SubmissionCount => state.Submissions;
    }
}

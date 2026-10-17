// Failure-only startup hook. Never a product component or acceptance observer.
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class StartupHook
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
    private static readonly EventHandler Updated = (_, _) => Observe();
    private static readonly Dictionary<object, HashSet<long>> Frames = new(ReferenceEqualityComparer.Instance);
    private static Timer? timer;
    private static string? output;
    private static object? owner;
    private static EventInfo? update;
    private static int pending, observations, streams, stopped;
    private static long bytes;
    private static double lastPoll = double.NegativeInfinity;

    public static void Initialize()
    {
        try
        {
            output = Environment.GetEnvironmentVariable("PROGPU_MODAL_POPUP_OBSERVER_ROOT");
            string? expected = Environment.GetEnvironmentVariable("PROGPU_MODAL_POPUP_OBSERVER_APP_SHA256");
            string? process = Environment.ProcessPath;
            if (!OperatingSystem.IsWindows() || output is null || !Path.IsPathFullyQualified(output) ||
                !Directory.Exists(output) || process is null ||
                Path.GetFileName(process) != "ProGPU.Wpf.ModalInteractionApp.exe" ||
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
                Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
                Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") != "wieslawsoltes/LibreWPF" ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(process))), expected,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Exact portable modal application on disposable hosted Windows required.");
            Save("start.json", new { diagnosticOnly = true, qualified = false, pid = Environment.ProcessId,
                expected, stopSeconds = 55, maximumObservations = 64, maximumSceneBytes = 32 * 1024 * 1024 });
            timer = new Timer(FindOwner, null, 100, 100);
        }
        catch (Exception error) { Fail(error); }
    }

    private static object? Property(object value, string name) =>
        value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);

    private static object? Field(object value, string name) =>
        value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(value);

    private static void FindOwner(object? _)
    {
        if (Volatile.Read(ref stopped) != 0 || Clock.Elapsed.TotalSeconds >= 55) { timer?.Dispose(); return; }
        if (Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
        bool queued = false;
        try
        {
            Assembly? framework = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "PresentationFramework");
            object? app = framework?.GetType("System.Windows.Application")?.GetProperty("Current")?.GetValue(null);
            object? dispatcher = app is null ? null : Property(app, "Dispatcher");
            if (dispatcher is null) return;
            MethodInfo begin = dispatcher.GetType().GetMethod("BeginInvoke", [typeof(Delegate), typeof(object[])])
                ?? throw new MissingMethodException("Original dispatcher unavailable.");
            Action action = () =>
            {
                try { if (owner is null) Attach(app!); else Observe(); }
                catch (Exception error) { Fail(error); }
                finally { Volatile.Write(ref pending, 0); }
            };
            begin.Invoke(dispatcher, [action, Array.Empty<object>()]);
            queued = true;
        }
        catch (Exception error) { Fail(error); }
        finally { if (!queued) Volatile.Write(ref pending, 0); }
    }

    private static void Attach(object app)
    {
        if (Volatile.Read(ref stopped) != 0 || owner is not null || Clock.Elapsed.TotalSeconds >= 55) return;
        Assembly? bridge = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "ProGPU.Wpf");
        MethodInfo? getHost = bridge?.GetType("System.Windows.Media.ProGPU.ProGpuWpfDiagnostics")?.GetMethod("TryGetWindowHost");
        if (getHost is null || Property(app, "Windows") is not IEnumerable windows) return;
        object[] matches = windows.Cast<object>().Where(w => Equals(Property(w, "Name"), "OwnerWindow") &&
            Property(w, "Title") is string title && title.StartsWith("WpfModalInteractionApp [", StringComparison.Ordinal)).ToArray();
        if (matches.Length == 0) return;
        if (matches.Length != 1) throw new InvalidOperationException("Ambiguous original modal owner.");
        object?[] args = [matches[0], null];
        if (!Equals(getHost.Invoke(null, args), true) || args[1] is null) return;
        object host = args[1]!;
        update = host.GetType().GetEvent("UpdateTick", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException("Original owner-loop event unavailable.");
        update.GetAddMethod(true)!.Invoke(host, [Updated]);
        owner = host;
    }

    private static void Observe()
    {
        try
        {
            double now = Clock.Elapsed.TotalSeconds;
            if (now >= 55 || observations >= 64) { Stop(); return; }
            if (now - lastPoll < .1) return;
            object host = owner ?? throw new InvalidOperationException("No original owner.");
            if (Field(host, "_portablePopupBridges") is not IEnumerable bridges)
                throw new MissingMemberException("Original popup inventory unavailable.");
            object[] inventory = bridges.Cast<object>().ToArray();
            if (inventory.Length > 32) throw new InvalidOperationException("Popup inventory exceeds bound.");
            foreach (object popup in inventory)
            {
                if (observations >= 64 || Clock.Elapsed.TotalSeconds >= 55) break;
                if (!Equals(Property(popup, "IsVisible"), true)) continue;
                object? nativeHost = Field(popup, "_nativeHost");
                object? popupHost = nativeHost is null ? null : Field(nativeHost, "_popupHost");
                if (popupHost is null) continue;
                object? retained = Property(popupHost, "LastNativeMilSessionFrame");
                long frame = (long)Property(popupHost, "PresentedFrameCount")!;
                object? scene = retained is null ? null : Property(retained, "Scene");
                if (!Frames.TryGetValue(popupHost, out HashSet<long>? seen)) Frames.Add(popupHost, seen = []);
                int index = ++observations;
                string? sceneFile = null, sceneSha = null;
                if (frame > 0 && scene is not null && seen.Count < 2 && !seen.Contains(frame) && streams < 8)
                {
                    byte[] stream = Property(scene, "Stream") as byte[]
                        ?? throw new InvalidOperationException("Original scene storage unavailable.");
                    if (stream.Length is <= 0 or > 4 * 1024 * 1024 || bytes + stream.Length > 32 * 1024 * 1024)
                        throw new InvalidOperationException("Original scene exceeds diagnostic byte bound.");
                    sceneFile = $"popup-{index:D3}.bin";
                    using FileStream file = new(Path.Combine(output!, sceneFile), FileMode.CreateNew, FileAccess.Write);
                    file.Write(stream);
                    sceneSha = Convert.ToHexString(SHA256.HashData(stream));
                    seen.Add(frame); streams++; bytes += stream.Length;
                }
                MethodInfo poll = popupHost.GetType().GetMethod("TryPollNativePresentationCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new MissingMemberException("Original submission checkpoint unavailable.");
                object?[] pollArgs = [null];
                bool complete = Equals(poll.Invoke(popupHost, pollArgs), true);
                object window = Property(popupHost, "NativeWindowHandle")!;
                object? root = Property(popupHost, "WpfRootVisual");
                Type? layout = AppDomain.CurrentDomain.GetAssemblies()
                    .SingleOrDefault(a => a.GetName().Name == "PresentationFramework")?
                    .GetType("System.Windows.Controls.Primitives.LayoutInformation");
                object? slot = root is null ? null : layout?.GetMethod("GetLayoutSlot")?.Invoke(null, [root]);
                Save($"popup-{index:D3}.json", new { diagnosticOnly = true, qualified = false,
                    elapsedSeconds = Clock.Elapsed.TotalSeconds, frame, sceneFile, sceneSha,
                    nativeKind = Property(window, "Kind")?.ToString(),
                    nativeHandle = ((nint)Property(window, "Handle")!).ToInt64(),
                    sourceHandle = ((nint)Property(popup, "Handle")!).ToInt64(),
                    complete, checkpoint = complete ? pollArgs[0] : null,
                    rootType = root?.GetType().FullName,
                    rootRenderSize = root is null ? null : Size(Property(root, "RenderSize")),
                    rootDesiredSize = root is null ? null : Size(Property(root, "DesiredSize")),
                    rootLayoutSlot = slot is null ? null : new { x = Property(slot, "X"), y = Property(slot, "Y"), size = Size(slot) },
                    isRendering = Field(popupHost, "_isRendering"),
                    pendingRecovery = Field(popupHost, "_hasPendingDeviceRecovery"),
                    nativePerformance = Field(popupHost, "_nativeMilPerformance"),
                    sceneUpdate = Property(popupHost, "LastNativeMilSceneUpdateMetrics"),
                    frameAfterPoll = Property(popupHost, "PresentedFrameCount"),
                    sameRetainedFrame = ReferenceEquals(retained, Property(popupHost, "LastNativeMilSessionFrame")),
                    request = retained is null ? null : Property(retained, "Request"), metrics = scene is null ? null : Property(scene, "Metrics"),
                    build = scene is null ? null : Property(scene, "BuildResult") });
            }
            lastPoll = now;
        }
        catch (Exception error) { Fail(error); }
    }

    private static object? Size(object? value) => value is null ? null : new
    {
        width = Property(value, "Width"), height = Property(value, "Height")
    };

    private static void Stop()
    {
        Volatile.Write(ref stopped, 1);
        timer?.Dispose();
        object? host = owner;
        if (host is not null) update?.GetRemoveMethod(true)?.Invoke(host, [Updated]);
        owner = null;
    }

    private static void Save(string name, object value)
    {
        using FileStream file = new(Path.Combine(output!, name), FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(file, value, Json);
    }

    private static void Fail(Exception error)
    {
        timer?.Dispose();
        try { Stop(); } catch { }
        try { if (output is not null) Save("error.json", new { qualified = false, error = error.ToString() }); }
        catch { }
    }
}

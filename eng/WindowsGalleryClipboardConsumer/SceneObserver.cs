// Separate failure-only startup hook. Never part of a product/package or acceptance run.
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class StartupHook
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };
    private static Timer? timer;
    private static string? output;
    private static int pending;
    private static object? owner;
    private static EventInfo? updateEvent;
    private static readonly EventHandler Updated = (_, _) => Observe();
    private static readonly HashSet<long> frames = [];
    private static int polls;
    private static double lastPollSeconds = double.NegativeInfinity;

    public static void Initialize()
    {
        try
        {
            output = Environment.GetEnvironmentVariable("PROGPU_GALLERY_SCENE_OBSERVER_ROOT");
            string? expected = Environment.GetEnvironmentVariable("PROGPU_GALLERY_SCENE_OBSERVER_APP_SHA256");
            string? process = Environment.ProcessPath;
            if (!OperatingSystem.IsWindows() || output is null || !Path.IsPathFullyQualified(output) ||
                !Directory.Exists(output) || process is null || Path.GetFileName(process) != "GalleryClipboardApp.exe" ||
                Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
                Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
                Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") != "wieslawsoltes/LibreWPF" ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(process))), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Failure observer requires the exact original app in disposable hosted CI.");
            Save("scene-observer-start.json", new { diagnosticOnly = true, qualified = false,
                pid = Environment.ProcessId, appSha256 = expected, deadlineSeconds = 60, observerStopsAtSeconds = 55 });
            // Discover only assemblies already loaded by the original application.
            // At most one queued owner-thread callback; no source work or rendering is requested.
            timer = new Timer(FindOwner, null, 100, 100);
        }
        catch (Exception error) { Fail(error); }
    }

    private static object? Property(object value, string name) =>
        value.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);

    private static void FindOwner(object? _)
    {
        if (Clock.Elapsed.TotalSeconds >= 55) { timer?.Dispose(); return; }
        if (owner is not null || Interlocked.CompareExchange(ref pending, 1, 0) != 0) return;
        bool queued = false;
        try
        {
            Assembly? framework = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "PresentationFramework");
            object? app = framework?.GetType("System.Windows.Application")?.GetProperty("Current")?.GetValue(null);
            object? dispatcher = app is null ? null : Property(app, "Dispatcher");
            if (dispatcher is null) return;
            MethodInfo begin = dispatcher.GetType().GetMethod("BeginInvoke", [typeof(Delegate), typeof(object[])])
                ?? throw new MissingMethodException("Original dispatcher BeginInvoke is unavailable.");
            Action action = () =>
            {
                try { Attach(app!); }
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
        if (owner is not null || Clock.Elapsed.TotalSeconds >= 55) return;
        Assembly? bridge = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == "ProGPU.Wpf");
        MethodInfo? getHost = bridge?.GetType("System.Windows.Media.ProGPU.ProGpuWpfDiagnostics")?.GetMethod("TryGetWindowHost");
        if (getHost is null || Property(app, "Windows") is not IEnumerable windows) return;
        object[] matches = windows.Cast<object>().Where(w => Equals(Property(w, "Title"), "Gallery clipboard image qualification")).ToArray();
        if (matches.Length == 0) return;
        if (matches.Length != 1) throw new InvalidOperationException("Ambiguous original Gallery owner.");
        object?[] arguments = [matches[0], null];
        if (!Equals(getHost.Invoke(null, arguments), true) || arguments[1] is null) return;
        object host = arguments[1]!;
        updateEvent = host.GetType().GetEvent("UpdateTick", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException("Original native owner-loop event is unavailable.");
        updateEvent.GetAddMethod(true)!.Invoke(host, [Updated]);
        owner = host;
        timer?.Dispose();
        Observe();
    }

    private static void Observe()
    {
        try
        {
            if (Clock.Elapsed.TotalSeconds >= 55) { Stop(); return; }
            object host = owner ?? throw new InvalidOperationException("No observed owner.");
            if (!Equals(Property(host, "HasPresentedFrame"), true)) return;
            long frame = (long)Property(host, "PresentedFrameCount")!;
            object? retained = Property(host, "LastNativeMilSessionFrame");
            if (retained is null) return;
            object scene = Property(retained, "Scene")!;
            if (!frames.Contains(frame) && frames.Count < 2)
            {
                byte[] bytes = Property(scene, "Stream") as byte[]
                    ?? throw new InvalidOperationException("Original scene is not an owned byte array.");
                if (bytes.Length is <= 0 or > 32 * 1024 * 1024)
                    throw new InvalidOperationException("Original scene exceeds diagnostic storage bound.");
                using (FileStream file = new(Path.Combine(output!, $"scene-frame-{frame}.bin"), FileMode.CreateNew, FileAccess.Write))
                    file.Write(bytes);
                object? target = Property(host, "CompositionTarget");
                object? context = target is null ? null : Property(target, "Context");
                object native = Property(host, "NativeWindowHandle")!;
                Save($"scene-frame-{frame}.json", new { diagnosticOnly = true, qualified = false,
                    frame, bytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    request = Property(retained, "Request"), metrics = Property(scene, "Metrics"),
                    build = Property(scene, "BuildResult"),
                    nativeWindow = new { kind = Property(native, "Kind")?.ToString(),
                        handle = ((nint)Property(native, "Handle")!).ToInt64() },
                    adapter = context is null ? null : Property(context, "AdapterName"),
                    backend = context is null ? null : Property(context, "AdapterBackendType"),
                    bitmapExternalImages = Property(retained, "BitmapExternalImageSources") is ICollection images
                        ? images.Count : Property(Property(retained, "BitmapExternalImageSources")!, "Count"),
                    elapsedSeconds = Clock.Elapsed.TotalSeconds });
                frames.Add(frame);
            }
            // The exact live renderer is polled on its creating thread, without waiting,
            // rendering, source mutation or using elapsed time as GPU completion.
            // Spread the bounded observations across the original run. Consuming
            // all 16 on consecutive updates observes only the first second and
            // detaches before the original pasted frame can be retained.
            double now = Clock.Elapsed.TotalSeconds;
            if (polls >= 16 || now - lastPollSeconds < 1) return;
            lastPollSeconds = now;
            object renderer = host.GetType().GetField("_nativeMilCompositor", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host)
                ?? throw new MissingMemberException("Original native renderer is unavailable.");
            object token = renderer.GetType().GetMethod("GetLastSubmissionToken")!.Invoke(renderer, null)!;
            object complete = renderer.GetType().GetMethod("IsSubmissionComplete")!.Invoke(renderer, [token])!;
            Save($"scene-completion-{++polls}.json", new { diagnosticOnly = true, qualified = false,
                frame, token, complete, elapsedSeconds = Clock.Elapsed.TotalSeconds });
            if (frames.Count == 2 && Equals(complete, true)) Stop();
        }
        catch (Exception error) { Fail(error); }
    }

    private static void Stop()
    {
        timer?.Dispose();
        object? host = owner;
        if (host is not null) updateEvent?.GetRemoveMethod(true)?.Invoke(host, [Updated]);
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
        try { Stop(); } catch { /* Never replace the original application's result. */ }
        try { if (output is not null) Save("scene-observer-error.json", new { qualified = false, error = error.ToString() }); }
        catch { /* A diagnostic failure is never an application exception. */ }
    }
}

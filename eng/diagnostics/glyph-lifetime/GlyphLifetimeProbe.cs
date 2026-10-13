using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

internal static class GlyphLifetimeProbe
{
    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new InvalidOperationException("Disposable hosted Windows only.");
        string output = Environment.GetEnvironmentVariable("GLYPH_PROBE_OUTPUT") ?? throw new InvalidOperationException("No private output.");
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (!File.Exists(path)) throw new FileNotFoundException("Original system font is required.", path);
        var uri = new Uri(path);
        var font = new GlyphTypeface(uri);
        var modes = new List<object>();
        foreach (string mode in new[] { "retained-face", "retained-face-forced-gc", "fresh-face-forced-gc" })
        {
            using var stop = new ManualResetEventSlim();
            long collections = 0;
            Thread? collector = null;
            if (mode != "retained-face") {
                collector = new Thread(() => {
                    while (!stop.Wait(1)) {
                        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                        Interlocked.Increment(ref collections);
                    }
                }) { IsBackground = true };
                collector.Start();
            }
            long count = 0;
            var clock = Stopwatch.StartNew();
            string expected = Snapshot(NewRun(font));
            try {
                while (clock.Elapsed.TotalSeconds < 10 && count < 100000) {
                    var selected = mode == "fresh-face-forced-gc" ? new GlyphTypeface(uri) : font;
                    GlyphRun run = NewRun(selected);
                    string actual = Snapshot(run);
                    if (actual != expected) throw new InvalidOperationException("Original exported glyph data changed.");
                    var source = (IPortableNativeGlyphRunSource)run;
                    if (!source.TryGetPortableNativeGlyphRun(out var first) ||
                        !source.TryGetPortableNativeGlyphRun(out var second) || !ReferenceEquals(first, second))
                        throw new InvalidOperationException("Initialized source export lost retained identity.");
                    GC.KeepAlive(run);
                    count++;
                }
            } finally {
                stop.Set();
                if (collector is not null && !collector.Join(5000))
                    throw new InvalidOperationException("Owned collector did not retire.");
            }
            modes.Add(new { mode, count, collections, seconds = clock.Elapsed.TotalSeconds });
            File.WriteAllText(Path.Combine(output, mode + ".json"), JsonSerializer.Serialize(modes[^1]));
        }
        var modules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
            .Where(m => m.ModuleName.Contains("DirectWrite", StringComparison.OrdinalIgnoreCase) ||
                        m.ModuleName.Equals("dwrite.dll", StringComparison.OrdinalIgnoreCase) ||
                        m.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase))
            .Select(m => new { m.ModuleName, m.FileName, version = m.FileVersionInfo.FileVersion,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(m.FileName))) }).ToArray();
        var result = new { diagnosticOnly = true, qualified = false, nativeInput = false, clipboard = false,
            runtime = Environment.Version.ToString(), modes, modules, font = path,
            fontSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            sourceAssembly = typeof(GlyphRun).Assembly.Location };
        File.WriteAllText(Path.Combine(output, "glyph-lifetime.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Bounded source GlyphRun lifetime probe completed; this is not Showcase qualification.");
        return 0;
    }

    private static GlyphRun NewRun(GlyphTypeface font)
    {
        char[] text = ['A', '0', 'g'];
        ushort[] ids = text.Select(c => font.CharacterToGlyphMap[c]).ToArray();
        double[] advances = ids.Select(g => font.AdvanceWidths[g] * 16).ToArray();
        return new GlyphRun(font, 0, false, 16, 1, ids, new Point(2, 20), advances,
            null!, text, null!, null!, null!, XmlLanguage.GetLanguage("en-US"));
    }

    private static string Snapshot(GlyphRun run)
    {
        if (!((IPortableNativeGlyphRunSource)run).TryGetPortableNativeGlyphRun(out var value))
            throw new InvalidOperationException("Original native glyph export rejected.");
        return JsonSerializer.Serialize(new { value.HasInkBounds,
            bounds = new[] { value.InkBounds.X, value.InkBounds.Y, value.InkBounds.Width, value.InkBounds.Height },
            value.GlyphIndices,
            positions = value.GlyphPositions.Select(p => new[] { p.X, p.Y }),
            value.FontRenderingEmSize, value.FontUri, families = value.FontFamilyNames.Order(),
            value.IsBold, value.IsItalic });
    }
}


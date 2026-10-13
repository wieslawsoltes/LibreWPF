using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

internal static class ExitGlyphProbe
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetModuleHandleExW(uint flags, string name, out nint module);

    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted")
            throw new InvalidOperationException("Disposable hosted Windows only.");
        string output = Environment.GetEnvironmentVariable("EXIT_PROBE_OUTPUT") ?? throw new InvalidOperationException();
        string mode = Environment.GetEnvironmentVariable("EXIT_PROBE_MODE") ?? throw new InvalidOperationException();
        if (mode is not ("before-exit" or "exit-unpinned" or "exit-pinned")) throw new InvalidOperationException(mode);
        var font = new GlyphTypeface(new Uri(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf")));
        string expected = Snapshot(NewRun(font));
        // Keep a new source export uncached across the actual ProcessExit event.
        var original = NewRun(font);
        nint moduleBefore = GetModuleHandleW("dwrite.dll");
        if (moduleBefore == 0) throw new InvalidOperationException("DirectWrite was not loaded by the original font path.");
        if (mode == "exit-pinned")
        {
            // Diagnostic control only: the OS retains this exact module until
            // termination. There is no product patch or global loader policy.
            if (!GetModuleHandleExW(1, "dwrite.dll", out nint pinned) || pinned != moduleBefore)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        File.WriteAllText(Path.Combine(output, "before-exit.json"), JsonSerializer.Serialize(new
        {
            diagnosticOnly = true, qualified = false, mode, module = moduleBefore.ToInt64(),
            runtime = Environment.Version.ToString(), sourceAssembly = typeof(GlyphRun).Assembly.Location,
            expected
        }));
        void Observe()
        {
            nint moduleAtEntry = GetModuleHandleW("dwrite.dll");
            File.WriteAllText(Path.Combine(output, "callback-entered.json"), JsonSerializer.Serialize(new
            {
                mode, moduleAtEntry = moduleAtEntry.ToInt64(), shutdown = Environment.HasShutdownStarted,
                originalModule = moduleBefore.ToInt64()
            }));
            string actual = Snapshot(original);
            if (actual != expected) throw new InvalidOperationException("Source glyph export changed at shutdown.");
            File.WriteAllText(Path.Combine(output, "callback-completed.json"), JsonSerializer.Serialize(new
            {
                mode, exactSnapshot = true, moduleAfter = GetModuleHandleW("dwrite.dll").ToInt64()
            }));
            GC.KeepAlive(original);
            GC.KeepAlive(font);
        }
        if (mode == "before-exit") Observe();
        else AppDomain.CurrentDomain.ProcessExit += (_, _) => Observe();
        Console.WriteLine("Prepared original glyph source; entering actual process shutdown.");
        Environment.Exit(0);
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
            throw new InvalidOperationException("Original glyph export rejected.");
        return JsonSerializer.Serialize(new { value.HasInkBounds,
            bounds = new[] { value.InkBounds.X, value.InkBounds.Y, value.InkBounds.Width, value.InkBounds.Height },
            value.GlyphIndices, positions = value.GlyphPositions.Select(p => new[] { p.X, p.Y }),
            value.FontRenderingEmSize, value.FontUri, families = value.FontFamilyNames.Order(),
            value.IsBold, value.IsItalic });
    }
}

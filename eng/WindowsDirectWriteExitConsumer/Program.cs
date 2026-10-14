// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

internal static class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint GetModuleHandleW(string name);

    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Actual Windows DirectWrite is required; no skipped outcome is accepted.");

        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (!File.Exists(path)) throw new FileNotFoundException("The original system font is required.", path);
        var font = new GlyphTypeface(new Uri(path));
        string expected = Snapshot(NewRun(font));
        // Keep this source export uncached until the actual process-exit event.
        var original = NewRun(font);
        nint module = GetModuleHandleW("dwrite.dll");
        if (module == 0) throw new InvalidOperationException("The original font path did not load DirectWrite.");

        // Registration follows PresentationCore initialization. The old loader's
        // earlier ProcessExit callback unloaded the DLL despite these live fonts.
        // GetModuleHandle borrows its identity without extending the DLL lifetime.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (GetModuleHandleW("dwrite.dll") != module)
                throw new InvalidOperationException("DirectWrite was unloaded while original glyph sources remained live.");
            if (Snapshot(original) != expected)
                throw new InvalidOperationException("The original glyph snapshot changed during process exit.");
            GC.KeepAlive(original);
            GC.KeepAlive(font);
            Console.WriteLine($"Windows DirectWrite process-lifetime contracts passed: 2; skipped: 0; architecture: {RuntimeInformation.ProcessArchitecture}.");
        };

        Console.WriteLine("Windows DirectWrite pre-exit source control passed; shutdown export remains pending.");
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
            throw new InvalidOperationException("The original glyph export was rejected.");
        return JsonSerializer.Serialize(new
        {
            value.HasInkBounds,
            bounds = new[] { value.InkBounds.X, value.InkBounds.Y, value.InkBounds.Width, value.InkBounds.Height },
            value.GlyphIndices,
            positions = value.GlyphPositions.Select(p => new[] { p.X, p.Y }),
            value.FontRenderingEmSize,
            value.FontUri,
            families = value.FontFamilyNames.Order(),
            value.IsBold,
            value.IsItalic
        });
    }
}

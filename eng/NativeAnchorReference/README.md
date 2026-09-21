# Native WPF anchor reference

This standalone Windows program records bottomless Figure/Floater behavior from
the installed Microsoft WindowsDesktop runtime. It deliberately has no LibreWPF
or ProGPU references. Its local empty Directory.Build files prevent importing the
source-replacement repository build into this independent reference program;
they do not alter any product build, SDK admission, or validation gate.

Run on Windows with a .NET 10 SDK and WindowsDesktop runtime:

```text
dotnet run --project eng/NativeAnchorReference/NativeAnchorReference.csproj -c Release
```

The program briefly creates nonactivating windows, obtains actual TextPointer
geometry, closes each window in finally, and writes JSON to stdout. It fails on
missing geometry rather than publishing empty success. Coordinates are viewer
DIPs; the output records the actual framework identity/location and OS. Run
serially with other .NET builds. No PowerShell execution-policy change is needed.

Cases cover fixed/auto widths, left/right/center/stretch, a wrapped prefix,
sibling packing, full-width exclusion and an anchor-only paragraph. This is
reference evidence, not a ProGPU comparison or a replacement for the unchanged
application/package and Windows/macOS/Linux final gates. Exact glyph coordinates
can vary with font/runtime/DPI; retain the recorded environment when comparing.

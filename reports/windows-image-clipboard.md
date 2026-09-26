# Windows clipboard BitmapSource transport

Acceptance application: `ProGPU.Wpf.ShowcaseApp`, Editors → Data transfer →
Copy image (Windows), then Paste image (Windows). The pasted source is assigned
to a real WPF Image. The historical issue117 WPFGallery report describes a hang
and subsequent exit; no stack trace proves the cause of that hang.

## Source-backed blocker and implementation

The default net10 graph defines `PROGPU_WPF_ALIAS_WINCORE`. Its shipped
preview.45 and preview.65 PresentationCore assemblies contain the
`WpfOleServices.GetDataHere = E_NOTIMPL` and failed native-object-read branches.
Windows clipboard routing already uses real OLE; the non-Windows clipboard
registry intentionally does not replace it.

Restoring the alternate branch alone is insufficient. Canonical ProGPU
`Bitmap.GetHbitmap` explicitly rejects export without a native image adapter,
and the existing InteropBitmap HBITMAP constructor enters the legacy MIL/WIC
factory. The implementation therefore uses the shared lightweight
`WindowsGdiBitmap` helper from [ProGPU PR188](https://github.com/wieslawsoltes/ProGPU/pull/188).
The source submodule pins its exact committed implementation, not copied files.

## Aligned source and CI dependency

ProGPU PR188 is merged at `bd9d034a72e79ce6bced330a30780e970b07c89a`.
The dependency remains pinned to its exact qualified producer head
`08f4343ef15328ba742cdcf11f8eb2daeefb5f7b`, whose complete
[Build 36248366666](https://github.com/wieslawsoltes/ProGPU/actions/runs/36248366666)
passed all 43 jobs. The native package staging script requires that successful
whole Build and its live `progpu-native-package` artifact; a merge commit or an
individual successful job does not substitute for that producer identity.

The LibreWinForms dependency is the merged
[PR62](https://github.com/wieslawsoltes/LibreWinForms/pull/62) commit
`0d9145f7e6429a941261dcd96d1293e4b7633097`. Its producer head
`d9f058d4e8b7b33b83ae4844c39adced03f96587` passed all nine jobs in
[Build 36259003664](https://github.com/wieslawsoltes/LibreWinForms/actions/runs/36259003664).
Its nested ProGPU pin is the same
`08f4343ef15328ba742cdcf11f8eb2daeefb5f7b`, preserving the canonical graph's
exact-equality check. Current LibreWPF main is integrated without dropping the
clipboard consumer copy-local fix, RID-aligned output, four STA contracts, or
exact native-payload hash checks.
This aligned LibreWPF head still requires its own complete source/package CI,
including actual Windows x64 and ARM64 clipboard execution. No local heavy build
or additional VM validation was performed for this integration.

The WPF adapter validates CF_BITMAP, content aspect, lindex and the GDI medium
before encoding. The existing BMP encoder retains source pixel-format/palette
conversion. GDI receives only a validated complete BI_RGB BMP and publishes an
owned real HBITMAP after success. Caller-owned media are not overwritten.
Portable import copies native pixels while the OLE medium is alive, constructs
an independently owned BitmapSource, then releases the medium in finally.
Unexpected media and failed HRESULTs also retain release ownership. There is no
System.Drawing conversion, private fake handle or Windows portable-clipboard
reroute. Explicit native-media import retains its existing InteropBitmap path;
the untouched non-alias implementation and text/non-Windows routes stay separate.

The admitted BMP encoder formats and strict native ingress do not qualify every
WIC output format: 16-bit/bitfield/compressed BMP inputs remain rejected. CF_BITMAP
carries opaque RGB, not an alpha- or source-DPI-preserving image contract.

## Authored regression and qualification boundary

`ShowcaseClipboardImage` is compiled unchanged in the actual SDK Showcase and
source PresentationCore tests. Opaque asymmetric 3×2 pixels, padded source rows
and a partial Indexed8 palette detect row flips, channel swaps and palette loss.
The lifetime cycle flushes OLE, mutates the source, reads, clears the clipboard,
checks the retained image, freezes it, republishes and checks again. Showcase's
existing Windows package self-test invokes the actual copy/paste buttons and both
lifetime cases. Non-Windows does not claim this Windows gate or change clipboard
behavior; the added image controls are hidden there.

Source tests also construct an actual native COM data producer with real GDI or
global-memory media and a custom IUnknown release owner. They cover success,
failure with owned output, wrong TYMED, invalid export descriptors and preservation
of a caller-owned output bitmap. These are typed COM boundary tests, not fake WPF
objects.

The existing Windows x64 and ARM64 package Showcase jobs additionally compile
`eng/WindowsClipboardConsumer` against the exact package implementation assets
already staged for Showcase. It reuses the existing signed source-test friend
identity and links all four test bodies unchanged. An STA entry thread selects
portable pixel storage, invokes every body directly (no discovery or skip path),
and must finish within 60 seconds with four passed, zero skipped and the expected
process architecture. PresentationCore, WindowsBase, System.Private.Windows.Core,
PresentationNative_cor3 and shared Interop output hashes must match their exact
packages and selected RID. This is a real Windows OLE/GDI transport
gate, not a renderer fallback or a new public source API.

PresentationCore Release compiled with the repository SDK on macOS ARM64:
zero warnings and zero errors. The complete PresentationCore.Tests source project
including the four new Windows clipboard cases compiles with seven existing
warnings and zero errors. Showcase XAML/code and the small package-consumer
executable also compile with zero warnings and zero errors. These local checks
use explicit current source-built core assemblies and the existing source-built
bridge closure, not a freshly produced Windows package. They are compile-only;
the CI consumer instead uses the exact downloaded package implementation bytes.
An additional isolated Windows ARM64 source diagnostic launched the unchanged
four-body executable on .NET 10.0.5, with copied managed PE files checked by SHA-256 and
macOS native shims excluded. It failed during WindowsBase window-procedure setup
because that source-only closure lacks `PresentationNative_cor3.dll`; no test
completion marker was produced. The normal transport explicitly repacks native
WindowsDesktop runtime assets from the declared `10.0.11` packages, alongside the
separately Windows-built PresentationCore/DirectWriteForwarder and IJW host. The
SDK copies selected native runtime assets to Showcase; the clipboard consumer
then copies that declared DLL closure and verifies the selected PresentationNative
bytes before execution. No ambient WindowsDesktop DLL graft or stock managed WPF
replacement is used. The failed source probe is incomplete native staging, not
Windows image transport qualification or evidence about the reported hang. Preflight also
found and corrected the standalone consumer's inherited copy-local suppression:
its declared xUnit assertion DLL now accompanies the executable. Existing exact-head
CI and Windows x64/ARM64 package/runtime gates remain required; source compilation
and this failed diagnostic do not close issue117.

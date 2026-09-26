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

PresentationCore Release compiled with the repository SDK on macOS ARM64:
zero warnings and zero errors. The complete PresentationCore.Tests source project
including the four new Windows clipboard cases compiles with seven existing
warnings and zero errors. Showcase compilation is in progress.
No test bodies, Windows VM, GDI/OLE runtime, rendered screenshot or image-quality
comparison has run during this implementation-first batch. Existing exact-head
CI and Windows x64/ARM64 package/runtime gates remain required; source compilation
does not close issue117 or prove its hang fixed.

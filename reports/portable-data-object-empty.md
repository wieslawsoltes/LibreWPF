# Portable DataObject empty results

The SDK-built Showcase data-object/clipboard flow must be able to request a
missing format without entering Windows OLE. `DataObject.GetData` previously
used null coalescing to select between the portable and OLE implementations.
A legitimate portable `null` therefore accessed the absent OLE composition and
threw `PlatformNotSupportedException`.

Selection now depends on whether the portable data object exists, not on its
return value. Empty, populated and wrapped data objects retain their original
payloads and format metadata. String/type overloads and empty image, audio,
file-list and text helpers reuse this same public method. Argument validation
and the native Windows OLE branch are unchanged.

Four new portable source tests ran on macOS ARM64 using .NET 10.0.5: three
reproduced the original OLE exception and the argument-validation control passed.
After the fix, all four plus the two existing portable clipboard tests passed
with no skips. The source test project retains its existing warnings. Existing
Windows STA tests remain separate; the new portable tests explicitly skip on
Windows rather than claiming to exercise that implementation.

The installed-SDK runtime harness and Showcase self-test also exercise missing
formats on empty, populated and wrapped public data objects, retained payload
identity, and empty well-known formats. Their existing exact-package CI gates
remain required. A local harness build alone does not qualify those consumers.

This is not a fix or qualification for the distinct Windows clipboard-image
hang reported in issue #117, nor does it add cross-process image clipboard
support to the non-Windows text clipboard adapter.

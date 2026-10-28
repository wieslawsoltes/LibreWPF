# Portable system font discovery

The first portable text measurement constructs the system font catalog on its
calling thread. Discovery now admits each exact source URI once before opening
font files. It keeps the first occurrence, including the bundled Fluent Symbols
font's priority, and leaves distinct source files independent even when their
family names or bytes match. Case-distinct paths and symlink aliases are not
merged. Each admitted TTC still contributes every original face index in order.

The macOS root list no longer includes `/System/Library/Fonts/Supplemental`
separately: the recursive `/System/Library/Fonts` scan already covers it. Previously
both passes opened and parsed every Supplemental file, then published duplicate
faces. A read-only inventory on 28 October 2026 found 724 discovered entries but
434 exact paths, including 290 repeated Supplemental files. This identifies
duplicate work; it is not a measured startup-time improvement on the reporter's
machine.

Filtering belongs only to system discovery. Explicit/custom `FromUris` and
`FromFontSources` collections retain their existing duplicate-source behavior.
Source factories still run sequentially on the caller's thread; catalog
publication keeps its existing lock. Metadata retains the original source URI
and face index, while complete font bytes remain unloaded until selected glyph
data is needed. No persistent catalog, background callback, family substitution,
glyph placement or native rendering policy is introduced.

`PortableSystemFontDiscoveryTests` exercises overlapping/repeated directory
roots, first-seen source order, bundled priority, separate files with identical
family metadata, every slot of a shared-table TTC and explicit custom duplicates.
The existing `PortableFontDataTests` retain streamed/explicit metadata agreement
and lazy font-byte ownership. Local source validation passed all ten discovery,
font-data and collection controls without skips on macOS ARM64, using the pinned
.NET 11 SDK to build the existing net10.0 source test graph.
Actual OpenDevelop splash/first-text measurement
and cold/warm system-catalog timings remain necessary to resolve the full startup
delay reported in [LibreWPF #297](https://github.com/wieslawsoltes/LibreWPF/issues/297).

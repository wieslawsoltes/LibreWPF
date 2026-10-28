# Portable system font discovery

The first portable text measurement publishes the system font catalog on its
calling thread. Discovery admits each exact source URI once before opening
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

For at least 32 sources from the sealed built-in factory, up to four workers now
read independent file metadata. The caller captures every source and file path
first; workers invoke no source callbacks and never load complete font bytes.
Indexed result slots preserve the original source order and every TTC face order
regardless of completion order. Recoverable file/format rejection stays unchanged;
the first fatal error in source order retains its original exception type.
Smaller batches, custom factories, explicit collections and any batch containing
a non-file source keep sequential reads. The existing locked publication and
cached subsequent lookup remain unchanged.

`PortableSystemFontDiscoveryTests` exercises overlapping/repeated directory
roots, first-seen source order, bundled priority, separate files with identical
family metadata, every slot of a shared-table TTC and explicit custom duplicates.
The existing `PortableFontDataTests` retain streamed/explicit metadata agreement
and lazy font-byte ownership. Local source validation passed all ten discovery,
font-data and collection controls without skips on macOS ARM64, using the pinned
.NET 11 SDK to build the existing net10.0 source test graph.

Four additional discovery cases compare small and parallel-sized catalogs with
the original serial parser, including separate font families, malformed/missing
files, aliased TTC slots, all ten metrics, supported localized name IDs 0–19 and lazy
selected-face ownership. Custom factories retain 40 ordered caller-thread
callbacks and the exact original fatal-exception boundary. All fourteen font
discovery/data/collection cases passed locally without skips; the existing hosted
source selection now requires all eight discovery cases.

A bounded catalog measurement on the same macOS ARM64 machine ran the unchanged
source `FontCollection.FromUris` parser with the original and current discovery
sequences in four fresh processes, ordered original/current/current/original.
The first parses took 246.530, 114.543, 104.606 and 161.416 ms respectively; a
second parse in each process took 168.417, 104.425, 98.945 and 161.606 ms.
First-parse thread allocations fell from 30,168,488 to 20,417,096 bytes. Both
sequences retained all 394 families and the same first occurrence of every
original source URI/face index. The published face count fell from 1,330 to 851
by removing repeated source entries. Including the bundled symbol font, this
measurement visited 725 versus 435 URIs; the earlier 724/434 inventory counted
only system-directory entries.

A separate fresh-process call to the actual current
`Factory.GetSystemFontCollection` took 104.074 ms. Its second call returned the
same catalog instance in 0.0147 ms. The measurements used the existing source
assembly and real system fonts without a renderer or desktop host; source/font
hashes, original face order, all successful results and two diagnostic-loader
failures were retained. Each process had a 60-second bound. First-process
parsing does not mean cold operating-system disk caches, and the second parser
pass is distinct from the actual cached system-collection lookup.

A second fixed four-process comparison measured the complete actual system
catalog before and after bounded metadata reading, in serial/parallel/parallel/
serial order. First-call times were 320.014, 60.471, 60.707 and 113.739 ms;
subsequent cached calls were 0.074–0.101 ms. All 851 ordered faces, 394 families,
catalog-property/name fingerprints and original font hashes matched. Both versions
loaded the same dependency binaries. A dependency-hash preflight failure was
retained before any workload ran; the original baseline was preserved while a
separate matched dependency closure was prepared.

Parallel catalog allocation was about 19.304 MB versus 19.230 MB serial, an
additional 74 KB. Its process CPU time was about 171.8 ms, versus 114.0–197.1 ms
serial: the bounded workers reduce elapsed latency by using parallel CPU work.
No operating-system file-cache reset or application startup measurement was
performed, and every process retained its 60-second deadline.

These results demonstrate the catalog component improvement on this machine.
They do not measure the original OpenDevelop splash or first text, or identify
another startup bottleneck. That application observation remains necessary to
resolve the full delay reported in
[LibreWPF #297](https://github.com/wieslawsoltes/LibreWPF/issues/297).

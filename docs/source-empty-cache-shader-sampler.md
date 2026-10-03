# Known-empty cache sampler source checkpoint

This unpublished child of source PR #245 retains the actual initialized empty
BitmapCacheBrush target through the existing explicit empty-source sideband. A
separate shader brush handle prevents an ordinary paint using the same original
brush from acquiring shader-only empty-source authorization. Original descriptor
snapshots and visual owner identities remain shared, not fabricated replacements.
The paired native implementation is ProGPU #344 (`d60613bdca69f7967e9c94aa99a3bba4051c615f`).

An internal managed validation-only capture walks typed cache-source ownership,
including hidden descendants, without changing source visibility or manufacturing
positive source bounds. It retains the existing root-state exclusions, rejects
unsupported state, 3D, cycles and multiple parents, and disposes its recording.
Ordinary cache capture keeps its existing empty-source behavior. Fifteen authored
controls cover this graph boundary; they have not been compiled or executed.

## Publication hold: original sampler contract

Primary original-source research identified a dedicated BitmapCacheBrush shader
sampler path that obtains the cache render-target bitmap directly. The inherited
#245 full-receiving-frame ordinary-brush rendering path is therefore **not an
established shader sampling contract**. Its current frame/UV, brush mapping and
opacity assumptions must be reconciled with that original path before this child
is published. The transparent full-frame managed implementation in this checkpoint
is not pixel evidence and must not be used as an oracle.

Compiler/session controls and nested empty cache-brush dependency retention also
remain unfinished. In particular, ordinary null-target lowering must not erase an
actual nested empty dependency from the shader ownership closure. Existing prior
empty-rejection controls have not yet been updated to the new producer contract.

No builds, tests, syntax checks, verifiers, probes, GPU/UI execution or CI runs were
performed. Qualified submodule pins remain unchanged. Final source/native union
qualification, original Windows differential and application checks remain required.

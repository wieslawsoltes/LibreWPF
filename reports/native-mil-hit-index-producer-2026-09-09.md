# Native MIL hit-index producer connection — 2026-09-09

## Acceptance dependency

Acceptance application: existing Toolkit/AvalonDock. Actions: clicking docked
content, menus/popups and selection. Source-backed blocker: native presentation
uses C++ MIL scenes while retained host owner queries use the managed compositor.
The previous owner-snapshot checkpoint supplied identity, not index production.

ProGPU `0a2acfd4` adds opt-in native MIL index generation for analytic primitives
and plain path fills through the existing reusable C++ builder. No acceptance
application or final gate was reduced. Full application hit coverage and host
query routing remain blocking; this is not a native-input completion claim.
Follow-up `a4100cc8` adds C/C++ flag agreement and cached-visual rejection fixtures.
Both commits are pushed to [ProGPU PR #139](https://github.com/wieslawsoltes/ProGPU/pull/139).

## Implementation and provenance

MIL records visual handles around their own content, independently of resource
and draw IDs. Sparse builder-owned boundaries preserve signed IDs, exclude
unowned draws and prevent image merging across owners. The encoder retains
analytic/local path data, affine inverses and exact rectangular clip edges, then
uses the existing C++ quadtree/index serializer. It converts semantic fill-rule
numbering to the canonical hit shader's numbering explicitly. SIMD four-corner
placement uses NEON/SSE2; metadata/tree traversal is dependency-bound.

Unsupported draw/state/layer/cache families reject the requested index without
publishing partial coverage. Normal rendering remains unchanged and the LibreWPF
host does not yet select the new option. No per-draw native calls, managed object
pointers, reflection, new shader variant, CPU pixel fallback or readback was added.

ProGPU's [design/provenance record](../external/ProGPU/docs/native-mil-hit-test-ownership.md)
documents original in-repository algorithm sources, primary research references,
costs, paired managed/native applicability and remaining work. Native producer
fixtures cover owners, clips/transforms, fill-rule conversion, reset, unsupported
layers and canonical MIL parent/child resource reuse. A managed factory fixture
uses matched values; the module consumer exercises the public C++ API.

## Build-only evidence

The clean isolated checkout at
`artifacts/native-core-build.KvxVug/progpu` was advanced from `da36a718` to
`0a2acfd4`. Unrelated semantic-render/state edits and deleted performance artifacts
in the working ProGPU checkout were preserved and not incorporated. Fresh fetch
confirmed main remains `102e39e5088b462624da6296ff70a43ed2c5d8b4`, an ancestor of
the feature branch. The MIL coverage digest was regenerated; verifier execution
remains deferred under the user-directed implementation-first phase.

The existing macOS ARM64 command in the
[payload build record](native-mil-macos-payload-refresh-2026-09-09.md) completed
all 79 incremental compile/link steps with exit 0. It uses `--build-only`,
strict C++20, Apple Clang, modules OFF, the existing target-specific cache and
pinned provider dependencies. Both renderer libraries and six SDK archives were
staged; tests/samples compiled but did not execute. Module-consumer execution and
compilation on a module-enabled toolchain remain in final qualification.

The x64 build at `0a2acfd4` also completed all 79 steps. After advancing the clean
checkout to final `a4100cc8`, both architectures completed another 53 incremental
steps each, including the updated native fixtures, and staged both providers and
six SDK archives. Both commands exited 0. The standalone ProGPU.Tests Release
graph compiled at `0a2acfd4` with 0 warnings/errors in 74.10 seconds; the follow-up
changes only native fixture coverage and a complexity comment.

The managed command was:

```sh
/Users/wieslawsoltes/.dotnet/dotnet build src/ProGPU.Tests/ProGPU.Tests.csproj \
  -c Release -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -v:quiet
```

All build processes are terminal. The two macOS staged RIDs now use `a4100cc8`;
Linux and Windows inputs still use `da36a718`. Existing development NuGet feeds
remain at their documented earlier source versions; staging a native payload
does not produce or qualify a refreshed LibreWPF package feed. No tests, apps,
GPU workloads, benchmarks, VM graphics or CI polling ran. Final Windows/Linux,
module-enabled, package-consumer, runtime and CI qualification remains mandatory.

## Next core work

Complete the producer's text/image and remaining stroke/clip families, preserve
coverage independently of cache/effect shortcuts, and connect point/all-owner/
region queries to the presented snapshot without stale asynchronous results or
managed replay. Keep those changes on the same Toolkit/MVP acceptance path.
Then freeze and run final platform, package, image/input/lifetime/performance and
exact-head CI gates. Broader Direct2D/Win2D work stays explicitly deferred.

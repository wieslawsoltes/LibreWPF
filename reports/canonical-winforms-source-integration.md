# Canonical WinForms Source Integration

## Outcome

LibreWPF can compile and package its real `WindowsFormsIntegration` reference
and implementation assemblies against the source-built `System.Windows.Forms`
identity from LibreWinForms and ProGPU's `System.Drawing.Common`. The opt-in
path uses typed assembly references only; it does not introduce reflection,
duck typing, fake WinForms objects, or a second compatibility-shaped runtime.

The normal developer path remains package-based and consumes released ProGPU
`0.1.0-preview.62` packages. The source gate is an additional qualification
lane for replacing the transitional LibreWinForms compatibility packages.

## Qualified Source Pins

- LibreWinForms: `cd0f9dfeb8ce3fa526e650777ecf3af1f432ca91`
- ProGPU: `d73cef34b92dfc71b40288dbc004d6f23c3b6fa8`
- ProGPU release base: `v0.1.0-preview.62` / `00cf8707`

LibreWinForms and LibreWPF must point at the same ProGPU commit. The executable
gate checks that invariant before compiling anything.

## Implemented Fixes

1. `WindowsFormsIntegration.csproj` and its reference project accept an
   explicit canonical assembly root. Their legacy private WinForms references
   and official drawing package remain the default when the property is absent.
2. The canonical path references the source-built Forms, Forms.Primitives,
   ProGPU drawing, private Windows support, and ProGPU interop assemblies by
   exact typed identities and fails early when any file is missing.
3. LibreWinForms' opt-in net10/ProGPU lane aligns the .NET reference pack and
   support packages with LibreWPF instead of mixing .NET 10 and .NET 11
   metadata closures.
4. ProGPU keeps its public `PathGeometry.CombineDeferred` API out of the
   deliberate `PROGPU_VECTOR_INTERNAL` source-embedding mode, preserving the
   existing WPF source seam without copying WebGPU solver implementation into
   PresentationCore.
5. The gate serializes CsWin32 generation and the WPF reference/API-cycle
   roots. This prevents clean caches from producing an empty primitive
   assembly or compiling ReachFramework before its cycle-breaker contracts.
6. `LibreWinForms.WindowsFormsIntegration` now packages the exact qualified
   implementation under `lib/net10.0` and reference assembly under
   `ref/net10.0`, with an exact dependency on the matching canonical
   `LibreWinForms.System.Windows.Forms` package.
7. The source gate stages ProGPU's drawing-runtime packages from the qualified
   commit, packs canonical Forms and WFI from source, verifies ref/lib hashes,
   and rejects a WFI package whose canonical Forms dependency is not exact.
8. Clean hosted validation restores `PresentationBuildTasks` before the
   managed transport build and unshallows the ProGPU checkout before checking
   that the pinned source descends from the released package baseline.

## Verification

`eng/progpu-wpf-canonical-winforms-integration.sh` performs the following:

- validates the aligned LibreWinForms/ProGPU gitlinks;
- optionally runs ProGPU System.Drawing API, correctness, and allocation gates;
- builds canonical LibreWinForms for `net10.0` from ProGPU source;
- builds the WPF primitive, reference, API-cycle, and implementation foundation
  in deterministic order;
- builds `WindowsFormsIntegration-ref` and `WindowsFormsIntegration` with
  `MSB3243` and `MSB3277` promoted to errors; and
- packs the exact ProGPU/Forms/WFI dependency closure and verifies that the
  WFI package contains the byte-identical ref/lib outputs.

Local qualification produced both assemblies with zero errors and no assembly
conflict warnings. The `LibreWPF Build` workflow runs the source integration
gate on Ubuntu in addition to the existing package-mode SDK lane.

## Remaining Cutover Work

The transitional compatibility package is not deleted by this change. Removal
still requires:

1. publishing a ProGPU release that contains the qualified post-preview.62
   source fixes;
2. migrating the LibreWPF SDK package-mode defaults and SharpDevelop integration
   driver to the canonical package closure;
3. deleting `src/LibreWinForms.Portable` only after compatibility, designer,
   resource, and runtime gates pass without it.

Until those gates are complete, Portable stays frozen rather than receiving
new compatibility behavior.

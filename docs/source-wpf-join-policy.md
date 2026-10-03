# Retained source WPF join policy

This draft consumes [ProGPU #359](https://github.com/wieslawsoltes/ProGPU/pull/359)
and its matching future build's `Pen.UseWpfJoinSemantics` contract. It deliberately
leaves `external/ProGPU` at the qualified
`48a49afeb993214c0c40c6908e9896ef0b5dec97` and the runtime package at
`0.1.0-preview.65`. Those older artifacts do not provide the new contract:
this source change requires the coordinated producer build and source rebuild
before build, package or runtime qualification. There is no reflection fallback,
pin change, SDK admission or claim that the old dependency graph builds it.

## Source connection

`WpfResourceResolver` selects the policy at all three native pen construction
sites: cached local solid pens, `IPortablePenSource` snapshots, and raw
`PortablePenState` coverage pens. The last path serves cached-brush line,
rectangle, transformed quadrilateral, ellipse, rounded rectangle and admitted
general-path strokes. It retains the original brush separately; the white
coverage pen does not replace that source identity. Existing geometry, dash,
cap, frame and brush admission stays unchanged.

`WithLineCaps` snapshots the complete retained pen before replacing the two
caps, preserving raw join intent, dash storage, offset and stroke mode. The
producer's PresentationCore shim also selects this policy in `Pen.ToNative`;
the resolver connection remains necessary because the actual source bridge
constructs its native pens directly instead of calling that shim method.

Full WPF policy is distinct from independent clipped-miter policy. It preserves
WPF reversal behavior and interprets retained `IsSmoothJoin` as Round rather
than discarding the join adornment. The producer admits this policy only for
normal-width source joins Miter, Bevel and Round. Generic pens retain their
default behavior; this does not admit WPF device-width or join value 3.
The source geometry converter keeps the original smooth flag, rather than
rewriting the path or changing every generic smooth join globally.

## Reached acceptance path and remaining boundary

In the existing ShowcaseApp, opening the **Layout** tab and resizing its stroked
geometry area reaches the source pen adapters. That page includes the stroked
triangle, rectangle, ellipse and line. Source inspection identified the adapter
policy omission; it did not observe a rendered application failure. The existing
sample does not explicitly author a sharp `IsSmoothJoin=true` corner. Added
sharp-corner controls are authored contract cases, not evidence of a previously
observed Showcase action or successful desktop validation.

Public source `Geometry.GetRenderBounds` and `Geometry.StrokeContains` remain a
separate implementation gap: `WpfPortableGeometryOperations.QueryPen` constructs
`NativeGeometryQueryPen` for `NativeGeometryUtilities` without a WPF join policy.
They do not consume these retained Vector pens. This change does not claim that
public-query path is corrected, and adds no source-local stroker. Producer
retained paint, hit, bounds and transport controls are separate from public
source queries and final original-renderer comparisons.

## Authored controls

`WpfSourcePenJoinPolicyTests` contains 13 source configurations: three joins for
each local cached, typed portable and raw cached-stroke adapter; three actual
recorded paths retaining the smooth bit and policy; and one source join rejection
with an unchanged generic-default control. Local cache cases retain earlier
snapshots across source changes. The corresponding producer shim fixture covers
three joins through each of its four conversion overloads (12 configurations).
These controls are authored only. No builds, tests, source verifiers, probes,
GPU/UI runs, CI or VM execution have been performed for this slice. Original
pixel, hit, package and application qualification remain required.

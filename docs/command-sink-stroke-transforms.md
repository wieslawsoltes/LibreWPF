# Source pen width through composition transforms

The composition command sink adapted WPF pens in source units but did not mark
their commands with ProGPU's existing `IsPenThicknessLocal` contract. The shared
stroke compiler therefore interpreted the width as an already scaled width.
Shrinking a border in a Viewbox could leave strokes wider than the box, as
reported in LibreWPF #308. Nonuniform transforms also changed retained bounds.

Every pen emitted through `AddNativeCommand` now retains the local-width flag.
The shared ProGPU compiler applies the complete actual transform. The sink keeps
its cached pen, original brush, dash storage, caps, miter limit and WPF join
policy. It adds no scaled-pen allocation and does not modify the source pen.
Square/round/triangle cap geometry still uses original local coordinates before
the outer transform. Cached-brush stroke preparation uses its existing separate
ProGPU recording path.

The proposed scaled-copy helper in #308 predates the current local-width
contract. Applying it now would lose source join policy and scale local cap
geometry prematurely. This correction uses the existing renderer contract
instead. Credit to Lex Li for identifying the missing width conversion and the
Viewbox reproduction.

## Applicability and validation

Both ordinary shim drawing and typed primitive ingress share this sink. Managed
ProGPU rendering and retained hit/bounds compilation read the local-width flag
in `Compositor.TryResolveLocalStrokeThickness`. The native picture compiler
requires it for native stroke transport, retaining original width and transform
separately. Direct source-MIL transport has its own source pen/transform records;
it does not consume this sink or need a scaled copy. No shared shader or native
ABI changes are needed.

The actual WPF test project reproduces the missing contract, and 15 new controls
cover nine primitive ingress cases, snapshot reuse and policy, three
nonuniform/reflected retained bounds cases with native picture compilation, and
three cap families. All pass after correction, together with all 13 existing
source pen policy tests. The bounds checks retain the public bounds reader's
conservative endpoint envelope, not a tighter painted bound. Original failed
results and the initial test-envelope correction remain recorded separately.

The new controls are included in the existing retained layout-clip CI gate;
the complete actual-project gate passes all 1,279 cases without failures or skips.
Hosted whole-framework, renderer/package and original application image gates
remain required; source controls do not establish final Viewbox pixel parity.

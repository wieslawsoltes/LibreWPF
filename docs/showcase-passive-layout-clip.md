# Passive Showcase layout-clip gate

This is a separate opt-in native application gate for the retained layout-clip
change in #179. Implementation and source/metric controls are not evidence that
the application has passed idle qualification. No native run is claimed here.

Use a freshly built, loose-file **ShowcaseApp** package consumer with
`ProGpuWpfRendererMode=NativeMilWgpu`. The default managed renderer, a detached
source window, pre-display self-tests, and `LIVE_VALIDATE=0` Windows package
self-tests cannot satisfy this gate. No renderer fallback is allowed. Retain
the producer/package identity separately; the receipt records actual loaded
managed assembly MVIDs and SHA-256 hashes, not an inferred source commit.

Run the bounded launcher against that existing executable (or its DLL with an
explicit `--dotnet` host), in a real supported graphical session:

```sh
python3 eng/progpu-wpf-showcase-idle.py \
  --app /absolute/package-output/ProGPU.Wpf.ShowcaseApp \
  --evidence-parent /absolute/existing-evidence-directory
```

The launcher owns one child, a fresh evidence directory, a 120-second deadline,
the complete child output, and a mandatory JSON receipt. It does not build,
install packages, select a software adapter, start a VM, disable application
timers/carets/input, or reuse an old receipt. Do not interact with the application
during the observation. Other activity causes a failure, not a tolerance.

The application uses its existing Selectors tab, Expander, ScrollViewer and text.
It expands normal text to produce pixel scrolling and temporarily attaches an
ordinary zero-height `Border` with `ClipToBounds=true`. Typed source layout
state must describe both the real viewport clip and the non-Empty 80×0 clip.
The root remains the actual `Window`, owned by its frozen portable presentation
source and genuine native MIL host. Every phase checks native window/source
identity, positive native command/draw/submission counts and unchanged device
recovery. Setup uses ordinary source changes and actual native client resizing.

Four phases are required, in order:

1. Initial overflowing, clipped content at scroll offset zero.
2. Actually scrolled source content, displaced by its real pixel offset.
3. Real native resize with larger source content and surface dimensions.
4. Restored native dimensions and scroll offset zero.

Each phase has one fixed one-second settling delay and one two-second passive
interval. The observer reads the atomic presented-frame count, wall time,
process CPU, total managed allocated bytes and GC collection totals only at the
two endpoints. It performs no dispatcher calls, layout, hit queries, render
requests, native memory polling, status writes or logging between them. A
continuously redrawing baseline still yields bounded metrics and fails; there
is no quiet-until-success loop. All four intervals require **exactly zero new
presentations**. CPU, allocation and GC deltas are reported without invented
performance thresholds; they include process-wide activity and observer cost.

Source/native checks happen outside the intervals. Failure receipts retain
observed metrics. The original text, scroll request, selected tab, Expander,
native size and temporary child are restored in `finally`; an unresponsive child
still fails at the launcher's deadline. Existing live input, clipping/picking,
forced-frame performance, source tests and package gates remain independent and
unchanged. Passing this idle gate would not establish GPU residency, pixel
fidelity, all application interactions or another platform's qualification.

## Validation state

The endpoint helper has strict zero/nonzero/regressed-frame controls and a
two-endpoint execution contract. Source guards preserve the side-effect-free
interval and real Showcase ownership seam. The launcher has offline negative
receipt/child controls. CI adds these controls to the existing retained lane:
all original 619 cases remain mandatory, plus 10 endpoint/source cases (minimum
629), followed by the offline launcher suite. No live application is launched
by that headless lane. These are implementation regressions, not native idle
receipts. Compilation, complete CI and subsequent actual graphical execution
remain required before claiming the #179 application acceptance criterion.

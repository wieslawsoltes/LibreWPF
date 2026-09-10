# Native MIL registered source-resource validation

## Acceptance and correction

The source drawing acceptance path includes cached fills and pens in retained
MIL records. Registered resources must retain their typed source identity before
media adaptation. The decoder already consumes this contract, but the public
`WpfMilResourceRegistry` did not implement `IWpfRawMilResourceResolver`.
It now exposes the same checked lookup used by its existing typed getters.
One-based tokens, registered overrides, null entries and missing-token rejection
are unchanged. No new stroker, renderer fallback or cache implementation is added.

The regression fixtures also now use actual source contracts: cache visual bounds
accept real Rect values and authoritative Empty; drawing content enters through
an immutable typed render-data snapshot; cycle coverage wraps DrawDrawing instead
of supplying a drawing object where render data is required. The empty-point
retained test captures its actual typed retained child, not an untyped container.

All 264 focused cache/pen/native-hit tests pass after the registry correction.
Five explicit registry identity, override and invalid-token cases are added.
Validation uses the clean source worktree with both dependency pins at
ProGPU `9e05651a` / LibreWinForms `c67b04a8`, plus this working patch.
Native libraries are loaded through explicit build/runtime paths.

## Remaining validation

The broad WPF suite is not green. It exposed older source-contract assertions,
opacity-mask Empty conversion, animation no-op accounting and representation
expectations requiring investigation. A monitor-geometry unit test entered GLFW
on the test worker; its captured managed stack identifies the real monitor
service. That run was stopped, not reported as a pass or silently skipped.
Logs are retained in the validation checkout's artifacts directory.

ProGPU's hosted Windows x64 native job now passes at `9e05651a`, including the
masked-image check without tolerance changes. Windows ARM64 VM native CTest passes
20/20 at the same source (553.72 seconds, under concurrent host build load).
LibreWinForms PR #29's seven checks pass at `c67b04a8`. Other ProGPU jobs and
exact-head LibreWPF source/package/application qualification remain prerequisites
for the ordered merges. These results do not qualify the broader Direct2D/Win2D
scope or native package-mode application startup.

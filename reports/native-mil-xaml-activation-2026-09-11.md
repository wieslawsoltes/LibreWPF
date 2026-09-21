# XAML runtime activation qualification

The existing real XAML runtime harness built cleanly, but its injected Alt+A
assertion failed because source activation was false. The harness injects input
without pumping native focus; source Show intent correctly does not imply active
window state.

The access-key fixture now reports inactive/active state through the existing
`WpfPortableWindowActivation.TrySetWindowActivationState` bridge, which delegates
to the typed source activation service. It asserts inactive rejection and no
focus assignment before retaining the existing active handled/focus assertions.
No product admission policy or native focus behavior was changed.

The final prepared-source Release build passed with zero warnings/errors in
10.89 seconds. The complete macOS arm64 harness then reported
`Real WPF XAML runtime smoke succeeded` and exited zero. This does not prove
native focus delivery or exact-head package/platform qualification.

At this checkpoint ProGPU run 34553573493 has passed browser WebGPU, GCC,
Ubuntu native Dawn, retained compositor and upstream text jobs. Remaining jobs
and package artifacts still require completion; these successes are not a claim
that all PR checks pass.

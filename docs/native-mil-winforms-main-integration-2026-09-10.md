# Native MIL integration with canonical WinForms main

The delivery branch merges `progpu-rendering-port` at `d877d7aef`, including
the canonical WinForms and WindowsFormsIntegration work from #120/#121. Both
histories are retained. ProGPU remains on the native MIL integration branch,
which includes the ProGPU source used by that base, rather than reverting the
renderer to the base's older gitlink.

Conflict decisions preserve native input admission and failed-attachment cleanup,
the new dispatcher/native-owner registrations, and all portable desktop/caret
interfaces alongside the source's WinForms identity adapter. Ordinary attachment
publishes its root inside the checked setup; the explicit hidden-source route
remains detached until Show. The source identity is not a native HWND.

SDK defaults advance to ProGPU preview.62 while preserving explicit package
version overrides, final Showcase/SciChart names, .NET support version 10.0.11,
build-only package production and all native renderer qualification gates.
CI keeps the base's release ancestry check and additionally requires exact-head
native artifacts when the pin differs from the release tag; released packages
must not be substituted as evidence for a newer renderer.

The newly inherited canonical WinForms gate requires its nested ProGPU pin to
equal the selected LibreWPF ProGPU commit. The current base's LibreWinForms pin
predates our native MIL changes. That source-graph alignment is an outstanding
cross-repository integration requirement, not a waived check or proof of source
compatibility from ancestry alone. The ordinary SDK/package and platform tests
remain required. This merge resolves conflicts; it does not qualify the release.

Follow-up: [LibreWinForms #29](https://github.com/wieslawsoltes/LibreWinForms/pull/29)
aligns the nested ProGPU gitlink to `ebd12fc0`. LibreWPF now selects that dependency
commit (`26c942dc8`) without changing the physical submodule checkouts. The source
graph mismatch is addressed; dependency CI, canonical integration, package/runtime
qualification and ordered PR merges remain pending.

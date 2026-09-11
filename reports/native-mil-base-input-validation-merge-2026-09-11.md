# Base input-validation merge

GitHub reported LibreWPF PR115 conflicting at 7a4818f98, with no head checks,
after base `progpu-rendering-port` advanced to c4df7dcb0 (PR125). ProGPU PR139
and LibreWinForms PR29 remained conflict-free with pending checks; that does not
prove complete CI success.

Merged c4df7dcb0 while preserving native source changes and exact dependency pins.
Git detected the renamed Showcase script/application. Resolved the script and
project-graph assertions using the existing final Showcase naming. Retained all
new upstream Thumb target alignment/BringIntoView checks, Toolkit transient-surface
setup, SplitButton PART_ActionButton targeting and matching assertions. No legacy
delivery-stage sample names or duplicate launchers were restored.

Validation: `bash -n` on the Showcase launcher and `git diff --check` pass.
The prepared ProGPU.Wpf.Tests Release build passes (zero errors, 116 existing
warnings), and the two affected Toolkit/SDK project-graph tests pass with no skips.
These are source-graph checks, not proof of live input behavior or native source
application/package qualification. Existing qualification gates remain unchanged.

The user's physical dirty submodule worktrees were not staged or reset. Native
anchor placement/wrapping/child ownership remains the core implementation queue
after this merge blocker. Fresh exact-head CI still must complete before merging.

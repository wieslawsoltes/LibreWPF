# Native MIL merge producer alignment

The source graph now selects ProGPU
`3fbbd1fbf23035b9059e89acc0e035c20c2bd097` and canonical LibreWinForms
`daf792a3c34c93eee003a21f6207fcf9d62fa19e`. The canonical WinForms gitlink
selects that same ProGPU revision. Existing unrelated physical submodule
checkouts in the primary WPF workspace were preserved; gitlinks were updated
explicitly rather than staging those checkouts' current heads.

The producer includes the sorted native export manifests, browser diagnostics,
native excluded-paragraph/fragment transport and the verified SVG curve coverage
checksum correction. The latter retains all numeric budgets and quality gates;
its evidence is documented in ProGPU's
`docs/native-mil-svg-checksum-investigation-2026-09-10.md`.

This alignment starts the consumer CI graph while exact-head producer CI is
pending. It does not substitute an older successful artifact or mark the selected
producer qualified. The SDK stage still requires a successful exact-revision
ProGPU build. The preceding WPF SDK run correctly rejected failed producer
`e574a911`; its downstream package/application checks were skipped, not passed.

LibreWinForms documentation and its retired-vector manifest verifier pass with
Bash 5.3.15. The initial attempt used macOS Bash 3.2 and failed at `declare -A`;
installing Homebrew Bash (and its ncurses dependency) supplied the required shell
without changing the verifier or the system login shell.

Merge order remains ProGPU #139, LibreWinForms #29, then LibreWPF #115, after
their required CI and application qualification. Figure/Floater source ownership
and exclusion-dependent formatting remain the concrete ShowcaseApp integration
blocker; native transport alone does not admit those source elements. Full
package startup, required host actions and Windows/Linux/VM qualification remain
open. Broader DirectX/Direct2D/Win2D scope stays deferred behind core delivery,
not reported complete. No SDK admission or native input-index gate is relaxed.

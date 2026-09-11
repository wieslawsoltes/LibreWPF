# Source harness runtime loading

The normal native SDK gate passed its native-host stage, then stopped in the
real XAML harness with `DllNotFoundException` for `progpu_native`. The host
script's child-only loader configuration did not reach the subsequent source
harnesses, which now require native paragraph services in both renderer modes.

The SDK script now scopes native build/runtime loader paths to a subshell
containing the XAML, Application.Run/lifetime and theme source harnesses. Later
package consumers do not inherit those added paths and must load packaged assets.
No renderer fallback, assertion relaxation or qualification bypass was added.

The unchanged XAML harness passes with the exact b54db165 ARM64 native build and
the scoped paths. Shell syntax passes. A source-scope regression was added;
its execution and the complete SDK rerun remain required. Pending macOS CI and
final platform/package qualification still prevent a merge-ready claim.

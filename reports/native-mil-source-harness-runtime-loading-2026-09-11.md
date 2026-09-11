# Source harness runtime loading

The normal native SDK gate passed its native-host stage, then stopped in the
real XAML harness with `DllNotFoundException` for `progpu_native`. The host
script's child-only loader configuration did not reach the subsequent source
harnesses, which now require native paragraph services in both renderer modes.

The SDK script now scopes native build/runtime loader paths to a subshell
containing the XAML, Application.Run/lifetime and theme source harnesses. Later
package consumers do not inherit those added paths and must load packaged assets.
No renderer fallback, assertion relaxation or qualification bypass was added.

CI stages exact native packages without local native build directories. The
same source-only scope therefore includes the host RID under the staged native
package root (`ProGpuNativeRuntimeRoot` when explicitly supplied), after explicit
build/runtime directories. Package-consumer loader isolation is unchanged.

The unchanged XAML harness passes with the exact b54db165 ARM64 native build and
the scoped paths. Shell syntax passes. A source-scope regression was added;
its execution and the complete SDK rerun remain required. Pending macOS CI and
final platform/package qualification still prevent a merge-ready claim.

The actual source-harness subshell was also executed with a recording `run_dotnet`
function: all six invocations received the expected paths, and parent loader state
remained unchanged. Stubbed `uname` checks passed for Darwin, Linux and MINGW,
each at ARM64/x64. These check shell dispatch/isolation, not foreign-platform
binary execution. The integrated 12752bca1 run remains active; the later staged
path change at 47e6743fc is not part of that running checkout.

The integrated 12752bca1 run subsequently passed the mandatory native host and
device-recovery stage, real XAML runtime, Application.Run, last-window/main-window/
explicit lifetime scenarios, and Fluent theme runtime together. It produced the
LibreWPF packages and entered their artifact audit. This closes the source-harness
loading failure in the normal gate, not the later package/runtime checks or
qualification of the newer staged-path revision. The full log is retained at
`artifacts/native-exact-b54db165.dBPf7B/sdk-12752bca1.log`.

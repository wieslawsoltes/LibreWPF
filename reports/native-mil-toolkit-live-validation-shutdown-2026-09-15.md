# Toolkit live-validation dispatcher shutdown

The Toolkit/AvalonDock live-validation harness previously called
`Environment.Exit(0)` or `Environment.Exit(1)` from its worker task while
the portable application dispatcher and native presentation loop were still
running. Two source-overlaid macOS native-MIL probes reached the full live
input success marker but exited 134 immediately afterwards. Their managed
stack started at `BindingExpression.SetupDefaultValueConverter` and
`PropertyPathWorker.ReplaceItem` during template layout. The binding engine
clears its converter table on AppDomain shutdown, so abrupt worker-thread
process exit can race with that layout. The stack is evidence of the race;
it is not evidence that the native picture-mask pipeline caused it.

The sample now writes and flushes the validation result, then requests
`Application.Current.Shutdown(exitCode)` at `DispatcherPriority.Send` on
its own dispatcher. A no-frame failure throws into the shared error path;
there is no worker-thread `Environment.Exit` in live validation. The source
guard checks both success and error exit paths and their ordering.

The exact local Toolkit sample built in `NativeMilWgpu` mode with native
hit testing and zero warnings/errors. Its runtime configuration records
`LibreWPF.RequestedRendererMode=NativeMilWgpu`; the native bundle and
vector-clip traces prove that the selected run actually used the C++ backend.
The sample assembly SHA-256 was
`6b3a33a68dc4e4db1ac4371b172d7e36bedc0d5f6c1c9f22d314b3800dd5461b`.
The isolated source-overlaid native dylib SHA-256 was
`96325f58ba34b98a0c1886a78a3225fc5ae7ebfa40605162ac92a2b4bbbd5b11`.
Two exact-binary patched native runs completed Toolkit/AvalonDock live input,
published their separate status files, and exited zero with no managed null
reference.
The focused source guard passed 1/1. The `ProGPU.Wpf.Tests` project built
with zero errors and 117 existing analyzer warnings.

This is a source-overlay runtime diagnosis using a cached SDK package feed,
not final Windows/macOS/Linux package qualification. Exact Windows VM popup
validation and final SDK/package gates remain required.

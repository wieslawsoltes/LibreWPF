# Exact owned popup input on the WPF source route

This child of the combined source stack requires the new shared ProGPU native
modal gate on the actual fixed-owner Cocoa popup route. The source registrar and
factory still choose real owned panels; input is attached through the existing
`SilkNetWpfInputService`, never a handle/provider guess. Only after attachment does
the host query `NativePopupWindow.SupportsModalInput` for that exact window and
its current typed native input context. Reentrant provider lookups are followed
by the original host/window lifetime checks before publishing the subscription.

The host retains the context identity beside its source subscription. Each native
Show verifies the same service, subscription and currently attached context,
including hide/reopen. Detachment invalidates that proof before cancellation or
context disposal can reenter source code. Missing or replaced contexts cannot
become visible success, even if source `PortableModalInputScope` admission is true.
Source gate policy, pointer/scroll ownership, keyboard owner, view/render leases
and native-session release completion remain independent. No additional native
poll, source scroll conversion or fallback provider is added.

This depends on authored ProGPU #286, initially 95c1a0bca. Both existing qualified
gitlinks remain unchanged pending the final producer boundary. Authored source
controls and shared actual-provider controls have not been executed. No build,
syntax/test check, UI/VM/GPU execution, workflow dispatch or native staging ran.
All intermediate commits use [skip ci]. Automatic native modality remains off
until final ordinary source selection and cross-framework qualification are
completed; enabling and exercising that real path remains mandatory, not a
capability-presence or source-test parity claim. Display/shader admission is
unchanged from the parent integration stack.

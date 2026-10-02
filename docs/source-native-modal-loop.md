# Source-owned native modal loop

The actual portable `Window.ShowDialog` already enters its source input scope,
shows the real host, and restores source input/focus through `ReleaseDialog`.
`RunDialog` now has explicit `EnableNativeModalSessions` admission: a live visible
source-owned ordinary top-level may begin the shared AppKit session before its
event loop. Owned non-key popup surfaces cannot become dialogs. Unsupported
providers reject; ordinary polling is not a fallback after requested admission.

Native Begin may reenter source Close, Hide, dialog release or disposal. The
creating host retains the original window before Begin and queues these actions
through its existing exact-window release coordinator after Begin unwinds. The
coordinator, not returned Dispose or an absent retention query, owns completion.
Source focus restoration remains after every original session lease releases.
The owner alone polls; popup windows still drain their local input queues.

The option remains false during paired Forms integration. Both source factories
and typed pointer routes exist, but ordinary provider-qualified selection and
full cross-framework native dialog/focus/retirement qualification are mandatory
final-tip work, not satisfied by the option's presence. ProGPU and Forms pins are
unchanged. Authored source controls are unexecuted: the current implementation
phase deliberately performs no compilation, test execution or CI dispatch.

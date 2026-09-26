# Portable Window resize capabilities

## Application blocker

Acceptance action: ShowcaseApp's **Show MessageBox**, using the ordinary portable
WPF source dialog. Issue [#112](https://github.com/wieslawsoltes/LibreWPF/issues/112)
reports minimize/maximize buttons on a message box. Source inspection establishes
that `PortableMessageBoxDialog` already requests `ResizeMode.NoResize`; the bridge
lost the distinction between `NoResize` and `CanMinimize` when converting both to
a fixed border. The shared controller consequently retained enabled defaults.
The historical native appearance has not been reproduced in this change.

## Source-to-controller contract

| ResizeMode | Resize border | Minimize intent | Maximize intent |
| --- | --- | --- | --- |
| NoResize | Fixed | false | false |
| CanMinimize | Fixed | true | false |
| CanResize | Resizable | true | true |
| CanResizeWithGrip | Resizable | true | true |

`WindowStyle.None` retains the existing hidden/hidden-resizable border mapping,
independently of these capabilities. Initial options, attachment, and property
callbacks use the same mapping. Missing or unknown mode values preserve explicit
fallback capabilities; the pre-existing border-only host setter preserves them.
Default direct-host options remain true/true for compatibility.

The existing `SilkWindowController.SetCanMinimize`/`SetCanMaximize` methods receive
the requested state before native attachment and on subsequent updates. No
ProGPU API, taskbar state, modal policy, renderer choice, or Cocoa implementation
changes are included. Requested host properties are not native observations.
In particular, Cocoa's existing disabled-button behavior does not prove hidden,
close-only chrome, and a controller setter success is not a pixel result.

## Compilation and qualification

The bridge builds against the unchanged pinned ProGPU source graph on macOS ARM64
with zero errors and one existing unused-event warning. Its complete test project
also compiles (existing analyzer warnings). Authored executable cases cover all
four modes with decorated/custom chrome, initial state versus live updates,
fallback state, border-only compatibility, and disposed-host rejection.

The existing packaged Showcase secondary-window gate exercises an actual source
`AboutWindow`, whose initial `NoResize` matches the MessageBox contract. It changes
all four source modes and custom chrome on the live host, verifies requested
capabilities through typed diagnostics, and restores the original source state.
It does not replace the MessageBox action or assert native button visibility.
Only these additional host-intent assertions require Portable media, which must
already be frozen and have a live host. The same source application's native
Windows MIL backend retains every existing secondary-window check without
requiring a ProGPU host.
The SDK application compiles with zero warnings/errors using the newly built
bridge plus an explicit existing source-assembly staging directory and source
markup tasks. This mixed staged compilation is not fresh package qualification.

No test bodies were executed until all authored code compiled at the end of the
feature-implementation phase. The subsequent focused source run passed all 11
mapping/update/lifetime cases with zero skips on macOS ARM64 (.NET 10.0.5), using
the test project's existing .NET 10 SDK/VSTest configuration. No native windows
or VMs were started. Full unchanged
source/package CI and platform-native
Showcase MessageBox chrome/modal qualification remain required. This change alone
does not resolve taskbar behavior, issue #113, or complete native appearance parity.

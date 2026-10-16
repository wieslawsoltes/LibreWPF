# In-process macOS file panels

The acceptance application for issue257 is an ordinary LibreWPF SDK application
calling `Microsoft.Win32.OpenFileDialog`, `SaveFileDialog`, and `OpenFolderDialog`.
The action is to select or cancel a file/folder, then read or write the selected
file in that same application. The blocking path was
`WpfPortableWindowActivation.ShowPortableFileDialog` →
`ProcessWpfFileDialogService` → `osascript`.

The default macOS platform service now uses `MacOsWpfFileDialogService`. It creates
an owned `NSOpenPanel` or `NSSavePanel` inside the application and uses AppKit's
application-modal `runModal` API, matching the synchronous WPF call. A panel is
created for each request and released after completion. This implementation does
not select an asynchronous owner sheet or transport an Objective-C callback block.
Explicitly supplied platform services and other operating systems are unchanged.

The typed request preserves the title, initial/default directory, suggested name,
extension patterns and multiple-selection setting. AppKit uses `UTType` values
for extension filters; unsupported wildcard shapes fail before showing a panel.
The current portable source bridge continues to combine the WPF filter groups;
this change does not introduce a filter-selector accessory or claim FilterIndex
selection. Folder panels admit directories, and Save never permits multiple items.
Selected paths come from actual file URLs and are copied before native retirement.
They are not parsed from process output, so embedded newlines remain intact.

Native calls require the application's main thread. A canceled token cannot show
a panel or publish an accepted result. Cancellation during the native modal loop
queues `cancel:` on that exact panel using Foundation's main-thread selector API,
including the actual AppKit modal and Foundation default run-loop modes. The
native queue retains the receiver until dispatch; a fresh panel prevents delayed
cancellation from affecting a subsequent request. Cancellation registration ends
before the panel's managed owner releases it.

AppKit owns the operating-system file-selection authorization. This adapter does
not manufacture sandbox permission, persist security-scoped bookmarks, or claim
that arbitrary paths are authorized. Signed/sandboxed application behavior and
the original WPF file-action acceptance remain separate from authored controls.

## Completed application acceptance

[PR #277](https://github.com/wieslawsoltes/LibreWPF/pull/277) merged the implementation
after exact source `ffa1f5dd0c279f78f65c3cdd60d6fe3bbacbdd43` passed all 16
[Build jobs](https://github.com/wieslawsoltes/LibreWPF/actions/runs/38314579531)
and [Docs](https://github.com/wieslawsoltes/LibreWPF/actions/runs/38314579424).
All 36 focused source controls also passed, including the 21 native-service cases.

On 12 October 2026, an ordinary LibreWPF SDK application built from that exact
successful package artifact completed real in-process open/read, multiple-file
selection/read, save/write/readback, folder selection/I/O and cancellation through
computer-use clicks. It used owned fixtures on the external volume without
clipboard access. All five actions passed; the owned process exited normally in
79.818 seconds within its original 180-second bound. An independent control also
canceled the actual native modal call through its token in 5.036 seconds.

[Issue #257](https://github.com/wieslawsoltes/LibreWPF/issues/257) is closed with
this evidence. Original source hashes, the package digest, native action log and
exit receipts remain retained in `file-panels-257-sdk-acceptance`. The action-log
SHA-256 is `a4ed408d140aaafad1cfa0004b4dc79735450d831ec644b8f387985f9392a3db`;
the qualified package-artifact SHA-256 is
`a91434f6062b050799d9705729c3b76fec21934157c140220dcc533a9620b54f`.

This qualifies the original application-modal file actions. Asynchronous owner
sheets, filter-selector accessories and persistent sandbox bookmarks remain
separate follow-up work in [#261](https://github.com/wieslawsoltes/LibreWPF/issues/261).
It does not establish sandbox authorization persistence or LibreWinForms dialog
acceptance.

References: [NSOpenPanel](https://developer.apple.com/documentation/appkit/nsopenpanel),
[NSSavePanel](https://developer.apple.com/documentation/appkit/nssavepanel),
[App Sandbox file access](https://developer.apple.com/documentation/security/accessing-files-from-the-macos-app-sandbox),
and [main-thread selectors](https://developer.apple.com/documentation/objectivec/nsobject-swift.class/performselector(onmainthread:with:waituntildone:modes:)).

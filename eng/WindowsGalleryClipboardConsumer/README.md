# Original Gallery clipboard-image acceptance

Capture readiness now requires an actual native submission-completion checkpoint
for the exact source frame. A returned `SurfacePresent` call alone can precede
GPU completion. See [the diagnosed race and completion contract](../../docs/native-presentation-completion.md).
Every original image/action check remains required. The current consumer explicitly
selects the SDK's packaged DXC compiler and restores the original 60-second
controller and 57-second host reserve; foreground setup must finish before second
55. Package DLL/manifests and the actual selected compiler are checked. See
[compiler configuration and measurements](../../docs/wpf-dx12-compiler.md).
Both actual captures, exact
retained RGB, clipboard actions, owned HWND/PID, real GPU completion, process exit
and cleanup remain mandatory. No diagnostic observer runs in final acceptance.
This completion precondition does not retry a failed capture or admit a uniform
image. The earlier preview69 FXC acceptance used the owner-approved 90/87-second
exception. The new 60/57-second full application acceptance remains pending in
[issue #117](https://github.com/wieslawsoltes/LibreWPF/issues/117); offscreen timing
does not qualify the app. Historical diagnostic sections below retain the limits
under which their original runs failed.

This opt-in Windows acceptance case hosts the original WPFGallery ClipboardPage,
its ViewModel, ControlExample/PageHeader templates and **unchanged pack-resource
PNG** from microsoft/WPF-Samples commit
`811d01e95c8c929e68539d698d0a0609e94fd185`. The twelve-file manifest includes the
upstream MIT license, original byte lengths, Git blob IDs and SHA256 values.
No substitute control, reconstructed image or copied clipboard handler is used.

The reported action in [issue117](https://github.com/wieslawsoltes/LibreWPF/issues/117)
is Copy Image → Paste Image → displayed result on Windows11/preview45. Current
main already includes the Windows OLE bitmap implementation and four real native
package contracts. This fixture covers the original page/resource action; it does
not establish the historical hang's cause or close the issue merely by compiling.

## Prepare or compile without running

The Python tools require Python3.11 or later. `prepare.py` performs no network requests, application launch, image decoding,
desktop operation or clipboard access. Supply locally retained **complete**
`gh run view --json databaseId,name,headSha,status,conclusion,jobs` receipts for
Build and Docs, the original artifact metadata from `gh api .../actions/artifacts/ID`,
and that exact downloaded package ZIP. Do not fetch one successful job's output
from a failed, canceled, incomplete or different-head Build.

The preparer requires at least all thirteen current Build jobs, every additional
reported Build job, and the Docs job to have succeeded, including the SDK bundle
upload. Docs remains a separate documentation check, never a package producer.
It binds the artifact's run/head/name
and SHA256 to the archive, checks source-package commit/version identities, and
uses a fresh private feed, SDK resolver cache, NuGet cache, CLI home and temporary
directory. Every build explicitly disables development-certificate generation,
global-tools PATH addition and CLI telemetry; these exact environment overrides
are retained in the receipt. It retains the original package bytes. Missing receipts, packages,
source files or a missing original job fail explicitly; there is no source
DLL substitution or automatic producer selection.

`--original-source` names an already staged directory with the twelve relative
paths in `source-manifest.json`. The existing reviewed original fixture is
`/Volumes/1TB-macOS/librewpf-gallery-clipboard-probe.k3C6Hsjz/original`.
Alternatively stage those exact paths from the pinned upstream commit (the MIT
LICENSE is from its repository root). Every file is verified before and after
copying; the asset must retain its original pack URI
`pack://application:,,,/Assets/ControlImages/Clipboard.png`.

Example **preparation command**, with explicit reviewed values substituted:

```text
python prepare.py --original-source <original-directory> --build-receipt <build.json> --docs-receipt <docs.json> --artifact-receipt <artifact.json> --package-archive <original.zip> --expected-head <40-hex-head> --expected-build <run-id> --expected-docs <run-id> --architecture arm64 --output <new-consumer-directory>
```

Adding `--build-only --dotnet <explicit-installed-dotnet>` compiles the real
installed-SDK consumer with its normal bootstrap, XAML compiler and original
CommunityToolkit8.2.2 source generator. The compiler SDK is pinned from the
repository's global.json. The build is single-node, has a300-second limit and
never executes its output. The explicit
`AppendRuntimeIdentifierToOutputPath=false` build property matches the verifier's
`bin/Release/net10.0-windows` directory while retaining the requested Windows RID.
It checks the actual native apphost architecture,
requested NativeMilWgpu mode, and exact package bytes for PresentationCore,
PresentationFramework, WindowsBase, WinCore, PresentationNative, bridge, interop
and native engine. `preparation.json` and `build.log` retain failures; even a
successful compile keeps `qualified:false` and `applicationExecuted:false`.

Build staging is separate from the existing shared Showcase gates. The preparer
does not change their gates, timeouts or product code.

## Disposable Windows CI execution

The opt-in `Original Gallery clipboard acceptance` workflow runs this same
consumer on a fresh GitHub-hosted Windows11 ARM64 runner. Supply the exact
qualified LibreWPF head and its whole successful Build and Docs run IDs. All
sixteen current Build jobs are required, including the three SDK bootstrap
hosts, and every additional reported job must succeed. It
checks out that head, fetches the original digest-bound package archive and the
twelve pinned upstream files, and invokes the same preparer and capture runner.
It never selects the latest successful job or substitutes source DLLs.

This workflow uses `--require-empty-ci-clipboard`. Both runner and host require
the repository's [GitHub-hosted Windows environment](https://docs.github.com/en/actions/reference/workflows-and-actions/variables); the host rejects a nonempty
clipboard without reading its contents. Its sequence must remain unchanged
from the empty preflight until the original copy action. Existing sequence
ownership checks still govern all subsequent writes and cleanup. This mode
does not authorize replacing a local or self-hosted machine's clipboard.

The [original run38478934066](https://github.com/wieslawsoltes/LibreWPF/actions/runs/38478934066)
completed preparation and pasted the original image, then correctly rejected its
physical bounds `[79,577,275,775]` on the runner's1024×768 desktop. The retained
failure has no image captures and is not visual qualification. The workflow now
prepares the disposable desktop before launching the unchanged900-DIP window:
it selects the smallest sufficiently large mode actually enumerated by the
display driver, preserving color depth, refresh rate, orientation, origin and DPI.
It uses [ChangeDisplaySettingsExW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-changedisplaysettingsexw)
with a native validity preflight and one dynamic change, without persisting settings.
Actual physical desktop/work-area readback is mandatory. The workflow always
restores the exact recorded original mode after the app exits; runner, session,
device and mode identity must still match. Both receipts are uploaded. Unsupported
modes fail setup; this does not crop/resize the app or image, relax monitor coverage,
change capture coordinates, or extend the original60-second runtime limit.

The private diagnostic host uses console output type with WinExe inference
disabled. It no longer creates a second console over the application. The original
hosted run at `044a1461019c52fb9fd747e096036d7763a3fe3f` failed before either capture:
Windows rejected the Gallery application's own foreground request. A new console
and a request from the application thread do not establish foreground entitlement.
The original failed receipts remain evidence in workflow `38328938035`.

Only the empty **GitHub-hosted CI mode** now performs one native caption click
after the application's first actual frame, before any original clipboard action.
The host publishes its real HWND/PID; the driver checks live ownership, actual
monitor coverage and `WM_NCHITTEST == HTCAPTION` at an unobstructed native point.
It rejects close/minimize/resize/client areas, held mouse buttons/modifiers and
active capture/menu/move state. An absolute virtual-desktop pointer move requires
exact physical cursor readback and a second ownership/hit check before the one
down/up pair. A partially inserted press is released and remains a failure; there
is no second click or retry. This uses documented
[SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput),
[absolute mouse coordinates](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput)
and [native caption hit testing](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-nchittest).

Actual foreground readback is required before publishing the driver acknowledgment
and again on the Gallery application thread. CI never adds a SetForegroundWindow
request after the click. `receipt.json` retains the point, native mapping, input
counts and before/after HWND; `activation.json` retains the app's independent
readback. Local and self-hosted modes cannot select this input path. No input-queue
attachment or desktop-policy change is used. The host remains the single owned
child under the approved 90-second limit.

The approved 90-second deadline, whole-image visibility, foreground/window
ownership, unobstructed desktop capture, exact retained-image comparison and
original page handlers remain unchanged. Uploaded receipts and native captures
still require manual review against the original PNG. A completed probe keeps
`qualified:false`; routed button events do not establish native-pointer input
qualification. An unavailable desktop or changed clipboard fails the run.
Capture admission additionally proves the complete image rectangle is covered
by the actual physical monitors before and after BitBlt. A monitor bounding box
cannot hide an off-screen region or a gap between displays; changing monitor
geometry during the capture fails explicitly.

Before the original clipboard action the application validates its visible HWND
against its own PID. In the separately permissioned local mode it makes at most one ordinary
[SetForegroundWindow request](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
if that window is not already foreground. It records the result and reads back
the actual foreground HWND on the calling application thread. The host clock now
starts at Main entry, including application/resource construction in its approved
87-second reserve. Rejection, changed ownership or failure to become foreground fails the
run. The local mode never synthesizes input; CI uses only the caption sequence above.
Both captures still require the exact foreground HWND before and after capture,
and the second phase cannot request activation again. On a capture failure the
driver gives the host its approved 87-second cleanup reserve before terminating
a hung owned process within the 90-second outer budget. Original failed receipts
remain evidence; these controls do not establish native-pointer qualification.

## Later execution requires explicit coordination and clipboard permission

Do **not** invoke the following during the implementation/compile-only phase.
The actual clipboard may contain user data. This case cannot preserve/restore all
OLE formats or delayed renderers; obtain explicit permission to replace its
contents before using the opt-in. It never logs previous contents or format names.

```text
python run-windows.py --app <verified-GalleryClipboardApp.exe> --output <fresh-evidence-directory> --allow-replace-clipboard
```

The real page is displayed in a source Window. The host raises routed Click on
the page's original Copy Image and Paste Image Buttons, invoking their unchanged
handlers (not OS pointer injection). It validates the decoded72×73 original,
all copied opaque RGB pixels, original status/visibility, a newer actual presented
frame and changed source revision. It brings the actual pasted Image into view.
Then it clears the clipboard only while its sequence number is still owned,
verifies retained/frozen image ownership, captures the displayed image again,
and republishes it through the original paste handler. External clipboard
mutation fails without clearing or overwriting that new state.

The Windows runner uses the approved 90-second total limit and
87-second host cleanup reserve. It checks actual HWND/PID/foreground ownership,
exact native/source client geometry without inferred scale/titlebar offsets,
the entire visible image rectangle, and before/after obstruction inventories.
It captures actual desktop pixels with BitBlt, never source pixels as a substitute.
Nonzero child exits, timeout, clipboard cleanup, image/geometry/ownership failures
and changed retained capture remain failures. Only the owned child is terminated.

**Rendered-result qualification remains explicit:** two nonuniform, equal RGB
captures are not an oracle for correct rendering. The original Image stretches
inside its real198×198 content area. A reviewer must inspect the retained actual
image-region captures against the original image before qualifying that result.
Receipts deliberately remain `qualified:false`; no DPI/alpha/Gallery-wide/native
input parity or issue closure is implied. Conservative obstruction checks can
reject transparent/shadow rectangles, and endpoint checks cannot prove the absence
of every intervening obstruction.

## Authored checks and current status

The current thirty-four offline admission tests pass, including the original
controls, all three required bootstrap jobs, empty-CI clipboard selection,
physical-monitor coverage and rejected/foreign/lost host foreground receipts.
The updated C# host also
compiled against a retained whole-qualified producer with zero errors and six
original-source nullable warnings. These checks do not execute the application
or qualify clipboard or desktop behavior. The opt-in CI workflow still requires
a complete qualified producer and review of its original native captures.

The initial `test_contract.py` contained seventeen authored offline tests: the reused
native-geometry admission controls and new complete-producer rejection controls.
These were not app or clipboard validation and were not executed in the
initial compile-only phase. All three initial Python files were syntax-compiled
at commit14092e1a8 without importing/executing the fixture. The explicit RID-layout
command and fixed first-run environment subsequently completed both real installed
consumer builds; the offline source-contract tests themselves remain unexecuted.
The C# consumer compiled successfully for win-arm64 and win-x64 against the exact
PR217 package bundle, using `/usr/local/share/dotnet/dotnet` and SDK
11.0.100-preview.5.26302.115. ARM64 completed in18.92s, x64 in19.22s, each with
0 errors and6 unsuppressed warnings from three original-source nullable sites
emitted in both markup and final compilation. The original twelve files remained
unchanged; actual XAML compilation and the CommunityToolkit generator were used.
Each fresh private feed contains all23 unchanged producer packages, and all eight
required package/output asset comparisons, apphost PE architecture and native-mode
checks passed. The workload-verification advisory was retained without running
update/repair. These are PR217 compile receipts, **not PR218 qualification**.

The earlier first attempt
used the repository-local SDK11.0.100-preview.5 and its isolated CLI home emitted
the first-run development-certificate banner. The original log is retained; this
does not establish actual keychain installation. No trust/cleanup action was taken.
Both subsequent builds used the explicit first-run overrides above and emitted no
certificate-generation banner. The consumer has not been executed; no new desktop evidence is claimed. Original package CI
coverage remains separate from this exact Gallery acceptance case.

The initial identified qualified input was PR217 head
`45b1cffaa47c3c5e4eb7fe05c938c977886128aa`, whole Build36604077464 and
Docs36604077406. Its package artifact11053355676 is118464911 bytes with SHA256
`24c794bb80c975aad4b58a7c3072b3fb74e3c22d1e4359fc6648a691050d648d`.
The original archive was subsequently downloaded and its full byte length/digest
verified before staging the first compile-only attempt. Evidence is retained in
`/Volumes/1TB-macOS/librewpf-gallery-installed.a83ZsqZ0/`:

- `arm64-attempt1/`: earlier SDK-path/first-run banner, preserved unchanged.
- `arm64-isolated/preparation.json`: SHA256
  `9648e5a7ea01e56a9ede0cc251f938743aad78f8361ffd5f0c1dbed2a5645888`;
  `build.log`: `eb3d4de4f840164b769c2a0932d57f42265ee7cd3c681ac1034c69f777b09cbe`.
- `x64-isolated/preparation.json`: SHA256
  `1f722ab7770ff3c17e670547216f80b2470151f3a0fe3bb5f1d368a5966690ec`;
  `build.log`: `9da778c203da2f6f266e8e8654623e8ea7ebb4e1221e2ad56057c0d4dbf2d03a`.

Both final receipts retain `compiled:true`, `qualified:false`,
`applicationExecuted:false` and `clipboardAccessed:false`, with the exact compiler
command/environment and app/package hashes. No tests, runner, clipboard, captures,
image viewers or VM operations were executed during this compile-only preparation.

Revalidate it when staging. A later producer requires explicit
matching arguments and complete receipts, never an implicit latest-success lookup.

### Hosted Windows startup window

The Windows 11 ARM64 hosted image can start with a fullscreen “Microsoft account”
CoreWindow owned by Windows `WWAHost.exe`. It obstructs Gallery’s real caption.
The workflow now requests normal `WM_CLOSE` on that exact window before launching
Gallery, after verifying the ordinary input desktop, native system-image path and
same interactive session. Changed identity, an unknown foreground or refusal to
close fails setup. No forced termination, sign-in, credentials, clipboard access,
security setting or local/self-hosted input is involved. The original foreground,
physical monitor, full-pixel, ownership and 60-second checks still run afterward.
The successful startup close and native Gallery activation were observed in
[run 38336623666](https://github.com/wieslawsoltes/LibreWPF/actions/runs/38336623666);
that run subsequently timed out without captures and remains a failed acceptance.

### Stale Actions upload metadata

GitHub can report a completed successful producer job while its step timeline
still marks the upload pending. Run38467759739 stopped before application launch
for this inconsistency in producer38462552210; the original failed receipts remain
retained. Whole-run success alone does not qualify an upload.

The preparer now accepts a separate original upload-log receipt only when the
exact completed job/run/head and one non-overwriting `actions/upload-artifact@v4`
action agree on the artifact name, ID, byte size, SHA256 and ordered completion
timestamps inside that job. Failed, skipped, duplicate, unscoped or contradictory
upload evidence is rejected. API statuses are preserved, not rewritten to success.
The actual downloaded archive must still match the original size and SHA256.

The hosted fetcher retains the original individual-job response and raw log along
with the validated receipt. The producer log can include ANSI formatting; current
GitHub CLI requires `--allow-escape-sequences` even when stdout is redirected.
Only this log request enables the flag, capturing bytes into a pipe without
terminal rendering or escape removal. The original 1 MiB bound, API failure
handling and exact upload validation remain unchanged.
Standalone preparation can supply that receipt through
`--upload-evidence`; missing evidence still fails closed when the step is incomplete.
All original Build/Docs, source-byte, clipboard, ownership, image and 60-second
application checks remain mandatory. This metadata repair is not an application pass.

## Observing a failed capture

The optional `diagnostic: true` dispatch input uses the exact whole-successful
producer and unchanged prepared application/driver. It defaults to false.
An observer from the workflow revision records actual, unobstructed full-client
BitBlt pixels immediately before each original image capture, retaining uniform
pixels as diagnostic evidence. It preserves the original capture call, failures,
60-second deadline and clipboard/input policy. The observer may affect timing;
its run is never acceptance or release qualification, including if the original
assertions pass. Its run/job/artifact names and `diagnostic-run.json` explicitly
identify it. Use ordinary acceptance with diagnostics disabled after a diagnosed fix.
The observer also records the owned window's native styles/display-affinity,
compares ordinary SRCCOPY with the documented CAPTUREBLT layered-window option,
and enables the existing native-loop/render-surface trace only in this diagnostic
process. Both captures retain their real pixels and timings. Neither the alternate
capture nor trace metadata can substitute for ordinary acceptance pixels.

After the identical white SRCCOPY/CAPTUREBLT failure, the separate diagnostic
startup hook retains at most two original canonical scene byte arrays (32 MiB
each) and at most 16 nonblocking completion observations on the actual renderer
owner thread. It stops at second 55 within the original 60-second process bound.
Completion polls are spaced at least one second apart, leaving the hook attached
for the original pasted frame instead of exhausting its budget in the first second.
It does not render, alter a scene, inject input, change source assertions or
replace application/package files. Existing startup hooks remain in order.
Reflection is confined to this isolated observer to attach to the already loaded
original application; no product reader or hot path uses it. Its temporary
purpose ends when the blank presentation is diagnosed. Observer failures are
retained separately and never become application exceptions or passing evidence.
See the [.NET startup-hook contract](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-startup-hook.md).

The separate `replay_scene_only: true` job (requiring `diagnostic: true`) diagnoses the demonstrated
pending software-renderer submissions. It admits only the two original byte arrays
and their exact retained diagnostic archive, then uses the same whole-qualified
producer packages in a private copied runtime. A new typed entry point renders
both original scenes offscreen on the same Windows software adapter, polls actual
completion and retains real readback pixels and existing compute traces. It never
launches Gallery or uses desktop input/clipboard. The process has a 60-second outer
bound; neither offscreen pixels nor a successful replay qualifies the application.
Original package/application files and all failed evidence remain unchanged.

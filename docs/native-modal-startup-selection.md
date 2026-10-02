# Explicit application startup for native modal sessions

The actual LibreWPF SDK module initializer now recognizes the deliberate
`--libre-native-modal-sessions` application argument before source/media startup.
ShowcaseApp and SciChartApp already consume this initializer; no application source
edit or replacement host is required. The captured immutable option flows into
every actual source Window host, in both renderer modes, and into the canonical
Forms backend when WPF/Forms integration is selected.

On macOS, a later source `ShowDialog` requests the existing Cocoa session. Actual
Popup/ContextMenu/ComboBox/ToolTip creation retains its owned panel, exact typed
input provider, native point/line scroll route and provider-aware retirement.
This switch does not bypass any of those checks, configure a substitute provider,
change rendering/input-index policy or reinterpret a native Begin failure.

For the final qualified package runs, pass the switch to the built application:

```text
ProGPU.Wpf.ShowcaseApp --libre-native-modal-sessions
ProGPU.Wpf.SciChartApp --libre-native-modal-sessions
```

Select renderer/backend/native-hit-test options independently as required by the
existing acceptance lane. Windows/Linux reject this Cocoa-specific request before
source startup. `PROGPU_WPF_DISABLE_NATIVE_POPUPS=1` also rejects it. No argument
means the original default remains off. Duplicate or unknown native-modal options
fail, and `--` ends option parsing. Original application arguments remain intact.
Only the SDK startup snapshot owns selection: changing application argument data
later cannot change newly opened windows or an active session.

Controls are authored for parsing, unsupported/conflicting choices, original
arguments, snapshot ownership, independent copied Window options and both actual
SDK sample entrypoints. No compilation, tests, verifier, CI, native/UI/VM execution
or runtime staging was performed for this implementation-only child of #237.
Qualified dependency pins remain unchanged. The new Forms overload belongs to the
authored #149/#150 source stack and must be integrated at the final qualified
dependency boundary, not emulated against the older pin.

Required final work is still the complete qualified producer/source/package union
and real application modal/popup/input/focus/retirement runs. Explicit availability
does not enable automatic defaults or establish UI qualification. Legacy-only
MouseWheel handlers do not acquire invented notches from precise scroll input.

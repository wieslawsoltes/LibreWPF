# Confirmation dialogs during Closing

An ordinary source `Window.Closing` handler may open an owned confirmation dialog
before deciding whether to cancel the close. The host previously rejected that
owner as soon as its close-intent flag was set. It now admits a live owner during
the synchronous native closing callback and rejects it after an accepted close.
Cancellation restores ordinary owner admission. Disposal always rejects, including
disposal from inside that callback; a closing child cannot assign or clear owners.

Native ownership is still required. `WpfPortableWindowActivation` preserves native
refusal for both setting and clearing and never substitutes a managed-only owner.
The host rechecks source owner admission after hidden initialization and native
binding because those operations may dispatch callbacks. The shared ProGPU
`SilkWindowController` also needs the corresponding close-intent/native-lifetime
fix; a source host update alone cannot qualify this interaction.

The host controls invoke the actual closing event path and verify cancellation,
accepted close, native refusal, disposal and closing-child rejection. The source
activation control additionally verifies refusal of a nonnull owner. They run in
the existing layout-clip source-contract gate. These are lifecycle controls, not
desktop application or package evidence.

Both source renderer modes share this host and controller. No renderer geometry,
MIL packet or C++ scene implementation changes. Keep exact qualified producer
pins until the complete ProGPU producer and source/package gates pass; the
original confirmation-dialog interaction still requires assembled application
validation. The clipboard part of PR #309 is independent and remains integrated.

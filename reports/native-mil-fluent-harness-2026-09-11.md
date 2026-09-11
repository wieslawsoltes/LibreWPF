# Fluent theme harness qualification

Acceptance: the existing real XAML application's Fluent resources, representative
control styles, layout, retained visual replay and portable activation.

The rebuilt harness first failed because it measured a FlowDocument without
registering the required portable text/document providers. It now invokes the
existing `ProGpuWpfNativeMediaServices.Initialize` in its assembly load context
before loading PresentationFramework and constructing application objects. No
document fallback or theme assertion was changed.

After that correction the layout/replay checks completed, exposing an outdated
reflection call to `TryActivate`. The call now supplies the existing optional
`duringShow` argument explicitly as true, matching the method's default.

Final prepared-source Release build: zero warnings/errors, 10.20 seconds.
Final macOS arm64 execution: `Real WPF Fluent theme runtime smoke succeeded`,
exit zero. Both changes were mirrored into the prepared build checkout. This is
source-harness evidence, not exact-head package, Windows, or Linux qualification.

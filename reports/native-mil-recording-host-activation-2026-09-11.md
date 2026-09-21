# Recording host activation and next application failure

RealApplicationRunHarness previously sent Alt+A without publishing any activation
event. The source access-key policy correctly requires a live visible active
Window through IPortableAccessKeyScopeSource. The real ProGPU host publishes native
focus events through the typed activation registrar; the recording host did not.

The fixture now publishes explicit inactive and active test states through that
same typed ingress. It asserts inactive Alt+A is unhandled and cannot assign focus,
then asserts active Alt+A is handled and focuses the original target. It does not
write IsActive directly, infer activation from Show, relax product admission or
claim that a recording-host event qualifies real native focus behavior.

Prepared Release application-harness build passes with zero warnings/errors in
11.67 seconds. The actual application run passes both access-key checks and moves
on to ValidatePortableInputBindingActivation. Focusing its TextBox terminates the
process via MS.Internal.Invariant in TextBoxLine.GetBoundsFromPosition, called by
TextBoxView's caret rectangle query. Exit code is 134; this is not a passing run.
Next investigate the source GetTextBounds/caret contract and its native retained
line output without weakening the invariant or disabling editor focus.

The fixture change is shared by both renderers. Application, packages, platform
input/pixels and final-head CI remain unqualified; deferred broader compatibility
scope is unchanged.

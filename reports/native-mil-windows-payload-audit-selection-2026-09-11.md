# Shared Windows payload selection

After the integrated source application stages passed, the SDK gate stopped at
the Windows PresentationCore package hash check. Packing consumed the explicit
`LibreWpfWindowsManagedPayloadDir`, while the auditor only read
`LIBREWPF_WINDOWS_MANAGED_PAYLOAD_DIR` and consequently selected an older default
artifact directory. The audit correctly rejected these different bytes.

The SDK entry point now normalizes the two settings once before any build or
audit, preserving the existing default and rejecting conflicting explicit values.
No expected hash, payload contents or audit assertion is changed. A source contract
test accompanies the fix. Complete qualification on the final revision remains
required; the prior run stopped at the artifact audit, not at application startup.

Re-running the unchanged audit with the explicitly selected Windows artifact
directory passes for all packages at 0.1.0-preview.45. The new entry point also
rejects two conflicting directory settings with exit code 2 before starting
production. Shell syntax passes; final integrated execution is still required.

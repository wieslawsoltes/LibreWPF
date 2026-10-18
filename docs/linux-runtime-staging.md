# Staging the source Xlib dependency

The optional fourth argument of `eng/progpu-stage-ci-native-runtimes.sh` stages
the Linux dependency carried by `ProGPU.Backend`. It uses the same exact commit,
successful whole Build, artifact and package version as the native/SDK and DX12
payloads. Existing two- and three-argument callers keep their behavior.

```sh
./eng/progpu-stage-ci-native-runtimes.sh "$qualified_progpu_commit" \
  "$progpu_root/artifacts/progpu-native/package" \
  "$progpu_root/artifacts/progpu-dx12/package" \
  "$progpu_root/artifacts/progpu-xlib/package"
```

The release extractor also accepts paired `--xlib-package` and
`--xlib-destination` arguments. It verifies the package ID, version, repository
and exact source commit before extracting any selected runtime. Xlib staging
requires both Linux x64/arm64 inventories, matching producer and dependency
receipts, the versioned extension, ABI-test completion, every original file hash,
and the corresponding ELF architecture. Extensionless license files retain their
original path and bytes. Malformed or incomplete candidates are discarded before
the Xlib destination is published. Existing destinations are never overwritten.

This supplies the future source-first package build with the real producer
payload; it does not patch a consumer DLL or a NuGet cache. The helper does not
advance a gitlink or qualify an unfinished ProGPU Build. Call sites and dependency
pins must be updated together after the producer is fully qualified.

Validation covers existing native/SDK/DX12 staging, exact whole-Build selection,
optional Xlib argument routing, changed provenance, partial inventories and
changed bytes. An actual local package control preserves all 14 corrected
Linux payload files and rejects the original malformed license path without
publishing a destination. That local package is a staging control, not a
whole-producer or application qualification result. The mixed-session source
application remains tracked in [#187](https://github.com/wieslawsoltes/LibreWPF/issues/187).

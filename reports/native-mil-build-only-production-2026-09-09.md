# Native build-only production — 2026-09-09

## Acceptance dependency and implementation

The package-mode LibreWPF MVP needs complete native platform payloads before its
startup can be qualified. The ordinary ProGPU scripts mix native compilation with
protocol/export checks, CTest, renderer samples and managed differential runs.
SkipExtendedIntegration still executes qualification and omits some package files;
it cannot implement the requested build-first/validate-at-end workflow.

ProGPU `979f72ca727b5bdd537b80d78c76df02f04783ae` adds explicit CLI-only
--build-only and -BuildOnly modes. They reject reduced compiler-qualification
profiles, keep native test/sample compilation, build both wgpu and Dawn providers,
require every SDK payload before staging, and return before verification or
application execution. Windows also requires its Direct2D DLL and Dawn import
library and reuses normal symbol staging. Unix stages two libraries and six SDK
archives for the actual host RID. Existing no-switch CI/release paths keep their
gates. Pinned Dawn checkout preparation is now shared with its existing verifier;
no foreign implementation, new dependency version or renderer algorithm was added.

## Clean macOS ARM64 production

The existing isolated checkout was clean and advanced from 95504c8d to 979f72ca:

`artifacts/native-core-build.KvxVug/progpu`

The build reused its existing CMake cache and pinned dependency checkouts. The
first attempt selected the WPF-local .NET 11 SDK and stopped before restore because
ProGPU requires .NET 10.0.201. The installed `/Users/wieslawsoltes/.dotnet/dotnet`
provides that exact SDK; changing PATH to it resolved the issue without altering
global.json, feed configuration or SDK admission.

The successful command, from the isolated ProGPU checkout:

```sh
env PATH="/Users/wieslawsoltes/.dotnet:$PATH" \
  PROGPU_NATIVE_WGPU_SOURCE=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/wgpu-native \
  PROGPU_NATIVE_BUILD_DIR=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/build-osx-arm64 \
  PROGPU_NATIVE_INCLUDE_DIR=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/include \
  PROGPU_NATIVE_RUNTIME_DIR=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/runtime \
  PROGPU_NATIVE_DAWN_HEADER_SOURCE=/Users/wieslawsoltes/GitHub/wpf/artifacts/native-core-build.KvxVug/dawn-headers \
  PROGPU_NATIVE_CXX_COMPILER=/usr/bin/clang++ PROGPU_NATIVE_BUILD_JOBS=4 \
  ./eng/build-progpu-native.sh --build-only
```

Restore, pinned dependency preparation, CMake configuration and the complete
default native target build succeeded. Ninja performed 41 incremental compile/link
steps; unchanged targets were reused, not presented as a second clean rebuild.
The checkout remained clean. Output is under its own
`artifacts/progpu-native/package/runtimes/osx-arm64/native`:

- libprogpu_native.dylib: 3,376,776 bytes.
- libprogpu_native_dawn.dylib: 3,407,400 bytes.
- Six nonempty compression/hit-testing/image/MIL/text/scene-builder SDK archives.

This is **unqualified** native payload production for one RID, not a full LibreWPF
package. Prior macOS Intel output remains separate. Windows/Linux native outputs
and source-built Windows managed/IJW payloads are still missing.

## Compilation and VM blocker

Two source-contract fixtures protect explicit mode selection, full artifact
requirements, early exits, shared pinned preparation and unchanged release gates.
The ProGPU.Tests graph compiles with 0 warnings/0 errors using the workspace SDK;
fixtures were authored, not executed. The Windows build-only mode has not run.
No tests, verifier scripts, renderer samples, VM/GPU application workloads,
benchmarks, CI checks or package/application qualification executed.

Parallels 27.0.1 reported Windows 11 UUID
9d6a85ae-607e-4508-bb6d-f79a2a0f0059 suspended with installed/outdated Tools. After
inventory, local help and snapshot-list inspection, one graceful resume was
attempted. The same live operation was followed until it exited 255 with a critical
error; fresh status again reported suspended. No reset, forced stop, saved-state
discard, Tools upgrade or VM-bundle edits were performed. The storage/VM issue
still needs resolution before Windows payload production. No guest build ran.

A read-only alternative Linux-build-host check found the Docker CLI but no live
daemon: the sole default context points at a missing /var/run/docker.sock. No
container, image download, Docker context change or service startup was performed.

Latest fetched ProGPU main is included. Unrelated native semantic-state edits and
performance artifact deletions remain untouched. The goal stays active: core
application/native-popup/platform work and all final qualification gates remain.

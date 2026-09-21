# ProGPU Release Warning Cleanup

Date: 2026-07-05

## Scope

The ProGPU test-project build used by the LibreWPF package/release lane had accumulated warning noise in shared WinUI, designer, chart, DXF, sample, and headless-test code. This cleanup keeps the release gate warning-clean without changing public package identities.

## Changes

- Fixed nullable cached-buffer flow in WinUI chart renderers.
- Marked intentional designer property/event hiding with `new`.
- Moved designer callbacks after their target controls are initialized.
- Allowed designer font initialization to accept nullable fonts, matching `PopupService.DefaultFont` and sample app state.
- Forwarded `DesignerCanvas.DragOver` through the designer-specific event rather than leaving the event unused.
- Removed dead DXF image/parser locals.
- Hardened the markdown virtualization headless test so an inaccessible local `/Users/.../Downloads/spec.txt` fixture falls back to generated dense markdown and scrolls only valid viewport origins.

## Validation

- `DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build external/ProGPU/src/ProGPU.Tests/ProGPU.Tests.csproj --no-restore --verbosity minimal -m:1`
- `DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build external/ProGPU/src/ProGPU.Tests/ProGPU.Tests.csproj --configuration Release --no-restore --verbosity minimal -m:1`
- `DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet vstest external/ProGPU/src/ProGPU.Tests.Headless/bin/Debug/net10.0/ProGPU.Tests.Headless.dll`
- `PROGPU_PACKAGE_VERSION=11.0.0-dev DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 ./eng/progpu-pack.sh`
- `git -C external/ProGPU diff --check`
- `git diff --check`

The Release build completed with `0 Warning(s), 0 Error(s)`. The package script emitted the expected ProGPU runtime packages plus `LibreWPF.Interop.11.0.0-dev.nupkg` into `external/ProGPU/artifacts/packages/Release`.

# ProGPU DirectX SciChart Span Uploads

Date: 2026-07-05

## Decision

SciChart 2D batch helpers in `ProGPU.DirectX` should keep generated vertex and instance data in their existing `List<float>` builders, but the final upload into `ProGpuDirectXBuffer` must use `CollectionsMarshal.AsSpan(...)` instead of materializing temporary arrays. The buffer API already accepts `ReadOnlySpan<T>`, so the DirectX shim can avoid `List<float>.ToArray()` while preserving the existing draw batching, validation, command recording, and buffer lifetime behavior.

## Implementation

- `CreateColoredSpriteInstanceBuffer(...)` now writes `instanceData` through `CollectionsMarshal.AsSpan(instanceData)`.
- `CreateTexturedColorVertexBuffer(...)` now writes `vertexData` through `CollectionsMarshal.AsSpan(vertexData)`.
- `CreateSolidColorVertexBuffer(...)` now writes `vertexData` through `CollectionsMarshal.AsSpan(vertexData)`.
- ProGPU and WPF graph tests now reject the old `instanceData.ToArray()` and `vertexData.ToArray()` upload shape.

## Validation

- `DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build external/ProGPU/src/ProGPU.Tests/ProGPU.Tests.csproj --no-restore --verbosity minimal -m:1`
- `DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet vstest external/ProGPU/src/ProGPU.Tests/bin/Debug/net10.0/ProGPU.Tests.dll --Tests:SciChartRenderContextUploadsListBackedBatchesWithoutArrayMaterialization,SciChartRenderContextRecordsBatchedTextureVerticesAndClip,SciChartRenderContextRecordsColoredSpritesAndClip,FlushSubmitsGpuBackedSciChartBatchedTextureVertexCommands,FlushSubmitsGpuBackedSciChartColoredSpriteCommands`
- `DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet build src/ProGPU.Wpf.Tests/ProGPU.Wpf.Tests.csproj --no-restore --verbosity minimal -m:1`
- `DOTNET_ROLL_FORWARD=Major DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 dotnet vstest src/ProGPU.Wpf.Tests/bin/Debug/net10.0/ProGPU.Wpf.Tests.dll --Tests:ProGpuWpfSdkProvidesSwitchOnlyPackagingSurface`

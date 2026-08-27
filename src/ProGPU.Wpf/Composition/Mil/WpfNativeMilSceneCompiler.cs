using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using WpfPortableGeometryBoundsReader = System.Windows.Media.ProGPU.Composition.WpfPortableGeometryBoundsReader;
using WpfReplayRect = System.Windows.Media.ProGPU.Composition.WpfReplayRect;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed record WpfNativeMilBitmapSource(
    uint Handle,
    uint Width,
    uint Height,
    uint RowBytes,
    byte[] Rgba8Pixels);

public sealed record WpfNativeMilGlyphRunFont(
    uint Handle,
    uint FaceIndex,
    NativeMilGlyphStyleSimulations StyleSimulations,
    ReadOnlyMemory<byte> FontData);

public sealed record WpfNativeMilDrawingImageBounds(
    uint Handle,
    NativeMilRect Bounds);

public sealed record WpfNativeMilDrawingGroupBounds(
    uint Handle,
    NativeMilRect Bounds);

/// <summary>
/// Carries exact Visual descendant bounds through ProGPU's ABI-compatible
/// cache-named sideband for BitmapCache and bounded effect planning.
/// </summary>
public sealed record WpfNativeMilVisualCacheBounds(
    uint Handle,
    NativeMilRect Bounds);

public sealed record WpfNativeMilViewport3DScene(
    uint Handle,
    NativeMilViewport3DScene Scene);

public sealed record WpfNativeMilBatch(
    byte[] Bytes,
    uint TargetHandle,
    IReadOnlyList<WpfNativeMilBitmapSource>? BitmapSources = null,
    IReadOnlyList<WpfNativeMilGlyphRunFont>? GlyphRunFonts = null,
    IReadOnlyList<WpfNativeMilDrawingImageBounds>? DrawingImageBounds = null,
    IReadOnlyList<WpfNativeMilVisualCacheBounds>? VisualCacheBounds = null,
    IReadOnlyList<WpfNativeMilDrawingGroupBounds>? DrawingGroupBounds = null,
    IReadOnlyList<WpfNativeMilViewport3DScene>? Viewport3DScenes = null);

public sealed record WpfNativeMilCompilation(
    NativeMilCompiledScene Scene,
    NativeMilBatchMetrics BatchMetrics);

/// <summary>
/// Compiles the typed portable state published by source-built LibreWPF
/// visuals into canonical MIL and then into ProGPU's native semantic scene.
/// </summary>
/// <remarks>
/// This fail-closed slice supports retained offsets, affine transforms and
/// opacity plus nested transform/opacity scopes and portable solid, linear,
/// and radial-gradient analytic render-data. The existing managed portable
/// renderer remains independent and is not replaced or selected implicitly.
/// </remarks>
public sealed class WpfNativeMilSceneCompiler
{
    public WpfNativeMilBatch BuildBatch(
        object rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        NativeMilColor clearColor = default)
    {
        ArgumentNullException.ThrowIfNull(rootVisual);
        var context = new BuildContext();
        uint rootHandle = context.AddVisual(rootVisual);
        uint targetHandle = context.NextHandle();
        context.Batch.CreateResource(
            targetHandle, NativeMilResourceType.GenericRenderTarget);
        context.Batch.CreateGenericTarget(targetHandle, pixelWidth, pixelHeight);
        context.Batch.SetTargetClearColor(targetHandle, clearColor);
        context.Batch.SetTargetRoot(targetHandle, rootHandle);
        return new WpfNativeMilBatch(
            context.Batch.ToArray(),
            targetHandle,
            context.BitmapSources.ToArray(),
            context.GlyphRunFonts.ToArray(),
            context.DrawingImageBounds.ToArray(),
            context.VisualCacheBounds.ToArray(),
            context.DrawingGroupBounds.ToArray(),
            context.Viewport3DScenes.ToArray());
    }

    public WpfNativeMilCompilation Compile(
        object rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        ulong sceneId,
        ulong generation,
        NativeMilBackend backend = NativeMilBackend.WgpuNative,
        NativeMilColor clearColor = default)
    {
        WpfNativeMilBatch batch = BuildBatch(
            rootVisual, pixelWidth, pixelHeight, clearColor);
        using var channel = new NativeMilChannel(backend);
        NativeMilBatchMetrics batchMetrics = channel.Apply(batch.Bytes);
        foreach (WpfNativeMilBitmapSource bitmap in
                 batch.BitmapSources ?? Array.Empty<WpfNativeMilBitmapSource>())
        {
            channel.SetBitmapSourceRgba8(
                bitmap.Handle,
                bitmap.Width,
                bitmap.Height,
                bitmap.RowBytes,
                bitmap.Rgba8Pixels);
        }
        foreach (WpfNativeMilGlyphRunFont glyphRunFont in
                 batch.GlyphRunFonts ??
                 Array.Empty<WpfNativeMilGlyphRunFont>())
        {
            channel.SetGlyphRunFontSfnt(
                glyphRunFont.Handle,
                glyphRunFont.FontData.Span,
                glyphRunFont.FaceIndex,
                glyphRunFont.StyleSimulations);
        }
        foreach (WpfNativeMilDrawingImageBounds drawingImage in
                 batch.DrawingImageBounds ??
                 Array.Empty<WpfNativeMilDrawingImageBounds>())
        {
            channel.SetDrawingImageBounds(
                drawingImage.Handle, drawingImage.Bounds);
        }
        foreach (WpfNativeMilDrawingGroupBounds drawingGroup in
                 batch.DrawingGroupBounds ??
                 Array.Empty<WpfNativeMilDrawingGroupBounds>())
        {
            channel.SetDrawingGroupBounds(
                drawingGroup.Handle, drawingGroup.Bounds);
        }
        foreach (WpfNativeMilVisualCacheBounds visualCache in
                 batch.VisualCacheBounds ??
                 Array.Empty<WpfNativeMilVisualCacheBounds>())
        {
            channel.SetVisualCacheBounds(
                visualCache.Handle, visualCache.Bounds);
        }
        foreach (WpfNativeMilViewport3DScene viewport3D in
                 batch.Viewport3DScenes ??
                 Array.Empty<WpfNativeMilViewport3DScene>())
        {
            channel.SetViewport3DScene(
                viewport3D.Handle, viewport3D.Scene);
        }
        NativeMilCompiledScene scene = channel.CompileScene(
            batch.TargetHandle, sceneId, generation);
        return new WpfNativeMilCompilation(scene, batchMetrics);
    }

    private sealed class BuildContext
    {
        private readonly Dictionary<object, uint> _visualHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _brushHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _transformHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _penHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _geometryHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _drawingHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _glyphRunHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _imageSourceHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _guidelineSetHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _doubleAnimationHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _pointAnimationHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _rectAnimationHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _effectHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, uint> _bitmapCacheHandles =
            new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> _activeDrawings =
            new(ReferenceEqualityComparer.Instance);
        private uint _nextHandle = 1;

        internal NativeMilBatchBuilder Batch { get; } = new();

        internal List<WpfNativeMilBitmapSource> BitmapSources { get; } = [];

        internal List<WpfNativeMilGlyphRunFont> GlyphRunFonts { get; } = [];

        internal List<WpfNativeMilDrawingImageBounds> DrawingImageBounds
            { get; } = [];

        internal List<WpfNativeMilDrawingGroupBounds> DrawingGroupBounds
            { get; } = [];

        internal List<WpfNativeMilVisualCacheBounds> VisualCacheBounds
            { get; } = [];

        internal List<WpfNativeMilViewport3DScene> Viewport3DScenes
            { get; } = [];

        internal uint NextHandle()
        {
            if (_nextHandle == 0)
            {
                throw new InvalidOperationException(
                    "The native MIL channel handle namespace was exhausted.");
            }
            return _nextHandle++;
        }

        internal uint AddVisual(object visual)
        {
            if (_visualHandles.ContainsKey(visual))
            {
                throw new InvalidOperationException(
                    "The portable visual graph contains a cycle or a visual with multiple parents.");
            }
            if (visual is not IPortableVisualStateSource stateSource ||
                !stateSource.TryGetPortableVisualState(out PortableVisualState state))
            {
                throw MissingContract(nameof(IPortableVisualStateSource));
            }
            RejectUnsupportedState(state);

            bool isViewport3D = visual is IPortableViewport3DSceneSource;
            if (isViewport3D)
            {
                RejectUnsupportedViewport3DState(state);
            }

            uint visualHandle = NextHandle();
            _visualHandles.Add(visual, visualHandle);
            Batch.CreateResource(
                visualHandle,
                isViewport3D
                    ? NativeMilResourceType.Viewport3DVisual
                    : NativeMilResourceType.Visual);
            Batch.CreateVisual(visualHandle);
            if (isViewport3D)
            {
                var viewportSource = (IPortableViewport3DSceneSource)visual;
                if (!viewportSource.TryGetPortableViewport3DScene(
                        out PortableViewport3DScene portableScene))
                {
                    throw MissingContract(
                        nameof(IPortableViewport3DSceneSource));
                }
                Viewport3DScenes.Add(
                    new WpfNativeMilViewport3DScene(
                        visualHandle,
                        CreateNativeViewport3DScene(portableScene)));
            }
            if (state.HasTransform)
            {
                if (state.Transform is null)
                {
                    throw MissingContract(nameof(IPortableTransformMatrixSource));
                }
                Batch.SetVisualTransform(
                    visualHandle, ResolveTransform(state.Transform));
            }
            if (state.HasEffect)
            {
                if (state.Effect is null)
                {
                    throw MissingContract(nameof(IPortableEffectSource));
                }
                Batch.SetVisualEffect(
                    visualHandle, ResolveEffect(state.Effect));
            }
            if (state.HasCacheMode)
            {
                if (state.CacheMode is null)
                {
                    throw MissingContract(nameof(IPortableBitmapCacheSource));
                }
                Batch.SetVisualCacheMode(
                    visualHandle, ResolveBitmapCache(state.CacheMode));
            }
            bool requiresVisualIsolationBounds =
                state.HasCacheMode ||
                state.HasEffect ||
                (state.HasOpacity && state.Opacity != 1.0) ||
                state.HasOpacityMask;
            if (requiresVisualIsolationBounds)
            {
                if (!TryGetVisualBounds(
                        visual, out NativeMilRect visualBounds))
                {
                    throw new NotSupportedException(
                        "Native MIL BitmapCache/effect/opacity/opacity-mask isolation requires exact typed Visual descendant bounds.");
                }
                VisualCacheBounds.Add(
                    new WpfNativeMilVisualCacheBounds(
                        visualHandle,
                        visualBounds));
            }
            if (state.HasClip)
            {
                if (state.Clip is null)
                {
                    throw MissingContract(
                        nameof(IPortablePrimitiveGeometrySource));
                }
                Batch.SetVisualClip(
                    visualHandle,
                    state.HasEffect
                        ? ResolveExactEffectRectangleClip(state.Clip)
                        : ResolveGeometry(state.Clip));
            }
            if (state.HasScrollableAreaClip)
            {
                Batch.SetVisualScrollableAreaClip(
                    visualHandle,
                    new NativeMilRect(
                        state.ScrollableAreaClip.X,
                        state.ScrollableAreaClip.Y,
                        state.ScrollableAreaClip.Width,
                        state.ScrollableAreaClip.Height));
            }
            if (state.HasOpacityMask)
            {
                if (state.OpacityMask is null)
                {
                    throw MissingContract(nameof(IPortableBrushSource));
                }
                Batch.SetVisualOpacityMask(
                    visualHandle,
                    ResolveVisualOpacityMask(
                        state.OpacityMask,
                        allowSpatialMask: true,
                        out _));
            }
            if (state.HasSnappingGuidelinesX ||
                state.HasSnappingGuidelinesY)
            {
                if (state.SnappingGuidelinesX is null ||
                    state.SnappingGuidelinesY is null)
                {
                    throw new InvalidOperationException(
                        "Portable Visual guideline state is incomplete.");
                }
                Batch.SetVisualGuidelines(
                    visualHandle,
                    state.SnappingGuidelinesX,
                    state.SnappingGuidelinesY);
            }
            if (state.HasOffset)
            {
                Batch.SetVisualOffset(
                    visualHandle, state.Offset.X, state.Offset.Y);
            }
            if (state.HasOpacity)
            {
                Batch.SetVisualOpacity(visualHandle, state.Opacity);
            }
            NativeMilRenderOptionFlags renderOptionFlags =
                NativeMilRenderOptionFlags.None;
            NativeMilBitmapScalingMode bitmapScalingMode =
                NativeMilBitmapScalingMode.Unspecified;
            if (state.HasBitmapScalingMode &&
                !state.HasPortableBitmapScalingMode)
            {
                throw MissingContract(nameof(PortableBitmapScalingMode));
            }
            if (state.HasPortableBitmapScalingMode)
            {
                renderOptionFlags |=
                    NativeMilRenderOptionFlags.BitmapScalingMode;
                bitmapScalingMode = state.PortableBitmapScalingMode switch
                {
                    PortableBitmapScalingMode.Unspecified =>
                        NativeMilBitmapScalingMode.Unspecified,
                    PortableBitmapScalingMode.Linear =>
                        NativeMilBitmapScalingMode.Linear,
                    PortableBitmapScalingMode.Fant =>
                        NativeMilBitmapScalingMode.Fant,
                    PortableBitmapScalingMode.NearestNeighbor =>
                        NativeMilBitmapScalingMode.NearestNeighbor,
                    _ => throw new NotSupportedException(
                        $"Bitmap scaling mode {(int)state.PortableBitmapScalingMode} is unsupported.")
                };
            }
            NativeMilEdgeMode edgeMode = NativeMilEdgeMode.Unspecified;
            if (state.HasEdgeMode && !state.HasPortableEdgeMode)
            {
                throw MissingContract(nameof(PortableEdgeMode));
            }
            if (state.HasPortableEdgeMode)
            {
                renderOptionFlags |= NativeMilRenderOptionFlags.EdgeMode;
                edgeMode = state.PortableEdgeMode switch
                {
                    PortableEdgeMode.Unspecified =>
                        NativeMilEdgeMode.Unspecified,
                    PortableEdgeMode.Aliased => NativeMilEdgeMode.Aliased,
                    _ => throw new NotSupportedException(
                        $"Edge mode {(int)state.PortableEdgeMode} is unsupported.")
                };
            }
            NativeMilClearTypeHint clearTypeHint =
                NativeMilClearTypeHint.Auto;
            if (state.HasClearTypeHint &&
                !state.HasPortableClearTypeHint)
            {
                throw MissingContract(nameof(PortableClearTypeHint));
            }
            if (state.HasPortableClearTypeHint)
            {
                renderOptionFlags |=
                    NativeMilRenderOptionFlags.ClearTypeHint;
                clearTypeHint = state.PortableClearTypeHint switch
                {
                    PortableClearTypeHint.Auto =>
                        NativeMilClearTypeHint.Auto,
                    PortableClearTypeHint.Enabled =>
                        NativeMilClearTypeHint.Enabled,
                    _ => throw new NotSupportedException(
                        $"ClearType hint {(int)state.PortableClearTypeHint} is unsupported.")
                };
            }
            NativeMilTextRenderingMode textRenderingMode =
                NativeMilTextRenderingMode.Auto;
            if (state.HasTextRenderingMode &&
                !state.HasPortableTextRenderingMode)
            {
                throw MissingContract(nameof(PortableTextRenderingMode));
            }
            if (state.HasPortableTextRenderingMode)
            {
                renderOptionFlags |=
                    NativeMilRenderOptionFlags.TextRenderingMode;
                textRenderingMode = state.PortableTextRenderingMode switch
                {
                    PortableTextRenderingMode.Auto =>
                        NativeMilTextRenderingMode.Auto,
                    PortableTextRenderingMode.Aliased =>
                        NativeMilTextRenderingMode.Aliased,
                    PortableTextRenderingMode.Grayscale =>
                        NativeMilTextRenderingMode.Grayscale,
                    PortableTextRenderingMode.ClearType =>
                        NativeMilTextRenderingMode.ClearType,
                    _ => throw new NotSupportedException(
                        $"Text rendering mode {(int)state.PortableTextRenderingMode} is unsupported.")
                };
            }
            NativeMilTextHintingMode textHintingMode =
                NativeMilTextHintingMode.Auto;
            if (state.HasTextHintingMode &&
                !state.HasPortableTextHintingMode)
            {
                throw MissingContract(nameof(PortableTextHintingMode));
            }
            if (state.HasPortableTextHintingMode)
            {
                renderOptionFlags |=
                    NativeMilRenderOptionFlags.TextHintingMode;
                textHintingMode = state.PortableTextHintingMode switch
                {
                    PortableTextHintingMode.Auto =>
                        NativeMilTextHintingMode.Auto,
                    PortableTextHintingMode.Fixed =>
                        NativeMilTextHintingMode.Fixed,
                    PortableTextHintingMode.Animated =>
                        NativeMilTextHintingMode.Animated,
                    _ => throw new NotSupportedException(
                        $"Text hinting mode {(int)state.PortableTextHintingMode} is unsupported.")
                };
            }
            if (renderOptionFlags != NativeMilRenderOptionFlags.None)
            {
                Batch.SetVisualRenderOptions(
                    visualHandle,
                    new NativeMilRenderOptions(
                        renderOptionFlags,
                        edgeMode,
                        bitmapScalingMode,
                        clearTypeHint,
                        textRenderingMode,
                        textHintingMode));
            }

            if (!isViewport3D &&
                visual is IPortableDrawingContentSource contentSource &&
                contentSource.TryGetPortableDrawingContent(out object? content) &&
                content is not null)
            {
                AddContent(visualHandle, content);
            }

            if (visual is not IPortableVisualChildrenSource childrenSource ||
                !childrenSource.TryGetPortableVisualChildCount(out int count) ||
                count < 0)
            {
                throw MissingContract(nameof(IPortableVisualChildrenSource));
            }
            for (int index = 0; index < count; index++)
            {
                if (!childrenSource.TryGetPortableVisualChild(
                        index, out object? child) || child is null)
                {
                    throw new InvalidOperationException(
                        $"Portable visual child {index} is unavailable.");
                }
                uint childHandle = AddVisual(child);
                Batch.InsertVisualChild(
                    visualHandle, childHandle, checked((uint)index));
            }
            return visualHandle;
        }

        private void AddContent(uint visualHandle, object content)
        {
            if (content is not IPortableRenderDataSource renderDataSource ||
                !renderDataSource.TryGetPortableRenderDataSnapshot(
                    out PortableRenderDataSnapshot snapshot))
            {
                throw MissingContract(nameof(IPortableRenderDataSource));
            }
            var nested = new NativeMilRenderDataBuilder(
                Math.Max(snapshot.RenderData.Length, 1));
            TranslateRenderData(snapshot, nested);
            uint contentHandle = NextHandle();
            Batch.CreateResource(contentHandle, NativeMilResourceType.RenderData);
            Batch.SetRenderData(contentHandle, nested);
            Batch.SetVisualContent(visualHandle, contentHandle);
        }

        private void TranslateRenderData(
            PortableRenderDataSnapshot snapshot,
            NativeMilRenderDataBuilder destination)
        {
            ReadOnlySpan<byte> source = snapshot.RenderData;
            int offset = 0;
            int scopeDepth = 0;
            while (offset < source.Length)
            {
                if (source.Length - offset < 8)
                {
                    throw new InvalidOperationException(
                        "The portable WPF render-data header is truncated.");
                }
                int recordSize = BinaryPrimitives.ReadInt32LittleEndian(
                    source[offset..]);
                int command = BinaryPrimitives.ReadInt32LittleEndian(
                    source[(offset + 4)..]);
                if (recordSize < 8 || (recordSize & 7) != 0 ||
                    recordSize > source.Length - offset)
                {
                    throw new InvalidOperationException(
                        $"The portable WPF render-data record at {offset} has an invalid size.");
                }
                ReadOnlySpan<byte> payload = source.Slice(
                    offset + 8, recordSize - 8);
                switch ((WpfMilCommandId)command)
                {
                    case WpfMilCommandId.DrawLine:
                        if (recordSize != 48)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF line record has an invalid size.");
                        }
                        uint linePenToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]);
                        uint linePadding =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[36..]);
                        if (linePadding != 0)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF line record has nonzero padding.");
                        }
                        uint linePenHandle = linePenToken == 0
                            ? 0
                            : ResolvePen(
                                snapshot.DependentResources,
                                linePenToken);
                        destination.DrawLine(
                            ReadDouble(payload, 0),
                            ReadDouble(payload, 8),
                            ReadDouble(payload, 16),
                            ReadDouble(payload, 24),
                            linePenHandle);
                        break;
                    case WpfMilCommandId.DrawLineAnimate:
                        if (recordSize != 56)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated-line record has an invalid size.");
                        }
                        uint animatedLinePenToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]);
                        uint point0AnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[36..]);
                        uint point1AnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[40..]);
                        uint animatedLinePadding =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[44..]);
                        if (animatedLinePadding != 0)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated-line record has nonzero padding.");
                        }
                        destination.DrawLine(
                            ReadDouble(payload, 0),
                            ReadDouble(payload, 8),
                            ReadDouble(payload, 16),
                            ReadDouble(payload, 24),
                            animatedLinePenToken == 0
                                ? 0
                                : ResolvePen(
                                    snapshot.DependentResources,
                                    animatedLinePenToken),
                            point0AnimationToken == 0
                                ? 0
                                : ResolvePointAnimation(
                                    snapshot.DependentResources,
                                    point0AnimationToken),
                            point1AnimationToken == 0
                                ? 0
                                : ResolvePointAnimation(
                                    snapshot.DependentResources,
                                    point1AnimationToken));
                        break;
                    case WpfMilCommandId.DrawRectangle:
                        if (recordSize != 48)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF rectangle record has an invalid size.");
                        }
                        uint brushToken = BinaryPrimitives.ReadUInt32LittleEndian(
                            payload[32..]);
                        uint penToken = BinaryPrimitives.ReadUInt32LittleEndian(
                            payload[36..]);
                        uint brushHandle = brushToken == 0
                            ? 0
                            : ResolveBrush(
                                snapshot.DependentResources,
                                brushToken);
                        uint penHandle = penToken == 0
                            ? 0
                            : ResolvePen(
                                snapshot.DependentResources,
                                penToken);
                        destination.DrawRectangle(
                            ReadDouble(payload, 0),
                            ReadDouble(payload, 8),
                            ReadDouble(payload, 16),
                            ReadDouble(payload, 24),
                            brushHandle,
                            penHandle);
                        break;
                    case WpfMilCommandId.DrawRectangleAnimate:
                        if (recordSize != 56)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated-rectangle record has an invalid size.");
                        }
                        uint animatedRectangleBrushToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]);
                        uint animatedRectanglePenToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[36..]);
                        uint rectangleAnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[40..]);
                        uint animatedRectanglePadding =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[44..]);
                        if (animatedRectanglePadding != 0)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated-rectangle record has nonzero padding.");
                        }
                        destination.DrawRectangle(
                            ReadDouble(payload, 0),
                            ReadDouble(payload, 8),
                            ReadDouble(payload, 16),
                            ReadDouble(payload, 24),
                            animatedRectangleBrushToken == 0
                                ? 0
                                : ResolveBrush(
                                    snapshot.DependentResources,
                                    animatedRectangleBrushToken),
                            animatedRectanglePenToken == 0
                                ? 0
                                : ResolvePen(
                                    snapshot.DependentResources,
                                    animatedRectanglePenToken),
                            rectangleAnimationToken == 0
                                ? 0
                                : ResolveRectAnimation(
                                    snapshot.DependentResources,
                                    rectangleAnimationToken));
                        break;
                    case WpfMilCommandId.DrawEllipse:
                        if (recordSize != 48)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF ellipse record has an invalid size.");
                        }
                        uint ellipseBrushToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]);
                        uint ellipsePenToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[36..]);
                        uint ellipseBrushHandle = ellipseBrushToken == 0
                            ? 0
                            : ResolveBrush(
                                snapshot.DependentResources,
                                ellipseBrushToken);
                        uint ellipsePenHandle = ellipsePenToken == 0
                            ? 0
                            : ResolvePen(
                                snapshot.DependentResources,
                                ellipsePenToken);
                        destination.DrawEllipse(
                            ReadDouble(payload, 0),
                            ReadDouble(payload, 8),
                            ReadDouble(payload, 16),
                            ReadDouble(payload, 24),
                            ellipseBrushHandle,
                            ellipsePenHandle);
                        break;
                    case WpfMilCommandId.DrawEllipseAnimate:
                        if (recordSize != 64)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated-ellipse record has an invalid size.");
                        }
                        uint animatedEllipseBrushToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]);
                        uint animatedEllipsePenToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[36..]);
                        uint centerAnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[40..]);
                        uint ellipseRadiusXAnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[44..]);
                        uint ellipseRadiusYAnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[48..]);
                        uint animatedEllipsePadding =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[52..]);
                        if (animatedEllipsePadding != 0)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated-ellipse record has nonzero padding.");
                        }
                        destination.DrawEllipse(
                            ReadDouble(payload, 0),
                            ReadDouble(payload, 8),
                            ReadDouble(payload, 16),
                            ReadDouble(payload, 24),
                            animatedEllipseBrushToken == 0
                                ? 0
                                : ResolveBrush(
                                    snapshot.DependentResources,
                                    animatedEllipseBrushToken),
                            animatedEllipsePenToken == 0
                                ? 0
                                : ResolvePen(
                                    snapshot.DependentResources,
                                    animatedEllipsePenToken),
                            centerAnimationToken == 0
                                ? 0
                                : ResolvePointAnimation(
                                    snapshot.DependentResources,
                                    centerAnimationToken),
                            ellipseRadiusXAnimationToken == 0
                                ? 0
                                : ResolveDoubleAnimation(
                                    snapshot.DependentResources,
                                    ellipseRadiusXAnimationToken),
                            ellipseRadiusYAnimationToken == 0
                                ? 0
                                : ResolveDoubleAnimation(
                                    snapshot.DependentResources,
                                    ellipseRadiusYAnimationToken));
                        break;
                    case WpfMilCommandId.DrawRoundedRectangle:
                        if (recordSize != 64)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF rounded-rectangle record has an invalid size.");
                        }
                        double radiusX = ReadDouble(payload, 32);
                        double radiusY = ReadDouble(payload, 40);
                        if (radiusX != radiusY &&
                            (radiusX == 0 || radiusY == 0) &&
                            (ReadDouble(payload, 16) == 0 ||
                             ReadDouble(payload, 24) == 0))
                        {
                            throw new NotSupportedException(
                                "Native MIL degenerate zero-axis asymmetric rounded-rectangle radii are not implemented yet.");
                        }
                        uint roundedBrushToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[48..]);
                        uint roundedPenToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[52..]);
                        uint roundedBrushHandle = roundedBrushToken == 0
                            ? 0
                            : ResolveBrush(
                                snapshot.DependentResources,
                                roundedBrushToken);
                        uint roundedPenHandle = roundedPenToken == 0
                            ? 0
                            : ResolvePen(
                                snapshot.DependentResources,
                                roundedPenToken);
                        destination.DrawRoundedRectangle(
                            ReadDouble(payload, 0),
                            ReadDouble(payload, 8),
                            ReadDouble(payload, 16),
                            ReadDouble(payload, 24),
                            radiusX,
                            radiusY,
                            roundedBrushHandle,
                            roundedPenHandle);
                        break;
                    case WpfMilCommandId.DrawRoundedRectangleAnimate:
                        if (recordSize != 80)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated rounded-rectangle record has an invalid size.");
                        }
                        uint animatedRoundedBrushToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[48..]);
                        uint animatedRoundedPenToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[52..]);
                        uint roundedRectangleAnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[56..]);
                        uint roundedRadiusXAnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[60..]);
                        uint roundedRadiusYAnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[64..]);
                        uint animatedRoundedPadding =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[68..]);
                        if (animatedRoundedPadding != 0)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated rounded-rectangle record has nonzero padding.");
                        }
                        destination.DrawRoundedRectangle(
                            ReadDouble(payload, 0),
                            ReadDouble(payload, 8),
                            ReadDouble(payload, 16),
                            ReadDouble(payload, 24),
                            ReadDouble(payload, 32),
                            ReadDouble(payload, 40),
                            animatedRoundedBrushToken == 0
                                ? 0
                                : ResolveBrush(
                                    snapshot.DependentResources,
                                    animatedRoundedBrushToken),
                            animatedRoundedPenToken == 0
                                ? 0
                                : ResolvePen(
                                    snapshot.DependentResources,
                                    animatedRoundedPenToken),
                            roundedRectangleAnimationToken == 0
                                ? 0
                                : ResolveRectAnimation(
                                    snapshot.DependentResources,
                                    roundedRectangleAnimationToken),
                            roundedRadiusXAnimationToken == 0
                                ? 0
                                : ResolveDoubleAnimation(
                                    snapshot.DependentResources,
                                    roundedRadiusXAnimationToken),
                            roundedRadiusYAnimationToken == 0
                                ? 0
                                : ResolveDoubleAnimation(
                                    snapshot.DependentResources,
                                    roundedRadiusYAnimationToken));
                        break;
                    case WpfMilCommandId.DrawGeometry:
                        if (recordSize != 24)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF geometry record has an invalid size.");
                        }
                        uint geometryBrushToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload);
                        uint geometryPenToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
                        uint geometryToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]);
                        uint geometryPadding =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]);
                        if (geometryPadding != 0)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF geometry record has nonzero padding.");
                        }
                        uint geometryBrushHandle = geometryBrushToken == 0
                            ? 0
                            : ResolveBrush(
                                snapshot.DependentResources,
                                geometryBrushToken);
                        uint geometryPenHandle = geometryPenToken == 0
                            ? 0
                            : ResolvePen(
                                snapshot.DependentResources,
                                geometryPenToken);
                        uint geometryHandle = ResolveGeometry(
                            snapshot.DependentResources,
                            geometryToken);
                        destination.DrawGeometry(
                            geometryBrushHandle,
                            geometryPenHandle,
                            geometryHandle);
                        break;
                    case WpfMilCommandId.DrawGlyphRun:
                        if (recordSize != 16)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF glyph-run record has an invalid size.");
                        }
                        uint glyphBrushToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload);
                        uint glyphRunToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
                        uint glyphBrushHandle = glyphBrushToken == 0
                            ? 0
                            : ResolveBrush(
                                snapshot.DependentResources,
                                glyphBrushToken);
                        uint glyphRunHandle = glyphRunToken == 0
                            ? 0
                            : ResolveGlyphRun(
                                snapshot.DependentResources,
                                glyphRunToken);
                        destination.DrawGlyphRun(
                            glyphBrushHandle, glyphRunHandle);
                        break;
                    case WpfMilCommandId.DrawDrawing:
                        if (recordSize != 16)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF drawing record has an invalid size.");
                        }
                        uint drawingToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload);
                        uint drawingPadding =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
                        if (drawingPadding != 0)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF drawing record has nonzero padding.");
                        }
                        destination.DrawDrawing(ResolveDrawing(
                            snapshot.DependentResources,
                            drawingToken));
                        break;
                    case WpfMilCommandId.DrawImage:
                        if (recordSize != 48)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF image record has an invalid size.");
                        }
                        uint imageToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]);
                        uint imagePadding =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[36..]);
                        if (imagePadding != 0)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF image record has nonzero padding.");
                        }
                        if (imageToken != 0)
                        {
                            destination.DrawImage(
                                new NativeMilRect(
                                    ReadDouble(payload, 0),
                                    ReadDouble(payload, 8),
                                    ReadDouble(payload, 16),
                                    ReadDouble(payload, 24)),
                                ResolveImageSource(
                                    snapshot.DependentResources,
                                    imageToken));
                        }
                        break;
                    case WpfMilCommandId.DrawImageAnimate:
                        if (recordSize != 48)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated-image record has an invalid size.");
                        }
                        uint animatedImageToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[32..]);
                        uint imageRectangleAnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[36..]);
                        if (animatedImageToken != 0)
                        {
                            destination.DrawImage(
                                new NativeMilRect(
                                    ReadDouble(payload, 0),
                                    ReadDouble(payload, 8),
                                    ReadDouble(payload, 16),
                                    ReadDouble(payload, 24)),
                                ResolveImageSource(
                                    snapshot.DependentResources,
                                    animatedImageToken),
                                imageRectangleAnimationToken == 0
                                    ? 0
                                    : ResolveRectAnimation(
                                        snapshot.DependentResources,
                                        imageRectangleAnimationToken));
                        }
                        break;
                    case WpfMilCommandId.PushClip:
                        if (recordSize != 16)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF clip-scope record has an invalid size.");
                        }
                        uint clipToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload);
                        if (clipToken == 0)
                        {
                            destination.PushTransform(0);
                        }
                        else
                        {
                            destination.PushClip(ResolveGeometry(
                                snapshot.DependentResources,
                                clipToken));
                        }
                        scopeDepth++;
                        break;
                    case WpfMilCommandId.PushOpacityMask:
                        if (recordSize != 32)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF opacity-mask scope record has an invalid size.");
                        }
                        uint opacityMaskToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[16..]);
                        if (opacityMaskToken == 0)
                        {
                            destination.PushTransform(0);
                        }
                        else
                        {
                            destination.PushOpacityMask(
                                new NativeMilRect(
                                    ReadSingle(payload, 0),
                                    ReadSingle(payload, 4),
                                    ReadSingle(payload, 8),
                                    ReadSingle(payload, 12)),
                                ResolveBrush(
                                    snapshot.DependentResources,
                                    opacityMaskToken));
                        }
                        scopeDepth++;
                        break;
                    case WpfMilCommandId.PushOpacity:
                        if (recordSize != 16)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF opacity-scope record has an invalid size.");
                        }
                        destination.PushOpacity(ReadDouble(payload, 0));
                        scopeDepth++;
                        break;
                    case WpfMilCommandId.PushOpacityAnimate:
                        if (recordSize != 24)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated-opacity scope record has an invalid size.");
                        }
                        uint opacityAnimationToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[8..]);
                        uint opacityAnimationPadding =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload[12..]);
                        if (opacityAnimationPadding != 0)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF animated-opacity scope record has nonzero padding.");
                        }
                        double opacityBaseValue = ReadDouble(payload, 0);
                        if (opacityAnimationToken == 0)
                        {
                            destination.PushOpacity(opacityBaseValue);
                        }
                        else
                        {
                            destination.PushOpacity(
                                opacityBaseValue,
                                ResolveDoubleAnimation(
                                    snapshot.DependentResources,
                                    opacityAnimationToken));
                        }
                        scopeDepth++;
                        break;
                    case WpfMilCommandId.PushGuidelineSet:
                        if (recordSize != 16)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF guideline-scope record has an invalid size.");
                        }
                        uint guidelineToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload);
                        destination.PushGuidelineSet(
                            guidelineToken == 0
                                ? 0
                                : ResolveGuidelineSet(
                                    snapshot.DependentResources,
                                    guidelineToken));
                        scopeDepth++;
                        break;
                    case WpfMilCommandId.PushEffect:
                        if (recordSize != 16)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF legacy-effect scope record has an invalid size.");
                        }
                        // WPF's native render-data executor disables legacy
                        // BitmapEffect execution and treats both managed-only
                        // handles as an opacity-1 scope. Preserve only its Pop
                        // participation through the canonical identity scope.
                        destination.PushTransform(0);
                        scopeDepth++;
                        break;
                    case WpfMilCommandId.PushTransform:
                        if (recordSize != 16)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF transform-scope record has an invalid size.");
                        }
                        uint transformToken =
                            BinaryPrimitives.ReadUInt32LittleEndian(payload);
                        uint transformHandle = transformToken == 0
                            ? 0
                            : ResolveTransform(
                                snapshot.DependentResources,
                                transformToken);
                        destination.PushTransform(transformHandle);
                        scopeDepth++;
                        break;
                    case WpfMilCommandId.Pop:
                        if (recordSize != 8)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF pop record has an invalid size.");
                        }
                        if (scopeDepth == 0)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF render-data stack is unbalanced.");
                        }
                        destination.Pop();
                        scopeDepth--;
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Native MIL render-data command 0x{command:x} is not implemented by this fail-closed slice.");
                }
                offset += recordSize;
            }
            if (scopeDepth != 0)
            {
                throw new InvalidOperationException(
                    "The portable WPF render-data stack is unbalanced.");
            }
        }

        private uint ResolveBrush(
            IReadOnlyList<object?> resources,
            uint token)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable brush token {token} is unavailable.");
            }
            return ResolveBrush(resource);
        }

        private uint ResolveDoubleAnimation(
            IReadOnlyList<object?> resources,
            uint token)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable double-animation token {token} is unavailable.");
            }
            if (_doubleAnimationHandles.TryGetValue(
                    resource,
                    out uint existing))
            {
                return existing;
            }
            if (resource is not IPortableDoubleAnimationValueSource source ||
                !source.TryGetPortableDoubleAnimationValue(out double value))
            {
                throw MissingContract(
                    nameof(IPortableDoubleAnimationValueSource));
            }
            uint handle = NextHandle();
            _doubleAnimationHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.DoubleResource);
            Batch.SetDoubleResource(handle, value);
            return handle;
        }

        private uint ResolvePointAnimation(
            IReadOnlyList<object?> resources,
            uint token)
        {
            object resource = ResolveAnimationResource(
                resources, token, "point");
            if (_pointAnimationHandles.TryGetValue(
                    resource,
                    out uint existing))
            {
                return existing;
            }
            if (resource is not IPortablePointAnimationValueSource source ||
                !source.TryGetPortablePointAnimationValue(
                    out PortablePoint value))
            {
                throw MissingContract(
                    nameof(IPortablePointAnimationValueSource));
            }
            uint handle = NextHandle();
            _pointAnimationHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.PointResource);
            Batch.SetPointResource(
                handle,
                new NativeMilPoint(value.X, value.Y));
            return handle;
        }

        private uint ResolveRectAnimation(
            IReadOnlyList<object?> resources,
            uint token)
        {
            object resource = ResolveAnimationResource(
                resources, token, "rectangle");
            if (_rectAnimationHandles.TryGetValue(
                    resource,
                    out uint existing))
            {
                return existing;
            }
            if (resource is not IPortableRectAnimationValueSource source ||
                !source.TryGetPortableRectAnimationValue(out PortableRect value))
            {
                throw MissingContract(
                    nameof(IPortableRectAnimationValueSource));
            }
            uint handle = NextHandle();
            _rectAnimationHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.RectResource);
            Batch.SetRectResource(
                handle,
                new NativeMilRect(
                    value.X,
                    value.Y,
                    value.Width,
                    value.Height));
            return handle;
        }

        private static object ResolveAnimationResource(
            IReadOnlyList<object?> resources,
            uint token,
            string valueKind)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable {valueKind}-animation token {token} is unavailable.");
            }
            return resource;
        }

        private uint ResolveBrush(object resource)
        {
            if (_brushHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            if (resource is not IPortableBrushSource source ||
                !source.TryGetPortableBrush(out PortableBrush brush))
            {
                throw MissingContract(nameof(IPortableBrushSource));
            }
            uint handle = AddPortableBrush(brush);
            _brushHandles.Add(resource, handle);
            return handle;
        }

        private uint AddPortableBrush(PortableBrush brush)
        {
            switch (brush.Kind)
            {
                case PortableBrushKind.SolidColor:
                    if (brush.HasTransform || brush.HasRelativeTransform)
                    {
                        throw new NotSupportedException(
                            "Portable solid-brush transforms are not implemented by the native MIL slice.");
                    }
                    uint solidHandle = NextHandle();
                    Batch.CreateResource(
                        solidHandle,
                        NativeMilResourceType.SolidColorBrush);
                    Batch.SetSolidColorBrush(
                        solidHandle,
                        ToLinearColor(brush.Color),
                        brush.Opacity);
                    return solidHandle;
                case PortableBrushKind.LinearGradient:
                {
                    uint transformHandle = brush.HasTransform
                        ? AddGeometryTransform(brush.Transform)
                        : 0;
                    uint relativeTransformHandle = brush.HasRelativeTransform
                        ? AddGeometryTransform(brush.RelativeTransform)
                        : 0;
                    uint handle = NextHandle();
                    Batch.CreateResource(
                        handle,
                        NativeMilResourceType.LinearGradientBrush);
                    Batch.SetLinearGradientBrush(
                        handle,
                        new NativeMilLinearGradientBrush(
                            new NativeMilPoint(
                                brush.StartPoint.X,
                                brush.StartPoint.Y),
                            new NativeMilPoint(
                                brush.EndPoint.X,
                                brush.EndPoint.Y),
                            brush.Opacity,
                            ToNativeGradientInterpolation(
                                brush.ColorInterpolationMode),
                            ToNativeBrushMappingMode(brush.MappingMode),
                            ToNativeGradientSpreadMethod(brush.SpreadMethod),
                            TransformHandle: transformHandle,
                            RelativeTransformHandle: relativeTransformHandle),
                        ToNativeGradientStops(brush.GradientStops));
                    return handle;
                }
                case PortableBrushKind.RadialGradient:
                {
                    uint transformHandle = brush.HasTransform
                        ? AddGeometryTransform(brush.Transform)
                        : 0;
                    uint relativeTransformHandle = brush.HasRelativeTransform
                        ? AddGeometryTransform(brush.RelativeTransform)
                        : 0;
                    uint handle = NextHandle();
                    Batch.CreateResource(
                        handle,
                        NativeMilResourceType.RadialGradientBrush);
                    Batch.SetRadialGradientBrush(
                        handle,
                        new NativeMilRadialGradientBrush(
                            new NativeMilPoint(
                                brush.Center.X,
                                brush.Center.Y),
                            new NativeMilPoint(
                                brush.GradientOrigin.X,
                                brush.GradientOrigin.Y),
                            brush.RadiusX,
                            brush.RadiusY,
                            brush.Opacity,
                            ToNativeGradientInterpolation(
                                brush.ColorInterpolationMode),
                            ToNativeBrushMappingMode(brush.MappingMode),
                            ToNativeGradientSpreadMethod(brush.SpreadMethod),
                            TransformHandle: transformHandle,
                            RelativeTransformHandle: relativeTransformHandle),
                        ToNativeGradientStops(brush.GradientStops));
                    return handle;
                }
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(brush),
                        brush.Kind,
                        "Unsupported portable brush kind.");
            }
        }

        private uint ResolveTransform(
            IReadOnlyList<object?> resources,
            uint token)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable transform token {token} is unavailable.");
            }
            return ResolveTransform(resource);
        }

        private uint ResolveTransform(object resource)
        {
            if (_transformHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            if (resource is not IPortableTransformMatrixSource source ||
                !source.TryGetPortableTransformMatrix(
                    out PortableMatrix3x2 matrix))
            {
                throw MissingContract(nameof(IPortableTransformMatrixSource));
            }
            uint handle = NextHandle();
            _transformHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.MatrixTransform);
            Batch.SetMatrixTransform(
                handle,
                new NativeMilMatrix3x2(
                    matrix.M11,
                    matrix.M12,
                    matrix.M21,
                    matrix.M22,
                    matrix.OffsetX,
                    matrix.OffsetY));
            return handle;
        }

        private uint ResolvePen(
            IReadOnlyList<object?> resources,
            uint token)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable pen token {token} is unavailable.");
            }
            return ResolvePen(resource);
        }

        private uint ResolvePen(object resource)
        {
            if (_penHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            if (resource is not IPortablePenSource source ||
                !source.TryGetPortablePen(out PortablePen pen))
            {
                throw MissingContract(nameof(IPortablePenSource));
            }
            uint brushHandle = AddPortableBrush(pen.Brush);
            uint dashStyleHandle = 0;
            if (pen.DashArray.Length != 0)
            {
                dashStyleHandle = NextHandle();
                Batch.CreateResource(
                    dashStyleHandle,
                    NativeMilResourceType.DashStyle);
                Batch.SetDashStyle(
                    dashStyleHandle,
                    pen.DashOffset,
                    pen.DashArray);
            }
            uint penHandle = NextHandle();
            _penHandles.Add(resource, penHandle);
            Batch.CreateResource(penHandle, NativeMilResourceType.Pen);
            Batch.SetPen(
                penHandle,
                new NativeMilPen(
                    brushHandle,
                    pen.Thickness,
                    ToNativeLineCap(pen.StartLineCap),
                    ToNativeLineCap(pen.EndLineCap),
                    ToNativeLineCap(pen.DashCap),
                    ToNativeLineJoin(pen.LineJoin),
                    pen.MiterLimit,
                    dashStyleHandle));
            return penHandle;
        }

        private uint ResolveGeometry(
            IReadOnlyList<object?> resources,
            uint token)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable geometry token {token} is unavailable.");
            }
            return ResolveGeometry(resource);
        }

        private uint ResolveGeometry(object resource)
        {
            if (_geometryHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            if (resource is IPortablePrimitiveGeometrySource primitiveSource &&
                primitiveSource.TryGetPortablePrimitiveGeometry(
                    out PortablePrimitiveGeometry primitive))
            {
                return AddPrimitiveGeometry(resource, primitive);
            }
            if (resource is not IPortableGeometryPathSource source ||
                !source.TryGetPortableGeometryPath(
                    out PortableGeometryPath path))
            {
                throw MissingContract(
                    nameof(IPortablePrimitiveGeometrySource));
            }
            if (path.Kind != PortableGeometryPathKind.Path)
            {
                throw new NotSupportedException(
                    "Combined portable geometry is not implemented by the native MIL slice.");
            }
            if (!IsSingleStrokedLine(path))
            {
                return AddPathGeometry(resource, path);
            }
            uint transformHandle = AddGeometryTransform(path.Transform);
            uint handle = NextHandle();
            _geometryHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.LineGeometry);
            PortablePoint start = path.Figures[0].StartPoint;
            PortablePoint end = path.Figures[0].Segments[0].Point1;
            Batch.SetLineGeometry(
                handle,
                start.X,
                start.Y,
                end.X,
                end.Y,
                transformHandle);
            return handle;
        }

        private uint ResolveExactEffectRectangleClip(object resource)
        {
            if (resource is not IPortablePrimitiveGeometrySource source ||
                !source.TryGetPortablePrimitiveGeometry(
                    out PortablePrimitiveGeometry geometry))
            {
                throw MissingContract(
                    nameof(IPortablePrimitiveGeometrySource));
            }
            PortableMatrix3x2 transform = geometry.Transform;
            bool preservesAxisAlignment =
                (transform.M12 == 0.0 && transform.M21 == 0.0) ||
                (transform.M11 == 0.0 && transform.M22 == 0.0);
            if (geometry.Kind != PortablePrimitiveGeometryKind.Rectangle ||
                geometry.RadiusX != 0.0 || geometry.RadiusY != 0.0 ||
                !preservesAxisAlignment)
            {
                throw new NotSupportedException(
                    "Native WPF visual effects require an exact axis-aligned, non-rounded portable rectangle clip.");
            }
            if (_geometryHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            return AddPrimitiveGeometry(resource, geometry);
        }

        private uint ResolveDrawing(
            IReadOnlyList<object?> resources,
            uint token)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable drawing token {token} is unavailable.");
            }
            return ResolveDrawing(resource);
        }

        private uint ResolveGlyphRun(
            IReadOnlyList<object?> resources,
            uint token)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable glyph-run token {token} is unavailable.");
            }
            return ResolveGlyphRun(resource);
        }

        private uint ResolveGlyphRun(object resource)
        {
            if (_glyphRunHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            if (!WpfResourceResolver.TryAdaptNativeGlyphRun(
                    resource, out WpfNativeGlyphRun glyphRun))
            {
                throw MissingContract(
                    $"{nameof(IPortableNativeGlyphRunSource)} or " +
                    nameof(IPortableGlyphRunSource));
            }
            if (glyphRun.GlyphIndices.Length == 0 ||
                glyphRun.GlyphPositions.Length < glyphRun.GlyphIndices.Length ||
                !glyphRun.Transform.IsIdentity ||
                !glyphRun.HasBounds ||
                glyphRun.Font.FaceIndex < 0 ||
                glyphRun.Font.FontData.IsEmpty)
            {
                throw new NotSupportedException(
                    "Native MIL glyph runs require cached finite native positions, an identity glyph transform, exact bounds, and typed SFNT font bytes.");
            }
            uint handle = NextHandle();
            _glyphRunHandles.Add(resource, handle);
            WpfReplayRect bounds = glyphRun.LocalBounds;
            Batch.SetGlyphRun(
                handle,
                new NativeMilGlyphRun(
                    new NativeMilPoint(
                        glyphRun.Position.X, glyphRun.Position.Y),
                    glyphRun.FontSize,
                    new NativeMilRect(
                        bounds.X,
                        bounds.Y,
                        bounds.Width,
                        bounds.Height)),
                glyphRun.GlyphIndices,
                ReadOnlySpan<float>.Empty,
                glyphRun.GlyphPositions.AsSpan(
                    0, glyphRun.GlyphIndices.Length));
            NativeMilGlyphStyleSimulations simulations =
                NativeMilGlyphStyleSimulations.None;
            if (glyphRun.IsBold)
            {
                simulations |= NativeMilGlyphStyleSimulations.Bold;
            }
            if (glyphRun.IsItalic)
            {
                simulations |= NativeMilGlyphStyleSimulations.Italic;
            }
            GlyphRunFonts.Add(new WpfNativeMilGlyphRunFont(
                handle,
                checked((uint)glyphRun.Font.FaceIndex),
                simulations,
                glyphRun.Font.FontData));
            return handle;
        }

        private uint AddGeometryDrawing(
            object resource,
            PortableGeometryDrawingState state)
        {
            if ((state.HasBrush && state.Brush is null) ||
                (state.HasPen && state.Pen is null) ||
                (state.HasGeometry && state.Geometry is null))
            {
                throw new InvalidOperationException(
                    "Portable geometry-drawing state is incomplete.");
            }
            uint brushHandle = state.HasBrush
                ? ResolveBrush(state.Brush!)
                : 0;
            uint penHandle = state.HasPen
                ? ResolvePen(state.Pen!)
                : 0;
            uint geometryHandle = state.HasGeometry
                ? ResolveGeometry(state.Geometry!)
                : 0;
            uint handle = NextHandle();
            _drawingHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.GeometryDrawing);
            Batch.SetGeometryDrawing(
                handle,
                brushHandle,
                penHandle,
                geometryHandle);
            return handle;
        }

        private uint AddGlyphRunDrawing(
            object resource,
            PortableGlyphRunDrawingState state)
        {
            if ((state.HasGlyphRun && state.GlyphRun is null) ||
                (state.HasForegroundBrush && state.ForegroundBrush is null))
            {
                throw new InvalidOperationException(
                    "Portable glyph-run-drawing state is incomplete.");
            }
            uint glyphRunHandle = state.HasGlyphRun
                ? ResolveGlyphRun(state.GlyphRun!)
                : 0;
            uint foregroundBrushHandle = state.HasForegroundBrush
                ? ResolveBrush(state.ForegroundBrush!)
                : 0;
            uint handle = NextHandle();
            _drawingHandles.Add(resource, handle);
            Batch.CreateResource(
                handle, NativeMilResourceType.GlyphRunDrawing);
            Batch.SetGlyphRunDrawing(
                handle, glyphRunHandle, foregroundBrushHandle);
            return handle;
        }

        private uint AddImageDrawing(
            object resource,
            PortableImageDrawingState state)
        {
            if ((state.HasImageSource && state.ImageSource is null) ||
                (state.HasRect &&
                 (!double.IsFinite(state.Rect.X) ||
                  !double.IsFinite(state.Rect.Y) ||
                  !double.IsFinite(state.Rect.Width) ||
                  !double.IsFinite(state.Rect.Height) ||
                  state.Rect.Width < 0 || state.Rect.Height < 0)))
            {
                throw new InvalidOperationException(
                    "Portable image-drawing state is incomplete.");
            }
            uint imageSourceHandle = state.HasImageSource
                ? ResolveImageSource(state.ImageSource!)
                : 0;
            PortableRect rect = state.HasRect
                ? state.Rect
                : new PortableRect(0, 0, 0, 0);
            uint handle = NextHandle();
            _drawingHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.ImageDrawing);
            Batch.SetImageDrawing(
                handle,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                imageSourceHandle);
            return handle;
        }

        private uint ResolveImageSource(object imageSource)
        {
            if (_imageSourceHandles.TryGetValue(
                    imageSource, out uint existing))
            {
                return existing;
            }
            if (imageSource is IPortableDrawingImageSource drawingImageSource)
            {
                bool hasDrawing = drawingImageSource.TryGetPortableDrawingImage(
                    out object? drawing);
                if (hasDrawing && drawing is null)
                {
                    throw new InvalidOperationException(
                        "Portable drawing-image state is incomplete.");
                }
                uint drawingHandle = hasDrawing
                    ? ResolveDrawing(drawing!)
                    : 0;
                uint drawingImageHandle = NextHandle();
                Batch.CreateResource(
                    drawingImageHandle, NativeMilResourceType.DrawingImage);
                Batch.SetDrawingImage(drawingImageHandle, drawingHandle);
                _imageSourceHandles.Add(imageSource, drawingImageHandle);
                if (drawingHandle != 0)
                {
                    if (!WpfDrawingReplay.TryGetDrawingBounds(
                            drawing!, null, out Rect bounds) ||
                        !double.IsFinite(bounds.X) ||
                        !double.IsFinite(bounds.Y) ||
                        !double.IsFinite(bounds.Width) ||
                        !double.IsFinite(bounds.Height) ||
                        bounds.Width <= 0 || bounds.Height <= 0)
                    {
                        throw new NotSupportedException(
                            "Native MIL DrawingImage requires exact typed drawing content bounds.");
                    }
                    DrawingImageBounds.Add(
                        new WpfNativeMilDrawingImageBounds(
                            drawingImageHandle,
                            new NativeMilRect(
                                bounds.X,
                                bounds.Y,
                                bounds.Width,
                                bounds.Height)));
                }
                return drawingImageHandle;
            }
            if (!WpfBitmapSourceImageAdapter.TryCopyPixelsAsRgba32(
                    imageSource,
                    out byte[] pixels,
                    out int width,
                    out int height) ||
                width <= 0 || height <= 0 || width > 16_384 ||
                height > 16_384)
            {
                throw MissingContract(
                    nameof(IPortableBitmapSourcePixelsSource));
            }
            uint rowBytes = checked((uint)width * 4U);
            uint handle = NextHandle();
            Batch.CreateResource(handle, NativeMilResourceType.BitmapSource);
            _imageSourceHandles.Add(imageSource, handle);
            BitmapSources.Add(new WpfNativeMilBitmapSource(
                handle,
                checked((uint)width),
                checked((uint)height),
                rowBytes,
                pixels));
            return handle;
        }

        private uint ResolveImageSource(
            IReadOnlyList<object?> resources,
            uint token)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable image-source token {token} is unavailable.");
            }
            return ResolveImageSource(resource);
        }

        private uint AddDrawingGroup(
            object resource,
            PortableDrawingGroupState state,
            IPortableDrawingGroupChildrenSource childrenSource)
        {
            if ((state.HasTransform && state.Transform is null) ||
                (state.HasClipGeometry && state.ClipGeometry is null) ||
                (state.HasOpacityMask && state.OpacityMask is null) ||
                (state.HasGuidelineSet && state.GuidelineSet is null))
            {
                throw new InvalidOperationException(
                    "Portable drawing-group state is incomplete.");
            }
            if (state.HasEffect || state.HasBitmapEffect ||
                state.HasBitmapEffectInput || state.HasCacheMode ||
                state.HasTextRenderingMode || state.HasTextHintingMode)
            {
                throw new NotSupportedException(
                    "Portable drawing-group effects, bitmap effects, cache, and text render options are not implemented by the native MIL slice.");
            }
            if (!childrenSource.TryGetPortableDrawingGroupChildCount(
                    out int childCount))
            {
                childCount = 0;
            }
            if (childCount < 0)
            {
                throw new InvalidOperationException(
                    "The portable drawing-group child count is invalid.");
            }
            uint transformHandle = state.HasTransform
                ? ResolveTransform(state.Transform!)
                : 0;
            uint clipHandle = state.HasClipGeometry
                ? ResolveGeometry(state.ClipGeometry!)
                : 0;
            bool hasLocalBounds = TryGetDrawingGroupLocalBounds(
                state, out NativeMilRect localBounds);
            if (state.HasLocalBounds && !hasLocalBounds)
            {
                throw new InvalidOperationException(
                    "Portable DrawingGroup local bounds are invalid.");
            }
            bool hasSpatialOpacityMask = false;
            uint opacityMaskHandle = state.HasOpacityMask
                ? ResolveVisualOpacityMask(
                    state.OpacityMask!,
                    allowSpatialMask: true,
                    out hasSpatialOpacityMask)
                : 0;
            if (hasSpatialOpacityMask && !hasLocalBounds)
            {
                throw new NotSupportedException(
                    "Native MIL spatial DrawingGroup opacity masks require exact typed local content bounds.");
            }
            uint guidelineSetHandle = state.HasGuidelineSet
                ? ResolveGuidelineSet(state.GuidelineSet!)
                : 0;
            NativeMilBitmapScalingMode bitmapScalingMode =
                NativeMilBitmapScalingMode.Unspecified;
            if (state.HasBitmapScalingMode &&
                !state.HasPortableBitmapScalingMode)
            {
                throw MissingContract(nameof(PortableBitmapScalingMode));
            }
            if (state.HasPortableBitmapScalingMode)
            {
                bitmapScalingMode = state.PortableBitmapScalingMode switch
                {
                    PortableBitmapScalingMode.Unspecified =>
                        NativeMilBitmapScalingMode.Unspecified,
                    PortableBitmapScalingMode.Linear =>
                        NativeMilBitmapScalingMode.Linear,
                    PortableBitmapScalingMode.Fant =>
                        NativeMilBitmapScalingMode.Fant,
                    PortableBitmapScalingMode.NearestNeighbor =>
                        NativeMilBitmapScalingMode.NearestNeighbor,
                    _ => throw new NotSupportedException(
                        $"Bitmap scaling mode {(int)state.PortableBitmapScalingMode} is unsupported.")
                };
            }
            NativeMilEdgeMode edgeMode = NativeMilEdgeMode.Unspecified;
            if (state.HasEdgeMode && !state.HasPortableEdgeMode)
            {
                throw MissingContract(nameof(PortableEdgeMode));
            }
            if (state.HasPortableEdgeMode)
            {
                edgeMode = state.PortableEdgeMode switch
                {
                    PortableEdgeMode.Unspecified =>
                        NativeMilEdgeMode.Unspecified,
                    PortableEdgeMode.Aliased => NativeMilEdgeMode.Aliased,
                    _ => throw new NotSupportedException(
                        $"Edge mode {(int)state.PortableEdgeMode} is unsupported.")
                };
            }
            NativeMilClearTypeHint clearTypeHint =
                NativeMilClearTypeHint.Auto;
            if (state.HasClearTypeHint &&
                !state.HasPortableClearTypeHint)
            {
                throw MissingContract(nameof(PortableClearTypeHint));
            }
            if (state.HasPortableClearTypeHint)
            {
                clearTypeHint = state.PortableClearTypeHint switch
                {
                    PortableClearTypeHint.Auto =>
                        NativeMilClearTypeHint.Auto,
                    PortableClearTypeHint.Enabled =>
                        NativeMilClearTypeHint.Enabled,
                    _ => throw new NotSupportedException(
                        $"ClearType hint {(int)state.PortableClearTypeHint} is unsupported.")
                };
            }
            var childHandles = new uint[childCount];
            for (int index = 0; index < childCount; index++)
            {
                if (!childrenSource.TryGetPortableDrawingGroupChild(
                        index,
                        out object child) || child is null)
                {
                    throw new InvalidOperationException(
                        $"Portable drawing-group child {index} is unavailable.");
                }
                childHandles[index] = ResolveDrawing(child);
            }
            uint handle = NextHandle();
            _drawingHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.DrawingGroup);
            Batch.SetDrawingGroup(
                handle,
                new NativeMilDrawingGroup(
                    state.HasOpacity ? state.Opacity : 1.0,
                    ClipGeometryHandle: clipHandle,
                    OpacityMaskHandle: opacityMaskHandle,
                    TransformHandle: transformHandle,
                    GuidelineSetHandle: guidelineSetHandle,
                    EdgeMode: edgeMode,
                    BitmapScalingMode: bitmapScalingMode,
                    ClearTypeHint: clearTypeHint),
                childHandles);
            if (hasLocalBounds)
            {
                DrawingGroupBounds.Add(
                    new WpfNativeMilDrawingGroupBounds(
                        handle, localBounds));
            }
            return handle;
        }

        private uint ResolveVisualOpacityMask(
            object resource,
            bool allowSpatialMask,
            out bool isSpatialMask)
        {
            if (resource is not IPortableBrushSource source ||
                !source.TryGetPortableBrush(out PortableBrush brush))
            {
                throw MissingContract(nameof(IPortableBrushSource));
            }
            bool supportedSpatialKind =
                brush.Kind == PortableBrushKind.LinearGradient ||
                brush.Kind == PortableBrushKind.RadialGradient;
            isSpatialMask = supportedSpatialKind;
            if (brush.Kind != PortableBrushKind.SolidColor &&
                (!allowSpatialMask || !supportedSpatialKind))
            {
                throw new NotSupportedException(
                    allowSpatialMask
                        ? "Only typed solid, linear-gradient, and radial-gradient Visual opacity masks are implemented by the native MIL isolation slice."
                        : "Only static solid opacity masks are implemented by the native MIL slice.");
            }
            if (_brushHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            uint handle = AddPortableBrush(brush);
            _brushHandles.Add(resource, handle);
            return handle;
        }

        private static bool TryGetDrawingGroupLocalBounds(
            PortableDrawingGroupState state,
            out NativeMilRect bounds)
        {
            PortableRect candidate = state.LocalBounds;
            if (!state.HasLocalBounds || candidate.IsEmpty ||
                !double.IsFinite(candidate.X) ||
                !double.IsFinite(candidate.Y) ||
                !double.IsFinite(candidate.Width) ||
                !double.IsFinite(candidate.Height) ||
                candidate.Width <= 0 || candidate.Height <= 0)
            {
                bounds = default;
                return false;
            }
            bounds = new NativeMilRect(
                candidate.X,
                candidate.Y,
                candidate.Width,
                candidate.Height);
            return true;
        }

        private uint ResolveGuidelineSet(object resource)
        {
            if (_guidelineSetHandles.TryGetValue(
                    resource, out uint existing))
            {
                return existing;
            }
            if (resource is not IPortableGuidelineSetSource source ||
                !source.TryGetPortableGuidelineSet(
                    out PortableGuidelineSet guidelines))
            {
                throw MissingContract(nameof(IPortableGuidelineSetSource));
            }
            uint handle = NextHandle();
            _guidelineSetHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.GuidelineSet);
            Batch.SetGuidelineSet(
                handle,
                guidelines.IsDynamic,
                guidelines.GuidelinesX,
                guidelines.GuidelinesY);
            return handle;
        }

        private uint ResolveGuidelineSet(
            IReadOnlyList<object?> resources,
            uint token)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable guideline-set token {token} is unavailable.");
            }
            return ResolveGuidelineSet(resource);
        }

        private uint ResolveDrawing(object resource)
        {
            if (_drawingHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            if (!_activeDrawings.Add(resource))
            {
                throw new InvalidOperationException(
                    "The portable drawing graph contains a cycle.");
            }
            try
            {
                if (resource is IPortableGeometryDrawingStateSource source &&
                    source.TryGetPortableGeometryDrawingState(
                        out PortableGeometryDrawingState state))
                {
                    return AddGeometryDrawing(resource, state);
                }
                if (resource is IPortableGlyphRunDrawingStateSource glyphSource &&
                    glyphSource.TryGetPortableGlyphRunDrawingState(
                        out PortableGlyphRunDrawingState glyphState))
                {
                    return AddGlyphRunDrawing(resource, glyphState);
                }
                if (resource is IPortableImageDrawingStateSource imageSource &&
                    imageSource.TryGetPortableImageDrawingState(
                        out PortableImageDrawingState imageState))
                {
                    return AddImageDrawing(resource, imageState);
                }
                if (resource is IPortableDrawingGroupStateSource groupSource &&
                    groupSource.TryGetPortableDrawingGroupState(
                        out PortableDrawingGroupState groupState) &&
                    resource is IPortableDrawingGroupChildrenSource children)
                {
                    return AddDrawingGroup(resource, groupState, children);
                }
                throw MissingContract(
                    $"{nameof(IPortableGeometryDrawingStateSource)}, " +
                    $"{nameof(IPortableGlyphRunDrawingStateSource)}, " +
                    $"{nameof(IPortableImageDrawingStateSource)}, or " +
                    nameof(IPortableDrawingGroupStateSource));
            }
            finally
            {
                _activeDrawings.Remove(resource);
            }
        }

        private uint ResolveEffect(object resource)
        {
            if (_effectHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            if (resource is not IPortableEffectSource source ||
                !source.TryGetPortableEffect(out PortableEffect effect))
            {
                throw MissingContract(nameof(IPortableEffectSource));
            }
            uint handle = NextHandle();
            _effectHandles.Add(resource, handle);
            NativeMilEffectRenderingBias renderingBias =
                effect.RenderingBias switch
                {
                    PortableEffectRenderingBias.Performance =>
                        NativeMilEffectRenderingBias.Performance,
                    PortableEffectRenderingBias.Quality =>
                        NativeMilEffectRenderingBias.Quality,
                    _ => throw new NotSupportedException(
                        $"Effect rendering bias {(int)effect.RenderingBias} is unsupported.")
                };
            switch (effect.Kind)
            {
                case PortableEffectKind.Blur:
                    NativeMilBlurKernelType kernelType =
                        effect.BlurKernel switch
                        {
                            PortableBlurKernel.Gaussian =>
                                NativeMilBlurKernelType.Gaussian,
                            PortableBlurKernel.Box =>
                                NativeMilBlurKernelType.Box,
                            _ => throw new NotSupportedException(
                                $"Blur kernel {(int)effect.BlurKernel} is unsupported.")
                        };
                    Batch.CreateResource(
                        handle, NativeMilResourceType.BlurEffect);
                    Batch.SetBlurEffect(
                        handle,
                        effect.Radius,
                        renderingBias,
                        kernelType);
                    break;
                case PortableEffectKind.DropShadow:
                    Batch.CreateResource(
                        handle, NativeMilResourceType.DropShadowEffect);
                    Batch.SetDropShadowEffect(
                        handle,
                        effect.ShadowDepth,
                        ToLinearColor(effect.Color),
                        effect.Direction,
                        effect.Opacity,
                        effect.BlurRadius,
                        renderingBias);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Portable effect kind {(int)effect.Kind} is unsupported.");
            }
            return handle;
        }

        private uint ResolveBitmapCache(object resource)
        {
            if (_bitmapCacheHandles.TryGetValue(
                    resource, out uint existing))
            {
                return existing;
            }
            if (resource is not IPortableBitmapCacheSource source ||
                !source.TryGetPortableBitmapCache(
                    out PortableBitmapCache cache))
            {
                throw MissingContract(nameof(IPortableBitmapCacheSource));
            }
            if (!double.IsFinite(cache.RenderAtScale))
            {
                throw new InvalidOperationException(
                    "Portable BitmapCache RenderAtScale must be finite.");
            }
            uint handle = NextHandle();
            _bitmapCacheHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.BitmapCache);
            Batch.SetBitmapCache(
                handle,
                new NativeMilBitmapCache(
                    cache.RenderAtScale,
                    cache.SnapsToDevicePixels,
                    cache.EnableClearType));
            return handle;
        }

        private static bool TryGetVisualBounds(
            object visual,
            out NativeMilRect bounds)
        {
            bounds = default;
            if (visual is not IPortableVisualBoundsSource source ||
                !source.TryGetPortableVisualBounds(
                    out PortableVisualBounds portableBounds))
            {
                return false;
            }
            PortableRect candidate;
            if (portableBounds.HasDescendantBounds)
            {
                candidate = portableBounds.DescendantBounds;
            }
            else if (portableBounds.HasContentBounds)
            {
                candidate = portableBounds.ContentBounds;
            }
            else
            {
                return false;
            }
            if (candidate.IsEmpty ||
                !double.IsFinite(candidate.X) ||
                !double.IsFinite(candidate.Y) ||
                !double.IsFinite(candidate.Width) ||
                !double.IsFinite(candidate.Height) ||
                candidate.Width <= 0 || candidate.Height <= 0)
            {
                return false;
            }
            bounds = new NativeMilRect(
                candidate.X,
                candidate.Y,
                candidate.Width,
                candidate.Height);
            return true;
        }

        private uint AddPathGeometry(
            object resource,
            PortableGeometryPath path)
        {
            if (!WpfPortableGeometryBoundsReader.TryGetLocalGeometryBounds(
                    path,
                    out WpfReplayRect bounds))
            {
                throw new NotSupportedException(
                    "Portable path geometry has no exact local bounds for native MIL replay.");
            }
            var figures = new NativeMilPathFigure[path.Figures.Length];
            for (int figureIndex = 0;
                figureIndex < path.Figures.Length;
                figureIndex++)
            {
                PortablePathFigure figure = path.Figures[figureIndex];
                var segments = new NativeMilPathSegment[
                    figure.Segments.Length];
                for (int segmentIndex = 0;
                    segmentIndex < figure.Segments.Length;
                    segmentIndex++)
                {
                    segments[segmentIndex] = ToNativePathSegment(
                        figure.Segments[segmentIndex]);
                }
                figures[figureIndex] = new NativeMilPathFigure(
                    ToNativePoint(figure.StartPoint),
                    figure.IsFilled,
                    figure.IsClosed,
                    segments);
            }

            uint transformHandle = AddGeometryTransform(path.Transform);
            uint handle = NextHandle();
            _geometryHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.PathGeometry);
            Batch.SetPathGeometry(
                handle,
                new NativeMilPathGeometry(
                    path.FillRule == PortableFillRule.EvenOdd
                        ? NativeMilPathFillRule.EvenOdd
                        : NativeMilPathFillRule.Nonzero,
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height,
                    figures),
                transformHandle);
            return handle;
        }

        private static bool IsSingleStrokedLine(PortableGeometryPath path)
        {
            return path.Figures.Length == 1 &&
                !path.Figures[0].IsClosed &&
                path.Figures[0].Segments.Length == 1 &&
                path.Figures[0].Segments[0].Kind ==
                    PortablePathSegmentKind.Line &&
                path.Figures[0].Segments[0].IsStroked;
        }

        private static NativeMilPathSegment ToNativePathSegment(
            PortablePathSegment segment) => segment.Kind switch
            {
                PortablePathSegmentKind.Line =>
                    NativeMilPathSegment.Line(
                        ToNativePoint(segment.Point1),
                        segment.IsStroked,
                        segment.IsSmoothJoin),
                PortablePathSegmentKind.QuadraticBezier =>
                    NativeMilPathSegment.QuadraticBezier(
                        ToNativePoint(segment.Point1),
                        ToNativePoint(segment.Point2),
                        segment.IsStroked,
                        segment.IsSmoothJoin),
                PortablePathSegmentKind.CubicBezier =>
                    NativeMilPathSegment.CubicBezier(
                        ToNativePoint(segment.Point1),
                        ToNativePoint(segment.Point2),
                        ToNativePoint(segment.Point3),
                        segment.IsStroked,
                        segment.IsSmoothJoin),
                PortablePathSegmentKind.Arc =>
                    NativeMilPathSegment.Arc(
                        ToNativePoint(segment.Point1),
                        segment.Size.Width,
                        segment.Size.Height,
                        segment.RotationAngle,
                        segment.IsLargeArc,
                        segment.SweepDirection ==
                            PortableSweepDirection.Clockwise,
                        segment.IsStroked,
                        segment.IsSmoothJoin),
                _ => throw new NotSupportedException(
                    $"Portable path segment kind {segment.Kind} is not implemented by the native MIL slice.")
            };

        private static NativeMilPoint ToNativePoint(PortablePoint point) =>
            new(point.X, point.Y);

        private uint AddPrimitiveGeometry(
            object resource,
            PortablePrimitiveGeometry geometry)
        {
            uint transformHandle = AddGeometryTransform(geometry.Transform);
            uint handle = NextHandle();
            _geometryHandles.Add(resource, handle);
            switch (geometry.Kind)
            {
                case PortablePrimitiveGeometryKind.Line:
                    Batch.CreateResource(
                        handle,
                        NativeMilResourceType.LineGeometry);
                    Batch.SetLineGeometry(
                        handle,
                        geometry.Point1.X,
                        geometry.Point1.Y,
                        geometry.Point2.X,
                        geometry.Point2.Y,
                        transformHandle);
                    break;
                case PortablePrimitiveGeometryKind.Rectangle:
                    if (geometry.Rect.IsEmpty)
                    {
                        throw new NotSupportedException(
                            "Empty rectangle geometry is not implemented by the native MIL slice.");
                    }
                    if (geometry.RadiusX != geometry.RadiusY &&
                        (geometry.RadiusX == 0 || geometry.RadiusY == 0) &&
                        (geometry.Rect.Width == 0 || geometry.Rect.Height == 0))
                    {
                        throw new NotSupportedException(
                            "Native MIL degenerate zero-axis asymmetric rounded-rectangle geometry radii are not implemented yet.");
                    }
                    Batch.CreateResource(
                        handle,
                        NativeMilResourceType.RectangleGeometry);
                    Batch.SetRectangleGeometry(
                        handle,
                        geometry.Rect.X,
                        geometry.Rect.Y,
                        geometry.Rect.Width,
                        geometry.Rect.Height,
                        geometry.RadiusX,
                        geometry.RadiusY,
                        transformHandle);
                    break;
                case PortablePrimitiveGeometryKind.Ellipse:
                    Batch.CreateResource(
                        handle,
                        NativeMilResourceType.EllipseGeometry);
                    Batch.SetEllipseGeometry(
                        handle,
                        geometry.Point1.X,
                        geometry.Point1.Y,
                        geometry.RadiusX,
                        geometry.RadiusY,
                        transformHandle);
                    break;
                default:
                    throw new NotSupportedException(
                        $"Portable primitive geometry kind {geometry.Kind} is not implemented by the native MIL slice.");
            }
            return handle;
        }

        private uint AddGeometryTransform(PortableMatrix3x2 transform)
        {
            if (transform.IsIdentity)
            {
                return 0;
            }
            uint handle = NextHandle();
            Batch.CreateResource(
                handle,
                NativeMilResourceType.MatrixTransform);
            Batch.SetMatrixTransform(
                handle,
                new NativeMilMatrix3x2(
                    transform.M11,
                    transform.M12,
                    transform.M21,
                    transform.M22,
                    transform.OffsetX,
                    transform.OffsetY));
            return handle;
        }

        private static NativeMilPenLineCap ToNativeLineCap(
            PortablePenLineCap cap) => cap switch
            {
                PortablePenLineCap.Flat => NativeMilPenLineCap.Flat,
                PortablePenLineCap.Square => NativeMilPenLineCap.Square,
                PortablePenLineCap.Round => NativeMilPenLineCap.Round,
                PortablePenLineCap.Triangle => NativeMilPenLineCap.Triangle,
                _ => throw new ArgumentOutOfRangeException(nameof(cap))
            };

        private static NativeMilPenLineJoin ToNativeLineJoin(
            PortablePenLineJoin join) => join switch
            {
                PortablePenLineJoin.Miter => NativeMilPenLineJoin.Miter,
                PortablePenLineJoin.Bevel => NativeMilPenLineJoin.Bevel,
                PortablePenLineJoin.Round => NativeMilPenLineJoin.Round,
                _ => throw new ArgumentOutOfRangeException(nameof(join))
            };

        private static NativeMilGradientInterpolation
            ToNativeGradientInterpolation(
                PortableGradientColorInterpolationMode interpolation) =>
                interpolation switch
                {
                    PortableGradientColorInterpolationMode.ScRgbLinearInterpolation =>
                        NativeMilGradientInterpolation.ScRgb,
                    PortableGradientColorInterpolationMode.SRgbLinearInterpolation =>
                        NativeMilGradientInterpolation.SRgb,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(interpolation))
                };

        private static NativeMilBrushMappingMode ToNativeBrushMappingMode(
            PortableBrushMappingMode mappingMode) => mappingMode switch
            {
                PortableBrushMappingMode.Absolute =>
                    NativeMilBrushMappingMode.Absolute,
                PortableBrushMappingMode.RelativeToBoundingBox =>
                    NativeMilBrushMappingMode.RelativeToBoundingBox,
                _ => throw new ArgumentOutOfRangeException(nameof(mappingMode))
            };

        private static NativeMilGradientSpreadMethod
            ToNativeGradientSpreadMethod(
                PortableGradientSpreadMethod spreadMethod) =>
                spreadMethod switch
                {
                    PortableGradientSpreadMethod.Pad =>
                        NativeMilGradientSpreadMethod.Pad,
                    PortableGradientSpreadMethod.Reflect =>
                        NativeMilGradientSpreadMethod.Reflect,
                    PortableGradientSpreadMethod.Repeat =>
                        NativeMilGradientSpreadMethod.Repeat,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(spreadMethod))
                };

        private static NativeMilGradientStop[] ToNativeGradientStops(
            PortableGradientStop[] stops)
        {
            var result = new NativeMilGradientStop[stops.Length];
            for (int index = 0; index < stops.Length; index++)
            {
                result[index] = new NativeMilGradientStop(
                    stops[index].Offset,
                    ToLinearColor(stops[index].Color));
            }
            return result;
        }

        private static NativeMilColor ToLinearColor(PortableColor color)
        {
            return new NativeMilColor(
                SrgbToLinear(color.R),
                SrgbToLinear(color.G),
                SrgbToLinear(color.B),
                color.A / 255.0f);
        }

        private static float SrgbToLinear(byte component)
        {
            float value = component / 255.0f;
            return value <= 0.04045f
                ? value / 12.92f
                : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
        }

        private static double ReadDouble(
            ReadOnlySpan<byte> source,
            int offset)
        {
            long bits = BinaryPrimitives.ReadInt64LittleEndian(source[offset..]);
            return BitConverter.Int64BitsToDouble(bits);
        }

        private static float ReadSingle(
            ReadOnlySpan<byte> source,
            int offset)
        {
            return BinaryPrimitives.ReadSingleLittleEndian(source[offset..]);
        }

        private static NativeMilViewport3DScene CreateNativeViewport3DScene(
            PortableViewport3DScene scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (scene.Camera is null || scene.Viewport.IsEmpty ||
                !TryToFiniteFloat(scene.Viewport.X, out float viewportX) ||
                !TryToFiniteFloat(scene.Viewport.Y, out float viewportY) ||
                !TryToFinitePositiveFloat(
                    scene.Viewport.Width, out float viewportWidth) ||
                !TryToFinitePositiveFloat(
                    scene.Viewport.Height, out float viewportHeight))
            {
                throw new NotSupportedException(
                    "Native MIL Viewport3D requires a finite positive typed viewport and camera.");
            }

            float aspectRatio = viewportWidth / viewportHeight;
            if (!TryCreateViewportCamera(
                    scene.Camera,
                    aspectRatio,
                    out Matrix4x4 projection,
                    out Matrix4x4 view,
                    out Vector3 cameraPosition))
            {
                throw new NotSupportedException(
                    "Native MIL Viewport3D camera state is invalid or unsupported.");
            }

            PortableViewport3DMesh[] sourceMeshes =
                scene.Meshes ?? Array.Empty<PortableViewport3DMesh>();
            NativeSceneLight3D[] nativeLights = CreateNativeLights(scene.Lights);
            if (sourceMeshes.Length == 0)
            {
                throw new NotSupportedException(
                    "Native MIL Viewport3D requires at least one typed mesh.");
            }

            int totalVertexCount = 0;
            int totalIndexCount = 0;
            int totalMeshCount = 0;
            for (int meshIndex = 0;
                 meshIndex < sourceMeshes.Length;
                 meshIndex++)
            {
                PortableViewport3DMesh? mesh = sourceMeshes[meshIndex];
                if (mesh is null ||
                    mesh.Positions is null || mesh.Normals is null ||
                    mesh.TextureCoordinates is null ||
                    mesh.Indices is null || mesh.Positions.Length < 3 ||
                    mesh.Normals.Length < mesh.Positions.Length ||
                    mesh.Indices.Length < 3 ||
                    mesh.Indices.Length % 3 != 0)
                {
                    throw new NotSupportedException(
                        "Native MIL Viewport3D requires triangle meshes with one typed normal per position.");
                }
                if (mesh.Materials is null)
                {
                    throw new NotSupportedException(
                        "Native MIL Viewport3D material-layer storage cannot be null.");
                }
                totalVertexCount = checked(
                    totalVertexCount + mesh.Positions.Length);
                totalIndexCount = checked(
                    totalIndexCount + mesh.Indices.Length);
                totalMeshCount = checked(totalMeshCount +
                    Math.Max(1, mesh.Materials.Length));
            }

            var nativeMeshes =
                new NativeSceneMesh3D[totalMeshCount];
            var nativeMaterials =
                new NativeSceneBrush[totalMeshCount];
            Array.Fill(
                nativeMaterials,
                NativeSceneBrush.Solid(Vector4.One));
            var nativeGradientStops =
                new List<NativeSceneGradientStop>();
            bool hasNativeGradientMaterial = false;
            var vertices =
                new NativeSceneMesh3DVertex[totalVertexCount];
            var indices = new uint[totalIndexCount];
            Vector3 lightDirection = ToFiniteVector3(
                scene.LightDirection, nameof(scene.LightDirection));
            if (lightDirection.LengthSquared() <= 0.000001f ||
                !TryToFiniteNonNegativeFloat(
                    scene.LightIntensity, out float lightIntensity))
            {
                throw new NotSupportedException(
                    "Native MIL Viewport3D requires a finite nonzero light direction and nonnegative intensity.");
            }
            Vector3 sceneAmbient = ToFiniteVector3(
                scene.AmbientColor, nameof(scene.AmbientColor));
            if (!TryToFiniteNonNegativeFloat(
                    scene.AmbientIntensity, out float ambientIntensity))
            {
                throw new NotSupportedException(
                    "Native MIL Viewport3D ambient intensity must be finite and nonnegative.");
            }

            int vertexOffset = 0;
            int indexOffset = 0;
            int nativeMeshIndex = 0;
            for (int meshIndex = 0;
                 meshIndex < sourceMeshes.Length;
                 meshIndex++)
            {
                PortableViewport3DMesh mesh = sourceMeshes[meshIndex];
                Matrix4x4 model = ToFiniteMatrix(
                    mesh.ModelTransform,
                    nameof(mesh.ModelTransform));
                if (!Matrix4x4.Invert(model, out Matrix4x4 inverseModel))
                {
                    throw new NotSupportedException(
                        "Native MIL Viewport3D mesh transforms must be invertible.");
                }
                Matrix4x4 normal = Matrix4x4.Transpose(inverseModel);
                if (!IsFinite(normal))
                {
                    throw new NotSupportedException(
                        "Native MIL Viewport3D normal transform is invalid.");
                }

                for (int vertexIndex = 0;
                     vertexIndex < mesh.Positions.Length;
                     vertexIndex++)
                {
                    Vector2 textureCoordinate = vertexIndex <
                        mesh.TextureCoordinates.Length
                            ? ToFiniteVector2(
                                mesh.TextureCoordinates[vertexIndex],
                                nameof(mesh.TextureCoordinates))
                            : Vector2.Zero;
                    vertices[vertexOffset + vertexIndex] =
                        new NativeSceneMesh3DVertex
                        {
                            Position = new NativePoint3D(
                                ToFiniteVector3(
                                    mesh.Positions[vertexIndex],
                                    nameof(mesh.Positions))),
                            Normal = new NativePoint3D(
                                NormalizeFiniteOrZero(
                                    ToFiniteVector3(
                                        mesh.Normals[vertexIndex],
                                        nameof(mesh.Normals)),
                                    nameof(mesh.Normals))),
                            TextureCoordinate = textureCoordinate,
                            Reserved0 = 0U,
                            Reserved1 = 0U
                        };
                }
                for (int meshIndexOffset = 0;
                     meshIndexOffset < mesh.Indices.Length;
                     meshIndexOffset++)
                {
                    int sourceIndex = mesh.Indices[meshIndexOffset];
                    if ((uint)sourceIndex >=
                        (uint)mesh.Positions.Length)
                    {
                        throw new NotSupportedException(
                            "Native MIL Viewport3D mesh indices must address the typed position range.");
                    }
                    indices[indexOffset + meshIndexOffset] =
                        (uint)sourceIndex;
                }

                var nativeMesh = new NativeSceneMesh3D
                {
                    StructSize = (uint)Unsafe.SizeOf<NativeSceneMesh3D>(),
                    Flags = (uint)(mesh.IsBackFace
                        ? NativeMesh3DFlags.BackFace
                        : NativeMesh3DFlags.FrontFace),
                    Topology = (uint)NativeMesh3DTopology.Triangles,
                    RenderMode = (uint)NativeMesh3DRenderMode.Solid,
                    VertexOffset = (uint)vertexOffset,
                    VertexCount = (uint)mesh.Positions.Length,
                    IndexOffset = (uint)indexOffset,
                    IndexCount = (uint)mesh.Indices.Length,
                    ModelTransform = new NativeMatrix4x4(model),
                    NormalTransform = new NativeMatrix4x4(normal),
                    LightDirection = ToNativeFloat4(
                        lightDirection, lightIntensity),
                    AmbientColor = ToNativeFloat4(
                        sceneAmbient, ambientIntensity),
                    ShadingMode = 1U,
                    LightOffset = 0U,
                    LightCount = (uint)nativeLights.Length
                };
                if (mesh.Materials.Length == 0)
                {
                    if (!TryToFiniteUnitFloat(
                            mesh.Opacity, out float opacity) ||
                        !TryToFinitePositiveFloat(
                            mesh.Shininess, out float shininess))
                    {
                        throw new NotSupportedException(
                            "Native MIL Viewport3D material state is invalid.");
                    }
                    Vector4 diffuse = ToFiniteVector4(
                        mesh.DiffuseColor, nameof(mesh.DiffuseColor));
                    Vector3 specular = ToFiniteVector3(
                        mesh.SpecularColor, nameof(mesh.SpecularColor));
                    Vector3 materialAmbient = ToFiniteVector3(
                        mesh.AmbientColor, nameof(mesh.AmbientColor));
                    nativeMesh.Color = diffuse;
                    nativeMesh.SpecularColor = ToNativeFloat4(
                        specular,
                        shininess);
                    nativeMesh.MaterialAmbient = ToNativeFloat4(
                        materialAmbient,
                        1.0f);
                    nativeMesh.Opacity = opacity;
                    nativeMeshes[nativeMeshIndex++] = nativeMesh;
                }
                else
                {
                    for (int materialIndex = 0;
                         materialIndex < mesh.Materials.Length;
                         materialIndex++)
                    {
                        if (!WpfViewport3DMaterialMapper.TryMapNative(
                                mesh.Materials[materialIndex],
                                out WpfViewport3DMaterialPass materialPass))
                        {
                            throw new NotSupportedException(
                                "Native MIL Viewport3D requires typed solid, linear-gradient, or radial-gradient material layers.");
                        }
                        NativeSceneMesh3D materialMesh = nativeMesh;
                        materialMesh.Color = materialPass.Color;
                        materialMesh.SpecularColor = ToNativeFloat4(
                            materialPass.SpecularColor,
                            materialPass.Shininess);
                        materialMesh.MaterialAmbient = ToNativeFloat4(
                            materialPass.AmbientColor,
                            1.0f);
                        materialMesh.Opacity = materialPass.Opacity;
                        materialMesh.ShadingMode =
                            materialPass.IsUnlit ? 0U : 1U;
                        if (materialPass.Kind ==
                            PortableViewport3DMaterialKind.Specular &&
                            materialPass.MaterialBrush is not null)
                        {
                            materialMesh.Flags |= (uint)
                                NativeMesh3DFlags.SpecularMaterial;
                        }
                        int materialMeshIndex = nativeMeshIndex++;
                        nativeMeshes[materialMeshIndex] = materialMesh;
                        if (materialPass.MaterialBrush is not null)
                        {
                            nativeMaterials[materialMeshIndex] =
                                CreateNativeMeshMaterial(
                                    materialPass.MaterialBrush,
                                    nativeGradientStops);
                            hasNativeGradientMaterial = true;
                        }
                    }
                }
                vertexOffset += mesh.Positions.Length;
                indexOffset += mesh.Indices.Length;
            }

            var nativeScene = new NativeMilViewport3DScene(
                new NativeSceneCamera3D(
                    projection, view, cameraPosition),
                new NativeImageRect(
                    viewportX,
                    viewportY,
                    viewportWidth,
                    viewportHeight),
                nativeMeshes,
                vertices,
                indices,
                nativeLights);
            return hasNativeGradientMaterial
                ? nativeScene with
                {
                    Materials = nativeMaterials,
                    GradientStops = nativeGradientStops.ToArray()
                }
                : nativeScene;
        }

        private static NativeSceneBrush CreateNativeMeshMaterial(
            global::ProGPU.Vector.Brush source,
            List<NativeSceneGradientStop> gradientStops)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(gradientStops);
            global::ProGPU.Vector.GradientStop[] sourceStops;
            Vector2 start;
            Vector2 end = default;
            Vector2 center = default;
            float radiusX = 0.0f;
            float radiusY = 0.0f;
            Matrix4x4 coordinateTransform;
            global::ProGPU.Vector.GradientSpreadMethod spread;
            global::ProGPU.Vector.GradientColorInterpolationMode
                interpolation;
            bool radial;
            if (source is global::ProGPU.Vector.LinearGradientBrush linear)
            {
                sourceStops = linear.Stops;
                start = linear.StartPoint;
                end = linear.EndPoint;
                coordinateTransform = linear.CoordinateTransform;
                spread = linear.SpreadMethod;
                interpolation = linear.ColorInterpolationMode;
                radial = false;
            }
            else if (source is
                global::ProGPU.Vector.RadialGradientBrush radialBrush)
            {
                sourceStops = radialBrush.Stops;
                start = radialBrush.GradientOrigin;
                center = radialBrush.Center;
                radiusX = radialBrush.RadiusX;
                radiusY = radialBrush.RadiusY;
                coordinateTransform = radialBrush.CoordinateTransform;
                spread = radialBrush.SpreadMethod;
                interpolation = radialBrush.ColorInterpolationMode;
                radial = true;
            }
            else
            {
                throw new NotSupportedException(
                    "Native MIL Mesh3D material sidebands accept only typed linear and radial ProGPU brushes.");
            }

            if (sourceStops is null || sourceStops.Length == 0 ||
                !float.IsFinite(source.Opacity) || source.Opacity < 0.0f ||
                source.Opacity > 1.0f ||
                !IsFinite(start) || !IsFinite(end) || !IsFinite(center) ||
                !float.IsFinite(radiusX) || !float.IsFinite(radiusY) ||
                (radial && (radiusX <= 0.0f || radiusY <= 0.0f)) ||
                !IsFinite2DAffine(coordinateTransform))
            {
                throw new NotSupportedException(
                    "Native MIL Mesh3D gradient material state is invalid.");
            }

            int stopOffset = gradientStops.Count;
            for (int index = 0; index < sourceStops.Length; index++)
            {
                global::ProGPU.Vector.GradientStop stop = sourceStops[index];
                if (!IsFinite(stop.Color) || !float.IsFinite(stop.Offset))
                {
                    throw new NotSupportedException(
                        "Native MIL Mesh3D gradient stops must be finite.");
                }
                gradientStops.Add(new NativeSceneGradientStop(
                    stop.Color,
                    stop.Offset));
            }
            ReadOnlySpan<NativeSceneGradientStop> localStops =
                CollectionsMarshal.AsSpan(gradientStops).Slice(
                    stopOffset,
                    sourceStops.Length);
            var nativeTransform = new Matrix3x2(
                coordinateTransform.M11,
                coordinateTransform.M12,
                coordinateTransform.M21,
                coordinateTransform.M22,
                coordinateTransform.M41,
                coordinateTransform.M42);
            var nativeSpread = (NativeSceneGradientSpread)(uint)spread;
            var nativeInterpolation =
                (NativeSceneGradientInterpolation)(uint)interpolation;
            return radial
                ? NativeSceneBrush.RadialGradient(
                    center,
                    start,
                    radiusX,
                    radiusY,
                    (uint)stopOffset,
                    localStops,
                    source.Opacity,
                    nativeSpread,
                    nativeInterpolation,
                    nativeTransform)
                : NativeSceneBrush.LinearGradient(
                    start,
                    end,
                    (uint)stopOffset,
                    localStops,
                    source.Opacity,
                    nativeSpread,
                    nativeInterpolation,
                    nativeTransform);
        }

        private static bool IsFinite2DAffine(Matrix4x4 value) =>
            IsFinite(value) &&
            MathF.Abs(value.M13) <= 0.0001f &&
            MathF.Abs(value.M14) <= 0.0001f &&
            MathF.Abs(value.M23) <= 0.0001f &&
            MathF.Abs(value.M24) <= 0.0001f &&
            MathF.Abs(value.M31) <= 0.0001f &&
            MathF.Abs(value.M32) <= 0.0001f &&
            MathF.Abs(value.M33 - 1.0f) <= 0.0001f &&
            MathF.Abs(value.M34) <= 0.0001f &&
            MathF.Abs(value.M43) <= 0.0001f &&
            MathF.Abs(value.M44 - 1.0f) <= 0.0001f;

        private static NativeSceneLight3D[] CreateNativeLights(
            PortableViewport3DLight[]? lights)
        {
            if (lights is null || lights.Length == 0)
            {
                return [];
            }
            if (lights.Length > 16)
            {
                throw new NotSupportedException(
                    "Native MIL Viewport3D supports at most 16 typed lights per mesh.");
            }

            var result = new NativeSceneLight3D[lights.Length];
            for (int index = 0; index < lights.Length; index++)
            {
                PortableViewport3DLight light = lights[index] ??
                    throw new NotSupportedException(
                        "Native MIL Viewport3D light entries cannot be null.");
                Vector4 color = ToFiniteVector4(
                    light.Color, nameof(light.Color));
                var native = new NativeSceneLight3D
                {
                    StructSize = (uint)Unsafe.SizeOf<NativeSceneLight3D>(),
                    Kind = (uint)(NativeLight3DKind)light.Kind,
                    Flags = 0U,
                    Reserved0 = 0U,
                    Color = color
                };
                switch (light.Kind)
                {
                    case PortableViewport3DLightKind.Ambient:
                        break;
                    case PortableViewport3DLightKind.Directional:
                        Vector3 directional = ToFiniteVector3(
                            light.Direction, nameof(light.Direction));
                        if (directional.LengthSquared() <= 0.000001f)
                        {
                            throw new NotSupportedException(
                                "Native MIL Viewport3D directional lights require a nonzero direction.");
                        }
                        native.DirectionInnerCos = ToNativeFloat4(
                            Vector3.Normalize(directional), 0.0f);
                        break;
                    case PortableViewport3DLightKind.Point:
                        PopulateNativePointLight(light, ref native);
                        break;
                    case PortableViewport3DLightKind.Spot:
                        PopulateNativePointLight(light, ref native);
                        Vector3 spotDirection = ToFiniteVector3(
                            light.Direction, nameof(light.Direction));
                        if (spotDirection.LengthSquared() <= 0.000001f)
                        {
                            throw new NotSupportedException(
                                "Native MIL Viewport3D spot lights require a nonzero direction.");
                        }
                        float outerAngle = ClampConeAngle(
                            light.OuterConeAngle,
                            nameof(light.OuterConeAngle));
                        float innerAngle = MathF.Min(
                            ClampConeAngle(
                                light.InnerConeAngle,
                                nameof(light.InnerConeAngle)),
                            outerAngle);
                        float innerCos = ConeHalfAngleCosine(innerAngle);
                        native.DirectionInnerCos = ToNativeFloat4(
                            Vector3.Normalize(spotDirection), innerCos);
                        native.AttenuationOuterCos.W =
                            ConeHalfAngleCosine(outerAngle);
                        break;
                    default:
                        throw new NotSupportedException(
                            $"Native MIL Viewport3D light kind {light.Kind} is unsupported.");
                }
                result[index] = native;
            }
            return result;
        }

        private static void PopulateNativePointLight(
            PortableViewport3DLight source,
            ref NativeSceneLight3D target)
        {
            Vector3 position = ToFiniteVector3(
                source.Position, nameof(source.Position));
            float range = double.IsPositiveInfinity(source.Range)
                ? float.MaxValue
                : ToFiniteFloat(source.Range, nameof(source.Range));
            if (range <= 0.0f)
            {
                throw new NotSupportedException(
                    "Native MIL Viewport3D point and spot light ranges must be positive.");
            }
            float constantAttenuation = ToFiniteFloat(
                source.ConstantAttenuation,
                nameof(source.ConstantAttenuation));
            float linearAttenuation = ToFiniteFloat(
                source.LinearAttenuation,
                nameof(source.LinearAttenuation));
            float quadraticAttenuation = ToFiniteFloat(
                source.QuadraticAttenuation,
                nameof(source.QuadraticAttenuation));
            if (constantAttenuation < 0.0f || linearAttenuation < 0.0f ||
                quadraticAttenuation < 0.0f ||
                (constantAttenuation == 0.0f &&
                    linearAttenuation == 0.0f &&
                    quadraticAttenuation == 0.0f))
            {
                throw new NotSupportedException(
                    "Native MIL Viewport3D point and spot light attenuation must be nonnegative and contain a positive term.");
            }
            target.PositionRange = ToNativeFloat4(position, range);
            target.AttenuationOuterCos = new NativeFloat4
            {
                X = constantAttenuation,
                Y = linearAttenuation,
                Z = quadraticAttenuation,
                W = 0.0f
            };
        }

        private static float ClampConeAngle(
            double angle,
            string parameterName)
        {
            float value = ToFiniteFloat(angle, parameterName);
            return Math.Clamp(value, 0.0f, 180.0f);
        }

        private static float ConeHalfAngleCosine(float angle) =>
            MathF.Cos(angle * (MathF.PI / 360.0f));

        private static bool TryCreateViewportCamera(
            PortableViewport3DCamera camera,
            float aspectRatio,
            out Matrix4x4 projection,
            out Matrix4x4 view,
            out Vector3 position)
        {
            projection = Matrix4x4.Identity;
            view = Matrix4x4.Identity;
            position = Vector3.Zero;
            if (!float.IsFinite(aspectRatio) || aspectRatio <= 0.0f)
            {
                return false;
            }
            if (camera.Kind == PortableViewport3DCameraKind.Matrix)
            {
                projection = ToFiniteMatrix(
                    camera.ProjectionMatrix,
                    nameof(camera.ProjectionMatrix));
                view = ToFiniteMatrix(
                    camera.ViewMatrix,
                    nameof(camera.ViewMatrix));
                if (!Matrix4x4.Invert(view, out Matrix4x4 cameraToWorld))
                {
                    return false;
                }
                position = Vector3.Transform(Vector3.Zero, cameraToWorld);
                return IsFinite(projection) && IsFinite(view) &&
                    IsFinite(position);
            }
            position = ToFiniteVector3(
                camera.Position, nameof(camera.Position));
            Vector3 lookDirection = ToFiniteVector3(
                camera.LookDirection, nameof(camera.LookDirection));
            Vector3 upDirection = ToFiniteVector3(
                camera.UpDirection, nameof(camera.UpDirection));
            if (lookDirection.LengthSquared() <= 0.000001f ||
                upDirection.LengthSquared() <= 0.000001f)
            {
                return false;
            }
            if (camera.HasTransform)
            {
                Matrix4x4 transform = ToFiniteMatrix(
                    camera.Transform, nameof(camera.Transform));
                position = Vector3.Transform(position, transform);
                lookDirection = Vector3.TransformNormal(
                    lookDirection, transform);
                upDirection = Vector3.TransformNormal(
                    upDirection, transform);
            }
            if (!IsFinite(position) || !IsFinite(lookDirection) ||
                !IsFinite(upDirection) ||
                lookDirection.LengthSquared() <= 0.000001f ||
                upDirection.LengthSquared() <= 0.000001f)
            {
                return false;
            }
            view = Matrix4x4.CreateLookAt(
                position, position + lookDirection, upDirection);
            if (!TryToFinitePositiveFloat(
                    camera.NearPlaneDistance, out float nearPlane))
            {
                return false;
            }
            float farPlane;
            if (!TryToFiniteFloat(
                    camera.FarPlaneDistance, out farPlane) ||
                farPlane <= nearPlane)
            {
                return false;
            }
            switch (camera.Kind)
            {
                case PortableViewport3DCameraKind.Orthographic:
                    if (!TryToFinitePositiveFloat(
                            camera.Width, out float width))
                    {
                        return false;
                    }
                    projection = Matrix4x4.CreateOrthographic(
                        width,
                        width / aspectRatio,
                        nearPlane,
                        farPlane);
                    break;
                case PortableViewport3DCameraKind.Perspective:
                    if (!TryToFiniteFloat(
                            camera.FieldOfView,
                            out float horizontalFovDegrees) ||
                        horizontalFovDegrees <= 0.0f ||
                        horizontalFovDegrees >= 180.0f)
                    {
                        return false;
                    }
                    float horizontalFovRadians =
                        horizontalFovDegrees * MathF.PI / 180.0f;
                    float verticalFovRadians = 2.0f * MathF.Atan(
                        MathF.Tan(horizontalFovRadians * 0.5f) /
                        aspectRatio);
                    projection = Matrix4x4.CreatePerspectiveFieldOfView(
                        verticalFovRadians,
                        aspectRatio,
                        nearPlane,
                        farPlane);
                    break;
                default:
                    return false;
            }
            return IsFinite(projection) && IsFinite(view);
        }

        private static Vector3 ToFiniteVector3(
            PortableVector3 value,
            string parameterName)
        {
            if (!TryToFiniteFloat(value.X, out float x) ||
                !TryToFiniteFloat(value.Y, out float y) ||
                !TryToFiniteFloat(value.Z, out float z))
            {
                throw new NotSupportedException(
                    $"Native MIL Viewport3D {parameterName} contains a non-finite or out-of-range value.");
            }
            return new Vector3(x, y, z);
        }

        private static Vector3 NormalizeFiniteOrZero(
            Vector3 value,
            string parameterName)
        {
            float lengthSquared = value.LengthSquared();
            if (!float.IsFinite(lengthSquared))
            {
                throw new NotSupportedException(
                    $"Native MIL Viewport3D {parameterName} contains a normal whose length is out of range.");
            }
            if (!(lengthSquared > 0.0f))
            {
                return Vector3.Zero;
            }

            // Vector3 division is hardware-intrinsic accelerated by the .NET
            // runtime and retains XMVector3Normalize's exact zero handling.
            return value / MathF.Sqrt(lengthSquared);
        }

        private static Vector2 ToFiniteVector2(
            PortablePoint value,
            string parameterName)
        {
            if (!TryToFiniteFloat(value.X, out float x) ||
                !TryToFiniteFloat(value.Y, out float y))
            {
                throw new NotSupportedException(
                    $"Native MIL Viewport3D {parameterName} contains a non-finite or out-of-range value.");
            }
            return new Vector2(x, y);
        }

        private static Vector3 ToFiniteVector3(
            PortableColor4 value,
            string parameterName)
        {
            if (!TryToFiniteFloat(value.R, out float r) ||
                !TryToFiniteFloat(value.G, out float g) ||
                !TryToFiniteFloat(value.B, out float b))
            {
                throw new NotSupportedException(
                    $"Native MIL Viewport3D {parameterName} contains a non-finite or out-of-range value.");
            }
            return new Vector3(r, g, b);
        }

        private static Vector4 ToFiniteVector4(
            PortableColor4 value,
            string parameterName)
        {
            Vector3 rgb = ToFiniteVector3(value, parameterName);
            if (!TryToFiniteFloat(value.A, out float alpha))
            {
                throw new NotSupportedException(
                    $"Native MIL Viewport3D {parameterName} contains a non-finite or out-of-range alpha.");
            }
            return new Vector4(rgb, alpha);
        }

        private static Matrix4x4 ToFiniteMatrix(
            PortableMatrix4x4 value,
            string parameterName)
        {
            var matrix = new Matrix4x4(
                ToFiniteFloat(value.M11, parameterName),
                ToFiniteFloat(value.M12, parameterName),
                ToFiniteFloat(value.M13, parameterName),
                ToFiniteFloat(value.M14, parameterName),
                ToFiniteFloat(value.M21, parameterName),
                ToFiniteFloat(value.M22, parameterName),
                ToFiniteFloat(value.M23, parameterName),
                ToFiniteFloat(value.M24, parameterName),
                ToFiniteFloat(value.M31, parameterName),
                ToFiniteFloat(value.M32, parameterName),
                ToFiniteFloat(value.M33, parameterName),
                ToFiniteFloat(value.M34, parameterName),
                ToFiniteFloat(value.M41, parameterName),
                ToFiniteFloat(value.M42, parameterName),
                ToFiniteFloat(value.M43, parameterName),
                ToFiniteFloat(value.M44, parameterName));
            return matrix;
        }

        private static float ToFiniteFloat(
            double value,
            string parameterName)
        {
            if (!TryToFiniteFloat(value, out float result))
            {
                throw new NotSupportedException(
                    $"Native MIL Viewport3D {parameterName} contains a non-finite or out-of-range matrix value.");
            }
            return result;
        }

        private static NativeFloat4 ToNativeFloat4(
            Vector3 value,
            float w) => new()
            {
                X = value.X,
                Y = value.Y,
                Z = value.Z,
                W = w
            };

        private static bool TryToFiniteFloat(
            double value,
            out float result)
        {
            result = (float)value;
            return double.IsFinite(value) && float.IsFinite(result);
        }

        private static bool TryToFinitePositiveFloat(
            double value,
            out float result) =>
            TryToFiniteFloat(value, out result) && result > 0.0f;

        private static bool TryToFiniteNonNegativeFloat(
            double value,
            out float result) =>
            TryToFiniteFloat(value, out result) && result >= 0.0f;

        private static bool TryToFiniteUnitFloat(
            double value,
            out float result) =>
            TryToFiniteFloat(value, out result) &&
            result >= 0.0f && result <= 1.0f;

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.X) && float.IsFinite(value.Y) &&
            float.IsFinite(value.Z);

        private static bool IsFinite(Vector2 value) =>
            float.IsFinite(value.X) && float.IsFinite(value.Y);

        private static bool IsFinite(Vector4 value) =>
            float.IsFinite(value.X) && float.IsFinite(value.Y) &&
            float.IsFinite(value.Z) && float.IsFinite(value.W);

        private static bool IsFinite(Matrix4x4 value) =>
            float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
            float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
            float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
            float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
            float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
            float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
            float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
            float.IsFinite(value.M43) && float.IsFinite(value.M44);

        private static void RejectUnsupportedViewport3DState(
            PortableVisualState state)
        {
            if (state.HasOpacityMask || state.HasEffect ||
                state.HasCacheMode || state.HasSnappingGuidelinesX ||
                state.HasSnappingGuidelinesY)
            {
                throw new NotSupportedException(
                    "Native MIL Viewport3D currently supports retained offset, axis-preserving transform, opacity, and exact rectangle/scroll clips; mask, effect, cache, and guideline scopes fail closed.");
            }
        }

        private static void RejectUnsupportedState(PortableVisualState state)
        {
            if (state.HasBitmapEffect || state.HasBitmapEffectInput)
            {
                throw new NotSupportedException(
                    "The portable visual contains state not implemented by the native MIL slice.");
            }
        }

        private static InvalidOperationException MissingContract(string contract)
        {
            return new InvalidOperationException(
                $"The supplied source does not publish {contract} data.");
        }
    }
}

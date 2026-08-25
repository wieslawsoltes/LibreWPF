using System.Buffers.Binary;
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

public sealed record WpfNativeMilBatch(
    byte[] Bytes,
    uint TargetHandle,
    IReadOnlyList<WpfNativeMilBitmapSource>? BitmapSources = null,
    IReadOnlyList<WpfNativeMilGlyphRunFont>? GlyphRunFonts = null,
    IReadOnlyList<WpfNativeMilDrawingImageBounds>? DrawingImageBounds = null);

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
            context.DrawingImageBounds.ToArray());
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

            uint visualHandle = NextHandle();
            _visualHandles.Add(visual, visualHandle);
            Batch.CreateResource(visualHandle, NativeMilResourceType.Visual);
            Batch.CreateVisual(visualHandle);
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
            if (state.HasClip)
            {
                if (state.Clip is null)
                {
                    throw MissingContract(
                        nameof(IPortablePrimitiveGeometrySource));
                }
                Batch.SetVisualClip(
                    visualHandle, ResolveGeometry(state.Clip));
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
                    ResolveSolidOpacityMask(state.OpacityMask));
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
                if (state.SnappingGuidelinesX.Length > 1 ||
                    state.SnappingGuidelinesY.Length > 1)
                {
                    throw new NotSupportedException(
                        "Multiple WPF Visual guidelines per axis require native piecewise geometry deformation.");
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

            if (visual is IPortableDrawingContentSource contentSource &&
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
                    case WpfMilCommandId.PushOpacity:
                        if (recordSize != 16)
                        {
                            throw new InvalidOperationException(
                                "The portable WPF opacity-scope record has an invalid size.");
                        }
                        destination.PushOpacity(ReadDouble(payload, 0));
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
            uint opacityMaskHandle = state.HasOpacityMask
                ? ResolveSolidOpacityMask(state.OpacityMask!)
                : 0;
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
            return handle;
        }

        private uint ResolveSolidOpacityMask(object resource)
        {
            if (resource is not IPortableBrushSource source ||
                !source.TryGetPortableBrush(out PortableBrush brush))
            {
                throw MissingContract(nameof(IPortableBrushSource));
            }
            if (brush.Kind != PortableBrushKind.SolidColor)
            {
                throw new NotSupportedException(
                    "Only static solid opacity masks are implemented by the native MIL slice.");
            }
            if (_brushHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            uint handle = AddPortableBrush(brush);
            _brushHandles.Add(resource, handle);
            return handle;
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
            if (guidelines.IsDynamic)
            {
                throw new NotSupportedException(
                    "Dynamic WPF guideline pairs are not implemented by the native MIL slice.");
            }
            if (guidelines.GuidelinesX.Length > 1 ||
                guidelines.GuidelinesY.Length > 1)
            {
                throw new NotSupportedException(
                    "Multiple WPF guidelines per axis require native piecewise geometry deformation.");
            }
            uint handle = NextHandle();
            _guidelineSetHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.GuidelineSet);
            Batch.SetGuidelineSet(
                handle,
                false,
                guidelines.GuidelinesX,
                guidelines.GuidelinesY);
            return handle;
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
                    if (effect.BlurKernel != PortableBlurKernel.Gaussian)
                    {
                        throw new NotSupportedException(
                            "WPF Box BlurEffect requires a native box-filter effect node.");
                    }
                    Batch.CreateResource(
                        handle, NativeMilResourceType.BlurEffect);
                    Batch.SetBlurEffect(
                        handle, effect.Radius, renderingBias);
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

        private static void RejectUnsupportedState(PortableVisualState state)
        {
            if (state.HasBitmapEffect || state.HasBitmapEffectInput)
            {
                throw new NotSupportedException(
                    "The portable visual contains state not implemented by the native MIL slice.");
            }
            if (state.HasEffect &&
                (state.HasClip || state.HasScrollableAreaClip ||
                 state.HasOpacityMask ||
                 (state.HasOpacity && state.Opacity != 1.0)))
            {
                throw new NotSupportedException(
                    "Native WPF visual effects currently require an unclipped, fully opaque visual while separate source and composite effect clips are being added.");
            }
        }

        private static InvalidOperationException MissingContract(string contract)
        {
            return new InvalidOperationException(
                $"The supplied source does not publish {contract} data.");
        }
    }
}

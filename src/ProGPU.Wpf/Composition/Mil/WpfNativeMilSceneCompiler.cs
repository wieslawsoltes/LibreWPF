using System.Buffers.Binary;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed record WpfNativeMilBatch(byte[] Bytes, uint TargetHandle);

public sealed record WpfNativeMilCompilation(
    NativeMilCompiledScene Scene,
    NativeMilBatchMetrics BatchMetrics);

/// <summary>
/// Compiles the typed portable state published by source-built LibreWPF
/// visuals into canonical MIL and then into ProGPU's native semantic scene.
/// </summary>
/// <remarks>
/// This initial fail-closed slice supports retained offsets/opacity plus nested
/// opacity scopes and solid-brush rectangle render-data. The existing managed
/// portable renderer remains independent and is not replaced or selected
/// implicitly.
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
        return new WpfNativeMilBatch(context.Batch.ToArray(), targetHandle);
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
        private uint _nextHandle = 1;

        internal NativeMilBatchBuilder Batch { get; } = new();

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
            if (state.HasOffset)
            {
                Batch.SetVisualOffset(
                    visualHandle, state.Offset.X, state.Offset.Y);
            }
            if (state.HasOpacity)
            {
                Batch.SetVisualOpacity(visualHandle, state.Opacity);
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
                        if (penToken != 0)
                        {
                            throw new NotSupportedException(
                                "Native MIL rectangle pens are not implemented yet.");
                        }
                        uint brushHandle = ResolveSolidBrush(
                            snapshot.DependentResources, brushToken);
                        destination.DrawRectangle(
                            ReadDouble(payload, 0),
                            ReadDouble(payload, 8),
                            ReadDouble(payload, 16),
                            ReadDouble(payload, 24),
                            brushHandle);
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

        private uint ResolveSolidBrush(
            IReadOnlyList<object?> resources,
            uint token)
        {
            if (token == 0 || token > resources.Count ||
                resources[checked((int)token - 1)] is not object resource)
            {
                throw new InvalidOperationException(
                    $"Portable brush token {token} is unavailable.");
            }
            if (_brushHandles.TryGetValue(resource, out uint existing))
            {
                return existing;
            }
            if (resource is not IPortableBrushSource source ||
                !source.TryGetPortableBrush(out PortableBrush brush))
            {
                throw MissingContract(nameof(IPortableBrushSource));
            }
            if (brush.Kind != PortableBrushKind.SolidColor ||
                brush.HasTransform || brush.HasRelativeTransform)
            {
                throw new NotSupportedException(
                    "Only untransformed portable solid brushes are implemented by the native MIL slice.");
            }
            uint handle = NextHandle();
            _brushHandles.Add(resource, handle);
            Batch.CreateResource(handle, NativeMilResourceType.SolidColorBrush);
            Batch.SetSolidColorBrush(
                handle,
                ToLinearColor(brush.Color),
                brush.Opacity);
            return handle;
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
            if (state.HasTransform || state.HasClip ||
                state.HasScrollableAreaClip || state.HasOpacityMask ||
                state.HasEffect || state.HasBitmapEffect ||
                state.HasBitmapEffectInput || state.HasCacheMode ||
                state.HasBitmapScalingMode || state.HasEdgeMode ||
                state.HasClearTypeHint || state.HasTextRenderingMode ||
                state.HasTextHintingMode || state.HasSnappingGuidelinesX ||
                state.HasSnappingGuidelinesY)
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

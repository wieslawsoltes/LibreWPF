using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompilerTests
{
    [Theory]
    [InlineData(PortableShaderSamplingMode.Auto)]
    [InlineData(PortableShaderSamplingMode.NearestNeighbor)]
    [InlineData(PortableShaderSamplingMode.Bilinear)]
    public void VisualShaderSamplerRetainsOriginalGraphMappingAndBounds(PortableShaderSamplingMode sampling)
    {
        var child = new FakeVisual(null);
        var visual = new SamplerVisual(null, child) { Bounds = new(10, 20, 8, 6) };
        var brush = new VisualSamplerBrush(visual);
        var effect = VisualSamplerEffect(brush, sampling);
        using var batch = ShaderBatch(effect);
        int packet = FindCommand(batch.Bytes, 0x83);
        uint visualHandle = ReadUInt32(batch.Bytes, packet + 148);
        Assert.NotEqual(0U, visualHandle);
        Assert.True(batch.VisualOwners.TryGetOwner(unchecked((int)visualHandle), out object? source));
        Assert.Same(visual, source);
        Assert.Equal(3, batch.VisualOwners.Count); // receiver, source and actual child
        Assert.Equal(new NativeMilRect(10, 20, 8, 6),
            Assert.Single(batch.VisualCacheBounds!, value => value.Handle == visualHandle).Bounds);
        Assert.Equal(0.5, ReadDouble(batch.Bytes, packet + 12));
        Assert.Equal(0.5, ReadDouble(batch.Bytes, packet + 36));
        Assert.NotEqual(0U, ReadUInt32(batch.Bytes, packet + 104));
        Assert.Empty(batch.BitmapSources!);
        Assert.Empty(batch.EmptyVisualBrushSources.ToArray());
        Assert.Equal(1, brush.Reads);
        byte[] original = (byte[])batch.Bytes.Clone();
        brush.Opacity = 0.25;
        using var updated = ShaderBatch(effect);
        Assert.Equal([0x83], ReadCommands(WpfNativeMilCompilationSession.CreateDelta(batch, updated).Bytes));
        Assert.Equal(original, batch.Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisualShaderSamplerSharesOriginalOrdinarySourceRegardlessOfOrder(bool sourceFirst)
    {
        var visual = new SamplerVisual(null);
        var brush = new VisualSamplerBrush(visual);
        var ordinary = new FakeVisual(new FakeRenderData(CreateRectangleRecord(1, 0), [brush]));
        var shader = ShaderVisual(VisualSamplerEffect(brush));
        using var batch = new WpfNativeMilSceneCompiler().BuildBatch(new FakeVisual(null, null,
            sourceFirst ? [visual, ordinary, shader] : [shader, ordinary, visual]), 64, 64);
        Assert.Equal(1, ReadCommands(batch.Bytes).Count(value => value == 0x83));
        Assert.Equal(4, batch.VisualOwners.Count);
        Assert.Equal(2, batch.VisualCacheBounds!.Count); // source and effect receiver
    }

    [Fact]
    public void VisualShaderSamplerNullAndEmptyRemainDifferentOwnedStates()
    {
        var brush = new VisualSamplerBrush(null);
        var effect = VisualSamplerEffect(brush);
        using var disconnected = ShaderBatch(effect);
        Assert.Equal(0U, ReadUInt32(disconnected.Bytes, FindCommand(disconnected.Bytes, 0x83) + 148));
        Assert.Equal(1, disconnected.VisualOwners.Count);
        Assert.Empty(disconnected.EmptyVisualBrushSources.ToArray());

        var visual = new SamplerVisual(null) { Bounds = default, Empty = true };
        brush.Visual = visual;
        using var empty = ShaderBatch(effect);
        uint handle = Assert.Single(empty.EmptyVisualBrushSources.ToArray());
        Assert.Equal(handle, ReadUInt32(empty.Bytes, FindCommand(empty.Bytes, 0x83) + 148));
        Assert.True(empty.VisualOwners.TryGetOwner(unchecked((int)handle), out object? owner));
        Assert.Same(visual, owner);
        Assert.DoesNotContain(empty.VisualCacheBounds!, bounds => bounds.Handle == handle);
        using var retained = empty with { };
        Assert.Equal(empty.EmptyVisualBrushSources.ToArray(), retained.EmptyVisualBrushSources.ToArray());

        visual.Empty = false;
        visual.Bounds = new(10, 20, 8, 6);
        using var restored = ShaderBatch(effect);
        Assert.Empty(restored.EmptyVisualBrushSources.ToArray());
        Assert.False(WpfNativeMilCompilationSession.HasStableSidebandTopology(empty, restored));
        Assert.Single(empty.EmptyVisualBrushSources.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisualShaderSamplerRejectsCyclesEvenForEmptySources(bool empty)
    {
        object[] children = new object[1];
        var visual = new SamplerVisual(null, children) { Empty = empty };
        children[0] = visual;
        Assert.Throws<InvalidOperationException>(() => ShaderBatch(VisualSamplerEffect(new VisualSamplerBrush(visual))));
    }

    [Theory]
    [InlineData(0)] // missing typed visual contracts
    [InlineData(1)] // missing typed bounds
    [InlineData(2)] // genuine child cannot have two source parents
    public void VisualShaderSamplerRejectsUnownedGraphsAtomically(int invalid)
    {
        var brush = new VisualSamplerBrush(new SamplerVisual(null));
        var effect = VisualSamplerEffect(brush);
        using var original = ShaderBatch(effect);
        byte[] bytes = (byte[])original.Bytes.Clone();
        object child = new FakeVisual(null);
        brush.Visual = invalid switch
        {
            0 => new object(),
            1 => new FakeVisualWithoutBounds(new()),
            _ => new SamplerVisual(null, child, child)
        };
        if (invalid == 1)
            Assert.Throws<NotSupportedException>(() => ShaderBatch(effect));
        else
            Assert.Throws<InvalidOperationException>(() => ShaderBatch(effect));
        Assert.Equal(bytes, original.Bytes);
    }

    [Fact]
    public void VisualShaderSamplerRejectsReplacedReferenceInOneBatch()
    {
        var brush = new VisualSamplerBrush(new SamplerVisual(null));
        var first = ShaderVisual(VisualSamplerEffect(brush));
        var later = VisualSamplerEffect(brush);
        later.AfterCapture = () => brush.Visual = new SamplerVisual(null);
        Assert.Contains("changed during native batch capture", Assert.Throws<InvalidOperationException>(() =>
            new WpfNativeMilSceneCompiler().BuildBatch(new FakeVisual(null, null, first, ShaderVisual(later)), 64, 64)).Message);
    }

    [Theory]
    [InlineData(PortableTileBrushKind.Image)]
    [InlineData(PortableTileBrushKind.Drawing)]
    public void GenericBrushSamplerDoesNotAdmitOtherTileFamilies(PortableTileBrushKind kind)
    {
        var tile = new PortableTileBrush(kind, new object(), 1, new(0, 0, 1, 1), new(0, 0, 1, 1),
            PortableBrushMappingMode.RelativeToBoundingBox, PortableBrushMappingMode.RelativeToBoundingBox,
            PortableTileMode.None, PortableStretch.Fill, PortableAlignmentX.Center, PortableAlignmentY.Center,
            false, default, false, default);
        Assert.Throws<NotSupportedException>(() => ShaderBatch(VisualSamplerEffect(new FakeTileBrush(tile))));
    }

    [Fact]
    public void VisualShaderSamplerDependenciesFollowRetainedVisualAndChild()
    {
        var child = new FakeVisual(null);
        var visual = new SamplerVisual(null, child);
        var brush = new VisualSamplerBrush(visual);
        var effect = VisualSamplerEffect(brush);
        using var tracker = new WpfVisualInvalidationTracker();
        tracker.Attach(ShaderVisual(effect));
        Assert.True(tracker.ConsumeDirty());
        visual.Invalidate();
        Assert.Contains(visual, tracker.DirtySources);
        var dependencies = WpfVisualInvalidationTracker.EnumerateTrackedDependencies(effect);
        Assert.Contains(brush, dependencies);
        Assert.Contains(visual, dependencies);
        Assert.Contains(child, dependencies);
    }

    [Theory]
    [InlineData(NativeMilBackend.WgpuNative)]
    [InlineData(NativeMilBackend.Dawn)]
    public void VisualShaderSessionAppliesKnownEmptySourceAndRetainsLastGenerationAfterRejection(NativeMilBackend backend)
    {
        var content = new FakeRenderData(CreateRectangleRecord(1, 0), [new FakeBrush(new(255, 255, 0, 0))]);
        var visual = new SamplerVisual(content);
        var brush = new VisualSamplerBrush(visual);
        var effect = VisualSamplerEffect(brush);
        var receiver = ShaderVisual(effect);
        using var session = new WpfNativeMilCompilationSession(backend);
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        var live = session.CompileFrame(11941, 1, 0, 1);
        byte[] original = live.Scene.Stream.ToArray();

        visual.Content = null;
        visual.Empty = true;
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        var empty = session.CompileFrame(11941, 2, 0, 2);
        byte[] retained = empty.Scene.Stream.ToArray();
        Assert.False(session.Update(receiver, 64, 64).RecreatedChannel);
        brush.Visual = new FakeVisualWithoutBounds(new());
        Assert.Throws<NotSupportedException>(() => session.Update(receiver, 64, 64));
        Assert.True(session.IsInitialized);
        Assert.Equal(retained, session.CompileFrame(11941, 2, 0, 2).Scene.Stream.ToArray());

        brush.Visual = visual;
        visual.Content = content;
        visual.Empty = false;
        Assert.True(session.Update(receiver, 64, 64).RecreatedChannel);
        Assert.Equal(original, session.CompileFrame(11941, 1, 0, 1).Scene.Stream.ToArray());
        Assert.Equal(original, live.Scene.Stream.ToArray());
        Assert.Equal(retained, empty.Scene.Stream.ToArray());
    }

    private static ShaderEffectSource VisualSamplerEffect(object brush,
        PortableShaderSamplingMode mode = PortableShaderSamplingMode.Auto) => new(new())
        { Samplers = [new(0, brush, mode)] };

    private sealed class VisualSamplerBrush(object? visual) : IPortableTileBrushSource
    {
        internal object? Visual = visual;
        internal double Opacity = 0.5;
        internal int Reads;
        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            ++Reads;
            brush = PortableTileBrush.Visual(Visual, Opacity, new(0, 0, 0.5, 1), new(0, 0, 1, 1),
                PortableBrushMappingMode.RelativeToBoundingBox, PortableBrushMappingMode.RelativeToBoundingBox,
                PortableTileMode.Tile, PortableStretch.Fill, PortableAlignmentX.Center, PortableAlignmentY.Center,
                true, new(1, 0, 0, 1, 7, 8), false, default);
            return true;
        }
    }

    private sealed class SamplerVisual(object? content, params object[] children)
        : FakeVisual(content, null, children), IPortableVisualBoundsSource, IPortableDrawingContentSource,
          IPortableInvalidationSource
    {
        internal object? Content = content;
        internal PortableRect Bounds = new(1, 2, 30, 20);
        internal bool Empty;
        private event EventHandler? Invalidated;
        internal void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);
        bool IPortableDrawingContentSource.TryGetPortableDrawingContent(out object? value)
        { value = Content; return true; }
        public bool TrySubscribeInvalidated(EventHandler handler, out IDisposable subscription)
        { Invalidated += handler; subscription = new PortableInvalidationSubscription(() => Invalidated -= handler); return true; }
        bool IPortableVisualBoundsSource.TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        {
            bounds = new() { HasDescendantBounds = true,
                DescendantBounds = Empty ? PortableRect.Empty : Bounds };
            return true;
        }
    }
}

using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompilerTests
{
    private sealed class OriginalTransformSource(PortableTransform value) : IPortableTransformSource, IPortableTransformMatrixSource
    {
        public PortableTransform Value = value;
        public int Calls;
        public bool Reject;
        public bool TryGetPortableTransform(out PortableTransform transform)
        { ++Calls; transform = Value; return !Reject; }
        public bool TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix)
            => throw new InvalidOperationException("A typed resource must not be flattened.");
    }

    private static WpfNativeMilBatch TransformBatch(object transform) => new WpfNativeMilSceneCompiler().BuildBatch(
        new FakeVisual(null, new PortableVisualState { HasTransform = true, Transform = transform }), 64, 64);

    [Fact]
    public void OriginalTransformGroupRetainsSharedChildOrderAndSingleCapture()
    {
        var translate = new OriginalTransformSource(new PortableTranslateTransform(double.BitIncrement(2), -0.0));
        var rotate = new OriginalTransformSource(new PortableRotateTransform(360000000.25, 3, 4));
        var group = new OriginalTransformSource(new PortableTransformGroup([translate, rotate, translate]));
        using var batch = TransformBatch(group);
        Assert.Equal(1, translate.Calls); Assert.Equal(1, rotate.Calls); Assert.Equal(1, group.Calls);
        Assert.DoesNotContain(0x77, ReadCommands(batch.Bytes));
        int t = FindCommand(batch.Bytes, 0x73), r = FindCommand(batch.Bytes, 0x76), g = FindCommand(batch.Bytes, 0x72);
        Assert.Equal(BitConverter.DoubleToInt64Bits(double.BitIncrement(2)), BitConverter.DoubleToInt64Bits(ReadDouble(batch.Bytes, t + 12)));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(ReadDouble(batch.Bytes, t + 20)));
        Assert.Equal(360000000.25, ReadDouble(batch.Bytes, r + 12));
        Assert.Equal(12U, ReadUInt32(batch.Bytes, g + 12));
        Assert.Equal(ReadUInt32(batch.Bytes, t + 8), ReadUInt32(batch.Bytes, g + 16));
        Assert.Equal(ReadUInt32(batch.Bytes, r + 8), ReadUInt32(batch.Bytes, g + 20));
        Assert.Equal(ReadUInt32(batch.Bytes, t + 8), ReadUInt32(batch.Bytes, g + 24));
    }

    [Fact]
    public void TypedChildMutationUsesExistingDeltaAndOldBytesRemainOwned()
    {
        var scale = new OriginalTransformSource(new PortableScaleTransform(1.25, 1.5, 3, 4));
        var skew = new OriginalTransformSource(new PortableSkewTransform(45, -30, 5, 6));
        var group = new OriginalTransformSource(new PortableTransformGroup([scale, skew]));
        using var first = TransformBatch(group); byte[] saved = (byte[])first.Bytes.Clone();
        scale.Value = new PortableScaleTransform(double.BitIncrement(1.25), 1.5, 3, 4);
        using var second = TransformBatch(group);
        var delta = WpfNativeMilCompilationSession.CreateDelta(first, second);
        Assert.False(delta.RequiresRebuild); Assert.Equal([0x74], ReadCommands(delta.Bytes));
        Assert.Equal(saved, first.Bytes);
        group.Value = new PortableTransformGroup([skew, scale]);
        using var third = TransformBatch(group);
        Assert.True(WpfNativeMilCompilationSession.CreateDelta(second, third).RequiresRebuild);
    }

    [Fact]
    public void TypedCycleAndAdvertisedFailureNeverFallBackToMatrixOrPoisonNextBatch()
    {
        var leaf = new OriginalTransformSource(new PortableTranslateTransform(1, 2));
        var group = new OriginalTransformSource(new PortableTransformGroup([leaf]));
        leaf.Value = new PortableTransformGroup([group]);
        Assert.Contains("cycle", Assert.Throws<InvalidOperationException>(() => TransformBatch(group)).Message);
        leaf.Value = new PortableTranslateTransform(1, 2); leaf.Reject = true;
        Assert.Contains(nameof(IPortableTransformSource), Assert.Throws<InvalidOperationException>(() => TransformBatch(group)).Message);
        leaf.Reject = false;
        using var valid = TransformBatch(group);
        Assert.Contains(0x73, ReadCommands(valid.Bytes));
    }

    [Fact]
    public void TypedLateInvalidPrimitiveAndExcessDepthRejectWholeCapture()
    {
        var valid = new OriginalTransformSource(new PortableTranslateTransform(1, 2));
        var invalid = new OriginalTransformSource(new PortableSkewTransform(3, double.NaN, 4, 5));
        var group = new OriginalTransformSource(new PortableTransformGroup([valid, invalid]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TransformBatch(group));
        object deep = valid;
        for (int i = 0; i < 256; ++i) deep = new OriginalTransformSource(new PortableTransformGroup([deep]));
        Assert.Contains("depth", Assert.Throws<InvalidOperationException>(() => TransformBatch(deep)).Message);
    }

    [Fact]
    public void EmptyOriginalGroupIsNotElidedIntoAMatrix()
    {
        using var batch = TransformBatch(new OriginalTransformSource(new PortableTransformGroup([])));
        int group = FindCommand(batch.Bytes, 0x72);
        Assert.Equal(16, ReadInt32(batch.Bytes, group)); Assert.Equal(0U, ReadUInt32(batch.Bytes, group + 12));
        Assert.DoesNotContain(0x77, ReadCommands(batch.Bytes));
    }
}

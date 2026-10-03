using System;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests;

[Collection(PortableRenderDataSinkProviderCollection.Name)]
public sealed class ProGpuShaderEffectTargetFrameTests
{
    [Fact]
    public void HostSuppliesActualTargetGeometryBeforeSourceReplaySelection()
    {
        string host = Regex.Replace(File.ReadAllText(FindHostSource()), @"\s+", string.Empty);
        const string capture = "varshaderTargetFrame=newWpfShaderEffectTargetFrame(logicalWidth,logicalHeight," +
            "pixelWidth,pixelHeight,newProGpuRenderTargetViewport(viewportX,viewportY,viewportWidth,viewportHeight));";
        int captureAt = host.IndexOf(capture, StringComparison.Ordinal);
        Assert.True(captureAt >= 0, "The source frame must retain the actual logical, target and viewport inputs.");
        int adapterAt = host.IndexOf("_target.CreateFrameImageSourceAdapter(WpfImageSourceAdapter,(float)dpiScale,shaderTargetFrame)",
            captureAt, StringComparison.Ordinal);
        Assert.True(adapterAt > captureAt);
        int replaySelectionAt = host.IndexOf("_target.ShouldReplayVisualSubtree(wpfRootVisual)", captureAt, StringComparison.Ordinal);
        Assert.True(replaySelectionAt > adapterAt, "A frame change must invalidate replay before deciding to skip it.");
        Assert.Contains("_target.Render(logicalWidth,logicalHeight,pixelWidth,pixelHeight,newProGpuRenderTargetViewport(" +
            "viewportX,viewportY,ResolveGeometryViewportDimension(viewportWidth,pixelWidth)," +
            "ResolveGeometryViewportDimension(viewportHeight,pixelHeight)),(float)dpiScale,targetView)", host,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ActualTargetReusesExactFrameAndInvalidatesOnSameDpiViewportChange()
    {
        // Real device-owning factory, authored for the eventual platform run;
        // this is not an uninitialized stand-in for target/cache ownership.
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        var root = new object();
        target.WpfInvalidationTracker.Attach(root);
        WpfShaderEffectTargetFrame initial = AsymmetricFrame();
        var first = Assert.IsType<WpfShaderEffectSamplerImageSourceAdapter>(
            target.CreateFrameImageSourceAdapter(null, 1.25f, initial));
        target.WpfInvalidationTracker.ConsumeDirty();
        Assert.False(target.ShouldReplayVisualSubtree(root));
        Assert.Same(first, target.CreateFrameImageSourceAdapter(null, 1.25f, initial));
        Assert.Same(first, target.CreateFrameImageSourceAdapter(first));
        Assert.False(target.ShouldReplayVisualSubtree(root));
        Assert.Equal(initial, first.TargetFrame);

        // Origin alone leaves pixels/unit unchanged but changes the real target
        // frame. It must not be mistaken for a reusable retained source frame.
        var moved = initial with { Viewport = new Scene.RenderTargetViewport(12, 16, 192, 128) };
        var second = Assert.IsType<WpfShaderEffectSamplerImageSourceAdapter>(
            target.CreateFrameImageSourceAdapter(null, 1.25f, moved));
        Assert.NotSame(first, second);
        Assert.Equal(initial, first.TargetFrame);
        Assert.Equal(moved, second.TargetFrame);
        Assert.True(target.ShouldReplayVisualSubtree(root));
        Assert.True(target.LastRetainedBranchInvalidationUsedFallback);
        Assert.False(target.TryPrepareDirtyRetainedVisualBranchReplayTargets(root, second, out var targets));
        Assert.Empty(targets);

        target.WpfInvalidationTracker.ConsumeDirty();
        Assert.Same(second, target.CreateFrameImageSourceAdapter(null, 1.25f, moved));
        Assert.False(target.ShouldReplayVisualSubtree(root));
        var resized = moved with { Viewport = new Scene.RenderTargetViewport(12, 16, 128, 128) };
        var third = target.CreateFrameImageSourceAdapter(null, 1.25f, resized);
        Assert.NotSame(second, third);
        Assert.True(target.ShouldReplayVisualSubtree(root));
        Assert.True(target.LastRetainedBranchInvalidationUsedFallback);
    }

    [Fact]
    public void SourceAdapterAndResourceResolverForwardAuthoritativeFrameWithoutChangingDescriptor()
    {
        var inner = new MetadataAdapter();
        WpfShaderEffectTargetFrame target = AsymmetricFrame();
        var adapter = new WpfShaderEffectSamplerImageSourceAdapter(inner, UnusedCache(), 1.25f, target);
        var resolver = new WpfResourceResolver(imageSourceAdapter: adapter);
        object owner = new(), brush = new();
        var source = OriginalSource();
        var request = WpfShaderEffectSamplerFrame.FromSource(owner, source) with
        {
            DpiScale = 3,
            TargetFrame = new(64, 64, 64, 64, Scene.RenderTargetViewport.Full(64, 64))
        };
        Assert.True(resolver.TryAdaptSourceShaderEffectSamplerBrush(brush, 3, Scene.TextureSamplingMode.Nearest,
            request, out var sampler));
        Assert.Equal(target, inner.Request.TargetFrame);
        Assert.Equal(1.25f, inner.Request.DpiScale);
        Assert.Equal(3f, request.DpiScale);
        Assert.NotEqual(request.TargetFrame, inner.Request.TargetFrame);
        Assert.Same(owner, inner.Request.Owner);
        Assert.Same(brush, inner.Brush);
        Assert.Equal(3, inner.Register);
        Assert.Equal(Scene.TextureSamplingMode.Nearest, inner.Mode);
        AssertOriginalBits(source, inner.Request.SourceCapture!.Value);
        Assert.Equal(default, inner.Request.ContentBounds);
        Assert.Equal(0f, inner.Request.Padding);
        Assert.Equal(1, inner.SourceCalls);
        Assert.Equal(0, inner.LegacyCalls);
        sampler.Dispose();
    }

    [Fact]
    public void LegacyAdapterRejectsSourceRequestEvenWithCallerSuppliedTargetFrameBeforeCallbacks()
    {
        var trap = new MetadataAdapter { ThrowOnSource = true };
        var adapter = new WpfShaderEffectSamplerImageSourceAdapter(trap, UnusedCache(), 1.25f);
        var request = WpfShaderEffectSamplerFrame.FromSource(new object(), OriginalSource()) with
        {
            TargetFrame = AsymmetricFrame()
        };
        Assert.False(adapter.TryAdaptSourceShaderEffectSamplerBrush(new object(), 2,
            Scene.TextureSamplingMode.Linear, request, out var sampler));
        Assert.Null(sampler);
        Assert.Null(adapter.TargetFrame);
        Assert.Equal(0, trap.SourceCalls);
        Assert.Equal(0, trap.LegacyCalls);
    }

    [Fact]
    public void InvalidTargetMappingRejectsConstructionBeforeAnyAdapterCallback()
    {
        var trap = new MetadataAdapter { ThrowOnSource = true };
        var cache = UnusedCache();
        foreach (float nonfinite in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            var invalid = AsymmetricFrame() with { Viewport = new Scene.RenderTargetViewport(8, 16, nonfinite, 128) };
            Assert.False(invalid.TryGetPixelsPerUnit(out var mapping));
            Assert.Equal(Vector2.Zero, mapping);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WpfShaderEffectSamplerImageSourceAdapter(trap, cache, 1.25f, invalid));
        }
        Assert.Equal(0, trap.SourceCalls);
        Assert.Equal(0, trap.LegacyCalls);
    }

    [Fact]
    public void AsymmetricMappingMatchesSharedProjectionResolverNotSemanticDpi()
    {
        var frame = AsymmetricFrame();
        Assert.True(frame.TryGetPixelsPerUnit(out var actual));
        Assert.True(Scene.EffectCaptureFrame.TryResolveSourcePixelsPerUnit(frame.LogicalWidth, frame.LogicalHeight,
            frame.TargetWidth, frame.TargetHeight, frame.Viewport, out var shared));
        Assert.Equal(new Vector2(1.5f, 2f), actual);
        Assert.Equal(BitConverter.SingleToInt32Bits(shared.X), BitConverter.SingleToInt32Bits(actual.X));
        Assert.Equal(BitConverter.SingleToInt32Bits(shared.Y), BitConverter.SingleToInt32Bits(actual.Y));
        Assert.NotEqual(new Vector2(1.25f), actual);
        var translatedViewport = frame with { Viewport = new Scene.RenderTargetViewport(12, 32, 192, 128) };
        Assert.True(translatedViewport.TryGetPixelsPerUnit(out var translatedMapping));
        Assert.Equal(actual, translatedMapping);
        Assert.NotEqual(frame, translatedViewport);
    }

    [Fact]
    public void ActualSamplerCacheUsesVectorPhysicalExtentAndClearsItsTargetIdentity()
    {
        using var target = ProGpuWpfCompositionTarget.CreateHeadless();
        using var cache = new WpfShaderEffectSamplerTextureCache(target.Context, target.Compositor,
            target.Viewport3DTextureCache);
        WpfShaderEffectTargetFrame frame = AsymmetricFrame();
        var adapter = new WpfShaderEffectSamplerImageSourceAdapter(null, cache, 1.25f, frame);
        var source = new Scene.ShaderEffectSourceCapture(8.25, 10.125, 7.5, 5.5, .25, .5, .75, 1.25);
        var owner = new object();
        var brush = new EmptyVisualSampler();
        var request = WpfShaderEffectSamplerFrame.FromSource(owner, source);
        Assert.True(adapter.TryAdaptSourceShaderEffectSamplerBrush(brush, 2,
            Scene.TextureSamplingMode.Nearest, request, out var sampler));
        // Literal physical intervals: X=[floor(7.5*1.5),ceil(17*1.5))=[11,26),
        // Y=[floor(9.875*2),ceil(16.125*2))=[19,33). Neither axis uses DPI1.25.
        Assert.NotNull(sampler.Texture);
        Assert.Equal(15u, sampler.Texture.Width);
        Assert.Equal(14u, sampler.Texture.Height);
        Assert.True(cache.HasSourceTargetFrame(frame));
        var different = frame with { Viewport = new Scene.RenderTargetViewport(12, 16, 192, 128) };
        Assert.False(cache.HasSourceTargetFrame(different));
        Assert.Equal(1.25f, adapter.DpiScale);
        sampler.Dispose();
        cache.Clear();
        Assert.True(cache.HasSourceTargetFrame(different));
        GC.KeepAlive(owner);
        GC.KeepAlive(brush);
    }

    private static WpfShaderEffectTargetFrame AsymmetricFrame() =>
        new(128, 64, 256, 256, new Scene.RenderTargetViewport(8, 16, 192, 128));

    private static Scene.ShaderEffectSourceCapture OriginalSource() =>
        new(Math.BitIncrement(8d), Math.BitDecrement(10d), Math.BitIncrement(32d), Math.BitDecrement(24d),
            BitConverter.Int64BitsToDouble(long.MinValue), 0.5, Math.BitIncrement(0.75d), 1.25);

    private static WpfShaderEffectSamplerTextureCache UnusedCache() =>
        // Only metadata-forwarding and rejection paths may use this object.
        // Any fallback capture is a test failure, never fake GPU ownership.
        (WpfShaderEffectSamplerTextureCache)RuntimeHelpers.GetUninitializedObject(typeof(WpfShaderEffectSamplerTextureCache));

    private static void AssertOriginalBits(Scene.ShaderEffectSourceCapture expected, Scene.ShaderEffectSourceCapture actual)
    {
        double[] before = [expected.X, expected.Y, expected.Width, expected.Height,
            expected.PaddingTop, expected.PaddingBottom, expected.PaddingLeft, expected.PaddingRight];
        double[] after = [actual.X, actual.Y, actual.Width, actual.Height,
            actual.PaddingTop, actual.PaddingBottom, actual.PaddingLeft, actual.PaddingRight];
        for (int index = 0; index < before.Length; index++)
            Assert.Equal(BitConverter.DoubleToInt64Bits(before[index]), BitConverter.DoubleToInt64Bits(after[index]));
    }

    private static string FindHostSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs");
            if (File.Exists(Path.Combine(directory.FullName, "eng", "progpu-wpf-sdk-ci.sh")) && File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Could not locate the actual ProGpuWpfWindowHost source from the test output.");
    }

    private sealed class MetadataAdapter : IWpfImageSourceAdapter, IWpfShaderEffectSamplerBrushAdapter
    {
        internal bool ThrowOnSource { get; init; }
        internal int SourceCalls { get; private set; }
        internal int LegacyCalls { get; private set; }
        internal WpfShaderEffectSamplerFrame Request { get; private set; }
        internal object? Brush { get; private set; }
        internal int Register { get; private set; }
        internal Scene.TextureSamplingMode Mode { get; private set; }
        public ImageSource? AdaptImageSource(object? source)
        {
            LegacyCalls++;
            throw new InvalidOperationException("Source target-frame forwarding reached an image-only adapter.");
        }
        public bool TryAdaptSourceShaderEffectSamplerBrush(object? brush, int registerIndex,
            Scene.TextureSamplingMode mode, WpfShaderEffectSamplerFrame frame, out Scene.WpfShaderEffectSampler sampler)
        {
            SourceCalls++;
            if (ThrowOnSource) throw new InvalidOperationException("Invalid source target mapping reached a callback.");
            Request = frame;
            Brush = brush;
            Register = registerIndex;
            Mode = mode;
            // Records forwarding metadata only; no actual capture is claimed.
            sampler = new(registerIndex, null, mode);
            return true;
        }
        public bool TryAdaptShaderEffectSamplerBrush(object? brush, int registerIndex,
            Scene.TextureSamplingMode mode, out Scene.WpfShaderEffectSampler sampler)
        {
            LegacyCalls++;
            throw new InvalidOperationException("Source target-frame forwarding reached the legacy sampler path.");
        }
        public bool TryAdaptShaderEffectSamplerBrush(object? brush, int registerIndex,
            Scene.TextureSamplingMode mode, WpfShaderEffectSamplerFrame frame, out Scene.WpfShaderEffectSampler sampler)
        {
            LegacyCalls++;
            throw new InvalidOperationException("Source target-frame forwarding reached the scalar sampler path.");
        }
    }

    private sealed class EmptyVisualSampler : IPortableTileBrushSource
    {
        public bool TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            brush = PortableTileBrush.Visual(null, 1, new(0, 0, 1, 1), new(0, 0, 1, 1),
                PortableBrushMappingMode.RelativeToBoundingBox, PortableBrushMappingMode.RelativeToBoundingBox,
                PortableTileMode.None, PortableStretch.Fill, PortableAlignmentX.Center, PortableAlignmentY.Center,
                false, PortableMatrix3x2.Identity, false, PortableMatrix3x2.Identity);
            return true;
        }
    }
}

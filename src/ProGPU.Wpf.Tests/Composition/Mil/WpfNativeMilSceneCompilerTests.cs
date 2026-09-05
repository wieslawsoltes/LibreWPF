using System.Buffers.Binary;
using System.Numerics;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Backend;
using ProGPU.Backend.Native;
using ProGPU.Text;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfNativeMilSceneCompilerTests
{
    [Fact]
    public void BuildBatchTranslatesTypedBitmapCache()
    {
        var cache = new FakeBitmapCache(
            new PortableBitmapCache(1.0, false, false));
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasCacheMode = true,
                CacheMode = cache
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Equal(
            [0x07, 0x1a, 0x07, 0x8d, 0x1e,
             0x07, 0x34, 0x36, 0x35],
            ReadCommands(result.Bytes));
        int cacheOffset = FindCommand(result.Bytes, 0x8d);
        uint cacheHandle = ReadUInt32(result.Bytes, cacheOffset + 8);
        Assert.Equal(1.0, ReadDouble(result.Bytes, cacheOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, cacheOffset + 20));
        Assert.Equal(0U, ReadUInt32(result.Bytes, cacheOffset + 24));
        Assert.Equal(0U, ReadUInt32(result.Bytes, cacheOffset + 28));
        int visualOffset = FindCommand(result.Bytes, 0x1e);
        Assert.Equal(1U, ReadUInt32(result.Bytes, visualOffset + 8));
        Assert.Equal(cacheHandle, ReadUInt32(result.Bytes, visualOffset + 12));
        WpfNativeMilVisualCacheBounds bounds = Assert.Single(
            result.VisualCacheBounds!);
        Assert.Equal(1U, bounds.Handle);
        Assert.Equal(new NativeMilRect(1, 2, 30, 20), bounds.Bounds);
    }

    [Fact]
    public void BuildBatchRejectsUntypedBitmapCache()
    {
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasCacheMode = true,
                CacheMode = new object()
            });

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(nameof(IPortableBitmapCacheSource), exception.Message);
    }

    [Fact]
    public void BuildBatchRejectsBitmapCacheWithoutTypedVisualBounds()
    {
        var visual = new FakeVisualWithoutBounds(
            new PortableVisualState
            {
                HasCacheMode = true,
                CacheMode = new FakeBitmapCache(
                    new PortableBitmapCache(1.0, false, false))
            });

        NotSupportedException exception =
            Assert.Throws<NotSupportedException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains("exact typed Visual descendant bounds", exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedVisualClips()
    {
        var clip = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                0,
                0,
                PortableMatrix3x2.Identity));
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasClip = true,
                Clip = clip,
                HasScrollableAreaClip = true,
                ScrollableAreaClip = new PortableRect(4, 5, 30, 24)
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Equal(3U, result.TargetHandle);
        Assert.Equal(
            [0x07, 0x1a, 0x07, 0x79, 0x1f, 0x28,
             0x07, 0x34, 0x36, 0x35],
            ReadCommands(result.Bytes));
        int clipOffset = FindCommand(result.Bytes, 0x1f);
        Assert.Equal(1U, ReadUInt32(result.Bytes, clipOffset + 8));
        Assert.Equal(2U, ReadUInt32(result.Bytes, clipOffset + 12));
        int scrollOffset = FindCommand(result.Bytes, 0x28);
        Assert.Equal(1U, ReadUInt32(result.Bytes, scrollOffset + 8));
        Assert.Equal(4.0, ReadDouble(result.Bytes, scrollOffset + 12));
        Assert.Equal(5.0, ReadDouble(result.Bytes, scrollOffset + 20));
        Assert.Equal(30.0, ReadDouble(result.Bytes, scrollOffset + 28));
        Assert.Equal(24.0, ReadDouble(result.Bytes, scrollOffset + 36));
        Assert.Equal(1U, ReadUInt32(result.Bytes, scrollOffset + 44));
    }

    [Fact]
    public void BuildBatchRejectsUntypedVisualClip()
    {
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasClip = true,
                Clip = new object()
            });

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            nameof(IPortablePrimitiveGeometrySource), exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedSolidVisualOpacityMask()
    {
        var opacityMask = new FakeBrush(
            PortableBrush.SolidColor(
                new PortableColor(128, 255, 255, 255),
                opacity: 0.5));
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasOpacityMask = true,
                OpacityMask = opacityMask
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Equal(
            [0x07, 0x1a, 0x07, 0x7e, 0x23,
             0x07, 0x34, 0x36, 0x35],
            ReadCommands(result.Bytes));
        int maskCommandOffset = FindCommand(result.Bytes, 0x23);
        uint maskHandle = ReadUInt32(result.Bytes, maskCommandOffset + 12);
        Assert.NotEqual(0U, maskHandle);
        int brushOffset = FindCommand(result.Bytes, 0x7e);
        Assert.Equal(maskHandle, ReadUInt32(result.Bytes, brushOffset + 8));
        Assert.Equal(0.5, ReadDouble(result.Bytes, brushOffset + 12));
        Assert.Equal(128F / 255F, ReadSingle(result.Bytes, brushOffset + 32));
        Assert.Equal(
            new NativeMilRect(1, 2, 30, 20),
            Assert.Single(result.VisualCacheBounds!).Bounds);
    }

    [Fact]
    public void BuildBatchTranslatesGradientVisualOpacityMaskWithBounds()
    {
        var opacityMask = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(1, 0),
            [
                new PortableGradientStop(
                    new PortableColor(0, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 1)
            ]));
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasOpacityMask = true,
                OpacityMask = opacityMask
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Contains(0x7f, ReadCommands(result.Bytes));
        Assert.Contains(0x23, ReadCommands(result.Bytes));
        Assert.Equal(
            new NativeMilRect(1, 2, 30, 20),
            Assert.Single(result.VisualCacheBounds!).Bounds);
    }

    [Fact]
    public void BuildBatchTranslatesTypedStaticVisualGuidelines()
    {
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasSnappingGuidelinesX = true,
                SnappingGuidelinesX = [2.25],
                HasSnappingGuidelinesY = true,
                SnappingGuidelinesY = [3.5]
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Equal(
            [0x07, 0x1a, 0x27, 0x07, 0x34, 0x36, 0x35],
            ReadCommands(result.Bytes));
        int offset = FindCommand(result.Bytes, 0x27);
        Assert.Equal(1U, ReadUInt16(result.Bytes, offset + 12));
        Assert.Equal(1U, ReadUInt16(result.Bytes, offset + 16));
        Assert.Equal(2.25F, ReadSingle(result.Bytes, offset + 20));
        Assert.Equal(3.5F, ReadSingle(result.Bytes, offset + 24));
    }

    [Fact]
    public void BuildBatchPublishesCachedGradientMaskGuidelineAndEffectPackets()
    {
        var opacityMask = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(1, 0),
            [
                new PortableGradientStop(
                    new PortableColor(0, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 1)
            ]));
        var effect = new FakeEffect(PortableEffect.Blur(
            6,
            PortableBlurKernel.Gaussian,
            PortableEffectRenderingBias.Quality));
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasCacheMode = true,
                CacheMode = new FakeBitmapCache(
                    new PortableBitmapCache(1, false, false)),
                HasOpacityMask = true,
                OpacityMask = opacityMask,
                HasEffect = true,
                Effect = effect,
                HasSnappingGuidelinesX = true,
                SnappingGuidelinesX = [2.25],
                HasSnappingGuidelinesY = true,
                SnappingGuidelinesY = [3.5]
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        List<int> commands = ReadCommands(result.Bytes);

        Assert.Single(commands, command => command == 0x7f);
        Assert.Single(commands, command => command == 0x23);
        Assert.Single(commands, command => command == 0x27);
        Assert.Single(commands, command => command == 0x1e);
        Assert.Single(commands, command => command == 0x6e);
        Assert.Single(commands, command => command == 0x1d);
        int guidelineOffset = FindCommand(result.Bytes, 0x27);
        Assert.Equal(1U, ReadUInt16(result.Bytes, guidelineOffset + 12));
        Assert.Equal(1U, ReadUInt16(result.Bytes, guidelineOffset + 16));
        Assert.Equal(2.25F, ReadSingle(result.Bytes, guidelineOffset + 20));
        Assert.Equal(3.5F, ReadSingle(result.Bytes, guidelineOffset + 24));
        Assert.Equal(
            new NativeMilRect(1, 2, 30, 20),
            Assert.Single(result.VisualCacheBounds!).Bounds);
    }

    [Fact]
    public void BuildBatchPublishesMultipleVisualGuidelines()
    {
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasSnappingGuidelinesX = true,
                SnappingGuidelinesX = [1, 2]
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int guidelineOffset = FindCommand(result.Bytes, 0x27);
        Assert.Equal(2U, ReadUInt16(result.Bytes, guidelineOffset + 12));
        Assert.Equal(0U, ReadUInt16(result.Bytes, guidelineOffset + 16));
        Assert.Equal(1F, ReadSingle(result.Bytes, guidelineOffset + 20));
        Assert.Equal(2F, ReadSingle(result.Bytes, guidelineOffset + 24));
    }

    [Fact]
    public void BuildBatchTranslatesTypedVisualGaussianBlurEffect()
    {
        var effect = new FakeEffect(PortableEffect.Blur(
            9.5,
            PortableBlurKernel.Gaussian,
            PortableEffectRenderingBias.Quality));
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasEffect = true,
                Effect = effect
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Equal(
            [0x07, 0x1a, 0x07, 0x6e, 0x1d,
             0x07, 0x34, 0x36, 0x35],
            ReadCommands(result.Bytes));
        int effectOffset = FindCommand(result.Bytes, 0x6e);
        Assert.Equal(2U, ReadUInt32(result.Bytes, effectOffset + 8));
        Assert.Equal(9.5, ReadDouble(result.Bytes, effectOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, effectOffset + 20));
        Assert.Equal(0U, ReadUInt32(result.Bytes, effectOffset + 24));
        Assert.Equal(1U, ReadUInt32(result.Bytes, effectOffset + 28));
        int visualOffset = FindCommand(result.Bytes, 0x1d);
        Assert.Equal(1U, ReadUInt32(result.Bytes, visualOffset + 8));
        Assert.Equal(2U, ReadUInt32(result.Bytes, visualOffset + 12));
        WpfNativeMilVisualCacheBounds bounds = Assert.Single(
            result.VisualCacheBounds!);
        Assert.Equal(1U, bounds.Handle);
        Assert.Equal(new NativeMilRect(1, 2, 30, 20), bounds.Bounds);
    }

    [Fact]
    public void BuildBatchTranslatesTypedVisualBoxBlurEffect()
    {
        var effect = new FakeEffect(PortableEffect.Blur(
            7.25,
            PortableBlurKernel.Box,
            PortableEffectRenderingBias.Quality));
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasEffect = true,
                Effect = effect
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int effectOffset = FindCommand(result.Bytes, 0x6e);
        Assert.Equal(2U, ReadUInt32(result.Bytes, effectOffset + 8));
        Assert.Equal(7.25, ReadDouble(result.Bytes, effectOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, effectOffset + 20));
        Assert.Equal(1U, ReadUInt32(result.Bytes, effectOffset + 24));
        Assert.Equal(1U, ReadUInt32(result.Bytes, effectOffset + 28));
        Assert.Contains(0x1d, ReadCommands(result.Bytes));
        Assert.Equal(
            new NativeMilRect(1, 2, 30, 20),
            Assert.Single(result.VisualCacheBounds!).Bounds);
    }

    [Fact]
    public void BuildBatchTranslatesTypedVisualDropShadowEffect()
    {
        var effect = new FakeEffect(PortableEffect.DropShadow(
            6.5,
            4,
            315,
            0.4,
            new PortableColor(128, 32, 64, 128),
            PortableEffectRenderingBias.Performance));
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasEffect = true,
                Effect = effect
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int offset = FindCommand(result.Bytes, 0x6f);

        Assert.Equal(2U, ReadUInt32(result.Bytes, offset + 8));
        Assert.Equal(4.0, ReadDouble(result.Bytes, offset + 12));
        Assert.Equal(315.0, ReadDouble(result.Bytes, offset + 36));
        Assert.Equal(0.4, ReadDouble(result.Bytes, offset + 44));
        Assert.Equal(6.5, ReadDouble(result.Bytes, offset + 52));
        Assert.Equal(128F / 255F, ReadSingle(result.Bytes, offset + 32));
        WpfNativeMilVisualCacheBounds bounds = Assert.Single(
            result.VisualCacheBounds!);
        Assert.Equal(1U, bounds.Handle);
        Assert.Equal(new NativeMilRect(1, 2, 30, 20), bounds.Bounds);
    }

    [Fact]
    public void BuildBatchRejectsEffectWithoutTypedVisualBounds()
    {
        var visual = new FakeVisualWithoutBounds(
            new PortableVisualState
            {
                HasEffect = true,
                Effect = new FakeEffect(PortableEffect.Blur(5))
            });

        NotSupportedException exception =
            Assert.Throws<NotSupportedException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            "exact typed Visual descendant bounds",
            exception.Message);
    }

    [Fact]
    public void BuildBatchPublishesBoundsForInheritedOpacityAroundChildEffect()
    {
        var child = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasEffect = true,
                Effect = new FakeEffect(PortableEffect.Blur(5))
            });
        var parent = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasOpacity = true,
                Opacity = 0.5
            },
            child);

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            parent, 64, 64);

        Assert.Collection(
            result.VisualCacheBounds!,
            bounds =>
            {
                Assert.Equal(1U, bounds.Handle);
                Assert.Equal(
                    new NativeMilRect(1, 2, 30, 20), bounds.Bounds);
            },
            bounds =>
            {
                Assert.Equal(2U, bounds.Handle);
                Assert.Equal(
                    new NativeMilRect(1, 2, 30, 20), bounds.Bounds);
            });
    }

    [Fact]
    public void BuildBatchPublishesBoundsForInheritedMaskAroundChildEffect()
    {
        var opacityMask = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(1, 0),
            [
                new PortableGradientStop(
                    new PortableColor(0, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 1)
            ]));
        var childOpacityMask = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(0, 1),
            [
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(0, 255, 255, 255), 1)
            ]));
        var child = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasEffect = true,
                Effect = new FakeEffect(PortableEffect.Blur(5)),
                HasOpacityMask = true,
                OpacityMask = childOpacityMask
            });
        var parent = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasOpacityMask = true,
                OpacityMask = opacityMask
            },
            child);

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            parent, 64, 64);

        Assert.Collection(
            result.VisualCacheBounds!,
            bounds =>
            {
                Assert.Equal(1U, bounds.Handle);
                Assert.Equal(
                    new NativeMilRect(1, 2, 30, 20), bounds.Bounds);
            },
            bounds =>
            {
                Assert.Equal(3U, bounds.Handle);
                Assert.Equal(
                    new NativeMilRect(1, 2, 30, 20), bounds.Bounds);
            });
        Assert.Equal(2, ReadCommands(result.Bytes).Count(x => x == 0x23));
        Assert.Contains(0x1d, ReadCommands(result.Bytes));
    }

    [Fact]
    public void BuildBatchPublishesNestedCachedMaskOwnershipPackets()
    {
        var parentMask = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(1, 0),
            [
                new PortableGradientStop(
                    new PortableColor(0, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 1)
            ]));
        var childMask = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(0, 1),
            [
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(0, 255, 255, 255), 1)
            ]));
        var child = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasCacheMode = true,
                CacheMode = new FakeBitmapCache(
                    new PortableBitmapCache(1, false, false)),
                HasEffect = true,
                Effect = new FakeEffect(PortableEffect.Blur(5)),
                HasOpacityMask = true,
                OpacityMask = childMask
            });
        var parent = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasCacheMode = true,
                CacheMode = new FakeBitmapCache(
                    new PortableBitmapCache(1, false, false)),
                HasOpacityMask = true,
                OpacityMask = parentMask
            },
            child);

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            parent, 64, 64);
        List<int> commands = ReadCommands(result.Bytes);

        Assert.Equal(2, commands.Count(x => x == 0x1e));
        Assert.Equal(2, commands.Count(x => x == 0x23));
        Assert.Single(commands, x => x == 0x1d);
        Assert.Equal(2, result.VisualCacheBounds!.Count);
        Assert.All(
            result.VisualCacheBounds,
            bounds => Assert.Equal(
                new NativeMilRect(1, 2, 30, 20), bounds.Bounds));
    }

    [Fact]
    public void BuildBatchRejectsOpacityIsolationWithoutTypedVisualBounds()
    {
        var visual = new FakeVisualWithoutBounds(
            new PortableVisualState
            {
                HasOpacity = true,
                Opacity = 0.5
            });

        NotSupportedException exception =
            Assert.Throws<NotSupportedException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            "exact typed Visual descendant bounds",
            exception.Message);
    }

    [Fact]
    public void BuildBatchRejectsOpacityMaskIsolationWithoutTypedVisualBounds()
    {
        var opacityMask = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(1, 0),
            [
                new PortableGradientStop(
                    new PortableColor(0, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 1)
            ]));
        var visual = new FakeVisualWithoutBounds(
            new PortableVisualState
            {
                HasOpacityMask = true,
                OpacityMask = opacityMask
            });

        NotSupportedException exception =
            Assert.Throws<NotSupportedException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            "exact typed Visual descendant bounds",
            exception.Message);
    }

    [Fact]
    public void BuildBatchAllowsExactTypedRectangleAndScrollClipsWithEffect()
    {
        var clip = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                0,
                0,
                new PortableMatrix3x2(2, 0, 0, 3, 4, 5)));
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasEffect = true,
                Effect = new FakeEffect(PortableEffect.Blur(5)),
                HasClip = true,
                Clip = clip,
                HasScrollableAreaClip = true,
                ScrollableAreaClip = new PortableRect(4, 5, 30, 24)
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        List<int> commands = ReadCommands(result.Bytes);

        Assert.Contains(0x1f, commands);
        Assert.Contains(0x28, commands);
        Assert.Contains(0x1d, commands);
        WpfNativeMilVisualCacheBounds bounds = Assert.Single(
            result.VisualCacheBounds!);
        Assert.Equal(new NativeMilRect(1, 2, 30, 20), bounds.Bounds);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BuildBatchPreservesExactEffectGeometryClips(bool cached, bool masked)
    {
        PortablePrimitiveGeometry[] clips =
        [
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                2,
                2,
                PortableMatrix3x2.Identity),
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                0,
                0,
                new PortableMatrix3x2(1, 0.5, 0, 1, 0, 0)),
            PortablePrimitiveGeometry.Ellipse(
                new PortablePoint(12, 9),
                10,
                6,
                PortableMatrix3x2.Identity)
        ];

        foreach (PortablePrimitiveGeometry clip in clips)
        {
            var visual = new FakeVisual(
                null,
                new PortableVisualState
                {
                    HasEffect = !cached,
                    Effect = cached ? null : new FakeEffect(PortableEffect.Blur(5)),
                    HasCacheMode = cached,
                    CacheMode = cached ? new FakeBitmapCache(
                        new PortableBitmapCache(2, true, false)) : null,
                    HasOpacityMask = masked,
                    OpacityMask = masked ? new FakeBrush(PortableBrush.LinearGradient(
                        new PortablePoint(0, 0), new PortablePoint(1, 0),
                        [new PortableGradientStop(new PortableColor(0, 255, 255, 255), 0),
                         new PortableGradientStop(new PortableColor(255, 255, 255, 255), 1)])) : null,
                    HasClip = true,
                    Clip = new FakePrimitiveGeometry(clip)
                });

            WpfNativeMilBatch result =
                new WpfNativeMilSceneCompiler().BuildBatch(visual, 64, 64);
            int clipOffset = FindCommand(result.Bytes, 0x1f);
            Assert.Equal(1U, ReadUInt32(result.Bytes, clipOffset + 8));
            Assert.NotEqual(0U, ReadUInt32(result.Bytes, clipOffset + 12));
            Assert.Contains(clip.Kind == PortablePrimitiveGeometryKind.Ellipse
                ? 0x7a : 0x79, ReadCommands(result.Bytes));
            Assert.Contains(cached ? 0x1e : 0x1d, ReadCommands(result.Bytes));
            Assert.Single(result.VisualCacheBounds!);
            if (masked)
            {
                Assert.Contains(0x7f, ReadCommands(result.Bytes));
                Assert.Contains(0x23, ReadCommands(result.Bytes));
            }
        }
    }

    [Fact]
    public void BuildBatchAllowsTypedGradientOpacityMaskWithIsolation()
    {
        var opacityMask = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(1, 0),
            [
                new PortableGradientStop(
                    new PortableColor(0, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 1)
            ]));
        var visual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasEffect = true,
                Effect = new FakeEffect(PortableEffect.Blur(5)),
                HasOpacityMask = true,
                OpacityMask = opacityMask
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        List<int> commands = ReadCommands(result.Bytes);

        Assert.Contains(0x7f, commands);
        Assert.Contains(0x23, commands);
        Assert.Contains(0x1d, commands);
        Assert.Single(result.VisualCacheBounds!);

        var cachedVisual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasCacheMode = true,
                CacheMode = new FakeBitmapCache(
                    new PortableBitmapCache(1, false, false)),
                HasOpacityMask = true,
                OpacityMask = opacityMask
            });
        WpfNativeMilBatch cachedResult =
            new WpfNativeMilSceneCompiler().BuildBatch(
                cachedVisual, 64, 64);
        List<int> cachedCommands = ReadCommands(cachedResult.Bytes);
        Assert.Contains(0x7f, cachedCommands);
        Assert.Contains(0x23, cachedCommands);
        Assert.Contains(0x1e, cachedCommands);
    }

    [Fact]
    public void BuildBatchRejectsUnknownBlurKernelButAllowsUniformOpacity()
    {
        var unknown = new FakeEffect(PortableEffect.Blur(
            5,
            (PortableBlurKernel)2));
        var unknownVisual = new FakeVisual(
            null,
            new PortableVisualState { HasEffect = true, Effect = unknown });
        Assert.Throws<NotSupportedException>(() =>
            new WpfNativeMilSceneCompiler().BuildBatch(
                unknownVisual, 64, 64));

        var combinedVisual = new FakeVisual(
            null,
            new PortableVisualState
            {
                HasEffect = true,
                Effect = new FakeEffect(PortableEffect.Blur(5)),
                HasOpacity = true,
                Opacity = 0.5
            });
        WpfNativeMilBatch result =
            new WpfNativeMilSceneCompiler().BuildBatch(
                combinedVisual, 64, 64);

        Assert.Contains(0x1d, ReadCommands(result.Bytes));
        Assert.Contains(0x20, ReadCommands(result.Bytes));
        WpfNativeMilVisualCacheBounds bounds = Assert.Single(
            result.VisualCacheBounds!);
        Assert.Equal(new NativeMilRect(1, 2, 30, 20), bounds.Bounds);
    }

    [Fact]
    public void BuildBatchTranslatesTypedVisualRectangleAndSolidBrush()
    {
        var brush = new FakeBrush(new PortableColor(192, 128, 64, 32));
        var visual = new FakeVisual(
            new FakeRenderData(CreateRectangleRecord(1, 0), [brush]),
            new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1,
                HasBitmapScalingMode = true,
                HasPortableBitmapScalingMode = true,
                PortableBitmapScalingMode =
                    PortableBitmapScalingMode.NearestNeighbor,
                HasEdgeMode = true,
                HasPortableEdgeMode = true,
                PortableEdgeMode = PortableEdgeMode.Aliased,
                HasClearTypeHint = true,
                HasPortableClearTypeHint = true,
                PortableClearTypeHint = PortableClearTypeHint.Enabled,
                HasTextRenderingMode = true,
                HasPortableTextRenderingMode = true,
                PortableTextRenderingMode =
                    PortableTextRenderingMode.ClearType,
                HasTextHintingMode = true,
                HasPortableTextHintingMode = true,
                PortableTextHintingMode = PortableTextHintingMode.Fixed
            });

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 640, 480);
        List<int> commands = ReadCommands(result.Bytes);

        Assert.Equal(4U, result.TargetHandle);
        Assert.Equal(
            [0x07, 0x1a, 0x1b, 0x20, 0x21, 0x07, 0x7e, 0x07, 0x18,
             0x22, 0x07, 0x34, 0x36, 0x35],
            commands);
        int renderOptionsOffset = FindCommand(result.Bytes, 0x21);
        Assert.Equal(1U, ReadUInt32(result.Bytes, renderOptionsOffset + 8));
        Assert.Equal(0x3bU, ReadUInt32(result.Bytes, renderOptionsOffset + 12));
        Assert.Equal(1U, ReadUInt32(result.Bytes, renderOptionsOffset + 16));
        Assert.Equal(0U, ReadUInt32(result.Bytes, renderOptionsOffset + 20));
        Assert.Equal(3U, ReadUInt32(result.Bytes, renderOptionsOffset + 24));
        Assert.Equal(1U, ReadUInt32(result.Bytes, renderOptionsOffset + 28));
        Assert.Equal(3U, ReadUInt32(result.Bytes, renderOptionsOffset + 32));
        Assert.Equal(1U, ReadUInt32(result.Bytes, renderOptionsOffset + 36));
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;
        Assert.Equal(48, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x40, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 44));
        Assert.Equal(2.0, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(6.0, ReadDouble(result.Bytes, nestedOffset + 16));
        Assert.Equal(30.0, ReadDouble(result.Bytes, nestedOffset + 24));
        Assert.Equal(40.0, ReadDouble(result.Bytes, nestedOffset + 32));

        int brushOffset = FindCommand(result.Bytes, 0x7e);
        Assert.Equal(2U, ReadUInt32(result.Bytes, brushOffset + 8));
        Assert.Equal(1.0, ReadDouble(result.Bytes, brushOffset + 12));
        Assert.Equal(SrgbToLinear(128), ReadSingle(result.Bytes, brushOffset + 20));
        Assert.Equal(SrgbToLinear(64), ReadSingle(result.Bytes, brushOffset + 24));
        Assert.Equal(SrgbToLinear(32), ReadSingle(result.Bytes, brushOffset + 28));
        Assert.Equal(192 / 255.0f, ReadSingle(result.Bytes, brushOffset + 32));
    }

    [Fact]
    public void BuildBatchTranslatesTypedLinearGradientBrush()
    {
        var brush = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0.1, 0.2),
            new PortablePoint(0.9, 0.8),
            [
                new PortableGradientStop(
                    new PortableColor(255, 255, 0, 0), -0.25),
                new PortableGradientStop(
                    new PortableColor(128, 0, 64, 255), 1.25)
            ],
            opacity: 0.75,
            mappingMode: PortableBrushMappingMode.RelativeToBoundingBox,
            spreadMethod: PortableGradientSpreadMethod.Reflect,
            colorInterpolationMode:
                PortableGradientColorInterpolationMode.ScRgbLinearInterpolation,
            hasTransform: true,
            transform: new PortableMatrix3x2(1, 0, 0, 1, 12, 14),
            hasRelativeTransform: true,
            relativeTransform: new PortableMatrix3x2(2, 0, 0, 3, 0, 0)));
        var visual = new FakeVisual(
            new FakeRenderData(CreateRectangleRecord(1, 0), [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 48);

        int gradientOffset = FindCommand(result.Bytes, 0x7f);
        Assert.Equal(4U, ReadUInt32(result.Bytes, gradientOffset + 8));
        Assert.Equal(0.75, ReadDouble(result.Bytes, gradientOffset + 12));
        Assert.Equal(0.1, ReadDouble(result.Bytes, gradientOffset + 20));
        Assert.Equal(0.2, ReadDouble(result.Bytes, gradientOffset + 28));
        Assert.Equal(0.9, ReadDouble(result.Bytes, gradientOffset + 36));
        Assert.Equal(0.8, ReadDouble(result.Bytes, gradientOffset + 44));
        Assert.Equal(2U, ReadUInt32(result.Bytes, gradientOffset + 56));
        Assert.Equal(3U, ReadUInt32(result.Bytes, gradientOffset + 60));
        Assert.Equal(0U, ReadUInt32(result.Bytes, gradientOffset + 64));
        Assert.Equal(1U, ReadUInt32(result.Bytes, gradientOffset + 68));
        Assert.Equal(1U, ReadUInt32(result.Bytes, gradientOffset + 72));
        Assert.Equal(48U, ReadUInt32(result.Bytes, gradientOffset + 76));
        Assert.Equal(-0.25, ReadDouble(result.Bytes, gradientOffset + 88));
        Assert.Equal(
            SrgbToLinear(255),
            ReadSingle(result.Bytes, gradientOffset + 96));
        Assert.Equal(1.25, ReadDouble(result.Bytes, gradientOffset + 112));
        Assert.Equal(
            SrgbToLinear(64),
            ReadSingle(result.Bytes, gradientOffset + 124));
        Assert.Equal(
            128 / 255.0f,
            ReadSingle(result.Bytes, gradientOffset + 132));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 40));
    }

    [Fact]
    public void BuildBatchTranslatesTypedRadialGradientPenBrush()
    {
        PortableBrush brush = PortableBrush.RadialGradient(
            new PortablePoint(0.5, 0.5),
            new PortablePoint(0.25, 0.75),
            0.6,
            0.4,
            [
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(255, 0, 0, 0), 1)
            ],
            opacity: 0.625,
            mappingMode: PortableBrushMappingMode.Absolute,
            spreadMethod: PortableGradientSpreadMethod.Repeat,
            colorInterpolationMode:
                PortableGradientColorInterpolationMode.SRgbLinearInterpolation);
        var pen = new FakePen(
            brush,
            3,
            PortablePenLineCap.Round,
            PortablePenLineCap.Square,
            PortablePenLineCap.Flat,
            PortablePenLineJoin.Round,
            6,
            []);
        var visual = new FakeVisual(
            new FakeRenderData(CreateRectangleRecord(0, 1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 48);

        int gradientOffset = FindCommand(result.Bytes, 0x80);
        Assert.Equal(2U, ReadUInt32(result.Bytes, gradientOffset + 8));
        Assert.Equal(0.625, ReadDouble(result.Bytes, gradientOffset + 12));
        Assert.Equal(0.5, ReadDouble(result.Bytes, gradientOffset + 20));
        Assert.Equal(0.5, ReadDouble(result.Bytes, gradientOffset + 28));
        Assert.Equal(0.6, ReadDouble(result.Bytes, gradientOffset + 36));
        Assert.Equal(0.4, ReadDouble(result.Bytes, gradientOffset + 44));
        Assert.Equal(0.25, ReadDouble(result.Bytes, gradientOffset + 52));
        Assert.Equal(0.75, ReadDouble(result.Bytes, gradientOffset + 60));
        Assert.Equal(1U, ReadUInt32(result.Bytes, gradientOffset + 80));
        Assert.Equal(0U, ReadUInt32(result.Bytes, gradientOffset + 84));
        Assert.Equal(2U, ReadUInt32(result.Bytes, gradientOffset + 88));
        Assert.Equal(48U, ReadUInt32(result.Bytes, gradientOffset + 92));

        int penOffset = FindCommand(result.Bytes, 0x86);
        Assert.Equal(2U, ReadUInt32(result.Bytes, penOffset + 28));
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchTranslatesTypedRectanglePen()
    {
        var brush = new FakeBrush(new PortableColor(255, 255, 0, 0));
        var pen = new FakePen(
            new PortableColor(255, 0, 0, 255),
            2,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Round,
            PortablePenLineJoin.Bevel,
            8,
            []);
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRectangleRecord(1, 2),
                [brush, pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 32, 32);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 44));
        int penOffset = FindCommand(result.Bytes, 0x86);
        Assert.Equal(4U, ReadUInt32(result.Bytes, penOffset + 8));
        Assert.Equal(3U, ReadUInt32(result.Bytes, penOffset + 28));
        Assert.Equal(1U, ReadUInt32(result.Bytes, penOffset + 48));
    }

    [Fact]
    public void BuildBatchPreservesPenOnlyRectangle()
    {
        var pen = new FakePen(
            new PortableColor(255, 0, 255, 0),
            1,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Miter,
            10,
            [2, 1]);
        var visual = new FakeVisual(
            new FakeRenderData(CreateRectangleRecord(0, 1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 32, 32);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchTranslatesTypedLineGeometryWithTransform()
    {
        var pen = new FakePen(
            new PortableColor(255, 0, 128, 255),
            2,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Round,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Miter,
            10,
            []);
        var geometry = new FakeGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            Transform = new PortableMatrix3x2(2, 0, 0, 3, 11, 13),
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(1, 2),
                    IsClosed = false,
                    IsFilled = false,
                    Segments =
                    [
                        PortablePathSegment.Line(
                            new PortablePoint(5, 8),
                            isSmoothJoin: false,
                            isStroked: true)
                    ]
                }
            ]
        });
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawGeometryRecord(0, 1, 2),
                [pen, geometry]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(24, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x46, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 12));
        Assert.Equal(5U, ReadUInt32(result.Bytes, nestedOffset + 16));

        int geometryOffset = FindCommand(result.Bytes, 0x78);
        Assert.Equal(5U, ReadUInt32(result.Bytes, geometryOffset + 8));
        Assert.Equal(1.0, ReadDouble(result.Bytes, geometryOffset + 12));
        Assert.Equal(2.0, ReadDouble(result.Bytes, geometryOffset + 20));
        Assert.Equal(5.0, ReadDouble(result.Bytes, geometryOffset + 28));
        Assert.Equal(8.0, ReadDouble(result.Bytes, geometryOffset + 36));
        Assert.Equal(4U, ReadUInt32(result.Bytes, geometryOffset + 44));
    }

    [Fact]
    public void BuildBatchTranslatesTypedGeometryDrawing()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 96, 192));
        var geometry = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                0,
                0,
                PortableMatrix3x2.Identity));
        var drawing = new FakeGeometryDrawing(
            brush,
            pen: null,
            geometry);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [drawing]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Equal(6U, result.TargetHandle);
        int drawingOffset = FindCommand(result.Bytes, 0x87);
        Assert.Equal(4U, ReadUInt32(result.Bytes, drawingOffset + 8));
        Assert.Equal(2U, ReadUInt32(result.Bytes, drawingOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, drawingOffset + 16));
        Assert.Equal(3U, ReadUInt32(result.Bytes, drawingOffset + 20));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(16, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x4a, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 12));
    }

    [Fact]
    public void BuildBatchTranslatesTypedNativeGlyphRun()
    {
        TtfFont font = LoadInterFont();
        var glyphRun = new FakeNativeGlyphRun(new PortableNativeGlyphRun
        {
            GlyphIndices = [
                font.GetGlyphIndex('A'),
                font.GetGlyphIndex('B')
            ],
            GlyphPositions = [
                new Vector2(0, 0),
                new Vector2(14, 0)
            ],
            BaselineOrigin = new Vector2(4, 24),
            FontRenderingEmSize = 20,
            NativeFont = font,
            IsBold = true
        });
        var brush = new FakeBrush(new PortableColor(255, 32, 96, 192));
        var visual = new FakeVisual(new FakeRenderData(
            CreateDrawGlyphRunRecord(1, 2),
            [brush, glyphRun]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        WpfNativeMilGlyphRunFont binding = Assert.Single(
            result.GlyphRunFonts!);
        Assert.Equal(3U, binding.Handle);
        Assert.Equal(0U, binding.FaceIndex);
        Assert.Equal(
            NativeMilGlyphStyleSimulations.Bold,
            binding.StyleSimulations);
        Assert.True(binding.FontData.Length > 0);

        int glyphOffset = FindCommand(result.Bytes, 0x3a);
        Assert.Equal(3U, ReadUInt32(result.Bytes, glyphOffset + 8));
        Assert.Equal(0UL, ReadUInt64(result.Bytes, glyphOffset + 12));
        Assert.Equal(0x10, ReadUInt16(result.Bytes, glyphOffset + 20));
        Assert.Equal(4.0f, ReadSingle(result.Bytes, glyphOffset + 24));
        Assert.Equal(24.0f, ReadSingle(result.Bytes, glyphOffset + 28));
        Assert.Equal(20.0f, ReadSingle(result.Bytes, glyphOffset + 32));
        Assert.Equal(2, ReadUInt16(result.Bytes, glyphOffset + 68));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(16, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x49, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 12));
    }

    [Fact]
    public void BuildBatchTranslatesTypedGlyphRunDrawing()
    {
        TtfFont font = LoadInterFont();
        var glyphRun = new FakeNativeGlyphRun(new PortableNativeGlyphRun
        {
            GlyphIndices = [font.GetGlyphIndex('W')],
            GlyphPositions = [new Vector2(0, 0)],
            BaselineOrigin = new Vector2(3, 22),
            FontRenderingEmSize = 18,
            NativeFont = font,
            IsItalic = true
        });
        var brush = new FakeBrush(new PortableColor(255, 16, 32, 64));
        var drawing = new FakeGlyphRunDrawing(glyphRun, brush);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [drawing]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        WpfNativeMilGlyphRunFont binding = Assert.Single(
            result.GlyphRunFonts!);
        Assert.Equal(2U, binding.Handle);
        Assert.Equal(
            NativeMilGlyphStyleSimulations.Italic,
            binding.StyleSimulations);

        int drawingOffset = FindCommand(result.Bytes, 0x88);
        Assert.Equal(4U, ReadUInt32(result.Bytes, drawingOffset + 8));
        Assert.Equal(2U, ReadUInt32(result.Bytes, drawingOffset + 12));
        Assert.Equal(3U, ReadUInt32(result.Bytes, drawingOffset + 16));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(0x4a, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 8));
    }

    [Fact]
    public void BuildBatchRejectsGlyphRunWithNonidentityTransform()
    {
        TtfFont font = LoadInterFont();
        var glyphRun = new FakeNativeGlyphRun(new PortableNativeGlyphRun
        {
            GlyphIndices = [1],
            GlyphPositions = [new Vector2(0, 0)],
            BaselineOrigin = new Vector2(0, 16),
            FontRenderingEmSize = 16,
            NativeFont = font,
            HasTransform = true,
            Transform = Matrix4x4.CreateTranslation(2, 3, 0)
        });
        var visual = new FakeVisual(new FakeRenderData(
            CreateDrawGlyphRunRecord(0, 1),
            [glyphRun]));

        NotSupportedException exception =
            Assert.Throws<NotSupportedException>(
                () => new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains("identity glyph transform", exception.Message);
    }

    [Fact]
    public void BuildBatchRejectsUntypedDrawing()
    {
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawDrawingRecord(1),
                [new object()]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            nameof(IPortableGeometryDrawingStateSource),
            exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedImageDrawingAndBitmapPixels()
    {
        var bitmap = new FakeBitmapSource(new PortableBitmapSourcePixels(
            2,
            2,
            96,
            96,
            8,
            PortablePixelDataFormat.Pbgra32,
            [
                0, 0, 255, 255,
                0, 255, 0, 255,
                255, 0, 0, 255,
                255, 255, 255, 255
            ]));
        var drawing = new FakeImageDrawing(
            bitmap,
            new PortableRect(3, 5, 20, 10));
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [drawing]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Equal(5U, result.TargetHandle);
        WpfNativeMilBitmapSource source = Assert.Single(
            result.BitmapSources!);
        Assert.Equal(2U, source.Handle);
        Assert.Equal(2U, source.Width);
        Assert.Equal(2U, source.Height);
        Assert.Equal(8U, source.RowBytes);
        Assert.Equal(
            [
                255, 0, 0, 255,
                0, 255, 0, 255,
                0, 0, 255, 255,
                255, 255, 255, 255
            ],
            source.Rgba8Pixels);

        int drawingOffset = FindCommand(result.Bytes, 0x89);
        Assert.Equal(3U, ReadUInt32(result.Bytes, drawingOffset + 8));
        Assert.Equal(3.0, ReadDouble(result.Bytes, drawingOffset + 12));
        Assert.Equal(5.0, ReadDouble(result.Bytes, drawingOffset + 20));
        Assert.Equal(20.0, ReadDouble(result.Bytes, drawingOffset + 28));
        Assert.Equal(10.0, ReadDouble(result.Bytes, drawingOffset + 36));
        Assert.Equal(2U, ReadUInt32(result.Bytes, drawingOffset + 44));
        Assert.Equal(0U, ReadUInt32(result.Bytes, drawingOffset + 48));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 8));
    }

    [Fact]
    public void BuildBatchTranslatesDirectTypedBitmapDrawImage()
    {
        var bitmap = new FakeBitmapSource(new PortableBitmapSourcePixels(
            1,
            1,
            144,
            192,
            4,
            PortablePixelDataFormat.Pbgra32,
            [16, 32, 64, 255]));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawImageRecord(2, 3, 40, 24, 1),
                [bitmap]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        WpfNativeMilBitmapSource source = Assert.Single(result.BitmapSources!);
        Assert.Equal(144.0, source.DpiX);
        Assert.Equal(192.0, source.DpiY);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(48, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x47, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(2.0, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(3.0, ReadDouble(result.Bytes, nestedOffset + 16));
        Assert.Equal(40.0, ReadDouble(result.Bytes, nestedOffset + 24));
        Assert.Equal(24.0, ReadDouble(result.Bytes, nestedOffset + 32));
        Assert.Equal(source.Handle, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchTranslatesPortableNativeImageWithoutCpuPixels()
    {
        var bitmap = new FakePortableNativeImage(128, 64) { DpiX = 144, DpiY = 192 };
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawImageRecord(2, 3, 40, 24, 1),
                [bitmap]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 256, 128);
        WpfNativeMilBitmapExternalImageSource source = Assert.Single(
            result.BitmapExternalImageSources!);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Empty(result.BitmapSources!);
        Assert.Equal(128U, source.Width);
        Assert.Equal(64U, source.Height);
        Assert.Equal(144.0, source.DpiX);
        Assert.Equal(192.0, source.DpiY);
        Assert.Same(bitmap, source.TextureSource);
        Assert.Equal(source.Handle, ReadUInt32(result.Bytes, nestedOffset + 40));
        int createOffset = FindCreateResource(result.Bytes, source.Handle);
        Assert.Equal(95U, ReadUInt32(result.Bytes, createOffset + 12));
    }

    [Fact]
    public void BuildBatchTranslatesPortableD3DImageWithCanonicalPresent()
    {
        var image = new FakeD3DImage(192, 108, 7);
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawImageRecord(2, 3, 40, 24, 1),
                [image]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 256, 128);
        WpfNativeMilD3DImageSource source = Assert.Single(
            result.D3DImageSources!);
        int updateOffset = FindCommand(result.Bytes, 0x0a);
        int presentOffset = FindCommand(result.Bytes, 0x0b);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(192U, source.Width);
        Assert.Equal(108U, source.Height);
        Assert.Equal(7UL, source.ContentVersion);
        Assert.Same(image, source.TextureSource);
        Assert.Equal(source.Handle, ReadUInt32(result.Bytes, updateOffset + 8));
        Assert.Equal(0U, ReadUInt32(result.Bytes, updateOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, updateOffset + 16));
        Assert.Equal(0U, ReadUInt32(result.Bytes, updateOffset + 20));
        Assert.Equal(0U, ReadUInt32(result.Bytes, updateOffset + 24));
        Assert.Equal(source.Handle, ReadUInt32(result.Bytes, presentOffset + 8));
        Assert.Equal(0U, ReadUInt32(result.Bytes, presentOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, presentOffset + 16));
        Assert.Equal(source.Handle, ReadUInt32(result.Bytes, nestedOffset + 40));
        int createOffset = FindCreateResource(result.Bytes, source.Handle);
        Assert.Equal(97U, ReadUInt32(result.Bytes, createOffset + 12));
        Assert.Empty(result.BitmapSources!);
        Assert.Empty(result.BitmapExternalImageSources!);
    }

    [Fact]
    public void BuildBatchPreservesNullDirectImageAsNoOp()
    {
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawImageRecord(2, 3, 40, 24, 0),
                []));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);

        Assert.Equal(16U, ReadUInt32(result.Bytes, renderDataOffset));
        Assert.Empty(result.BitmapSources!);
    }

    [Fact]
    public void BuildBatchTranslatesTypedLiveVideoWithoutCpuPixels()
    {
        var player = new FakeMediaPlayer(64, 32, 7);
        var rectangle = new FakeRectAnimationValue(
            new PortableRect(4, 5, 30, 20));
        byte[] renderData = CreateDrawVideoRecord(
                2, 3, 40, 24, 1)
            .Concat(CreateDrawVideoRecord(
                1, 2, 10, 12, 1, 2))
            .ToArray();
        var visual = new FakeVisual(new FakeRenderData(
            renderData,
            [player, rectangle]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        WpfNativeMilMediaPlayerSource source = Assert.Single(
            result.MediaPlayerSources!);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(64U, source.Width);
        Assert.Equal(32U, source.Height);
        Assert.Equal(7UL, source.ContentVersion);
        Assert.Same(player, source.TextureSource);
        Assert.Empty(result.BitmapSources!);
        Assert.Equal(0x4b, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(source.Handle, ReadUInt32(
            result.Bytes, nestedOffset + 40));
        nestedOffset += 48;
        Assert.Equal(0x4c, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(source.Handle, ReadUInt32(
            result.Bytes, nestedOffset + 40));
        Assert.NotEqual(0U, ReadUInt32(result.Bytes, nestedOffset + 44));
        int createOffset = FindCreateResource(
            result.Bytes,
            source.Handle);
        Assert.Equal(1U, ReadUInt32(result.Bytes, createOffset + 12));
    }

    [Fact]
    public void BuildBatchTreatsMediaPlayerWithoutReadyFrameAsNoOp()
    {
        var player = new FakeMediaPlayer();
        var visual = new FakeVisual(new FakeRenderData(
            CreateDrawVideoRecord(2, 3, 40, 24, 1),
            [player]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);

        Assert.Equal(16U, ReadUInt32(result.Bytes, renderDataOffset));
        Assert.Empty(result.MediaPlayerSources!);
    }

    [Fact]
    public void BuildBatchRejectsUntypedVideoPlayer()
    {
        var visual = new FakeVisual(new FakeRenderData(
            CreateDrawVideoRecord(2, 3, 40, 24, 1),
            [new object()]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(nameof(IPortableMediaPlayerSource), exception.Message);
    }

    [Fact]
    public void BuildBatchRejectsImageDrawingWithoutTypedPixels()
    {
        var drawing = new FakeImageDrawing(
            new object(),
            new PortableRect(0, 0, 10, 10));
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [drawing]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            nameof(IPortableBitmapSourcePixelsSource),
            exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedDrawingImageWithExactBounds()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 96, 224));
        var geometry = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(10, 20, 20, 10),
                0,
                0,
                PortableMatrix3x2.Identity));
        var vectorDrawing = new FakeGeometryDrawing(
            brush,
            null,
            geometry,
            new PortableRect(10, 20, 20, 10));
        var drawingImage = new FakeDrawingImage(vectorDrawing);
        var imageDrawing = new FakeImageDrawing(
            drawingImage,
            new PortableRect(2, 4, 40, 20));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawDrawingRecord(1), [imageDrawing]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Empty(result.BitmapSources!);
        WpfNativeMilDrawingImageBounds source = Assert.Single(
            result.DrawingImageBounds!);
        Assert.Equal(5U, source.Handle);
        Assert.Equal(new NativeMilRect(10, 20, 20, 10), source.Bounds);

        int drawingImageOffset = FindCommand(result.Bytes, 0x71);
        Assert.Equal(5U, ReadUInt32(result.Bytes, drawingImageOffset + 8));
        Assert.Equal(4U, ReadUInt32(result.Bytes, drawingImageOffset + 12));
        int imageDrawingOffset = FindCommand(result.Bytes, 0x89);
        Assert.Equal(5U, ReadUInt32(result.Bytes, imageDrawingOffset + 44));
    }

    [Fact]
    public void BuildBatchPreservesEmptyTypedDrawingImageAsNoOp()
    {
        var imageDrawing = new FakeImageDrawing(
            new FakeDrawingImage(null),
            new PortableRect(2, 4, 40, 20));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawDrawingRecord(1), [imageDrawing]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Empty(result.DrawingImageBounds!);
        int drawingImageOffset = FindCommand(result.Bytes, 0x71);
        Assert.Equal(0U, ReadUInt32(result.Bytes, drawingImageOffset + 12));
    }

    [Fact]
    public void BuildBatchTranslatesTypedDrawingGroup()
    {
        var transform = new FakeTransform(
            new PortableMatrix3x2(1, 0, 0, 1, 10, 20));
        var clip = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(0, 0, 10, 10),
                0,
                0,
                PortableMatrix3x2.Identity));
        var brush = new FakeBrush(new PortableColor(255, 32, 96, 192));
        var geometry = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                0,
                0,
                PortableMatrix3x2.Identity));
        var child = new FakeGeometryDrawing(brush, null, geometry);
        var group = new FakeDrawingGroup(
            new PortableDrawingGroupState
            {
                HasLocalBounds = true,
                LocalBounds = new PortableRect(2, 3, 20, 12),
                HasOpacity = true,
                Opacity = 0.5,
                HasTransform = true,
                Transform = transform,
                HasClipGeometry = true,
                ClipGeometry = clip,
                HasEdgeMode = true,
                HasPortableEdgeMode = true,
                PortableEdgeMode = PortableEdgeMode.Aliased,
                HasClearTypeHint = true,
                HasPortableClearTypeHint = true,
                PortableClearTypeHint = PortableClearTypeHint.Enabled,
                HasPortableBitmapScalingMode = true,
                PortableBitmapScalingMode =
                    PortableBitmapScalingMode.NearestNeighbor
            },
            [child]);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [group]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        Assert.Equal(9U, result.TargetHandle);
        int childOffset = FindCommand(result.Bytes, 0x87);
        Assert.Equal(6U, ReadUInt32(result.Bytes, childOffset + 8));
        Assert.Equal(4U, ReadUInt32(result.Bytes, childOffset + 12));
        Assert.Equal(5U, ReadUInt32(result.Bytes, childOffset + 20));

        int groupOffset = FindCommand(result.Bytes, 0x8b);
        Assert.Equal(7U, ReadUInt32(result.Bytes, groupOffset + 8));
        Assert.Equal(0.5, ReadDouble(result.Bytes, groupOffset + 12));
        Assert.Equal(4U, ReadUInt32(result.Bytes, groupOffset + 20));
        Assert.Equal(3U, ReadUInt32(result.Bytes, groupOffset + 24));
        Assert.Equal(0U, ReadUInt32(result.Bytes, groupOffset + 28));
        Assert.Equal(0U, ReadUInt32(result.Bytes, groupOffset + 32));
        Assert.Equal(2U, ReadUInt32(result.Bytes, groupOffset + 36));
        Assert.Equal(0U, ReadUInt32(result.Bytes, groupOffset + 40));
        Assert.Equal(1U, ReadUInt32(result.Bytes, groupOffset + 44));
        Assert.Equal(3U, ReadUInt32(result.Bytes, groupOffset + 48));
        Assert.Equal(1U, ReadUInt32(result.Bytes, groupOffset + 52));
        Assert.Equal(6U, ReadUInt32(result.Bytes, groupOffset + 56));

        WpfNativeMilDrawingGroupBounds groupBounds = Assert.Single(
            result.DrawingGroupBounds!);
        Assert.Equal(7U, groupBounds.Handle);
        Assert.Equal(
            new NativeMilRect(2, 3, 20, 12),
            groupBounds.Bounds);

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(7U, ReadUInt32(result.Bytes, nestedOffset + 8));
    }

    [Fact]
    public void BuildBatchRejectsTypedDrawingGroupCycle()
    {
        var group = new FakeDrawingGroup(
            new PortableDrawingGroupState
            {
                HasOpacity = true,
                Opacity = 1
            },
            []);
        group.SetChildren([group]);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [group]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains("cycle", exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedStaticDrawingGroupGuidelines()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 96, 192));
        var geometry = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                0,
                0,
                PortableMatrix3x2.Identity));
        var child = new FakeGeometryDrawing(brush, null, geometry);
        var guidelines = new FakeGuidelineSet(
            new PortableGuidelineSet(
                isFrozen: true,
                isDynamic: false,
                [2.25],
                [3.5]));
        var group = new FakeDrawingGroup(
            new PortableDrawingGroupState
            {
                HasGuidelineSet = true,
                GuidelineSet = guidelines
            },
            [child]);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [group]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int guidelineOffset = FindCommand(result.Bytes, 0x8c);
        Assert.Equal(40, ReadInt32(result.Bytes, guidelineOffset));
        uint guidelineHandle = ReadUInt32(result.Bytes, guidelineOffset + 8);
        Assert.Equal(8U, ReadUInt32(result.Bytes, guidelineOffset + 12));
        Assert.Equal(8U, ReadUInt32(result.Bytes, guidelineOffset + 16));
        Assert.Equal(0U, ReadUInt32(result.Bytes, guidelineOffset + 20));
        Assert.Equal(2.25, ReadDouble(result.Bytes, guidelineOffset + 24));
        Assert.Equal(3.5, ReadDouble(result.Bytes, guidelineOffset + 32));

        int groupOffset = FindCommand(result.Bytes, 0x8b);
        Assert.Equal(
            guidelineHandle,
            ReadUInt32(result.Bytes, groupOffset + 40));
    }

    [Fact]
    public void BuildBatchPublishesDynamicAndMultipleTypedGuidelines()
    {
        static FakeVisual CreateVisual(PortableGuidelineSet state)
        {
            var group = new FakeDrawingGroup(
                new PortableDrawingGroupState
                {
                    HasGuidelineSet = true,
                    GuidelineSet = new FakeGuidelineSet(state)
                },
                []);
            return new FakeVisual(
                new FakeRenderData(CreateDrawDrawingRecord(1), [group]));
        }

        WpfNativeMilBatch dynamicResult =
            new WpfNativeMilSceneCompiler().BuildBatch(
                CreateVisual(new PortableGuidelineSet(
                    true, true, [], [2, 0])),
                64,
                64);
        int dynamicGuidelineOffset = FindCommand(
            dynamicResult.Bytes, 0x8c);
        Assert.Equal(40, ReadInt32(
            dynamicResult.Bytes, dynamicGuidelineOffset));
        Assert.Equal(0U, ReadUInt32(
            dynamicResult.Bytes, dynamicGuidelineOffset + 12));
        Assert.Equal(16U, ReadUInt32(
            dynamicResult.Bytes, dynamicGuidelineOffset + 16));
        Assert.Equal(1U, ReadUInt32(
            dynamicResult.Bytes, dynamicGuidelineOffset + 20));
        Assert.Equal(2, ReadDouble(
            dynamicResult.Bytes, dynamicGuidelineOffset + 24));
        Assert.Equal(0, ReadDouble(
            dynamicResult.Bytes, dynamicGuidelineOffset + 32));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            CreateVisual(new PortableGuidelineSet(
                true, false, [1, 2], [])),
            64,
            64);

        int guidelineOffset = FindCommand(result.Bytes, 0x8c);
        Assert.Equal(40, ReadInt32(result.Bytes, guidelineOffset));
        Assert.Equal(16U, ReadUInt32(result.Bytes, guidelineOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, guidelineOffset + 16));
        Assert.Equal(1, ReadDouble(result.Bytes, guidelineOffset + 24));
        Assert.Equal(2, ReadDouble(result.Bytes, guidelineOffset + 32));
    }

    [Fact]
    public void BuildBatchTranslatesTypedSolidDrawingGroupOpacityMask()
    {
        var opacityMask = new FakeBrush(
            PortableBrush.SolidColor(
                new PortableColor(128, 255, 255, 255),
                opacity: 0.5));
        var childBrush = new FakeBrush(
            new PortableColor(255, 32, 96, 192));
        var geometry = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                0,
                0,
                PortableMatrix3x2.Identity));
        var child = new FakeGeometryDrawing(childBrush, null, geometry);
        var group = new FakeDrawingGroup(
            new PortableDrawingGroupState
            {
                HasOpacityMask = true,
                OpacityMask = opacityMask
            },
            [child]);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [group]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int groupOffset = FindCommand(result.Bytes, 0x8b);
        uint opacityMaskHandle = ReadUInt32(result.Bytes, groupOffset + 32);
        Assert.NotEqual(0U, opacityMaskHandle);
        int maskOffset = FindCommand(result.Bytes, 0x7e);
        Assert.Equal(
            opacityMaskHandle,
            ReadUInt32(result.Bytes, maskOffset + 8));
        Assert.Equal(0.5, ReadDouble(result.Bytes, maskOffset + 12));
        Assert.Equal(128F / 255F, ReadSingle(result.Bytes, maskOffset + 32));
    }

    [Fact]
    public void BuildBatchTranslatesGradientDrawingGroupOpacityMaskWithLocalBounds()
    {
        var opacityMask = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(1, 0),
            [
                new PortableGradientStop(
                    new PortableColor(0, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 1)
            ]));
        var group = new FakeDrawingGroup(
            new PortableDrawingGroupState
            {
                HasLocalBounds = true,
                LocalBounds = new PortableRect(2, 3, 20, 12),
                HasOpacityMask = true,
                OpacityMask = opacityMask
            },
            []);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [group]));

        WpfNativeMilBatch result =
            new WpfNativeMilSceneCompiler().BuildBatch(visual, 64, 64);

        int groupOffset = FindCommand(result.Bytes, 0x8b);
        Assert.NotEqual(0U, ReadUInt32(result.Bytes, groupOffset + 32));
        WpfNativeMilDrawingGroupBounds bounds = Assert.Single(
            result.DrawingGroupBounds!);
        Assert.Equal(
            new NativeMilRect(2, 3, 20, 12),
            bounds.Bounds);
    }

    [Fact]
    public void BuildBatchRejectsGradientDrawingGroupOpacityMaskWithoutLocalBounds()
    {
        var opacityMask = new FakeBrush(PortableBrush.LinearGradient(
            new PortablePoint(0, 0),
            new PortablePoint(1, 0),
            [
                new PortableGradientStop(
                    new PortableColor(0, 255, 255, 255), 0),
                new PortableGradientStop(
                    new PortableColor(255, 255, 255, 255), 1)
            ]));
        var group = new FakeDrawingGroup(
            new PortableDrawingGroupState
            {
                HasOpacityMask = true,
                OpacityMask = opacityMask
            },
            []);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [group]));

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => new WpfNativeMilSceneCompiler().BuildBatch(
                visual, 64, 64));

        Assert.Contains("local content bounds", exception.Message);
    }

    [Fact]
    public void BuildBatchRejectsLegacyObjectBitmapScalingMode()
    {
        var group = new FakeDrawingGroup(
            new PortableDrawingGroupState
            {
                HasBitmapScalingMode = true,
                BitmapScalingMode = "NearestNeighbor"
            },
            []);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [group]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(nameof(PortableBitmapScalingMode), exception.Message);
    }

    [Theory]
    [InlineData(0, nameof(PortableBitmapScalingMode))]
    [InlineData(1, nameof(PortableEdgeMode))]
    [InlineData(2, nameof(PortableClearTypeHint))]
    [InlineData(3, nameof(PortableTextRenderingMode))]
    [InlineData(4, nameof(PortableTextHintingMode))]
    public void BuildBatchRejectsLegacyObjectVisualRenderOptions(
        int option,
        string expectedContract)
    {
        var state = new PortableVisualState();
        if (option == 0)
        {
            state.HasBitmapScalingMode = true;
            state.BitmapScalingMode = "NearestNeighbor";
        }
        else if (option == 1)
        {
            state.HasEdgeMode = true;
            state.EdgeMode = "Aliased";
        }
        else if (option == 2)
        {
            state.HasClearTypeHint = true;
            state.ClearTypeHint = "Enabled";
        }
        else if (option == 3)
        {
            state.HasTextRenderingMode = true;
            state.TextRenderingMode = "ClearType";
        }
        else
        {
            state.HasTextHintingMode = true;
            state.TextHintingMode = "Fixed";
        }
        var visual = new FakeVisual(null, state);

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(expectedContract, exception.Message);
    }

    [Fact]
    public void BuildBatchRejectsLegacyObjectEdgeMode()
    {
        var group = new FakeDrawingGroup(
            new PortableDrawingGroupState
            {
                HasEdgeMode = true,
                EdgeMode = "Aliased"
            },
            []);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [group]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(nameof(PortableEdgeMode), exception.Message);
    }

    [Fact]
    public void BuildBatchRejectsLegacyObjectClearTypeHint()
    {
        var group = new FakeDrawingGroup(
            new PortableDrawingGroupState
            {
                HasClearTypeHint = true,
                ClearTypeHint = "Enabled"
            },
            []);
        var visual = new FakeVisual(
            new FakeRenderData(CreateDrawDrawingRecord(1), [group]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            nameof(PortableClearTypeHint),
            exception.Message);
    }

    [Fact]
    public void BuildBatchRejectsUntypedLineGeometry()
    {
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawGeometryRecord(0, 0, 1),
                [new object()]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            nameof(IPortablePrimitiveGeometrySource),
            exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedRectangleAndEllipseGeometry()
    {
        var rectangle = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                0,
                2,
                new PortableMatrix3x2(2, 0, 0, 3, 11, 13)));
        var ellipse = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Ellipse(
                new PortablePoint(8, 9),
                6,
                7,
                PortableMatrix3x2.Identity));
        byte[] renderData = CreateDrawGeometryRecord(0, 0, 1)
            .Concat(CreateDrawGeometryRecord(0, 0, 2))
            .ToArray();
        var visual = new FakeVisual(
            new FakeRenderData(renderData, [rectangle, ellipse]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int rectangleOffset = FindCommand(result.Bytes, 0x79);
        Assert.Equal(3U, ReadUInt32(result.Bytes, rectangleOffset + 8));
        Assert.Equal(0.0, ReadDouble(result.Bytes, rectangleOffset + 12));
        Assert.Equal(2.0, ReadDouble(result.Bytes, rectangleOffset + 20));
        Assert.Equal(2.0, ReadDouble(result.Bytes, rectangleOffset + 28));
        Assert.Equal(3.0, ReadDouble(result.Bytes, rectangleOffset + 36));
        Assert.Equal(20.0, ReadDouble(result.Bytes, rectangleOffset + 44));
        Assert.Equal(12.0, ReadDouble(result.Bytes, rectangleOffset + 52));
        Assert.Equal(2U, ReadUInt32(result.Bytes, rectangleOffset + 60));

        int ellipseOffset = FindCommand(result.Bytes, 0x7a);
        Assert.Equal(4U, ReadUInt32(result.Bytes, ellipseOffset + 8));
        Assert.Equal(6.0, ReadDouble(result.Bytes, ellipseOffset + 12));
        Assert.Equal(7.0, ReadDouble(result.Bytes, ellipseOffset + 20));
        Assert.Equal(8.0, ReadDouble(result.Bytes, ellipseOffset + 28));
        Assert.Equal(9.0, ReadDouble(result.Bytes, ellipseOffset + 36));
        Assert.Equal(0U, ReadUInt32(result.Bytes, ellipseOffset + 44));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 16));
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 40));
    }

    [Fact]
    public void BuildBatchTranslatesTypedGeneralPathGeometry()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 96, 192));
        var geometry = new FakeGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = PortableFillRule.EvenOdd,
            Transform = new PortableMatrix3x2(2, 0, 0, 3, 11, 13),
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(1, 2),
                    IsClosed = true,
                    IsFilled = true,
                    Segments =
                    [
                        PortablePathSegment.Line(
                            new PortablePoint(9, 2),
                            isSmoothJoin: false,
                            isStroked: true),
                        PortablePathSegment.QuadraticBezier(
                            new PortablePoint(9, 8),
                            new PortablePoint(5, 8),
                            isSmoothJoin: true,
                            isStroked: true),
                        PortablePathSegment.CubicBezier(
                            new PortablePoint(3, 8),
                            new PortablePoint(1, 6),
                            new PortablePoint(1, 2),
                            isSmoothJoin: true,
                            isStroked: true)
                    ]
                }
            ]
        });
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawGeometryRecord(1, 0, 2),
                [brush, geometry]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int geometryOffset = FindCommand(result.Bytes, 0x7d);
        Assert.Equal(4U, ReadUInt32(result.Bytes, geometryOffset + 8));
        Assert.Equal(3U, ReadUInt32(result.Bytes, geometryOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, geometryOffset + 16));
        Assert.Equal(232U, ReadUInt32(result.Bytes, geometryOffset + 20));
        Assert.Equal(3U, ReadUInt32(result.Bytes, geometryOffset + 28));
        Assert.Equal(1.0, ReadDouble(result.Bytes, geometryOffset + 32));
        Assert.Equal(2.0, ReadDouble(result.Bytes, geometryOffset + 40));
        Assert.Equal(9.0, ReadDouble(result.Bytes, geometryOffset + 48));
        Assert.Equal(8.0, ReadDouble(result.Bytes, geometryOffset + 56));
        Assert.Equal(14U, ReadUInt32(result.Bytes, geometryOffset + 76));
        Assert.Equal(3U, ReadUInt32(result.Bytes, geometryOffset + 80));
        Assert.Equal(1U, ReadUInt32(result.Bytes, geometryOffset + 112));
        Assert.Equal(3U, ReadUInt32(result.Bytes, geometryOffset + 144));
        Assert.Equal(2U, ReadUInt32(result.Bytes, geometryOffset + 192));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 12));
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 16));
    }

    [Fact]
    public void BuildBatchTranslatesTypedPathArcRecord()
    {
        var brush = new FakeBrush(new PortableColor(255, 192, 96, 32));
        var geometry = new FakeGeometry(new PortableGeometryPath
        {
            Kind = PortableGeometryPathKind.Path,
            FillRule = PortableFillRule.Nonzero,
            Figures =
            [
                new PortablePathFigure
                {
                    StartPoint = new PortablePoint(0, 5),
                    IsClosed = true,
                    IsFilled = true,
                    Segments =
                    [
                        PortablePathSegment.Arc(
                            new PortablePoint(10, 5),
                            new PortableSize(5, 5),
                            rotationAngle: 30,
                            isLargeArc: false,
                            sweepDirection:
                                PortableSweepDirection.Clockwise,
                            isSmoothJoin: true,
                            isStroked: true),
                        PortablePathSegment.Line(
                            new PortablePoint(0, 5),
                            isSmoothJoin: false,
                            isStroked: true)
                    ]
                }
            ]
        });
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateDrawGeometryRecord(1, 0, 2),
                [brush, geometry]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int geometryOffset = FindCommand(result.Bytes, 0x7d);
        Assert.Equal(3U, ReadUInt32(result.Bytes, geometryOffset + 8));
        Assert.Equal(0U, ReadUInt32(result.Bytes, geometryOffset + 12));
        Assert.Equal(1U, ReadUInt32(result.Bytes, geometryOffset + 16));
        Assert.Equal(184U, ReadUInt32(result.Bytes, geometryOffset + 20));
        Assert.Equal(4U, ReadUInt32(result.Bytes, geometryOffset + 112));
        Assert.Equal(0U, ReadUInt32(result.Bytes, geometryOffset + 120));
        Assert.Equal(0U, ReadUInt32(result.Bytes, geometryOffset + 124));
        Assert.Equal(10.0, ReadDouble(result.Bytes, geometryOffset + 128));
        Assert.Equal(5.0, ReadDouble(result.Bytes, geometryOffset + 136));
        Assert.Equal(5.0, ReadDouble(result.Bytes, geometryOffset + 144));
        Assert.Equal(5.0, ReadDouble(result.Bytes, geometryOffset + 152));
        Assert.Equal(30.0, ReadDouble(result.Bytes, geometryOffset + 160));
        Assert.Equal(1U, ReadUInt32(result.Bytes, geometryOffset + 168));
        Assert.Equal(0U, ReadUInt32(result.Bytes, geometryOffset + 172));
        Assert.Equal(1U, ReadUInt32(result.Bytes, geometryOffset + 176));
        Assert.Equal(64U, ReadUInt32(result.Bytes, geometryOffset + 184));
    }

    [Fact]
    public void BuildBatchTranslatesBalancedOpacityScopes()
    {
        var brush = new FakeBrush(new PortableColor(255, 0, 128, 255));
        byte[] renderData = CreatePushOpacityRecord(0.5)
            .Concat(CreateRectangleRecord(1, 0))
            .Concat(CreatePopRecord())
            .ToArray();
        var visual = new FakeVisual(new FakeRenderData(renderData, [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(16, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x4f, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0.5, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(48, ReadInt32(result.Bytes, nestedOffset + 16));
        Assert.Equal(0x40, ReadInt32(result.Bytes, nestedOffset + 20));
        Assert.Equal(8, ReadInt32(result.Bytes, nestedOffset + 64));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 68));
    }

    [Fact]
    public void BuildBatchTranslatesTypedAnimatedOpacityScope()
    {
        var animation = new FakeDoubleAnimationValue(0.625);
        byte[] renderData = CreatePushOpacityAnimateRecord(0.25, 1)
            .Concat(CreatePopRecord())
            .ToArray();
        var visual = new FakeVisual(
            new FakeRenderData(renderData, [animation]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int animationOffset = FindCommand(result.Bytes, 0x0e);
        uint animationHandle = ReadUInt32(result.Bytes, animationOffset + 8);
        Assert.Equal(0.625, ReadDouble(result.Bytes, animationOffset + 12));
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;
        Assert.Equal(24, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x50, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0.25, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(
            animationHandle,
            ReadUInt32(result.Bytes, nestedOffset + 16));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 20));
        Assert.Equal(8, ReadInt32(result.Bytes, nestedOffset + 24));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 28));
    }

    [Fact]
    public void BuildBatchRejectsUntypedAnimatedOpacity()
    {
        var visual = new FakeVisual(
            new FakeRenderData(
                CreatePushOpacityAnimateRecord(0.25, 1)
                    .Concat(CreatePopRecord())
                    .ToArray(),
                [new object()]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            nameof(IPortableDoubleAnimationValueSource),
            exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedAnimatedDrawFamily()
    {
        var point0 = new FakePointAnimationValue(new PortablePoint(2, 3));
        var point1 = new FakePointAnimationValue(new PortablePoint(8, 9));
        var rectangle = new FakeRectAnimationValue(
            new PortableRect(4, 5, 30, 20));
        var radiusX = new FakeDoubleAnimationValue(6);
        var radiusY = new FakeDoubleAnimationValue(7);
        var bitmap = new FakeBitmapSource(new PortableBitmapSourcePixels(
            1,
            1,
            96,
            96,
            4,
            PortablePixelDataFormat.Pbgra32,
            [0, 0, 255, 255]));
        byte[] renderData = CreateAnimatedLineRecord(0, 1, 2)
            .Concat(CreateAnimatedRectangleRecord(0, 0, 3))
            .Concat(CreateAnimatedRoundedRectangleRecord(0, 0, 3, 4, 5))
            .Concat(CreateAnimatedEllipseRecord(0, 0, 1, 4, 5))
            .Concat(CreateAnimatedImageRecord(6, 3))
            .ToArray();
        var visual = new FakeVisual(new FakeRenderData(
            renderData,
            [point0, point1, rectangle, radiusX, radiusY, bitmap]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);

        int firstPointOffset = FindCommand(result.Bytes, 0x10);
        uint point0Handle = ReadUInt32(result.Bytes, firstPointOffset + 8);
        int rectOffset = FindCommand(result.Bytes, 0x11);
        uint rectHandle = ReadUInt32(result.Bytes, rectOffset + 8);
        int doubleOffset = FindCommand(result.Bytes, 0x0e);
        uint radiusXHandle = ReadUInt32(result.Bytes, doubleOffset + 8);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(0x3f, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(point0Handle, ReadUInt32(result.Bytes, nestedOffset + 44));
        Assert.NotEqual(0U, ReadUInt32(result.Bytes, nestedOffset + 48));
        nestedOffset += 56;
        Assert.Equal(0x41, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(rectHandle, ReadUInt32(result.Bytes, nestedOffset + 48));
        nestedOffset += 56;
        Assert.Equal(0x43, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(rectHandle, ReadUInt32(result.Bytes, nestedOffset + 64));
        Assert.Equal(radiusXHandle, ReadUInt32(result.Bytes, nestedOffset + 68));
        Assert.NotEqual(0U, ReadUInt32(result.Bytes, nestedOffset + 72));
        nestedOffset += 80;
        Assert.Equal(0x45, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(point0Handle, ReadUInt32(result.Bytes, nestedOffset + 48));
        Assert.Equal(radiusXHandle, ReadUInt32(result.Bytes, nestedOffset + 52));
        Assert.NotEqual(0U, ReadUInt32(result.Bytes, nestedOffset + 56));
        nestedOffset += 64;
        Assert.Equal(0x48, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.NotEqual(0U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(rectHandle, ReadUInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchRejectsUntypedAnimatedDrawValues()
    {
        var visual = new FakeVisual(new FakeRenderData(
            CreateAnimatedRectangleRecord(0, 0, 1),
            [new object()]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains(
            nameof(IPortableRectAnimationValueSource),
            exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedClipAndOpacityMaskScopes()
    {
        var geometry = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(2, 3, 20, 12),
                0,
                0,
                PortableMatrix3x2.Identity));
        var mask = new FakeBrush(new PortableColor(255, 255, 255, 255));
        byte[] renderData = CreatePushClipRecord(1)
            .Concat(CreatePushOpacityMaskRecord(4, 5, 30, 24, 2))
            .Concat(CreatePopRecord())
            .Concat(CreatePopRecord())
            .ToArray();
        var visual = new FakeVisual(
            new FakeRenderData(renderData, [geometry, mask]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(16, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x4d, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.NotEqual(0U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(32, ReadInt32(result.Bytes, nestedOffset + 16));
        Assert.Equal(0x4e, ReadInt32(result.Bytes, nestedOffset + 20));
        Assert.Equal(4f, ReadSingle(result.Bytes, nestedOffset + 24));
        Assert.Equal(5f, ReadSingle(result.Bytes, nestedOffset + 28));
        Assert.Equal(34f, ReadSingle(result.Bytes, nestedOffset + 32));
        Assert.Equal(29f, ReadSingle(result.Bytes, nestedOffset + 36));
        Assert.NotEqual(0U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(8, ReadInt32(result.Bytes, nestedOffset + 48));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 52));
        Assert.Equal(8, ReadInt32(result.Bytes, nestedOffset + 56));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 60));
    }

    [Fact]
    public void BuildBatchKeepsNullClipAndMaskScopesBalancedAsNoOps()
    {
        byte[] renderData = CreatePushClipRecord(0)
            .Concat(CreatePopRecord())
            .Concat(CreatePushOpacityMaskRecord(0, 0, 0, 0, 0))
            .Concat(CreatePopRecord())
            .ToArray();
        var visual = new FakeVisual(new FakeRenderData(renderData, []));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(0x51, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 20));
        Assert.Equal(0x51, ReadInt32(result.Bytes, nestedOffset + 28));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 32));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchTranslatesTypedStaticGuidelineScope()
    {
        var guidelines = new FakeGuidelineSet(
            new PortableGuidelineSet(
                isFrozen: true,
                isDynamic: false,
                [3.5, 12.25],
                [7.75]));
        byte[] renderData = CreatePushGuidelineSetRecord(1)
            .Concat(CreatePopRecord())
            .ToArray();
        var visual = new FakeVisual(
            new FakeRenderData(renderData, [guidelines]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(16, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x52, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.NotEqual(0U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(8, ReadInt32(result.Bytes, nestedOffset + 16));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 20));
    }

    [Fact]
    public void BuildBatchTranslatesTypedDynamicGuidelineScope()
    {
        var guidelines = new FakeGuidelineSet(
            new PortableGuidelineSet(
                isFrozen: false,
                isDynamic: true,
                [3.5, 0],
                [7.75, 0]));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreatePushGuidelineSetRecord(1)
                    .Concat(CreatePopRecord())
                    .ToArray(),
                [guidelines]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int guidelineOffset = FindCommand(result.Bytes, 0x8c);
        Assert.Equal(1U, ReadUInt32(result.Bytes, guidelineOffset + 20));
        Assert.Equal(16U, ReadUInt32(result.Bytes, guidelineOffset + 12));
        Assert.Equal(16U, ReadUInt32(result.Bytes, guidelineOffset + 16));

        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;
        Assert.Equal(0x52, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.NotEqual(0U, ReadUInt32(result.Bytes, nestedOffset + 8));
    }

    [Fact]
    public void BuildBatchTranslatesCompactDynamicGuidelineScopes()
    {
        byte[] renderData = CreatePushGuidelineY1Record(1.25)
            .Concat(CreatePopRecord())
            .Concat(CreatePushGuidelineY2Record(2.5, -0.75))
            .Concat(CreatePopRecord())
            .ToArray();
        var visual = new FakeVisual(new FakeRenderData(renderData, []));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(16, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x53, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(1.25, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(8, ReadInt32(result.Bytes, nestedOffset + 16));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 20));
        Assert.Equal(24, ReadInt32(result.Bytes, nestedOffset + 24));
        Assert.Equal(0x54, ReadInt32(result.Bytes, nestedOffset + 28));
        Assert.Equal(2.5, ReadDouble(result.Bytes, nestedOffset + 32));
        Assert.Equal(-0.75, ReadDouble(result.Bytes, nestedOffset + 40));
        Assert.Equal(8, ReadInt32(result.Bytes, nestedOffset + 48));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 52));
    }

    [Fact]
    public void BuildBatchLowersLegacyPushEffectToBalancedIdentityScope()
    {
        byte[] renderData = CreatePushEffectRecord(17, 23)
            .Concat(CreatePopRecord())
            .ToArray();
        var visual = new FakeVisual(new FakeRenderData(renderData, []));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(16, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x51, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(8, ReadInt32(result.Bytes, nestedOffset + 16));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 20));
    }

    [Fact]
    public void BuildBatchFailsClosedForUnbalancedOpacityScope()
    {
        var visual = new FakeVisual(
            new FakeRenderData(CreatePushOpacityRecord(0.5), []));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => new WpfNativeMilSceneCompiler().BuildBatch(
                    visual, 64, 64));

        Assert.Contains("stack is unbalanced", exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesEllipseWithNativeBrushHandle()
    {
        var brush = new FakeBrush(new PortableColor(255, 0, 255, 64));
        var visual = new FakeVisual(
            new FakeRenderData(CreateEllipseRecord(1, 0), [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(48, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x44, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(5.0, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(9.0, ReadDouble(result.Bytes, nestedOffset + 16));
        Assert.Equal(7.0, ReadDouble(result.Bytes, nestedOffset + 24));
        Assert.Equal(11.0, ReadDouble(result.Bytes, nestedOffset + 32));
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchPreservesPenOnlyEllipse()
    {
        var pen = new FakePen(
            new PortableColor(255, 64, 128, 255),
            3,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Miter,
            10,
            []);
        var visual = new FakeVisual(
            new FakeRenderData(CreateEllipseRecord(0, 1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchTranslatesUniformRoundedRectangle()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRoundedRectangleRecord(4, 4, 1, 0), [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;

        Assert.Equal(64, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x42, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(4.0, ReadDouble(result.Bytes, nestedOffset + 40));
        Assert.Equal(4.0, ReadDouble(result.Bytes, nestedOffset + 48));
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 56));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 60));
    }

    [Fact]
    public void BuildBatchPreservesPenOnlyRoundedRectangle()
    {
        var pen = new FakePen(
            new PortableColor(255, 64, 128, 255),
            2,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Round,
            10,
            []);
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRoundedRectangleRecord(4, 4, 0, 1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 56));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 60));
    }

    [Fact]
    public void BuildBatchTranslatesNonUniformRoundedRectangle()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRoundedRectangleRecord(4, 6, 1, 0), [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(4.0, ReadDouble(result.Bytes, nestedOffset + 40));
        Assert.Equal(6.0, ReadDouble(result.Bytes, nestedOffset + 48));
    }

    [Fact]
    public void BuildBatchTranslatesZeroAxisAsymmetricRoundedRectangle()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        var visual = new FakeVisual(
            new FakeRenderData(
                CreateRoundedRectangleRecord(0, 6, 1, 0), [brush]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0.0, ReadDouble(result.Bytes, nestedOffset + 40));
        Assert.Equal(6.0, ReadDouble(result.Bytes, nestedOffset + 48));
    }

    [Fact]
    public void BuildBatchFailsClosedForDegenerateZeroAxisAsymmetricRoundedRectangle()
    {
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        byte[] record = CreateRoundedRectangleRecord(0, 6, 1, 0);
        WriteDouble(record, 24, 0);
        var visual = new FakeVisual(new FakeRenderData(record, [brush]));

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => new WpfNativeMilSceneCompiler().BuildBatch(visual, 64, 64));

        Assert.Contains("degenerate zero-axis asymmetric", exception.Message);
    }

    [Fact]
    public void BuildBatchReusesTypedMatrixForVisualAndNestedTransform()
    {
        var transform = new FakeTransform(
            new PortableMatrix3x2(2, 0.5, -0.25, 3, 11, 13));
        var brush = new FakeBrush(new PortableColor(255, 32, 64, 128));
        byte[] renderData = CreatePushTransformRecord(1)
            .Concat(CreateRectangleRecord(2, 0))
            .Concat(CreatePopRecord())
            .ToArray();
        var state = new PortableVisualState
        {
            HasOffset = true,
            Offset = new PortablePoint(5, 7),
            HasTransform = true,
            Transform = transform,
            HasOpacity = true,
            Opacity = 0.75
        };
        var visual = new FakeVisual(
            new FakeRenderData(renderData, [transform, brush]),
            state);

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        Assert.Equal(5U, result.TargetHandle);
        Assert.Equal(1, ReadCommands(result.Bytes).Count(
            static command => command == 0x77));

        int matrixOffset = FindCommand(result.Bytes, 0x77);
        Assert.Equal(64, ReadInt32(result.Bytes, matrixOffset));
        Assert.Equal(2U, ReadUInt32(result.Bytes, matrixOffset + 8));
        Assert.Equal(2.0, ReadDouble(result.Bytes, matrixOffset + 12));
        Assert.Equal(0.5, ReadDouble(result.Bytes, matrixOffset + 20));
        Assert.Equal(-0.25, ReadDouble(result.Bytes, matrixOffset + 28));
        Assert.Equal(3.0, ReadDouble(result.Bytes, matrixOffset + 36));
        Assert.Equal(11.0, ReadDouble(result.Bytes, matrixOffset + 44));
        Assert.Equal(13.0, ReadDouble(result.Bytes, matrixOffset + 52));
        Assert.Equal(0U, ReadUInt32(result.Bytes, matrixOffset + 60));

        int visualTransformOffset = FindCommand(result.Bytes, 0x1c);
        Assert.Equal(1U, ReadUInt32(result.Bytes, visualTransformOffset + 8));
        Assert.Equal(2U, ReadUInt32(result.Bytes, visualTransformOffset + 12));

        int renderDataOffset = FindCommand(result.Bytes, 0x18);
        int nestedOffset = renderDataOffset + 16;
        Assert.Equal(16, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x51, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(2U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 12));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 56));
    }

    [Fact]
    public void BuildBatchPreservesNullTransformAsBalancedNoOpScope()
    {
        byte[] renderData = CreatePushTransformRecord(0)
            .Concat(CreatePopRecord())
            .ToArray();
        var visual = new FakeVisual(new FakeRenderData(renderData, []));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 16, 16);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0x51, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 8));
        Assert.Equal(0x56, ReadInt32(result.Bytes, nestedOffset + 20));
    }

    [Fact]
    public void BuildBatchRejectsTransformWithoutTypedPortableContract()
    {
        var state = new PortableVisualState
        {
            HasTransform = true,
            Transform = new object()
        };
        var visual = new FakeVisual(content: null, state: state);

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(visual, 16, 16));

        Assert.Contains(nameof(IPortableTransformMatrixSource), exception.Message);
    }

    [Fact]
    public void BuildBatchTranslatesTypedSolidPenLine()
    {
        var pen = new FakePen(
            new PortableColor(255, 32, 96, 192),
            2.5,
            PortablePenLineCap.Square,
            PortablePenLineCap.Round,
            PortablePenLineCap.Triangle,
            PortablePenLineJoin.Bevel,
            7,
            []);
        var visual = new FakeVisual(
            new FakeRenderData(CreateLineRecord(1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 64, 64);
        Assert.Equal(5U, result.TargetHandle);

        int penOffset = FindCommand(result.Bytes, 0x86);
        Assert.Equal(56, ReadInt32(result.Bytes, penOffset));
        Assert.Equal(3U, ReadUInt32(result.Bytes, penOffset + 8));
        Assert.Equal(2.5, ReadDouble(result.Bytes, penOffset + 12));
        Assert.Equal(7.0, ReadDouble(result.Bytes, penOffset + 20));
        Assert.Equal(2U, ReadUInt32(result.Bytes, penOffset + 28));
        Assert.Equal(0U, ReadUInt32(result.Bytes, penOffset + 32));
        Assert.Equal(1U, ReadUInt32(result.Bytes, penOffset + 36));
        Assert.Equal(2U, ReadUInt32(result.Bytes, penOffset + 40));
        Assert.Equal(3U, ReadUInt32(result.Bytes, penOffset + 44));
        Assert.Equal(1U, ReadUInt32(result.Bytes, penOffset + 48));
        Assert.Equal(0U, ReadUInt32(result.Bytes, penOffset + 52));

        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(48, ReadInt32(result.Bytes, nestedOffset));
        Assert.Equal(0x3e, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(1.0, ReadDouble(result.Bytes, nestedOffset + 8));
        Assert.Equal(2.0, ReadDouble(result.Bytes, nestedOffset + 16));
        Assert.Equal(5.0, ReadDouble(result.Bytes, nestedOffset + 24));
        Assert.Equal(8.0, ReadDouble(result.Bytes, nestedOffset + 32));
        Assert.Equal(3U, ReadUInt32(result.Bytes, nestedOffset + 40));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 44));
    }

    [Fact]
    public void BuildBatchPreservesNullPenLineAsNoOpCommand()
    {
        var visual = new FakeVisual(
            new FakeRenderData(CreateLineRecord(0), []));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 16, 16);
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;

        Assert.Equal(0x3e, ReadInt32(result.Bytes, nestedOffset + 4));
        Assert.Equal(0U, ReadUInt32(result.Bytes, nestedOffset + 40));
    }

    [Fact]
    public void BuildBatchTranslatesTypedDashedLinePen()
    {
        var pen = new FakePen(
            new PortableColor(255, 255, 255, 255),
            1,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Miter,
            10,
            [2, 1]);
        var visual = new FakeVisual(
            new FakeRenderData(CreateLineRecord(1), [pen]));

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 16, 16);

        int dashOffset = FindCommand(result.Bytes, 0x85);
        Assert.Equal(44, ReadInt32(result.Bytes, dashOffset));
        Assert.Equal(3U, ReadUInt32(result.Bytes, dashOffset + 8));
        Assert.Equal(0.0, ReadDouble(result.Bytes, dashOffset + 12));
        Assert.Equal(0U, ReadUInt32(result.Bytes, dashOffset + 20));
        Assert.Equal(16U, ReadUInt32(result.Bytes, dashOffset + 24));
        Assert.Equal(2.0, ReadDouble(result.Bytes, dashOffset + 28));
        Assert.Equal(1.0, ReadDouble(result.Bytes, dashOffset + 36));

        int penOffset = FindCommand(result.Bytes, 0x86);
        Assert.Equal(4U, ReadUInt32(result.Bytes, penOffset + 8));
        Assert.Equal(3U, ReadUInt32(result.Bytes, penOffset + 52));
        int nestedOffset = FindCommand(result.Bytes, 0x18) + 16;
        Assert.Equal(4U, ReadUInt32(result.Bytes, nestedOffset + 40));
    }

    [Fact]
    public void BuildBatchRejectsNegativeDashInterval()
    {
        var pen = new FakePen(
            new PortableColor(255, 255, 255, 255),
            1,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Flat,
            PortablePenLineCap.Square,
            PortablePenLineJoin.Miter,
            10,
            [2, -1]);
        var visual = new FakeVisual(
            new FakeRenderData(CreateLineRecord(1), [pen]));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WpfNativeMilSceneCompiler().BuildBatch(visual, 16, 16));
    }

    [Fact]
    public void BuildBatchRejectsLinePenWithoutTypedPortableContract()
    {
        var visual = new FakeVisual(
            new FakeRenderData(CreateLineRecord(1), [new object()]));

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() =>
                new WpfNativeMilSceneCompiler().BuildBatch(visual, 16, 16));

        Assert.Contains(nameof(IPortablePenSource), exception.Message);
    }

    [Fact]
    public void BuildBatchFlattensTypedViewport3DIntoNativeSideband()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        var visual = new FakeViewport3DVisual(scene);

        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(
            visual, 160, 120);

        Assert.Equal(
            [0x07, 0x1a, 0x1b, 0x20, 0x07, 0x34, 0x36, 0x35],
            ReadCommands(result.Bytes));
        Assert.Equal(
            (uint)NativeMilResourceType.Viewport3DVisual,
            ReadUInt32(result.Bytes, 12));
        WpfNativeMilViewport3DScene retained = Assert.Single(
            result.Viewport3DScenes!);
        Assert.Equal(1U, retained.Handle);
        Assert.Equal(12.0f, retained.Scene.Viewport.X);
        Assert.Equal(18.0f, retained.Scene.Viewport.Y);
        Assert.Equal(80.0f, retained.Scene.Viewport.Width);
        Assert.Equal(60.0f, retained.Scene.Viewport.Height);
        NativeSceneMesh3D mesh = Assert.Single(retained.Scene.Meshes);
        Assert.Equal((uint)NativeMesh3DFlags.FrontFace, mesh.Flags);
        Assert.Equal(3U, mesh.VertexCount);
        Assert.Equal(3U, mesh.IndexCount);
        Assert.Equal(1U, mesh.ShadingMode);
        Assert.Equal(3, retained.Scene.Vertices.Length);
        Assert.Equal([0U, 1U, 2U], retained.Scene.Indices);
        Assert.Equal(2.0f, retained.Scene.Camera.CameraPosition.Z);
        Assert.Equal(0.0f, retained.Scene.Vertices[0].Normal.X);
        Assert.Equal(0.0f, retained.Scene.Vertices[0].Normal.Y);
        Assert.Equal(1.0f, retained.Scene.Vertices[0].Normal.Z);
        Assert.Equal(0.0f, retained.Scene.Vertices[1].Normal.X);
        Assert.Equal(0.0f, retained.Scene.Vertices[1].Normal.Y);
        Assert.Equal(0.0f, retained.Scene.Vertices[1].Normal.Z);
        Assert.Equal(0.25f, retained.Scene.Vertices[0].TextureCoordinate.X);
        Assert.Equal(0.75f, retained.Scene.Vertices[0].TextureCoordinate.Y);
        Assert.Equal(Vector2.Zero,
            retained.Scene.Vertices[2].TextureCoordinate);
    }

    [Fact]
    public void BuildBatchPreservesViewport3DBackMaterialFaceMode()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        scene.Meshes[0].IsBackFace = true;

        WpfNativeMilBatch result =
            new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120);

        WpfNativeMilViewport3DScene retained = Assert.Single(
            result.Viewport3DScenes!);
        Assert.Equal(
            (uint)NativeMesh3DFlags.BackFace,
            Assert.Single(retained.Scene.Meshes).Flags);
    }

    [Fact]
    public void BuildBatchRejectsNonFiniteViewport3DNormal()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        scene.Meshes[0].Normals[0] =
            new PortableVector3(double.NaN, 0, 1);

        Assert.Throws<NotSupportedException>(() =>
            new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120));
    }

    [Fact]
    public void BuildBatchExpandsOrderedSolidViewport3DMaterialPasses()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        scene.Meshes[0].Materials =
        [
            new PortableViewport3DMaterial
            {
                Kind = PortableViewport3DMaterialKind.Diffuse,
                Brush = PortableBrush.SolidColor(
                    new PortableColor(255, 255, 0, 0)),
                Color = new PortableColor4(1, 1, 1, 1),
                AmbientColor = new PortableVector3(0.1, 0.2, 0.3)
            },
            new PortableViewport3DMaterial
            {
                Kind = PortableViewport3DMaterialKind.Specular,
                Brush = PortableBrush.SolidColor(
                    new PortableColor(255, 255, 255, 255)),
                Color = new PortableColor4(0.5, 0.25, 0.125, 1),
                SpecularPower = 12
            },
            new PortableViewport3DMaterial
            {
                Kind = PortableViewport3DMaterialKind.Emissive,
                Brush = PortableBrush.SolidColor(
                    new PortableColor(128, 0, 255, 0)),
                Color = new PortableColor4(1, 1, 1, 0.5)
            }
        ];

        NativeMilViewport3DScene retained = Assert.Single(
            new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120)
                .Viewport3DScenes!).Scene;

        Assert.Equal(3, retained.Meshes.Length);
        NativeSceneMesh3D diffuse = retained.Meshes[0];
        NativeSceneMesh3D specular = retained.Meshes[1];
        NativeSceneMesh3D emissive = retained.Meshes[2];
        Assert.Equal(diffuse.VertexOffset, specular.VertexOffset);
        Assert.Equal(diffuse.IndexOffset, emissive.IndexOffset);
        Assert.Equal(new Vector4(1, 0, 0, 1), diffuse.Color);
        Assert.Equal(0.5f, specular.SpecularColor.X);
        Assert.Equal(12f, specular.SpecularColor.W);
        Assert.Equal(0U, emissive.ShadingMode);
        Assert.Equal(new Vector4(0, 1, 0, 1), emissive.Color);
        Assert.Equal(128f / 255f * 0.5f, emissive.Opacity);
        Assert.Empty(retained.Materials);
        Assert.Empty(retained.GradientStops);
    }

    [Fact]
    public void BuildBatchPreservesGradientViewport3DMaterialSideband()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        scene.Meshes[0].Materials =
        [
            new PortableViewport3DMaterial
            {
                Kind = PortableViewport3DMaterialKind.Diffuse,
                Brush = PortableBrush.LinearGradient(
                    new PortablePoint(0.1, 0.2),
                    new PortablePoint(0.9, 0.8),
                    [
                        new PortableGradientStop(
                            new PortableColor(255, 255, 0, 0), 0),
                        new PortableGradientStop(
                            new PortableColor(255, 0, 0, 255), 1)
                    ],
                    opacity: 0.75,
                    spreadMethod: PortableGradientSpreadMethod.Reflect,
                    colorInterpolationMode:
                        PortableGradientColorInterpolationMode
                            .ScRgbLinearInterpolation),
                Color = new PortableColor4(0.5, 0.75, 1, 0.8),
                AmbientColor = new PortableVector3(0.1, 0.2, 0.3)
            },
            new PortableViewport3DMaterial
            {
                Kind = PortableViewport3DMaterialKind.Emissive,
                Brush = PortableBrush.RadialGradient(
                    new PortablePoint(0.5, 0.5),
                    new PortablePoint(0.25, 0.75),
                    0.6,
                    0.4,
                    [
                        new PortableGradientStop(
                            new PortableColor(255, 0, 255, 0), 0),
                        new PortableGradientStop(
                            new PortableColor(128, 0, 0, 0), 1)
                    ],
                    opacity: 0.625,
                    mappingMode: PortableBrushMappingMode.Absolute,
                    spreadMethod: PortableGradientSpreadMethod.Repeat),
                Color = new PortableColor4(1, 1, 1, 0.5)
            }
        ];

        NativeMilViewport3DScene retained = Assert.Single(
            new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120)
                .Viewport3DScenes!).Scene;

        Assert.Equal(2, retained.Meshes.Length);
        Assert.Equal(new Vector4(0.5f, 0.75f, 1f, 1f),
            retained.Meshes[0].Color);
        Assert.Equal(0.8f, retained.Meshes[0].Opacity);
        Assert.Equal(0U, retained.Meshes[1].ShadingMode);
        Assert.Equal(0.5f, retained.Meshes[1].Opacity);
        Assert.Equal(2, retained.Materials.Length);
        NativeSceneBrush linear = retained.Materials[0];
        Assert.Equal(NativeSceneBrushKind.LinearGradient, linear.Kind);
        Assert.Equal(0.75f, linear.Opacity);
        Assert.Equal(new Vector2(0.1f, 0.2f), linear.StartPoint);
        Assert.Equal(new Vector2(0.9f, 0.8f), linear.EndPoint);
        Assert.Equal(NativeSceneGradientSpread.Reflect, linear.Spread);
        Assert.Equal(NativeSceneGradientInterpolation.ScRgb,
            linear.Interpolation);
        Assert.Equal(0U, linear.StopOffset);
        Assert.Equal(2U, linear.StopCount);
        NativeSceneBrush radial = retained.Materials[1];
        Assert.Equal(NativeSceneBrushKind.RadialGradient, radial.Kind);
        Assert.Equal(new Vector2(0.5f, 0.5f), radial.Center);
        Assert.Equal(new Vector2(0.25f, 0.75f), radial.StartPoint);
        Assert.Equal(0.6f, radial.Radius);
        Assert.Equal(0.4f, radial.RadiusY);
        Assert.Equal(NativeSceneGradientSpread.Repeat, radial.Spread);
        Assert.Equal(2U, radial.StopOffset);
        Assert.Equal(2U, radial.StopCount);
        Assert.Equal(4, retained.GradientStops.Length);
        Assert.Equal(new Vector4(1, 0, 0, 1),
            retained.GradientStops[0].Color);
        Assert.Equal(new Vector4(0, 0, 1, 1),
            retained.GradientStops[1].Color);
        Assert.Equal(new Vector4(0, 1, 0, 1),
            retained.GradientStops[2].Color);
        Assert.Equal(new Vector4(0, 0, 0, 128f / 255f),
            retained.GradientStops[3].Color);
    }

    [Fact]
    public void BuildBatchPreservesSpecularGradientViewport3DMaterial()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        scene.Meshes[0].Materials =
        [
            new PortableViewport3DMaterial
            {
                Kind = PortableViewport3DMaterialKind.Specular,
                Color = new PortableColor4(0.25, 0.5, 0.75, 0.625),
                SpecularPower = 24,
                Brush = PortableBrush.LinearGradient(
                    new PortablePoint(0, 0),
                    new PortablePoint(1, 1),
                    [
                        new PortableGradientStop(
                            new PortableColor(255, 255, 255, 255), 0),
                        new PortableGradientStop(
                            new PortableColor(255, 0, 0, 0), 1)
                    ])
            }
        ];

        NativeMilViewport3DScene retained = Assert.Single(
            new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120)
                .Viewport3DScenes!).Scene;

        NativeSceneMesh3D mesh = Assert.Single(retained.Meshes);
        Assert.NotEqual(0U, mesh.Flags &
            (uint)NativeMesh3DFlags.SpecularMaterial);
        Assert.Equal(new Vector4(0, 0, 0, 1), mesh.Color);
        Assert.Equal(0.25f, mesh.SpecularColor.X);
        Assert.Equal(0.5f, mesh.SpecularColor.Y);
        Assert.Equal(0.75f, mesh.SpecularColor.Z);
        Assert.Equal(24f, mesh.SpecularColor.W);
        Assert.Equal(0.625f, mesh.Opacity);
        Assert.Equal(NativeSceneBrushKind.LinearGradient,
            Assert.Single(retained.Materials).Kind);
        Assert.Equal(2, retained.GradientStops.Length);
    }

    [Fact]
    public void BuildBatchCreatesTypedOrthographicViewport3DCamera()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        Assert.NotNull(scene.Camera);
        scene.Camera.Kind = PortableViewport3DCameraKind.Orthographic;
        scene.Camera.Width = 4;

        WpfNativeMilBatch result =
            new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120);

        NativeSceneCamera3D camera = Assert.Single(
            result.Viewport3DScenes!).Scene.Camera;
        Matrix4x4 expected = Matrix4x4.CreateOrthographic(
            4,
            3,
            0.1f,
            100);
        Assert.Equal(expected.M11, camera.Projection.M11);
        Assert.Equal(expected.M22, camera.Projection.M22);
        Assert.Equal(expected.M33, camera.Projection.M33);
        Assert.Equal(expected.M34, camera.Projection.M34);
        Assert.Equal(expected.M43, camera.Projection.M43);
        Assert.Equal(expected.M44, camera.Projection.M44);
    }

    [Fact]
    public void BuildBatchPreservesTypedMatrixViewport3DCamera()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        Assert.NotNull(scene.Camera);
        Matrix4x4 view = Matrix4x4.CreateTranslation(-3, -4, -5);
        Matrix4x4 projection = new(
            2, 0, 0, 0,
            0, 3, 0, 0,
            0, 0, 4, 1,
            0, 0, -2, 0);
        scene.Camera.Kind = PortableViewport3DCameraKind.Matrix;
        scene.Camera.ViewMatrix = ToPortableMatrix(view);
        scene.Camera.ProjectionMatrix = ToPortableMatrix(projection);

        WpfNativeMilBatch result =
            new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120);

        NativeSceneCamera3D camera = Assert.Single(
            result.Viewport3DScenes!).Scene.Camera;
        Assert.Equal(view.M41, camera.View.M41);
        Assert.Equal(view.M42, camera.View.M42);
        Assert.Equal(view.M43, camera.View.M43);
        Assert.Equal(projection.M11, camera.Projection.M11);
        Assert.Equal(projection.M22, camera.Projection.M22);
        Assert.Equal(projection.M33, camera.Projection.M33);
        Assert.Equal(projection.M34, camera.Projection.M34);
        Assert.Equal(projection.M43, camera.Projection.M43);
        Assert.Equal(3f, camera.CameraPosition.X);
        Assert.Equal(4f, camera.CameraPosition.Y);
        Assert.Equal(5f, camera.CameraPosition.Z);
    }

    [Fact]
    public void BuildBatchRejectsSingularTypedMatrixViewport3DCamera()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        Assert.NotNull(scene.Camera);
        scene.Camera.Kind = PortableViewport3DCameraKind.Matrix;
        scene.Camera.ViewMatrix = new PortableMatrix4x4();

        Assert.Throws<NotSupportedException>(() =>
            new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120));
    }

    [Fact]
    public void BuildBatchPreservesPointLightInNativeLightBuffer()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        scene.Lights =
        [
            new PortableViewport3DLight
            {
                Kind = PortableViewport3DLightKind.Point,
                Position = new PortableVector3(0, 0, 2)
            }
        ];

        WpfNativeMilBatch result =
            new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120);

        NativeMilViewport3DScene retained = Assert.Single(
            result.Viewport3DScenes!).Scene;
        NativeSceneLight3D light = Assert.Single(retained.Lights);
        Assert.Equal((uint)NativeLight3DKind.Point, light.Kind);
        Assert.Equal(2.0f, light.PositionRange.Z);
        Assert.Equal(float.MaxValue, light.PositionRange.W);
        Assert.Equal(1.0f, light.AttenuationOuterCos.X);
        Assert.Equal(1U, Assert.Single(retained.Meshes).LightCount);
    }

    [Fact]
    public void BuildBatchPreservesMultipleLightsAndClampsSpotConesLikeWpf()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        scene.Lights =
        [
            new PortableViewport3DLight
            {
                Kind = PortableViewport3DLightKind.Ambient,
                Color = new PortableColor4(0.1, 0.2, 0.3, 1.0)
            },
            new PortableViewport3DLight
            {
                Kind = PortableViewport3DLightKind.Spot,
                Color = new PortableColor4(1.0, 0.5, 0.25, 1.0),
                Position = new PortableVector3(1, 2, 3),
                Direction = new PortableVector3(0, 0, -2),
                Range = 40,
                ConstantAttenuation = 0.5,
                LinearAttenuation = 0.25,
                QuadraticAttenuation = 0.125,
                InnerConeAngle = 180,
                OuterConeAngle = 90
            }
        ];

        NativeMilViewport3DScene retained = Assert.Single(
            new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120)
                .Viewport3DScenes!).Scene;

        Assert.Equal(2, retained.Lights.Length);
        Assert.Equal((uint)NativeLight3DKind.Ambient, retained.Lights[0].Kind);
        NativeSceneLight3D spot = retained.Lights[1];
        Assert.Equal((uint)NativeLight3DKind.Spot, spot.Kind);
        Assert.Equal(-1.0f, spot.DirectionInnerCos.Z);
        Assert.Equal(spot.AttenuationOuterCos.W, spot.DirectionInnerCos.W);
        Assert.Equal(0.5f, spot.AttenuationOuterCos.X);
        Assert.Equal(0.25f, spot.AttenuationOuterCos.Y);
        Assert.Equal(0.125f, spot.AttenuationOuterCos.Z);
        Assert.Equal(2U, Assert.Single(retained.Meshes).LightCount);
    }

    [Fact]
    public void BuildBatchRejectsInvalidPointLightAttenuation()
    {
        PortableViewport3DScene scene = CreatePortableViewport3DScene();
        scene.Lights =
        [
            new PortableViewport3DLight
            {
                Kind = PortableViewport3DLightKind.Point,
                Position = new PortableVector3(0, 0, 2),
                ConstantAttenuation = 0,
                LinearAttenuation = 0,
                QuadraticAttenuation = 0
            }
        ];

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => new WpfNativeMilSceneCompiler().BuildBatch(
                new FakeViewport3DVisual(scene), 160, 120));

        Assert.Contains("positive term", exception.Message);
    }

    [Fact]
    public void BuildBatchPreservesExactViewport3DRectangleAndScrollClips()
    {
        var clip = new FakePrimitiveGeometry(
            PortablePrimitiveGeometry.Rectangle(
                new PortableRect(20, 22, 50, 40),
                0,
                0,
                PortableMatrix3x2.Identity));
        var visual = new FakeViewport3DVisual(
            CreatePortableViewport3DScene(),
            new PortableVisualState
            {
                HasClip = true,
                Clip = clip,
                HasScrollableAreaClip = true,
                ScrollableAreaClip = new PortableRect(24, 26, 40, 30)
            });

        WpfNativeMilBatch result =
            new WpfNativeMilSceneCompiler().BuildBatch(
                visual, 160, 120);

        List<int> commands = ReadCommands(result.Bytes);
        Assert.Contains(0x1f, commands);
        Assert.Contains(0x28, commands);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BuildBatchPreservesViewport3DGeometryClipAndIsolation(bool cached, bool masked)
    {
        var visual = new FakeViewport3DVisual(CreatePortableViewport3DScene(),
            new PortableVisualState
            {
                HasClip = true,
                Clip = new FakePrimitiveGeometry(PortablePrimitiveGeometry.Ellipse(
                    new PortablePoint(40, 30), 20, 15, PortableMatrix3x2.Identity)),
                HasEffect = !cached,
                Effect = cached ? null : new FakeEffect(PortableEffect.Blur(5)),
                HasCacheMode = cached,
                CacheMode = cached ? new FakeBitmapCache(new PortableBitmapCache(2, false, false)) : null,
                HasOpacityMask = masked,
                OpacityMask = masked ? new FakeBrush(PortableBrush.LinearGradient(
                    new PortablePoint(0, 0), new PortablePoint(1, 0),
                    [new PortableGradientStop(new PortableColor(0, 255, 255, 255), 0),
                     new PortableGradientStop(new PortableColor(255, 255, 255, 255), 1)])) : null
            });
        WpfNativeMilBatch result = new WpfNativeMilSceneCompiler().BuildBatch(visual, 160, 120);
        List<int> commands = ReadCommands(result.Bytes);
        Assert.Contains(0x1f, commands);
        Assert.Contains(0x7a, commands);
        Assert.Contains(cached ? 0x1e : 0x1d, commands);
        Assert.Single(result.Viewport3DScenes!);
        Assert.Single(result.VisualCacheBounds!);
        if (masked) Assert.Contains(0x23, commands);
    }

    private static PortableViewport3DScene CreatePortableViewport3DScene()
    {
        return new PortableViewport3DScene
        {
            Viewport = new PortableRect(12, 18, 80, 60),
            Camera = new PortableViewport3DCamera
            {
                Kind = PortableViewport3DCameraKind.Perspective,
                Position = new PortableVector3(0, 0, 2),
                LookDirection = new PortableVector3(0, 0, -1),
                UpDirection = new PortableVector3(0, 1, 0),
                NearPlaneDistance = 0.1,
                FarPlaneDistance = 100,
                FieldOfView = 45,
                Width = 2
            },
            LightDirection = new PortableVector3(0.5, 1, -0.5),
            LightIntensity = 1,
            AmbientColor = new PortableVector3(1, 1, 1),
            AmbientIntensity = 0.2,
            Meshes =
            [
                new PortableViewport3DMesh
                {
                    Positions =
                    [
                        new PortableVector3(-0.8, -0.8, 0),
                        new PortableVector3(0.8, -0.8, 0),
                        new PortableVector3(0, 0.8, 0)
                    ],
                    Normals =
                    [
                        new PortableVector3(0, 0, 4),
                        new PortableVector3(0, 0, 0),
                        new PortableVector3(0, 0, 1)
                    ],
                    TextureCoordinates =
                    [
                        new PortablePoint(0.25, 0.75),
                        new PortablePoint(1, 0)
                    ],
                    Indices = [0, 1, 2],
                    ModelTransform = PortableMatrix4x4.Identity,
                    DiffuseColor = new PortableColor4(0.25, 0.5, 0.75, 1),
                    SpecularColor = new PortableColor4(0.1, 0.1, 0.1, 1),
                    Shininess = 24,
                    AmbientColor = new PortableVector3(0.2, 0.2, 0.2),
                    Opacity = 1
                }
            ]
        };
    }

    private static PortableMatrix4x4 ToPortableMatrix(Matrix4x4 matrix) =>
        new(
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44);

    private static List<int> ReadCommands(byte[] batch)
    {
        var commands = new List<int>();
        int offset = 0;
        while (offset < batch.Length)
        {
            int itemSize = ReadInt32(batch, offset);
            Assert.True(itemSize >= 8);
            Assert.Equal(0, itemSize & 3);
            Assert.InRange(itemSize, 8, batch.Length - offset);
            commands.Add(ReadInt32(batch, offset + 4));
            offset += itemSize;
        }
        Assert.Equal(batch.Length, offset);
        return commands;
    }

    private static byte[] CreateRectangleRecord(uint brush, uint pen)
    {
        byte[] record = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x40);
        WriteDouble(record, 8, 2);
        WriteDouble(record, 16, 6);
        WriteDouble(record, 24, 30);
        WriteDouble(record, 32, 40);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(44), pen);
        return record;
    }

    private static byte[] CreateLineRecord(uint pen)
    {
        byte[] record = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x3e);
        WriteDouble(record, 8, 1);
        WriteDouble(record, 16, 2);
        WriteDouble(record, 24, 5);
        WriteDouble(record, 32, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), pen);
        return record;
    }

    private static byte[] CreateDrawImageRecord(
        double x,
        double y,
        double width,
        double height,
        uint image)
    {
        byte[] record = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x47);
        WriteDouble(record, 8, x);
        WriteDouble(record, 16, y);
        WriteDouble(record, 24, width);
        WriteDouble(record, 32, height);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), image);
        return record;
    }

    private static byte[] CreateDrawVideoRecord(
        double x,
        double y,
        double width,
        double height,
        uint player,
        uint rectangleAnimation = 0)
    {
        byte[] record = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(
            record.AsSpan(4),
            rectangleAnimation == 0 ? 0x4b : 0x4c);
        WriteDouble(record, 8, x);
        WriteDouble(record, 16, y);
        WriteDouble(record, 24, width);
        WriteDouble(record, 32, height);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), player);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(44), rectangleAnimation);
        return record;
    }

    private static byte[] CreatePushOpacityRecord(double opacity)
    {
        byte[] record = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x4f);
        WriteDouble(record, 8, opacity);
        return record;
    }

    private static byte[] CreatePushOpacityAnimateRecord(
        double opacity,
        uint animation)
    {
        byte[] record = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x50);
        WriteDouble(record, 8, opacity);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16), animation);
        return record;
    }

    private static byte[] CreateAnimatedLineRecord(
        uint pen,
        uint point0Animation,
        uint point1Animation)
    {
        byte[] record = new byte[56];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x3f);
        WriteDouble(record, 8, 1);
        WriteDouble(record, 16, 2);
        WriteDouble(record, 24, 3);
        WriteDouble(record, 32, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), pen);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(44), point0Animation);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(48), point1Animation);
        return record;
    }

    private static byte[] CreateAnimatedRectangleRecord(
        uint brush,
        uint pen,
        uint rectangleAnimation)
    {
        byte[] record = new byte[56];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x41);
        WriteDouble(record, 8, 1);
        WriteDouble(record, 16, 2);
        WriteDouble(record, 24, 10);
        WriteDouble(record, 32, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(44), pen);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(48), rectangleAnimation);
        return record;
    }

    private static byte[] CreateAnimatedRoundedRectangleRecord(
        uint brush,
        uint pen,
        uint rectangleAnimation,
        uint radiusXAnimation,
        uint radiusYAnimation)
    {
        byte[] record = new byte[80];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x43);
        WriteDouble(record, 8, 1);
        WriteDouble(record, 16, 2);
        WriteDouble(record, 24, 10);
        WriteDouble(record, 32, 12);
        WriteDouble(record, 40, 2);
        WriteDouble(record, 48, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(56), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(60), pen);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(64), rectangleAnimation);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(68), radiusXAnimation);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(72), radiusYAnimation);
        return record;
    }

    private static byte[] CreateAnimatedEllipseRecord(
        uint brush,
        uint pen,
        uint centerAnimation,
        uint radiusXAnimation,
        uint radiusYAnimation)
    {
        byte[] record = new byte[64];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x45);
        WriteDouble(record, 8, 8);
        WriteDouble(record, 16, 9);
        WriteDouble(record, 24, 2);
        WriteDouble(record, 32, 3);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(44), pen);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(48), centerAnimation);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(52), radiusXAnimation);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(56), radiusYAnimation);
        return record;
    }

    private static byte[] CreateAnimatedImageRecord(
        uint image,
        uint rectangleAnimation)
    {
        byte[] record = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x48);
        WriteDouble(record, 8, 1);
        WriteDouble(record, 16, 2);
        WriteDouble(record, 24, 10);
        WriteDouble(record, 32, 12);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), image);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(44), rectangleAnimation);
        return record;
    }

    private static byte[] CreatePushClipRecord(uint geometry)
    {
        byte[] record = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x4d);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), geometry);
        return record;
    }

    private static byte[] CreatePushOpacityMaskRecord(
        float x,
        float y,
        float width,
        float height,
        uint brush)
    {
        byte[] record = new byte[32];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x4e);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(8), x);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(12), y);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(16), width);
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(24), brush);
        return record;
    }

    private static byte[] CreatePushGuidelineSetRecord(uint guidelines)
    {
        byte[] record = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x52);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), guidelines);
        return record;
    }

    private static byte[] CreatePushGuidelineY1Record(double coordinate)
    {
        byte[] record = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x53);
        WriteDouble(record, 8, coordinate);
        return record;
    }

    private static byte[] CreatePushGuidelineY2Record(
        double leadingCoordinate,
        double offsetToDrivenCoordinate)
    {
        byte[] record = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x54);
        WriteDouble(record, 8, leadingCoordinate);
        WriteDouble(record, 16, offsetToDrivenCoordinate);
        return record;
    }

    private static byte[] CreatePushEffectRecord(uint effect, uint effectInput)
    {
        byte[] record = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x55);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), effect);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12), effectInput);
        return record;
    }

    private static byte[] CreatePushTransformRecord(uint transform)
    {
        byte[] record = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x51);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), transform);
        return record;
    }

    private static byte[] CreateEllipseRecord(uint brush, uint pen)
    {
        byte[] record = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x44);
        WriteDouble(record, 8, 5);
        WriteDouble(record, 16, 9);
        WriteDouble(record, 24, 7);
        WriteDouble(record, 32, 11);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(40), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(44), pen);
        return record;
    }

    private static byte[] CreateRoundedRectangleRecord(
        double radiusX,
        double radiusY,
        uint brush,
        uint pen)
    {
        byte[] record = new byte[64];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x42);
        WriteDouble(record, 8, 1);
        WriteDouble(record, 16, 3);
        WriteDouble(record, 24, 20);
        WriteDouble(record, 32, 30);
        WriteDouble(record, 40, radiusX);
        WriteDouble(record, 48, radiusY);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(56), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(60), pen);
        return record;
    }

    private static byte[] CreateDrawGeometryRecord(
        uint brush,
        uint pen,
        uint geometry)
    {
        byte[] record = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x46);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), brush);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12), pen);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16), geometry);
        return record;
    }

    private static byte[] CreateDrawDrawingRecord(uint drawing)
    {
        byte[] record = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x4a);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(8), drawing);
        return record;
    }

    private static byte[] CreateDrawGlyphRunRecord(
        uint foregroundBrush,
        uint glyphRun)
    {
        byte[] record = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x49);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(8), foregroundBrush);
        BinaryPrimitives.WriteUInt32LittleEndian(
            record.AsSpan(12), glyphRun);
        return record;
    }

    private static byte[] CreatePopRecord()
    {
        byte[] record = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(record, record.Length);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(4), 0x56);
        return record;
    }

    private static void WriteDouble(byte[] bytes, int offset, double value) =>
        BinaryPrimitives.WriteInt64LittleEndian(
            bytes.AsSpan(offset), BitConverter.DoubleToInt64Bits(value));

    private static int FindCommand(byte[] batch, int command)
    {
        int offset = 0;
        while (offset < batch.Length)
        {
            if (ReadInt32(batch, offset + 4) == command)
            {
                return offset;
            }
            offset += ReadInt32(batch, offset);
        }
        throw new Xunit.Sdk.XunitException(
            $"MIL command 0x{command:x} was not found.");
    }

    private static int FindCreateResource(byte[] batch, uint handle)
    {
        int offset = 0;
        while (offset < batch.Length)
        {
            if (ReadInt32(batch, offset + 4) == 0x07 &&
                ReadUInt32(batch, offset + 8) == handle)
            {
                return offset;
            }
            offset += ReadInt32(batch, offset);
        }
        throw new Xunit.Sdk.XunitException(
            $"MIL resource handle {handle} was not created.");
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));

    private static ulong ReadUInt64(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset, 8));

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));

    private static float ReadSingle(byte[] bytes, int offset) =>
        BitConverter.UInt32BitsToSingle(ReadUInt32(bytes, offset));

    private static double ReadDouble(byte[] bytes, int offset) =>
        BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset, 8)));

    private static float SrgbToLinear(byte component)
    {
        float value = component / 255.0f;
        return value <= 0.04045f
            ? value / 12.92f
            : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
    }

    private static TtfFont LoadInterFont() => new(Path.Combine(
        FindRepositoryRoot(),
        "external",
        "ProGPU",
        "src",
        "ProGPU.Fonts.Inter",
        "Fonts",
        "Inter-Regular.ttf"));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName, "Microsoft.Dotnet.Wpf.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate the WPF repository root.");
    }

    private sealed class FakeVisual :
        IPortableVisualStateSource,
        IPortableVisualChildrenSource,
        IPortableDrawingContentSource,
        IPortableVisualBoundsSource
    {
        private readonly object? _content;
        private readonly PortableVisualState _state;
        private readonly object[] _children;

        internal FakeVisual(
            object? content,
            PortableVisualState? state = null,
            params object[] children)
        {
            _content = content;
            _children = children;
            _state = state ?? new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1
            };
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }

        public bool TryGetPortableVisualChildCount(out int count)
        {
            count = _children.Length;
            return true;
        }

        public bool TryGetPortableVisualChild(int index, out object? child)
        {
            if ((uint)index >= (uint)_children.Length)
            {
                child = null;
                return false;
            }
            child = _children[index];
            return true;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = _content;
            return true;
        }

        public bool TryGetPortableVisualBounds(
            out PortableVisualBounds bounds)
        {
            bounds = new PortableVisualBounds
            {
                HasDescendantBounds = true,
                DescendantBounds = new PortableRect(1, 2, 30, 20)
            };
            return true;
        }
    }

    private sealed class FakeViewport3DVisual :
        IPortableVisualStateSource,
        IPortableVisualChildrenSource,
        IPortableVisualBoundsSource,
        IPortableViewport3DSceneSource
    {
        private readonly PortableViewport3DScene _scene;
        private readonly PortableVisualState _state;

        internal FakeViewport3DVisual(
            PortableViewport3DScene scene,
            PortableVisualState? state = null)
        {
            _scene = scene;
            _state = state ?? new PortableVisualState
            {
                HasOffset = true,
                Offset = new PortablePoint(0, 0),
                HasOpacity = true,
                Opacity = 1
            };
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }

        public bool TryGetPortableVisualChildCount(out int count)
        {
            count = 0;
            return true;
        }

        public bool TryGetPortableVisualChild(int index, out object? child)
        {
            child = null;
            return false;
        }

        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        {
            bounds = new PortableVisualBounds
            {
                HasDescendantBounds = true,
                DescendantBounds = _scene.Viewport
            };
            return true;
        }

        public bool TryGetPortableViewport3DScene(
            out PortableViewport3DScene scene)
        {
            scene = _scene;
            return true;
        }
    }

    private sealed class FakeRenderData : IPortableRenderDataSource
    {
        private readonly PortableRenderDataSnapshot _snapshot;

        internal FakeRenderData(byte[] bytes, IReadOnlyList<object?> resources)
        {
            _snapshot = new PortableRenderDataSnapshot(bytes, resources);
        }

        public bool TryGetPortableRenderDataSnapshot(
            out PortableRenderDataSnapshot snapshot)
        {
            snapshot = _snapshot;
            return true;
        }
    }

    private sealed class FakeMediaPlayer :
        IPortableMediaPlayerSource,
        IProGpuTextureLeaseSource
    {
        private readonly PortableMediaPlayerFrame? _frame;

        internal FakeMediaPlayer()
        {
        }

        internal FakeMediaPlayer(int width, int height, ulong contentVersion)
        {
            _frame = new PortableMediaPlayerFrame(
                width,
                height,
                contentVersion,
                this);
        }

        public bool TryGetPortableMediaPlayerFrame(
            out PortableMediaPlayerFrame frame)
        {
            frame = _frame.GetValueOrDefault();
            return _frame.HasValue;
        }

        public bool TryGetGpuTexture(out GpuTexture texture)
        {
            texture = null!;
            return false;
        }

        public bool TryAcquireGpuTextureLease(
            out IProGpuTextureLease lease)
        {
            lease = null!;
            return false;
        }
    }

    private sealed class FakePortableNativeImage :
        IPortableNativeImageSource,
        IProGpuTextureLeaseSource
    {
        internal FakePortableNativeImage(int width, int height)
        {
            PixelWidth = width;
            PixelHeight = height;
        }

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public double DpiX { get; init; } = 96.0;

        public double DpiY { get; init; } = 96.0;

        public bool TryGetPortableNativeImage(out object? nativeImage)
        {
            nativeImage = this;
            return true;
        }

        public bool TryGetGpuTexture(out GpuTexture texture)
        {
            texture = null!;
            return false;
        }

        public bool TryAcquireGpuTextureLease(
            out IProGpuTextureLease lease)
        {
            lease = null!;
            return false;
        }
    }

    private sealed class FakeD3DImage :
        IPortableD3DImageSource,
        IProGpuTextureLeaseSource
    {
        private readonly PortableD3DImageFrame _frame;

        internal FakeD3DImage(int width, int height, ulong contentVersion)
        {
            _frame = new PortableD3DImageFrame(
                width,
                height,
                contentVersion,
                this);
        }

        public bool TryGetPortableD3DImageFrame(
            out PortableD3DImageFrame frame)
        {
            frame = _frame;
            return true;
        }

        public bool TryGetGpuTexture(out GpuTexture texture)
        {
            texture = null!;
            return false;
        }

        public bool TryAcquireGpuTextureLease(
            out IProGpuTextureLease lease)
        {
            lease = null!;
            return false;
        }
    }

    private sealed class FakeBrush : IPortableBrushSource
    {
        private readonly PortableBrush _brush;

        internal FakeBrush(PortableColor color)
        {
            _brush = PortableBrush.SolidColor(color);
        }

        internal FakeBrush(PortableBrush brush)
        {
            _brush = brush;
        }

        public bool TryGetPortableBrush(out PortableBrush brush)
        {
            brush = _brush;
            return true;
        }
    }

    private sealed class FakeBitmapCache : IPortableBitmapCacheSource
    {
        private readonly PortableBitmapCache _cache;

        internal FakeBitmapCache(PortableBitmapCache cache)
        {
            _cache = cache;
        }

        public bool TryGetPortableBitmapCache(out PortableBitmapCache cache)
        {
            cache = _cache;
            return true;
        }
    }

    private sealed class FakeVisualWithoutBounds :
        IPortableVisualStateSource,
        IPortableVisualChildrenSource,
        IPortableDrawingContentSource
    {
        private readonly PortableVisualState _state;

        internal FakeVisualWithoutBounds(PortableVisualState state)
        {
            _state = state;
        }

        public bool TryGetPortableVisualState(out PortableVisualState state)
        {
            state = _state;
            return true;
        }

        public bool TryGetPortableVisualChildCount(out int count)
        {
            count = 0;
            return true;
        }

        public bool TryGetPortableVisualChild(int index, out object? child)
        {
            child = null;
            return false;
        }

        public bool TryGetPortableDrawingContent(out object? content)
        {
            content = null;
            return true;
        }
    }

    private sealed class FakeTransform : IPortableTransformMatrixSource
    {
        private readonly PortableMatrix3x2 _matrix;

        internal FakeTransform(PortableMatrix3x2 matrix)
        {
            _matrix = matrix;
        }

        public bool TryGetPortableTransformMatrix(
            out PortableMatrix3x2 matrix)
        {
            matrix = _matrix;
            return true;
        }
    }

    private sealed class FakePen : IPortablePenSource
    {
        private readonly PortablePen _pen;

        internal FakePen(
            PortableColor color,
            double thickness,
            PortablePenLineCap startLineCap,
            PortablePenLineCap endLineCap,
            PortablePenLineCap dashCap,
            PortablePenLineJoin lineJoin,
            double miterLimit,
            double[] dashArray)
        {
            _pen = new PortablePen(
                PortableBrush.SolidColor(color),
                thickness,
                startLineCap,
                endLineCap,
                dashCap,
                lineJoin,
                miterLimit,
                dashArray,
                dashOffset: 0);
        }

        internal FakePen(
            PortableBrush brush,
            double thickness,
            PortablePenLineCap startLineCap,
            PortablePenLineCap endLineCap,
            PortablePenLineCap dashCap,
            PortablePenLineJoin lineJoin,
            double miterLimit,
            double[] dashArray)
        {
            _pen = new PortablePen(
                brush,
                thickness,
                startLineCap,
                endLineCap,
                dashCap,
                lineJoin,
                miterLimit,
                dashArray,
                dashOffset: 0);
        }

        public bool TryGetPortablePen(out PortablePen pen)
        {
            pen = _pen;
            return true;
        }
    }

    private sealed class FakeGeometry : IPortableGeometryPathSource
    {
        private readonly PortableGeometryPath _path;

        internal FakeGeometry(PortableGeometryPath path)
        {
            _path = path;
        }

        public bool TryGetPortableGeometryPath(out PortableGeometryPath path)
        {
            path = _path;
            return true;
        }
    }

    private sealed class FakeGeometryDrawing :
        IPortableGeometryDrawingStateSource,
        IPortableDrawingBoundsSource
    {
        private readonly PortableGeometryDrawingState _state;
        private readonly PortableRect? _bounds;

        internal FakeGeometryDrawing(
            object? brush,
            object? pen,
            object? geometry,
            PortableRect? bounds = null)
        {
            _bounds = bounds;
            _state = new PortableGeometryDrawingState
            {
                HasBrush = brush is not null,
                Brush = brush,
                HasPen = pen is not null,
                Pen = pen,
                HasGeometry = geometry is not null,
                Geometry = geometry
            };
        }

        public bool TryGetPortableGeometryDrawingState(
            out PortableGeometryDrawingState state)
        {
            state = _state;
            return true;
        }

        public bool TryGetPortableDrawingBounds(out PortableRect bounds)
        {
            bounds = _bounds.GetValueOrDefault();
            return _bounds.HasValue;
        }
    }

    private sealed class FakeNativeGlyphRun :
        IPortableNativeGlyphRunSource
    {
        private readonly PortableNativeGlyphRun _glyphRun;

        internal FakeNativeGlyphRun(PortableNativeGlyphRun glyphRun)
        {
            _glyphRun = glyphRun;
        }

        public bool TryGetPortableNativeGlyphRun(
            out PortableNativeGlyphRun glyphRun)
        {
            glyphRun = _glyphRun;
            return true;
        }
    }

    private sealed class FakeGlyphRunDrawing :
        IPortableGlyphRunDrawingStateSource
    {
        private readonly PortableGlyphRunDrawingState _state;

        internal FakeGlyphRunDrawing(object? glyphRun, object? brush)
        {
            _state = new PortableGlyphRunDrawingState
            {
                HasGlyphRun = glyphRun is not null,
                GlyphRun = glyphRun,
                HasForegroundBrush = brush is not null,
                ForegroundBrush = brush
            };
        }

        public bool TryGetPortableGlyphRunDrawingState(
            out PortableGlyphRunDrawingState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class FakeImageDrawing :
        IPortableImageDrawingStateSource
    {
        private readonly PortableImageDrawingState _state;

        internal FakeImageDrawing(object? imageSource, PortableRect rect)
        {
            _state = new PortableImageDrawingState
            {
                HasImageSource = imageSource is not null,
                ImageSource = imageSource,
                HasRect = true,
                Rect = rect
            };
        }

        public bool TryGetPortableImageDrawingState(
            out PortableImageDrawingState state)
        {
            state = _state;
            return true;
        }
    }

    private sealed class FakeBitmapSource :
        IPortableBitmapSourcePixelsSource
    {
        private readonly PortableBitmapSourcePixels _pixels;

        internal FakeBitmapSource(PortableBitmapSourcePixels pixels)
        {
            _pixels = pixels;
        }

        public bool TryGetPortableBitmapSourcePixels(
            out PortableBitmapSourcePixels pixels)
        {
            pixels = _pixels;
            return true;
        }
    }

    private sealed class FakeDrawingImage : IPortableDrawingImageSource
    {
        private readonly object? _drawing;

        internal FakeDrawingImage(object? drawing)
        {
            _drawing = drawing;
        }

        public bool TryGetPortableDrawingImage(out object? drawing)
        {
            drawing = _drawing;
            return drawing is not null;
        }
    }

    private sealed class FakeDrawingGroup :
        IPortableDrawingGroupStateSource,
        IPortableDrawingGroupChildrenSource
    {
        private readonly PortableDrawingGroupState _state;
        private object[] _children;

        internal FakeDrawingGroup(
            PortableDrawingGroupState state,
            object[] children)
        {
            _state = state;
            _children = children;
        }

        internal void SetChildren(object[] children) => _children = children;

        public bool TryGetPortableDrawingGroupState(
            out PortableDrawingGroupState state)
        {
            state = _state;
            return true;
        }

        public bool TryGetPortableDrawingGroupChildCount(out int count)
        {
            count = _children.Length;
            return count != 0;
        }

        public bool TryGetPortableDrawingGroupChild(
            int index,
            out object child)
        {
            if ((uint)index < (uint)_children.Length)
            {
                child = _children[index];
                return true;
            }
            child = null!;
            return false;
        }
    }

    private sealed class FakeGuidelineSet : IPortableGuidelineSetSource
    {
        private readonly PortableGuidelineSet _state;

        internal FakeGuidelineSet(PortableGuidelineSet state)
        {
            _state = state;
        }

        public bool TryGetPortableGuidelineSet(
            out PortableGuidelineSet guidelineSet)
        {
            guidelineSet = _state;
            return true;
        }
    }

    private sealed class FakeDoubleAnimationValue :
        IPortableDoubleAnimationValueSource
    {
        private readonly double _value;

        internal FakeDoubleAnimationValue(double value)
        {
            _value = value;
        }

        public bool TryGetPortableDoubleAnimationValue(out double value)
        {
            value = _value;
            return true;
        }
    }

    private sealed class FakePointAnimationValue :
        IPortablePointAnimationValueSource
    {
        private readonly PortablePoint _value;

        internal FakePointAnimationValue(PortablePoint value)
        {
            _value = value;
        }

        public bool TryGetPortablePointAnimationValue(
            out PortablePoint value)
        {
            value = _value;
            return true;
        }
    }

    private sealed class FakeRectAnimationValue :
        IPortableRectAnimationValueSource
    {
        private readonly PortableRect _value;

        internal FakeRectAnimationValue(PortableRect value)
        {
            _value = value;
        }

        public bool TryGetPortableRectAnimationValue(out PortableRect value)
        {
            value = _value;
            return true;
        }
    }

    private sealed class FakePrimitiveGeometry :
        IPortablePrimitiveGeometrySource
    {
        private readonly PortablePrimitiveGeometry _geometry;

        internal FakePrimitiveGeometry(PortablePrimitiveGeometry geometry)
        {
            _geometry = geometry;
        }

        public bool TryGetPortablePrimitiveGeometry(
            out PortablePrimitiveGeometry geometry)
        {
            geometry = _geometry;
            return true;
        }
    }

    private sealed class FakeEffect : IPortableEffectSource
    {
        private readonly PortableEffect _effect;

        internal FakeEffect(PortableEffect effect)
        {
            _effect = effect;
        }

        public bool TryGetPortableEffect(out PortableEffect effect)
        {
            effect = _effect;
            return true;
        }
    }
}

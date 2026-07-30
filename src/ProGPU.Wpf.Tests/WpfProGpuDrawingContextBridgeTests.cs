using System.Numerics;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Scene;
using ProGPU.Wpf.Interop;
using Xunit;
using ProGpuDrawingVisual = ProGPU.Scene.DrawingVisual;

namespace ProGPU.Wpf.Tests;

public sealed class WpfProGpuDrawingContextBridgeTests
{
    [Fact]
    public void PortableNativeDrawingContextStateAdaptsToTypedProGpuState()
    {
        var root = new ProGpuDrawingVisual();
        var frame = new ProGpuWpfDrawingFrame(root, 100, 50);
        using var context = frame.OpenObjectRenderDataSinkContext(ownerVisual: null);
        var source = Assert.IsAssignableFrom<IPortableNativeDrawingContextStateSource>(context);
        var expected = new Matrix4x4(
            2f, 0.25f, 0f, 0f,
            0.5f, 3f, 0f, 0f,
            0f, 0f, 1f, 0f,
            11f, 13f, 0f, 1f);

        context.PushTransform(new FakePortableTransform(expected));

        Assert.True(WpfProGpuDrawingContextBridge.TryGetProGpuDrawingContextState(
            source,
            out var state));
        Assert.Same(root.Context, state.DrawingContext);
        Assert.Equal(expected, state.OuterTransform);
    }

    [Fact]
    public void PortableNativeDrawingContextStateAdaptationFailsClosed()
    {
        Assert.False(WpfProGpuDrawingContextBridge.TryGetProGpuDrawingContextState(
            new FakePortableNativeDrawingContextStateSource(new object(), Matrix4x4.Identity),
            out var wrongContextState));
        Assert.Equal(default, wrongContextState);

        var nativeContext = new DrawingContext();
        var invalidTransform = Matrix4x4.Identity;
        invalidTransform.M11 = float.NaN;

        Assert.False(WpfProGpuDrawingContextBridge.TryGetProGpuDrawingContextState(
            new FakePortableNativeDrawingContextStateSource(nativeContext, invalidTransform),
            out var invalidTransformState));
        Assert.Equal(default, invalidTransformState);
    }

    [Fact]
    public void PortableNativeDrawingContextStateAdaptationDoesNotAllocate()
    {
        var nativeContext = new DrawingContext();
        var source = new FakePortableNativeDrawingContextStateSource(
            nativeContext,
            Matrix4x4.Identity);

        Assert.True(WpfProGpuDrawingContextBridge.TryGetProGpuDrawingContextState(
            source,
            out _));
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 1_000_000; index++)
        {
            if (!WpfProGpuDrawingContextBridge.TryGetProGpuDrawingContextState(
                    source,
                    out var state) ||
                !ReferenceEquals(nativeContext, state.DrawingContext))
            {
                throw new InvalidOperationException("Typed drawing-context state changed.");
            }
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void TypedProGpuDrawingContextBridgeUsesNoReflection()
    {
        var source = File.ReadAllText(FindRepoPath(
            "src",
            "ProGPU.Wpf",
            "Composition",
            "WpfProGpuDrawingContextBridge.cs"));

        Assert.Contains(
            "IPortableNativeDrawingContextStateSource source",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ProGpuDrawingContextState.TryCreate(",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BindingFlags", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetField(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetMethod(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke(", source, StringComparison.Ordinal);
    }

    private sealed class FakePortableTransform : IPortableTransformMatrixSource
    {
        private readonly PortableMatrix3x2 _matrix;

        public FakePortableTransform(Matrix4x4 matrix)
        {
            _matrix = new PortableMatrix3x2(
                matrix.M11,
                matrix.M12,
                matrix.M21,
                matrix.M22,
                matrix.M41,
                matrix.M42);
        }

        public bool TryGetPortableTransformMatrix(out PortableMatrix3x2 matrix)
        {
            matrix = _matrix;
            return true;
        }
    }

    private sealed class FakePortableNativeDrawingContextStateSource :
        IPortableNativeDrawingContextStateSource
    {
        private readonly PortableNativeDrawingContextState _state;

        public FakePortableNativeDrawingContextStateSource(
            object nativeDrawingContext,
            Matrix4x4 transform)
        {
            _state = new PortableNativeDrawingContextState(
                nativeDrawingContext,
                transform);
        }

        public bool TryGetPortableNativeDrawingContextState(
            out PortableNativeDrawingContextState state)
        {
            state = _state;
            return true;
        }
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}

using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;

// Uses only typed portable contracts and the actual native channel. No window,
// GPU device, real-assembly loader, or reflection is needed for this ingress gate.
internal static class NativeMilViewportRetentionSmoke
{
    internal static void Run()
    {
        var visual = new Viewport();
        using var session = new WpfNativeMilCompilationSession();
        var first = session.Update(visual, 160, 120);
        Require(first.RecreatedChannel && first.AppliedSidebandCount == 1, "initial binding");
        byte[] original = session.CompileFrame(1, 1, 0, 1).Scene.Stream;
        var same = session.Update(visual, 160, 120);
        Require(!same.RecreatedChannel && same.AppliedSidebandCount == 0, "unchanged binding");
        Require(original.AsSpan().SequenceEqual(session.CompileFrame(1, 1, 0, 2).Scene.Stream),
            "unchanged semantic stream");
        visual.Scene.Meshes[0].Positions[0] = new(-0.4, -0.8, 0);
        var changed = session.Update(visual, 160, 120);
        Require(!changed.RecreatedChannel && changed.AppliedSidebandCount == 1, "mutated producer binding");
        Require(!original.AsSpan().SequenceEqual(session.CompileFrame(1, 1, 0, 3).Scene.Stream),
            "mutated semantic stream");
        Require(session.Update(visual, 160, 120).AppliedSidebandCount == 0, "new baseline");
        var resized = session.Update(visual, 320, 240);
        Require(!resized.RecreatedChannel && resized.AppliedSidebandCount == 0, "resize preserves payload");
        // Adding an ordinary parent changes packet/handle topology and must
        // replace the channel, including its owned viewport comparison state.
        var parent = new Parent(visual);
        var replacement = session.Update(parent, 320, 240);
        Require(replacement.RecreatedChannel && replacement.AppliedSidebandCount == 1, "replacement baseline");
        Require(session.Update(parent, 320, 240).AppliedSidebandCount == 0, "replacement reuse");
        session.Dispose();
        Require(session.IsDisposed && !session.IsInitialized, "disposed retention");
        Console.WriteLine("Native MIL viewport retention smoke passed: unchanged, mutated, resized, disposed.");
    }

    private static void Require(bool condition, string operation)
    {
        if (!condition) throw new InvalidOperationException($"Native MIL retention failed: {operation}.");
    }

    private sealed class Parent(Viewport viewport) : IPortableVisualStateSource,
        IPortableVisualChildrenSource, IPortableVisualBoundsSource
    {
        public bool TryGetPortableVisualState(out PortableVisualState state)
        { state = new() { HasOpacity = true, Opacity = 1 }; return true; }
        public bool TryGetPortableVisualChildCount(out int count)
        { count = 1; return true; }
        public bool TryGetPortableVisualChild(int index, out object? child)
        { child = index == 0 ? viewport : null; return index == 0; }
        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        { bounds = new() { HasDescendantBounds = true, DescendantBounds = viewport.Scene.Viewport }; return true; }
    }

    private sealed class Viewport : IPortableViewport3DSceneSource,
        IPortableVisualStateSource, IPortableVisualChildrenSource, IPortableVisualBoundsSource
    {
        internal PortableViewport3DScene Scene { get; } = new()
        {
            Viewport = new(12, 18, 80, 60),
            Camera = new()
            {
                Kind = PortableViewport3DCameraKind.Perspective,
                Position = new(0, 0, 2), LookDirection = new(0, 0, -1), UpDirection = new(0, 1, 0),
                NearPlaneDistance = 0.1, FarPlaneDistance = 100, FieldOfView = 45, Width = 2
            },
            LightDirection = new(0.5, 1, -0.5), LightIntensity = 1,
            AmbientColor = new(1, 1, 1), AmbientIntensity = 0.2,
            Meshes =
            [
                new()
                {
                    Positions = [new(-0.8, -0.8, 0), new(0.8, -0.8, 0), new(0, 0.8, 0)],
                    Normals = [new(0, 0, 1), new(0, 0, 1), new(0, 0, 1)],
                    Indices = [0, 1, 2], ModelTransform = PortableMatrix4x4.Identity,
                    DiffuseColor = new(0.25, 0.5, 0.75, 1), SpecularColor = new(0.1, 0.1, 0.1, 1),
                    Shininess = 24, AmbientColor = new(0.2, 0.2, 0.2), Opacity = 1
                }
            ]
        };

        public bool TryGetPortableViewport3DScene(out PortableViewport3DScene scene)
        { scene = Scene; return true; }
        public bool TryGetPortableVisualState(out PortableVisualState state)
        { state = new() { HasOpacity = true, Opacity = 1 }; return true; }
        public bool TryGetPortableVisualChildCount(out int count)
        { count = 0; return true; }
        public bool TryGetPortableVisualChild(int index, out object? child)
        { child = null; return false; }
        public bool TryGetPortableVisualBounds(out PortableVisualBounds bounds)
        { bounds = new() { HasDescendantBounds = true, DescendantBounds = Scene.Viewport }; return true; }
    }
}

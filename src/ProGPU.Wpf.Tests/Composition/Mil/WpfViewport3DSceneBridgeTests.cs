using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.ProGPU.Composition;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;
using Xunit;
using MediaBrush = System.Windows.Media.Brush;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaGeometry = System.Windows.Media.Geometry;
using MediaGlyphRun = System.Windows.Media.GlyphRun;
using MediaImageSource = System.Windows.Media.ImageSource;
using MediaPen = System.Windows.Media.Pen;
using MediaTransform = System.Windows.Media.Transform;

namespace ProGPU.Wpf.Tests.Composition.Mil;

public sealed class WpfViewport3DSceneBridgeTests
{
    [Fact]
    public void TryCreateReplayDataRejectsWpfShapedViewport3DVisual()
    {
        var viewport = CreateTriangleViewport();

        var replayed = WpfViewport3DSceneBridge.TryCreateReplayData(viewport, out var replayData);

        Assert.False(replayed);
        Assert.Equal(default, replayData);
    }

    [Fact]
    public void TryCreateReplayDataPrefersPortableViewport3DSceneWithoutPropertyProbe()
    {
        var viewport = new PortableViewport3DVisual();

        var replayed = WpfViewport3DSceneBridge.TryCreateReplayData(viewport, out var replayData);

        Assert.True(replayed);
        Assert.Equal(new Vector2(320, 180), replayData.Payload.ViewportSize);
        Assert.Equal(new global::ProGPU.Scene.Rect(4, 8, 320, 180), replayData.Viewport);
        var mesh = Assert.Single(replayData.Payload.Meshes);
        Assert.Same(viewport.GeometryKey, mesh.Geometry);
        Assert.Equal(13, mesh.GeometryVersion);
        Assert.Equal(new[] { 0, 1, 2 }, mesh.Indices);
        Assert.Equal(10, mesh.ModelTransform.M41);
        Assert.Equal(0.25f, replayData.Payload.AmbientIntensity);
    }

    [Fact]
    public void TryCreateReplayDataPreservesPointAndSpotLightsForManagedGpuBuffer()
    {
        var viewport = new PortableViewport3DVisual
        {
            Lights =
            [
                new PortableViewport3DLight
                {
                    Kind = PortableViewport3DLightKind.Ambient,
                    Color = new PortableColor4(0.1, 0.2, 0.3, 1)
                },
                new PortableViewport3DLight
                {
                    Kind = PortableViewport3DLightKind.Point,
                    Position = new PortableVector3(0, 0, 2),
                    Range = 25,
                    LinearAttenuation = 0.25
                },
                new PortableViewport3DLight
                {
                    Kind = PortableViewport3DLightKind.Spot,
                    Position = new PortableVector3(1, 2, 3),
                    Direction = new PortableVector3(0, 0, -2),
                    Range = 40,
                    InnerConeAngle = 180,
                    OuterConeAngle = 90
                }
            ]
        };

        Assert.True(WpfViewport3DSceneBridge.TryCreateReplayData(
            viewport,
            out var replayData));
        Assert.Equal(3, replayData.Payload.Lights.Count);
        Assert.Equal(
            global::ProGPU.Scene.Extensions.LightKind3D.Ambient,
            replayData.Payload.Lights[0].Kind);
        Assert.Equal(
            global::ProGPU.Scene.Extensions.LightKind3D.Point,
            replayData.Payload.Lights[1].Kind);
        Assert.Equal(25f, replayData.Payload.Lights[1].Range);
        Assert.Equal(0.25f,
            replayData.Payload.Lights[1].LinearAttenuation);
        var spot = replayData.Payload.Lights[2];
        Assert.Equal(
            global::ProGPU.Scene.Extensions.LightKind3D.Spot,
            spot.Kind);
        Assert.Equal(-1f, spot.Direction.Z);
        Assert.Equal(spot.OuterConeCosine, spot.InnerConeCosine);
    }

    [Fact]
    public void ReplaySubtreeRejectsWpfShapedViewport3DVisualWithoutPortableScene()
    {
        var viewport = CreateTriangleViewport();
        var sink = new ViewportSink { DrawViewport3DResult = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(viewport, sink);

        Assert.Equal(0, sink.DrawViewport3DCount);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(0, result.ContentCount);
        Assert.Equal(0, result.ChildEdgeCount);
        Assert.Equal(0, result.UnsupportedContentCount);
    }

    [Fact]
    public void ReplaySubtreeRoutesPortableViewportSceneSourceWithoutTypeName()
    {
        var viewport = new PortableSceneHost();
        var sink = new ViewportSink { DrawViewport3DResult = true };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(viewport, sink);

        Assert.Equal(1, sink.DrawViewport3DCount);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(1, result.ContentCount);
        Assert.Equal(0, result.ChildEdgeCount);
        Assert.Equal(0, result.UnsupportedContentCount);
    }

    [Fact]
    public void ReplaySubtreeCountsPortableViewport3DSceneUnsupportedWhenSinkCannotDrawIt()
    {
        var viewport = new PortableSceneHost();
        var sink = new ViewportSink { DrawViewport3DResult = false };

        var result = new WpfVisualTreeRenderer().ReplaySubtree(viewport, sink);

        Assert.Equal(1, sink.DrawViewport3DCount);
        Assert.Equal(1, result.VisualCount);
        Assert.Equal(0, result.ContentCount);
        Assert.Equal(0, result.ChildEdgeCount);
        Assert.Equal(1, result.UnsupportedContentCount);
    }

    private static FakeViewport3DVisual CreateTriangleViewport()
    {
        var viewport = new FakeViewport3DVisual
        {
            Viewport = new FakeRect(10, 20, 200, 100),
            Camera = new FakePerspectiveCamera(
                new FakePoint3D(0, 0, 4),
                new FakeVector3D(0, 0, -4),
                new FakeVector3D(0, 1, 0),
                fieldOfView: 60)
        };

        var model = new FakeModelVisual3D
        {
            Transform = new FakeTransform3D(new FakeMatrix3D(OffsetX: 5))
        };
        model.Content = new FakeGeometryModel3D
        {
            Geometry = new FakeMeshGeometry3D(
                new FakePoint3DCollection(
                    new FakePoint3D(0, 0, 0),
                    new FakePoint3D(1, 0, 0),
                    new FakePoint3D(0, 1, 0)),
                normals: new FakeVector3DCollection(),
                indices: new FakeInt32Collection(0, 1, 2)),
            Material = new FakeDiffuseMaterial(Brushes.Red)
        };

        viewport.Children.Add(model);
        return viewport;
    }

    private sealed class FakeViewport3DVisual
    {
        public FakeRect Viewport { get; init; }

        public object? Camera { get; init; }

        public FakeVisual3DCollection Children { get; } = new();
    }

    private sealed class FakeModelVisual3D
    {
        public object? Content { get; set; }

        public object? Transform { get; init; }

        public FakeVisual3DCollection Children { get; } = new();
    }

    private sealed class FakeGeometryModel3D
    {
        public object? Geometry { get; init; }

        public object? Material { get; init; }

        public object? BackMaterial { get; init; }

        public object? Transform { get; init; }
    }

    private sealed class FakeMeshGeometry3D
    {
        public FakeMeshGeometry3D(
            FakePoint3DCollection positions,
            FakeVector3DCollection normals,
            FakeInt32Collection indices)
        {
            Positions = positions;
            Normals = normals;
            TriangleIndices = indices;
        }

        public FakePoint3DCollection Positions { get; }

        public FakeVector3DCollection Normals { get; }

        public FakeInt32Collection TriangleIndices { get; }

        public int Version { get; } = 7;
    }

    private sealed class FakeDiffuseMaterial
    {
        public FakeDiffuseMaterial(MediaBrush brush)
        {
            Brush = brush;
        }

        public MediaBrush Brush { get; }
    }

    private sealed class FakePerspectiveCamera
    {
        public FakePerspectiveCamera(
            FakePoint3D position,
            FakeVector3D lookDirection,
            FakeVector3D upDirection,
            double fieldOfView)
        {
            Position = position;
            LookDirection = lookDirection;
            UpDirection = upDirection;
            FieldOfView = fieldOfView;
        }

        public FakePoint3D Position { get; }

        public FakeVector3D LookDirection { get; }

        public FakeVector3D UpDirection { get; }

        public double FieldOfView { get; }

        public double NearPlaneDistance { get; } = 0.1;

        public double FarPlaneDistance { get; } = 100;
    }

    private sealed class FakeTransform3D
    {
        public FakeTransform3D(FakeMatrix3D value)
        {
            Value = value;
        }

        public FakeMatrix3D Value { get; }
    }

    private readonly record struct FakeMatrix3D(
        double M11 = 1,
        double M12 = 0,
        double M13 = 0,
        double M14 = 0,
        double M21 = 0,
        double M22 = 1,
        double M23 = 0,
        double M24 = 0,
        double M31 = 0,
        double M32 = 0,
        double M33 = 1,
        double M34 = 0,
        double OffsetX = 0,
        double OffsetY = 0,
        double OffsetZ = 0,
        double M44 = 1);

    private sealed class FakeVisual3DCollection
    {
        private readonly List<object> _items = new();

        public int Count => _items.Count;

        public object this[int index] => _items[index];

        public void Add(object item)
        {
            _items.Add(item);
        }
    }

    private sealed class PortableViewport3DVisual : IPortableViewport3DSceneSource
    {
        public object GeometryKey { get; } = new();

        public PortableViewport3DLight[] Lights { get; init; } = [];

        public object Viewport => throw new InvalidOperationException("Portable scene should not probe Viewport.");

        public object Camera => throw new InvalidOperationException("Portable scene should not probe Camera.");

        public object Children => throw new InvalidOperationException("Portable scene should not probe Children.");

        public bool TryGetPortableViewport3DScene(out PortableViewport3DScene scene)
        {
            scene = new PortableViewport3DScene
            {
                Viewport = new PortableRect(4, 8, 320, 180),
                Camera = new PortableViewport3DCamera
                {
                    Kind = PortableViewport3DCameraKind.Perspective,
                    Position = new PortableVector3(0, 0, 4),
                    LookDirection = new PortableVector3(0, 0, -4),
                    UpDirection = new PortableVector3(0, 1, 0),
                    NearPlaneDistance = 0.1,
                    FarPlaneDistance = 100,
                    FieldOfView = 60
                },
                AmbientIntensity = 0.25,
                Lights = Lights,
                Meshes = new[]
                {
                    new PortableViewport3DMesh
                    {
                        Geometry = GeometryKey,
                        GeometryVersion = 13,
                        Positions = new[]
                        {
                            new PortableVector3(0, 0, 0),
                            new PortableVector3(1, 0, 0),
                            new PortableVector3(0, 1, 0)
                        },
                        Normals = new[]
                        {
                            new PortableVector3(0, 0, 1),
                            new PortableVector3(0, 0, 1),
                            new PortableVector3(0, 0, 1)
                        },
                        Indices = new[] { 0, 1, 2 },
                        ModelTransform = new PortableMatrix4x4(
                            1, 0, 0, 0,
                            0, 1, 0, 0,
                            0, 0, 1, 0,
                            10, 0, 0, 1),
                        DiffuseColor = new PortableColor4(1, 0, 0, 1)
                    }
                }
            };
            return true;
        }
    }

    private sealed class PortableSceneHost : IPortableViewport3DSceneSource
    {
        public bool TryGetPortableViewport3DScene(out PortableViewport3DScene scene)
        {
            scene = new PortableViewport3DScene();
            return true;
        }
    }

    private sealed class FakePoint3DCollection
    {
        private readonly FakePoint3D[] _items;

        public FakePoint3DCollection(params FakePoint3D[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public FakePoint3D this[int index] => _items[index];
    }

    private sealed class FakeVector3DCollection
    {
        private readonly FakeVector3D[] _items;

        public FakeVector3DCollection(params FakeVector3D[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public FakeVector3D this[int index] => _items[index];
    }

    private sealed class FakeInt32Collection
    {
        private readonly int[] _items;

        public FakeInt32Collection(params int[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;

        public int this[int index] => _items[index];
    }

    private readonly record struct FakeRect(double X, double Y, double Width, double Height);

    private readonly record struct FakePoint3D(double X, double Y, double Z);

    private readonly record struct FakeVector3D(double X, double Y, double Z);

    private sealed class ViewportSink : IWpfCompositionCommandSink, IWpfViewport3DCommandSink
    {
        public int DrawViewport3DCount { get; private set; }

        public bool DrawViewport3DResult { get; init; }

        public MediaDrawingContext DrawingContext => null!;

        public bool DrawViewport3D(object viewportVisual)
        {
            DrawViewport3DCount++;
            return DrawViewport3DResult;
        }

        public void DrawLine(MediaPen? pen, Point point0, Point point1)
        {
        }

        public void DrawRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle)
        {
        }

        public void DrawRoundedRectangle(MediaBrush? brush, MediaPen? pen, Rect rectangle, double radiusX, double radiusY)
        {
        }

        public void DrawEllipse(MediaBrush? brush, MediaPen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public void DrawGeometry(MediaBrush? brush, MediaPen? pen, MediaGeometry geometry)
        {
        }

        public void DrawImage(MediaImageSource imageSource, Rect rectangle)
        {
        }

        public void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public void DrawGlyphRun(MediaBrush? foregroundBrush, MediaGlyphRun glyphRun)
        {
        }

        public void PushClip(MediaGeometry clipGeometry)
        {
        }

        public void PushOpacity(double opacity)
        {
        }

        public void PushOpacityMask(MediaBrush? opacityMask, Rect bounds)
        {
        }

        public void PushTransform(MediaTransform transform)
        {
        }

        public void PushGuidelineSet()
        {
        }

        public void Pop()
        {
        }

        public void Close()
        {
        }

        public void Dispose()
        {
        }
    }
}

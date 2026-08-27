// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;

namespace System.Windows.Media.Media3D.Tests;

public sealed class PortableViewport3DSceneTests
{
    [Fact]
    public void SourceBuiltViewportExportsTypedFrontAndBackMeshes()
    {
        var mesh = new MeshGeometry3D
        {
            Positions = new Point3DCollection
            {
                new Point3D(-1, -1, 0),
                new Point3D(1, -1, 0),
                new Point3D(0, 1, 0)
            },
            TriangleIndices = new Int32Collection { 0, 1, 2 }
        };
        var frontBrush = new SolidColorBrush(
            Color.FromArgb(128, 200, 100, 50))
        {
            Opacity = 0.5
        };
        var modelTransform = new TranslateTransform3D(1, 2, 3);
        var model = new GeometryModel3D(
            mesh,
            new DiffuseMaterial(frontBrush))
        {
            BackMaterial = new DiffuseMaterial(Brushes.Blue),
            Transform = modelTransform
        };
        var visualTransform = new ScaleTransform3D(2, 3, 4);
        var modelVisual = new ModelVisual3D
        {
            Content = model,
            Transform = visualTransform
        };
        var viewport = new Viewport3DVisual
        {
            Viewport = new Rect(12, 18, 80, 60),
            Camera = new PerspectiveCamera
            {
                Position = new Point3D(0, 0, 5),
                LookDirection = new Vector3D(0, 0, -1),
                UpDirection = new Vector3D(0, 1, 0),
                NearPlaneDistance = 0.25,
                FarPlaneDistance = 250,
                FieldOfView = 50
            }
        };
        viewport.Children.Add(modelVisual);

        var source = Assert.IsAssignableFrom<IPortableViewport3DSceneSource>(
            viewport);

        Assert.True(source.TryGetPortableViewport3DScene(out var scene));
        PortableViewport3DCamera camera = Assert.IsType<PortableViewport3DCamera>(
            scene.Camera);
        Assert.Equal(new PortableRect(12, 18, 80, 60), scene.Viewport);
        Assert.Equal(PortableViewport3DCameraKind.Perspective, camera.Kind);
        Assert.Equal(new PortableVector3(0, 0, 5), camera.Position);
        Assert.Equal(0.25, camera.NearPlaneDistance);
        Assert.Equal(250, camera.FarPlaneDistance);
        Assert.Equal(50, camera.FieldOfView);

        Assert.Equal(2, scene.Meshes.Length);
        PortableViewport3DMesh front = scene.Meshes[0];
        PortableViewport3DMesh back = scene.Meshes[1];
        Assert.False(front.IsBackFace);
        Assert.True(back.IsBackFace);
        Assert.Same(mesh, front.Geometry);
        Assert.Same(mesh, back.Geometry);
        Assert.Equal([0, 1, 2], front.Indices);
        Assert.Equal([0, 1, 2], back.Indices);
        Assert.All(front.Normals, normal =>
            Assert.Equal(new PortableVector3(0, 0, 1), normal));

        Matrix3D expectedTransform =
            modelTransform.Value * visualTransform.Value;
        Assert.Equal(ToPortableMatrix(expectedTransform), front.ModelTransform);
        Assert.Equal(front.ModelTransform, back.ModelTransform);
        Assert.Equal(200 / 255.0, front.DiffuseColor.R);
        Assert.Equal(100 / 255.0, front.DiffuseColor.G);
        Assert.Equal(50 / 255.0, front.DiffuseColor.B);
        Assert.Equal(0.5 * 128 / 255.0, front.Opacity, 6);
        Assert.Equal(new PortableColor4(0, 0, 1, 1), back.DiffuseColor);
    }

    [Fact]
    public void SourceBuiltViewportFailsClosedWithoutUsableScene()
    {
        var viewport = new Viewport3DVisual
        {
            Viewport = Rect.Empty,
            Camera = new PerspectiveCamera()
        };
        var source = Assert.IsAssignableFrom<IPortableViewport3DSceneSource>(
            viewport);

        Assert.False(source.TryGetPortableViewport3DScene(out var scene));
        Assert.Null(scene);
    }

    private static PortableMatrix4x4 ToPortableMatrix(Matrix3D matrix) =>
        new(
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.OffsetX, matrix.OffsetY, matrix.OffsetZ, matrix.M44);
}

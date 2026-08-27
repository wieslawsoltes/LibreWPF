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
            TextureCoordinates = new PointCollection
            {
                new Point(0.25, 0.75),
                new Point(1, 0)
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
        var lightTransform = new RotateTransform3D(
            new AxisAngleRotation3D(new Vector3D(0, 1, 0), 90));
        var light = new DirectionalLight(
            Colors.White,
            new Vector3D(0, 0, -1))
        {
            Transform = lightTransform
        };
        var ambientLight = new AmbientLight(Colors.Gray);
        var pointLight = new PointLight(
            Colors.Red,
            new Point3D(1, 2, 3))
        {
            Range = 40,
            ConstantAttenuation = 0.5,
            LinearAttenuation = 0.25,
            QuadraticAttenuation = 0.125,
            Transform = new TranslateTransform3D(4, 5, 6)
        };
        var spotLight = new SpotLight(
            Colors.Green,
            new Point3D(-1, 1, 2),
            new Vector3D(0, 0, -2),
            70,
            30)
        {
            Range = 80,
            ConstantAttenuation = 0.75,
            LinearAttenuation = 0.125,
            QuadraticAttenuation = 0.0625
        };
        var group = new Model3DGroup();
        group.Children.Add(light);
        group.Children.Add(ambientLight);
        group.Children.Add(pointLight);
        group.Children.Add(spotLight);
        group.Children.Add(model);
        var modelVisual = new ModelVisual3D
        {
            Content = group,
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
        Vector3D expectedLightDirection =
            (lightTransform.Value * visualTransform.Value).Transform(
                light.Direction);
        expectedLightDirection.Normalize();
        Assert.Equal(expectedLightDirection.X, scene.LightDirection.X, 6);
        Assert.Equal(expectedLightDirection.Y, scene.LightDirection.Y, 6);
        Assert.Equal(expectedLightDirection.Z, scene.LightDirection.Z, 6);

        Assert.Equal(4, scene.Lights.Length);
        Assert.Equal(
            PortableViewport3DLightKind.Directional,
            scene.Lights[0].Kind);
        Assert.Equal(
            new PortableVector3(
                expectedLightDirection.X,
                expectedLightDirection.Y,
                expectedLightDirection.Z),
            scene.Lights[0].Direction);
        Assert.Equal(
            PortableViewport3DLightKind.Ambient,
            scene.Lights[1].Kind);
        Assert.Equal(
            PortableViewport3DLightKind.Point,
            scene.Lights[2].Kind);
        Point3D expectedPointPosition =
            (pointLight.Transform.Value * visualTransform.Value).Transform(
                pointLight.Position);
        Assert.Equal(
            new PortableVector3(
                expectedPointPosition.X,
                expectedPointPosition.Y,
                expectedPointPosition.Z),
            scene.Lights[2].Position);
        Assert.Equal(40, scene.Lights[2].Range);
        Assert.Equal(0.5, scene.Lights[2].ConstantAttenuation);
        Assert.Equal(0.25, scene.Lights[2].LinearAttenuation);
        Assert.Equal(0.125, scene.Lights[2].QuadraticAttenuation);
        Assert.Equal(
            PortableViewport3DLightKind.Spot,
            scene.Lights[3].Kind);
        Point3D expectedSpotPosition = visualTransform.Value.Transform(
            spotLight.Position);
        Assert.Equal(
            new PortableVector3(
                expectedSpotPosition.X,
                expectedSpotPosition.Y,
                expectedSpotPosition.Z),
            scene.Lights[3].Position);
        Assert.Equal(new PortableVector3(0, 0, -1), scene.Lights[3].Direction);
        Assert.Equal(70, scene.Lights[3].OuterConeAngle);
        Assert.Equal(30, scene.Lights[3].InnerConeAngle);

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
        Assert.Equal(
            [
                new PortablePoint(0.25, 0.75),
                new PortablePoint(1, 0),
                new PortablePoint(0, 0)
            ],
            front.TextureCoordinates);
        Assert.Equal(front.TextureCoordinates, back.TextureCoordinates);

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

    [Fact]
    public void SourceBuiltViewportExportsTypedMatrixCamera()
    {
        var view = Matrix3D.Identity;
        view.Translate(new Vector3D(-2, -3, -4));
        var projection = new Matrix3D(
            2, 0, 0, 0,
            0, 3, 0, 0,
            0, 0, 4, 1,
            0, 0, -2, 0);
        var viewport = CreateRenderableViewport(
            new MatrixCamera(view, projection));
        var source = Assert.IsAssignableFrom<IPortableViewport3DSceneSource>(
            viewport);

        Assert.True(source.TryGetPortableViewport3DScene(out var scene));
        PortableViewport3DCamera camera = Assert.IsType<PortableViewport3DCamera>(
            scene.Camera);
        Assert.Equal(PortableViewport3DCameraKind.Matrix, camera.Kind);
        Assert.Equal(ToPortableMatrix(view), camera.ViewMatrix);
        Assert.Equal(ToPortableMatrix(projection), camera.ProjectionMatrix);
    }

    private static Viewport3DVisual CreateRenderableViewport(Camera camera)
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
        var viewport = new Viewport3DVisual
        {
            Viewport = new Rect(0, 0, 80, 60),
            Camera = camera
        };
        viewport.Children.Add(new ModelVisual3D
        {
            Content = new GeometryModel3D(
                mesh,
                new DiffuseMaterial(Brushes.Red))
        });
        return viewport;
    }

    private static PortableMatrix4x4 ToPortableMatrix(Matrix3D matrix) =>
        new(
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.OffsetX, matrix.OffsetY, matrix.OffsetZ, matrix.M44);
}

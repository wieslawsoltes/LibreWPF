// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.Media3D
{
    public sealed partial class Viewport3DVisual
    {
        private const double DefaultNearPlaneDistance = 0.125;
        private const double DefaultFarPlaneDistance = 1000.0;
        private const double DefaultPerspectiveFieldOfView = 45.0;
        private const double DefaultOrthographicWidth = 2.0;

        bool IPortableViewport3DSceneSource.TryGetPortableViewport3DScene(out PortableViewport3DScene scene)
        {
            var viewport = Viewport;
            if (!IsUsableViewport(viewport))
            {
                viewport = ContentBounds;
            }

            if (!IsUsableViewport(viewport)
                || !TryCreateCamera(Camera, out var camera))
            {
                scene = null!;
                return false;
            }

            var state = new PortableViewport3DExportState();
            foreach (Visual3D child in Children)
            {
                CompileVisual3D(child, Matrix3D.Identity, state);
            }

            scene = new PortableViewport3DScene
            {
                Viewport = new PortableRect(viewport.X, viewport.Y, viewport.Width, viewport.Height),
                Camera = camera,
                LightDirection = state.LightDirection,
                LightIntensity = state.LightIntensity,
                AmbientColor = state.AmbientColor,
                AmbientIntensity = state.AmbientIntensity,
                Lights = state.Lights.ToArray(),
                Meshes = state.Meshes.ToArray()
            };
            return scene.Meshes.Length > 0;
        }

        private static void CompileVisual3D(
            Visual3D visual,
            Matrix3D parentTransform,
            PortableViewport3DExportState state)
        {
            var localTransform = ReadTransform(visual.Transform) * parentTransform;

            if (visual is ModelVisual3D modelVisual)
            {
                if (modelVisual.Content != null)
                {
                    CompileModel3D(modelVisual.Content, localTransform, state);
                }

                foreach (Visual3D child in modelVisual.Children)
                {
                    CompileVisual3D(child, localTransform, state);
                }
            }
        }

        private static void CompileModel3D(
            Model3D model,
            Matrix3D parentTransform,
            PortableViewport3DExportState state)
        {
            var modelTransform = ReadTransform(model.Transform) * parentTransform;

            if (model is DirectionalLight directionalLight)
            {
                var transformedDirection = modelTransform.Transform(
                    directionalLight.Direction);
                var direction = new PortableVector3(
                    transformedDirection.X,
                    transformedDirection.Y,
                    transformedDirection.Z);
                state.LightDirection = NormalizeOrDefault(direction, state.LightDirection);
                var color = ToPortableColor4(directionalLight.Color);
                state.LightIntensity = Math.Max(color.R, Math.Max(color.G, color.B));
                state.Lights.Add(new PortableViewport3DLight
                {
                    Kind = PortableViewport3DLightKind.Directional,
                    Color = color,
                    Direction = state.LightDirection
                });
                return;
            }

            if (model is AmbientLight ambientLight)
            {
                var color = ToPortableColor4(ambientLight.Color);
                state.AmbientColor = new PortableVector3(color.R, color.G, color.B);
                state.AmbientIntensity = color.A;
                state.Lights.Add(new PortableViewport3DLight
                {
                    Kind = PortableViewport3DLightKind.Ambient,
                    Color = color
                });
                return;
            }

            if (model is SpotLight spotLight)
            {
                var position = modelTransform.Transform(spotLight.Position);
                var direction = modelTransform.Transform(spotLight.Direction);
                state.Lights.Add(new PortableViewport3DLight
                {
                    Kind = PortableViewport3DLightKind.Spot,
                    Color = ToPortableColor4(spotLight.Color),
                    Position = new PortableVector3(position.X, position.Y, position.Z),
                    Direction = NormalizeOrDefault(
                        new PortableVector3(direction.X, direction.Y, direction.Z),
                        new PortableVector3(0, 0, -1)),
                    Range = spotLight.Range,
                    ConstantAttenuation = spotLight.ConstantAttenuation,
                    LinearAttenuation = spotLight.LinearAttenuation,
                    QuadraticAttenuation = spotLight.QuadraticAttenuation,
                    OuterConeAngle = spotLight.OuterConeAngle,
                    InnerConeAngle = spotLight.InnerConeAngle
                });
                return;
            }

            if (model is PointLight pointLight)
            {
                var position = modelTransform.Transform(pointLight.Position);
                state.Lights.Add(new PortableViewport3DLight
                {
                    Kind = PortableViewport3DLightKind.Point,
                    Color = ToPortableColor4(pointLight.Color),
                    Position = new PortableVector3(position.X, position.Y, position.Z),
                    Range = pointLight.Range,
                    ConstantAttenuation = pointLight.ConstantAttenuation,
                    LinearAttenuation = pointLight.LinearAttenuation,
                    QuadraticAttenuation = pointLight.QuadraticAttenuation
                });
                return;
            }

            if (model is Model3DGroup group)
            {
                foreach (Model3D child in group.Children)
                {
                    CompileModel3D(child, modelTransform, state);
                }

                return;
            }

            if (model is not GeometryModel3D geometryModel
                || geometryModel.Geometry is not MeshGeometry3D mesh
                || !TryCreateMeshData(mesh, out var positions, out var normals, out var indices))
            {
                return;
            }

            var material = ReadMaterial(geometryModel.Material);
            var backMaterial = ReadMaterial(geometryModel.BackMaterial);
            if (geometryModel.Material != null || geometryModel.BackMaterial == null)
            {
                state.Meshes.Add(CreateMesh(
                    mesh,
                    positions,
                    normals,
                    indices,
                    modelTransform,
                    material,
                    isBackFace: false));
            }

            if (geometryModel.BackMaterial != null)
            {
                state.Meshes.Add(CreateMesh(
                    mesh,
                    positions,
                    normals,
                    indices,
                    modelTransform,
                    backMaterial,
                    isBackFace: true));
            }
        }

        private static PortableViewport3DMesh CreateMesh(
            MeshGeometry3D mesh,
            PortableVector3[] positions,
            PortableVector3[] normals,
            int[] indices,
            Matrix3D modelTransform,
            MaterialDescriptor material,
            bool isBackFace)
        {
            return new PortableViewport3DMesh
            {
                Geometry = mesh,
                Positions = positions,
                Normals = normals,
                Indices = indices,
                ModelTransform = ToPortableMatrix(modelTransform),
                DiffuseColor = material.DiffuseColor,
                SpecularColor = new PortableColor4(material.SpecularColor.X, material.SpecularColor.Y, material.SpecularColor.Z, 1),
                Shininess = material.Shininess,
                AmbientColor = material.AmbientColor,
                Opacity = material.Opacity,
                IsBackFace = isBackFace
            };
        }

        private static bool TryCreateMeshData(
            MeshGeometry3D mesh,
            out PortableVector3[] positions,
            out PortableVector3[] normals,
            out int[] indices)
        {
            positions = ReadPoint3DCollection(mesh.Positions);
            if (positions.Length == 0)
            {
                normals = Array.Empty<PortableVector3>();
                indices = Array.Empty<int>();
                return false;
            }

            indices = ReadInt32Collection(mesh.TriangleIndices);
            if (indices.Length == 0)
            {
                indices = CreateSequentialTriangleIndices(positions.Length);
            }

            normals = ReadVector3DCollection(mesh.Normals);
            if (normals.Length != positions.Length)
            {
                normals = ComputeNormals(positions, indices);
            }

            return indices.Length > 0;
        }

        private static PortableViewport3DCamera CreateCamera(
            ProjectionCamera camera,
            PortableViewport3DCameraKind kind,
            double fieldOfView,
            double width)
        {
            var nearPlane = camera.NearPlaneDistance > 0 ? camera.NearPlaneDistance : DefaultNearPlaneDistance;
            var farPlane = camera.FarPlaneDistance > nearPlane ? camera.FarPlaneDistance : nearPlane + 1.0;
            var transform = ReadTransform(camera.Transform);

            return new PortableViewport3DCamera
            {
                Kind = kind,
                Position = new PortableVector3(camera.Position.X, camera.Position.Y, camera.Position.Z),
                LookDirection = new PortableVector3(camera.LookDirection.X, camera.LookDirection.Y, camera.LookDirection.Z),
                UpDirection = new PortableVector3(camera.UpDirection.X, camera.UpDirection.Y, camera.UpDirection.Z),
                NearPlaneDistance = nearPlane,
                FarPlaneDistance = farPlane,
                FieldOfView = fieldOfView,
                Width = width,
                HasTransform = !transform.IsIdentity,
                Transform = ToPortableMatrix(transform)
            };
        }

        private static bool TryCreateCamera(Camera camera, out PortableViewport3DCamera portableCamera)
        {
            portableCamera = null!;
            if (camera is PerspectiveCamera perspectiveCamera)
            {
                portableCamera = CreateCamera(
                    perspectiveCamera,
                    PortableViewport3DCameraKind.Perspective,
                    perspectiveCamera.FieldOfView > 0 ? perspectiveCamera.FieldOfView : DefaultPerspectiveFieldOfView,
                    DefaultOrthographicWidth);
                return true;
            }

            if (camera is OrthographicCamera orthographicCamera)
            {
                portableCamera = CreateCamera(
                    orthographicCamera,
                    PortableViewport3DCameraKind.Orthographic,
                    DefaultPerspectiveFieldOfView,
                    orthographicCamera.Width > 0 ? orthographicCamera.Width : DefaultOrthographicWidth);
                return true;
            }

            return false;
        }

        private static MaterialDescriptor ReadMaterial(Material material)
        {
            var descriptor = MaterialDescriptor.Default;
            if (material == null)
            {
                return descriptor;
            }

            if (material is MaterialGroup group)
            {
                foreach (Material child in group.Children)
                {
                    descriptor = descriptor.Merge(ReadMaterial(child));
                }

                return descriptor;
            }

            if (material is DiffuseMaterial diffuse)
            {
                descriptor = descriptor with
                {
                    DiffuseColor = ReadBrushColor(diffuse.Brush, descriptor.DiffuseColor)
                };
                descriptor = descriptor with
                {
                    DiffuseColor = MultiplyColor(descriptor.DiffuseColor, ToPortableColor4(diffuse.Color)),
                    AmbientColor = ToPortableVector3(diffuse.AmbientColor)
                };
            }

            if (material is SpecularMaterial specular)
            {
                var specularColor = ReadBrushColor(specular.Brush, new PortableColor4(
                    descriptor.SpecularColor.X,
                    descriptor.SpecularColor.Y,
                    descriptor.SpecularColor.Z,
                    1));
                descriptor = descriptor with
                {
                    SpecularColor = new PortableVector3(specularColor.R, specularColor.G, specularColor.B),
                    Shininess = Math.Clamp(specular.SpecularPower, 1, 256)
                };
            }

            return descriptor with
            {
                Opacity = descriptor.Opacity * Math.Clamp(descriptor.DiffuseColor.A, 0, 1),
                DiffuseColor = new PortableColor4(descriptor.DiffuseColor.R, descriptor.DiffuseColor.G, descriptor.DiffuseColor.B, 1)
            };
        }

        private static PortableColor4 ReadBrushColor(Brush brush, PortableColor4 fallback)
        {
            if (brush is not IPortableBrushSource portableSource
                || !portableSource.TryGetPortableBrush(out var portableBrush))
            {
                return fallback;
            }

            var opacity = Math.Clamp(portableBrush.Opacity, 0, 1);
            return portableBrush.Kind switch
            {
                PortableBrushKind.SolidColor => ApplyOpacity(ToPortableColor4(portableBrush.Color), opacity),
                PortableBrushKind.LinearGradient when portableBrush.GradientStops.Length > 0 =>
                    ApplyOpacity(ToPortableColor4(portableBrush.GradientStops[0].Color), opacity),
                PortableBrushKind.RadialGradient when portableBrush.GradientStops.Length > 0 =>
                    ApplyOpacity(ToPortableColor4(portableBrush.GradientStops[0].Color), opacity),
                _ => fallback
            };
        }

        private static PortableColor4 ApplyOpacity(PortableColor4 color, double opacity)
        {
            return new PortableColor4(color.R, color.G, color.B, color.A * opacity);
        }

        private static PortableColor4 MultiplyColor(PortableColor4 left, PortableColor4 right)
        {
            return new PortableColor4(
                left.R * right.R,
                left.G * right.G,
                left.B * right.B,
                left.A * right.A);
        }

        private static PortableVector3[] ReadPoint3DCollection(Point3DCollection collection)
        {
            if (collection == null || collection.Count == 0)
            {
                return Array.Empty<PortableVector3>();
            }

            var values = new PortableVector3[collection.Count];
            for (var i = 0; i < values.Length; i++)
            {
                var point = collection[i];
                values[i] = new PortableVector3(point.X, point.Y, point.Z);
            }

            return values;
        }

        private static PortableVector3[] ReadVector3DCollection(Vector3DCollection collection)
        {
            if (collection == null || collection.Count == 0)
            {
                return Array.Empty<PortableVector3>();
            }

            var values = new PortableVector3[collection.Count];
            for (var i = 0; i < values.Length; i++)
            {
                var vector = collection[i];
                values[i] = new PortableVector3(vector.X, vector.Y, vector.Z);
            }

            return values;
        }

        private static int[] ReadInt32Collection(Int32Collection collection)
        {
            if (collection == null || collection.Count == 0)
            {
                return Array.Empty<int>();
            }

            var values = new int[collection.Count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = collection[i];
            }

            return values;
        }

        private static int[] CreateSequentialTriangleIndices(int positionCount)
        {
            var triangleCount = positionCount / 3;
            if (triangleCount == 0)
            {
                return Array.Empty<int>();
            }

            var indices = new int[triangleCount * 3];
            for (var i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }

            return indices;
        }

        private static PortableVector3[] ComputeNormals(PortableVector3[] positions, int[] indices)
        {
            var normals = new PortableVector3[positions.Length];
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var i0 = indices[i];
                var i1 = indices[i + 1];
                var i2 = indices[i + 2];
                if ((uint)i0 >= positions.Length || (uint)i1 >= positions.Length || (uint)i2 >= positions.Length)
                {
                    continue;
                }

                var edge1 = Subtract(positions[i1], positions[i0]);
                var edge2 = Subtract(positions[i2], positions[i0]);
                var normal = NormalizeOrDefault(Cross(edge1, edge2), default);
                if (LengthSquared(normal) <= 0.000001)
                {
                    continue;
                }

                normals[i0] = Add(normals[i0], normal);
                normals[i1] = Add(normals[i1], normal);
                normals[i2] = Add(normals[i2], normal);
            }

            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = NormalizeOrDefault(normals[i], new PortableVector3(0, 0, 1));
            }

            return normals;
        }

        private static Matrix3D ReadTransform(Transform3D transform)
        {
            return transform == null ? Matrix3D.Identity : transform.Value;
        }

        private static PortableMatrix4x4 ToPortableMatrix(Matrix3D matrix)
        {
            return new PortableMatrix4x4(
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.OffsetX, matrix.OffsetY, matrix.OffsetZ, matrix.M44);
        }

        private static PortableColor4 ToPortableColor4(Color color)
        {
            return new PortableColor4(
                color.R / 255.0,
                color.G / 255.0,
                color.B / 255.0,
                color.A / 255.0);
        }

        private static PortableColor4 ToPortableColor4(PortableColor color)
        {
            return new PortableColor4(
                color.R / 255.0,
                color.G / 255.0,
                color.B / 255.0,
                color.A / 255.0);
        }

        private static PortableVector3 ToPortableVector3(Color color)
        {
            return new PortableVector3(color.R / 255.0, color.G / 255.0, color.B / 255.0);
        }

        private static PortableVector3 Add(PortableVector3 left, PortableVector3 right)
        {
            return new PortableVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        private static PortableVector3 Subtract(PortableVector3 left, PortableVector3 right)
        {
            return new PortableVector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        private static PortableVector3 Cross(PortableVector3 left, PortableVector3 right)
        {
            return new PortableVector3(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);
        }

        private static double LengthSquared(PortableVector3 value)
        {
            return value.X * value.X + value.Y * value.Y + value.Z * value.Z;
        }

        private static PortableVector3 NormalizeOrDefault(PortableVector3 value, PortableVector3 fallback)
        {
            var lengthSquared = LengthSquared(value);
            if (lengthSquared <= 0.000001)
            {
                return fallback;
            }

            var length = Math.Sqrt(lengthSquared);
            return new PortableVector3(value.X / length, value.Y / length, value.Z / length);
        }

        private static bool IsUsableViewport(Rect viewport)
        {
            return double.IsFinite(viewport.X)
                && double.IsFinite(viewport.Y)
                && double.IsFinite(viewport.Width)
                && double.IsFinite(viewport.Height)
                && viewport.Width > 0
                && viewport.Height > 0;
        }

        private sealed class PortableViewport3DExportState
        {
            public List<PortableViewport3DMesh> Meshes { get; } = new();

            public List<PortableViewport3DLight> Lights { get; } = new();

            public PortableVector3 LightDirection { get; set; } = new(0.5, 1.0, -0.5);

            public double LightIntensity { get; set; } = 1.0;

            public PortableVector3 AmbientColor { get; set; } = new(1.0, 1.0, 1.0);

            public double AmbientIntensity { get; set; } = 0.2;
        }

        private readonly record struct MaterialDescriptor(
            PortableColor4 DiffuseColor,
            PortableVector3 SpecularColor,
            double Shininess,
            PortableVector3 AmbientColor,
            double Opacity)
        {
            public static MaterialDescriptor Default { get; } = new(
                new PortableColor4(1, 1, 1, 1),
                new PortableVector3(0.2, 0.2, 0.2),
                32.0,
                new PortableVector3(0.2, 0.2, 0.2),
                1.0);

            public MaterialDescriptor Merge(MaterialDescriptor next)
            {
                var defaultDiffuse = new PortableColor4(1, 1, 1, 1);
                var defaultVector = new PortableVector3(0.2, 0.2, 0.2);
                return new MaterialDescriptor(
                    !ColorEquals(next.DiffuseColor, defaultDiffuse) ? next.DiffuseColor : DiffuseColor,
                    !VectorEquals(next.SpecularColor, defaultVector) ? next.SpecularColor : SpecularColor,
                    next.Shininess != 32.0 ? next.Shininess : Shininess,
                    !VectorEquals(next.AmbientColor, defaultVector) ? next.AmbientColor : AmbientColor,
                    next.Opacity != 1.0 ? next.Opacity : Opacity);
            }
        }

        private static bool ColorEquals(PortableColor4 left, PortableColor4 right)
        {
            return left.R == right.R
                && left.G == right.G
                && left.B == right.B
                && left.A == right.A;
        }

        private static bool VectorEquals(PortableVector3 left, PortableVector3 right)
        {
            return left.X == right.X
                && left.Y == right.Y
                && left.Z == right.Z;
        }
    }
}

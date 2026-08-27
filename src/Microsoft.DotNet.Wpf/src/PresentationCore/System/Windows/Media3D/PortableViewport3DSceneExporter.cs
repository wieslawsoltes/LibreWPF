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
                || !TryCreateMeshData(
                    mesh,
                    out var positions,
                    out var normals,
                    out var textureCoordinates,
                    out var indices))
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
                    textureCoordinates,
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
                    textureCoordinates,
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
            PortablePoint[] textureCoordinates,
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
                TextureCoordinates = textureCoordinates,
                Indices = indices,
                ModelTransform = ToPortableMatrix(modelTransform),
                DiffuseColor = material.DiffuseColor,
                SpecularColor = new PortableColor4(material.SpecularColor.X, material.SpecularColor.Y, material.SpecularColor.Z, 1),
                Shininess = material.Shininess,
                AmbientColor = material.AmbientColor,
                Opacity = material.Opacity,
                IsBackFace = isBackFace,
                Materials = material.Materials
            };
        }

        private static bool TryCreateMeshData(
            MeshGeometry3D mesh,
            out PortableVector3[] positions,
            out PortableVector3[] normals,
            out PortablePoint[] textureCoordinates,
            out int[] indices)
        {
            positions = ReadPoint3DCollection(mesh.Positions);
            if (positions.Length == 0)
            {
                normals = Array.Empty<PortableVector3>();
                textureCoordinates = Array.Empty<PortablePoint>();
                indices = Array.Empty<int>();
                return false;
            }

            indices = ReadInt32Collection(mesh.TriangleIndices);
            if (indices.Length == 0)
            {
                indices = CreateSequentialTriangleIndices(positions.Length);
            }

            var suppliedNormals = ReadVector3DCollection(mesh.Normals);
            if (suppliedNormals.Length < positions.Length)
            {
                normals = ComputeNormals(positions, indices);
            }
            else
            {
                normals = new PortableVector3[positions.Length];
            }

            var suppliedNormalCount = Math.Min(
                suppliedNormals.Length,
                positions.Length);
            for (var i = 0; i < suppliedNormalCount; i++)
            {
                normals[i] = NormalizeOrZero(suppliedNormals[i]);
            }

            textureCoordinates = ReadTextureCoordinates(
                mesh.TextureCoordinates,
                positions.Length);

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
            if (camera is MatrixCamera matrixCamera)
            {
                portableCamera = new PortableViewport3DCamera
                {
                    Kind = PortableViewport3DCameraKind.Matrix,
                    ViewMatrix = ToPortableMatrix(matrixCamera.GetViewMatrix()),
                    ProjectionMatrix = ToPortableMatrix(matrixCamera.ProjectionMatrix)
                };
                return true;
            }

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
            var layers = new List<PortableViewport3DMaterial>();
            AppendMaterialLayers(material, layers);
            return MaterialDescriptor.FromLayers(layers.ToArray());
        }

        private static void AppendMaterialLayers(
            Material material,
            List<PortableViewport3DMaterial> layers)
        {
            if (material == null)
            {
                return;
            }

            if (material is MaterialGroup group)
            {
                foreach (Material child in group.Children)
                {
                    AppendMaterialLayers(child, layers);
                }
                return;
            }

            if (material is DiffuseMaterial diffuse && diffuse.Brush != null)
            {
                layers.Add(CreateMaterialLayer(
                    PortableViewport3DMaterialKind.Diffuse,
                    diffuse.Brush,
                    ToPortableColor4(diffuse.Color),
                    ToPortableVector3(diffuse.AmbientColor),
                    1.0));
                return;
            }

            if (material is SpecularMaterial specular && specular.Brush != null)
            {
                layers.Add(CreateMaterialLayer(
                    PortableViewport3DMaterialKind.Specular,
                    specular.Brush,
                    ToPortableColor4(specular.Color),
                    default,
                    specular.SpecularPower));
                return;
            }

            if (material is EmissiveMaterial emissive && emissive.Brush != null)
            {
                layers.Add(CreateMaterialLayer(
                    PortableViewport3DMaterialKind.Emissive,
                    emissive.Brush,
                    ToPortableColor4(emissive.Color),
                    default,
                    1.0));
            }
        }

        private static PortableViewport3DMaterial CreateMaterialLayer(
            PortableViewport3DMaterialKind kind,
            Brush brush,
            PortableColor4 color,
            PortableVector3 ambientColor,
            double specularPower)
        {
            var layer = new PortableViewport3DMaterial
            {
                Kind = kind,
                Color = color,
                AmbientColor = ambientColor,
                SpecularPower = specularPower
            };
            if (brush is IPortableBrushSource brushSource
                && brushSource.TryGetPortableBrush(out var portableBrush))
            {
                layer.Brush = portableBrush;
            }
            else if (brush is IPortableTileBrushSource tileBrushSource
                && tileBrushSource.TryGetPortableTileBrush(out var tileBrush))
            {
                layer.TileBrush = tileBrush;
            }
            return layer;
        }

        private static PortableColor4 ReadPortableBrushColor(
            PortableBrush brush,
            PortableColor4 fallback)
        {
            if (brush == null)
            {
                return fallback;
            }

            var opacity = Math.Clamp(brush.Opacity, 0, 1);
            return brush.Kind switch
            {
                PortableBrushKind.SolidColor =>
                    ApplyOpacity(ToPortableColor4(brush.Color), opacity),
                PortableBrushKind.LinearGradient when brush.GradientStops.Length > 0 =>
                    ApplyOpacity(ToPortableColor4(brush.GradientStops[0].Color), opacity),
                PortableBrushKind.RadialGradient when brush.GradientStops.Length > 0 =>
                    ApplyOpacity(ToPortableColor4(brush.GradientStops[0].Color), opacity),
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

        private static PortablePoint[] ReadTextureCoordinates(
            PointCollection collection,
            int vertexCount)
        {
            if (collection == null || collection.Count == 0 || vertexCount == 0)
            {
                return Array.Empty<PortablePoint>();
            }

            var values = new PortablePoint[vertexCount];
            var count = Math.Min(collection.Count, vertexCount);
            for (var i = 0; i < count; i++)
            {
                var point = collection[i];
                values[i] = new PortablePoint(point.X, point.Y);
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
                var normal = NormalizeOrZero(Cross(edge1, edge2));
                if (LengthSquared(normal) == 0)
                {
                    continue;
                }

                normals[i0] = Add(normals[i0], normal);
                normals[i1] = Add(normals[i1], normal);
                normals[i2] = Add(normals[i2], normal);
            }

            for (var i = 0; i < normals.Length; i++)
            {
                normals[i] = NormalizeOrZero(normals[i]);
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

        private static PortableVector3 NormalizeOrZero(PortableVector3 value)
        {
            var lengthSquared = LengthSquared(value);
            if (!double.IsFinite(lengthSquared))
            {
                // Preserve malformed input for the typed consumer to reject;
                // silently converting it to a valid zero normal would hide a
                // scene-contract error.
                return value;
            }
            if (!(lengthSquared > 0))
            {
                return default;
            }

            var inverseLength = 1.0 / Math.Sqrt(lengthSquared);
            return new PortableVector3(
                value.X * inverseLength,
                value.Y * inverseLength,
                value.Z * inverseLength);
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
            double Opacity,
            PortableViewport3DMaterial[] Materials)
        {
            public static MaterialDescriptor Default { get; } = new(
                new PortableColor4(1, 1, 1, 1),
                new PortableVector3(0.2, 0.2, 0.2),
                32.0,
                new PortableVector3(0.2, 0.2, 0.2),
                1.0,
                Array.Empty<PortableViewport3DMaterial>());

            public static MaterialDescriptor FromLayers(
                PortableViewport3DMaterial[] layers)
            {
                var descriptor = Default;
                for (var i = 0; i < layers.Length; i++)
                {
                    var layer = layers[i];
                    if (layer.Kind == PortableViewport3DMaterialKind.Diffuse)
                    {
                        var diffuseColor = ReadPortableBrushColor(
                            layer.Brush,
                            descriptor.DiffuseColor);
                        diffuseColor = MultiplyColor(
                            diffuseColor,
                            layer.Color);
                        descriptor = descriptor with
                        {
                            DiffuseColor = new PortableColor4(
                                diffuseColor.R,
                                diffuseColor.G,
                                diffuseColor.B,
                                1),
                            AmbientColor = layer.AmbientColor,
                            Opacity = Math.Clamp(diffuseColor.A, 0, 1)
                        };
                    }
                    else if (layer.Kind == PortableViewport3DMaterialKind.Specular)
                    {
                        var specularColor = ReadPortableBrushColor(
                            layer.Brush,
                            layer.Color);
                        descriptor = descriptor with
                        {
                            SpecularColor = new PortableVector3(
                                specularColor.R,
                                specularColor.G,
                                specularColor.B),
                            Shininess = Math.Clamp(
                                layer.SpecularPower,
                                1,
                                256)
                        };
                    }
                }
                return descriptor with { Materials = layers };
            }
        }

    }
}

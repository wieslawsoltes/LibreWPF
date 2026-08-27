using System;
using System.Numerics;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public static class WpfViewport3DSceneBridge
{
    private const float DefaultNearPlaneDistance = 0.125f;
    private const float DefaultPerspectiveFieldOfView = 45f;
    private const float DefaultOrthographicWidth = 2f;

    public static bool TryCreateReplayData(object viewportVisual, out WpfViewport3DReplayData replayData)
    {
        return TryCreateReplayData(viewportVisual, textureCache: null, out replayData);
    }

    internal static bool TryCreateReplayData(
        object viewportVisual,
        WpfViewport3DTextureCache? textureCache,
        out WpfViewport3DReplayData replayData)
    {
        ArgumentNullException.ThrowIfNull(viewportVisual);

        if (viewportVisual is not IPortableViewport3DSceneSource portableSceneSource
            || !portableSceneSource.TryGetPortableViewport3DScene(out var scene))
        {
            replayData = default;
            return false;
        }

        return TryCreateReplayDataFromPortableScene(
            viewportVisual,
            scene,
            textureCache,
            out replayData);
    }

    private static bool TryCreateReplayDataFromPortableScene(
        object viewportVisual,
        PortableViewport3DScene scene,
        WpfViewport3DTextureCache? textureCache,
        out WpfViewport3DReplayData replayData)
    {
        replayData = default;
        if (scene.Camera == null
            || scene.Viewport.IsEmpty
            || scene.Viewport.Width <= 0
            || scene.Viewport.Height <= 0)
        {
            return false;
        }

        var viewportWidth = Math.Max(1f, (float)scene.Viewport.Width);
        var viewportHeight = Math.Max(1f, (float)scene.Viewport.Height);
        var aspectRatio = viewportWidth / viewportHeight;
        if (!TryCreateCameraMatrices(scene.Camera, aspectRatio, out var projection, out var view))
        {
            return false;
        }

        var payload = new global::ProGPU.Scene.Extensions.Viewport3DCompilationPayload
        {
            ViewportSize = new Vector2(viewportWidth, viewportHeight),
            LightDirection = ToVector3(scene.LightDirection),
            LightIntensity = (float)Math.Clamp(scene.LightIntensity, 0, double.MaxValue),
            AmbientColor = ToVector3(scene.AmbientColor),
            AmbientIntensity = (float)Math.Clamp(scene.AmbientIntensity, 0, double.MaxValue)
        };
        if (!TryAddPortableLights(scene.Lights, payload))
        {
            return false;
        }

        if (textureCache != null)
        {
            var textures = textureCache.GetOrCreate(
                viewportVisual,
                (uint)Math.Ceiling(viewportWidth),
                (uint)Math.Ceiling(viewportHeight));
            payload.ColorTexture = textures.ColorTexture;
            payload.MsaaColorTexture = textures.MsaaColorTexture;
            payload.DepthTexture = textures.DepthTexture;
        }

        var meshes = scene.Meshes;
        for (var meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
        {
            var mesh = meshes[meshIndex];
            if (mesh == null)
            {
                continue;
            }
            if (mesh.Positions == null
                || mesh.Normals == null
                || mesh.Indices == null
                || mesh.Normals.Length < mesh.Positions.Length)
            {
                return false;
            }
            if (mesh.Positions.Length == 0 || mesh.Indices.Length == 0)
            {
                continue;
            }
            if (!TryToVector3Array(
                    mesh.Positions,
                    mesh.Positions.Length,
                    normalize: false,
                    out Vector3[] positions)
                || !TryToVector3Array(
                    mesh.Normals,
                    mesh.Positions.Length,
                    normalize: true,
                    out Vector3[] normals))
            {
                return false;
            }
            if (!TryToVector2Array(
                    mesh.TextureCoordinates,
                    out Vector2[] textureCoordinates))
            {
                return false;
            }

            if (mesh.Materials is null)
            {
                return false;
            }
            PortableViewport3DMaterial[] materials = mesh.Materials;
            if (materials.Length == 0)
            {
                payload.Meshes.Add(CreateMeshEntry(
                    mesh,
                    positions,
                    normals,
                    textureCoordinates));
                continue;
            }

            for (var materialIndex = 0;
                 materialIndex < materials.Length;
                 materialIndex++)
            {
                if (!WpfViewport3DMaterialMapper.TryMapSolid(
                        materials[materialIndex],
                        out WpfViewport3DSolidMaterialPass materialPass))
                {
                    return false;
                }
                payload.Meshes.Add(CreateMeshEntry(
                    mesh,
                    positions,
                    normals,
                    textureCoordinates,
                    materialPass));
            }
        }

        replayData = new WpfViewport3DReplayData(
            payload,
            projection,
            view,
            new global::ProGPU.Scene.Rect(
                (float)scene.Viewport.X,
                (float)scene.Viewport.Y,
                viewportWidth,
                viewportHeight));
        return payload.Meshes.Count > 0;
    }

    private static bool TryAddPortableLights(
        PortableViewport3DLight[]? lights,
        global::ProGPU.Scene.Extensions.Viewport3DCompilationPayload payload)
    {
        if (lights is null || lights.Length == 0)
        {
            return true;
        }
        if (lights.Length > 16)
        {
            return false;
        }
        foreach (PortableViewport3DLight? light in lights)
        {
            if (light is null ||
                !TryToFiniteVector4(light.Color, out Vector4 color))
            {
                return false;
            }
            var entry = new global::ProGPU.Scene.Extensions.Light3DCompilationEntry
            {
                Kind = (global::ProGPU.Scene.Extensions.LightKind3D)light.Kind,
                Color = color
            };
            switch (light.Kind)
            {
                case PortableViewport3DLightKind.Ambient:
                    break;
                case PortableViewport3DLightKind.Directional:
                    if (!TryToFiniteVector3(
                            light.Direction, out Vector3 direction) ||
                        direction.LengthSquared() <= 0.000001f)
                    {
                        return false;
                    }
                    entry.Direction = Vector3.Normalize(direction);
                    break;
                case PortableViewport3DLightKind.Point:
                case PortableViewport3DLightKind.Spot:
                    if (!TryPopulatePointLight(light, ref entry))
                    {
                        return false;
                    }
                    if (light.Kind == PortableViewport3DLightKind.Spot)
                    {
                        if (!TryToFiniteVector3(
                                light.Direction, out Vector3 spotDirection) ||
                            spotDirection.LengthSquared() <= 0.000001f ||
                            !TryToFiniteFloat(
                                light.InnerConeAngle, out float innerAngle) ||
                            !TryToFiniteFloat(
                                light.OuterConeAngle, out float outerAngle))
                        {
                            return false;
                        }
                        outerAngle = Math.Clamp(outerAngle, 0f, 180f);
                        innerAngle = Math.Min(
                            Math.Clamp(innerAngle, 0f, 180f),
                            outerAngle);
                        entry.Direction = Vector3.Normalize(spotDirection);
                        entry.InnerConeCosine = MathF.Cos(
                            innerAngle * (MathF.PI / 360f));
                        entry.OuterConeCosine = MathF.Cos(
                            outerAngle * (MathF.PI / 360f));
                    }
                    break;
                default:
                    return false;
            }
            payload.Lights.Add(entry);
        }
        return true;
    }

    private static bool TryPopulatePointLight(
        PortableViewport3DLight source,
        ref global::ProGPU.Scene.Extensions.Light3DCompilationEntry target)
    {
        if (!TryToFiniteVector3(source.Position, out Vector3 position) ||
            (!double.IsPositiveInfinity(source.Range) &&
                !TryToFiniteFloat(source.Range, out _)) ||
            !TryToFiniteFloat(
                source.ConstantAttenuation, out float constant) ||
            !TryToFiniteFloat(
                source.LinearAttenuation, out float linear) ||
            !TryToFiniteFloat(
                source.QuadraticAttenuation, out float quadratic))
        {
            return false;
        }
        float range = double.IsPositiveInfinity(source.Range)
            ? float.MaxValue
            : (float)source.Range;
        if (range <= 0f || constant < 0f || linear < 0f || quadratic < 0f ||
            (constant == 0f && linear == 0f && quadratic == 0f))
        {
            return false;
        }
        target.Position = position;
        target.Range = range;
        target.ConstantAttenuation = constant;
        target.LinearAttenuation = linear;
        target.QuadraticAttenuation = quadratic;
        return true;
    }

    private static bool TryToFiniteVector3(
        PortableVector3 value,
        out Vector3 result)
    {
        result = ToVector3(value);
        return float.IsFinite(result.X) && float.IsFinite(result.Y) &&
            float.IsFinite(result.Z);
    }

    private static bool TryToFiniteVector4(
        PortableColor4 value,
        out Vector4 result)
    {
        result = ToVector4(value);
        return float.IsFinite(result.X) && float.IsFinite(result.Y) &&
            float.IsFinite(result.Z) && float.IsFinite(result.W);
    }

    private static bool TryToFiniteFloat(double value, out float result)
    {
        result = (float)value;
        return float.IsFinite(result);
    }

    private static bool TryCreateCameraMatrices(
        PortableViewport3DCamera camera,
        float aspectRatio,
        out Matrix4x4 projection,
        out Matrix4x4 view)
    {
        projection = Matrix4x4.Identity;
        view = Matrix4x4.Identity;

        if (camera.Kind == PortableViewport3DCameraKind.Matrix)
        {
            projection = ToMatrix4x4(camera.ProjectionMatrix);
            view = ToMatrix4x4(camera.ViewMatrix);
            return IsFinite(projection) && IsFinite(view) &&
                Matrix4x4.Invert(view, out _);
        }

        var position = ToVector3(camera.Position);
        var lookDirection = ToVector3(camera.LookDirection);
        var upDirection = ToVector3(camera.UpDirection);
        if (lookDirection.LengthSquared() <= 0.000001f || upDirection.LengthSquared() <= 0.000001f)
        {
            return false;
        }

        if (camera.HasTransform)
        {
            var transform = ToMatrix4x4(camera.Transform);
            position = Vector3.Transform(position, transform);
            lookDirection = Vector3.TransformNormal(lookDirection, transform);
            upDirection = Vector3.TransformNormal(upDirection, transform);
        }

        view = Matrix4x4.CreateLookAt(position, position + lookDirection, upDirection);

        var nearPlane = camera.NearPlaneDistance > 0
            ? (float)camera.NearPlaneDistance
            : DefaultNearPlaneDistance;
        var farPlane = camera.FarPlaneDistance > nearPlane
            ? (float)camera.FarPlaneDistance
            : nearPlane + 1f;

        if (camera.Kind == PortableViewport3DCameraKind.Orthographic)
        {
            var width = camera.Width > 0
                ? (float)camera.Width
                : DefaultOrthographicWidth;
            var height = width / Math.Max(0.0001f, aspectRatio);
            projection = Matrix4x4.CreateOrthographic(width, height, nearPlane, farPlane);
            return true;
        }

        if (camera.Kind != PortableViewport3DCameraKind.Perspective)
        {
            return false;
        }

        var horizontalFovDegrees = camera.FieldOfView > 0
            ? (float)camera.FieldOfView
            : DefaultPerspectiveFieldOfView;
        horizontalFovDegrees = Math.Clamp(horizontalFovDegrees, 1f, 179f);
        var horizontalFovRadians = horizontalFovDegrees * MathF.PI / 180f;
        var verticalFovRadians = 2f * MathF.Atan(MathF.Tan(horizontalFovRadians / 2f) / Math.Max(0.0001f, aspectRatio));
        projection = Matrix4x4.CreatePerspectiveFieldOfView(verticalFovRadians, aspectRatio, nearPlane, farPlane);
        return true;
    }

    private static Vector3 ToVector3(PortableVector3 value)
    {
        return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
    }

    private static global::ProGPU.Scene.Extensions.MeshCompilationEntry
        CreateMeshEntry(
            PortableViewport3DMesh mesh,
            Vector3[] positions,
            Vector3[] normals,
            Vector2[] textureCoordinates,
            WpfViewport3DSolidMaterialPass? materialPass = null)
    {
        WpfViewport3DSolidMaterialPass material =
            materialPass.GetValueOrDefault();
        bool hasMaterialPass = materialPass.HasValue;
        return new global::ProGPU.Scene.Extensions.MeshCompilationEntry
        {
            Geometry = mesh.Geometry,
            GeometryVersion = mesh.GeometryVersion,
            Positions = positions,
            Normals = normals,
            TextureCoordinates = textureCoordinates,
            Indices = mesh.Indices,
            ModelTransform = ToMatrix4x4(mesh.ModelTransform),
            Color = hasMaterialPass
                ? material.Color
                : ToVector4(mesh.DiffuseColor),
            SpecularColor = hasMaterialPass
                ? material.SpecularColor
                : ToVector3(mesh.SpecularColor),
            Shininess = hasMaterialPass
                ? material.Shininess
                : (float)Math.Clamp(mesh.Shininess, 1, 256),
            AmbientColor = hasMaterialPass
                ? material.AmbientColor
                : ToVector3(mesh.AmbientColor),
            Opacity = hasMaterialPass
                ? material.Opacity
                : (float)Math.Clamp(mesh.Opacity, 0, 1),
            IsBackFace = mesh.IsBackFace,
            ShadingModeOverride =
                hasMaterialPass && material.IsUnlit
                    ? global::ProGPU.Scene.Extensions.ShadingMode3D.Flat
                    : null
        };
    }

    private static Vector3 ToVector3(PortableColor4 value)
    {
        return new Vector3((float)value.R, (float)value.G, (float)value.B);
    }

    private static Vector4 ToVector4(PortableColor4 value)
    {
        return new Vector4((float)value.R, (float)value.G, (float)value.B, (float)value.A);
    }

    private static bool TryToVector3Array(
        PortableVector3[]? values,
        int count,
        bool normalize,
        out Vector3[] result)
    {
        if (values is null || count < 0 || values.Length < count)
        {
            result = Array.Empty<Vector3>();
            return false;
        }
        if (count == 0)
        {
            result = Array.Empty<Vector3>();
            return true;
        }

        result = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            Vector3 value = ToVector3(values[i]);
            if (!float.IsFinite(value.X)
                || !float.IsFinite(value.Y)
                || !float.IsFinite(value.Z))
            {
                result = Array.Empty<Vector3>();
                return false;
            }

            if (normalize)
            {
                float lengthSquared = value.LengthSquared();
                if (!float.IsFinite(lengthSquared))
                {
                    result = Array.Empty<Vector3>();
                    return false;
                }
                value = lengthSquared > 0.0f
                    ? value / MathF.Sqrt(lengthSquared)
                    : Vector3.Zero;
            }
            result[i] = value;
        }

        return true;
    }

    private static bool TryToVector2Array(
        PortablePoint[]? values,
        out Vector2[] result)
    {
        if (values is null)
        {
            result = Array.Empty<Vector2>();
            return false;
        }
        if (values.Length == 0)
        {
            result = Array.Empty<Vector2>();
            return true;
        }

        result = new Vector2[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var value = new Vector2(
                (float)values[i].X,
                (float)values[i].Y);
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                result = Array.Empty<Vector2>();
                return false;
            }
            result[i] = value;
        }
        return true;
    }

    private static Matrix4x4 ToMatrix4x4(PortableMatrix4x4 value)
    {
        return new Matrix4x4(
            (float)value.M11, (float)value.M12, (float)value.M13, (float)value.M14,
            (float)value.M21, (float)value.M22, (float)value.M23, (float)value.M24,
            (float)value.M31, (float)value.M32, (float)value.M33, (float)value.M34,
            (float)value.M41, (float)value.M42, (float)value.M43, (float)value.M44);
    }

    private static bool IsFinite(Matrix4x4 value)
    {
        return float.IsFinite(value.M11) && float.IsFinite(value.M12) &&
            float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
            float.IsFinite(value.M21) && float.IsFinite(value.M22) &&
            float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
            float.IsFinite(value.M31) && float.IsFinite(value.M32) &&
            float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
            float.IsFinite(value.M41) && float.IsFinite(value.M42) &&
            float.IsFinite(value.M43) && float.IsFinite(value.M44);
    }
}

public readonly record struct WpfViewport3DReplayData(
    global::ProGPU.Scene.Extensions.Viewport3DCompilationPayload Payload,
    Matrix4x4 Projection,
    Matrix4x4 View,
    global::ProGPU.Scene.Rect Viewport);

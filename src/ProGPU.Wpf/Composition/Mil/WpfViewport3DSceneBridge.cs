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
            || scene.Viewport.Height <= 0
            || HasUnsupportedPortableLights(scene.Lights))
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
            if (mesh == null
                || mesh.Positions.Length == 0
                || mesh.Indices.Length == 0)
            {
                continue;
            }

            payload.Meshes.Add(new global::ProGPU.Scene.Extensions.MeshCompilationEntry
            {
                Geometry = mesh.Geometry,
                GeometryVersion = mesh.GeometryVersion,
                Positions = ToVector3Array(mesh.Positions),
                Normals = ToVector3Array(mesh.Normals),
                Indices = mesh.Indices,
                ModelTransform = ToMatrix4x4(mesh.ModelTransform),
                Color = ToVector4(mesh.DiffuseColor),
                SpecularColor = ToVector3(mesh.SpecularColor),
                Shininess = (float)Math.Clamp(mesh.Shininess, 1, 256),
                AmbientColor = ToVector3(mesh.AmbientColor),
                Opacity = (float)Math.Clamp(mesh.Opacity, 0, 1),
                IsBackFace = mesh.IsBackFace
            });
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

    private static bool HasUnsupportedPortableLights(
        PortableViewport3DLight[]? lights)
    {
        if (lights is null || lights.Length == 0)
        {
            return false;
        }

        var ambientCount = 0;
        var directionalCount = 0;
        foreach (PortableViewport3DLight? light in lights)
        {
            if (light is null)
            {
                return true;
            }

            switch (light.Kind)
            {
                case PortableViewport3DLightKind.Ambient:
                    ambientCount++;
                    break;
                case PortableViewport3DLightKind.Directional:
                    directionalCount++;
                    break;
                default:
                    return true;
            }
        }

        return ambientCount > 1 || directionalCount > 1;
    }

    private static bool TryCreateCameraMatrices(
        PortableViewport3DCamera camera,
        float aspectRatio,
        out Matrix4x4 projection,
        out Matrix4x4 view)
    {
        projection = Matrix4x4.Identity;
        view = Matrix4x4.Identity;

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

    private static Vector3 ToVector3(PortableColor4 value)
    {
        return new Vector3((float)value.R, (float)value.G, (float)value.B);
    }

    private static Vector4 ToVector4(PortableColor4 value)
    {
        return new Vector4((float)value.R, (float)value.G, (float)value.B, (float)value.A);
    }

    private static Vector3[] ToVector3Array(PortableVector3[] values)
    {
        if (values.Length == 0)
        {
            return Array.Empty<Vector3>();
        }

        var result = new Vector3[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            result[i] = ToVector3(values[i]);
        }

        return result;
    }

    private static Matrix4x4 ToMatrix4x4(PortableMatrix4x4 value)
    {
        return new Matrix4x4(
            (float)value.M11, (float)value.M12, (float)value.M13, (float)value.M14,
            (float)value.M21, (float)value.M22, (float)value.M23, (float)value.M24,
            (float)value.M31, (float)value.M32, (float)value.M33, (float)value.M34,
            (float)value.M41, (float)value.M42, (float)value.M43, (float)value.M44);
    }
}

public readonly record struct WpfViewport3DReplayData(
    global::ProGPU.Scene.Extensions.Viewport3DCompilationPayload Payload,
    Matrix4x4 Projection,
    Matrix4x4 View,
    global::ProGPU.Scene.Rect Viewport);

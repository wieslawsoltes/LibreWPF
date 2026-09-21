using System;
using System.Numerics;
using ProGpuContainerVisual = global::ProGPU.Scene.ContainerVisual;
using ProGpuDrawingContext = global::ProGPU.Scene.DrawingContext;
using ProGpuDrawingVisual = global::ProGPU.Scene.DrawingVisual;
using ProGpuPicture = global::ProGPU.Scene.GpuPicture;
using ProGpuRect = global::ProGPU.Scene.Rect;
using ProGpuRenderCommand = global::ProGPU.Scene.RenderCommand;
using ProGpuRenderCommandType = global::ProGPU.Scene.RenderCommandType;
using ProGpuRenderDataProvider = global::ProGPU.Scene.IRenderDataProvider;
using ProGpuVisual = global::ProGPU.Scene.Visual;
using ProGpuPathGeometry = global::ProGPU.Vector.PathGeometry;
using ProGpuPen = global::ProGPU.Vector.Pen;

namespace System.Windows.Media.ProGPU.Composition;

internal static class ProGpuSceneBoundsReader
{
    public static bool TryGetContentBounds(ProGpuVisual visual, out ProGpuRect bounds)
    {
        ArgumentNullException.ThrowIfNull(visual);

        var accumulator = new BoundsAccumulator();
        ReadVisual(visual, Matrix4x4.Identity, includeLocalTransform: false, ref accumulator);
        return accumulator.TryGetBounds(out bounds);
    }

    private static void ReadVisual(
        ProGpuVisual visual,
        Matrix4x4 parentTransform,
        bool includeLocalTransform,
        ref BoundsAccumulator accumulator)
    {
        if (!visual.IsVisible || visual.Opacity <= 0.0001f)
        {
            return;
        }

        Matrix4x4 transform = includeLocalTransform
            ? visual.GetLocalTransform() * parentTransform
            : parentTransform;

        if (visual is ProGpuRetainedDrawingVisual retainedDrawingVisual)
        {
            ReadCommands(retainedDrawingVisual.Context, transform, ref accumulator);
        }
        else if (visual is ProGpuDrawingVisual drawingVisual)
        {
            ReadCommands(drawingVisual.Context, transform, ref accumulator);
        }

        if (visual is not ProGpuContainerVisual container)
        {
            return;
        }

        var children = container.Children;
        for (int i = 0; i < children.Count; i++)
        {
            ReadVisual(children[i], transform, includeLocalTransform: true, ref accumulator);
        }
    }

    private static void ReadCommands(
        ProGpuDrawingContext context,
        Matrix4x4 visualTransform,
        ref BoundsAccumulator accumulator)
    {
        var commands = context.Commands;
        for (int i = 0; i < commands.Count; i++)
        {
            ReadCommand(commands[i], context, visualTransform, ref accumulator);
        }
    }

    private static void ReadCommands(
        ProGpuPicture picture,
        Matrix4x4 visualTransform,
        ref BoundsAccumulator accumulator)
    {
        var commands = picture.Commands;
        for (int i = 0; i < commands.Length; i++)
        {
            ReadCommand(commands[i], picture, visualTransform, ref accumulator);
        }
    }

    private static void ReadCommand(
        ProGpuRenderCommand command,
        ProGpuRenderDataProvider provider,
        Matrix4x4 visualTransform,
        ref BoundsAccumulator accumulator)
    {
        Matrix4x4 activeTransform = ResolveCommandTransform(command, visualTransform);
        switch (command.Type)
        {
            case ProGpuRenderCommandType.DrawRect:
            case ProGpuRenderCommandType.DrawRoundedRect:
            case ProGpuRenderCommandType.DrawTexture:
            case ProGpuRenderCommandType.PushOpacityMask:
                AddRect(command.Rect, activeTransform, command.Pen, ref accumulator);
                break;

            case ProGpuRenderCommandType.DrawLine:
                AddPoints(command.Position, command.Position2, activeTransform, command.Pen, ref accumulator);
                break;

            case ProGpuRenderCommandType.DrawEllipse:
                AddRect(
                    new ProGpuRect(
                        command.Position2.X - command.RadiusX,
                        command.Position2.Y - command.RadiusY,
                        command.RadiusX * 2f,
                        command.RadiusY * 2f),
                    activeTransform,
                    command.Pen,
                    ref accumulator);
                break;

            case ProGpuRenderCommandType.DrawCircle:
                AddRect(
                    new ProGpuRect(
                        command.Position2.X - command.RadiusX,
                        command.Position2.Y - command.RadiusX,
                        command.RadiusX * 2f,
                        command.RadiusX * 2f),
                    activeTransform,
                    command.Pen,
                    ref accumulator);
                break;

            case ProGpuRenderCommandType.DrawBezier:
                AddPoints(
                    command.Position,
                    command.Position2,
                    command.Position3,
                    activeTransform,
                    command.Pen,
                    ref accumulator);
                break;

            case ProGpuRenderCommandType.DrawCubicBezier:
            case ProGpuRenderCommandType.FillQuad:
                AddPoints(
                    command.Position,
                    command.Position2,
                    command.Position3,
                    command.Position4,
                    activeTransform,
                    command.Pen,
                    ref accumulator);
                break;

            case ProGpuRenderCommandType.FillTriangle:
                AddPoints(
                    command.Position,
                    command.Position2,
                    command.Position3,
                    activeTransform,
                    command.Pen,
                    ref accumulator);
                break;

            case ProGpuRenderCommandType.DrawPath:
            case ProGpuRenderCommandType.PushGeometryClip:
                AddPath(command.Path, activeTransform, command.Pen, ref accumulator);
                break;

            case ProGpuRenderCommandType.DrawPolyline:
                AddPointBuffer(
                    provider,
                    command.PointBufferOffset,
                    command.PointBufferCount,
                    activeTransform,
                    command.Pen,
                    ref accumulator);
                break;

            case ProGpuRenderCommandType.DrawText:
                AddText(command, activeTransform, ref accumulator);
                break;

            case ProGpuRenderCommandType.DrawGlyphRun:
                AddGlyphRun(command, activeTransform, ref accumulator);
                break;

            case ProGpuRenderCommandType.DrawPicture:
                if (command.Picture != null)
                {
                    ReadCommands(command.Picture, activeTransform, ref accumulator);
                }

                break;
        }
    }

    private static Matrix4x4 ResolveCommandTransform(ProGpuRenderCommand command, Matrix4x4 visualTransform)
    {
        if (command.UseGpuTransforms)
        {
            return Matrix4x4.Identity;
        }

        Matrix4x4 commandTransform = command.Transform == default
            ? Matrix4x4.Identity
            : command.Transform;
        return commandTransform * visualTransform;
    }

    private static void AddRect(ProGpuRect rect, Matrix4x4 transform, ProGpuPen? pen, ref BoundsAccumulator accumulator)
    {
        if (!IsUsableRect(rect))
        {
            return;
        }

        Vector2 min = new(rect.X, rect.Y);
        Vector2 max = new(rect.Right, rect.Bottom);
        AddTransformedBounds(min, max, transform, GetStrokePadding(pen), ref accumulator);
    }

    private static void AddPath(ProGpuPathGeometry? path, Matrix4x4 transform, ProGpuPen? pen, ref BoundsAccumulator accumulator)
    {
        if (path == null || !path.TryGetBounds(out var min, out var max))
        {
            return;
        }

        AddTransformedBounds(min, max, transform, GetStrokePadding(pen), ref accumulator);
    }

    private static void AddPointBuffer(
        ProGpuRenderDataProvider provider,
        int offset,
        int count,
        Matrix4x4 transform,
        ProGpuPen? pen,
        ref BoundsAccumulator accumulator)
    {
        if (count <= 0)
        {
            return;
        }

        var points = provider.GetPoints(offset, count);
        if (points.IsEmpty)
        {
            return;
        }

        var min = points[0];
        var max = points[0];
        for (int i = 1; i < points.Length; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }

        AddTransformedBounds(min, max, transform, GetStrokePadding(pen), ref accumulator);
    }

    private static void AddText(ProGpuRenderCommand command, Matrix4x4 transform, ref BoundsAccumulator accumulator)
    {
        if (string.IsNullOrEmpty(command.Text) || command.FontSize <= 0f)
        {
            return;
        }

        float width = MathF.Max(command.FontSize, command.Text.Length * command.FontSize * 0.6f);
        float height = command.FontSize;
        AddTransformedBounds(
            command.Position,
            command.Position + new Vector2(width, height),
            transform,
            padding: 1f,
            ref accumulator);
    }

    private static void AddGlyphRun(ProGpuRenderCommand command, Matrix4x4 transform, ref BoundsAccumulator accumulator)
    {
        if (IsUsableRect(command.Rect))
        {
            AddRect(command.Rect, transform, command.Pen, ref accumulator);
            return;
        }

        if (command.GlyphPositions is not { Length: > 0 } positions)
        {
            AddText(command, transform, ref accumulator);
            return;
        }

        var min = positions[0];
        var max = positions[0];
        for (int i = 1; i < positions.Length; i++)
        {
            min = Vector2.Min(min, positions[i]);
            max = Vector2.Max(max, positions[i]);
        }

        float padding = MathF.Max(1f, command.FontSize);
        AddTransformedBounds(min, max + new Vector2(padding), transform, padding: 0f, ref accumulator);
    }

    private static void AddPoints(
        Vector2 point0,
        Vector2 point1,
        Matrix4x4 transform,
        ProGpuPen? pen,
        ref BoundsAccumulator accumulator)
    {
        AddTransformedBounds(
            Vector2.Min(point0, point1),
            Vector2.Max(point0, point1),
            transform,
            GetStrokePadding(pen),
            ref accumulator);
    }

    private static void AddPoints(
        Vector2 point0,
        Vector2 point1,
        Vector2 point2,
        Matrix4x4 transform,
        ProGpuPen? pen,
        ref BoundsAccumulator accumulator)
    {
        var min = Vector2.Min(Vector2.Min(point0, point1), point2);
        var max = Vector2.Max(Vector2.Max(point0, point1), point2);
        AddTransformedBounds(min, max, transform, GetStrokePadding(pen), ref accumulator);
    }

    private static void AddPoints(
        Vector2 point0,
        Vector2 point1,
        Vector2 point2,
        Vector2 point3,
        Matrix4x4 transform,
        ProGpuPen? pen,
        ref BoundsAccumulator accumulator)
    {
        var min = Vector2.Min(Vector2.Min(point0, point1), Vector2.Min(point2, point3));
        var max = Vector2.Max(Vector2.Max(point0, point1), Vector2.Max(point2, point3));
        AddTransformedBounds(min, max, transform, GetStrokePadding(pen), ref accumulator);
    }

    private static void AddTransformedBounds(
        Vector2 min,
        Vector2 max,
        Matrix4x4 transform,
        float padding,
        ref BoundsAccumulator accumulator)
    {
        if (!IsFinite(min) || !IsFinite(max) || max.X < min.X || max.Y < min.Y)
        {
            return;
        }

        if (padding > 0f)
        {
            min -= new Vector2(padding);
            max += new Vector2(padding);
        }

        var p0 = TransformPoint(new Vector2(min.X, min.Y), transform);
        var p1 = TransformPoint(new Vector2(max.X, min.Y), transform);
        var p2 = TransformPoint(new Vector2(min.X, max.Y), transform);
        var p3 = TransformPoint(new Vector2(max.X, max.Y), transform);
        accumulator.Add(p0);
        accumulator.Add(p1);
        accumulator.Add(p2);
        accumulator.Add(p3);
    }

    private static Vector2 TransformPoint(Vector2 point, Matrix4x4 transform)
    {
        var transformed = Vector3.Transform(new Vector3(point.X, point.Y, 0f), transform);
        return new Vector2(transformed.X, transformed.Y);
    }

    private static float GetStrokePadding(ProGpuPen? pen)
    {
        return pen == null ? 0f : MathF.Max(0f, pen.Thickness / 2f);
    }

    private static bool IsUsableRect(ProGpuRect rect)
    {
        return !rect.IsEmpty &&
            float.IsFinite(rect.X) &&
            float.IsFinite(rect.Y) &&
            float.IsFinite(rect.Width) &&
            float.IsFinite(rect.Height);
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }

    private struct BoundsAccumulator
    {
        private Vector2 _min;
        private Vector2 _max;
        private bool _hasBounds;

        public void Add(Vector2 point)
        {
            if (!IsFinite(point))
            {
                return;
            }

            if (_hasBounds)
            {
                _min = Vector2.Min(_min, point);
                _max = Vector2.Max(_max, point);
            }
            else
            {
                _min = point;
                _max = point;
                _hasBounds = true;
            }
        }

        public readonly bool TryGetBounds(out ProGpuRect bounds)
        {
            if (!_hasBounds || _max.X < _min.X || _max.Y < _min.Y)
            {
                bounds = ProGpuRect.Empty;
                return false;
            }

            bounds = new ProGpuRect(_min, _max - _min);
            return true;
        }
    }
}

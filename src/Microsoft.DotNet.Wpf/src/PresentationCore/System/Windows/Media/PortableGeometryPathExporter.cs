// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media
{
    internal static class PortableGeometryPathExporter
    {
        internal static PortableGeometryPath FromGeometry(Geometry geometry)
        {
            PathGeometry pathGeometry = geometry.GetAsPathGeometry();
            return FromPathGeometry(pathGeometry, geometry.Bounds);
        }

        internal static PortableGeometryPath FromPathGeometry(PathGeometry pathGeometry, Rect bounds)
        {
            PathFigureCollection figures = pathGeometry.Figures;
            var portableFigures = new PortablePathFigure[figures?.Count ?? 0];

            for (int i = 0; i < portableFigures.Length; i++)
            {
                PathFigure figure = figures[i];
                portableFigures[i] = new PortablePathFigure
                {
                    StartPoint = ToPortablePoint(figure.StartPoint),
                    IsClosed = figure.IsClosed,
                    IsFilled = figure.IsFilled,
                    Segments = ToPortableSegments(figure.Segments)
                };
            }

            return new PortableGeometryPath
            {
                Kind = PortableGeometryPathKind.Path,
                FillRule = pathGeometry.FillRule == FillRule.EvenOdd
                    ? PortableFillRule.EvenOdd
                    : PortableFillRule.Nonzero,
                Transform = ToPortableMatrix(pathGeometry.Transform),
                Bounds = ToPortableRect(bounds),
                Figures = portableFigures
            };
        }

        internal static PortableMatrix3x2 ToPortableMatrix(Transform transform)
        {
            if (transform == null || transform.IsIdentity)
            {
                return PortableMatrix3x2.Identity;
            }

            return ToPortableMatrix(transform.Value);
        }

        internal static PortableRect ToPortableRect(Rect rect)
        {
            return rect.IsEmpty
                ? PortableRect.Empty
                : new PortableRect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static PortablePathSegment[] ToPortableSegments(PathSegmentCollection segments)
        {
            if (segments == null || segments.Count == 0)
            {
                return Array.Empty<PortablePathSegment>();
            }

            var portableSegments = new List<PortablePathSegment>(segments.Count);
            foreach (PathSegment segment in segments)
            {
                AddPortableSegment(portableSegments, segment);
            }

            return portableSegments.ToArray();
        }

        private static void AddPortableSegment(List<PortablePathSegment> segments, PathSegment segment)
        {
            switch (segment)
            {
                case LineSegment line:
                    segments.Add(PortablePathSegment.Line(
                        ToPortablePoint(line.Point),
                        line.IsSmoothJoin,
                        line.IsStroked));
                    break;
                case PolyLineSegment polyLine:
                    foreach (Point point in polyLine.Points)
                    {
                        segments.Add(PortablePathSegment.Line(
                            ToPortablePoint(point),
                            polyLine.IsSmoothJoin,
                            polyLine.IsStroked));
                    }

                    break;
                case QuadraticBezierSegment quadratic:
                    segments.Add(PortablePathSegment.QuadraticBezier(
                        ToPortablePoint(quadratic.Point1),
                        ToPortablePoint(quadratic.Point2),
                        quadratic.IsSmoothJoin,
                        quadratic.IsStroked));
                    break;
                case PolyQuadraticBezierSegment polyQuadratic:
                    AddPortableQuadraticBezierSegments(
                        segments,
                        polyQuadratic.Points,
                        polyQuadratic.IsSmoothJoin,
                        polyQuadratic.IsStroked);
                    break;
                case BezierSegment bezier:
                    segments.Add(PortablePathSegment.CubicBezier(
                        ToPortablePoint(bezier.Point1),
                        ToPortablePoint(bezier.Point2),
                        ToPortablePoint(bezier.Point3),
                        bezier.IsSmoothJoin,
                        bezier.IsStroked));
                    break;
                case PolyBezierSegment polyBezier:
                    AddPortableCubicBezierSegments(
                        segments,
                        polyBezier.Points,
                        polyBezier.IsSmoothJoin,
                        polyBezier.IsStroked);
                    break;
                case ArcSegment arc:
                    segments.Add(PortablePathSegment.Arc(
                        ToPortablePoint(arc.Point),
                        ToPortableSize(arc.Size),
                        arc.RotationAngle,
                        arc.IsLargeArc,
                        arc.SweepDirection == SweepDirection.Clockwise
                            ? PortableSweepDirection.Clockwise
                            : PortableSweepDirection.Counterclockwise,
                        arc.IsSmoothJoin,
                        arc.IsStroked));
                    break;
            }
        }

        private static void AddPortableQuadraticBezierSegments(
            List<PortablePathSegment> segments,
            PointCollection points,
            bool isSmoothJoin,
            bool isStroked)
        {
            for (int i = 0; i + 1 < points.Count; i += 2)
            {
                segments.Add(PortablePathSegment.QuadraticBezier(
                    ToPortablePoint(points[i]),
                    ToPortablePoint(points[i + 1]),
                    isSmoothJoin,
                    isStroked));
            }
        }

        private static void AddPortableCubicBezierSegments(
            List<PortablePathSegment> segments,
            PointCollection points,
            bool isSmoothJoin,
            bool isStroked)
        {
            for (int i = 0; i + 2 < points.Count; i += 3)
            {
                segments.Add(PortablePathSegment.CubicBezier(
                    ToPortablePoint(points[i]),
                    ToPortablePoint(points[i + 1]),
                    ToPortablePoint(points[i + 2]),
                    isSmoothJoin,
                    isStroked));
            }
        }

        private static PortablePoint ToPortablePoint(Point point)
        {
            return new PortablePoint(point.X, point.Y);
        }

        private static PortableSize ToPortableSize(Size size)
        {
            return new PortableSize(Math.Abs(size.Width), Math.Abs(size.Height));
        }

        private static PortableMatrix3x2 ToPortableMatrix(Matrix matrix)
        {
            return new PortableMatrix3x2(
                matrix.M11,
                matrix.M12,
                matrix.M21,
                matrix.M22,
                matrix.OffsetX,
                matrix.OffsetY);
        }
    }
}

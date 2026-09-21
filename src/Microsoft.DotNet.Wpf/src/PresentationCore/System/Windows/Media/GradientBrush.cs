// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//
//
// Description: This file contains the implementation of GradientBrush.
//              The GradientBrush is an abstract class of Brushes which describes
//              a way to fill a region by a gradient.  Derived classes describe different
//              ways of interpreting gradient stops.
//
//

using System.Windows.Markup;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media
{
    /// <summary>
    /// GradientBrush
    /// The GradientBrush is an abstract class of Brushes which describes
    /// a way to fill a region by a gradient.  Derived classes describe different
    /// ways of interpreting gradient stops.
    /// </summary>
    [ContentProperty("GradientStops")]
    public abstract partial class GradientBrush : Brush
    {
        #region Constructors
        
        /// <summary>
        /// Protected constructor for GradientBrush
        /// </summary>
        protected GradientBrush()
        {
        }

        /// <summary>
        /// Protected constructor for GradientBrush
        /// Sets all the values of the GradientStopCollection, all other values are left as default.
        /// </summary>
        protected GradientBrush(GradientStopCollection gradientStopCollection) 
        {
            GradientStops = gradientStopCollection;
        }

        #endregion Constructors

        private protected PortableGradientStop[] GetPortableGradientStops()
        {
            GradientStopCollection gradientStops = GradientStops;
            if (gradientStops == null || gradientStops.Count == 0)
            {
                return System.Array.Empty<PortableGradientStop>();
            }

            PortableGradientStop[] stops = new PortableGradientStop[gradientStops.Count];
            for (int i = 0; i < stops.Length; i++)
            {
                GradientStop stop = gradientStops[i];
                Color color = stop.Color;
                stops[i] = new PortableGradientStop(
                    new PortableColor(color.A, color.R, color.G, color.B),
                    stop.Offset);
            }

            return stops;
        }

        private protected PortableBrushMappingMode GetPortableBrushMappingMode()
        {
            return MappingMode == BrushMappingMode.Absolute
                ? PortableBrushMappingMode.Absolute
                : PortableBrushMappingMode.RelativeToBoundingBox;
        }

        private protected PortableGradientSpreadMethod GetPortableGradientSpreadMethod()
        {
            return SpreadMethod switch
            {
                GradientSpreadMethod.Reflect => PortableGradientSpreadMethod.Reflect,
                GradientSpreadMethod.Repeat => PortableGradientSpreadMethod.Repeat,
                _ => PortableGradientSpreadMethod.Pad
            };
        }

        private protected PortableGradientColorInterpolationMode GetPortableGradientColorInterpolationMode()
        {
            return ColorInterpolationMode == ColorInterpolationMode.ScRgbLinearInterpolation
                ? PortableGradientColorInterpolationMode.ScRgbLinearInterpolation
                : PortableGradientColorInterpolationMode.SRgbLinearInterpolation;
        }

        private protected bool TryGetPortableBrushTransform(Transform transform, out PortableMatrix3x2 matrix)
        {
            matrix = PortableMatrix3x2.Identity;
            if (transform == null || ReferenceEquals(transform, Transform.Identity))
            {
                return false;
            }

            if (transform is IPortableTransformMatrixSource matrixSource
                && matrixSource.TryGetPortableTransformMatrix(out matrix)
                && !matrix.IsIdentity)
            {
                return true;
            }

            matrix = PortableMatrix3x2.Identity;
            return false;
        }
    }
}

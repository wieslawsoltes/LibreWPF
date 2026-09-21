// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
//
//
// Description: This file contains the implementation of TileBrush.
//              The TileBrush is an abstract class of Brushes which describes
//              a way to fill a region by tiling.  The contents of the tiles
//              are described by classes derived from TileBrush.
//
//

using ProGPU.Wpf.Interop;
using System.Windows.Media.Composition;

namespace System.Windows.Media
{
    /// <summary>
    /// TileBrush
    /// The TileBrush is an abstract class of Brushes which describes
    /// a way to fill a region by tiling.  The contents of the tiles
    /// are described by classes derived from TileBrush.
    /// </summary>
    public abstract partial class TileBrush : Brush, IPortableTileBrushSource
    {
        #region Constructors
    
        /// <summary>
        /// Protected constructor for TileBrush.  
        /// Sets all values to their defaults.  
        /// To set property values, use the constructor which accepts paramters
        /// </summary>
        protected TileBrush()
        {
        }
   
        #endregion Constructors

        bool IPortableTileBrushSource.TryGetPortableTileBrush(out PortableTileBrush brush)
        {
            ReadPreamble();

            if (!TryGetPortableTileBrushContent(this, out PortableTileBrushKind kind, out object content)
                || content == null
                || !TryGetPortableTileBrushTransform(Transform, out bool hasTransform, out PortableMatrix3x2 transform)
                || !TryGetPortableTileBrushTransform(RelativeTransform, out bool hasRelativeTransform, out PortableMatrix3x2 relativeTransform))
            {
                brush = null;
                return false;
            }

            Rect viewport = Viewport;
            Rect viewbox = Viewbox;
            brush = new PortableTileBrush(
                kind,
                content,
                Opacity,
                ToPortableRect(viewport),
                ToPortableRect(viewbox),
                ToPortableBrushMappingMode(ViewportUnits),
                ToPortableBrushMappingMode(ViewboxUnits),
                ToPortableTileMode(TileMode),
                ToPortableStretch(Stretch),
                ToPortableAlignmentX(AlignmentX),
                ToPortableAlignmentY(AlignmentY),
                hasTransform,
                transform,
                hasRelativeTransform,
                relativeTransform);
            return true;
        }

        private static bool TryGetPortableTileBrushContent(
            TileBrush brush,
            out PortableTileBrushKind kind,
            out object content)
        {
            switch (brush)
            {
                case ImageBrush imageBrush:
                    kind = PortableTileBrushKind.Image;
                    content = imageBrush.ImageSource;
                    return content != null;

                case DrawingBrush drawingBrush:
                    kind = PortableTileBrushKind.Drawing;
                    content = drawingBrush.Drawing;
                    return content != null;

                case VisualBrush visualBrush:
                    kind = PortableTileBrushKind.Visual;
                    content = visualBrush.Visual;
                    return content != null;

                default:
                    kind = default;
                    content = null;
                    return false;
            }
        }

        private static bool TryGetPortableTileBrushTransform(
            Transform transform,
            out bool hasTransform,
            out PortableMatrix3x2 matrix)
        {
            hasTransform = false;
            matrix = PortableMatrix3x2.Identity;

            if (transform == null || ReferenceEquals(transform, Transform.Identity))
            {
                return true;
            }

            if (transform is not IPortableTransformMatrixSource transformSource
                || !transformSource.TryGetPortableTransformMatrix(out matrix))
            {
                return false;
            }

            hasTransform = !matrix.IsIdentity;
            if (!hasTransform)
            {
                matrix = PortableMatrix3x2.Identity;
            }

            return true;
        }

        private static PortableRect ToPortableRect(Rect rect)
        {
            return rect.IsEmpty
                ? PortableRect.Empty
                : new PortableRect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static PortableBrushMappingMode ToPortableBrushMappingMode(BrushMappingMode mappingMode)
        {
            return mappingMode == BrushMappingMode.Absolute
                ? PortableBrushMappingMode.Absolute
                : PortableBrushMappingMode.RelativeToBoundingBox;
        }

        private static PortableTileMode ToPortableTileMode(TileMode tileMode)
        {
            switch (tileMode)
            {
                case TileMode.Tile:
                    return PortableTileMode.Tile;
                case TileMode.FlipX:
                    return PortableTileMode.FlipX;
                case TileMode.FlipY:
                    return PortableTileMode.FlipY;
                case TileMode.FlipXY:
                    return PortableTileMode.FlipXY;
                default:
                    return PortableTileMode.None;
            }
        }

        private static PortableStretch ToPortableStretch(Stretch stretch)
        {
            switch (stretch)
            {
                case Stretch.None:
                    return PortableStretch.None;
                case Stretch.Uniform:
                    return PortableStretch.Uniform;
                case Stretch.UniformToFill:
                    return PortableStretch.UniformToFill;
                default:
                    return PortableStretch.Fill;
            }
        }

        private static PortableAlignmentX ToPortableAlignmentX(AlignmentX alignment)
        {
            switch (alignment)
            {
                case AlignmentX.Left:
                    return PortableAlignmentX.Left;
                case AlignmentX.Right:
                    return PortableAlignmentX.Right;
                default:
                    return PortableAlignmentX.Center;
            }
        }

        private static PortableAlignmentY ToPortableAlignmentY(AlignmentY alignment)
        {
            switch (alignment)
            {
                case AlignmentY.Top:
                    return PortableAlignmentY.Top;
                case AlignmentY.Bottom:
                    return PortableAlignmentY.Bottom;
                default:
                    return PortableAlignmentY.Center;
            }
        }

        /// <summary>
        /// Obtains the current bounds of the brush's content
        /// </summary>
        /// <param name="contentBounds"> Output bounds of content </param>            
        protected abstract void GetContentBounds(out Rect contentBounds);

        /// <summary>
        /// Obtains a matrix that maps the TileBrush's content to the coordinate
        /// space of the shape it is filling.
        /// </summary>
        /// <param name="shapeFillBounds">
        ///     Fill-bounds of the shape this brush is stroking/filling
        /// </param>
        /// <param name="tileBrushMapping">
        ///     Output matrix that maps the TileBrush's content to the coordinate
        ///     space of the shape it is filling
        /// </param>
        internal void GetTileBrushMapping(
            Rect shapeFillBounds,   
            out Matrix tileBrushMapping
            )
        {
            Rect contentBounds = Rect.Empty;            
            BrushMappingMode viewboxUnits = ViewboxUnits;
            bool brushIsEmpty = false;            
 
            // Initialize out-param 
            tileBrushMapping = Matrix.Identity;
 
            // Obtain content bounds for RelativeToBoundingBox ViewboxUnits
            //
            // If ViewboxUnits is RelativeToBoundingBox, then the tile-brush
            // transform is also dependent on the bounds of the content.
            if (viewboxUnits == BrushMappingMode.RelativeToBoundingBox)
            {
                GetContentBounds(out contentBounds);

                // If contentBounds is Rect.Empty then this brush renders nothing.
                // Set the empty flag & early-out.
                if (contentBounds == Rect.Empty)
                {
                    brushIsEmpty = true;
                }
            }

            //
            // Pass the properties to MilUtility_GetTileBrushMapping to calculate
            // the mapping, unless the brush is already determined to be empty
            //
            
            if (!brushIsEmpty)
            {
                //
                // Obtain properties that must be set into local variables
                // 
                
                Rect viewport = Viewport;
                Rect viewbox = Viewbox;
                Matrix transformValue;
                Matrix relativeTransformValue; 

                Transform.GetTransformValue(
                    Transform, 
                    out transformValue 
                    );
                
                Transform.GetTransformValue(
                    RelativeTransform,
                    out relativeTransformValue
                    );                
                
                unsafe
                {
                    D3DMATRIX d3dTransform;
                    D3DMATRIX d3dRelativeTransform;

                    D3DMATRIX d3dContentToShape;   
                    int brushIsEmptyBOOL;                

                    // Call MilUtility_GetTileBrushMapping, converting Matrix's to
                    // D3DMATRIX's when needed.
                    
                    MILUtilities.ConvertToD3DMATRIX(&transformValue, &d3dTransform);
                    MILUtilities.ConvertToD3DMATRIX(&relativeTransformValue, &d3dRelativeTransform);                
                    
                    MS.Win32.PresentationCore.UnsafeNativeMethods.MilCoreApi.MilUtility_GetTileBrushMapping(
                        &d3dTransform,
                        &d3dRelativeTransform,
                        Stretch,
                        AlignmentX,
                        AlignmentY,
                        ViewportUnits,
                        viewboxUnits,
                        &shapeFillBounds,
                        &contentBounds,
                        ref viewport,
                        ref viewbox,
                        out d3dContentToShape,
                        out brushIsEmptyBOOL
                        );

                    // Convert the brushIsEmpty flag from BOOL to a bool.                      
                    brushIsEmpty = (brushIsEmptyBOOL != 0);                     

                    // Set output matrix if the brush isn't empty.  Otherwise, the
                    // output of MilUtility_GetTileBrushMapping must be ignored.
                    if (!brushIsEmpty)
                    {
                        Matrix contentToShape;                        
                        MILUtilities.ConvertFromD3DMATRIX(&d3dContentToShape, &contentToShape);

                        // Set the out-param to the computed tile brush mapping
                        tileBrushMapping = contentToShape;                         
                    }   
                }
            }
        }    
    }
}

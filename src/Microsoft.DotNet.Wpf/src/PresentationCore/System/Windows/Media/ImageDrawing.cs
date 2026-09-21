// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//
// 
// Description: ImageDrawing represents a drawing operation that renders 
//              an image into a destination rectangle.
//

using ProGPU.Wpf.Interop;

namespace System.Windows.Media
{
    /// <summary>
    /// ImageDrawing represents a drawing operation that renders an image into 
    /// a destination rectangle
    /// </summary>
    public sealed partial class ImageDrawing : Drawing, IPortableImageDrawingStateSource
    {
        #region Constructors

        /// <summary>
        /// Default ImageDrawing constructor.  
        /// Constructs an object with all properties set to their default values
        /// </summary>        
        public ImageDrawing()
        {
        }

        /// <summary>
        /// Two-argument ImageDrawing constructor.
        /// Constructs an object with the ImageSource and Rect properties
        /// set to the value of their respective arguments.
        /// </summary>        
        public ImageDrawing(ImageSource imageSource, Rect rect)
        {
            ImageSource = imageSource;
            Rect = rect;
        }

        bool IPortableImageDrawingStateSource.TryGetPortableImageDrawingState(out PortableImageDrawingState state)
        {
            Rect rect = Rect;
            ImageSource imageSource = ImageSource;

            state = new PortableImageDrawingState
            {
                HasImageSource = imageSource != null,
                ImageSource = imageSource,
                HasRect = IsPortableUsableRect(rect),
                Rect = IsPortableUsableRect(rect)
                    ? new PortableRect(rect.X, rect.Y, rect.Width, rect.Height)
                    : PortableRect.Empty
            };
            return true;
        }

        private static bool IsPortableUsableRect(Rect rect)
        {
            return !rect.IsEmpty
                && double.IsFinite(rect.X)
                && double.IsFinite(rect.Y)
                && double.IsFinite(rect.Width)
                && double.IsFinite(rect.Height)
                && rect.Width > 0
                && rect.Height > 0;
        }

        #endregion        

        #region Internal methods

        /// <summary>
        /// Calls methods on the DrawingContext that are equivalent to the
        /// Drawing with the Drawing's current value.
        /// </summary>        
        internal override void WalkCurrentValue(DrawingContextWalker ctx)
        {     
            // We avoid unneccessary ShouldStopWalking checks based on assumptions
            // about when ShouldStopWalking is set.  Guard that assumption with an
            // assertion.  See DrawingGroup.WalkCurrentValue comment for more details.
           
            ctx.DrawImage(
                ImageSource,
                Rect
                );                      
        }

        #endregion Internal methods 
    }
}

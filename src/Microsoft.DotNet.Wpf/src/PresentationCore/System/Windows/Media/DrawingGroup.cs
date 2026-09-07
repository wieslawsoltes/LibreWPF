// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Markup;
using System.Windows.Media.Animation;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media
{
    /// <summary>
    /// DrawingGroup represents a collection of Drawing objects, and
    /// can apply group-operations such as clip and opacity to it's
    /// collections.
    /// </summary>
    [ContentProperty("Children")]
    public sealed partial class DrawingGroup : Drawing, IPortableDrawingGroupStateSource, IPortableDrawingGroupChildrenSource
    {
        #region Constructors

        /// <summary>
        /// Default DrawingGroup constructor.  
        /// Constructs an object with all properties set to their default values.
        /// </summary>        
        public DrawingGroup()
        {
        } 

        #endregion Constructors

        #region Public methods

        /// <summary>
        /// Opens the DrawingGroup for re-populating it's children, clearing any existing 
        /// children.
        /// </summary>  
        /// <returns>
        /// Returns DrawingContext to populate the DrawingGroup's children.        
        /// </returns>
        public DrawingContext Open()
        {
            VerifyOpen();
            
            _openedForAppend = false;
            
            return new DrawingGroupDrawingContext(this);            
        }

        /// <summary>
        /// Opens the DrawingGroup for populating it's children, appending to
        /// any existing children in the collection.
        /// </summary>  
        /// <returns>
        /// Returns DrawingContext to populate the DrawingGroup's children.        
        /// </returns>
        public DrawingContext Append()
        {
            VerifyOpen();

            _openedForAppend = true;

            return new DrawingGroupDrawingContext(this);
        }

        bool IPortableDrawingGroupStateSource.TryGetPortableDrawingGroupState(out PortableDrawingGroupState state)
        {
            Transform transform = Transform;
            Rect localBounds = GetPortableLocalBounds();
            Rect bounds = TryTransformPortableLocalBounds(
                localBounds, transform, out Rect transformedBounds)
                ? transformedBounds
                : Bounds;
            bool hasBounds = IsPortableUsableRect(bounds);
            bool hasLocalBounds = IsPortableUsableRect(localBounds);
            Geometry clipGeometry = ClipGeometry;
            Brush opacityMask = OpacityMask;
            GuidelineSet guidelineSet = GuidelineSet;
            #pragma warning disable 0618
            var bitmapEffect = BitmapEffect;
            var bitmapEffectInput = BitmapEffectInput;
            #pragma warning restore 0618
            BitmapScalingMode bitmapScalingMode = RenderOptions.GetBitmapScalingMode(this);
            EdgeMode edgeMode = RenderOptions.GetEdgeMode(this);
            ClearTypeHint clearTypeHint = RenderOptions.GetClearTypeHint(this);

            state = new PortableDrawingGroupState
            {
                HasBounds = hasBounds,
                Bounds = hasBounds
                    ? new PortableRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)
                    : PortableRect.Empty,
                HasLocalBounds = hasLocalBounds,
                LocalBounds = hasLocalBounds
                    ? new PortableRect(
                        localBounds.X,
                        localBounds.Y,
                        localBounds.Width,
                        localBounds.Height)
                    : PortableRect.Empty,
                HasTransform = transform != null,
                Transform = transform,
                HasClipGeometry = clipGeometry != null,
                ClipGeometry = clipGeometry,
                HasOpacity = true,
                Opacity = Opacity,
                HasOpacityMask = opacityMask != null,
                OpacityMask = opacityMask,
                HasGuidelineSet = guidelineSet != null,
                GuidelineSet = guidelineSet,
                HasBitmapEffect = bitmapEffect != null,
                BitmapEffect = bitmapEffect,
                HasBitmapEffectInput = bitmapEffectInput != null,
                BitmapEffectInput = bitmapEffectInput,
                HasBitmapScalingMode = bitmapScalingMode != BitmapScalingMode.Unspecified,
                BitmapScalingMode = bitmapScalingMode,
                HasPortableBitmapScalingMode = bitmapScalingMode != BitmapScalingMode.Unspecified,
                PortableBitmapScalingMode = bitmapScalingMode switch
                {
                    BitmapScalingMode.LowQuality => PortableBitmapScalingMode.Linear,
                    BitmapScalingMode.HighQuality => PortableBitmapScalingMode.Fant,
                    BitmapScalingMode.NearestNeighbor => PortableBitmapScalingMode.NearestNeighbor,
                    _ => PortableBitmapScalingMode.Unspecified
                },
                HasEdgeMode = edgeMode != EdgeMode.Unspecified,
                EdgeMode = edgeMode,
                HasPortableEdgeMode = edgeMode != EdgeMode.Unspecified,
                PortableEdgeMode = edgeMode == EdgeMode.Aliased
                    ? PortableEdgeMode.Aliased
                    : PortableEdgeMode.Unspecified,
                HasClearTypeHint = clearTypeHint != ClearTypeHint.Auto,
                ClearTypeHint = clearTypeHint,
                HasPortableClearTypeHint = clearTypeHint != ClearTypeHint.Auto,
                PortableClearTypeHint = clearTypeHint == ClearTypeHint.Enabled
                    ? PortableClearTypeHint.Enabled
                    : PortableClearTypeHint.Auto
            };
            return true;
        }

        private static bool TryTransformPortableLocalBounds(
            Rect localBounds,
            Transform transform,
            out Rect bounds)
        {
            if (transform == null)
            {
                bounds = localBounds;
                return true;
            }

            Matrix matrix = transform.Value;
            if (!double.IsFinite(matrix.M11) ||
                !double.IsFinite(matrix.M12) ||
                !double.IsFinite(matrix.M21) ||
                !double.IsFinite(matrix.M22) ||
                !double.IsFinite(matrix.OffsetX) ||
                !double.IsFinite(matrix.OffsetY) ||
                matrix.M12 != 0 || matrix.M21 != 0)
            {
                bounds = default;
                return false;
            }

            bounds = transform.TransformBounds(localBounds);
            return true;
        }

        private Rect GetPortableLocalBounds()
        {
            var context = new BoundsDrawingContextWalker();
            Geometry clipGeometry = ClipGeometry;
            bool hasClip = clipGeometry != null;
            if (hasClip)
            {
                context.PushClip(clipGeometry);
            }

            DrawingCollection children = Children;
            if (children != null)
            {
                for (int index = 0; index < children.Count; index++)
                {
                    Drawing child = children.Internal_GetItem(index);
                    child?.WalkCurrentValue(context);
                }
            }

            if (hasClip)
            {
                context.Pop();
            }
            return context.Bounds;
        }

        bool IPortableDrawingGroupChildrenSource.TryGetPortableDrawingGroupChildCount(out int count)
        {
            DrawingCollection children = Children;
            count = children?.Count ?? 0;
            return count > 0;
        }

        bool IPortableDrawingGroupChildrenSource.TryGetPortableDrawingGroupChild(int index, out object child)
        {
            DrawingCollection children = Children;
            if (children != null && index >= 0 && index < children.Count)
            {
                child = children.Internal_GetItem(index);
                return child != null;
            }

            child = null;
            return false;
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

        #endregion Public methods        

        #region Internal methods

        /// <summary>
        /// Called by a DrawingContext returned from Open or Append when the content
        /// created by it needs to be committed (because DrawingContext.Close/Dispose
        /// was called)
        /// </summary>
        /// <param name="rootDrawingGroupChildren"> 
        ///     Collection containing the Drawing elements created by a DrawingContext
        ///     returned from Open or Append.
        /// </param>
        internal void Close(DrawingCollection rootDrawingGroupChildren)
        {         
            WritePreamble();            
            
            Debug.Assert(_open);
            Debug.Assert(rootDrawingGroupChildren != null);

            if (!_openedForAppend)
            {
                // Clear out the previous contents by replacing the current collection with 
                // the new collection.
                //
                // When more than one element exists in rootDrawingGroupChildren, the
                // DrawingContext had to create this new collection anyways.  To behave
                // consistently between the one-element and many-element cases,
                // we always set Children to a new DrawingCollection instance during Close().
                //
                // Doing this also avoids having to protect against exceptions being thrown
                // from user-code, which could be executed if a Changed event was fired when
                // we tried to add elements to a pre-existing collection.
                //
                // The collection created by the DrawingContext will no longer be
                // used after the DrawingContext is closed, so we can take ownership
                // of the reference here to avoid any more unneccesary copies.
                Children = rootDrawingGroupChildren;
            }
            else                
            {
                //
                //
                // Append the collection to the current Children collection                
                //
                //
                DrawingCollection children = Children;

                // 
                // Ensure that we can Append to the Children collection
                //
                
                if (children == null)
                {
                    throw new InvalidOperationException(SR.DrawingGroup_CannotAppendToNullCollection);                                
                }
               
                if (children.IsFrozen)
                {
                    throw new InvalidOperationException(SR.DrawingGroup_CannotAppendToFrozenCollection);                                                  
                }

                // Append the new collection to our current Children.
                //
                // TransactionalAppend rolls-back the Append operation in the event
                // an exception is thrown from the Changed event.                
                children.TransactionalAppend(rootDrawingGroupChildren);
            }            

            // This DrawingGroup is no longer open
            _open = false;
        }

        /// <summary>
        /// Calls methods on the DrawingContext that are equivalent to the
        /// Drawing with the Drawing's current value.
        /// </summary>        
        internal override void WalkCurrentValue(DrawingContextWalker ctx)
        {            
            int popCount = 0;

            // We avoid unneccessary ShouldStopWalking checks based on assumptions
            // about when ShouldStopWalking is set.  Guard that assumption with an
            // assertion.
            //
            // ShouldStopWalking is currently only set during a hit-test walk after
            // an object has been hit.  Because a DrawingGroup can't be hit until after 
            // the first Drawing is tested, this method doesn't check ShouldStopWalking
            // until after the first child.  
            //
            // We don't need to add this check to other Drawing subclasses for
            // the same reason -- if the Drawing being tested isn't a DrawingGroup,
            // they are always the 'first child'.  
            //
            // If this assumption is ever broken then the ShouldStopWalking
            // check should be done on the first child -- including in the
            // WalkCurrentValue method of other Drawing subclasses.
            Debug.Assert(!ctx.ShouldStopWalking);            

            //
            // Draw the transform property
            //
            
            // Avoid calling PushTransform if the base value is set to the default and
            // no animations have been set on the property.
            if (!IsBaseValueDefault(DrawingGroup.TransformProperty) ||
                (null != AnimationStorage.GetStorage(this, DrawingGroup.TransformProperty)))
            {
                ctx.PushTransform(Transform);

                popCount++;
            }              

            //
            // Draw the clip property
            //

            // Avoid calling PushClip if the base value is set to the default and
            // no animations have been set on the property.
            if (!IsBaseValueDefault(DrawingGroup.ClipGeometryProperty) ||
                (null != AnimationStorage.GetStorage(this, DrawingGroup.ClipGeometryProperty)))
            {    
                ctx.PushClip(ClipGeometry);

                popCount++;
            }                

            //
            // Draw the opacity property
            //
            
            // Avoid calling PushOpacity if the base value is set to the default and
            // no animations have been set on the property.
            if (!IsBaseValueDefault(DrawingGroup.OpacityProperty) ||
                (null != AnimationStorage.GetStorage(this, DrawingGroup.OpacityProperty)))
            {                    
                // Push the current value of the opacity property, which
                // is what Opacity returns.
                ctx.PushOpacity(Opacity);

                popCount++;
            }

            // Draw the opacity mask property
            //
            if (OpacityMask != null)
            {
                ctx.PushOpacityMask(OpacityMask);
                popCount++;
            }

            //
            // Draw the effect property
            //
            
            // Push the current value of the effect property, which
            // is what BitmapEffect returns.
            if (BitmapEffect != null)
            {
                // Disable warning about obsolete method.  This code must remain active 
                // until we can remove the public BitmapEffect APIs.
                #pragma warning disable 0618
                ctx.PushEffect(BitmapEffect, BitmapEffectInput);
                #pragma warning restore 0618
                popCount++;                
            }

            //
            // Draw the Children collection
            // 

            // Get the current value of the children collection
            DrawingCollection collection = Children;

            // Call Walk on each child
            if (collection != null)
            {
                for (int i = 0; i < collection.Count; i++)
                {
                    Drawing drawing = collection.Internal_GetItem(i);
                    if (drawing != null)
                    {
                        drawing.WalkCurrentValue(ctx);

                        // Don't visit the remaining children if the previous 
                        // child caused us to stop walking.
                        if (ctx.ShouldStopWalking)
                        {
                            break;
                        }
                    }
                }
            }

            //
            // Call Pop() for every Push
            // 
            // Avoid placing this logic in a finally block because if an exception is
            // thrown, the Walk is simply aborted.  There is no requirement to Walk
            // through Pop instructions when an exception is thrown.
            //
            
            for (int i = 0; i < popCount; i++)
            {
                ctx.Pop();                    
            }            
        }

         
        #endregion Internal methods     

        #region Private Methods

        /// <summary>
        /// Called by both Open() and Append(), this method verifies the
        /// DrawingGroup isn't already open, and set's the open flag.
        /// </summary>
        private void VerifyOpen()
        {
            WritePreamble();
            
            // Throw an exception if we are already opened
            if (_open)
            {
                throw new InvalidOperationException(SR.DrawingGroup_AlreadyOpen);                                
            }
            
            _open = true;
        }

        #endregion Private Methods        

        #region Private fields
        
        private bool _openedForAppend;
        private bool _open;
        #endregion Private fields        
    }
}

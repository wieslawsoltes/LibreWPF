// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace System.Windows.Media
{
    public static class PortableRenderDataDrawingContextSinkProvider
    {
        public static IDisposable PushDrawingContextFactory(Func<object, DrawingContext> drawingContextFactory)
        {
            if (drawingContextFactory == null)
            {
                throw new ArgumentNullException(nameof(drawingContextFactory));
            }

            return RenderDataDrawingContextSinkProvider.PushDrawingContextFactory(
                delegate(Visual ownerVisual)
                {
                    return drawingContextFactory(ownerVisual);
                });
        }

        public static IDisposable PushObjectSinkFactory(Func<object, IPortableRenderDataDrawingContextSink> sinkFactory)
        {
            if (sinkFactory == null)
            {
                throw new ArgumentNullException(nameof(sinkFactory));
            }

            return RenderDataDrawingContextSinkProvider.PushSinkFactory(
                delegate(Visual ownerVisual)
                {
                    IPortableRenderDataDrawingContextSink sink = sinkFactory(ownerVisual);
                    return sink == null ? null : new ObjectRenderDataDrawingContextSink(sink);
                });
        }
    }
}

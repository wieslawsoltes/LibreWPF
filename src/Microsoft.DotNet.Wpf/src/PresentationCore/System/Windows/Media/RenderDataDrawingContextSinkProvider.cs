// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;

namespace System.Windows.Media
{
    internal static class RenderDataDrawingContextSinkProvider
    {
        private static readonly object s_syncRoot = new object();
        private static SinkFactoryScope s_currentScope;

        internal static Func<Visual, IRenderDataDrawingContextSink> SinkFactory { get; private set; }

        internal static IDisposable PushSinkFactory(Func<Visual, IRenderDataDrawingContextSink> sinkFactory)
        {
            if (sinkFactory == null)
            {
                throw new ArgumentNullException(nameof(sinkFactory));
            }

            lock (s_syncRoot)
            {
                SinkFactoryScope scope = new SinkFactoryScope(s_currentScope, sinkFactory);
                s_currentScope = scope;
                SinkFactory = sinkFactory;
                return scope;
            }
        }

        internal static IDisposable PushDrawingContextFactory(Func<Visual, DrawingContext> drawingContextFactory)
        {
            if (drawingContextFactory == null)
            {
                throw new ArgumentNullException(nameof(drawingContextFactory));
            }

            return PushSinkFactory(
                delegate(Visual ownerVisual)
                {
                    DrawingContext drawingContext = drawingContextFactory(ownerVisual);
                    return drawingContext == null ? null : new DrawingContextRenderDataSink(drawingContext);
                });
        }

        internal static IDisposable PushObjectSinkFactory(Func<Visual, object> sinkFactory)
        {
            if (sinkFactory == null)
            {
                throw new ArgumentNullException(nameof(sinkFactory));
            }

            return PushSinkFactory(
                delegate(Visual ownerVisual)
                {
                    object sink = sinkFactory(ownerVisual);
                    return sink is IPortableRenderDataDrawingContextSink portableSink
                        ? new ObjectRenderDataDrawingContextSink(portableSink)
                        : null;
                });
        }

        internal static IRenderDataDrawingContextSink CreateSink(Visual ownerVisual)
        {
            Debug.Assert(ownerVisual != null);

            Func<Visual, IRenderDataDrawingContextSink> sinkFactory;

            lock (s_syncRoot)
            {
                sinkFactory = SinkFactory;
            }

            return sinkFactory?.Invoke(ownerVisual);
        }

        private static void RestoreSinkFactory(SinkFactoryScope scope)
        {
            lock (s_syncRoot)
            {
                if (scope._disposed)
                {
                    return;
                }

                scope._disposed = true;

                if (!ReferenceEquals(s_currentScope, scope))
                {
                    return;
                }

                do
                {
                    s_currentScope = s_currentScope._previousScope;
                }
                while (s_currentScope != null && s_currentScope._disposed);

                SinkFactory = s_currentScope?._sinkFactory;
            }
        }

        private sealed class SinkFactoryScope : IDisposable
        {
            internal readonly SinkFactoryScope _previousScope;
            internal readonly Func<Visual, IRenderDataDrawingContextSink> _sinkFactory;
            internal bool _disposed;

            internal SinkFactoryScope(
                SinkFactoryScope previousScope,
                Func<Visual, IRenderDataDrawingContextSink> sinkFactory)
            {
                _previousScope = previousScope;
                _sinkFactory = sinkFactory;
            }

            public void Dispose()
            {
                RestoreSinkFactory(this);
            }
        }
    }
}

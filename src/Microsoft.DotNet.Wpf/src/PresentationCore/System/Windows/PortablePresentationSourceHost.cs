// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using MS.Internal;
using ProGPU.Wpf.Interop;

namespace System.Windows
{
    public static class PortablePresentationSourceHost
    {
        public static IPortablePresentationSourceHost Create(double dpiScaleX = 1.0, double dpiScaleY = 1.0)
        {
            return new PortablePresentationSource(dpiScaleX, dpiScaleY);
        }

        /// <summary>
        /// Reads the live portable source's client-DIP to desktop mapping, including
        /// when the source is the public HwndSource facade returned by FromVisual.
        /// </summary>
        /// <remarks>
        /// Call on the source's dispatcher thread. The returned value is a snapshot;
        /// read it again after source geometry changes. Its scale is independent of
        /// CompositionTarget.TransformToDevice. False means this mapping is unavailable,
        /// not that the source's screen coordinates use any particular units.
        /// Native HWND and custom sources keep their existing coordinate contracts.
        /// </remarks>
        public static bool TryGetDesktopTransform(PresentationSource source, out PortableDesktopTransform transform)
        {
            ArgumentNullException.ThrowIfNull(source);
            source.VerifyAccess();
            transform = default;
            if (!PointUtil.TryGetPortablePresentationSource(source, out PortablePresentationSource portable) ||
                source.IsDisposed || portable.IsDisposed || portable.HwndSource.IsDisposed)
            {
                return false;
            }

            transform = portable.DesktopTransform;
            return true;
        }
    }
}

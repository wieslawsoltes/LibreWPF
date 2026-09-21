// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;

namespace System.Windows.Interop
{
    /// <summary>
    /// Attaches a typed synchronized GPU-image provider to D3DImage while
    /// preserving canonical TYPE_D3DIMAGE and present protocol replay.
    /// </summary>
    public static class PortableD3DImageSourceFactory
    {
        public static void Attach(
            D3DImage image,
            IPortableD3DImageSource source)
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(source);
            image.SetPortableD3DImageSource(source);
        }

        public static void Detach(D3DImage image)
        {
            ArgumentNullException.ThrowIfNull(image);
            image.ClearPortableD3DImageSource();
        }
    }
}

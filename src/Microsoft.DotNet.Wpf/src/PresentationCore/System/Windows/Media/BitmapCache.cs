// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//

using ProGPU.Wpf.Interop;

namespace System.Windows.Media
{
    public partial class BitmapCache : CacheMode, IPortableBitmapCacheSource
    {
        public BitmapCache()
        {
        }

        public BitmapCache(double renderAtScale)
        {
            RenderAtScale = renderAtScale;
        }

        bool IPortableBitmapCacheSource.TryGetPortableBitmapCache(
            out PortableBitmapCache cache)
        {
            cache = new PortableBitmapCache(
                RenderAtScale,
                SnapsToDevicePixels,
                EnableClearType);
            return true;
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProGPU.Wpf.Interop;

namespace System.Windows.Media
{
    /// <summary>
    /// Attaches a typed portable GPU-video provider to a MediaPlayer without
    /// changing the canonical DrawingContext.DrawVideo API.
    /// </summary>
    public static class PortableMediaPlayerSourceFactory
    {
        public static void Attach(
            MediaPlayer player,
            IPortableMediaPlayerSource source)
        {
            ArgumentNullException.ThrowIfNull(player);
            ArgumentNullException.ThrowIfNull(source);
            player.SetPortableMediaPlayerSource(source);
        }

        public static void Detach(MediaPlayer player)
        {
            ArgumentNullException.ThrowIfNull(player);
            player.ClearPortableMediaPlayerSource();
        }
    }
}

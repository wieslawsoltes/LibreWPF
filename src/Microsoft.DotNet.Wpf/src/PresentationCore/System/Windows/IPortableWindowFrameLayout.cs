// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows
{
    // Only the actual source root Window converts between WPF's outer Window
    // layout size and the presentation source's native client size. Ordinary
    // source roots, popups and visuals retain their client-sized layout.
    internal interface IPortableWindowFrameLayout
    {
        Size GetOuterSizeForClient(Size clientSize);

        Size GetClientSizeForOuter(Size outerSize);
    }
}

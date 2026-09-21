// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows
{
    public static class PortablePresentationSourceHost
    {
        public static IPortablePresentationSourceHost Create(double dpiScaleX = 1.0, double dpiScaleY = 1.0)
        {
            return new PortablePresentationSource(dpiScaleX, dpiScaleY);
        }
    }
}

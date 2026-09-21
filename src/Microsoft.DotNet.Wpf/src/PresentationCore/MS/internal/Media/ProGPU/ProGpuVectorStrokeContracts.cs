// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProGPU.Vector
{
    // PresentationCore compiles a deliberately small, internal subset of the
    // ProGPU.Vector path implementation. Keep this contract synchronized with
    // ProGPU.Vector.PenLineCap without importing the public brush/pen surface.
    internal enum PenLineCap
    {
        Flat = 0,
        Square = 1,
        Round = 2,
        Triangle = 3,
    }
}

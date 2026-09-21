// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows
{
    public interface IPortableVisualOwnerHost
    {
        object PortableVisualParent { get; }

        bool IsPortableInputEnabled { get; }

        PortableVisualOwnerKind PortableVisualOwnerKind { get; }
    }
}

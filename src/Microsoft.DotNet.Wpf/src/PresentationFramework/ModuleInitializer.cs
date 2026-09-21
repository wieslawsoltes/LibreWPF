// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Win32;
using System.Windows;

internal static class ModuleInitializer
{
#pragma warning disable CA2255
    [ModuleInitializer]
    public static void Initialize()
    {
        PortableWindowActivationService.RegisterPortableInteropService();
        PortableLauncherService.RegisterPortableInteropService();
        PortableMessageBoxService.RegisterPortableInteropService();
        PortableFileDialogService.RegisterPortableInteropService();
    }
#pragma warning restore CA2255
}

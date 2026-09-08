// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using ProGPU.Wpf.Interop;

namespace System.Windows;

internal static class PortableMediaTestStartup
{
    // Test-process configuration only. Run Windows native and portable lanes in
    // separate processes; never reset the product's frozen backend between tests.
    [ModuleInitializer]
    internal static void Initialize()
    {
        string? selection = Environment.GetEnvironmentVariable("LIBREWPF_TEST_MEDIA_BACKEND");
        if (string.IsNullOrEmpty(selection)) return;
        PortableWpfRuntime.SelectMediaBackend(selection switch
        {
            "Portable" => PortableWpfMediaBackend.Portable,
            "WindowsMil" => PortableWpfMediaBackend.WindowsMil,
            _ => throw new InvalidOperationException("LIBREWPF_TEST_MEDIA_BACKEND must be Portable or WindowsMil.")
        });
    }
}

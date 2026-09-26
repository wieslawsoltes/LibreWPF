// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using ProGPU.Wpf.Interop;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Actual Windows OLE/GDI is required; no skipped test outcome is accepted.");
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Clipboard contracts require the actual STA entry thread.");
        PortableWpfRuntime.SelectMediaBackend(PortableWpfMediaBackend.Portable);
        RunContracts();
        Console.WriteLine($"Windows clipboard package contracts passed: 4; skipped: 0; architecture: {RuntimeInformation.ProcessArchitecture}.");
        return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunContracts()
    {
        WindowsClipboardImageTests tests = new();
        tests.OpaqueImageOutlivesFlushedOleMediumAndCanBeRepublished();
        tests.PartialPaletteImageUsesRealGdiColorConversion();
        tests.ExportRejectsWrongDescriptorAndCallerOwnedMediumAtomically();
        tests.ImportReleasesCustomMediumOwnerOnSuccessFailureAndWrongTymed();
    }
}

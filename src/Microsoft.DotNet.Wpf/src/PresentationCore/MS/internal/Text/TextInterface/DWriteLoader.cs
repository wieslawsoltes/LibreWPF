// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MS.Internal.Text.TextInterface
{
    internal static unsafe class DWriteLoader
    {
        private static IntPtr _dwrite;
        private static delegate* unmanaged<int, void*, void*, int> _dwriteCreateFactory;

        internal static void LoadDWrite()
        {
            // The module owns the code and vtables used by retained DirectWrite
            // font objects. ProcessExit does not quiesce those callers: another
            // thread, or a later exit handler, can still query a live GlyphRun.
            // Keep this one loader reference and factory entry point until the
            // operating system terminates the process; do not unload at ProcessExit.
            _dwrite = LoadDWriteLibraryAndGetProcAddress(out delegate* unmanaged<int, void*, void*, int> dwriteCreateFactory);

            if (_dwrite == IntPtr.Zero)
                throw new DllNotFoundException("dwrite.dll", new Win32Exception());

            if (dwriteCreateFactory == null)
                throw new InvalidOperationException();

            _dwriteCreateFactory = dwriteCreateFactory;
        }

        internal static delegate* unmanaged<int, void*, void*, int> GetDWriteCreateFactoryFunctionPointer()
        {
            return _dwriteCreateFactory;
        }

        private static IntPtr LoadDWriteLibraryAndGetProcAddress(out delegate* unmanaged<int, void*, void*, int> DWriteCreateFactory)
        {
            IntPtr hDWriteLibrary = NativeLibrary.Load("dwrite.dll", typeof(DWriteLoader).Assembly, DllImportSearchPath.System32);

            if (hDWriteLibrary != IntPtr.Zero)
            {
                DWriteCreateFactory = (delegate* unmanaged<int, void*, void*, int>)NativeLibrary.GetExport(hDWriteLibrary, "DWriteCreateFactory");
            }
            else
            {
                DWriteCreateFactory = null;
            }

            return hDWriteLibrary;
        }
    }
}

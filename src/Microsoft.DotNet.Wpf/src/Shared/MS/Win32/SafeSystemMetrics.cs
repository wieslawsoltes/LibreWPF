// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

//  File: SafeSystemMetrics.cs
//  This class is copied from the system metrics class in frameworks. The
//  reason it exists is to consolidate all system metric calls through one layer
//  so that maintenance from a security stand point gets easier. We will add
//  mertrics on a need basis. The caching code is removed since the original calls 
//  that were moved here do not rely on caching. If there is a percieved perf. problem
//  we can work on enabling this.

using MS.Internal.Interop;

namespace MS.Win32
{
    /// <summary>
    ///     Contains properties that are queries into the system's various settings.
    /// </summary>
    internal sealed class SafeSystemMetrics
    {

        private SafeSystemMetrics()
        {
        }

#if !PRESENTATION_CORE
        /// <summary>
        ///     Maps to SM_CXVIRTUALSCREEN
        /// </summary>
        internal static int VirtualScreenWidth
        {
            get
            {

                return GetSystemMetrics(SM.CXVIRTUALSCREEN, 0);
            }
        }

        /// <summary>
        ///     Maps to SM_CYVIRTUALSCREEN
        /// </summary>
        internal static int VirtualScreenHeight
        {
            get
            {
                return GetSystemMetrics(SM.CYVIRTUALSCREEN, 0);
            }
        }
#endif //end !PRESENTATIONCORE

        /// <summary>
        ///     Maps to SM_CXDOUBLECLK
        /// </summary>
        internal static int DoubleClickDeltaX
        {
            get
            {
                return GetSystemMetrics(SM.CXDOUBLECLK, 4);
            }
        }

        /// <summary>
        ///     Maps to SM_CYDOUBLECLK
        /// </summary>
        internal static int DoubleClickDeltaY
        {
            get
            {
                return GetSystemMetrics(SM.CYDOUBLECLK, 4);
            }
        }

            
        /// <summary>
        ///     Maps to SM_CXDRAG
        /// </summary>
        internal static int DragDeltaX
        {
            get
            {
                return GetSystemMetrics(SM.CXDRAG, 4);
            }
        }

        /// <summary>
        ///     Maps to SM_CYDRAG
        /// </summary>
        internal static int DragDeltaY
        {
            get
            {
                return GetSystemMetrics(SM.CYDRAG, 4);
            }
        }

        ///<summary> 
        /// Is an IMM enabled ? Maps to SM_IMMENABLED
        ///</summary> 
        internal static bool IsImmEnabled
        {
            get
            {
                return  (GetSystemMetrics(SM.IMMENABLED, 0) != 0);
            }

        }

        private static int GetSystemMetrics(SM metric, int nonWindowsDefault)
        {
            return System.OperatingSystem.IsWindows()
                ? UnsafeNativeMethods.GetSystemMetrics(metric)
                : nonWindowsDefault;
        }

    }
}

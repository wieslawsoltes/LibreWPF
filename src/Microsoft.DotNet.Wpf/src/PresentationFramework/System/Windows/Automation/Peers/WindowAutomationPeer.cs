// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Text;
using System.ComponentModel;
using MS.Win32;

namespace System.Windows.Automation.Peers
{
    /// 
    public class WindowAutomationPeer : FrameworkElementAutomationPeer
    {
        ///
        public WindowAutomationPeer(Window owner): base(owner)
        {}
    
        ///
        protected override string GetClassNameCore()
        {
            return "Window";
        }

        ///
        protected override string GetNameCore()
        {
            string name = base.GetNameCore();

            if(name.Length == 0)
            {
                Window window = (Window)Owner;

                if(!window.IsSourceWindowNull)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder(512);
                        UnsafeNativeMethods.GetWindowText(new HandleRef(null, window.Handle), sb, sb.Capacity);
                        name = sb.ToString();
                    }
                    catch (Win32Exception)
                    {
                        name = window.Title;
                    }

                    name ??= "";
                }
            }

            return name;
        }

        ///
        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.Window;
        }



        ///
        protected override Rect GetBoundingRectangleCore()
        {
            Window window = (Window)Owner;

            if (!OperatingSystem.IsWindows())
            {
                return GetPortableBoundingRectangle(window);
            }

            Rect bounds = new Rect(0,0,0,0);
            
            if(!window.IsSourceWindowNull)
            {
                NativeMethods.RECT rc = new NativeMethods.RECT(0,0,0,0);
                IntPtr windowHandle = window.Handle;
                if(windowHandle != IntPtr.Zero) //it is Zero on a window that was just closed
                {
                    try { SafeNativeMethods.GetWindowRect(new HandleRef(null, windowHandle), ref rc); }
                    catch(Win32Exception) {}
                }        
                bounds = new Rect(rc.left, rc.top, rc.right - rc.left, rc.bottom - rc.top);
            }

            return bounds;
        }

        private static Rect GetPortableBoundingRectangle(Window window)
        {
            if (PresentationSource.CriticalFromVisual(window) == null)
            {
                return Rect.Empty;
            }

            Size size = window.RenderSize;
            if (size.Width <= 0)
            {
                size.Width = GetNonNegativeFiniteSize(window.ActualWidth);
            }
            if (size.Height <= 0)
            {
                size.Height = GetNonNegativeFiniteSize(window.ActualHeight);
            }
            if (size.Width <= 0)
            {
                size.Width = GetNonNegativeFiniteSize(window.Width);
            }
            if (size.Height <= 0)
            {
                size.Height = GetNonNegativeFiniteSize(window.Height);
            }

            if (size.Width <= 0 && size.Height <= 0)
            {
                return Rect.Empty;
            }

            Point topLeft = new Point(GetFiniteCoordinate(window.Left), GetFiniteCoordinate(window.Top));
            try
            {
                topLeft = window.PointToScreen(new Point(0, 0));
            }
            catch (InvalidOperationException)
            {
            }

            return new Rect(topLeft, size);
        }

        private static double GetNonNegativeFiniteSize(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? 0
                : Math.Max(0, value);
        }

        private static double GetFiniteCoordinate(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? 0
                : value;
        }

        protected override bool IsDialogCore()
        {
            Window window = (Window)Owner;
            if (MS.Internal.Helper.IsDefaultValue(AutomationProperties.IsDialogProperty, window))
            {
                return window.IsShowingAsDialog;
            }
            else
            {
                return AutomationProperties.GetIsDialog(window);
            }
        }
    }
}

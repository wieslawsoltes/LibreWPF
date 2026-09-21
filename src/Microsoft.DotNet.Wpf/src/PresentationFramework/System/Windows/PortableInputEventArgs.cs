// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace System.Windows
{
    internal enum PortableInputEventKind
    {
        KeyDown,
        KeyUp,
        TextInput,
        MouseMove,
        MouseDown,
        MouseUp,
        MouseWheel
    }

    internal enum PortableMouseButton
    {
        None,
        Left,
        Right,
        Middle,
        XButton1,
        XButton2,
        Other
    }

    [Flags]
    internal enum PortableInputModifiers
    {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
        Super = 8
    }

    internal sealed class PortableInputEventArgs : EventArgs
    {
        internal PortableInputEventArgs(
            PortableInputEventKind kind,
            string key = null,
            int scanCode = 0,
            char? character = null,
            double x = 0,
            double y = 0,
            double deltaX = 0,
            double deltaY = 0,
            PortableMouseButton button = PortableMouseButton.None,
            PortableInputModifiers modifiers = PortableInputModifiers.None)
        {
            Kind = kind;
            Key = key;
            ScanCode = scanCode;
            Character = character;
            X = x;
            Y = y;
            DeltaX = deltaX;
            DeltaY = deltaY;
            Button = button;
            Modifiers = modifiers;
        }

        public PortableInputEventKind Kind { get; }

        public string Key { get; }

        public int ScanCode { get; }

        public char? Character { get; }

        public double X { get; }

        public double Y { get; }

        public double DeltaX { get; }

        public double DeltaY { get; }

        public PortableMouseButton Button { get; }

        public PortableInputModifiers Modifiers { get; }

        public bool Handled { get; set; }
    }
}

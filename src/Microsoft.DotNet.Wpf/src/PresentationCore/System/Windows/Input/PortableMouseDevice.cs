// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Windows.Input
{
    internal sealed class PortableMouseDevice : MouseDevice
    {
        private readonly Dictionary<MouseButton, MouseButtonState> _buttonStates = new Dictionary<MouseButton, MouseButtonState>();

        internal PortableMouseDevice(InputManager inputManager)
            : base(inputManager)
        {
        }

        internal void SetButtonState(MouseButton button, MouseButtonState buttonState)
        {
            if (buttonState == MouseButtonState.Released)
            {
                _buttonStates.Remove(button);
            }
            else
            {
                _buttonStates[button] = buttonState;
            }
        }

        internal override MouseButtonState GetButtonStateFromSystem(MouseButton mouseButton)
        {
            return _buttonStates.TryGetValue(mouseButton, out MouseButtonState buttonState)
                ? buttonState
                : MouseButtonState.Released;
        }
    }
}

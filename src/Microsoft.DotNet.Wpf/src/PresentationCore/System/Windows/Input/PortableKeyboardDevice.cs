// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Windows.Input
{
    internal sealed class PortableKeyboardDevice : KeyboardDevice
    {
        private readonly Dictionary<Key, KeyStates> _keyStates = new Dictionary<Key, KeyStates>();

        internal PortableKeyboardDevice(InputManager inputManager)
            : base(inputManager)
        {
        }

        internal void SetKeyStates(Key key, KeyStates keyStates)
        {
            if (keyStates == KeyStates.None)
            {
                _keyStates.Remove(key);
            }
            else
            {
                _keyStates[key] = keyStates;
            }
        }

        protected override KeyStates GetKeyStatesFromSystem(Key key)
        {
            return _keyStates.TryGetValue(key, out KeyStates keyStates)
                ? keyStates
                : KeyStates.None;
        }
    }
}

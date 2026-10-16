// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Windows.Threading;

namespace System.Windows
{
    // The portable native loop replaces Dispatcher.PushFrame, so it must own
    // the same source dispatcher context across native input callbacks.
    internal readonly struct PortableDispatcherContextScope : IDisposable
    {
        private readonly SynchronizationContext _previous;

        internal PortableDispatcherContextScope(Dispatcher dispatcher)
        {
            dispatcher.VerifyAccess();
            _previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        }

        public void Dispose() => SynchronizationContext.SetSynchronizationContext(_previous);
    }
}

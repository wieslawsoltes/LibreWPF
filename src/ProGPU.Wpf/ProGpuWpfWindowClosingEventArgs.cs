using System;

namespace System.Windows.Media.ProGPU;

public sealed class ProGpuWpfWindowClosingEventArgs : EventArgs
{
    public bool Cancel { get; set; }
}

using System;
using System.Collections.Generic;

namespace System.Windows.Media.ProGPU.Composition.Mil;

// Shared by nested visual/drawing replay during capture, including VisualBrush
// reentry through another renderer instance. Storage is bounded by graph depth;
// ordinary non-capture replay takes only the inactive branch.
internal static class WpfCaptureReplayGuard
{
    [ThreadStatic] private static int s_captureDepth;
    [ThreadStatic] private static HashSet<object>? s_active;
    [ThreadStatic] private static HashSet<object>? s_activeBounds;

    internal static bool IsActive => s_captureDepth != 0;

    internal static CaptureScope Begin()
    {
        s_active ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
        s_activeBounds ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
        s_captureDepth++;
        return new CaptureScope();
    }

    internal static NodeScope Enter(object source)
    {
        if (!IsActive) return default;
        if (s_active!.Count >= 256 || !s_active.Add(source))
            throw new InvalidOperationException("The cached visual/drawing source graph has a cycle or exceeds the native depth limit.");
        return new NodeScope(source, s_active);
    }

    // Bounds inference can run while the same drawing is being replayed. Its
    // recursion needs an independent ancestry, not an exemption from guarding.
    internal static NodeScope EnterBounds(object source)
    {
        if (!IsActive) return default;
        if (s_activeBounds!.Count >= 256 || !s_activeBounds.Add(source))
            throw new InvalidOperationException("The cached drawing bounds graph has a cycle or exceeds the native depth limit.");
        return new NodeScope(source, s_activeBounds);
    }

    internal readonly struct CaptureScope : IDisposable
    {
        public void Dispose()
        {
            if (--s_captureDepth == 0)
            {
                s_active!.Clear();
                s_activeBounds!.Clear();
            }
        }
    }

    internal readonly struct NodeScope(object? source, HashSet<object>? ancestry) : IDisposable
    {
        public void Dispose()
        {
            if (source != null) ancestry!.Remove(source);
        }
    }
}

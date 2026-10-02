// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable enable
using ProGPU.Wpf.Interop;

namespace MS.Internal.TextFormatting;

// Ordinary paragraphs are managed snapshots. Hinted paragraphs additionally
// require one independently retained producer reference per source line/break.
internal sealed class PortableTextParagraphReference : IDisposable
{
    private readonly object _gate = new();
    private IPortableTextParagraph _paragraph;
    private IPortableHintedTextParagraph? _hinted;
    private bool _closed, _disposing;

    private PortableTextParagraphReference(IPortableTextParagraph paragraph)
    {
        _paragraph = paragraph;
        if (paragraph is not IPortableHintedTextParagraph) GC.SuppressFinalize(this);
    }

    internal static PortableTextParagraphReference Acquire(IPortableTextParagraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        var reference = new PortableTextParagraphReference(paragraph);
        try
        {
            if (paragraph is IPortableHintedTextParagraph hinted)
            {
                var displayIdentity = paragraph is IPortableDisplayTextParagraph display
                    ? new PortableTextDisplayIdentity(display) : null;
                reference._hinted = hinted.Retain() ?? throw new InvalidOperationException("The hinted paragraph returned no retained reference.");
                reference._paragraph = reference._hinted as IPortableTextParagraph ??
                    throw new InvalidOperationException("The retained hinted paragraph lost its source paragraph contract.");
                if (displayIdentity != null) displayIdentity.Validate(reference._paragraph);
                else if (reference._paragraph is IPortableDisplayTextParagraph)
                    throw new InvalidOperationException("Retain changed an Ideal generation to Display.");
            }
            return reference;
        }
        catch { reference.DisposePreservingFailure(); throw; }
    }

    internal IPortableTextParagraph Paragraph
    {
        get { lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); return _paragraph; } }
    }

    internal PortableTextParagraphReference Retain()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            return Acquire(_paragraph);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
            if (_disposing) return;
            _disposing = true;
            try
            {
                // Keep the exact ended reference if native retirement throws.
                // Its Dispose contract retries retirement without ending a use twice.
                _hinted?.Dispose();
                _hinted = null;
                GC.SuppressFinalize(this);
            }
            finally { _disposing = false; }
        }
    }

    internal void DisposePreservingFailure()
    {
        try { Dispose(); }
        catch { /* Preserve the construction failure; finalization still owns this retry. */ }
    }

    ~PortableTextParagraphReference() => DisposePreservingFailure();
}

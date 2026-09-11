// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MS.Internal.Documents;

// Participates in the source FlowDocument formatter lifecycle, not a second
// document model. Drawing and text-view consumers borrow only the live layout.
internal sealed class PortableFlowDocumentFormatter(FlowDocument document) : IFlowDocumentFormatter
{
    private bool _dirty = true;
    private bool _arranged;
    private double _width, _dpi;
    private TextFormattingMode _mode;
    internal PortableFlowDocumentLayout Layout { get; private set; }
    internal event EventHandler ContentInvalidated;
    internal event EventHandler Suspended;

    internal void Format(Size constraint, double pixelsPerDip, TextFormattingMode mode)
    {
        document.Dispatcher.VerifyAccess();
        if (document.StructuralCache.IsFormattingInProgress)
            throw new InvalidOperationException(SR.FlowDocumentFormattingReentrancy);
        if (document.StructuralCache.IsContentChangeInProgress)
            throw new InvalidOperationException(SR.TextContainerChangingReentrancyInvalid);
        double width = FlowDocumentFormatter.ComputePageSize(document, constraint).Width;
        if (!_dirty && Layout != null && _width == width && _dpi == pixelsPerDip && _mode == mode) return;
        _dirty = true;
        _arranged = false;
        PortableFlowDocumentLayout next = null;
        using (document.Dispatcher.DisableProcessing())
        using (document.StructuralCache.BeginPortableFormat())
        {
            try
            {
                next = PortableFlowDocumentLayout.Create(document, width, pixelsPerDip, mode);
                document.StructuralCache.DetectInvalidOperation();
            }
            catch { next?.Dispose(); throw; }
        }
        var previous = Layout;
        Layout = next;
        _width = width; _dpi = pixelsPerDip; _mode = mode;
        _dirty = false;
        document.StructuralCache.ClearUpdateInfo(false);
        previous?.Dispose();
    }

    internal void OnArranged()
    {
        if (_dirty || Layout == null) throw new InvalidOperationException(SR.TextViewInvalidLayout);
        _arranged = true;
    }

    void IFlowDocumentFormatter.OnContentInvalidated(bool affectsLayout) => Invalidate();
    void IFlowDocumentFormatter.OnContentInvalidated(bool affectsLayout, ITextPointer start, ITextPointer end) => Invalidate();
    private void Invalidate()
    {
        _dirty = true; _arranged = false;
        ContentInvalidated?.Invoke(this, EventArgs.Empty);
    }

    void IFlowDocumentFormatter.Suspend()
    {
        _dirty = true; _arranged = false;
        try { Suspended?.Invoke(this, EventArgs.Empty); }
        finally { Layout?.Dispose(); Layout = null; }
    }

    bool IFlowDocumentFormatter.IsLayoutDataValid => Layout != null && !_dirty && _arranged &&
        !document.StructuralCache.IsContentChangeInProgress && !document.StructuralCache.IsFormattingInProgress;
}

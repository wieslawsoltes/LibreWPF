// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ProGPU.Wpf.Interop;

namespace MS.Internal.Documents;

internal partial class FlowDocumentView
{
    private PortableFlowDocumentFormatter _portableFormatter;
    private PortableFlowDocumentVisual _portableVisual;
    private PortableFlowDocumentTextView _portableTextView;
    private bool _portableViewUpdatePending;
    private static bool UsesPortableDocument => PortableWpfRuntime.GetMediaBackendAndFreeze() == PortableWpfMediaBackend.Portable;
    internal PortableFlowDocumentLayout PortableLayout => _portableFormatter?.Layout;
    internal bool PortableLayoutValid => !_suspendLayout && _portableFormatter != null &&
        ((IFlowDocumentFormatter)_portableFormatter).IsLayoutDataValid;
    internal Vector PortableScrollOffset => _scrollData?.Offset ?? new Vector();
    internal PortableFlowDocumentTextView PortableTextView => _portableTextView ??= new(this, Document);

    private void EnsurePortableFormatter()
    {
        if (_portableFormatter != null) return;
        _portableFormatter = Document.PortableBottomlessFormatter;
        _portableFormatter.ContentInvalidated += OnPortableContentInvalidated;
        _portableFormatter.Suspended += OnPortableFormatterSuspended;
    }

    private Size MeasurePortableDocument(Size constraint)
    {
        EnsurePortableFormatter();
        _portableFormatter.Format(constraint, GetDpi().PixelsPerDip, TextOptions.GetTextFormattingMode(this));
        Size size = _portableFormatter.Layout.Size;
        return _scrollData == null ? size : new(Math.Min(constraint.Width, size.Width), Math.Min(constraint.Height, size.Height));
    }

    private void ArrangePortableDocument(Size viewport)
    {
        EnsurePortableFormatter();
        if (_portableFormatter.Layout == null) throw new InvalidOperationException(SR.TextViewInvalidLayout);
        Size extent = _portableFormatter.Layout.Size;
        Vector offset = PortableScrollOffset;
        offset.X = Math.Clamp(offset.X, 0, Math.Max(0, extent.Width - viewport.Width));
        offset.Y = Math.Clamp(offset.Y, 0, Math.Max(0, extent.Height - viewport.Height));
        ResetScrollData(viewport, extent, offset);
        if (_portableVisual == null)
        {
            _portableVisual = new(this);
            AddVisualChild(_portableVisual);
        }
        _portableVisual.Draw(_portableFormatter.Layout);
        _portableVisual.Offset = -offset;
        Rect clip = new(offset.X, offset.Y, viewport.Width, viewport.Height);
        if (_portableVisual.Clip is not RectangleGeometry geometry || geometry.Rect != clip)
        {
            var viewportClip = new RectangleGeometry(clip); viewportClip.Freeze();
            _portableVisual.Clip = viewportClip;
        }
        _portableFormatter.OnArranged();
        _portableViewUpdatePending = true;
    }

    private void OnPortableLayoutUpdated(object sender, EventArgs args)
    {
        if (_portableViewUpdatePending && PortableLayoutValid && IsMeasureValid && IsArrangeValid)
        {
            _portableViewUpdatePending = false;
            _portableTextView?.PublishUpdate();
        }
    }

    private void OnPortableContentInvalidated(object sender, EventArgs args)
    {
        InvalidateMeasure(); InvalidateVisual();
    }

    private void OnPortableFormatterSuspended(object sender, EventArgs args)
    {
        var formatter = _portableFormatter;
        _portableFormatter = null;
        if (formatter != null)
        {
            formatter.ContentInvalidated -= OnPortableContentInvalidated;
            formatter.Suspended -= OnPortableFormatterSuspended;
        }
        if (!_suspendLayout) DisconnectPortableVisual();
        _portableViewUpdatePending = false;
    }

    private void DetachPortableFormatter()
    {
        var formatter = _portableFormatter;
        OnPortableFormatterSuspended(formatter, EventArgs.Empty);
        try { if (formatter != null) ((IFlowDocumentFormatter)formatter).Suspend(); }
        finally { DisconnectPortableVisual(); }
    }

    private void DisconnectPortableVisual()
    {
        if (_portableVisual == null) return;
        _portableVisual.Clear();
        RemoveVisualChild(_portableVisual);
        _portableVisual = null;
    }
}

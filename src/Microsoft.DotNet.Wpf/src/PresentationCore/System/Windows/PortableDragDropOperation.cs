// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace System.Windows
{
    /// <summary>
    /// Portable (non-Windows, non-OLE) replacement for the source half of
    /// <see cref="DragDrop.DoDragDrop"/>. Real WPF's drag source blocks on a Win32 OLE modal loop
    /// (<c>OleServicesContext.OleDoDragDrop</c>) that pumps native mouse/keyboard messages and
    /// calls back into <see cref="OleDragSource"/>/<see cref="OleDropTarget"/> as the pointer moves
    /// over registered drop-target HWNDs. <see cref="PortablePresentationSource"/> has no HWND/OLE
    /// message pump to drive that loop, so <see cref="DragDrop.DoDragDrop"/> previously "failed
    /// closed" (returned <see cref="DragDropEffects.None"/> immediately) for portable sources -
    /// meaning no WPF drag-and-drop (e.g. a toolbox item dragged onto a design surface) ever
    /// actually ran off Windows.
    ///
    /// This reimplements the same source-side protocol without OLE: capture the mouse on the drag
    /// source, push a nested <see cref="DispatcherFrame"/> so the call stays synchronous exactly
    /// like the OLE path while still processing input, and on every mouse move hit-test the
    /// portable source's visual tree (UIElement.InputHitTest on the root visual, which is already
    /// source-agnostic) to find the current drop target - then drive the SAME
    /// DragEnter/DragOver/DragLeave/Drop routed events real portable drop targets already handle
    /// via <see cref="DragDrop.ProcessPortableDragDrop"/> (that half of the pipeline was already
    /// built; only this source-side driver was missing). QueryContinueDrag/GiveFeedback are raised
    /// with the same default fallback semantics <see cref="OleDragSource"/> uses on Windows, so a
    /// handler written against the public DragDrop routed events behaves identically on both.
    ///
    /// Scoped to <see cref="UIElement"/> drag sources only (matches every current caller -
    /// WpfToolbox's own drag source is a ListBox); <see cref="ContentElement"/>/
    /// <see cref="UIElement3D"/> sources fail closed the same way the whole portable path used to.
    /// </summary>
    internal sealed class PortableDragDropOperation
    {
        private readonly UIElement _dragSource;
        private readonly PortablePresentationSource _source;
        private readonly DataObject _dataObject;
        private readonly DragDropEffects _allowedEffects;

        private DependencyObject _currentTarget;
        private DragDropEffects _lastEffects = DragDropEffects.None;
        private DragAction _action = DragAction.Continue;
        private bool _dropped;
        private Point _lastRootPoint;
        private bool _hasLastRootPoint;

        private PortableDragDropOperation(UIElement dragSource, PortablePresentationSource source, DataObject dataObject, DragDropEffects allowedEffects)
        {
            _dragSource = dragSource;
            _source = source;
            _dataObject = dataObject;
            _allowedEffects = allowedEffects;
        }

        /// <summary>
        /// Runs a portable drag-and-drop operation for <paramref name="dragSource"/>, or returns
        /// <see cref="DragDropEffects.None"/> immediately if the source isn't a portable
        /// <see cref="UIElement"/> with a live root visual (mirrors the old fail-closed behavior
        /// for every case this doesn't (yet) support).
        /// </summary>
        [ThreadStatic]
        private static bool s_isRunning;

        internal static DragDropEffects Run(DependencyObject dragSource, DataObject dataObject, DragDropEffects allowedEffects)
        {
            if (dragSource is not UIElement dragElement)
                return DragDropEffects.None;

            if (PresentationSource.CriticalFromVisual(dragSource) is not PortablePresentationSource source || source.RootVisual == null)
                return DragDropEffects.None;

            // Once a drag is under way, NativeInputPump (see Dispatcher.NativeInputPump's doc
            // comment) keeps routing every subsequent native mouse-move through WPF's normal
            // event system while nested inside this same operation's blocking wait - which means
            // any OTHER handler still subscribed to the drag source's PreviewMouseMove (e.g. the
            // very code that called DoDragDrop in the first place, like WpfToolbox's own
            // OnPreviewMouseMove, which has no "already dragging" guard because real OLE's native
            // modal loop would never let a second MouseMove reach it mid-drag) sees that event
            // too and calls DoDragDrop again, recursively, before this operation ever gets to
            // process it itself. Fail the reentrant call closed instead of nesting indefinitely.
            if (s_isRunning)
                return DragDropEffects.None;

            s_isRunning = true;
            try
            {
                return new PortableDragDropOperation(dragElement, source, dataObject, allowedEffects).RunCore();
            }
            finally
            {
                s_isRunning = false;
            }
        }

        private DragDropEffects RunCore()
        {
            if (!Mouse.Capture(_dragSource, CaptureMode.SubTree))
                return DragDropEffects.None;

            var frame = new DispatcherFrame();

            MouseEventHandler onPreviewMouseMove = (sender, e) => OnPointerUpdate(frame, e.GetPosition((IInputElement)_source.RootVisual));
            MouseButtonEventHandler onPreviewMouseButtonUp = (sender, e) => OnPointerUpdate(frame, e.GetPosition((IInputElement)_source.RootVisual));
            KeyEventHandler onPreviewKeyDown = (sender, e) =>
            {
                if (e.Key != Key.Escape)
                    return;
                _action = DragAction.Cancel;
                frame.Continue = false;
            };

            _dragSource.PreviewMouseMove += onPreviewMouseMove;
            _dragSource.PreviewMouseUp += onPreviewMouseButtonUp;
            _dragSource.PreviewKeyDown += onPreviewKeyDown;

            // Safety net: the ONLY thing that normally ends this loop is a PreviewMouseUp/
            // PreviewMouseMove routed event reaching _dragSource while it holds mouse capture. On
            // Windows the OLE modal loop guarantees that delivery. Off Windows, real interactive
            // drags have been observed to leave the button released at the OS level (Mouse.LeftButton
            // already Released) without that routed event ever firing on _dragSource - e.g. capture
            // getting silently redirected, or the up landing on a different element mid-drag - and
            // with no timeout this loop then spins in Dispatcher's NativeInputPump/Thread.Sleep(1)
            // forever, which is exactly an app hang a user has to force-quit to escape. Polling the
            // global button state once per composed frame (~60Hz) closes that gap: catches a missed
            // release within about one frame, while changing nothing when the routed event already
            // fires normally (RaiseQueryContinueDrag's own zero-buttons-down check still drives the
            // actual Drop decision).
            EventHandler onRenderingTick = (_, _) =>
            {
                if (_action != DragAction.Continue || !_hasLastRootPoint)
                    return;
                if (Mouse.LeftButton == MouseButtonState.Released &&
                    Mouse.MiddleButton == MouseButtonState.Released &&
                    Mouse.RightButton == MouseButtonState.Released)
                {
                    OnPointerUpdate(frame, _lastRootPoint);
                }
            };
            CompositionTarget.Rendering += onRenderingTick;

            try
            {
                Dispatcher.PushFrame(frame);
            }
            finally
            {
                CompositionTarget.Rendering -= onRenderingTick;
                _dragSource.PreviewMouseMove -= onPreviewMouseMove;
                _dragSource.PreviewMouseUp -= onPreviewMouseButtonUp;
                _dragSource.PreviewKeyDown -= onPreviewKeyDown;

                if (ReferenceEquals(Mouse.Captured, _dragSource))
                    Mouse.Capture(null);

                if (_currentTarget != null && !_dropped)
                {
                    DragDrop.ProcessPortableDragDrop(
                        _currentTarget, DragDrop.DragLeaveEvent, _dataObject,
                        GetCurrentKeyStates(), _allowedEffects, DragDropEffects.None, default);
                }
            }

            return _dropped ? _lastEffects : DragDropEffects.None;
        }

        private void OnPointerUpdate(DispatcherFrame frame, Point rootPoint)
        {
            if (_action != DragAction.Continue)
                return;

            _lastRootPoint = rootPoint;
            _hasLastRootPoint = true;

            var keyStates = GetCurrentKeyStates();
            var query = new QueryContinueDragEventArgs(escapePressed: false, keyStates);
            RaiseQueryContinueDrag(query);
            _action = query.Action;

            if (_action == DragAction.Cancel)
            {
                frame.Continue = false;
                return;
            }

            // rootPoint is already in RootVisual space (both handlers in RunCore compute it with
            // GetPosition(_source.RootVisual)), so hit-test it directly. MouseDevice.LocalHitTest's
            // (point, source) overload treats its point as CLIENT units and runs PointUtil.ClientToRoot
            // over it first - double-transforming an already-root point, which then resolved the
            // window's chrome Border instead of the element actually under the cursor. Measured at the
            // same point mid-drag: InputHitTest said Canvas (the real AllowDrop target),
            // LocalHitTest said Border, so ResolveDropTarget walked Border -> Window, found nothing
            // AllowDrop, and every portable drag silently completed with no DragOver/Drop at all.
            var hit = (_source.RootVisual as UIElement)?.InputHitTest(rootPoint) as DependencyObject;
            var target = ResolveDropTarget(hit);

            if (!ReferenceEquals(target, _currentTarget))
            {
                if (_currentTarget != null)
                {
                    DragDrop.ProcessPortableDragDrop(
                        _currentTarget, DragDrop.DragLeaveEvent, _dataObject,
                        keyStates, _allowedEffects, DragDropEffects.None, default);
                }

                _currentTarget = target;

                if (target != null)
                {
                    var targetPoint = InputElement.TranslatePoint(rootPoint, _source.RootVisual, target);
                    _lastEffects = DragDrop.ProcessPortableDragDrop(
                        target, DragDrop.DragEnterEvent, _dataObject,
                        keyStates, _allowedEffects, DragDropEffects.None, targetPoint);
                }
                else
                {
                    _lastEffects = DragDropEffects.None;
                }
            }
            else if (target != null)
            {
                var targetPoint = InputElement.TranslatePoint(rootPoint, _source.RootVisual, target);
                _lastEffects = DragDrop.ProcessPortableDragDrop(
                    target, DragDrop.DragOverEvent, _dataObject,
                    keyStates, _allowedEffects, DragDropEffects.None, targetPoint);
            }

            var feedback = new GiveFeedbackEventArgs(_lastEffects, useDefaultCursors: true);
            RaiseGiveFeedback(feedback);

            if (_action == DragAction.Drop)
            {
                if (_currentTarget != null)
                {
                    var targetPoint = InputElement.TranslatePoint(rootPoint, _source.RootVisual, _currentTarget);
                    _lastEffects = DragDrop.ProcessPortableDragDrop(
                        _currentTarget, DragDrop.DropEvent, _dataObject,
                        keyStates, _allowedEffects, DragDropEffects.None, targetPoint);
                }
                else
                {
                    _lastEffects = DragDropEffects.None;
                }

                _dropped = true;
                frame.Continue = false;
            }
        }

        // Mirrors DragDrop.GetCurrentTarget's single-hit check (no ancestor walk) so a portable
        // drag targets exactly what a real Windows/OLE drag would - see OleDropTarget.GetCurrentTarget.
        // Real OLE's GetCurrentTarget checks only the immediate hit, with no ancestor walk - that
        // works on Windows because AllowDrop-enabled elements there are typically reached via a
        // registered-HWND-wide hit test that lands on them directly. Here, the immediate hit is
        // very often an adorner (resize/move handles, which sit on top of everything precisely so
        // they ARE hit first) or the design surface's own root content element, neither of which
        // is AllowDrop - the actual AllowDrop element (DesignPanel's EatAllHitTestRequests overlay,
        // or an ancestor Panel a real click-then-hit-test sequence would normally reach once
        // CreateComponentTool's own DragOver handler flips IsAdornerLayerHitTestVisible) is further
        // up the tree. Walk up to find it instead of giving up on the first miss.
        //
        // Stop at the FIRST (innermost) AllowDrop match, matching real OLE's GetCurrentTarget as
        // closely as this can - an "outermost" walk was tried and made things worse (walked past
        // the real target, e.g. DesignPanel, to something even further out with no Drop
        // subscriber either). Some intermediate elements can still end up AllowDrop=true for
        // reasons unrelated to actually handling Drop (e.g. WpfDesign's PanelMoveAdorner, via a
        // generic shared style) - if that turns out to matter in practice, the fix belongs at the
        // caller/coordinate-choice level (pick a drop point that avoids hitting such an element),
        // not by guessing at tree depth here.
        private static DependencyObject ResolveDropTarget(DependencyObject hit)
        {
            for (DependencyObject current = hit; current != null; current = GetVisualOrLogicalParent(current))
            {
                switch (current)
                {
                    case UIElement { AllowDrop: true } uiElement when IsExplicitlyDropEnabled(uiElement, UIElement.AllowDropProperty):
                        return uiElement;
                    case ContentElement { AllowDrop: true } contentElement when IsExplicitlyDropEnabled(contentElement, ContentElement.AllowDropProperty):
                        return contentElement;
                    case UIElement3D { AllowDrop: true } uiElement3D when IsExplicitlyDropEnabled(uiElement3D, UIElement3D.AllowDropProperty):
                        return uiElement3D;
                }
            }
            return null;
        }

        // AllowDropProperty carries FrameworkPropertyMetadataOptions.Inherits (this matches real
        // WPF, not a portability difference - see FrameworkElement's static constructor). On
        // Windows that is harmless: OLE's RegisterDragDrop is registered once per HWND, and
        // OleDropTarget resolves the specific target through its own hit-testing, never by walking
        // AllowDrop ancestors. This portable reimplementation instead approximates Windows' registered
        // drop target by walking up for an AllowDrop==true ancestor - but with inheritance in play,
        // EVERY descendant under any AllowDrop-enabled root reports AllowDrop==true, so the walk
        // matched literally the first thing hit (an adorner, a ListBoxItem - anything) instead of the
        // real intended container. A dragged toolbox item then silently never landed: DragEnter/
        // DragOver fired on that incidental element, which usually has no Drop subscriber, so nothing
        // happened and no error was ever surfaced.
        //
        // The fix: only accept a value that was actually placed on THIS element (Local, Style,
        // Template, DefaultStyle, ParentTemplate, ...), never one that merely flowed down via
        // Inherited. That is what "AllowDrop declared here" means on Windows too, since inheritance
        // there is inert (nothing consults it).
        private static bool IsExplicitlyDropEnabled(DependencyObject element, DependencyProperty property)
        {
            var source = element.GetValueSource(
                property, null,
                out _, out var isExpression, out var isAnimated, out var isCoerced, out _);
            return source != BaseValueSourceInternal.Inherited || isExpression || isAnimated || isCoerced;
        }

        private static DependencyObject GetVisualOrLogicalParent(DependencyObject current)
        {
            // LogicalTreeHelper lives in PresentationFramework, not reachable from here - a
            // visual-tree-only walk is enough for design-surface hit testing (adorners and
            // DesignPanel's own overlay are all plain Visuals).
            return current is Visual || current is Visual3D ? VisualTreeHelper.GetParent(current) : null;
        }

        private static DragDropKeyStates GetCurrentKeyStates()
        {
            DragDropKeyStates states = 0;

            if (Mouse.LeftButton == MouseButtonState.Pressed)
                states |= DragDropKeyStates.LeftMouseButton;
            if (Mouse.RightButton == MouseButtonState.Pressed)
                states |= DragDropKeyStates.RightMouseButton;
            if (Mouse.MiddleButton == MouseButtonState.Pressed)
                states |= DragDropKeyStates.MiddleMouseButton;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                states |= DragDropKeyStates.ControlKey;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                states |= DragDropKeyStates.ShiftKey;
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                states |= DragDropKeyStates.AltKey;

            return states;
        }

        // Same shape and default fallback as OleDragSource.RaiseQueryContinueDragEvent/
        // OnDefaultQueryContinueDrag, just raised directly on the drag source element instead of
        // through an IOleDropSource COM callback.
        private void RaiseQueryContinueDrag(QueryContinueDragEventArgs args)
        {
            args.RoutedEvent = DragDrop.PreviewQueryContinueDragEvent;
            _dragSource.RaiseEvent(args);

            args.RoutedEvent = DragDrop.QueryContinueDragEvent;
            if (!args.Handled)
                _dragSource.RaiseEvent(args);

            if (args.Handled)
                return;

            int mouseButtonDownCount = 0;
            if ((args.KeyStates & DragDropKeyStates.LeftMouseButton) != 0)
                mouseButtonDownCount++;
            if ((args.KeyStates & DragDropKeyStates.MiddleMouseButton) != 0)
                mouseButtonDownCount++;
            if ((args.KeyStates & DragDropKeyStates.RightMouseButton) != 0)
                mouseButtonDownCount++;

            args.Action = DragAction.Continue;
            if (args.EscapePressed || mouseButtonDownCount >= 2)
                args.Action = DragAction.Cancel;
            else if (mouseButtonDownCount == 0)
                args.Action = DragAction.Drop;
        }

        private void RaiseGiveFeedback(GiveFeedbackEventArgs args)
        {
            args.RoutedEvent = DragDrop.PreviewGiveFeedbackEvent;
            _dragSource.RaiseEvent(args);

            args.RoutedEvent = DragDrop.GiveFeedbackEvent;
            if (!args.Handled)
                _dragSource.RaiseEvent(args);

            if (!args.Handled)
                args.UseDefaultCursors = true;

            // On Windows, DRAGDROP_S_USEDEFAULTCURSORS tells the OLE modal loop to draw its own
            // native drag cursor - no WPF code is involved. There's no OLE here, so the portable
            // path has to draw that feedback itself, or the mouse pointer never visibly changes
            // while a drag is in progress (it just looks like nothing is happening).
            if (args.UseDefaultCursors)
                Mouse.SetCursor(GetDefaultDragCursor(args.Effects));
        }

        private static Cursor GetDefaultDragCursor(DragDropEffects effects)
        {
            if (effects == DragDropEffects.None)
                return Cursors.No;
            if ((effects & DragDropEffects.Copy) != 0)
                return Cursors.Cross;
            if ((effects & DragDropEffects.Move) != 0)
                return Cursors.Hand;
            if ((effects & DragDropEffects.Link) != 0)
                return Cursors.Hand;
            return Cursors.Arrow;
        }
    }
}

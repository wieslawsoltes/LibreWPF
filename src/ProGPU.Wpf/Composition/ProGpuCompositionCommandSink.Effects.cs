using System;
using System.Numerics;
using System.Runtime.ExceptionServices;
using ProGPU.Scene;

namespace System.Windows.Media.ProGPU.Composition;

public sealed partial class ProGpuCompositionCommandSink
{
    private SmallValueStack<OwnedEffectScope> _ownedEffectScopes;

    bool IWpfOwnedShaderEffectCommandSink.PushOwnedShaderEffect(
        OwnedShaderEffectSource source, WpfReplayRect bounds)
    {
        ThrowIfClosed();
        ArgumentNullException.ThrowIfNull(source);
        ShaderEffectSourceCapture capture = source.SourceCapture;
        if (source.SourceTranslation != Vector2.Zero ||
            !SameSourceCoordinate(capture.X, bounds.X) ||
            !SameSourceCoordinate(capture.Y, bounds.Y) ||
            !SameSourceCoordinate(capture.Width, bounds.Width) ||
            !SameSourceCoordinate(capture.Height, bounds.Height))
            return false;

        var recorder = new GpuPictureRecorder();
        var child = recorder.BeginRecording(new global::ProGPU.Scene.Rect(
            (float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height));
        var scope = new OwnedEffectScope(NativeContext, child, recorder, source, _transformStack.Peek());
        bool scopePushed = false;
        bool transformPushed = false;
        try
        {
            _ownedEffectScopes.Push(scope);
            scopePushed = true;
            _transformStack.Push(Matrix4x4.Identity);
            transformPushed = true;
            _pushStack.Push(PushKind.OwnedShaderEffect);
        }
        catch
        {
            if (transformPushed) _transformStack.Pop();
            if (scopePushed) _ownedEffectScopes.Pop();
            // No caller ownership has transferred and the fresh child has no resources.
            child.Clear();
            throw;
        }

        // Original source coordinates remain unrebased in this picture. The
        // producer owns capture translation, target preparation and retirement.
        NativeContext = child;
        return true;
    }

    private void PopOwnedShaderEffect()
    {
        OwnedEffectScope scope = _ownedEffectScopes.Pop();
        NativeContext = scope.Parent;
        _transformStack.Pop();
        GpuPicture? content = null;
        Exception? failure = null;
        try
        {
            content = scope.Recorder.EndRecording();
            // Retains independent source/content leases before releasing the
            // caller references below; parent scopes and ordering stay intact.
            scope.Parent.DrawOwnedShaderEffect(content, scope.Source, scope.ParentTransform);
        }
        catch (Exception error) { failure = error; }
        try { scope.Child.Clear(); }
        catch (Exception error) { RecordEffectCleanupFailure(ref failure, error); }
        try { content?.Dispose(); }
        catch (Exception error) { RecordEffectCleanupFailure(ref failure, error); }
        try { scope.Source.Dispose(); }
        catch (Exception error) { RecordEffectCleanupFailure(ref failure, error); }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void AbortOwnedShaderEffects()
    {
        Exception? failure = null;
        while (_ownedEffectScopes.Count != 0)
        {
            OwnedEffectScope scope = _ownedEffectScopes.Pop();
            NativeContext = scope.Parent;
            try { scope.Child.Clear(); }
            catch (Exception error) { RecordEffectCleanupFailure(ref failure, error); }
            try { scope.Source.Dispose(); }
            catch (Exception error) { RecordEffectCleanupFailure(ref failure, error); }
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static bool SameSourceCoordinate(double left, double right) =>
        BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);

    private static void RecordEffectCleanupFailure(ref Exception? failure, Exception error)
    {
        if (failure == null) failure = error;
        else
        {
            try { failure.Data["OwnedShaderEffectCleanup"] = error; }
            catch { /* Cleanup diagnostics must not replace the original failure. */ }
        }
    }

    private readonly record struct OwnedEffectScope(
        global::ProGPU.Scene.DrawingContext Parent,
        global::ProGPU.Scene.DrawingContext Child,
        GpuPictureRecorder Recorder,
        OwnedShaderEffectSource Source,
        Matrix4x4 ParentTransform);
}

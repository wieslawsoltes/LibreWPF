using System;
using System.Numerics;
using ProGPU.Scene;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal interface IWpfShaderRecordingAdapterSource
{
    WpfShaderRecordingImageSourceAdapter? CreateShaderRecordingAdapter();
    bool RecordsOwnedShaderImages => false;
}

internal sealed class WpfOwnedEffectCandidate : IDisposable
{
    internal OwnedShaderEffectSource? Source { get; set; }
    internal bool Push(IWpfCompositionCommandSink sink, WpfReplayRect bounds)
    {
        if (Source is not { } source || sink is not IWpfOwnedShaderEffectCommandSink target ||
            !target.PushOwnedShaderEffect(source, bounds)) return false;
        Source = null; // The successful scope now owns the original reference.
        return true;
    }
    public void Dispose() { Source?.Dispose(); Source = null; }
}

internal static partial class WpfEffectMapper
{
    internal static bool TryCreateOwnedShaderEffect(object? value, object owner,
        WpfReplayRect bounds, IWpfImageSourceAdapter? imageAdapter, out OwnedShaderEffectSource source)
    {
        source = null!;
        if (value is not IPortableShaderEffectSource typed ||
            !typed.TryGetPortableShaderEffect(out var effect) ||
            imageAdapter is not IWpfShaderRecordingAdapterSource recordingSource) return false;
        var capture = new ShaderEffectSourceCapture(bounds.X, bounds.Y, bounds.Width, bounds.Height,
            effect.PaddingTop, effect.PaddingBottom, effect.PaddingLeft, effect.PaddingRight);
        if (!capture.IsValid || effect.IntConstantCount != 0 || effect.BoolConstantCount != 0 ||
            !TryResolveShaderReplacement(effect, out var replacement)) return false;
        var inputs = (PortableShaderSampler[])effect.Samplers.Clone();
        float[] constants = CopyPortableFloatConstants(effect);
        var recipes = new WpfRecordedShaderSampler?[inputs.Length];
        int count = 0, inputRegister = 0;
        uint registers = 0;
        bool implicitSeen = false;
        var sampling = TextureSamplingMode.Linear;
        // Validate the complete register list before source callbacks or owned
        // sampler allocation, including the default implicit input register.
        for (int i = 0; i < inputs.Length; i++)
        {
            var input = inputs[i];
            if ((uint)input.RegisterIndex >= WpfShaderEffectParams.MaxSamplerRegisterCount ||
                (registers & (1u << input.RegisterIndex)) != 0) return false;
            registers |= 1u << input.RegisterIndex;
            if (input.Kind == PortableShaderSamplerKind.ImplicitInput)
            {
                if (implicitSeen) return false;
                implicitSeen = true; inputRegister = input.RegisterIndex;
                sampling = ConvertSamplingMode(input.SamplingMode);
            }
            else if (input.Kind is not (PortableShaderSamplerKind.ImageSource or PortableShaderSamplerKind.Brush))
                return false;
        }
        for (int i = 0; i < inputs.Length; i++)
            if (inputs[i].Kind != PortableShaderSamplerKind.ImplicitInput && inputs[i].RegisterIndex == inputRegister)
                return false;
        var adapter = recordingSource.CreateShaderRecordingAdapter();
        if (adapter is null) return false;
        OwnedShaderEffectSource? candidate = null;
        try
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                if (input.Kind == PortableShaderSamplerKind.ImplicitInput) continue;
                recipes[count++] = adapter.CaptureSampler(input);
            }
            var preparation = new OwnedPreparation(owner, replacement.ShaderSource, replacement.ShaderKey,
                constants, inputRegister, sampling, recipes, count);
            candidate = new OwnedShaderEffectSource(capture, Vector2.Zero, preparation);
            // Retained pictures own independent image leases. Retire the
            // temporary recording adapter before publishing the candidate.
            adapter.Dispose();
            source = candidate;
            return true;
        }
        catch (Exception failure)
        {
            Exception? preserved = failure;
            if (candidate is not null) WpfShaderRecordingCleanup.Dispose(candidate, ref preserved);
            else for (int i = 0; i < count; i++) WpfShaderRecordingCleanup.Dispose(recipes[i], ref preserved);
            WpfShaderRecordingCleanup.Dispose(adapter, ref preserved);
            throw;
        }
    }

    private sealed class OwnedPreparation(object originalOwner, string shaderSource, string shaderKey,
        float[] constants, int inputRegister, TextureSamplingMode sampling,
        WpfRecordedShaderSampler?[] recipes, int count) : IShaderEffectPreparation
    {
        private bool _disposed;

        public OwnedShaderEffectParameters Prepare(ShaderEffectPreparationContext context)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            GC.KeepAlive(originalOwner);
            var samplers = new WpfShaderEffectSampler[count];
            OwnedShaderEffectParameters? candidate = null;
            Exception? failure = null;
            try
            {
                for (int i = 0; i < count; i++) samplers[i] = recipes[i]!.Prepare(context);
                candidate = new OwnedShaderEffectParameters(new WpfShaderEffectParams
                {
                    ShaderSource = shaderSource, ShaderKey = shaderKey,
                    Constants = constants, SourceTextureRegisterIndex = inputRegister,
                    SamplingMode = sampling, Samplers = samplers
                });
            }
            catch (Exception error) { failure = error; }
            // The producer snapshots independent owned leases. A failed
            // temporary release must retire that candidate too, not leak an
            // unpublished generation or replace the first source failure.
            for (int i = 0; i < samplers.Length; i++)
                WpfShaderRecordingCleanup.Dispose(samplers[i], ref failure);
            if (failure is not null)
            {
                WpfShaderRecordingCleanup.Dispose(candidate, ref failure);
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            return candidate!;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Exception? first = null;
            for (int i = 0; i < count; i++)
            {
                try { recipes[i]?.Dispose(); }
                catch (Exception failure) { first ??= failure; }
            }
            if (first is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();
        }
    }
}

internal static class WpfShaderRecordingCleanup
{
    internal static void Dispose(IDisposable? value, ref Exception? failure)
    {
        try { value?.Dispose(); }
        catch (Exception error)
        {
            if (failure is null) failure = error;
            else try { failure.Data["OwnedShaderRecordingCleanupFailure"] = error; } catch { }
        }
    }
}

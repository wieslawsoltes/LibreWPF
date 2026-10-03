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
        using var adapter = recordingSource.CreateShaderRecordingAdapter();
        if (adapter is null) return false;
        var inputs = effect.Samplers;
        var recipes = new WpfRecordedShaderSampler?[inputs.Length];
        int count = 0, inputRegister = 0;
        uint registers = 0;
        bool implicitSeen = false, transferred = false;
        var sampling = TextureSamplingMode.Linear;
        try
        {
            // Validate the whole register list before any source callbacks or
            // allocations. Every recipe below owns immutable source recordings.
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
            {
                var input = inputs[i];
                if (input.Kind == PortableShaderSamplerKind.ImplicitInput) continue;
                if (input.RegisterIndex == inputRegister) return false;
                recipes[count++] = adapter.CaptureSampler(input);
            }
            var preparation = new OwnedPreparation(owner, replacement.ShaderSource, replacement.ShaderKey,
                CopyPortableFloatConstants(effect), inputRegister, sampling, recipes, count);
            source = new OwnedShaderEffectSource(capture, Vector2.Zero, preparation);
            transferred = true;
            return true;
        }
        finally
        {
            if (!transferred)
                for (int i = 0; i < count; i++) recipes[i]?.Dispose();
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
            try
            {
                for (int i = 0; i < count; i++) samplers[i] = recipes[i]!.Prepare(context);
                return new OwnedShaderEffectParameters(new WpfShaderEffectParams
                {
                    ShaderSource = shaderSource, ShaderKey = shaderKey,
                    Constants = constants, SourceTextureRegisterIndex = inputRegister,
                    SamplingMode = sampling, Samplers = samplers
                });
            }
            finally
            {
                // The producer snapshots independent owned leases before this
                // temporary candidate retires; failure releases every candidate.
                for (int i = 0; i < samplers.Length; i++) samplers[i]?.Dispose();
            }
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

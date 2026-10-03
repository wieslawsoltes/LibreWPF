using System;
using System.Numerics;
using ProGPU.Scene;
using PortableColor = ProGPU.Wpf.Interop.PortableColor;
using PortableBitmapEffectInputSource = ProGPU.Wpf.Interop.IPortableBitmapEffectInputSource;
using PortableBlurKernel = ProGPU.Wpf.Interop.PortableBlurKernel;
using PortableEffect = ProGPU.Wpf.Interop.PortableEffect;
using PortableEffectKind = ProGPU.Wpf.Interop.PortableEffectKind;
using PortableEffectSource = ProGPU.Wpf.Interop.IPortableEffectSource;
using PortablePixelShader = ProGPU.Wpf.Interop.PortablePixelShader;
using PortableShaderEffect = ProGPU.Wpf.Interop.PortableShaderEffect;
using PortableShaderEffectSource = ProGPU.Wpf.Interop.IPortableShaderEffectSource;
using PortableShaderSampler = ProGPU.Wpf.Interop.PortableShaderSampler;
using PortableShaderSamplerKind = ProGPU.Wpf.Interop.PortableShaderSamplerKind;
using PortableShaderSamplingMode = ProGPU.Wpf.Interop.PortableShaderSamplingMode;
using MediaImageSource = System.Windows.Media.ImageSource;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static partial class WpfEffectMapper
{
    public static bool TryCreateProGpuEffect(
        object? effect,
        out global::ProGPU.Scene.EffectBase proGpuEffect,
        IWpfImageSourceAdapter? imageSourceAdapter = null,
        WpfReplayRect? effectBounds = null,
        object? effectOwner = null)
    {
        if (effect != null)
        {
            if (effect is PortableEffectSource effectSource
                && effectSource.TryGetPortableEffect(out var portableEffect)
                && TryCreatePortableEffect(portableEffect, out proGpuEffect))
            {
                return true;
            }

            if (effect is PortableShaderEffectSource shaderEffectSource
                && shaderEffectSource.TryGetPortableShaderEffect(out var portableShaderEffect)
                && TryCreatePortableShaderEffect(portableShaderEffect, imageSourceAdapter, out proGpuEffect, effectBounds, effectOwner))
            {
                return true;
            }

        }

        proGpuEffect = null!;
        return false;
    }

    public static bool TryCreateProGpuPushEffect(
        object? effect,
        object? effectInput,
        out global::ProGPU.Scene.EffectBase proGpuEffect,
        IWpfImageSourceAdapter? imageSourceAdapter = null,
        WpfReplayRect? effectBounds = null,
        object? effectOwner = null)
    {
        proGpuEffect = null!;
        if (effect == null || !IsSupportedBitmapEffectInput(effectInput))
        {
            return false;
        }

        return TryCreateProGpuEffect(effect, out proGpuEffect, imageSourceAdapter, effectBounds, effectOwner);
    }

    private static bool TryCreatePortableEffect(PortableEffect effect, out global::ProGPU.Scene.EffectBase proGpuEffect)
    {
        switch (effect.Kind)
        {
            case PortableEffectKind.Blur:
                global::ProGPU.Scene.BlurKernelType? kernelType =
                    effect.BlurKernel switch
                    {
                        PortableBlurKernel.Gaussian =>
                            global::ProGPU.Scene.BlurKernelType.Gaussian,
                        PortableBlurKernel.Box =>
                            global::ProGPU.Scene.BlurKernelType.Box,
                        _ => null
                    };
                if (kernelType is not { } resolvedKernelType)
                {
                    proGpuEffect = null!;
                    return false;
                }
                proGpuEffect = new global::ProGPU.Scene.BlurEffect(
                    (float)Math.Max(0d, effect.Radius))
                {
                    KernelType = resolvedKernelType
                };
                return true;

            case PortableEffectKind.DropShadow:
                var radians = effect.Direction * Math.PI / 180d;
                var offset = new Vector2(
                    (float)(effect.ShadowDepth * Math.Cos(radians)),
                    (float)(-effect.ShadowDepth * Math.Sin(radians)));
                proGpuEffect = new global::ProGPU.Scene.DropShadowEffect(
                    (float)Math.Max(0d, effect.BlurRadius),
                    offset,
                    ToVectorColor(effect.Color, Math.Clamp(effect.Opacity, 0d, 1d)));
                return true;

            default:
                proGpuEffect = null!;
                return false;
        }
    }

    private static bool TryCreatePortableShaderEffect(
        PortableShaderEffect effect,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out global::ProGPU.Scene.EffectBase proGpuEffect,
        WpfReplayRect? effectBounds = null,
        object? effectOwner = null)
    {
        proGpuEffect = null!;

        // The source DTO preserves the original doubles, including invalid
        // values. Reject before replacement lookup or sampler ownership can
        // allocate a candidate. The actual capture frame is shared Scene policy,
        // not the legacy scalar MaxPadding approximation.
        if (!IsValidShaderPadding(effect.PaddingTop) || !IsValidShaderPadding(effect.PaddingBottom) ||
            !IsValidShaderPadding(effect.PaddingLeft) || !IsValidShaderPadding(effect.PaddingRight))
        {
            return false;
        }

        // A source ShaderEffect has a real receiving owner and local content
        // rectangle. Preserve both before any replacement/sampler callbacks;
        // an absent frame cannot select the legacy symmetric capture policy.
        if (effectOwner is null || effectBounds is not { } bounds)
            return false;
        var sourceCapture = new ShaderEffectSourceCapture(bounds.X, bounds.Y, bounds.Width, bounds.Height,
            effect.PaddingTop, effect.PaddingBottom, effect.PaddingLeft, effect.PaddingRight);
        if (!sourceCapture.IsValid) return false;
        var samplerFrame = WpfShaderEffectSamplerFrame.FromSource(effectOwner, sourceCapture);

        if (!TryResolveShaderReplacement(effect, out var replacement))
        {
            return false;
        }

        if (effect.IntConstantCount > 0 || effect.BoolConstantCount > 0)
        {
            return false;
        }

        if (!TryReadPortableShaderSamplerState(
                effect,
                imageSourceAdapter,
                out var sourceTextureRegisterIndex,
                out var samplingMode,
                out var samplers,
                samplerFrame))
        {
            return false;
        }

        try
        {
            var parameters = new WpfShaderEffectParams
            {
                ShaderSource = replacement.ShaderSource,
                ShaderKey = replacement.ShaderKey,
                Constants = CopyPortableFloatConstants(effect),
                Samplers = samplers,
                SamplingMode = samplingMode,
                SourceTextureRegisterIndex = sourceTextureRegisterIndex
            };

            var nativeEffect = new WpfShaderEffect(parameters)
            {
                SourceCapture = sourceCapture,
                CaptureSourceVisualOpacity = true
            };

            proGpuEffect = nativeEffect;
            return true;
        }
        catch
        {
            DisposeFailedSamplers(samplers);
            throw;
        }
    }

    private static bool IsValidShaderPadding(double value) =>
        value >= 0 && double.IsFinite(value) && float.IsFinite((float)value);

    private static bool TryResolveShaderReplacement(
        PortableShaderEffect effect,
        out WpfShaderEffectReplacement replacement)
    {
        replacement = null!;

        if (TryGetReplacement(effect.EffectTypeFullName, out replacement)
            || TryGetReplacement(effect.EffectTypeName, out replacement))
        {
            return true;
        }

        PortablePixelShader? pixelShader = effect.PixelShader;
        if (pixelShader == null)
        {
            return false;
        }

        if (TryGetReplacement(pixelShader.UriSource, out replacement)
            || TryGetReplacement(pixelShader.AbsoluteUri, out replacement))
        {
            return true;
        }

        if (pixelShader.Bytecode.Length > 0)
        {
            return WpfShaderEffectRegistry.TryGet(
                WpfShaderEffectRegistry.CreatePixelShaderBytecodeKey(pixelShader.Bytecode),
                out replacement);
        }

        return false;
    }

    private static bool TryGetReplacement(
        string? key,
        out WpfShaderEffectReplacement replacement)
    {
        if (!string.IsNullOrWhiteSpace(key)
            && WpfShaderEffectRegistry.TryGet(key, out replacement))
        {
            return true;
        }

        replacement = null!;
        return false;
    }

    private static float[] CopyPortableFloatConstants(PortableShaderEffect effect)
    {
        if (effect.FloatConstants.Length == 0)
        {
            return Array.Empty<float>();
        }

        var length = Math.Min(effect.FloatConstants.Length, WpfShaderEffectParams.ConstantFloatCount);
        var constants = new float[length];
        Array.Copy(effect.FloatConstants, constants, length);
        return constants;
    }

    private static bool TryReadPortableShaderSamplerState(
        PortableShaderEffect effect,
        IWpfImageSourceAdapter? imageSourceAdapter,
        out int sourceTextureRegisterIndex,
        out TextureSamplingMode samplingMode,
        out WpfShaderEffectSampler[] samplers,
        WpfShaderEffectSamplerFrame samplerFrame)
    {
        sourceTextureRegisterIndex = 0;
        samplingMode = TextureSamplingMode.Linear;
        samplers = Array.Empty<WpfShaderEffectSampler>();

        var hasImplicitInput = false;
        var additionalSamplerCount = 0;
        var portableSamplers = effect.Samplers;

        for (var i = 0; i < portableSamplers.Length; i++)
        {
            var portableSampler = portableSamplers[i];
            var registerIndex = portableSampler.RegisterIndex;
            if ((uint)registerIndex >= WpfShaderEffectParams.MaxSamplerRegisterCount)
            {
                return false;
            }

            var samplerSamplingMode = ConvertSamplingMode(portableSampler.SamplingMode);
            if (portableSampler.Kind == PortableShaderSamplerKind.ImplicitInput)
            {
                if (hasImplicitInput)
                {
                    return false;
                }

                sourceTextureRegisterIndex = registerIndex;
                samplingMode = samplerSamplingMode;
                hasImplicitInput = true;
            }
            else if (portableSampler.Kind == PortableShaderSamplerKind.ImageSource)
            {
                additionalSamplerCount++;
            }
            else if (portableSampler.Kind == PortableShaderSamplerKind.Brush
                && portableSampler.Brush != null)
            {
                additionalSamplerCount++;
            }
            else
            {
                return false;
            }
        }

        if (additionalSamplerCount == 0)
        {
            return true;
        }

        var additionalSamplers = new WpfShaderEffectSampler[additionalSamplerCount];
        var additionalSamplerIndex = 0;
        bool published = false;
        try
        {
            for (var i = 0; i < portableSamplers.Length; i++)
            {
                var portableSampler = portableSamplers[i];
                var registerIndex = portableSampler.RegisterIndex;
                if (portableSampler.Kind == PortableShaderSamplerKind.ImplicitInput)
                {
                    continue;
                }

                if (registerIndex == sourceTextureRegisterIndex)
                {
                    return false;
                }

                var samplerSamplingMode = ConvertSamplingMode(portableSampler.SamplingMode);
                if (portableSampler.Kind == PortableShaderSamplerKind.ImageSource && portableSampler.Brush == null)
                {
                    if (!TryCreateImageSourceShaderSampler(
                            portableSampler.ImageSource,
                            imageSourceAdapter,
                            registerIndex,
                            samplerSamplingMode,
                            out additionalSamplers[additionalSamplerIndex]))
                    {
                        return false;
                    }

                    additionalSamplerIndex++;
                }
                else if (portableSampler.Kind == PortableShaderSamplerKind.Brush ||
                    (portableSampler.Kind == PortableShaderSamplerKind.ImageSource && portableSampler.Brush != null))
                {
                    if (!TryCreateShaderSamplerBrush(
                            portableSampler.Brush!,
                            imageSourceAdapter,
                            registerIndex,
                            samplerSamplingMode,
                            out additionalSamplers[additionalSamplerIndex], samplerFrame,
                            requireEffectFrame: portableSampler.Kind == PortableShaderSamplerKind.ImageSource ||
                                (portableSampler.Brush is global::ProGPU.Wpf.Interop.IPortableTileBrushSource tileSource &&
                                 tileSource.TryGetPortableTileBrush(out var tile) &&
                                 tile.Kind == global::ProGPU.Wpf.Interop.PortableTileBrushKind.Visual)))
                    {
                        return false;
                    }

                    additionalSamplerIndex++;
                }
            }

            samplers = additionalSamplers;
            published = true;
            return true;
        }
        finally
        {
            if (!published) DisposeFailedSamplers(additionalSamplers);
        }
    }

    private static void DisposeFailedSamplers(WpfShaderEffectSampler[] samplers)
    {
        foreach (WpfShaderEffectSampler? sampler in samplers)
        {
            // Preserve the original failed source/candidate result. Ordinary
            // texture samplers retain their existing borrowed ownership.
            try { sampler?.Dispose(); }
            catch { }
        }
    }

    private static bool TryCreateImageSourceShaderSampler(
        object? imageSource,
        IWpfImageSourceAdapter? imageSourceAdapter,
        int registerIndex,
        TextureSamplingMode samplingMode,
        out WpfShaderEffectSampler sampler)
    {
        sampler = null!;
        if (ResolveImageSource(imageSource, imageSourceAdapter) is MediaImageSource resolvedImageSource
            && WpfBitmapSourceImageAdapter.TryGetGpuTexture(resolvedImageSource, out var texture))
        {
            sampler = new WpfShaderEffectSampler(registerIndex, texture, samplingMode);
            return true;
        }

        sampler = null!;
        return false;
    }

    private static bool TryCreateShaderSamplerBrush(
        object brush,
        IWpfImageSourceAdapter? imageSourceAdapter,
        int registerIndex,
        TextureSamplingMode samplingMode,
        out WpfShaderEffectSampler sampler,
        WpfShaderEffectSamplerFrame samplerFrame,
        bool requireEffectFrame)
    {
        sampler = null!;
        if (requireEffectFrame)
        {
            return imageSourceAdapter is IWpfShaderEffectSamplerBrushAdapter framedAdapter &&
                framedAdapter.TryAdaptSourceShaderEffectSamplerBrush(brush, registerIndex, samplingMode,
                    samplerFrame, out sampler);
        }
        if (imageSourceAdapter is IWpfShaderEffectSamplerBrushAdapter samplerBrushAdapter
            && samplerBrushAdapter.TryAdaptShaderEffectSamplerBrush(
                brush,
                registerIndex,
                samplingMode,
                out sampler))
        {
            return true;
        }

        sampler = null!;
        return false;
    }

    private static MediaImageSource? ResolveImageSource(
        object? imageSource,
        IWpfImageSourceAdapter? imageSourceAdapter)
    {
        return imageSource is MediaImageSource mediaImageSource
            ? mediaImageSource
            : imageSourceAdapter?.AdaptImageSource(imageSource);
    }

    private static bool IsSupportedBitmapEffectInput(object? effectInput)
    {
        if (effectInput == null)
        {
            return true;
        }

        return effectInput is PortableBitmapEffectInputSource inputSource
            && inputSource.TryGetPortableBitmapEffectInput(out var input)
            && input.UsesContextInput
            && input.HasDefaultAreaToApplyEffect;
    }

    private static Vector4 ToVectorColor(PortableColor color, double opacity)
    {
        return ToVectorColor(color.A, color.R, color.G, color.B, opacity);
    }

    private static Vector4 ToVectorColor(byte a, byte r, byte g, byte b, double opacity)
    {
        return new Vector4(
            r / 255f,
            g / 255f,
            b / 255f,
            (float)((a / 255d) * opacity));
    }

    private static TextureSamplingMode ConvertSamplingMode(PortableShaderSamplingMode samplingMode)
    {
        return samplingMode == PortableShaderSamplingMode.NearestNeighbor
            ? TextureSamplingMode.Nearest
            : TextureSamplingMode.Linear;
    }
}

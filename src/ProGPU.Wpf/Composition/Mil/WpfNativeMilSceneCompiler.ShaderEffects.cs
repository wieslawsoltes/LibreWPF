using System.Numerics;
using System.Runtime.InteropServices;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed partial class WpfNativeMilSceneCompiler
{
    private sealed partial class BuildContext
    {
        private readonly Dictionary<IPortablePixelShaderSource, ShaderSnapshot> _pixelShaderHandles =
            new(ReferenceEqualityComparer.Instance);
        private uint _implicitInputBrushHandle;
        private readonly Dictionary<object, uint> _shaderCacheBrushHandles =
            new(ReferenceEqualityComparer.Instance);

        private readonly record struct ShaderSnapshot(
            uint Handle, NativeMilShaderRenderMode Mode, bool CompileSoftwareShader, byte[] Bytecode);

        private uint AddShaderEffect(object resource, PortableShaderEffect effect)
        {
            if (effect.IntConstantCount != 0 || effect.BoolConstantCount != 0 ||
                effect.PaddingTop != 0 || effect.PaddingBottom != 0 ||
                effect.PaddingLeft != 0 || effect.PaddingRight != 0)
                throw new NotSupportedException("Native source shaders require float constants and zero padding.");

            float[] constants = effect.FloatConstants;
            if ((constants.Length & 3) != 0 || constants.Length > 32 * 4)
                throw new NotSupportedException("Native source shaders require at most 32 complete float registers.");
            if (effect.Samplers.Length != 1)
                throw new NotSupportedException("Native source shaders require exactly one source sampler.");
            PortableShaderSampler sampler = effect.Samplers[0];
            if (sampler.RegisterIndex is < 0 or >= 16)
                throw new NotSupportedException("Native source shader sampler register is unsupported.");
            NativeMilShaderSamplingMode sampling = sampler.SamplingMode switch
            {
                PortableShaderSamplingMode.Auto => NativeMilShaderSamplingMode.Auto,
                PortableShaderSamplingMode.NearestNeighbor => NativeMilShaderSamplingMode.NearestNeighbor,
                PortableShaderSamplingMode.Bilinear => NativeMilShaderSamplingMode.Bilinear,
                _ => throw new NotSupportedException("Unknown source shader sampling mode.")
            };

            uint shader = ResolvePixelShader(effect.PixelShader);
            uint brush;
            switch (sampler.Kind)
            {
                case PortableShaderSamplerKind.ImplicitInput:
                    if (_implicitInputBrushHandle == 0)
                    {
                        _implicitInputBrushHandle = NextHandle();
                        Batch.CreateResource(_implicitInputBrushHandle, NativeMilResourceType.ImplicitInputBrush);
                        Batch.SetImplicitInputBrush(_implicitInputBrushHandle);
                    }
                    brush = _implicitInputBrushHandle;
                    break;
                case PortableShaderSamplerKind.ImageSource:
                    // A bare image cannot preserve ImageBrush mapping, opacity or
                    // transforms. Require the original typed brush and capture it
                    // once, before passing the same descriptor to ordinary MIL.
                    if (sampler.Brush is not IPortableTileBrushSource tileSource ||
                        !tileSource.TryGetPortableTileBrush(out PortableTileBrush tile) ||
                        tile.Kind != PortableTileBrushKind.Image ||
                        !ReferenceEquals(tile.Content, sampler.ImageSource))
                        throw new NotSupportedException("Native source shader image samplers require their original ImageBrush.");
                    brush = ResolveBrush(sampler.Brush, tile);
                    break;
                case PortableShaderSamplerKind.Brush:
                    if (sampler.Brush is IPortableBitmapCacheBrushSource cacheSource)
                    {
                        if (!cacheSource.TryGetPortableBitmapCacheBrush(out var cache))
                            throw MissingContract(nameof(IPortableBitmapCacheBrushSource));
                        if (cache.InternalTarget is { } target)
                        {
                            if (!TryGetVisualBounds(target, out _, allowEmpty: true))
                                throw new NotSupportedException("Native shader BitmapCacheBrush targets require original source bounds.");
                        }
                        // Raw cache-raster policy belongs to the exact shader
                        // resource, not ordinary paint using the same brush.
                        brush = ResolveBrush(sampler.Brush, capturedCache: cache,
                            shaderCacheSource: true);
                        break;
                    }
                    // The source exports its actual VisualBrush through the
                    // existing brush sampler contract. Do not turn it into an
                    // image, or admit other tile-brush families by shape.
                    if (sampler.Brush is not IPortableTileBrushSource visualSource ||
                        !visualSource.TryGetPortableTileBrush(out PortableTileBrush visualTile) ||
                        visualTile.Kind != PortableTileBrushKind.Visual)
                        throw new NotSupportedException("Native source shader brush samplers require their original VisualBrush or BitmapCacheBrush.");
                    brush = ResolveBrush(sampler.Brush, visualTile);
                    break;
                default:
                    throw new NotSupportedException("Native source shaders require implicit input or an original supported source brush.");
            }

            // Source exports dense registers with zero holes. A fresh native
            // descriptor also starts at zero; explicit ordered registers retain
            // those holes and allow later source removal to reset old constants.
            Span<short> registers = stackalloc short[constants.Length / 4];
            for (int index = 0; index < registers.Length; ++index)
                registers[index] = checked((short)index);
            uint handle = NextHandle();
            Batch.CreateResource(handle, NativeMilResourceType.ShaderEffect);
            Batch.SetShaderEffect(handle, shader, registers,
                MemoryMarshal.Cast<float, Vector4>(constants.AsSpan()),
                checked((uint)sampler.RegisterIndex), sampling, brush,
                effect.DdxUvDdyUvRegisterIndex);
            _effectHandles.Add(resource, handle);
            return handle;
        }

        private uint ResolvePixelShader(PortablePixelShader? shader)
        {
            if (shader?.Source is not { } source)
                throw MissingContract(nameof(IPortablePixelShaderSource));
            NativeMilShaderRenderMode mode = shader.RenderMode switch
            {
                PortableShaderRenderMode.Auto => NativeMilShaderRenderMode.Auto,
                PortableShaderRenderMode.HardwareOnly => NativeMilShaderRenderMode.HardwareOnly,
                _ => throw new NotSupportedException("Native source shaders require explicit Auto or HardwareOnly intent.")
            };
            ReadOnlySpan<byte> bytecode = shader.Bytecode;
            if (bytecode.Length < 4 || shader.MajorVersion != bytecode[1] || shader.MinorVersion != bytecode[0])
                throw new NotSupportedException("Source shader version does not match its original bytecode.");
            // This original MIL flag requests software compilation too; it does
            // not authorize SoftwareOnly intent or a CPU renderer fallback.
            bool compileSoftwareShader = !(shader.MajorVersion == 3 && shader.MinorVersion == 0);
            if (_pixelShaderHandles.TryGetValue(source, out ShaderSnapshot snapshot))
            {
                if (snapshot.Mode != mode || snapshot.CompileSoftwareShader != compileSoftwareShader ||
                    !bytecode.SequenceEqual(snapshot.Bytecode))
                    throw new InvalidOperationException("One source PixelShader changed during native batch capture.");
                return snapshot.Handle;
            }
            uint handle = NextHandle();
            Batch.CreateResource(handle, NativeMilResourceType.PixelShader);
            Batch.SetPixelShader(handle, bytecode, mode, compileSoftwareShader);
            _pixelShaderHandles.Add(source, new(handle, mode, compileSoftwareShader, bytecode.ToArray()));
            return handle;
        }
    }
}

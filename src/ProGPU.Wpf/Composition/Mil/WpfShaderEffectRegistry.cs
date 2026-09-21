using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace System.Windows.Media.ProGPU.Composition.Mil;

public sealed class WpfShaderEffectReplacement
{
    public WpfShaderEffectReplacement(string shaderSource, string? shaderKey = null)
    {
        if (string.IsNullOrWhiteSpace(shaderSource))
        {
            throw new ArgumentException("Shader source must not be empty.", nameof(shaderSource));
        }

        ShaderSource = shaderSource;
        ShaderKey = shaderKey ?? string.Empty;
    }

    public string ShaderSource { get; }

    public string ShaderKey { get; }
}

public static class WpfShaderEffectRegistry
{
    private const string PixelShaderBytecodePrefix = "pixel-shader-sha256:";
    private static readonly ConcurrentDictionary<string, WpfShaderEffectReplacement> Replacements = new(StringComparer.Ordinal);

    public static void Register(string key, string shaderSource, string? shaderKey = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Replacement key must not be empty.", nameof(key));
        }

        Replacements[key] = new WpfShaderEffectReplacement(shaderSource, shaderKey);
    }

    public static string RegisterPixelShaderBytecode(byte[] bytecode, string shaderSource, string? shaderKey = null)
    {
        ArgumentNullException.ThrowIfNull(bytecode);

        var key = CreatePixelShaderBytecodeKey(bytecode);
        Register(key, shaderSource, shaderKey);
        return key;
    }

    public static bool Unregister(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return Replacements.TryRemove(key, out _);
    }

    public static void Clear()
    {
        Replacements.Clear();
    }

    public static string CreatePixelShaderBytecodeKey(byte[] bytecode)
    {
        ArgumentNullException.ThrowIfNull(bytecode);

        return PixelShaderBytecodePrefix + Convert.ToHexString(SHA256.HashData(bytecode)).ToLowerInvariant();
    }

    internal static bool TryGet(string key, out WpfShaderEffectReplacement replacement)
    {
        return Replacements.TryGetValue(key, out replacement!);
    }
}

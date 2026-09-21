using System;
using System.Collections.Generic;
using ProGPU.Backend;
using Silk.NET.WebGPU;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal sealed class WpfViewport3DTextureCache : IDisposable
{
    private readonly WgpuContext _context;
    private readonly Dictionary<object, TextureSet> _entries = new(ReferenceEqualityComparer.Instance);
    private ulong _frameId;
    private bool _isDisposed;

    public WpfViewport3DTextureCache(WgpuContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public void BeginFrame()
    {
        ThrowIfDisposed();
        _frameId++;
    }

    public TextureSet GetOrCreate(object key, uint width, uint height)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(key);

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (!_entries.TryGetValue(key, out var textures))
        {
            textures = new TextureSet(_context, width, height);
            _entries.Add(key, textures);
        }
        else
        {
            textures.EnsureSize(width, height);
        }

        textures.LastUsedFrame = _frameId;
        return textures;
    }

    public void EndFrame()
    {
        ThrowIfDisposed();

        object[]? unusedKeys = null;
        int unusedKeyCount = 0;
        try
        {
            var entryEnumerator = _entries.GetEnumerator();
            while (entryEnumerator.MoveNext())
            {
                var entry = entryEnumerator.Current;
                if (entry.Value.LastUsedFrame == _frameId)
                {
                    continue;
                }

                entry.Value.Dispose();
                WpfPooledRemovalBuffer.Add(ref unusedKeys, ref unusedKeyCount, _entries.Count, entry.Key);
            }

            for (int i = 0; i < unusedKeyCount; i++)
            {
                _entries.Remove(unusedKeys![i]);
            }
        }
        finally
        {
            WpfPooledRemovalBuffer.Return(unusedKeys, unusedKeyCount);
        }
    }

    public void Clear()
    {
        ThrowIfDisposed();

        var entryEnumerator = _entries.Values.GetEnumerator();
        while (entryEnumerator.MoveNext())
        {
            var entry = entryEnumerator.Current;
            entry.Dispose();
        }

        _entries.Clear();
    }

    internal void GetMemoryDiagnostics(out int textureSetCount, out ulong textureBytes)
    {
        ThrowIfDisposed();

        textureSetCount = 0;
        textureBytes = 0;
        var entryEnumerator = _entries.Values.GetEnumerator();
        while (entryEnumerator.MoveNext())
        {
            TextureSet textures = entryEnumerator.Current;
            textureSetCount++;
            textureBytes += textures.AllocatedTextureBytes;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        var entryEnumerator = _entries.Values.GetEnumerator();
        while (entryEnumerator.MoveNext())
        {
            var entry = entryEnumerator.Current;
            entry.Dispose();
        }

        _entries.Clear();
        _isDisposed = true;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    internal sealed class TextureSet : IDisposable
    {
        private readonly WgpuContext _context;

        public TextureSet(WgpuContext context, uint width, uint height)
        {
            _context = context;
            ColorTexture = CreateColorTexture(width, height);
            MsaaColorTexture = CreateMsaaColorTexture(width, height);
            DepthTexture = CreateDepthTexture(width, height);
        }

        public GpuTexture ColorTexture { get; private set; }

        public GpuTexture MsaaColorTexture { get; private set; }

        public GpuTexture DepthTexture { get; private set; }

        public ulong LastUsedFrame { get; set; }

        public ulong AllocatedTextureBytes
        {
            get
            {
                ulong pixels = (ulong)ColorTexture.Width * ColorTexture.Height;
                const ulong colorBytesPerPixel = 4;
                const ulong multisampleCount = 4;
                const ulong depthStencilBytesPerPixel = 4;
                return pixels * (
                    colorBytesPerPixel +
                    (colorBytesPerPixel * multisampleCount) +
                    (depthStencilBytesPerPixel * multisampleCount));
            }
        }

        public void EnsureSize(uint width, uint height)
        {
            if (ColorTexture.Width == width && ColorTexture.Height == height)
            {
                return;
            }

            ColorTexture.Dispose();
            MsaaColorTexture.Dispose();
            DepthTexture.Dispose();

            ColorTexture = CreateColorTexture(width, height);
            MsaaColorTexture = CreateMsaaColorTexture(width, height);
            DepthTexture = CreateDepthTexture(width, height);
        }

        public void Dispose()
        {
            ColorTexture.Dispose();
            MsaaColorTexture.Dispose();
            DepthTexture.Dispose();
        }

        private GpuTexture CreateColorTexture(uint width, uint height)
        {
            return new GpuTexture(
                _context,
                width,
                height,
                TextureFormat.Rgba8Unorm,
                TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                "WPF Viewport3D Color Texture");
        }

        private GpuTexture CreateMsaaColorTexture(uint width, uint height)
        {
            return new GpuTexture(
                _context,
                width,
                height,
                TextureFormat.Rgba8Unorm,
                TextureUsage.RenderAttachment,
                "WPF Viewport3D MSAA Color Texture",
                sampleCount: 4u);
        }

        private GpuTexture CreateDepthTexture(uint width, uint height)
        {
            return new GpuTexture(
                _context,
                width,
                height,
                TextureFormat.Depth24PlusStencil8,
                TextureUsage.RenderAttachment,
                "WPF Viewport3D Depth Texture",
                sampleCount: 4u);
        }
    }
}

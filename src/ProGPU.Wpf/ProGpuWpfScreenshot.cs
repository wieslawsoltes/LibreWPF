using System;
using System.IO;
using System.IO.Compression;
using ProGPU.Backend;
using Silk.NET.WebGPU;

namespace System.Windows.Media.ProGPU;

public unsafe static class ProGpuWpfScreenshot
{
    public static byte[]? TryCapturePng(object window)
    {
        if (window == null)
        {
            return null;
        }

        try
        {
            if (!WpfPortableWindowActivation.TryGetActiveHost(window, out ProGpuWpfWindowHost? host) ||
                host?.CompositionTarget is not { } target ||
                target.Context.Surface == null)
            {
                return null;
            }

            var geometry = host.ResolveCurrentRenderSurfaceGeometryForDiagnostics();
            uint pixelWidth = Math.Max(1u, geometry.PixelWidth);
            uint pixelHeight = Math.Max(1u, geometry.PixelHeight);

            using var offscreenTexture = new GpuTexture(
                target.Context,
                pixelWidth,
                pixelHeight,
                target.Context.SwapChainFormat,
                TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                "ProGpuWpfScreenshot Offscreen Target");

            target.Render(
                geometry.LogicalWidth,
                geometry.LogicalHeight,
                pixelWidth,
                pixelHeight,
                (float)geometry.DpiScale,
                offscreenTexture.ViewPtr);

            byte[] pixels = offscreenTexture.ReadPixels();
            ConvertToRgbaInPlace(pixels, target.Context.SwapChainFormat);

            return PngEncoder.Encode(pixels, pixelWidth, pixelHeight);
        }
        catch
        {
            return null;
        }
    }

    private static void ConvertToRgbaInPlace(byte[] pixels, TextureFormat format)
    {
        if (format is not (TextureFormat.Bgra8Unorm or TextureFormat.Bgra8UnormSrgb))
        {
            return;
        }

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        }
    }

    private static class PngEncoder
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        public static byte[] Encode(byte[] rgbaPixels, uint width, uint height)
        {
            using var output = new MemoryStream();
            output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            byte[] ihdr = new byte[13];
            WriteUInt32BigEndian(ihdr, 0, width);
            WriteUInt32BigEndian(ihdr, 4, height);
            ihdr[8] = 8;
            ihdr[9] = 6;
            ihdr[10] = 0;
            ihdr[11] = 0;
            ihdr[12] = 0;
            WriteChunk(output, "IHDR", ihdr);

            byte[] scanlines = new byte[height * (1 + width * 4)];
            int srcIndex = 0;
            int dstIndex = 0;
            for (int y = 0; y < height; y++)
            {
                scanlines[dstIndex++] = 0;
                Array.Copy(rgbaPixels, srcIndex, scanlines, dstIndex, (int)(width * 4));
                srcIndex += (int)(width * 4);
                dstIndex += (int)(width * 4);
            }

            using var idatStream = new MemoryStream();
            idatStream.WriteByte(0x78);
            idatStream.WriteByte(0x9C);
            using (var deflate = new DeflateStream(idatStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(scanlines, 0, scanlines.Length);
            }

            uint adler = CalculateAdler32(scanlines);
            idatStream.WriteByte((byte)((adler >> 24) & 0xFF));
            idatStream.WriteByte((byte)((adler >> 16) & 0xFF));
            idatStream.WriteByte((byte)((adler >> 8) & 0xFF));
            idatStream.WriteByte((byte)(adler & 0xFF));
            WriteChunk(output, "IDAT", idatStream.ToArray());

            WriteChunk(output, "IEND", Array.Empty<byte>());
            return output.ToArray();
        }

        private static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }

        private static void WriteChunk(Stream stream, string type, byte[] data)
        {
            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            uint length = (uint)data.Length;
            stream.WriteByte((byte)((length >> 24) & 0xFF));
            stream.WriteByte((byte)((length >> 16) & 0xFF));
            stream.WriteByte((byte)((length >> 8) & 0xFF));
            stream.WriteByte((byte)(length & 0xFF));
            stream.Write(typeBytes, 0, 4);
            if (data.Length > 0)
            {
                stream.Write(data, 0, data.Length);
            }

            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < 4; i++)
            {
                crc = CrcTable[(crc ^ typeBytes[i]) & 0xFF] ^ (crc >> 8);
            }
            for (int i = 0; i < data.Length; i++)
            {
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            }
            crc ^= 0xFFFFFFFFu;

            stream.WriteByte((byte)((crc >> 24) & 0xFF));
            stream.WriteByte((byte)((crc >> 16) & 0xFF));
            stream.WriteByte((byte)((crc >> 8) & 0xFF));
            stream.WriteByte((byte)(crc & 0xFF));
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
                }
                table[i] = c;
            }
            return table;
        }

        private static uint CalculateAdler32(byte[] data)
        {
            uint s1 = 1;
            uint s2 = 0;
            for (int i = 0; i < data.Length; i++)
            {
                s1 = (s1 + data[i]) % 65521;
                s2 = (s2 + s1) % 65521;
            }
            return (s2 << 16) | s1;
        }
    }
}

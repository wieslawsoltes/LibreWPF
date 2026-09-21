using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal static class WpfPooledRemovalBuffer
{
    public static void Add<T>(ref T[]? buffer, ref int count, int capacity, T item)
    {
        buffer ??= ArrayPool<T>.Shared.Rent(Math.Max(1, capacity));
        if (count >= buffer.Length)
        {
            var larger = ArrayPool<T>.Shared.Rent(buffer.Length * 2);
            buffer.AsSpan(0, count).CopyTo(larger);
            Return(buffer, count);
            buffer = larger;
        }

        buffer[count++] = item;
    }

    public static void Return<T>(T[]? buffer, int count)
    {
        if (buffer == null)
        {
            return;
        }

        if (count > 0 && RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(buffer, 0, count);
        }

        ArrayPool<T>.Shared.Return(buffer);
    }
}

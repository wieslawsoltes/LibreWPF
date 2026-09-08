using System.Buffers.Binary;
using System.Windows.Media.ProGPU.Composition.Mil;
using ProGPU.Wpf.Interop;

internal static class NativeMilTextDecorationSmoke
{
    internal static void RequireUnderline(object text)
    {
        if (text is not IPortableDrawingContentSource source ||
            !source.TryGetPortableDrawingContent(out object? content) ||
            content is not IPortableRenderDataSource renderData ||
            !renderData.TryGetPortableRenderDataSnapshot(out var snapshot))
            throw new InvalidOperationException("Native hyperlink text has no typed drawing content.");

        ReadOnlySpan<byte> bytes = snapshot.RenderData;
        bool rectangle = false, guidelines = false;
        while (!bytes.IsEmpty)
        {
            if (bytes.Length < 8) throw new InvalidOperationException("Truncated hyperlink render record.");
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            var command = (WpfMilCommandId)BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
            if (length < 8 || length > bytes.Length) throw new InvalidOperationException("Invalid hyperlink render record.");
            if (command == WpfMilCommandId.PushGuidelineY2) guidelines = true;
            if (command == WpfMilCommandId.DrawRectangle && length == 48)
            {
                double width = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes[24..]));
                double height = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes[32..]));
                rectangle |= double.IsFinite(width) && width > 0 && double.IsFinite(height) && height > 0;
            }
            bytes = bytes[length..];
        }
        if (!rectangle || !guidelines)
            throw new InvalidOperationException("Native hyperlink text omitted its underline coverage or baseline guidelines.");
    }
}

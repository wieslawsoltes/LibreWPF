using System.Buffers.Binary;
using ProGPU.Backend.Native;

namespace System.Windows.Media.ProGPU.Composition.Mil;

/// <summary>
/// Describes one retained native-MIL channel update.
/// </summary>
public readonly record struct WpfNativeMilSessionUpdate(
    uint TargetHandle,
    bool RecreatedChannel,
    NativeMilBatchMetrics BatchMetrics);

/// <summary>
/// Couples a stateful native-MIL request with its compiled semantic scene.
/// </summary>
public sealed record WpfNativeMilSessionFrame(
    NativeMilSceneBuildRequest Request,
    NativeMilStatefulCompiledScene Scene);

/// <summary>
/// Owns the native MIL channel across WPF frames so dynamic guideline and
/// other protocol-owned state can advance without rebuilding the channel.
/// </summary>
/// <remarks>
/// Updates are fail-closed and intended for one presentation thread. A stable
/// packet topology is updated with only changed resource packets. Structural
/// changes build a complete replacement channel before the active channel is
/// swapped, so a failed replacement cannot corrupt the last compilable scene.
/// </remarks>
public sealed class WpfNativeMilCompilationSession : IDisposable
{
    private const uint CreateResourceCommand = 0x07;
    private const uint VisualCreateCommand = 0x1a;
    private const uint VisualInsertChildAtCommand = 0x26;
    private const uint TargetSetRootCommand = 0x35;

    private readonly WpfNativeMilSceneCompiler _compiler;
    private readonly NativeMilBackend _backend;
    private NativeMilChannel? _channel;
    private WpfNativeMilBatch? _lastBatch;
    private bool _requiresRebuild;
    private int _disposeState;

    public WpfNativeMilCompilationSession(
        NativeMilBackend backend = NativeMilBackend.WgpuNative,
        WpfNativeMilSceneCompiler? compiler = null)
    {
        _backend = backend;
        _compiler = compiler ?? new WpfNativeMilSceneCompiler();
    }

    public NativeMilBackend Backend => _backend;

    public bool IsInitialized => _channel is not null && !_requiresRebuild;

    public bool IsDisposed => Volatile.Read(ref _disposeState) != 0;

    public uint TargetHandle => _lastBatch?.TargetHandle ?? 0;

    public WpfNativeMilSessionUpdate Update(
        object rootVisual,
        uint pixelWidth,
        uint pixelHeight,
        NativeMilColor clearColor = default)
    {
        ThrowIfDisposed();
        WpfNativeMilBatch batch = _compiler.BuildBatch(
            rootVisual, pixelWidth, pixelHeight, clearColor);
        return Update(batch);
    }

    public WpfNativeMilSessionFrame CompileFrame(
        ulong sceneId,
        ulong generation,
        ulong monotonicTimeNanoseconds,
        ulong requestSerial,
        double dpiScaleX = 1.0,
        double dpiScaleY = 1.0,
        NativeMilSceneBuildRequestFlags flags =
            NativeMilSceneBuildRequestFlags.None)
    {
        ThrowIfDisposed();
        NativeMilChannel channel = _channel ?? throw new InvalidOperationException(
            "The native MIL session must be updated before compiling a frame.");
        if (_requiresRebuild || _lastBatch is null)
        {
            throw new InvalidOperationException(
                "The native MIL session requires a successful full update before compiling another frame.");
        }

        var request = new NativeMilSceneBuildRequest(
            _lastBatch.TargetHandle,
            sceneId,
            generation,
            monotonicTimeNanoseconds,
            requestSerial,
            dpiScaleX,
            dpiScaleY,
            flags);
        return new WpfNativeMilSessionFrame(
            request,
            channel.CompileScene(request));
    }

    internal WpfNativeMilSessionUpdate Update(WpfNativeMilBatch batch)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(batch);

        if (_channel is null || _lastBatch is null || _requiresRebuild)
        {
            return ReplaceChannel(batch);
        }

        NativeMilBatchDelta delta = CreateDelta(_lastBatch, batch);
        if (delta.RequiresRebuild)
        {
            return ReplaceChannel(batch);
        }

        NativeMilBatchMetrics metrics = default;
        try
        {
            if (delta.Bytes.Length != 0)
            {
                metrics = _channel.Apply(delta.Bytes);
            }
            WpfNativeMilSceneCompiler.ApplySidebands(_channel, batch);
        }
        catch
        {
            // Batch application is transactional, but sideband bindings are
            // individual typed ABI calls. Rebuild before compiling if any of
            // them fails after a successful packet delta.
            _requiresRebuild = true;
            throw;
        }

        _lastBatch = batch;
        return new WpfNativeMilSessionUpdate(
            batch.TargetHandle, false, metrics);
    }

    internal static NativeMilBatchDelta CreateDelta(
        WpfNativeMilBatch previous,
        WpfNativeMilBatch current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (previous.TargetHandle != current.TargetHandle)
        {
            return NativeMilBatchDelta.Rebuild;
        }

        ReadOnlySpan<byte> oldBytes = previous.Bytes;
        ReadOnlySpan<byte> newBytes = current.Bytes;
        int oldOffset = 0;
        int newOffset = 0;
        int deltaLength = 0;
        while (oldOffset < oldBytes.Length && newOffset < newBytes.Length)
        {
            Packet oldPacket = ReadPacket(oldBytes, oldOffset);
            Packet newPacket = ReadPacket(newBytes, newOffset);
            if (oldPacket.Command != newPacket.Command ||
                oldPacket.Handle != newPacket.Handle)
            {
                return NativeMilBatchDelta.Rebuild;
            }

            bool changed = !oldBytes.Slice(oldOffset, oldPacket.Size)
                .SequenceEqual(newBytes.Slice(newOffset, newPacket.Size));
            if (changed)
            {
                if (IsStructural(newPacket.Command))
                {
                    return NativeMilBatchDelta.Rebuild;
                }
                deltaLength = checked(deltaLength + newPacket.Size);
            }
            oldOffset += oldPacket.Size;
            newOffset += newPacket.Size;
        }
        if (oldOffset != oldBytes.Length || newOffset != newBytes.Length)
        {
            return NativeMilBatchDelta.Rebuild;
        }
        if (deltaLength == 0)
        {
            return new NativeMilBatchDelta([], false);
        }

        byte[] delta = GC.AllocateUninitializedArray<byte>(deltaLength);
        oldOffset = 0;
        newOffset = 0;
        int deltaOffset = 0;
        while (newOffset < newBytes.Length)
        {
            Packet oldPacket = ReadPacket(oldBytes, oldOffset);
            Packet newPacket = ReadPacket(newBytes, newOffset);
            ReadOnlySpan<byte> oldPacketBytes =
                oldBytes.Slice(oldOffset, oldPacket.Size);
            ReadOnlySpan<byte> newPacketBytes =
                newBytes.Slice(newOffset, newPacket.Size);
            if (!oldPacketBytes.SequenceEqual(newPacketBytes))
            {
                newPacketBytes.CopyTo(delta.AsSpan(deltaOffset));
                deltaOffset += newPacket.Size;
            }
            oldOffset += oldPacket.Size;
            newOffset += newPacket.Size;
        }
        return new NativeMilBatchDelta(delta, false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }
        _channel?.Dispose();
        _channel = null;
        _lastBatch = null;
    }

    private WpfNativeMilSessionUpdate ReplaceChannel(
        WpfNativeMilBatch batch)
    {
        var replacement = new NativeMilChannel(_backend);
        NativeMilBatchMetrics metrics;
        try
        {
            metrics = WpfNativeMilSceneCompiler.ApplyBatch(
                replacement, batch);
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        NativeMilChannel? previous = _channel;
        _channel = replacement;
        _lastBatch = batch;
        _requiresRebuild = false;
        previous?.Dispose();
        return new WpfNativeMilSessionUpdate(
            batch.TargetHandle, true, metrics);
    }

    private static Packet ReadPacket(ReadOnlySpan<byte> bytes, int offset)
    {
        if ((uint)offset > (uint)bytes.Length || bytes.Length - offset < 12)
        {
            throw new InvalidDataException(
                "The native MIL batch contains a truncated packet header.");
        }
        uint sizeValue = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset, 4));
        if (sizeValue < 12 || sizeValue > int.MaxValue ||
            (sizeValue & 3) != 0)
        {
            throw new InvalidDataException(
                "The native MIL batch contains an invalid packet size.");
        }
        int size = (int)sizeValue;
        if (size > bytes.Length - offset)
        {
            throw new InvalidDataException(
                "The native MIL batch contains a truncated packet payload.");
        }
        uint command = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset + 4, 4));
        uint handle = BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset + 8, 4));
        return new Packet(size, command, handle);
    }

    private static bool IsStructural(uint command) =>
        command is CreateResourceCommand or
            VisualCreateCommand or
            VisualInsertChildAtCommand or
            TargetSetRootCommand;

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }

    private readonly record struct Packet(
        int Size,
        uint Command,
        uint Handle);
}

internal readonly record struct NativeMilBatchDelta(
    byte[] Bytes,
    bool RequiresRebuild)
{
    internal static NativeMilBatchDelta Rebuild { get; } = new([], true);
}

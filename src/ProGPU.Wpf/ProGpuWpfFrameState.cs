using System;

namespace System.Windows.Media.ProGPU;

public readonly struct ProGpuWpfFrameState : IEquatable<ProGpuWpfFrameState>
{
    public ProGpuWpfFrameState(
        uint pixelWidth,
        uint pixelHeight,
        long sceneChangeVersion,
        long retainedWpfChangeVersion,
        long flatDrawingChangeVersion,
        int retainedBranchInvalidationCount = 0,
        int retainedBranchDirtySourceCount = 0,
        int retainedBranchMappedSourceCount = 0,
        int retainedBranchUnmappedSourceCount = 0,
        int retainedBranchSharedWithCleanSourceVisualCount = 0,
        int retainedBranchReplayTargetConflictCount = 0,
        bool retainedBranchInvalidationUsedFallback = false,
        uint logicalWidth = 0,
        uint logicalHeight = 0,
        double dpiScale = 0.0)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        LogicalWidth = logicalWidth;
        LogicalHeight = logicalHeight;
        DpiScale = double.IsFinite(dpiScale) ? dpiScale : 0.0;
        SceneChangeVersion = sceneChangeVersion;
        RetainedWpfChangeVersion = retainedWpfChangeVersion;
        FlatDrawingChangeVersion = flatDrawingChangeVersion;
        RetainedBranchInvalidationCount = retainedBranchInvalidationCount;
        RetainedBranchDirtySourceCount = retainedBranchDirtySourceCount;
        RetainedBranchMappedSourceCount = retainedBranchMappedSourceCount;
        RetainedBranchUnmappedSourceCount = retainedBranchUnmappedSourceCount;
        RetainedBranchSharedWithCleanSourceVisualCount = retainedBranchSharedWithCleanSourceVisualCount;
        RetainedBranchReplayTargetConflictCount = retainedBranchReplayTargetConflictCount;
        RetainedBranchInvalidationUsedFallback = retainedBranchInvalidationUsedFallback;
    }

    public uint PixelWidth { get; }

    public uint PixelHeight { get; }

    public uint LogicalWidth { get; }

    public uint LogicalHeight { get; }

    public double DpiScale { get; }

    public long SceneChangeVersion { get; }

    public long RetainedWpfChangeVersion { get; }

    public long FlatDrawingChangeVersion { get; }

    public int RetainedBranchInvalidationCount { get; }

    public int RetainedBranchDirtySourceCount { get; }

    public int RetainedBranchMappedSourceCount { get; }

    public int RetainedBranchUnmappedSourceCount { get; }

    public int RetainedBranchSharedWithCleanSourceVisualCount { get; }

    public int RetainedBranchReplayTargetConflictCount { get; }

    public bool RetainedBranchInvalidationUsedFallback { get; }

    public bool Equals(ProGpuWpfFrameState other)
    {
        return PixelWidth == other.PixelWidth &&
               PixelHeight == other.PixelHeight &&
               LogicalWidth == other.LogicalWidth &&
               LogicalHeight == other.LogicalHeight &&
               DpiScale.Equals(other.DpiScale) &&
               SceneChangeVersion == other.SceneChangeVersion &&
               RetainedWpfChangeVersion == other.RetainedWpfChangeVersion &&
               FlatDrawingChangeVersion == other.FlatDrawingChangeVersion &&
               RetainedBranchInvalidationCount == other.RetainedBranchInvalidationCount &&
               RetainedBranchDirtySourceCount == other.RetainedBranchDirtySourceCount &&
               RetainedBranchMappedSourceCount == other.RetainedBranchMappedSourceCount &&
               RetainedBranchUnmappedSourceCount == other.RetainedBranchUnmappedSourceCount &&
               RetainedBranchSharedWithCleanSourceVisualCount == other.RetainedBranchSharedWithCleanSourceVisualCount &&
               RetainedBranchReplayTargetConflictCount == other.RetainedBranchReplayTargetConflictCount &&
               RetainedBranchInvalidationUsedFallback == other.RetainedBranchInvalidationUsedFallback;
    }

    public override bool Equals(object? obj)
    {
        return obj is ProGpuWpfFrameState other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(PixelWidth);
        hashCode.Add(PixelHeight);
        hashCode.Add(LogicalWidth);
        hashCode.Add(LogicalHeight);
        hashCode.Add(DpiScale);
        hashCode.Add(SceneChangeVersion);
        hashCode.Add(RetainedWpfChangeVersion);
        hashCode.Add(FlatDrawingChangeVersion);
        hashCode.Add(RetainedBranchInvalidationCount);
        hashCode.Add(RetainedBranchDirtySourceCount);
        hashCode.Add(RetainedBranchMappedSourceCount);
        hashCode.Add(RetainedBranchUnmappedSourceCount);
        hashCode.Add(RetainedBranchSharedWithCleanSourceVisualCount);
        hashCode.Add(RetainedBranchReplayTargetConflictCount);
        hashCode.Add(RetainedBranchInvalidationUsedFallback);
        return hashCode.ToHashCode();
    }

    public static bool operator ==(ProGpuWpfFrameState left, ProGpuWpfFrameState right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ProGpuWpfFrameState left, ProGpuWpfFrameState right)
    {
        return !left.Equals(right);
    }
}

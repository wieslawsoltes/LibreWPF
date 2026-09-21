namespace System.Windows.Media.ProGPU.Composition.Mil;

public readonly record struct WpfMilDecodeResult(
    int RecordCount,
    int AppliedCount,
    int SkippedCount,
    int UnsupportedCount);

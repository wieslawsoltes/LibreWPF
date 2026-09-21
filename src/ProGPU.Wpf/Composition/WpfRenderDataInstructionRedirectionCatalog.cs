using System.Collections.Generic;

namespace System.Windows.Media.ProGPU.Composition;

public enum WpfRenderDataInstructionRedirectionKind
{
    Direct,
    DirectWithAnimationFallback,
    TypedDrawingReplay,
    UnsupportedDraw,
    UnsupportedScope,
}

public readonly record struct WpfRenderDataInstructionRedirection(
    string Name,
    WpfRenderDataInstructionRedirectionKind Kind,
    bool HasAdvancedOverload,
    bool HasGeneratedNoOpCheck,
    bool IsScope,
    bool IsGeneratedInternal,
    bool PreservesNullResourceScope);

public static class WpfRenderDataInstructionRedirectionCatalog
{
    private static readonly WpfRenderDataInstructionRedirection[] s_instructions =
    {
        new("DrawLine", WpfRenderDataInstructionRedirectionKind.DirectWithAnimationFallback, true, true, false, false, false),
        new("DrawRectangle", WpfRenderDataInstructionRedirectionKind.DirectWithAnimationFallback, true, true, false, false, false),
        new("DrawRoundedRectangle", WpfRenderDataInstructionRedirectionKind.DirectWithAnimationFallback, true, true, false, false, false),
        new("DrawEllipse", WpfRenderDataInstructionRedirectionKind.DirectWithAnimationFallback, true, true, false, false, false),
        new("DrawGeometry", WpfRenderDataInstructionRedirectionKind.Direct, false, true, false, false, false),
        new("DrawImage", WpfRenderDataInstructionRedirectionKind.DirectWithAnimationFallback, true, true, false, false, false),
        new("DrawGlyphRun", WpfRenderDataInstructionRedirectionKind.Direct, false, true, false, false, false),
        new("DrawDrawing", WpfRenderDataInstructionRedirectionKind.TypedDrawingReplay, false, true, false, false, false),
        new("DrawVideo", WpfRenderDataInstructionRedirectionKind.UnsupportedDraw, true, true, false, false, false),
        new("PushClip", WpfRenderDataInstructionRedirectionKind.Direct, false, false, true, false, true),
        new("PushOpacityMask", WpfRenderDataInstructionRedirectionKind.Direct, false, false, true, false, true),
        new("PushOpacity", WpfRenderDataInstructionRedirectionKind.DirectWithAnimationFallback, true, false, true, false, false),
        new("PushTransform", WpfRenderDataInstructionRedirectionKind.Direct, false, false, true, false, true),
        new("PushGuidelineSet", WpfRenderDataInstructionRedirectionKind.Direct, false, false, true, false, false),
        new("PushGuidelineY1", WpfRenderDataInstructionRedirectionKind.Direct, false, false, true, true, false),
        new("PushGuidelineY2", WpfRenderDataInstructionRedirectionKind.Direct, false, false, true, true, false),
        new("PushEffect", WpfRenderDataInstructionRedirectionKind.UnsupportedScope, false, false, true, false, false),
        new("Pop", WpfRenderDataInstructionRedirectionKind.Direct, false, false, true, false, false),
    };

    public static IReadOnlyList<WpfRenderDataInstructionRedirection> Instructions => s_instructions;
}

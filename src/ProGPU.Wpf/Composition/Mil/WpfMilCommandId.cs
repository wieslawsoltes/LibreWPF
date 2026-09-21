namespace System.Windows.Media.ProGPU.Composition.Mil;

public enum WpfMilCommandId : int
{
    Invalid = 0x00,

    DrawLine = 0x3e,
    DrawLineAnimate = 0x3f,
    DrawRectangle = 0x40,
    DrawRectangleAnimate = 0x41,
    DrawRoundedRectangle = 0x42,
    DrawRoundedRectangleAnimate = 0x43,
    DrawEllipse = 0x44,
    DrawEllipseAnimate = 0x45,
    DrawGeometry = 0x46,
    DrawImage = 0x47,
    DrawImageAnimate = 0x48,
    DrawGlyphRun = 0x49,
    DrawDrawing = 0x4a,
    DrawVideo = 0x4b,
    DrawVideoAnimate = 0x4c,
    PushClip = 0x4d,
    PushOpacityMask = 0x4e,
    PushOpacity = 0x4f,
    PushOpacityAnimate = 0x50,
    PushTransform = 0x51,
    PushGuidelineSet = 0x52,
    PushGuidelineY1 = 0x53,
    PushGuidelineY2 = 0x54,
    PushEffect = 0x55,
    Pop = 0x56
}

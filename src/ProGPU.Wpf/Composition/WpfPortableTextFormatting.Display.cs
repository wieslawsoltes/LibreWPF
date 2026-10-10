using System.Runtime.ExceptionServices;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

internal sealed partial class WpfPortableTextFormatting
{
    // Explicit qualification seam, deliberately NOT IPortableDisplayTextFormatting.
    // Capture/interpreter and all numeric policies belong to the caller. The
    // producer verifies that capture against original em/DPI; source never infers it.
    internal IPortableDisplayTextParagraph FormatSourceDisplay(in PortableTextParagraphRequest request,
        ReadOnlySpan<PortableDisplayTextStyle> sourceStyles, in PortableDisplayTextOptions options,
        ReadOnlySpan<PortableTextHintingStyle> deviceStyles, NativeSourceEmPolicy emPolicy,
        NativeSourceAdvancePolicy advancePolicy, NativeSourceOffsetPolicy offsetPolicy,
        PortableHintedTextProjection projection, PortableHintedTextCoverage coverage)
    {
        ValidateSourceDisplayRequest(in request, sourceStyles, in options, deviceStyles, emPolicy, advancePolicy, offsetPolicy);
        var nativeProjection = projection switch
        {
            PortableHintedTextProjection.Automatic => NativeHintedProjectionPolicy.Automatic,
            PortableHintedTextProjection.NativeCompute => NativeHintedProjectionPolicy.NativeCompute,
            PortableHintedTextProjection.GpuShader => NativeHintedProjectionPolicy.GpuShader,
            PortableHintedTextProjection.IntrinsicSimd => NativeHintedProjectionPolicy.IntrinsicSimd,
            PortableHintedTextProjection.ScalarReference => NativeHintedProjectionPolicy.ScalarReference,
            _ => throw new ArgumentOutOfRangeException(nameof(projection))
        };
        var nativeCoverage = coverage switch
        {
            PortableHintedTextCoverage.Strict => NativeHintedCoverage.Strict,
            PortableHintedTextCoverage.NonzeroVector => NativeHintedCoverage.NonzeroVector,
            PortableHintedTextCoverage.AntialiasedVector => NativeHintedCoverage.AntialiasedVector,
            _ => throw new ArgumentOutOfRangeException(nameof(coverage))
        };
        string text = request.Text.ToString();
        var captured = request with { Text = text.AsMemory() };
        var metrics = new PortableTextStyleMetrics[sourceStyles.Length];
        var originals = new NativeHintedSourceStyle[sourceStyles.Length];
        for (int i = 0; i < originals.Length; i++)
        {
            var style = sourceStyles[i];
            // These are compatibility shadows only. Original double metrics
            // enter the retained producer separately and remain authoritative.
            metrics[i] = new((float)style.Ascent, (float)style.Descent);
            originals[i] = new() { EmSize = style.EmSize, Ascent = style.Ascent, Descent = style.Descent };
        }
        var sourceOptions = NativeHintedSourceOptions.Create(options.EmSize, options.PixelsPerDip,
            options.MaximumWidth, options.LineHeight, options.TabOrigin, emPolicy, advancePolicy,
            request.Wrapping == PortableTextWrapping.Emergency, request.MeasureIntrinsicWidths, offsetPolicy);
        NativeHintedSourceParagraph? paragraph = null;
        NativeTextShapingContext? context = null;
        Exception? failure = null;
        try
        {
            context = new NativeTextShapingContext(request.Font.Data.Span, request.Font.FaceIndex);
            var prepared = PrepareHintedStyles(context, in captured, metrics, deviceStyles, (float)(1.0 / options.PixelsPerDip));
            var layout = HintedLayoutOptions(in captured);
            paragraph = context.LayoutHintedSourceParagraph(text.AsSpan(),
                request.RightToLeft ? NativeTextDirection.RightToLeft : NativeTextDirection.LeftToRight,
                in layout, prepared.Styles, prepared.Metrics, prepared.Devices, in sourceOptions, originals, prepared.Features);
        }
        catch (Exception error) { failure = error; }
        // Original generation owns its font contexts independently. Retire the
        // mutable registration context before publishing any source reference.
        try { context?.Dispose(); }
        catch (Exception cleanup) { RecordHintedCleanupFailure(ref failure, cleanup, "DisplayContext"); }
        if (failure is null)
        {
            try { return WpfSourceDisplayTextParagraph.Adopt(paragraph!, text, nativeProjection, nativeCoverage); }
            catch (Exception error) { failure = error; }
        }
        try { paragraph?.Dispose(); }
        catch (Exception cleanup) { RecordHintedCleanupFailure(ref failure, cleanup, "DisplayParagraph"); }
        ExceptionDispatchInfo.Capture(failure!).Throw();
        throw new InvalidOperationException("Unreachable source formatting failure.");
    }

    internal static void ValidateSourceDisplayRequest(in PortableTextParagraphRequest request,
        ReadOnlySpan<PortableDisplayTextStyle> sourceStyles, in PortableDisplayTextOptions options,
        ReadOnlySpan<PortableTextHintingStyle> devices, NativeSourceEmPolicy emPolicy,
        NativeSourceAdvancePolicy advancePolicy, NativeSourceOffsetPolicy offsetPolicy)
    {
        ValidateHintedFont(request.Font, request.FontSize);
        if (request.Text.IsEmpty || request.Styles.IsEmpty || sourceStyles.Length != request.Styles.Length || devices.Length != sourceStyles.Length)
            throw new ArgumentException("Source Display requires complete original text and matching explicit styles, metrics and capture styles.");
        if (request.IncrementalTab != 0 || request.Text.Span.Contains('\t') || request.Text.Span.Contains('\ufffc') ||
            request.Alignment == PortableTextAlignment.Justify || options.MaximumWidth == 0)
            throw new NotSupportedException("This explicit source family has no tab, object, justification or zero-width contract.");
        if (request.Alignment is not (PortableTextAlignment.Left or PortableTextAlignment.Center or PortableTextAlignment.Right) ||
            request.Wrapping is not (PortableTextWrapping.Emergency or PortableTextWrapping.WholeWord) ||
            (uint)emPolicy > (uint)NativeSourceEmPolicy.FloatCaptureNearestHalfUp ||
            (uint)advancePolicy > (uint)NativeSourceAdvancePolicy.SourceIdealUnits ||
            (uint)offsetPolicy > (uint)NativeSourceOffsetPolicy.SourceIdealUnits)
            throw new ArgumentOutOfRangeException(nameof(request));
        if (!double.IsFinite(options.EmSize) || options.EmSize <= 0 || (double)request.FontSize != options.EmSize ||
            !double.IsFinite(options.PixelsPerDip) || options.PixelsPerDip <= 0 || (double)(float)options.PixelsPerDip != options.PixelsPerDip ||
            !float.IsFinite((float)(1.0 / options.PixelsPerDip)) || (float)(1.0 / options.PixelsPerDip) <= 0 ||
            !double.IsFinite(options.MaximumWidth) || options.MaximumWidth <= 0 || !float.IsFinite((float)options.MaximumWidth) ||
            request.MaximumWidth != (float)options.MaximumWidth ||
            !double.IsFinite(options.LineHeight) || options.LineHeight < 0 || !float.IsFinite((float)options.LineHeight) ||
            request.LineHeight != (float)options.LineHeight ||
            !double.IsFinite(options.TabOrigin) || !float.IsFinite((float)options.TabOrigin) || request.TabOrigin != (float)options.TabOrigin)
            throw new NotSupportedException("Original source identity must match compatibility shadows and the exact raster em/DPI boundary.");
        int end = 0;
        for (int i = 0; i < sourceStyles.Length; i++)
        {
            var style = request.Styles.Span[i]; var original = sourceStyles[i]; var device = devices[i];
            ValidateHintedFont(style.Font, style.FontSize);
            if (style.Start != end || style.Length <= 0 || style.Length > request.Text.Length - end)
                throw new ArgumentException("Source styles must partition the complete original UTF-16 paragraph.");
            end = checked(end + style.Length);
            if (end < request.Text.Length && char.IsHighSurrogate(request.Text.Span[end - 1]) && char.IsLowSurrogate(request.Text.Span[end]))
                throw new ArgumentException("A source style cannot split a UTF-16 scalar.");
            if (!double.IsFinite(original.EmSize) || original.EmSize <= 0 || (double)style.FontSize != original.EmSize ||
                !double.IsFinite(original.Ascent) || original.Ascent < 0 || !float.IsFinite((float)original.Ascent) ||
                !double.IsFinite(original.Descent) || original.Descent < 0 || !float.IsFinite((float)original.Descent))
                throw new NotSupportedException("Source styles retain original finite metrics and exact raster em identity.");
            if (device.VariationStart != 0 || device.VariationCount != 0)
                throw new NotSupportedException("Source nominal binding requires the original default font instance.");
            if (device.Interpreter is not (PortableTextHintInterpreter.TrueType35 or PortableTextHintInterpreter.TrueType40) ||
                device.XPixelsPerEm266 == 0 || device.YPixelsPerEm266 == 0 || device.XPhase266 >= 64 || device.YPhase266 >= 64)
                throw new ArgumentOutOfRangeException(nameof(devices));
        }
        if (end != request.Text.Length) throw new ArgumentException("Source styles must cover the complete original paragraph.");
    }
}

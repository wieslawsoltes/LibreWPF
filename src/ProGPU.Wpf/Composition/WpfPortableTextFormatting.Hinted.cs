using System.Runtime.ExceptionServices;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;

namespace System.Windows.Media.ProGPU.Composition;

internal sealed partial class WpfPortableTextFormatting
{
    public IPortableHintedTextParagraph FormatHinted(in PortableTextParagraphRequest request,
        ReadOnlySpan<PortableTextStyleMetrics> metrics,
        ReadOnlySpan<PortableTextHintingStyle> deviceStyles,
        in PortableHintedTextOptions options, ReadOnlySpan<int> variationCoordinates16_16 = default,
        ReadOnlySpan<short> normalizedCoordinates = default)
        => FormatHintedCore(in request, metrics, deviceStyles, in options, variationCoordinates16_16, normalizedCoordinates, false);

    public IPortableHintedTextParagraph FormatHintedWithNominalMetrics(in PortableTextParagraphRequest request,
        ReadOnlySpan<PortableTextStyleMetrics> metrics, ReadOnlySpan<PortableTextHintingStyle> deviceStyles,
        in PortableHintedTextOptions options)
        => FormatHintedCore(in request, metrics, deviceStyles, in options, default, default, true);

    private static IPortableHintedTextParagraph FormatHintedCore(in PortableTextParagraphRequest request,
        ReadOnlySpan<PortableTextStyleMetrics> metrics, ReadOnlySpan<PortableTextHintingStyle> deviceStyles,
        in PortableHintedTextOptions options, ReadOnlySpan<int> variationCoordinates16_16,
        ReadOnlySpan<short> normalizedCoordinates, bool nominalMetrics)
    {
        // Complete temporary producer retirement before publishing a source
        // reference. A teardown fault cannot strand an internally returned view.
        string text = request.Text.ToString();
        var captured = request with { Text = text.AsMemory() };
        NativeHintedGlyphResource? resource = null;
        try
        {
            PrepareHintedResource(in captured, metrics, deviceStyles, in options,
                variationCoordinates16_16, normalizedCoordinates, nominalMetrics, out resource);
            return WpfHintedTextParagraph.Adopt(resource!, text);
        }
        catch (Exception error)
        {
            try { resource?.Dispose(); }
            catch (Exception cleanup)
            {
                try { error.Data["HintedSourceCleanupFailure"] = cleanup; }
                catch { /* Diagnostic attachment cannot replace the original error. */ }
            }
            throw;
        }
    }

    private static void PrepareHintedResource(in PortableTextParagraphRequest request,
        ReadOnlySpan<PortableTextStyleMetrics> metrics,
        ReadOnlySpan<PortableTextHintingStyle> deviceStyles,
        in PortableHintedTextOptions options, ReadOnlySpan<int> variationCoordinates16_16,
        ReadOnlySpan<short> normalizedCoordinates, bool nominalMetrics, out NativeHintedGlyphResource? resource)
    {
        resource = null;
        ValidateHintedRequest(in request, metrics.Length, deviceStyles.Length, in options);
        if (nominalMetrics)
            foreach (var style in deviceStyles)
                if (style.VariationCount != 0)
                    throw new NotSupportedException("Source nominal design metrics require an original default font instance.");
        var projection = options.Projection switch
        {
            PortableHintedTextProjection.Automatic => NativeHintedProjectionPolicy.Automatic,
            PortableHintedTextProjection.NativeCompute => NativeHintedProjectionPolicy.NativeCompute,
            PortableHintedTextProjection.GpuShader => NativeHintedProjectionPolicy.GpuShader,
            PortableHintedTextProjection.IntrinsicSimd => NativeHintedProjectionPolicy.IntrinsicSimd,
            PortableHintedTextProjection.ScalarReference => NativeHintedProjectionPolicy.ScalarReference,
            _ => throw new ArgumentOutOfRangeException(nameof(options))
        };
        var coverage = options.Coverage switch
        {
            PortableHintedTextCoverage.Strict => NativeHintedCoverage.Strict,
            PortableHintedTextCoverage.NonzeroVector => NativeHintedCoverage.NonzeroVector,
            PortableHintedTextCoverage.AntialiasedVector => NativeHintedCoverage.AntialiasedVector,
            _ => throw new ArgumentOutOfRangeException(nameof(options))
        };
        var context = new NativeTextShapingContext(request.Font.Data.Span, request.Font.FaceIndex);
        NativeHintedParagraph? paragraph = null;
        Exception? failure = null;
        try
        {
            paragraph = LayoutHintedSource(context, in request, metrics, deviceStyles, in options,
                variationCoordinates16_16, normalizedCoordinates);
            resource = nominalMetrics
                ? paragraph.PrepareGlyphResourceWithNominalMetrics(options.DpiScale, projection, coverage)
                : paragraph.PrepareGlyphResource(options.DpiScale, projection, coverage);
        }
        catch (Exception error) { failure = error; }
        try { paragraph?.Dispose(); }
        catch (Exception error) { RecordHintedCleanupFailure(ref failure, error, "Paragraph"); }
        try { context.Dispose(); }
        catch (Exception error) { RecordHintedCleanupFailure(ref failure, error, "Context"); }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RecordHintedCleanupFailure(ref Exception? failure, Exception cleanup, string owner)
    {
        if (failure is null) failure = cleanup;
        else
        {
            try { failure.Data["HintedProducerCleanupFailure:" + owner] = cleanup; }
            catch { /* Preserve the original formatting failure. */ }
        }
    }

    private static NativeHintedParagraph LayoutHintedSource(NativeTextShapingContext context,
        in PortableTextParagraphRequest request, ReadOnlySpan<PortableTextStyleMetrics> metrics,
        ReadOnlySpan<PortableTextHintingStyle> deviceStyles, in PortableHintedTextOptions options,
        ReadOnlySpan<int> variationCoordinates16_16, ReadOnlySpan<short> normalizedCoordinates)
    {
        var prepared = PrepareHintedStyles(context, in request, metrics, deviceStyles, 1f / options.DpiScale);
        var layout = HintedLayoutOptions(in request);
        return context.LayoutHintedParagraph(request.Text.Span,
            request.RightToLeft ? NativeTextDirection.RightToLeft : NativeTextDirection.LeftToRight,
            in layout, prepared.Styles, prepared.Metrics, prepared.Devices, prepared.Features,
            variationCoordinates16_16, normalizedCoordinates);
    }

    private sealed record HintedStyleInputs(NativeTextParagraphStyle[] Styles, NativeTextStyleMetrics[] Metrics,
        NativeHintedParagraphDeviceStyle[] Devices, NativeTextFeature[] Features);

    private static HintedStyleInputs PrepareHintedStyles(NativeTextShapingContext context,
        in PortableTextParagraphRequest request, ReadOnlySpan<PortableTextStyleMetrics> metrics,
        ReadOnlySpan<PortableTextHintingStyle> deviceStyles, float logicalUnitsPerPhysicalPixel)
    {
        var fonts = new Dictionary<PortableTextFont, uint> { [request.Font] = 0 };
        var styles = new NativeTextParagraphStyle[request.Styles.Length];
        var nativeMetrics = new NativeTextStyleMetrics[styles.Length];
        var devices = new NativeHintedParagraphDeviceStyle[styles.Length];
        int featureCount = 0;
        foreach (var style in request.Styles.Span) featureCount = checked(featureCount + style.Features.Length);
        var features = new NativeTextFeature[featureCount];
        int feature = 0;
        for (int i = 0; i < styles.Length; i++)
        {
            var source = request.Styles.Span[i];
            ValidateHintedFont(source.Font, source.FontSize);
            if (!fonts.TryGetValue(source.Font, out uint fontIndex))
            {
                var status = context.AddFallbackFont(source.Font.Data.Span, out fontIndex, source.Font.FaceIndex);
                if (status != NativeRendererStatus.Success || fontIndex != fonts.Count)
                    throw new InvalidOperationException($"Native hinted source font registration failed: {status}.");
                fonts.Add(source.Font, fontIndex);
            }
            float scale = source.FontSize / source.Font.UnitsPerEm;
            styles[i] = new(source.Start, source.Length, fontIndex, scale,
                checked((uint)feature), checked((uint)source.Features.Length), source.Language,
                source.DigitZero, source.ContextualDigits, source.PreserveSourceDigitBidi,
                source.Percent, source.GroupSeparator, source.DecimalSeparator);
            foreach (var value in source.Features.Span) features[feature++] = new(value.Tag, value.Value);
            nativeMetrics[i] = new() { Ascent = metrics[i].Ascent, Descent = metrics[i].Descent };
            var device = deviceStyles[i];
            devices[i] = new()
            {
                FontIndex = fontIndex, SourceScale = scale,
                LogicalUnitsPerPhysicalPixel = logicalUnitsPerPhysicalPixel,
                XPixelsPerEm266 = device.XPixelsPerEm266, YPixelsPerEm266 = device.YPixelsPerEm266,
                XPhase266 = device.XPhase266, YPhase266 = device.YPhase266,
                Interpreter = device.Interpreter switch
                {
                    PortableTextHintInterpreter.TrueType35 => (uint)NativeFontHintInterpreter.TrueType35,
                    PortableTextHintInterpreter.TrueType40 => (uint)NativeFontHintInterpreter.TrueType40,
                    _ => throw new ArgumentOutOfRangeException(nameof(deviceStyles))
                },
                VariationStart = device.VariationStart, VariationCount = device.VariationCount
            };
        }
        return new(styles, nativeMetrics, devices, features);
    }

    private static NativeTextParagraphOptions HintedLayoutOptions(in PortableTextParagraphRequest request)
        => new(request.FontSize / request.Font.UnitsPerEm,
            request.MaximumWidth, request.LineHeight, Alignment: request.Alignment switch
            {
                PortableTextAlignment.Left => NativeTextAlignment.Left,
                PortableTextAlignment.Center => NativeTextAlignment.Center,
                PortableTextAlignment.Right => NativeTextAlignment.Right,
                PortableTextAlignment.Justify => NativeTextAlignment.Justify,
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            });

    private static void ValidateHintedFont(PortableTextFont font, float size)
    {
        if (font is null || font.Data.IsEmpty || font.UnitsPerEm == 0 || !float.IsFinite(size) || size <= 0 ||
            !float.IsFinite(size / font.UnitsPerEm) || size / font.UnitsPerEm <= 0)
            throw new ArgumentException("Hinted source styles require an original physical face and positive em size.");
    }

    private static void ValidateHintedRequest(in PortableTextParagraphRequest request,
        int metricCount, int deviceCount, in PortableHintedTextOptions options)
    {
        ValidateHintedFont(request.Font, request.FontSize);
        if (!float.IsFinite(options.DpiScale) || options.DpiScale <= 0 || !float.IsFinite(1f / options.DpiScale))
            throw new ArgumentOutOfRangeException(nameof(options));
        if (request.Text.IsEmpty || request.Styles.IsEmpty || metricCount != request.Styles.Length || deviceCount != metricCount)
            throw new ArgumentException("Hinted paragraphs require nonempty original text and explicit matching styles, metrics and device styles.");
        if (!request.Features.IsEmpty || request.IncrementalTab != 0 || request.TabOrigin != 0 ||
            request.MeasureIntrinsicWidths || request.Wrapping != PortableTextWrapping.Emergency ||
            request.Text.Span.Contains('\t') || request.Text.Span.Contains('\ufffc'))
            throw new NotSupportedException("Hinted source formatting requires per-style features and the existing ordinary horizontal native contract; tabs, objects, intrinsic widths and alternate wrapping are not admitted.");
    }
}

using System.Runtime.CompilerServices;
using System.Windows.Media.ProGPU.Composition;
using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

// These invoke the real explicit source factory/guard. Invalid physical bytes
// prove rejected requests cannot reach native construction; no accepted native
// paragraph or formatter is fabricated. Exact-runtime positive execution is a
// separate required hosted qualification, not a skip/fallback in these controls.
public sealed class WpfSourceDisplayFactoryTests
{
    private static readonly PortableDisplayTextStyle[] Styles = [new(12, 9.0 + 1.0 / 3.0, 2.0 + 1.0 / 3.0)];
    private static readonly PortableTextHintingStyle[] Devices = [new(18 * 64, 18 * 64, PortableTextHintInterpreter.TrueType40)];
    private static readonly PortableDisplayTextOptions Options = new(1.5, 12, 12, 100.0 / 1.5, 3.5);

    private static PortableTextParagraphRequest Request(string text = "ab")
    {
        var font = new PortableTextFont(new byte[] { 0xa5 }, 0, 1024);
        PortableTextFeature[] features = [new(0x6c696761, 1)];
        return new(text.AsMemory(), font, 12, 12, (float)Options.MaximumWidth, false, PortableTextAlignment.Left,
            Features: features, Styles: new PortableTextStyle[] { new(0, text.Length, font, 12, features,
                Language: 0x454e47, DigitZero: 0x0660, ContextualDigits: true, PreserveSourceDigitBidi: true) },
            TabOrigin: (float)Options.TabOrigin, MeasureIntrinsicWidths: true, Wrapping: PortableTextWrapping.WholeWord);
    }

    private static IPortableDisplayTextParagraph Format(PortableTextParagraphRequest request,
        PortableDisplayTextStyle[]? styles = null, PortableDisplayTextOptions? options = null,
        PortableTextHintingStyle[]? devices = null, NativeSourceEmPolicy em = NativeSourceEmPolicy.FloatCaptureNearestHalfUp,
        NativeSourceAdvancePolicy advance = NativeSourceAdvancePolicy.SourceIdealUnits,
        NativeSourceOffsetPolicy offset = NativeSourceOffsetPolicy.SourceIdealUnits,
        PortableHintedTextProjection projection = PortableHintedTextProjection.ScalarReference,
        PortableHintedTextCoverage coverage = PortableHintedTextCoverage.AntialiasedVector)
        => new WpfPortableTextFormatting().FormatSourceDisplay(request, styles ?? Styles, options ?? Options,
            devices ?? Devices, em, advance, offset, projection, coverage);

    [Fact]
    public void OriginalDoubleWidthMetricsAndNonzeroTabOriginRemainUnchanged()
    {
        var request = Request(); var options = Options; var styles = Styles.ToArray();
        WpfPortableTextFormatting.ValidateSourceDisplayRequest(request, styles, options, Devices,
            NativeSourceEmPolicy.FloatCaptureNearestHalfUp, NativeSourceAdvancePolicy.SourceIdealUnits, NativeSourceOffsetPolicy.SourceIdealUnits);
        Assert.Equal(100.0 / 1.5, options.MaximumWidth); Assert.NotEqual((double)request.MaximumWidth, options.MaximumWidth);
        Assert.Equal(Styles, styles); Assert.NotEqual((double)(float)styles[0].Ascent, styles[0].Ascent);
        Assert.Equal(3.5, options.TabOrigin); Assert.True(request.MeasureIntrinsicWidths);
        Assert.Equal(PortableTextWrapping.WholeWord, request.Wrapping);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void MissingOriginalCoverageRejectsBeforeNativeConstruction(int invalid)
    {
        var request = Request(); var styles = Styles; var devices = Devices;
        switch (invalid)
        {
            case 0: request = request with { Text = ReadOnlyMemory<char>.Empty }; break;
            case 1: request = request with { Styles = ReadOnlyMemory<PortableTextStyle>.Empty }; break;
            case 2: styles = []; break;
            case 3: devices = []; break;
        }
        Assert.Throws<ArgumentException>(() => Format(request, styles, devices: devices));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void UnknownOrInvalidCapturePolicyRejectsBeforeNativeConstruction(int invalid)
    {
        var request = Request(); var devices = Devices.ToArray();
        var em = NativeSourceEmPolicy.FloatCaptureNearestHalfUp; var advance = NativeSourceAdvancePolicy.SourceIdealUnits;
        var offset = NativeSourceOffsetPolicy.SourceIdealUnits; var projection = PortableHintedTextProjection.ScalarReference;
        var coverage = PortableHintedTextCoverage.AntialiasedVector;
        switch (invalid)
        {
            case 0: em = (NativeSourceEmPolicy)4; break;
            case 1: advance = (NativeSourceAdvancePolicy)3; break;
            case 2: offset = (NativeSourceOffsetPolicy)2; break;
            case 3: devices[0] = devices[0] with { Interpreter = (PortableTextHintInterpreter)2 }; break;
            case 4: devices[0] = devices[0] with { XPixelsPerEm266 = 0 }; break;
            case 5: devices[0] = devices[0] with { YPixelsPerEm266 = 0 }; break;
            case 6: devices[0] = devices[0] with { XPhase266 = 64 }; break;
            case 7: projection = (PortableHintedTextProjection)99; break;
            case 8: coverage = (PortableHintedTextCoverage)99; break;
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => Format(request, devices: devices, em: em, advance: advance,
            offset: offset, projection: projection, coverage: coverage));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void UnsupportedSourceFormsDoNotFallBackToIdeal(int invalid)
    {
        var request = invalid switch { 0 => Request("a\t"), 1 => Request("a\ufffc"), _ => Request() };
        var options = Options; var devices = Devices.ToArray();
        if (invalid == 2) request = request with { Alignment = PortableTextAlignment.Justify };
        if (invalid == 3) options = options with { MaximumWidth = 0 };
        if (invalid == 4) devices[0] = devices[0] with { VariationCount = 1 };
        Assert.Throws<NotSupportedException>(() => Format(request, options: options, devices: devices));
    }

    [Fact]
    public void OriginalDoublesCannotBeRelabelledAfterNarrowing()
    {
        Assert.Throws<NotSupportedException>(() => Format(Request(), options: Options with { PixelsPerDip = Math.BitIncrement(1.5) }));
        Assert.Throws<NotSupportedException>(() => Format(Request(), options: Options with { EmSize = Math.BitIncrement(12.0) }));
        Assert.Throws<NotSupportedException>(() => Format(Request() with { MaximumWidth = 20 }));
    }

    [Fact]
    public void ExactStylesRejectSourceGapsAndSplitSurrogatesBeforeNativeConstruction()
    {
        var request = Request(); var style = request.Styles.Span[0];
        Assert.Throws<ArgumentException>(() => Format(request with { Styles = new[] { style with { Start = 1, Length = 1 } } }));
        var surrogate = Request("\U0001F600"); var first = surrogate.Styles.Span[0];
        Assert.Throws<ArgumentException>(() => Format(surrogate with { Styles = new[] { first with { Length = 1 }, first with { Start = 1, Length = 1 } } },
            [Styles[0], Styles[0]], devices: [Devices[0], Devices[0]]));
    }

    [Fact]
    public void ExplicitFactoryUsesActualOriginalNativeOwnerWithoutCapabilityRegistration()
    {
        string source = ReadFactory();
        Assert.Contains("context.LayoutHintedSourceParagraph(text.AsSpan()", source, StringComparison.Ordinal);
        Assert.Contains("in sourceOptions, originals, prepared.Features", source, StringComparison.Ordinal);
        Assert.Contains("WpfSourceDisplayTextParagraph.Adopt(paragraph!, text, nativeProjection, nativeCoverage)", source, StringComparison.Ordinal);
        Assert.True(source.IndexOf("context?.Dispose()", StringComparison.Ordinal) < source.IndexOf("WpfSourceDisplayTextParagraph.Adopt", StringComparison.Ordinal));
        Assert.DoesNotContain("EnsureRegistered", source, StringComparison.Ordinal);
        Assert.False(typeof(IPortableDisplayTextFormatting).IsAssignableFrom(typeof(WpfPortableTextFormatting)));
    }

    private static string ReadFactory([CallerFilePath] string file = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "ProGPU.Wpf", "Composition", "WpfPortableTextFormatting.Display.cs")));
}

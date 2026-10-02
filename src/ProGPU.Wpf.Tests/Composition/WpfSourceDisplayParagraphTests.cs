using ProGPU.Backend.Native;
using ProGPU.Wpf.Interop;
using System.Reflection;
using System.Windows.Media.ProGPU.Composition;
using Xunit;

namespace ProGPU.Wpf.Tests.Composition;

public sealed class WpfSourceDisplayParagraphTests
{
    [Fact]
    public void OriginalUtf16IdentityKeepsSupplementaryScalarCoordinates()
    {
        NativeTextScalar[] scalars = [new() { CodePoint = 'A', InputIndex = 0, InputLength = 1 },
            new() { CodePoint = 0x1F600, InputIndex = 1, InputLength = 2 }, new() { CodePoint = 'B', InputIndex = 3, InputLength = 1 }];
        WpfSourceDisplayTextParagraph.ValidateSourceText(scalars, "A\U0001F600B");
    }
    [Fact]
    public void SameLengthDifferentTextCannotAcquireOriginalNativeParagraph()
    {
        NativeTextScalar[] scalars = [new() { CodePoint = 'A', InputIndex = 0, InputLength = 1 }];
        Assert.Throws<ArgumentException>(() => WpfSourceDisplayTextParagraph.ValidateSourceText(scalars, "B"));
    }
    [Fact]
    public void SourceGapsAndPartialSurrogatesStayRejected()
    {
        NativeTextScalar[] gap = [new() { CodePoint = 'B', InputIndex = 1, InputLength = 1 }];
        Assert.Throws<ArgumentException>(() => WpfSourceDisplayTextParagraph.ValidateSourceText(gap, "AB"));
        NativeTextScalar[] partial = [new() { CodePoint = 0x1F600, InputIndex = 0, InputLength = 1 }];
        Assert.Throws<ArgumentException>(() => WpfSourceDisplayTextParagraph.ValidateSourceText(partial, "\U0001F600"));
    }
    [Fact]
    public void ReflowCannotReplaceWholeSourceWithItsSuffix()
    {
        NativeTextScalar[] scalars = [new() { CodePoint = 'A', InputIndex = 0, InputLength = 1 }, new() { CodePoint = 'B', InputIndex = 1, InputLength = 1 }];
        Assert.Throws<ArgumentException>(() => WpfSourceDisplayTextParagraph.ValidateSourceText(scalars, "B"));
        Assert.Throws<ArgumentException>(() => WpfSourceDisplayTextParagraph.ValidateSourceText(scalars, "ABC"));
    }
    [Fact]
    public void ExplicitSourceAdapterDoesNotAdvertiseFormatterPolicy()
    {
        Assert.True(typeof(IPortableDisplayTextParagraph).IsAssignableFrom(typeof(WpfSourceDisplayTextParagraph)));
        Assert.False(typeof(IPortableDisplayTextFormatting).IsAssignableFrom(typeof(WpfPortableTextFormatting)));
    }
    [Fact]
    public void SourceBindingRetainKeepsNativeMilConcreteBaseAndDisplayIdentity()
    {
        Type binding = typeof(WpfHintedGlyphRunBinding).GetNestedType("SourceBinding", BindingFlags.NonPublic)!;
        Assert.NotNull(binding);
        Assert.True(typeof(WpfHintedGlyphRunBinding).IsAssignableFrom(binding));
        Assert.True(typeof(IPortableDisplayGlyphRunBinding).IsAssignableFrom(binding));
        Assert.Equal(binding, binding.GetMethod(nameof(IPortableHintedGlyphRunBinding.Retain))!.DeclaringType);
    }
}

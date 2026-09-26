// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows;

// These portable contracts do not require a dispatcher or the system clipboard.
// Existing DataObjectTests retain the separate native Windows STA coverage.
public class DataObjectEmptyDataTests
{
    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact(Skip = "Exercises the non-Windows portable data object.", SkipWhen = nameof(IsWindows))]
    public void MissingDataReturnsNullForEveryGetDataOverload()
    {
        DataObject data = new();

        data.GetData("missing", autoConvert: false).Should().BeNull();
        data.GetData("missing", autoConvert: true).Should().BeNull();
        data.GetData("missing").Should().BeNull();
        data.GetData(typeof(string)).Should().BeNull();
        data.GetDataPresent("missing").Should().BeFalse();
        data.GetFormats().Should().BeEmpty();
    }

    [Fact(Skip = "Exercises the non-Windows portable data object.", SkipWhen = nameof(IsWindows))]
    public void MissingWellKnownDataRetainsItsEmptyResult()
    {
        DataObject data = new();

        data.GetAudioStream().Should().BeNull();
        data.GetImage().Should().BeNull();
        data.GetFileDropList().Should().BeEmpty();
        data.GetText().Should().BeEmpty();
        data.GetText(TextDataFormat.Text).Should().BeEmpty();
    }

    [Fact(Skip = "Exercises the non-Windows portable data object.", SkipWhen = nameof(IsWindows))]
    public void MissingDataDoesNotChangeExistingPayloadOrWrappedData()
    {
        const string format = "LibreWpf.EmptyDataControl";
        object payload = new();
        DataObject original = new(format, payload, autoConvert: false);
        DataObject wrapped = new(original);

        foreach (DataObject data in new[] { original, wrapped })
        {
            data.GetData("missing", autoConvert: false).Should().BeNull();
            data.GetData("missing", autoConvert: true).Should().BeNull();
            data.GetData(format, autoConvert: false).Should().BeSameAs(payload);
            data.GetDataPresent(format, autoConvert: false).Should().BeTrue();
            data.GetFormats(autoConvert: false).Should().ContainSingle().Which.Should().Be(format);
        }
    }

    [Fact(Skip = "Exercises the non-Windows portable data object.", SkipWhen = nameof(IsWindows))]
    public void InvalidPortableGetDataArgumentsStillThrow()
    {
        DataObject data = new();
        Action missingType = () => data.GetData((Type)null!);
        Action missingFormat = () => data.GetData((string)null!, autoConvert: false);
        Action emptyFormat = () => data.GetData(string.Empty, autoConvert: true);
        missingType.Should().Throw<ArgumentNullException>();
        missingFormat.Should().Throw<ArgumentNullException>();
        emptyFormat.Should().Throw<ArgumentException>();
    }
}

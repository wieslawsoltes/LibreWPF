using ProGPU.Wpf.Interop;
using Forms = System.Windows.Forms;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class PortableWinFormsClipboardServiceTests
{
    [Fact]
    public void SetTextMirrorsTextThroughPortableClipboardService()
    {
        Assert.True(PortableWpfServiceRegistry.TryGetClipboardService(
            PortableWpfServiceKey.WinForms,
            out var service));

        ResetManagedClipboardState(service);

        string? nativeText = null;
        using var registration = service.Register(() => nativeText, text => nativeText = text);

        Forms.Clipboard.SetText("SharpDevelop");

        Assert.Equal("SharpDevelop", nativeText);
        Assert.True(Forms.Clipboard.ContainsText());
        Assert.Equal("SharpDevelop", Forms.Clipboard.GetText());

        Forms.IDataObject? dataObject = Forms.Clipboard.GetDataObject();
        Assert.NotNull(dataObject);
        Assert.Equal("SharpDevelop", dataObject.GetData(Forms.DataFormats.Text));
        Assert.Equal("SharpDevelop", dataObject.GetData(Forms.DataFormats.UnicodeText));
        Assert.Equal("SharpDevelop", dataObject.GetData(Forms.DataFormats.StringFormat));

        Forms.Clipboard.Clear();
        Assert.Null(nativeText);
        Assert.False(Forms.Clipboard.ContainsText());
    }

    [Fact]
    public void GetTextReadsNativeTextWhenNoManagedClipboardStateExists()
    {
        Assert.True(PortableWpfServiceRegistry.TryGetClipboardService(
            PortableWpfServiceKey.WinForms,
            out var service));

        ResetManagedClipboardState(service);

        string? nativeSetText = null;
        using var registration = service.Register(() => "Native text", text => nativeSetText = text);

        Assert.Equal("Native text", Forms.Clipboard.GetText());
        Assert.Null(nativeSetText);

        Forms.IDataObject? dataObject = Forms.Clipboard.GetDataObject();
        Assert.NotNull(dataObject);
        Assert.Equal("Native text", dataObject.GetData(Forms.DataFormats.UnicodeText));
    }

    [Fact]
    public void SetDataObjectMirrorsTextFormatsThroughPortableClipboardService()
    {
        Assert.True(PortableWpfServiceRegistry.TryGetClipboardService(
            PortableWpfServiceKey.WinForms,
            out var service));

        ResetManagedClipboardState(service);

        string? nativeText = null;
        using var registration = service.Register(() => nativeText, text => nativeText = text);
        var dataObject = new Forms.DataObject();
        dataObject.SetData(Forms.DataFormats.UnicodeText, "Text from data object");

        Forms.Clipboard.SetDataObject(dataObject);

        Assert.Equal("Text from data object", nativeText);
        Assert.Same(dataObject, Forms.Clipboard.GetDataObject());
        Assert.Equal("Text from data object", Forms.Clipboard.GetText());
    }

    [Fact]
    public void DataObjectStringPayloadPublishesWinFormsTextFormats()
    {
        var dataObject = new Forms.DataObject("Text payload");

        Assert.Equal("Text payload", dataObject.GetData(Forms.DataFormats.Text));
        Assert.Equal("Text payload", dataObject.GetData(Forms.DataFormats.UnicodeText));
        Assert.Equal("Text payload", dataObject.GetData(Forms.DataFormats.StringFormat));
        Assert.Equal("Text payload", dataObject.GetData(typeof(string)));
    }

    private static void ResetManagedClipboardState(IPortableClipboardServiceRegistrar service)
    {
        using IDisposable registration = service.Register(() => null, _ => { });
    }
}

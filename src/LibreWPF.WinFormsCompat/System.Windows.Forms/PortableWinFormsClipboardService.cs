using System.Runtime.CompilerServices;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows.Forms;

internal static class PortableWinFormsClipboardService
{
    private static readonly object s_sync = new();
    private static readonly ClipboardServiceRegistrar s_registrar = new();
    private static IDisposable? s_registrarRegistration;
    private static IDataObject? s_dataObject;
    private static bool s_hasManagedClipboardState;
    private static Func<string?>? s_getText;
    private static Action<string?>? s_setText;

    [ModuleInitializer]
    internal static void RegisterPortableInteropService()
    {
        s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterClipboardService(s_registrar);
    }

    internal static void Clear()
    {
        RegisterPortableInteropService();
        lock (s_sync)
        {
            s_dataObject = null;
            s_hasManagedClipboardState = true;
        }

        Volatile.Read(ref s_setText)?.Invoke(null);
    }

    internal static void SetDataObject(object data)
    {
        RegisterPortableInteropService();
        IDataObject dataObject = data as IDataObject ?? new DataObject(data);
        string? text = TryGetText(dataObject, out string? dataText) ? dataText : null;
        lock (s_sync)
        {
            s_dataObject = dataObject;
            s_hasManagedClipboardState = true;
        }

        Volatile.Read(ref s_setText)?.Invoke(text);
    }

    internal static IDataObject? GetDataObject()
    {
        RegisterPortableInteropService();
        lock (s_sync)
        {
            if (s_dataObject != null)
            {
                return s_dataObject;
            }

            if (s_hasManagedClipboardState)
            {
                return null;
            }
        }

        Func<string?>? getText = Volatile.Read(ref s_getText);
        if (getText == null)
        {
            return null;
        }

        string? text = getText();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var dataObject = CreateTextDataObject(text);
        lock (s_sync)
        {
            if (!s_hasManagedClipboardState && s_dataObject == null)
            {
                s_dataObject = dataObject;
            }

            return s_dataObject;
        }
    }

    internal static void SetText(string text)
    {
        RegisterPortableInteropService();
        var dataObject = CreateTextDataObject(text);
        lock (s_sync)
        {
            s_dataObject = dataObject;
            s_hasManagedClipboardState = true;
        }

        Volatile.Read(ref s_setText)?.Invoke(text);
    }

    internal static string GetText()
    {
        return TryGetText(GetDataObject(), out string? text) ? text ?? string.Empty : string.Empty;
    }

    internal static bool ContainsText()
    {
        return !string.IsNullOrEmpty(GetText());
    }

    private static DataObject CreateTextDataObject(string text)
    {
        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.Text, text);
        dataObject.SetData(DataFormats.UnicodeText, text);
        dataObject.SetData(DataFormats.StringFormat, text);
        dataObject.SetData(typeof(string), text);
        return dataObject;
    }

    private static bool TryGetText(IDataObject? dataObject, out string? text)
    {
        text = null;
        if (dataObject == null)
        {
            return false;
        }

        if (TryGetText(dataObject, DataFormats.UnicodeText, out text) ||
            TryGetText(dataObject, DataFormats.Text, out text) ||
            TryGetText(dataObject, DataFormats.StringFormat, out text) ||
            TryGetText(dataObject, typeof(string), out text))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetText(IDataObject dataObject, string format, out string? text)
    {
        text = null;
        if (!dataObject.GetDataPresent(format))
        {
            return false;
        }

        text = dataObject.GetData(format) as string;
        return text != null;
    }

    private static bool TryGetText(IDataObject dataObject, Type format, out string? text)
    {
        text = null;
        if (!dataObject.GetDataPresent(format))
        {
            return false;
        }

        text = dataObject.GetData(format) as string;
        return text != null;
    }

    private static Registration Register(Func<string?> getText, Action<string?> setText)
    {
        ArgumentNullException.ThrowIfNull(getText);
        ArgumentNullException.ThrowIfNull(setText);

        Volatile.Write(ref s_getText, getText);
        Volatile.Write(ref s_setText, setText);
        return new Registration(getText, setText);
    }

    private sealed class ClipboardServiceRegistrar : IPortableClipboardServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.WinForms;

        public IDisposable Register(Func<string?> getText, Action<string?> setText)
        {
            return PortableWinFormsClipboardService.Register(getText, setText);
        }

        public void Clear()
        {
            PortableWinFormsClipboardService.Clear();
        }
    }

    private sealed class Registration : IDisposable
    {
        private Func<string?>? _getText;
        private Action<string?>? _setText;

        public Registration(Func<string?> getText, Action<string?> setText)
        {
            _getText = getText;
            _setText = setText;
        }

        public void Dispose()
        {
            Func<string?>? getText = _getText;
            Action<string?>? setText = _setText;
            if (getText == null || setText == null)
            {
                return;
            }

            _getText = null;
            _setText = null;
            bool removedRegistration = false;
            if (ReferenceEquals(Volatile.Read(ref s_getText), getText))
            {
                Volatile.Write(ref s_getText, null);
                removedRegistration = true;
            }

            if (ReferenceEquals(Volatile.Read(ref s_setText), setText))
            {
                Volatile.Write(ref s_setText, null);
                removedRegistration = true;
            }

            if (removedRegistration)
            {
                lock (s_sync)
                {
                    s_dataObject = null;
                    s_hasManagedClipboardState = false;
                }
            }
        }
    }
}

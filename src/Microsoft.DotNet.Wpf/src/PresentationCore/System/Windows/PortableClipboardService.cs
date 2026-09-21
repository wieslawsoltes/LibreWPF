// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows;

internal static class PortableClipboardService
{
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly object s_sync = new object();
    private static readonly ClipboardServiceRegistrar s_registrar = new ClipboardServiceRegistrar();
    private static IDisposable? s_registrarRegistration;
    private static IDataObject? s_dataObject;
    private static bool s_hasManagedClipboardState;
    private static Func<string?>? s_getText;
    private static Action<string?>? s_setText;

    internal static bool IsEnabled
    {
        get
        {
            return !s_isWindows;
        }
    }

    internal static void RegisterPortableInteropService()
    {
        s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterClipboardService(s_registrar);
    }

    internal static IDisposable Register(Func<string?> getText, Action<string?> setText)
    {
        ArgumentNullException.ThrowIfNull(getText);
        ArgumentNullException.ThrowIfNull(setText);

        if (s_isWindows)
        {
            return EmptyRegistration.Instance;
        }

        Volatile.Write(ref s_getText, getText);
        Volatile.Write(ref s_setText, setText);
        return new Registration(getText, setText);
    }

    internal static void Clear()
    {
        lock (s_sync)
        {
            s_dataObject = null;
            s_hasManagedClipboardState = true;
        }

        Volatile.Read(ref s_setText)?.Invoke(null);
    }

    internal static bool TryClear()
    {
        if (s_isWindows)
        {
            return false;
        }

        Clear();
        return true;
    }

    internal static bool TryFlush()
    {
        return !s_isWindows;
    }

    internal static bool TryGetDataObject(out IDataObject? dataObject)
    {
        dataObject = null;
        if (s_isWindows)
        {
            return false;
        }

        lock (s_sync)
        {
            if (s_dataObject != null)
            {
                dataObject = s_dataObject;
                return true;
            }

            if (s_hasManagedClipboardState)
            {
                return true;
            }
        }

        Func<string?>? getText = Volatile.Read(ref s_getText);
        if (getText == null)
        {
            return true;
        }

        string? text = getText();
        if (text == null)
        {
            return true;
        }

        var textDataObject = new PortableManagedDataObject();
        textDataObject.SetData(DataFormats.UnicodeText, text, autoConvert: false);
        lock (s_sync)
        {
            if (!s_hasManagedClipboardState && s_dataObject == null)
            {
                s_dataObject = textDataObject;
            }

            dataObject = s_dataObject;
        }

        return true;
    }

    internal static bool TryIsCurrent(IDataObject data, out bool isCurrent)
    {
        isCurrent = false;
        if (s_isWindows)
        {
            return false;
        }

        lock (s_sync)
        {
            isCurrent = ReferenceEquals(s_dataObject, data);
        }

        return true;
    }

    internal static bool TrySetData(string format, object data, bool autoConvert, bool copy)
    {
        if (s_isWindows)
        {
            return false;
        }

        var dataObject = new PortableManagedDataObject();
        dataObject.SetData(format, data, autoConvert);
        return TrySetDataObject(dataObject, copy);
    }

    internal static bool TrySetFileDropList(StringCollection fileDropList)
    {
        if (s_isWindows)
        {
            return false;
        }

        string[] strings = new string[fileDropList.Count];
        fileDropList.CopyTo(strings, 0);
        return TrySetData(DataFormats.FileDrop, strings, autoConvert: true, copy: true);
    }

    internal static bool TrySetObject(object data, bool copy)
    {
        if (s_isWindows)
        {
            return false;
        }

        IDataObject dataObject;
        if (data is IDataObject existingDataObject)
        {
            dataObject = existingDataObject;
        }
        else
        {
            var portableDataObject = new PortableManagedDataObject();
            portableDataObject.SetData(data);
            dataObject = portableDataObject;
        }

        return TrySetDataObject(dataObject, copy);
    }

    internal static bool TrySetDataObject(IDataObject dataObject, bool copy)
    {
        if (s_isWindows)
        {
            return false;
        }

        bool hasUnicodeText = TryGetUnicodeText(dataObject, out string? text);
        lock (s_sync)
        {
            s_dataObject = dataObject;
            s_hasManagedClipboardState = true;
        }

        Volatile.Read(ref s_setText)?.Invoke(hasUnicodeText ? text : null);

        return true;
    }

    private static bool TryGetUnicodeText(IDataObject dataObject, out string? text)
    {
        text = null;
        if (!dataObject.GetDataPresent(DataFormats.UnicodeText, autoConvert: false))
        {
            return false;
        }

        text = dataObject.GetData(DataFormats.UnicodeText, autoConvert: false) as string;
        return true;
    }

    private sealed class Registration : IDisposable
    {
        private Func<string?>? _getText;
        private Action<string?>? _setText;

        internal Registration(Func<string?> getText, Action<string?> setText)
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

    private sealed class EmptyRegistration : IDisposable
    {
        internal static readonly EmptyRegistration Instance = new EmptyRegistration();

        public void Dispose()
        {
        }
    }

    private sealed class ClipboardServiceRegistrar : IPortableClipboardServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey
        {
            get
            {
                return PortableWpfServiceKey.PresentationCore;
            }
        }

        public IDisposable Register(Func<string?> getText, Action<string?> setText)
        {
            return PortableClipboardService.Register(getText, setText);
        }

        public void Clear()
        {
            PortableClipboardService.Clear();
        }
    }
}

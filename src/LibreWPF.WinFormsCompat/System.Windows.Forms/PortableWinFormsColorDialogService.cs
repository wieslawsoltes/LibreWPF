using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows.Forms;

internal static class PortableWinFormsColorDialogService
{
    private static readonly ColorDialogServiceRegistrar s_registrar = new();
    private static IDisposable? s_registrarRegistration;
    private static Func<PortableColorDialogRequest, int?>? s_showDialog;

    [ModuleInitializer]
    internal static void RegisterPortableInteropService()
    {
        s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterColorDialogService(s_registrar);
    }

    internal static int? ShowColorDialog(int initialArgb, IReadOnlyList<int> customColors)
    {
        RegisterPortableInteropService();

        Func<PortableColorDialogRequest, int?>? showDialog = Volatile.Read(ref s_showDialog);
        if (showDialog == null)
        {
            return null;
        }

        return showDialog(new PortableColorDialogRequest(initialArgb, customColors));
    }

    private static Registration Register(Func<PortableColorDialogRequest, int?> showDialog)
    {
        ArgumentNullException.ThrowIfNull(showDialog);

        Volatile.Write(ref s_showDialog, showDialog);
        return new Registration(showDialog);
    }

    private static void Clear()
    {
        Volatile.Write(ref s_showDialog, null);
    }

    private sealed class ColorDialogServiceRegistrar : IPortableColorDialogServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.WinForms;

        public IDisposable Register(Func<PortableColorDialogRequest, int?> showDialog)
        {
            return PortableWinFormsColorDialogService.Register(showDialog);
        }

        public void Clear()
        {
            PortableWinFormsColorDialogService.Clear();
        }
    }

    private sealed class Registration : IDisposable
    {
        private Func<PortableColorDialogRequest, int?>? _showDialog;

        public Registration(Func<PortableColorDialogRequest, int?> showDialog)
        {
            _showDialog = showDialog;
        }

        public void Dispose()
        {
            Func<PortableColorDialogRequest, int?>? showDialog = _showDialog;
            if (showDialog == null)
            {
                return;
            }

            _showDialog = null;
            if (ReferenceEquals(Volatile.Read(ref s_showDialog), showDialog))
            {
                Volatile.Write(ref s_showDialog, null);
            }
        }
    }
}

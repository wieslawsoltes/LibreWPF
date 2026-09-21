using System.Runtime.CompilerServices;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows.Forms;

internal static class PortableWinFormsFontDialogService
{
    private static readonly FontDialogServiceRegistrar s_registrar = new();
    private static IDisposable? s_registrarRegistration;
    private static Func<PortableFontDialogRequest, PortableFontDialogResult?>? s_showDialog;

    [ModuleInitializer]
    internal static void RegisterPortableInteropService()
    {
        s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterFontDialogService(s_registrar);
    }

    internal static PortableFontDialogResult? ShowFontDialog(PortableFontDialogRequest request)
    {
        RegisterPortableInteropService();

        Func<PortableFontDialogRequest, PortableFontDialogResult?>? showDialog = Volatile.Read(ref s_showDialog);
        return showDialog?.Invoke(request);
    }

    private static Registration Register(Func<PortableFontDialogRequest, PortableFontDialogResult?> showDialog)
    {
        ArgumentNullException.ThrowIfNull(showDialog);

        Volatile.Write(ref s_showDialog, showDialog);
        return new Registration(showDialog);
    }

    private static void Clear()
    {
        Volatile.Write(ref s_showDialog, null);
    }

    private sealed class FontDialogServiceRegistrar : IPortableFontDialogServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.WinForms;

        public IDisposable Register(Func<PortableFontDialogRequest, PortableFontDialogResult?> showDialog)
        {
            return PortableWinFormsFontDialogService.Register(showDialog);
        }

        public void Clear()
        {
            PortableWinFormsFontDialogService.Clear();
        }
    }

    private sealed class Registration : IDisposable
    {
        private Func<PortableFontDialogRequest, PortableFontDialogResult?>? _showDialog;

        public Registration(Func<PortableFontDialogRequest, PortableFontDialogResult?> showDialog)
        {
            _showDialog = showDialog;
        }

        public void Dispose()
        {
            Func<PortableFontDialogRequest, PortableFontDialogResult?>? showDialog = _showDialog;
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

using System.Runtime.CompilerServices;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows.Forms;

internal static class PortableWinFormsDialogService
{
    private static readonly FileDialogServiceRegistrar s_registrar = new();
    private static IDisposable? s_registrarRegistration;
    private static Func<PortableFileDialogRequest, PortableFileDialogResult?>? s_showDialog;

    [ModuleInitializer]
    internal static void RegisterPortableInteropService()
    {
        s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterFileDialogService(s_registrar);
    }

    internal static PortableFileDialogResult? ShowFileDialog(
        string kind,
        string title,
        string initialDirectory,
        string suggestedItemName,
        string defaultExtension,
        string filter,
        int filterIndex,
        bool allowMultipleSelection = false)
    {
        RegisterPortableInteropService();

        Func<PortableFileDialogRequest, PortableFileDialogResult?>? showDialog = Volatile.Read(ref s_showDialog);
        if (showDialog == null)
        {
            return null;
        }

        var request = new PortableFileDialogRequest(
            kind,
            title,
            initialDirectory,
            defaultDirectory: string.Empty,
            suggestedItemName,
            defaultExtension,
            filter,
            filterIndex,
            allowMultipleSelection);
        return showDialog(request);
    }

    private static Registration RegisterResult(
        Func<PortableFileDialogRequest, PortableFileDialogResult?> showDialog)
    {
        ArgumentNullException.ThrowIfNull(showDialog);

        Volatile.Write(ref s_showDialog, showDialog);
        return new Registration(showDialog);
    }

    private static void Clear()
    {
        Volatile.Write(ref s_showDialog, null);
    }

    private sealed class FileDialogServiceRegistrar : IPortableFileDialogServiceRegistrar
    {
        public PortableWpfServiceKey ServiceKey => PortableWpfServiceKey.WinForms;

        public IDisposable Register(Func<PortableFileDialogRequest, string?> showDialog)
        {
            ArgumentNullException.ThrowIfNull(showDialog);
            return PortableWinFormsDialogService.RegisterResult(request =>
            {
                string? selectedPath = showDialog(request);
                return selectedPath == null ? null : new PortableFileDialogResult(selectedPath);
            });
        }

        public IDisposable RegisterResult(
            Func<PortableFileDialogRequest, PortableFileDialogResult?> showDialog)
        {
            return PortableWinFormsDialogService.RegisterResult(showDialog);
        }

        public void Clear()
        {
            PortableWinFormsDialogService.Clear();
        }
    }

    private sealed class Registration : IDisposable
    {
        private Func<PortableFileDialogRequest, PortableFileDialogResult?>? _showDialog;

        public Registration(Func<PortableFileDialogRequest, PortableFileDialogResult?> showDialog)
        {
            _showDialog = showDialog;
        }

        public void Dispose()
        {
            Func<PortableFileDialogRequest, PortableFileDialogResult?>? showDialog = _showDialog;
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

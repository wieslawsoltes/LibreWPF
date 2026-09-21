// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows
{
    internal readonly struct PortableMessageBoxRequest
    {
        internal PortableMessageBoxRequest(
            object owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options)
        {
            Owner = owner;
            MessageBoxText = messageBoxText;
            Caption = caption;
            Button = button;
            Icon = icon;
            DefaultResult = defaultResult;
            Options = options;
        }

        internal object Owner { get; }

        internal string MessageBoxText { get; }

        internal string Caption { get; }

        internal MessageBoxButton Button { get; }

        internal MessageBoxImage Icon { get; }

        internal MessageBoxResult DefaultResult { get; }

        internal MessageBoxOptions Options { get; }

        internal MessageBoxResult FallbackResult
        {
            get
            {
                return MessageBox.GetPortableFallbackResult(DefaultResult, Button);
            }
        }
    }

    internal static class PortableMessageBoxService
    {
        private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly MessageBoxServiceRegistrar s_registrar = new MessageBoxServiceRegistrar();
        private static IDisposable s_registrarRegistration;
        private static Handler s_handler;

        internal static bool IsEnabled
        {
            get
            {
                return !s_isWindows && Volatile.Read(ref s_handler) != null;
            }
        }

        internal static void RegisterPortableInteropService()
        {
            s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterMessageBoxService(s_registrar);
        }

        internal static IDisposable Register(Func<object, object> show)
        {
            ArgumentNullException.ThrowIfNull(show);

            return Register(
                request => ConvertResult(request, show(request)),
                preferBeforePortableDialog: true);
        }

        internal static IDisposable Register(Func<PortableMessageBoxRequest, MessageBoxResult> show)
        {
            ArgumentNullException.ThrowIfNull(show);

            if (s_isWindows)
            {
                return EmptyRegistration.Instance;
            }

            return Register(show, preferBeforePortableDialog: false);
        }

        internal static void Clear()
        {
            Volatile.Write(ref s_handler, null);
        }

        internal static bool TryShowOverride(
            object owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options,
            out MessageBoxResult result)
        {
            return TryShowCore(
                owner,
                messageBoxText,
                caption,
                button,
                icon,
                defaultResult,
                options,
                requireOverride: true,
                out result);
        }

        internal static bool TryShow(
            object owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options,
            out MessageBoxResult result)
        {
            return TryShowCore(
                owner,
                messageBoxText,
                caption,
                button,
                icon,
                defaultResult,
                options,
                requireOverride: false,
                out result);
        }

        private static IDisposable Register(
            Func<PortableMessageBoxRequest, MessageBoxResult> show,
            bool preferBeforePortableDialog)
        {
            ArgumentNullException.ThrowIfNull(show);

            if (s_isWindows)
            {
                return EmptyRegistration.Instance;
            }

            var handler = new Handler(show, preferBeforePortableDialog);
            Volatile.Write(ref s_handler, handler);
            return new Registration(handler);
        }

        private static bool TryShowCore(
            object owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options,
            bool requireOverride,
            out MessageBoxResult result)
        {
            result = MessageBoxResult.None;

            if (s_isWindows)
            {
                return false;
            }

            Handler handler = Volatile.Read(ref s_handler);
            if (handler == null || (requireOverride && !handler.PreferBeforePortableDialog))
            {
                return false;
            }

            var request = new PortableMessageBoxRequest(
                owner,
                messageBoxText,
                caption,
                button,
                icon,
                defaultResult,
                options);
            result = handler.Show(request);
            return true;
        }

        private static MessageBoxResult ConvertResult(PortableMessageBoxRequest request, object result)
        {
            if (result == null)
            {
                return request.FallbackResult;
            }

            if (result is MessageBoxResult messageBoxResult)
            {
                return messageBoxResult;
            }

            if (result is string resultName &&
                Enum.TryParse(resultName, ignoreCase: false, out MessageBoxResult parsedResult))
            {
                return parsedResult;
            }

            throw new InvalidOperationException($"Portable message box handler returned an invalid result '{result}'.");
        }

        private static ProGPU.Wpf.Interop.PortableMessageBoxRequest CreateInteropRequest(
            PortableMessageBoxRequest request)
        {
            return new ProGPU.Wpf.Interop.PortableMessageBoxRequest(
                request.Owner,
                request.MessageBoxText,
                request.Caption,
                request.Button.ToString(),
                request.Icon.ToString(),
                request.DefaultResult.ToString(),
                request.Options.ToString(),
                request.FallbackResult.ToString());
        }

        private sealed class Handler
        {
            internal Handler(
                Func<PortableMessageBoxRequest, MessageBoxResult> show,
                bool preferBeforePortableDialog)
            {
                Show = show;
                PreferBeforePortableDialog = preferBeforePortableDialog;
            }

            internal Func<PortableMessageBoxRequest, MessageBoxResult> Show { get; }

            internal bool PreferBeforePortableDialog { get; }
        }

        private sealed class Registration : IDisposable
        {
            private Handler _handler;

            public Registration(Handler handler)
            {
                _handler = handler;
            }

            public void Dispose()
            {
                Handler handler = _handler;
                if (handler == null)
                {
                    return;
                }

                _handler = null;
                if (ReferenceEquals(Volatile.Read(ref s_handler), handler))
                {
                    Volatile.Write(ref s_handler, null);
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

        private sealed class MessageBoxServiceRegistrar : IPortableMessageBoxServiceRegistrar
        {
            public PortableWpfServiceKey ServiceKey
            {
                get
                {
                    return PortableWpfServiceKey.PresentationFramework;
                }
            }

            public IDisposable Register(Func<ProGPU.Wpf.Interop.PortableMessageBoxRequest, string> show)
            {
                ArgumentNullException.ThrowIfNull(show);

                return PortableMessageBoxService.Register(
                    request => ConvertResult(request, show(CreateInteropRequest(request))),
                    preferBeforePortableDialog: true);
            }

            public IDisposable RegisterFallback(Func<ProGPU.Wpf.Interop.PortableMessageBoxRequest, string> show)
            {
                ArgumentNullException.ThrowIfNull(show);

                return PortableMessageBoxService.Register(
                    request => ConvertResult(request, show(CreateInteropRequest(request))),
                    preferBeforePortableDialog: false);
            }

            public void Clear()
            {
                PortableMessageBoxService.Clear();
            }
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Windows.Threading;
using MS.Internal;
using MS.Internal.KnownBoxes;

namespace System.Windows
{
    public class SplashScreen
    {
        private readonly string _resourceName;
        private readonly ResourceManager _resourceManager;
        private Dispatcher _dispatcher;
        private bool _isShown;

        public SplashScreen(string resourceName) : this(Assembly.GetEntryAssembly(), resourceName)
        {
        }

        public SplashScreen(Assembly resourceAssembly, string resourceName)
        {
            ArgumentNullException.ThrowIfNull(resourceAssembly);
            if (string.IsNullOrEmpty(resourceName))
            {
                throw new ArgumentNullException(nameof(resourceName));
            }

            _resourceName = resourceName.ToLowerInvariant();
            _resourceManager = new ResourceManager($"{ReflectionUtils.GetAssemblyPartialName(resourceAssembly)}.g", resourceAssembly);
        }

        public void Show(bool autoClose)
        {
            Show(autoClose, topMost: false);
        }

        public void Show(bool autoClose, bool topMost)
        {
            if (_isShown)
            {
                return;
            }

            using UnmanagedMemoryStream resourceStream = GetResourceStream()
                ?? throw new IOException(SR.Format(SR.UnableToLocateResource, _resourceName));

            _isShown = true;
            _dispatcher = Dispatcher.CurrentDispatcher;
            if (autoClose)
            {
                _dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    (DispatcherOperationCallback)(static arg =>
                    {
                        ((SplashScreen)arg).Close(TimeSpan.Zero);
                        return null;
                    }),
                    this);
            }
        }

        private UnmanagedMemoryStream GetResourceStream()
        {
            UnmanagedMemoryStream stream = _resourceManager.GetStream(_resourceName, CultureInfo.CurrentUICulture);
            if (stream is not null)
            {
                return stream;
            }

            string resourceName = ResourceIDHelper.GetResourceIDFromRelativePath(_resourceName);
            return _resourceManager.GetStream(resourceName, CultureInfo.CurrentUICulture);
        }

        public void Close(TimeSpan fadeoutDuration)
        {
            object result = null;
            if (_dispatcher is not null)
            {
                if (_dispatcher.CheckAccess())
                {
                    result = CloseInternal();
                }
                else
                {
                    result = _dispatcher.Invoke(DispatcherPriority.Normal, (DispatcherOperationCallback)(static arg => ((SplashScreen)arg).CloseInternal()), this);
                }
            }

            if (result != BooleanBoxes.TrueBox)
            {
                DestroyResources();
            }
        }

        private object CloseInternal()
        {
            DestroyResources();
            return BooleanBoxes.TrueBox;
        }

        private void DestroyResources(bool finalizer = false)
        {
            _isShown = false;
            GC.SuppressFinalize(this);

            if (!finalizer)
            {
                _resourceManager?.ReleaseAllResources();
            }
        }

        ~SplashScreen()
        {
            DestroyResources(finalizer: true);
        }
    }
}

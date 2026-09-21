// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Media.Composition;

namespace System.Windows.Media
{
    /// <summary>
    /// Managed composition target boundary for non-HWND hosts.
    /// </summary>
    internal sealed class PortableCompositionTarget : CompositionTarget
    {
        private Matrix _transformToDevice = Matrix.Identity;
        private Matrix _transformFromDevice = Matrix.Identity;

        internal PortableCompositionTarget()
        {
        }

        internal PortableCompositionTarget(double dpiScaleX, double dpiScaleY)
        {
            SetDeviceScaleCore(dpiScaleX, dpiScaleY);
        }

        internal event EventHandler RootVisualChanged;

        internal override bool UsesDuceComposition
        {
            get { return false; }
        }

        public override Matrix TransformToDevice
        {
            get
            {
                VerifyAPIReadOnly();
                return _transformToDevice;
            }
        }

        public override Matrix TransformFromDevice
        {
            get
            {
                VerifyAPIReadOnly();
                return _transformFromDevice;
            }
        }

        internal void SetDeviceScale(double dpiScaleX, double dpiScaleY)
        {
            VerifyAPIReadWrite();
            SetDeviceScaleCore(dpiScaleX, dpiScaleY);
            StateChangedCallback(new object[]
            {
                HostStateFlags.WorldTransform,
                _transformToDevice,
                Rect.Empty
            });
        }

        private void SetDeviceScaleCore(double dpiScaleX, double dpiScaleY)
        {
            if (!double.IsFinite(dpiScaleX) || dpiScaleX <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(dpiScaleX));
            }

            if (!double.IsFinite(dpiScaleY) || dpiScaleY <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(dpiScaleY));
            }

            _transformToDevice = new Matrix(dpiScaleX, 0.0, 0.0, dpiScaleY, 0.0, 0.0);
            _transformFromDevice = new Matrix(1.0 / dpiScaleX, 0.0, 0.0, 1.0 / dpiScaleY, 0.0, 0.0);
        }

        internal override void CreateUCEResources(DUCE.Channel channel, DUCE.Channel outOfBandChannel)
        {
        }

        internal override void ReleaseUCEResources(DUCE.Channel channel, DUCE.Channel outOfBandChannel)
        {
        }

        internal override void OnRootVisualChanged(Visual oldRootVisual, Visual newRootVisual)
        {
            RootVisualChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

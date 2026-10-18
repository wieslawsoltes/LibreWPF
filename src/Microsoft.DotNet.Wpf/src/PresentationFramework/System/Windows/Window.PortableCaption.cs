// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Media;

namespace System.Windows
{
    public partial class Window
    {
        private PortableFixedWindowCaption _portableFixedCaption;

        internal static readonly DependencyProperty IsPortableCaptionCloseEnabledProperty =
            DependencyProperty.Register("IsPortableCaptionCloseEnabled", typeof(bool), typeof(Window),
                new FrameworkPropertyMetadata(true, OnPortableCaptionCloseEnabledChanged));

        internal bool IsPortableCaptionCloseEnabled
        {
            get => (bool)GetValue(IsPortableCaptionCloseEnabledProperty);
            set => SetValue(IsPortableCaptionCloseEnabledProperty, value);
        }

        private static void OnPortableCaptionCloseEnabledChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
            => ((Window)source).ApplyPortableCloseButtonEnabled(force: true);

        private void ApplyPortableCloseButtonEnabled(bool force = false)
        {
            // Ordinary windows do not opt into a new native close policy.
            // The source guard remains authoritative when a host lacks support.
            object activation = _portableWindowActivation;
            if (activation != null && (force || ReadLocalValue(IsPortableCaptionCloseEnabledProperty) != DependencyProperty.UnsetValue))
                PortableWindowActivationService.SetCloseButtonEnabled(activation, IsPortableCaptionCloseEnabled);
        }

        protected override int VisualChildrenCount
            => base.VisualChildrenCount + (_portableFixedCaption == null ? 0 : 1);

        protected override Visual GetVisualChild(int index)
            => _portableFixedCaption != null && index == base.VisualChildrenCount
                ? _portableFixedCaption : base.GetVisualChild(index);

        private void UpdatePortableFixedCaption()
        {
            ApplyPortableCloseButtonEnabled();
            object activation = _portableWindowActivation;
            bool supported = PortableWindowActivationService.RequiresSourceFixedCaption(activation);
            // Host callbacks may reenter source property/lifetime changes. Read
            // source policy after the callback and retain the original owner.
            bool selected = activation != null && ReferenceEquals(activation, _portableWindowActivation) &&
                !_hasPortableCustomChrome && WindowStyle != WindowStyle.None &&
                ResizeMode == ResizeMode.NoResize && supported;
            if (selected == (_portableFixedCaption != null)) return;

            if (selected)
            {
                var caption = new PortableFixedWindowCaption(this);
                _portableFixedCaption = caption;
                try { AddVisualChild(caption); }
                catch { _portableFixedCaption = null; throw; }
            }
            else
            {
                var caption = _portableFixedCaption;
                _portableFixedCaption = null;
                RemoveVisualChild(caption);
            }
            InvalidateMeasure();
            InvalidateArrange();
        }

        private Size MeasurePortableFixedCaption(Size constraint)
        {
            if (_portableFixedCaption == null) return default;
            _portableFixedCaption.Measure(new Size(constraint.Width, double.PositiveInfinity));
            return _portableFixedCaption.DesiredSize;
        }

        private double ArrangePortableFixedCaption(Size client)
        {
            if (_portableFixedCaption == null) return 0;
            double height = Math.Min(client.Height, _portableFixedCaption.DesiredSize.Height);
            _portableFixedCaption.Arrange(new Rect(0, 0, client.Width, height));
            return height;
        }
    }
}

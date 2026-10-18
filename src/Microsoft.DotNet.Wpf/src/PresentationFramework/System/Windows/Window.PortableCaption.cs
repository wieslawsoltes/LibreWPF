// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Media;

namespace System.Windows
{
    public partial class Window
    {
        private PortableFixedWindowCaption _portableFixedCaption;

        protected override int VisualChildrenCount
            => base.VisualChildrenCount + (_portableFixedCaption == null ? 0 : 1);

        protected override Visual GetVisualChild(int index)
            => _portableFixedCaption != null && index == base.VisualChildrenCount
                ? _portableFixedCaption : base.GetVisualChild(index);

        private void UpdatePortableFixedCaption()
        {
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

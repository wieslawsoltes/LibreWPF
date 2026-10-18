// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace System.Windows
{
    // A source visual, not a guessed native frame. All NoResize windows selected
    // by the host use the same caption; MessageBox does not own a special path.
    internal sealed class PortableFixedWindowCaption : Grid
    {
        private readonly Window _window;

        internal PortableFixedWindowCaption(Window window)
        {
            _window = window;
            Height = 32;
            ClipToBounds = true;
            SetResourceReference(BackgroundProperty, SystemColors.ControlBrushKey);
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });
            title.SetResourceReference(TextBlock.ForegroundProperty, SystemColors.ControlTextBrushKey);
            Children.Add(title);

            var glyph = new Path
            {
                Data = Geometry.Parse("M 0,0 L 8,8 M 0,8 L 8,0"),
                Width = 10,
                Height = 10,
                Stretch = Stretch.Uniform,
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            glyph.Data.Freeze();
            glyph.SetResourceReference(Path.StrokeProperty, SystemColors.ControlTextBrushKey);
            var close = new Button
            {
                Content = glyph,
                Width = 36,
                Focusable = false,
                IsTabStop = false,
                ToolTip = "Close"
            };
            AutomationProperties.SetName(close, "Close");
            AutomationProperties.SetAutomationId(close, "PortableWindowClose");
            close.Click += (_, _) => _window.Close();
            SetColumn(close, 1);
            Children.Add(close);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!e.Handled && e.ClickCount == 1 && _window.WindowState == WindowState.Normal)
            {
                _window.DragMove();
                e.Handled = true;
            }
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            if (!e.Handled && _window.PortableWindowActivation != null)
            {
                Point desktop = PointToScreen(e.GetPosition(this));
                e.Handled = PortableWindowActivationService.TryShowSystemMenu(
                    _window.PortableWindowActivation, desktop.X, desktop.Y);
            }
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace System.Windows
{
    /// <summary>
    /// Provides a WPF-rendered message box for portable window hosts. Process-backed
    /// dialogs remain the fallback when no WPF application or portable host exists.
    /// </summary>
    internal static class PortableMessageBoxDialog
    {
        private const double DialogWidth = 520;
        private const double MinimumDialogHeight = 190;
        private const double MaximumDialogHeight = 460;
        private const double EstimatedLineHeight = 22;
        private const int EstimatedCharactersPerLine = 58;

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
            result = MessageBoxResult.None;
            Application application = Application.Current;

            if (OperatingSystem.IsWindows() ||
                !PortableWindowActivationService.IsEnabled ||
                application == null ||
                !application.Dispatcher.CheckAccess() ||
                (options & (MessageBoxOptions.ServiceNotification | MessageBoxOptions.DefaultDesktopOnly)) != 0)
            {
                return false;
            }

            if (owner is Window explicitOwner && !explicitOwner.IsVisible)
            {
                // WPF rejects assigning an Owner that has not been shown. Keep the
                // registered process/service backend available for startup-time calls.
                return false;
            }

            Window ownerWindow = ResolveOwner(owner);
            MessageBoxResult[] buttonResults = GetButtonResults(button);
            MessageBoxResult effectiveDefault = GetDefaultResult(buttonResults, defaultResult);
            MessageBoxResult selectedResult = GetCloseResult(buttonResults, effectiveDefault);

            double dialogHeight = EstimateDialogHeight(messageBoxText);
            var dialog = new Window
            {
                Title = caption ?? string.Empty,
                Width = DialogWidth,
                Height = dialogHeight,
                MinWidth = DialogWidth,
                MaxWidth = DialogWidth,
                MinHeight = MinimumDialogHeight,
                MaxHeight = MaximumDialogHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Owner = ownerWindow
            };

            dialog.SetResourceReference(Control.BackgroundProperty, SystemColors.WindowBrushKey);
            dialog.SetResourceReference(Control.ForegroundProperty, SystemColors.WindowTextBrushKey);

            Point position = GetDialogPosition(ownerWindow, DialogWidth, dialogHeight);
            dialog.Left = position.X;
            dialog.Top = position.Y;
            dialog.Loaded += delegate
            {
                // Some portable window managers apply their own initial placement while
                // creating the native surface. Reapply owner centering once that surface
                // exists so the explicit WPF position wins on X11 and Wayland alike.
                Point loadedPosition = GetDialogPosition(ownerWindow, DialogWidth, dialogHeight);
                if (dialog.PortableWindowActivation != null)
                {
                    PortableWindowActivationService.SetPosition(
                        dialog.PortableWindowActivation,
                        loadedPosition.X,
                        loadedPosition.Y);
                }
            };

            var content = CreateContent(
                dialog,
                messageBoxText,
                buttonResults,
                icon,
                effectiveDefault,
                options,
                buttonResult => selectedResult = buttonResult);
            dialog.Content = content;

            dialog.ShowDialog();
            result = selectedResult;
            return true;
        }

        internal static MessageBoxResult[] GetButtonResults(MessageBoxButton button)
        {
            switch (button)
            {
                case MessageBoxButton.OK:
                    return new[] { MessageBoxResult.OK };
                case MessageBoxButton.OKCancel:
                    return new[] { MessageBoxResult.OK, MessageBoxResult.Cancel };
                case MessageBoxButton.AbortRetryIgnore:
                    return new[] { MessageBoxResult.Abort, MessageBoxResult.Retry, MessageBoxResult.Ignore };
                case MessageBoxButton.YesNoCancel:
                    return new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel };
                case MessageBoxButton.YesNo:
                    return new[] { MessageBoxResult.Yes, MessageBoxResult.No };
                case MessageBoxButton.RetryCancel:
                    return new[] { MessageBoxResult.Retry, MessageBoxResult.Cancel };
                case MessageBoxButton.CancelTryContinue:
                    return new[] { MessageBoxResult.Cancel, MessageBoxResult.TryAgain, MessageBoxResult.Continue };
                default:
                    return new[] { MessageBoxResult.OK };
            }
        }

        internal static MessageBoxResult GetDefaultResult(
            MessageBoxResult[] buttonResults,
            MessageBoxResult requestedDefault)
        {
            if (requestedDefault != MessageBoxResult.None)
            {
                for (int i = 0; i < buttonResults.Length; i++)
                {
                    if (buttonResults[i] == requestedDefault)
                    {
                        return requestedDefault;
                    }
                }
            }

            return buttonResults[0];
        }

        internal static Point GetCenteredPosition(
            double ownerLeft,
            double ownerTop,
            double ownerWidth,
            double ownerHeight,
            double dialogWidth,
            double dialogHeight)
        {
            return new Point(
                ownerLeft + Math.Max(0, (ownerWidth - dialogWidth) / 2),
                ownerTop + Math.Max(0, (ownerHeight - dialogHeight) / 2));
        }

        private static Grid CreateContent(
            Window dialog,
            string messageBoxText,
            MessageBoxResult[] buttonResults,
            MessageBoxImage icon,
            MessageBoxResult defaultResult,
            MessageBoxOptions options,
            Action<MessageBoxResult> setResult)
        {
            bool rightToLeft = (options & MessageBoxOptions.RtlReading) != 0;
            bool rightAlign = (options & MessageBoxOptions.RightAlign) != 0;

            var root = new Grid
            {
                Margin = new Thickness(24, 22, 24, 18),
                FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
            };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var messageArea = new Grid();
            if (icon != MessageBoxImage.None)
            {
                messageArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            }
            messageArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int textColumn = 0;
            if (icon != MessageBoxImage.None)
            {
                FrameworkElement iconElement = CreateIcon(icon);
                iconElement.HorizontalAlignment = HorizontalAlignment.Center;
                iconElement.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(iconElement, 0);
                messageArea.Children.Add(iconElement);
                textColumn = 1;
            }

            var text = new TextBlock
            {
                Text = messageBoxText ?? string.Empty,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = rightAlign ? TextAlignment.Right : TextAlignment.Left,
                Margin = icon == MessageBoxImage.None
                    ? new Thickness(0, 4, 0, 16)
                    : new Thickness(14, 4, 0, 16)
            };
            Grid.SetColumn(text, textColumn);
            messageArea.Children.Add(text);
            Grid.SetRow(messageArea, 0);
            root.Children.Add(messageArea);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                FlowDirection = FlowDirection.LeftToRight
            };

            for (int i = 0; i < buttonResults.Length; i++)
            {
                MessageBoxResult buttonResult = buttonResults[i];
                var buttonControl = new Button
                {
                    Content = GetButtonText(buttonResult),
                    MinWidth = 88,
                    MinHeight = 30,
                    Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                    Padding = new Thickness(12, 3, 12, 3),
                    IsDefault = buttonResult == defaultResult,
                    IsCancel = buttonResult == MessageBoxResult.Cancel
                };
                buttonControl.Click += delegate
                {
                    setResult(buttonResult);
                    dialog.DialogResult = true;
                };
                buttons.Children.Add(buttonControl);
            }

            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);
            return root;
        }

        private static FrameworkElement CreateIcon(MessageBoxImage icon)
        {
            var canvas = new Canvas { Width = 44, Height = 44 };
            Brush background;
            string symbol;
            Brush foreground = Brushes.White;

            switch (icon)
            {
                case MessageBoxImage.Error:
                    background = Brushes.Firebrick;
                    symbol = "×";
                    break;
                case MessageBoxImage.Question:
                    background = Brushes.RoyalBlue;
                    symbol = "?";
                    break;
                case MessageBoxImage.Warning:
                    background = Brushes.Goldenrod;
                    foreground = Brushes.Black;
                    symbol = "!";
                    break;
                default:
                    background = Brushes.RoyalBlue;
                    symbol = "i";
                    break;
            }

            if (icon == MessageBoxImage.Warning)
            {
                var triangle = new Polygon
                {
                    Fill = background,
                    Points = new PointCollection
                    {
                        new Point(22, 1),
                        new Point(43, 41),
                        new Point(1, 41)
                    }
                };
                canvas.Children.Add(triangle);
            }
            else
            {
                canvas.Children.Add(new Ellipse
                {
                    Width = 42,
                    Height = 42,
                    Fill = background
                });
            }

            var symbolText = new TextBlock
            {
                Text = symbol,
                Foreground = foreground,
                FontSize = icon == MessageBoxImage.Information ? 28 : 30,
                FontWeight = FontWeights.Bold,
                Width = 42,
                Height = 42,
                TextAlignment = TextAlignment.Center
            };
            Canvas.SetTop(symbolText, icon == MessageBoxImage.Warning ? 6 : 1);
            canvas.Children.Add(symbolText);
            return canvas;
        }

        private static Window ResolveOwner(object owner)
        {
            if (owner is Window explicitOwner)
            {
                return explicitOwner;
            }

            Application application = Application.Current;
            if (application == null)
            {
                return null;
            }

            foreach (Window candidate in application.Windows)
            {
                if (candidate.IsVisible && candidate.IsActive)
                {
                    return candidate;
                }
            }

            if (application.MainWindow != null && application.MainWindow.IsVisible)
            {
                return application.MainWindow;
            }

            foreach (Window candidate in application.Windows)
            {
                if (candidate.IsVisible)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static Point GetDialogPosition(Window owner, double width, double height)
        {
            if (owner != null)
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(owner);
                Point origin = GetOwnerScreenOrigin(owner);
                if (IsFinite(origin.X) && IsFinite(origin.Y))
                {
                    double ownerWidth = GetOwnerDimension(owner, useWidth: true, width) * dpi.DpiScaleX;
                    double ownerHeight = GetOwnerDimension(owner, useWidth: false, height) * dpi.DpiScaleY;
                    return GetCenteredPosition(
                        origin.X,
                        origin.Y,
                        ownerWidth,
                        ownerHeight,
                        width * dpi.DpiScaleX,
                        height * dpi.DpiScaleY);
                }
            }

            Rect workArea = SystemParameters.WorkArea;
            return GetCenteredPosition(
                workArea.Left,
                workArea.Top,
                workArea.Width,
                workArea.Height,
                width,
                height);
        }

        private static Point GetOwnerScreenOrigin(Window owner)
        {
            if (owner == null)
            {
                return new Point(double.NaN, double.NaN);
            }

            try
            {
                Point origin = owner.PointToScreen(new Point());
                if (IsFinite(origin.X) && IsFinite(origin.Y))
                {
                    return origin;
                }
            }
            catch (InvalidOperationException)
            {
                // Fall back to explicitly requested coordinates below.
            }

            if (IsFinite(owner.Left) && IsFinite(owner.Top))
            {
                DpiScale dpi = VisualTreeHelper.GetDpi(owner);
                return new Point(owner.Left * dpi.DpiScaleX, owner.Top * dpi.DpiScaleY);
            }

            return new Point(double.NaN, double.NaN);
        }

        private static double EstimateDialogHeight(string messageBoxText)
        {
            int estimatedLines = 1;
            int lineLength = 0;
            string text = messageBoxText ?? string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    estimatedLines += Math.Max(1, (lineLength + EstimatedCharactersPerLine - 1) / EstimatedCharactersPerLine);
                    lineLength = 0;
                }
                else
                {
                    lineLength++;
                }
            }

            estimatedLines += Math.Max(0, (lineLength - 1) / EstimatedCharactersPerLine);
            double estimatedHeight = 142 + (estimatedLines * EstimatedLineHeight);
            return Math.Max(MinimumDialogHeight, Math.Min(MaximumDialogHeight, estimatedHeight));
        }

        private static MessageBoxResult GetCloseResult(
            MessageBoxResult[] buttonResults,
            MessageBoxResult defaultResult)
        {
            for (int i = 0; i < buttonResults.Length; i++)
            {
                if (buttonResults[i] == MessageBoxResult.Cancel)
                {
                    return MessageBoxResult.Cancel;
                }
            }

            return defaultResult;
        }

        private static string GetButtonText(MessageBoxResult result)
        {
            switch (result)
            {
                case MessageBoxResult.OK:
                    return "_OK";
                case MessageBoxResult.Cancel:
                    return "_Cancel";
                case MessageBoxResult.Abort:
                    return "_Abort";
                case MessageBoxResult.Retry:
                    return "_Retry";
                case MessageBoxResult.Ignore:
                    return "_Ignore";
                case MessageBoxResult.Yes:
                    return "_Yes";
                case MessageBoxResult.No:
                    return "_No";
                case MessageBoxResult.TryAgain:
                    return "_Try Again";
                case MessageBoxResult.Continue:
                    return "_Continue";
                default:
                    return result.ToString();
            }
        }

        private static double GetOwnerDimension(Window owner, bool useWidth, double fallback)
        {
            double actual = useWidth ? owner.ActualWidth : owner.ActualHeight;
            if (IsFinite(actual) && actual > 0)
            {
                return actual;
            }

            double rendered = useWidth ? owner.RenderSize.Width : owner.RenderSize.Height;
            if (IsFinite(rendered) && rendered > 0)
            {
                return rendered;
            }

            if (owner.Content is FrameworkElement content)
            {
                double contentActual = useWidth ? content.ActualWidth : content.ActualHeight;
                if (IsFinite(contentActual) && contentActual > 0)
                {
                    return contentActual;
                }

                double contentRendered = useWidth ? content.RenderSize.Width : content.RenderSize.Height;
                if (IsFinite(contentRendered) && contentRendered > 0)
                {
                    return contentRendered;
                }
            }

            double requested = useWidth ? owner.Width : owner.Height;
            if (IsFinite(requested) && requested > 0)
            {
                return requested;
            }

            return fallback;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}

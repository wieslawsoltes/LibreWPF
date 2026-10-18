// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shell;
using ProGPU.Wpf.Interop;

namespace System.Windows;

public partial class PortableWindowActivationServiceTests
{
    [PortableInputFact]
    public void OwnedFixedCaptionPreservesContentAndClientFrameWithOneCloseControl()
    {
        RunInUiApartment(() => WithCaptionWindow(true, (window, host, styles) =>
        {
            var originalContent = window.Content;
            var caption = Caption(window);
            Assert.NotNull(caption);
            Assert.Same(originalContent, window.Content);
            Assert.Equal(WindowStyle.SingleBorderWindow, window.WindowStyle);
            Assert.Equal(WindowStyle.None, styles.Last());
            Assert.Single(caption.Children.OfType<Button>());
            Assert.Equal("Original title", Assert.Single(caption.Children.OfType<TextBlock>()).Text);
            var template = (UIElement)VisualTreeHelper.GetChild(window, 0);
            Assert.Same(originalContent, Assert.IsType<ContentPresenter>(template).Content);
            Assert.Equal(caption.RenderSize.Height, VisualTreeHelper.GetOffset(template).Y);
            Assert.Equal(window.RenderSize.Height - caption.RenderSize.Height, template.RenderSize.Height);
            Assert.Equal(window.RenderSize.Width, template.RenderSize.Width);
            Assert.Equal(new Size(200, 140), window.RenderSize);
            Assert.True(PortableWindowActivationService.TryGetFrameInsets(window.PortableWindowActivation, out var frame));
            Assert.Equal(default, frame);
            window.Title = "Updated title";
            window.UpdateLayout();
            Assert.Equal("Updated title", Assert.Single(caption.Children.OfType<TextBlock>()).Text);
        }));
    }

    [PortableInputFact]
    public void OwnedFixedCaptionFollowsStyleResizeAndApplicationChromeChanges()
    {
        RunInUiApartment(() => WithCaptionWindow(true, (window, host, styles) =>
        {
            Assert.NotNull(Caption(window));
            window.ResizeMode = ResizeMode.CanMinimize;
            Assert.Null(Caption(window));
            Assert.Equal(WindowStyle.SingleBorderWindow, styles.Last());
            window.ResizeMode = ResizeMode.NoResize;
            Assert.NotNull(Caption(window));
            window.WindowStyle = WindowStyle.None;
            Assert.Null(Caption(window));
            window.WindowStyle = WindowStyle.SingleBorderWindow;
            Assert.NotNull(Caption(window));
            WindowChrome.SetWindowChrome(window, new WindowChrome());
            Assert.Null(Caption(window));
            Assert.Equal(WindowStyle.None, styles.Last());
            WindowChrome.SetWindowChrome(window, null);
            Assert.NotNull(Caption(window));
            Assert.Equal(WindowStyle.None, styles.Last());
        }));
    }

    [PortableInputFact]
    public void OwnedCaptionCloseUsesCancelableWindowCloseAndRetainsSourceContent()
    {
        RunInUiApartment(() => WithCaptionWindow(true, (window, host, styles) =>
        {
            var content = window.Content;
            var close = Assert.Single(Caption(window)!.Children.OfType<Button>());
            window.IsPortableCaptionCloseEnabled = false;
            Assert.False(close.IsEnabled);
            window.IsPortableCaptionCloseEnabled = true;
            Assert.True(close.IsEnabled);
            // Theme-disabled foreground must reach the vector X, rather than
            // retaining a locally fixed enabled text brush.
            close.Foreground = Brushes.Gray;
            Assert.Same(close.Foreground, Assert.IsType<System.Windows.Shapes.Path>(close.Content).Stroke);
            bool cancel = true;
            int requests = 0;
            window.Closing += (_, e) => { requests++; e.Cancel = cancel; };
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, requests);
            Assert.False(window.IsDisposed);
            Assert.Same(content, window.Content);
            cancel = false;
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, requests);
            Assert.True(window.IsDisposed);
        }));
    }

    [PortableInputFact]
    public void AbsentHostCaptionCapabilityPreservesNativeWindowRoute()
    {
        RunInUiApartment(() => WithCaptionWindow(false, (window, host, styles) =>
        {
            Assert.Null(Caption(window));
            window.ResizeMode = ResizeMode.CanResize;
            window.ResizeMode = ResizeMode.NoResize;
            Assert.Null(Caption(window));
            Assert.All(styles, style => Assert.Equal(WindowStyle.SingleBorderWindow, style));
        }));
    }

    private static PortableFixedWindowCaption? Caption(Window window)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(window); i++)
            if (VisualTreeHelper.GetChild(window, i) is PortableFixedWindowCaption caption) return caption;
        return null;
    }

    [PortableInputFact]
    public void CaptionHostQueryCannotPublishAgainstReenteredSourceStyle()
    {
        RunInUiApartment(() =>
        {
            int queries = 0;
            WithCaptionWindow(true, (window, host, styles) =>
            {
                Assert.Null(Caption(window));
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.All(styles, style => Assert.Equal(WindowStyle.None, style));
                window.WindowStyle = WindowStyle.SingleBorderWindow;
                Assert.NotNull(Caption(window));
            }, window =>
            {
                if (++queries == 1) window.WindowStyle = WindowStyle.None;
                return true;
            });
        });
    }

    [PortableInputFact]
    public void FixedCaptionSizeToContentIncludesOwnedCaptionOnce()
    {
        RunInUiApartment(() => WithCaptionWindow(true, (window, host, styles) =>
        {
            var content = (Border)window.Content;
            content.Width = 80;
            content.Height = 40;
            window.Width = double.NaN;
            window.Height = double.NaN;
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var caption = Caption(window)!;
            var template = (UIElement)VisualTreeHelper.GetChild(window, 0);
            Assert.Equal(template.DesiredSize.Height + caption.DesiredSize.Height, window.DesiredSize.Height);
            Assert.Equal(Math.Max(template.DesiredSize.Width, caption.DesiredSize.Width), window.DesiredSize.Width);
            Assert.Same(content, window.Content);
        }));
    }

    [PortableInputFact]
    public void CaptionClosePolicyReachesSameHostAndClearingRestoresEnabledState()
    {
        RunInUiApartment(() =>
        {
            foreach (bool sourceCaption in new[] { false, true })
            {
                var values = new List<(Window Owner, bool Enabled)>();
                WithCaptionWindow(sourceCaption, (window, host, styles) =>
                {
                    Assert.NotEmpty(values);
                    Assert.All(values, value => { Assert.Same(window, value.Owner); Assert.False(value.Enabled); });
                    window.ClearValue(Window.IsPortableCaptionCloseEnabledProperty);
                    Assert.True(values.Last().Enabled);
                    Assert.Same(window, values.Last().Owner);
                    window.IsPortableCaptionCloseEnabled = false;
                    Assert.False(values.Last().Enabled);
                    if (sourceCaption) Assert.False(Assert.Single(Caption(window)!.Children.OfType<Button>()).IsEnabled);
                }, closePolicy: (window, enabled) => values.Add((window, enabled)), initialCloseEnabled: false);
            }
            int ordinaryRequests = 0;
            WithCaptionWindow(false, (_, _, _) => Assert.Equal(0, ordinaryRequests),
                closePolicy: (_, _) => ordinaryRequests++);
        });
    }

    [PortableInputFact]
    public void LegacyCaptionRegistrationCannotRetainPriorCloseCapability()
    {
        RunInUiApartment(() =>
        {
            object activation = new();
            int calls = 0;
            try
            {
                PortableWindowActivationService.RegisterCallbacks(new PortableWindowActivationCallbacks(value => value)
                {
                    SetCloseButtonEnabled = (owner, _) => { Assert.Same(activation, owner); calls++; return true; }
                });
                Assert.True(PortableWindowActivationService.SetCloseButtonEnabled(activation, false));
                PortableWindowActivationService.Register(value => value);
                Assert.False(PortableWindowActivationService.SetCloseButtonEnabled(activation, false));
                Assert.Equal(1, calls);
            }
            finally { PortableWindowActivationService.Clear(); }
        });
    }

    private static void WithCaptionWindow(bool enabled,
        Action<Window, IPortablePresentationSourceHost, List<WindowStyle>> test,
        Func<Window, bool>? query = null,
        Action<Window, bool>? closePolicy = null,
        bool? initialCloseEnabled = null)
    {
        using IPortablePresentationSourceHost host = PortablePresentationSourceHost.Create();
        var window = new Window
        {
            Width = 200, Height = 140, ResizeMode = ResizeMode.NoResize,
            Title = "Original title", Content = new Border { Background = Brushes.White }
        };
        // This source-only fixture has no packaged native theme dictionary.
        // Supply an ordinary Window template so the original Content really
        // participates in layout; a caption-only visual tree is not evidence.
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty,
            new Binding(nameof(Window.Content)) { RelativeSource = RelativeSource.TemplatedParent });
        window.Template = new ControlTemplate(typeof(Window)) { VisualTree = presenter };
        var styles = new List<WindowStyle>();
        if (initialCloseEnabled is { } initial) window.IsPortableCaptionCloseEnabled = initial;
        PortableWindowActivationService.RegisterCallbacks(new PortableWindowActivationCallbacks(activate: value => value,
            getHandle: _ => host.Handle,
            setWindowBorder: (_, _, style) => styles.Add((WindowStyle)style))
        {
            GetFrameInsets = _ => default(PortableWindowFrameInsets),
            RequiresSourceFixedCaption = enabled ? source => query?.Invoke((Window)source) ?? true : null,
            SetCloseButtonEnabled = closePolicy == null ? null : (source, value) =>
            {
                closePolicy((Window)source, value);
                return true;
            }
        });
        try
        {
            window.Show();
            host.RootVisual = window;
            host.SetClientSize(200, 140);
            window.Measure(new Size(200, 140));
            window.Arrange(new Rect(0, 0, 200, 140));
            window.UpdateLayout();
            test(window, host, styles);
        }
        finally
        {
            host.RootVisual = null;
            if (!window.IsDisposed) window.Close();
            PortableWindowActivationService.Clear();
        }
    }
}

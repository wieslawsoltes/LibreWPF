// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows;

[Collection("Sequential")]
public class PortableMessageBoxModalTests
{
    private const string ChildMarker = "LIBREWPF_MESSAGEBOX_MODAL_CHILD";
    private const string CompletionMarker = "Public MessageBox modal contracts passed: explicit/inferred/resolved owner and short/scrollable long content.";

    [PortableMessageBoxFact]
    public void PublicMessageBoxBlocksItsOwnerUntilTheActualDialogCloses()
    {
        // Application can be created only once per process, including after Shutdown.
        // Keep its lifetime out of the shared unit-test process without replacing
        // MessageBox.Show, PortableMessageBoxDialog, Window.ShowDialog or their state.
        if (Environment.GetEnvironmentVariable(ChildMarker) != "1")
        {
            RunIsolated();
            return;
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RunApplication(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The actual MessageBox source dialog did not return.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Console.WriteLine(CompletionMarker);
    }

    private static void RunIsolated()
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The test host path is unavailable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(PortableMessageBoxModalTests).Assembly.Location);
        start.ArgumentList.Add("--filter-class");
        start.ArgumentList.Add(typeof(PortableMessageBoxModalTests).FullName!);
        start.ArgumentList.Add("--minimum-expected-tests");
        start.ArgumentList.Add("1");
        start.ArgumentList.Add("--timeout");
        start.ArgumentList.Add("30s");
        start.ArgumentList.Add("--no-progress");
        start.ArgumentList.Add("--exit-on-process-exit");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("--fail-skips");
        start.ArgumentList.Add("on");
        start.Environment[ChildMarker] = "1";
        start.Environment["LIBREWPF_TEST_MEDIA_BACKEND"] = "Portable";
        using Process child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the isolated MessageBox test.");
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try
        {
            Assert.True(child.WaitForExit(40_000), "The isolated MessageBox test exceeded 40 seconds.");
            string output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            Assert.True(child.ExitCode == 0, output);
            Assert.Contains(CompletionMarker, output);
            Console.WriteLine(output);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
            }
        }
    }

    private static void RunApplication()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var owner = new Window
        {
            Title = "MessageBox source owner", Width = 800, Height = 500,
            Left = 100, Top = 80, Background = Brushes.White, AllowDrop = true,
            Content = new Border { Background = Brushes.White }
        };
        application.MainWindow = owner;
        var sources = new Dictionary<Window, IPortablePresentationSourceHost>();
        int ownerActivationRequests = 0, ownerMouseReports = 0, ownerDrops = 0;
        int dialogRuns = 0, dialogCloses = 0, dialogDisposals = 0;
        string expectedMessage = string.Empty;
        bool ownerInputAllowed = true;
        bool observingOwnerInput = false;
        // Observe the real source ingress, not renderer-owned hit selection. This
        // source fixture has no GPU scene or native client window to select a hit.
        PreProcessInputEventHandler observeOwnerInput = (_, _) =>
        {
            if (observingOwnerInput) ownerMouseReports++;
        };
        void DeliverOwnerInput(PortableInputEventArgs input)
        {
            observingOwnerInput = true;
            try { PortableWindowActivationService.ProcessInput((PresentationSource)sources[owner], input); }
            finally { observingOwnerInput = false; }
        }
        InputManager.Current.PreProcessInput += observeOwnerInput;
        owner.Drop += (_, e) => { ownerDrops++; e.Effects = DragDropEffects.Copy; e.Handled = true; };
        using var ownerAdmission = PortableModalInputScope.RegisterWindow(owner, allowed => ownerInputAllowed = allowed);
        PortableWindowActivationService.Register(
            activate: value =>
            {
                Window window = Assert.IsType<Window>(value);
                sources.Add(window, PortablePresentationSourceHost.Create());
                return window;
            },
            getHandle: value => sources[(Window)value].Handle,
            show: value => sources[(Window)value].RootVisual = (Window)value,
            requestActivation: value =>
            {
                if (ReferenceEquals(value, owner)) ownerActivationRequests++;
                return true;
            },
            close: value => { if (!ReferenceEquals(value, owner)) dialogCloses++; },
            dispose: value =>
            {
                Window window = (Window)value;
                sources[window].RootVisual = null;
                sources[window].Dispose();
                sources.Remove(window);
                if (!ReferenceEquals(window, owner)) dialogDisposals++;
            },
            run: _ => throw new InvalidOperationException("MessageBox must not enter the application host loop."),
            releaseDialog: (_, completed) => completed(),
            runDialog: (value, shouldContinue) =>
            {
                Window dialog = Assert.IsType<Window>(value);
                Assert.NotSame(owner, dialog);
                dialogRuns++;
                var frame = new DispatcherFrame();
                Exception? callbackFailure = null;
                dialog.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
                {
                    try
                    {
                        Assert.True(shouldContinue());
                        Assert.True(dialog.IsVisible);
                        Assert.Same(owner, dialog.Owner);
                        Assert.Contains(dialog, application.Windows.Cast<Window>());
                        Assert.True(ComponentDispatcher.IsThreadModal);
                        Assert.True(PortableModalInputScope.IsActive);
                        Assert.True(PortableWindowActivationService.IsModalInputAllowed(dialog));
                        Assert.False(PortableWindowActivationService.IsModalInputAllowed(owner));
                        Assert.False(ownerInputAllowed);
                        Assert.True(owner.IsEnabled); // Modality must not overwrite application intent.
                        Assert.Equal(ResizeMode.NoResize, dialog.ResizeMode);
                        Assert.False(dialog.ShowInTaskbar);

                        int activations = ownerActivationRequests, mouseReports = ownerMouseReports, drops = ownerDrops;
                        Assert.False(PortableWindowActivationService.TryActivateInputOwner((PresentationSource)sources[owner]));
                        Assert.Equal(activations, ownerActivationRequests);
                        var blocked = new PortableInputEventArgs(PortableInputEventKind.MouseDown,
                            x: 100, y: 100, button: PortableMouseButton.Left);
                        DeliverOwnerInput(blocked);
                        Assert.True(blocked.Handled);
                        Assert.Equal(mouseReports, ownerMouseReports);
                        Assert.Equal(0, DeliverDrop(owner));
                        Assert.Equal(drops, ownerDrops);

                        // Click the real generated button. This runs the production
                        // selected-result callback and Window.DialogResult close path.
                        Grid content = Assert.IsType<Grid>(dialog.Content);
                        ScrollViewer messageViewport = Assert.IsType<ScrollViewer>(content.Children[0]);
                        Assert.Equal(0, Grid.GetRow(messageViewport));
                        Assert.Equal(ScrollBarVisibility.Auto, messageViewport.VerticalScrollBarVisibility);
                        Assert.Equal(ScrollBarVisibility.Disabled, messageViewport.HorizontalScrollBarVisibility);
                        Assert.False(messageViewport.CanContentScroll);
                        Grid messageArea = Assert.IsType<Grid>(messageViewport.Content);
                        TextBlock message = Assert.IsType<TextBlock>(Assert.Single(messageArea.Children.Cast<UIElement>()));
                        Assert.Equal(expectedMessage, message.Text);
                        Assert.Equal(TextWrapping.Wrap, message.TextWrapping);
                        StackPanel buttons = Assert.IsType<StackPanel>(content.Children[1]);
                        Assert.Equal(1, Grid.GetRow(buttons));
                        Assert.True(content.RowDefinitions[1].Height.IsAuto);
                        Assert.Equal(3, buttons.Children.Count);
                        Button selected = Assert.IsType<Button>(buttons.Children[1]);
                        Assert.Equal("_No", selected.Content);
                        Assert.True(selected.IsDefault);

                        // This source-only host has no native window theme. Lay
                        // out the actual dialog content at its bounded size;
                        // ScrollViewer keeps its real default control template.
                        content.Measure(new Size(dialog.Width, dialog.Height));
                        content.Arrange(new Rect(0, 0, dialog.Width, dialog.Height));
                        content.UpdateLayout();
                        Assert.True(messageViewport.ViewportHeight > 0);
                        Assert.True(selected.ActualHeight > 0);
                        Point buttonPosition = selected.TranslatePoint(new Point(), content);
                        Assert.InRange(buttonPosition.Y, 0, content.ActualHeight - selected.ActualHeight);
                        if (expectedMessage.Contains('\n'))
                        {
                            Assert.True(messageViewport.ScrollableHeight > 0);
                            messageViewport.ScrollToBottom();
                            content.UpdateLayout();
                            Assert.Equal(messageViewport.ScrollableHeight, messageViewport.VerticalOffset);
                            Assert.Equal(buttonPosition, selected.TranslatePoint(new Point(), content));
                        }
                        else
                        {
                            Assert.Equal(0, messageViewport.ScrollableHeight);
                        }
                        selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, selected));
                        Assert.False(shouldContinue());
                        Assert.True(dialog.IsDisposed);
                    }
                    catch (Exception exception) { callbackFailure = exception; }
                    finally
                    {
                        if (!dialog.IsDisposed) dialog.Close();
                        frame.Continue = false;
                    }
                }));
                Dispatcher.PushFrame(frame);
                if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                Assert.False(shouldContinue());
            });
        try
        {
            owner.Show();
            sources[owner].SetClientSize(800, 500);
            PortableWindowActivationService.SetActivationState(owner, true);
            Assert.True(MessageBox.UsesPortableBackend(owner));
            Assert.True(MessageBox.UsesPortableBackend());
            IntPtr sourceHandle = new WindowInteropHelper(owner).Handle;
            Assert.NotEqual(IntPtr.Zero, sourceHandle);
            Assert.Equal(sources[owner].Handle, sourceHandle);

            int overrides = 0;
            using (PortableMessageBoxService.Register((Func<object, object>)(value =>
            {
                var request = Assert.IsType<PortableMessageBoxRequest>(value);
                Assert.Same(owner, request.Owner);
                Assert.False(PortableModalInputScope.IsActive);
                overrides++;
                return MessageBoxResult.Yes;
            })))
            {
                // Registration and typed owner identity must work on portable
                // Windows too; no native dialog may precede the explicit override.
                Assert.Equal(MessageBoxResult.Yes, MessageBox.Show(owner, "Override", "Override", MessageBoxButton.YesNo));
                Assert.Equal(MessageBoxResult.Yes, MessageBox.ShowCore(sourceHandle,
                    "Override", "Override", MessageBoxButton.YesNo, MessageBoxImage.None, MessageBoxResult.None, 0));
                Assert.Equal(2, overrides);
                Assert.Equal(0, dialogRuns);

                Assert.Throws<System.ComponentModel.InvalidEnumArgumentException>(() => MessageBox.ShowCore(
                    new IntPtr(1234), "Invalid", "Invalid", (MessageBoxButton)999,
                    MessageBoxImage.None, MessageBoxResult.None, 0));
                Assert.Throws<ArgumentException>(() => MessageBox.ShowCore(sourceHandle,
                    "Invalid", "Invalid", MessageBoxButton.OK, MessageBoxImage.None,
                    MessageBoxResult.None, MessageBoxOptions.ServiceNotification));
                Assert.Throws<PlatformNotSupportedException>(() => MessageBox.ShowCore(new IntPtr(1234),
                    "Foreign", "Foreign", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, 0));
                using var detached = PortablePresentationSourceHost.Create();
                Assert.Throws<PlatformNotSupportedException>(() => MessageBox.ShowCore(detached.Handle,
                    "Detached", "Detached", MessageBoxButton.OK, MessageBoxImage.None, MessageBoxResult.None, 0));
                Assert.Equal(2, overrides);
                Assert.Equal(0, dialogRuns);
                Assert.False(PortableModalInputScope.IsActive);
            }

            int fallbacks = 0;
            using var hiddenOwner = new HiddenWindowOwner();
            using var fallback = PortableMessageBoxService.Register((PortableMessageBoxRequest request) =>
            {
                Assert.Same(hiddenOwner.Window, request.Owner);
                Assert.False(hiddenOwner.Window.IsVisible);
                Assert.Null(hiddenOwner.Window.PortableWindowActivation);
                fallbacks++;
                return MessageBoxResult.Cancel;
            });
            Assert.Equal(MessageBoxResult.Cancel, MessageBox.Show(hiddenOwner.Window,
                "Startup", "Startup", MessageBoxButton.OKCancel));
            Assert.Equal(1, fallbacks);
            Assert.Equal(0, dialogRuns);

            foreach (string messageText in new[]
            {
                "Actual source message",
                string.Join("\n", Enumerable.Range(1, 80).Select(index => $"Message line {index}: retained source content."))
            })
            foreach (int ownerKind in new[] { 0, 1, 2 })
            {
                expectedMessage = messageText;
                MessageBoxResult result = ownerKind switch
                {
                    0 => MessageBox.Show(owner, messageText, "Modal source dialog", MessageBoxButton.YesNoCancel,
                        MessageBoxImage.None, MessageBoxResult.No),
                    1 => MessageBox.Show(messageText, "Modal source dialog", MessageBoxButton.YesNoCancel,
                        MessageBoxImage.None, MessageBoxResult.No),
                    _ => MessageBox.ShowCore(sourceHandle, messageText, "Modal source dialog", MessageBoxButton.YesNoCancel,
                        MessageBoxImage.None, MessageBoxResult.No, 0)
                };
                Assert.Equal(MessageBoxResult.No, result);
                Assert.False(ComponentDispatcher.IsThreadModal);
                Assert.False(PortableModalInputScope.IsActive);
                Assert.True(PortableWindowActivationService.IsModalInputAllowed(owner));
                Assert.True(ownerInputAllowed);
                Assert.True(owner.IsEnabled);
                Assert.True(owner.IsVisible);
                Assert.Empty(owner.OwnedWindows.Cast<Window>());
                int activations = ownerActivationRequests;
                Assert.True(PortableWindowActivationService.TryActivateInputOwner((PresentationSource)sources[owner]));
                Assert.Equal(activations + 1, ownerActivationRequests);
                int mouseReports = ownerMouseReports, drops = ownerDrops;
                DeliverOwnerInput(new PortableInputEventArgs(PortableInputEventKind.MouseDown,
                    x: 100, y: 100, button: PortableMouseButton.Left));
                Assert.True(ownerMouseReports > mouseReports, "Admitted owner input must reach InputManager preprocessing.");
                DeliverOwnerInput(new PortableInputEventArgs(PortableInputEventKind.MouseUp,
                    x: 100, y: 100, button: PortableMouseButton.Left));
                Assert.Equal((int)DragDropEffects.Copy, DeliverDrop(owner));
                Assert.Equal(drops + 1, ownerDrops);
            }
            Assert.Equal(6, dialogRuns);
            Assert.Equal(6, dialogCloses);
            Assert.Equal(6, dialogDisposals);
            Assert.Equal(1, fallbacks); // A live source dialog takes priority over fallback.
        }
        finally
        {
            InputManager.Current.PreProcessInput -= observeOwnerInput;
            foreach (Window window in application.Windows.Cast<Window>().ToArray())
                if (!window.IsDisposed) window.Close();
            foreach (IPortablePresentationSourceHost source in sources.Values) source.Dispose();
            PortableWindowActivationService.Clear();
            application.Shutdown();
        }
    }

    private static int DeliverDrop(Window window) => PortableWindowActivationService.ProcessDragDropEvent(
        window, 0, Array.Empty<string>(), "source drag text", 100, 100, 1, 1);

    private sealed class HiddenWindowOwner : IDisposable
    {
        internal Window Window { get; } = new Window();
        public void Dispose() { if (!Window.IsDisposed) Window.Close(); }
    }

    private sealed class PortableMessageBoxFactAttribute : FactAttribute
    {
        public PortableMessageBoxFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Select LIBREWPF_TEST_MEDIA_BACKEND=Portable for the portable source contract.";
        }
    }
}

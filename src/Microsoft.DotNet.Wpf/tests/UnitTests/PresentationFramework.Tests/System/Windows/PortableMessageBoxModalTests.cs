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
    private const string CompletionMarker = "Public MessageBox modal contracts passed: explicit owner and inferred main window.";

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
                        StackPanel buttons = Assert.IsType<StackPanel>(content.Children[1]);
                        Assert.Equal(3, buttons.Children.Count);
                        Button selected = Assert.IsType<Button>(buttons.Children[1]);
                        Assert.Equal("_No", selected.Content);
                        Assert.True(selected.IsDefault);
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
            foreach (bool explicitOwner in new[] { true, false })
            {
                MessageBoxResult result = explicitOwner
                    ? MessageBox.Show(owner, "Actual source message", "Modal source dialog", MessageBoxButton.YesNoCancel,
                        MessageBoxImage.None, MessageBoxResult.No)
                    : MessageBox.Show("Actual source message", "Modal source dialog", MessageBoxButton.YesNoCancel,
                        MessageBoxImage.None, MessageBoxResult.No);
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
            Assert.Equal(2, dialogRuns);
            Assert.Equal(2, dialogCloses);
            Assert.Equal(2, dialogDisposals);
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

    private sealed class PortableMessageBoxFactAttribute : FactAttribute
    {
        public PortableMessageBoxFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
        {
            if (OperatingSystem.IsWindows()) Skip = "Windows MessageBox keeps its native user32 route; this source contract is non-Windows.";
            else if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable)
                Skip = "Select LIBREWPF_TEST_MEDIA_BACKEND=Portable for the portable source contract.";
        }
    }
}

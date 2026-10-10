// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ProGPU.Wpf.ModalInteractionApp;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? directory = null, run = null;
        bool nativeModal = false;
        for (int i = 0; i < e.Args.Length; ++i)
        {
            switch (e.Args[i])
            {
                case "--evidence-directory" when directory == null && i + 1 < e.Args.Length: directory = e.Args[++i]; break;
                case "--run-id" when run == null && i + 1 < e.Args.Length: run = e.Args[++i]; break;
                case "--libre-native-modal-sessions" when !nativeModal && OperatingSystem.IsMacOS(): nativeModal = true; break;
                default: throw new ArgumentException("Unknown, duplicate, incomplete, or unsupported modal acceptance argument.");
            }
        }
        if (directory == null || run == null || !Guid.TryParseExact(run, "N", out _))
            throw new ArgumentException("A new evidence directory and N-format run GUID are required.");
#if LIBREWPF_MODAL_PORTABLE
        if (OperatingSystem.IsMacOS() != nativeModal)
            throw new ArgumentException("The macOS acceptance workload requires the explicit native-modal startup option only on macOS.");
#else
        if (!OperatingSystem.IsWindows() || nativeModal)
            throw new PlatformNotSupportedException("The original Microsoft workload requires Windows and its unchanged modal policy.");
#endif
        var owner = new ModalWindow(run);
        MainWindow = owner;
        var observer = new ModalObservation(owner, directory, run, nativeModal);
        owner.Closed += (_, _) => observer.Dispose();
        owner.Show();
    }
}

internal sealed class ModalWindow : Window
{
    internal readonly Dictionary<string, FrameworkElement> Targets = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
    internal readonly TextBox Editor = new() { Name = "OwnerEditor", Text = "owner" };
    internal readonly ComboBox Combo = new() { Name = "OwnerCombo", ItemsSource = new[] { "First", "Second", "Third" }, SelectedIndex = 0 };
    internal readonly Popup Popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    internal readonly ContextMenu Menu = new();
    internal Window? Dialog;
    internal string Phase = "owner";
    internal string? MessageResult;
    internal bool? DialogResultObserved;
    internal string? PriorFocus;
    internal string? ReturnedFocus;
    internal readonly string Run;
    internal ModalObservation? Observer;

    internal ModalWindow(string run)
    {
        Run = run;
        Name = "OwnerWindow";
        Title = "WpfModalInteractionApp [" + run + "]";
        Width = 840; Height = 580; Left = 80; Top = 80;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        var panel = new Canvas { Background = Brushes.White };
        Content = panel;
        Add(panel, "editor", Editor, 20, 20, 180, 36);
        var message = Button(panel, "message", "MessageBox", 20, 80);
        var dialog = Button(panel, "dialog", "ShowDialog", 20, 135);
        var guard = Button(panel, "guard", "Owner input guard", 20, 420);
        var finish = Button(panel, "finish", "Finish", 600, 420);
        finish.Click += (_, _) => Close();
        var popup = Button(panel, "popup", "Open Popup", 20, 190);
        Add(panel, "combo", Combo, 20, 245, 180, 36);
        var tooltip = Button(panel, "tooltip", "Hover ToolTip", 20, 300);
        tooltip.ToolTip = new ToolTip { Content = "Original tooltip", Name = "OwnerToolTip" };
        ToolTipService.SetInitialShowDelay(tooltip, 250);
        ToolTipService.SetShowDuration(tooltip, 1500);
        tooltip.ToolTipOpening += (_, _) => Count("tooltip-opening");
        tooltip.ToolTipClosing += (_, _) => Count("tooltip-closing");
        var menuItem = new MenuItem { Header = "Context action", Name = "ContextAction" };
        menuItem.Click += (_, _) => Count("context-action");
        Menu.Items.Add(menuItem);
        Editor.ContextMenu = Menu;
        Menu.Opened += (_, _) => Count("context-opened");
        Menu.Closed += (_, _) => Count("context-closed");
        var popupAction = new Button { Content = "Popup action", Width = 180, Height = 45, Name = "PopupAction" };
        popupAction.Click += (_, _) => { Count("popup-action"); Popup.IsOpen = false; };
        Popup.Child = popupAction;
        Popup.PlacementTarget = popup;
        Popup.Opened += (_, _) => Count("popup-opened");
        Popup.Closed += (_, _) => Count("popup-closed");
        popup.Click += (_, _) => Popup.IsOpen = true;
        guard.Click += (_, _) => Count("guard-action");
        Combo.DropDownOpened += (_, _) => Count("combo-opened");
        Combo.DropDownClosed += (_, _) => Count("combo-closed");
        Combo.SelectionChanged += (_, _) => Count("combo-selection");
        message.Click += (_, _) => ShowMessage();
        dialog.Click += (_, _) => ShowActualDialog();
        Editor.TextChanged += (_, _) => Count("editor-text");
        PreviewMouseDown += (_, _) => Count("owner-pointer");
        PreviewKeyDown += (_, _) => Count("owner-key");
        Activated += (_, _) => Count("owner-activated");
        Deactivated += (_, _) => Count("owner-deactivated");
    }

    internal void Count(string name) => Counts[name] = Counts.GetValueOrDefault(name) + 1;
    internal static string? FocusName() => (Keyboard.FocusedElement as FrameworkElement)?.Name;
    private void ShowMessage()
    {
        PriorFocus = FocusName();
        Phase = "message";
        Count("message-open-request");
        Observer!.Snapshot();
        MessageResult = MessageBox.Show(this, "Accept with Enter after the owner input attempt.",
            Title + " MessageBox", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.OK).ToString();
        ReturnedFocus = FocusName();
        Count("message-returned");
        Phase = "owner";
        Observer.Snapshot();
    }

    private void ShowActualDialog()
    {
        PriorFocus = FocusName();
        var child = new Window { Name = "ModalWindow", Title = Title + " Dialog", Owner = this,
            Width = 340, Height = 230, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize };
        var panel = new Canvas { Background = Brushes.White };
        child.Content = panel;
        var edit = new TextBox { Name = "DialogEditor", Text = "dialog" };
        Add(panel, "dialog-editor", edit, 20, 20, 220, 36);
        var accept = Button(panel, "dialog-accept", "Accept dialog", 20, 90);
        accept.Click += (_, _) => { Count("dialog-accept-action"); child.DialogResult = true; };
        child.PreviewMouseDown += (_, _) => Count("dialog-pointer");
        child.PreviewKeyDown += (_, _) => Count("dialog-key");
        edit.TextChanged += (_, _) => Count("dialog-text");
        child.Loaded += (_, _) => Count("dialog-loaded");
        child.Closing += (_, _) => Count("dialog-closing");
        child.Closed += (_, _) => Count("dialog-closed");
        Dialog = child;
        Phase = "dialog";
        Count("dialog-open-request");
        Observer!.Snapshot();
        DialogResultObserved = child.ShowDialog();
        ReturnedFocus = FocusName();
        Count("dialog-returned");
        Phase = "owner";
        Dialog = null;
        Observer.Snapshot();
    }

    private Button Button(Canvas panel, string key, string text, double x, double y)
    {
        var value = new Button { Name = key.Replace('-', '_'), Content = text };
        Add(panel, key, value, x, y, 180, 36);
        value.PreviewMouseDown += (_, _) => Count(key + "-pointer");
        value.Click += (_, _) => Count(key + "-click");
        return value;
    }

    private void Add(Canvas panel, string key, FrameworkElement value, double x, double y, double width, double height)
    {
        Targets.Add(key, value);
        value.Width = width; value.Height = height;
        Canvas.SetLeft(value, x); Canvas.SetTop(value, y);
        panel.Children.Add(value);
    }
}

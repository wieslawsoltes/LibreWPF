using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using System.Windows.Threading;
using Xceed.Wpf.AvalonDock;
using Xceed.Wpf.AvalonDock.Layout;
using Xceed.Wpf.AvalonDock.Layout.Serialization;
using Xceed.Wpf.AvalonDock.Themes;
using Xceed.Wpf.Toolkit;
using Xceed.Wpf.Toolkit.Core;
using Xceed.Wpf.Toolkit.Primitives;
using Xceed.Wpf.Toolkit.PropertyGrid;
using Xceed.Wpf.Toolkit.Zoombox;
using AvalonDockAutoHideWindowControl = Xceed.Wpf.AvalonDock.Controls.LayoutAutoHideWindowControl;
using AvalonDockAnchorableItem = Xceed.Wpf.AvalonDock.Controls.LayoutAnchorableItem;
using AvalonDockLayoutAnchorControl = Xceed.Wpf.AvalonDock.Controls.LayoutAnchorControl;
using AvalonDockDocumentItem = Xceed.Wpf.AvalonDock.Controls.LayoutDocumentItem;
using AvalonDockLayoutItem = Xceed.Wpf.AvalonDock.Controls.LayoutItem;
using ToolkitMessageBoxControl = Xceed.Wpf.Toolkit.MessageBox;
using ToolkitRichTextBox = Xceed.Wpf.Toolkit.RichTextBox;
using ToolkitWrapPanel = Xceed.Wpf.Toolkit.Panels.WrapPanel;
using ToolkitZoomboxControl = Xceed.Wpf.Toolkit.Zoombox.Zoombox;

namespace ProGPU.Wpf.ToolkitApp;

public static class ToolkitDockCommands
{
    public static readonly RoutedUICommand ActivateEditor = new(
        "Activate editor",
        nameof(ActivateEditor),
        typeof(ToolkitDockCommands));

    public static readonly RoutedUICommand CloseOverview = new(
        "Close overview",
        nameof(CloseOverview),
        typeof(ToolkitDockCommands));

    public static readonly RoutedUICommand ActivateToolkitPane = new(
        "Activate Toolkit pane",
        nameof(ActivateToolkitPane),
        typeof(ToolkitDockCommands));

    public static readonly RoutedUICommand TogglePropertyPane = new(
        "Toggle property pane",
        nameof(TogglePropertyPane),
        typeof(ToolkitDockCommands));

    public static readonly RoutedUICommand CycleDockContent = new(
        "Cycle dock content",
        nameof(CycleDockContent),
        typeof(ToolkitDockCommands));

    public static readonly RoutedUICommand CycleDockAnchorable = new(
        "Cycle dock anchorable",
        nameof(CycleDockAnchorable),
        typeof(ToolkitDockCommands));

    public static readonly RoutedUICommand CycleAutoHideOverlay = new(
        "Cycle auto-hide overlay",
        nameof(CycleAutoHideOverlay),
        typeof(ToolkitDockCommands));
}

public partial class MainWindow : Window
{
    private const int GpuOwnerBufferCapacity = 64;

    private const string LiveValidationEnvironmentVariable = "PROGPU_WPF_TOOLKIT_LIVE_VALIDATE";
    private const string LiveValidationStatusPathEnvironmentVariable = "PROGPU_WPF_TOOLKIT_LIVE_VALIDATE_STATUS_PATH";
    private const int LiveValidationStartupMaxAttempts = 1200;
    private const int LiveValidationMaxAttempts = 400;
    private static readonly TimeSpan LiveValidationRetryDelay = TimeSpan.FromMilliseconds(16);
    private static readonly string[] AvalonDockThemeNames = ["Aero", "Metro", "VS2010"];
    private readonly ToolkitViewModel _viewModel = new();
    private int _avalonDockThemeIndex;
    private int _avalonDockKeyboardNavigationIndex;
    private int _avalonDockAnchorableKeyboardNavigationIndex;
    private int _avalonDockAutoHideOverlayIndex;
    private bool _liveValidationStarted;

    public MainWindow()
    {
        DataContext = _viewModel;
        InitializeComponent();
        ToolkitChildWindow.FocusedElement = ChildWindowInputTextBox;
        ConfigureToolkitWindowControlPrimitive();
        MagnifierManager.SetMagnifier(ZoomboxContentRoot, ToolkitMagnifier);
        SetAvalonDockTheme(AvalonDockThemeNames[_avalonDockThemeIndex], recordSwitch: false);
        DockManager.ActiveContentChanged += DockManager_ActiveContentChanged;
        DockManager.DocumentClosing += DockManager_DocumentClosing;
        DockManager.DocumentClosed += DockManager_DocumentClosed;
        DockManager.Floated += DockManager_Floated;
        DockManager.Docked += DockManager_Docked;
        DockManager.LayoutChanging += DockManager_LayoutChanging;
        DockManager.LayoutChanged += DockManager_LayoutChanged;
        SourceDockManager.ActiveContentChanged += SourceDockManager_ActiveContentChanged;
        OverviewDocument.Closed += OverviewDocument_Closed;
        PropertyPane.Hiding += PropertyPane_Hiding;
        PropertyPane.IsVisibleChanged += PropertyPane_IsVisibleChanged;
        ActivityPane.Closing += ActivityPane_Closing;
        ActivityPane.Closed += ActivityPane_Closed;
        Loaded += OnToolkitWindowLoaded;
        StartLiveValidationIfRequired();
    }

    private void ConfigureToolkitWindowControlPrimitive()
    {
        ToolkitWindowControl.WindowThickness = new Thickness(2);
        ToolkitWindowControl.AddHandler(WindowControl.HeaderIconClickedEvent, new MouseButtonEventHandler(ToolkitWindowControl_HeaderIconClicked));
        ToolkitWindowControl.AddHandler(WindowControl.HeaderIconDoubleClickedEvent, new MouseButtonEventHandler(ToolkitWindowControl_HeaderIconDoubleClicked));
        ToolkitWindowControl.AddHandler(WindowControl.HeaderMouseLeftButtonDoubleClickedEvent, new MouseButtonEventHandler(ToolkitWindowControl_HeaderMouseLeftButtonDoubleClicked));
        ToolkitWindowControl.AddHandler(WindowControl.HeaderMouseRightButtonClickedEvent, new MouseButtonEventHandler(ToolkitWindowControl_HeaderMouseRightButtonClicked));
    }

    private void AddDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        int index = _viewModel.DocumentCount + 1;
        var document = new ToolkitDocument(
            $"Generated {index}",
            "ProGPU",
            DateTime.Today.AddDays(index),
            $"Generated AvalonDock document {index}.");
        _viewModel.Documents.Add(document);
        _viewModel.SelectedDocument = document;
        _viewModel.Activity.Add($"Added document {index}");

        DocumentPane.Children.Add(
            new LayoutDocument
            {
                ContentId = $"generated-{index}",
                Title = document.Title,
                IconSource = TryFindResource("DocumentIcon") as ImageSource,
                Content = new TextBox
                {
                    Text = document.Body,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(16)
                }
            });

        _viewModel.Status = $"Added {document.Title}";
    }

    private void AddSourceDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        var document = _viewModel.AddSourceDocument();
        SourceDockManager.ActiveContent = document;
        _viewModel.Status = $"Added source {document.Title}";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ActivateSourceToolButton_Click(object sender, RoutedEventArgs e)
    {
        var tool = _viewModel.SourceAnchorables.First();
        SourceDockManager.ActiveContent = tool;
        _viewModel.Status = $"Activated source {tool.Title}";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ExerciseSourceTabGroupsButton_Click(object sender, RoutedEventArgs e)
    {
        ExerciseSourceBackedAvalonDockTabGroupCommands();
    }

    private void ActivateEditorButton_Click(object sender, RoutedEventArgs e)
    {
        ActivateEditorDocument();
    }

    private void ActivateEditorCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        ViewModel.AvalonDockContextMenuCommandCanExecuteCount++;
        e.CanExecute = DocumentPane.Children.Contains(EditorDocument);
        e.Handled = true;
    }

    private void ActivateEditorCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        ActivateEditorDocument();
        RecordAvalonDockContextMenuCommand("ActivateEditor");
        e.Handled = true;
    }

    private void ActivateEditorDocument()
    {
        EditorDocument.IsSelected = true;
        EditorDocument.IsActive = true;
        _viewModel.Status = "Editor document activated";
        _viewModel.Activity.Add("Activated editor document");
    }

    private void CloseOverviewDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        CloseOverviewDocument();
    }

    private void CloseOverviewCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        ViewModel.AvalonDockContextMenuCommandCanExecuteCount++;
        e.CanExecute = DocumentPane.Children.Contains(OverviewDocument);
        e.Handled = true;
    }

    private void CloseOverviewCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        CloseOverviewDocument();
        RecordAvalonDockContextMenuCommand("CloseOverview");
        e.Handled = true;
    }

    private void CloseOverviewDocument()
    {
        if (!DocumentPane.Children.Contains(OverviewDocument))
        {
            _viewModel.Status = "Overview document already closed";
            _viewModel.Activity.Add(_viewModel.Status);
            return;
        }

        OverviewDocument.Close();
        if (DocumentPane.Children.Contains(OverviewDocument))
        {
            return;
        }

        _viewModel.Status = "Overview document closed";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ActivateToolkitPaneCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        ViewModel.AvalonDockAnchorableContextMenuCommandCanExecuteCount++;
        e.CanExecute = ToolkitPane.IsVisible;
        e.Handled = true;
    }

    private void ActivateToolkitPaneCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        ActivateToolkitPane();
        RecordAvalonDockAnchorableContextMenuCommand("ActivateToolkitPane");
        e.Handled = true;
    }

    private void TogglePropertyPaneCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        ViewModel.AvalonDockAnchorableContextMenuCommandCanExecuteCount++;
        e.CanExecute = true;
        e.Handled = true;
    }

    private void TogglePropertyPaneCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        TogglePropertyPane();
        RecordAvalonDockAnchorableContextMenuCommand("TogglePropertyPane");
        e.Handled = true;
    }

    private void ActivateToolkitPane()
    {
        DockManager.ActiveContent = ToolkitPane.Content;
        SelectAvalonDockAnchorable(ToolkitPane);
        ToolkitPane.IsActive = true;
        FocusAvalonDockAnchorableContent(ToolkitPane);
        _viewModel.Status = "Toolkit pane activated";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void CycleDockContentCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        ViewModel.AvalonDockKeyboardNavigationCanExecuteCount++;
        e.CanExecute = DocumentPane.Children.Contains(EditorDocument) &&
            DocumentPane.Children.Contains(OverviewDocument);
        e.Handled = true;
    }

    private void CycleDockContentCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        CycleAvalonDockKeyboardNavigation();
        e.Handled = true;
    }

    private void CycleDockAnchorableCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        ViewModel.AvalonDockAnchorableKeyboardNavigationCanExecuteCount++;
        e.CanExecute = GetKeyboardNavigableAnchorables().Length >= 2;
        e.Handled = true;
    }

    private void CycleDockAnchorableCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        CycleAvalonDockAnchorableKeyboardNavigation();
        e.Handled = true;
    }

    private void CycleAutoHideOverlayCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        ViewModel.AvalonDockAutoHideOverlayCanExecuteCount++;
        e.CanExecute = IsLoaded &&
            GetAutoHideOverlayAnchorables().Any(anchorable => anchorable.IsAutoHidden);
        e.Handled = true;
    }

    private void CycleAutoHideOverlayCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        CycleAvalonDockAutoHideOverlay();
        e.Handled = true;
    }

    internal void CycleAvalonDockKeyboardNavigation()
    {
        LayoutContent[] cycle =
        [
            EditorDocument,
            OverviewDocument
        ];

        int currentIndex = Array.FindIndex(
            cycle,
            content => ReferenceEquals(DockManager.ActiveContent, content.Content) ||
                ReferenceEquals(DockManager.ActiveContent, content) ||
                ReferenceEquals(DockLayoutRoot.ActiveContent, content) ||
                content.IsActive ||
                content.IsSelected);
        if (currentIndex >= 0)
        {
            _avalonDockKeyboardNavigationIndex = currentIndex;
        }

        _avalonDockKeyboardNavigationIndex = (_avalonDockKeyboardNavigationIndex + 1) % cycle.Length;
        LayoutContent nextContent = cycle[_avalonDockKeyboardNavigationIndex];
        DockManager.ActiveContent = nextContent.Content;
        nextContent.IsSelected = true;
        nextContent.IsActive = true;

        ViewModel.AvalonDockKeyboardNavigationCount++;
        ViewModel.LastAvalonDockKeyboardNavigationTarget = nextContent.ContentId ?? nextContent.Title;
        ViewModel.Status = $"Keyboard dock navigation: {nextContent.Title}";
        ViewModel.Activity.Add(ViewModel.Status);
    }

    private LayoutAnchorable[] GetKeyboardNavigableAnchorables()
    {
        return
        [
            ToolkitPane,
            PropertyPane,
            ActivityPane
        ];
    }

    internal void CycleAvalonDockAnchorableKeyboardNavigation()
    {
        LayoutAnchorable[] cycle = GetKeyboardNavigableAnchorables();
        int currentIndex = Array.FindIndex(
            cycle,
            content => string.Equals(
                ViewModel.LastAvalonDockAnchorableKeyboardNavigationTarget,
                content.ContentId ?? content.Title,
                StringComparison.Ordinal));
        if (currentIndex < 0)
        {
            currentIndex = Array.FindIndex(cycle, IsAvalonDockAnchorableActive);
        }

        if (currentIndex < 0)
        {
            currentIndex = Array.FindIndex(cycle, IsAvalonDockAnchorableSelected);
        }

        if (currentIndex >= 0)
        {
            _avalonDockAnchorableKeyboardNavigationIndex = currentIndex;
        }

        _avalonDockAnchorableKeyboardNavigationIndex = (_avalonDockAnchorableKeyboardNavigationIndex + 1) % cycle.Length;
        LayoutAnchorable nextContent = cycle[_avalonDockAnchorableKeyboardNavigationIndex];
        if (nextContent.IsHidden)
        {
            nextContent.Show();
        }

        if (nextContent.IsAutoHidden)
        {
            nextContent.ToggleAutoHide();
        }

        SelectAvalonDockAnchorable(nextContent);
        if (DockManager.GetLayoutItemFromModel(nextContent) is AvalonDockAnchorableItem layoutItem &&
            layoutItem.ActivateCommand?.CanExecute(null) == true)
        {
            layoutItem.ActivateCommand.Execute(null);
            SelectAvalonDockAnchorable(nextContent);
        }
        else
        {
            DockLayoutRoot.ActiveContent = nextContent;
            DockManager.ActiveContent = nextContent;
            nextContent.IsActive = true;
        }

        FocusAvalonDockAnchorableContent(nextContent);

        ViewModel.AvalonDockAnchorableKeyboardNavigationCount++;
        ViewModel.LastAvalonDockAnchorableKeyboardNavigationTarget = nextContent.ContentId ?? nextContent.Title;
        ViewModel.Status = $"Keyboard anchorable navigation: {nextContent.Title}";
        ViewModel.Activity.Add(ViewModel.Status);
    }

    private static void SelectAvalonDockAnchorable(LayoutAnchorable anchorable)
    {
        if (anchorable.Parent is ILayoutContentSelector selector)
        {
            int nextIndex = selector.IndexOf(anchorable);
            if (nextIndex >= 0)
            {
                selector.SelectedContentIndex = nextIndex;
            }
        }

        anchorable.IsSelected = true;
    }

    private bool IsAvalonDockAnchorableSelected(LayoutAnchorable anchorable)
    {
        return anchorable.Parent is ILayoutContentSelector selector
            ? ReferenceEquals(selector.SelectedContent, anchorable)
            : anchorable.IsSelected;
    }

    private bool IsAvalonDockAnchorableActive(LayoutAnchorable anchorable)
    {
        return ReferenceEquals(DockManager.ActiveContent, anchorable.Content) ||
            ReferenceEquals(DockManager.ActiveContent, anchorable) ||
            ReferenceEquals(DockLayoutRoot.ActiveContent, anchorable) ||
            anchorable.IsActive;
    }

    private void FocusAvalonDockAnchorableContent(LayoutAnchorable anchorable)
    {
        UIElement? focusTarget = anchorable switch
        {
            _ when ReferenceEquals(anchorable, ToolkitPane) => PriorityEditor,
            _ when ReferenceEquals(anchorable, PropertyPane) => DocumentPropertyGrid,
            _ when ReferenceEquals(anchorable, ActivityPane) => ActivityList,
            _ => anchorable.Content as UIElement
        };

        if (focusTarget is null)
        {
            return;
        }

        focusTarget.Focus();
        Keyboard.Focus(focusTarget);
    }

    private LayoutAnchorable[] GetAutoHideOverlayAnchorables()
    {
        return
        [
            AgendaPane,
            ContactsPane
        ];
    }

    internal void CycleAvalonDockAutoHideOverlay()
    {
        LayoutAnchorable[] cycle = GetAutoHideOverlayAnchorables();
        int currentIndex = Array.FindIndex(
            cycle,
            content => string.Equals(
                ViewModel.LastAvalonDockAutoHideOverlayTarget,
                content.ContentId ?? content.Title,
                StringComparison.Ordinal));
        if (currentIndex >= 0)
        {
            _avalonDockAutoHideOverlayIndex = currentIndex;
        }

        _avalonDockAutoHideOverlayIndex = (_avalonDockAutoHideOverlayIndex + 1) % cycle.Length;
        LayoutAnchorable nextContent = cycle[_avalonDockAutoHideOverlayIndex];
        ShowAvalonDockAutoHideOverlay(nextContent);

        ViewModel.AvalonDockAutoHideOverlayCount++;
        ViewModel.LastAvalonDockAutoHideOverlayTarget = nextContent.ContentId ?? nextContent.Title;
        ViewModel.Status = $"Auto-hide overlay shown: {nextContent.Title}";
        ViewModel.Activity.Add(ViewModel.Status);
    }

    private void ShowAvalonDockAutoHideOverlay(LayoutAnchorable anchorable)
    {
        if (!anchorable.IsAutoHidden)
        {
            anchorable.ToggleAutoHide();
            Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
        }

        FindAutoHideAnchorControl(anchorable).Focus();
        Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
    }

    private void HideAvalonDockAutoHideOverlay(LayoutAnchorable anchorable)
    {
        _ = FindAutoHideAnchorControl(anchorable);
        ActivateEditorButton.Focus();
        Keyboard.Focus(ActivateEditorButton);
        Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
    }

    private AvalonDockLayoutAnchorControl FindAutoHideAnchorControl(LayoutAnchorable anchorable)
    {
        DockManager.ApplyTemplate();
        DockManager.UpdateLayout();

        return EnumerateVisualDescendants<AvalonDockLayoutAnchorControl>(DockManager)
            .FirstOrDefault(anchorControl => ReferenceEquals(anchorControl.Model, anchorable))
            ?? throw new InvalidOperationException(
                $"Expected AvalonDock auto-hide side tab control for '{anchorable.Title}'.");
    }

    private void RecordAvalonDockContextMenuCommand(string commandName)
    {
        _viewModel.AvalonDockContextMenuCommandExecutedCount++;
        _viewModel.LastAvalonDockContextMenuCommand = commandName;
    }

    private void RecordAvalonDockAnchorableContextMenuCommand(string commandName)
    {
        _viewModel.AvalonDockAnchorableContextMenuCommandExecutedCount++;
        _viewModel.LastAvalonDockAnchorableContextMenuCommand = commandName;
    }

    private void ReopenOverviewDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DocumentPane.Children.Contains(OverviewDocument))
        {
            DocumentPane.Children.Insert(0, OverviewDocument);
        }

        OverviewDocument.IsSelected = true;
        OverviewDocument.IsActive = true;
        _viewModel.Status = "Overview document reopened";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void CloseActivityPaneButton_Click(object sender, RoutedEventArgs e)
    {
        CloseActivityPane();
    }

    private void ReopenActivityPaneButton_Click(object sender, RoutedEventArgs e)
    {
        ReopenActivityPane();
    }

    private void CloseActivityPane()
    {
        if (!RightAnchorablePane.Children.Contains(ActivityPane))
        {
            _viewModel.Status = "Activity pane already closed";
            _viewModel.Activity.Add(_viewModel.Status);
            return;
        }

        ActivityPane.Close();
        if (RightAnchorablePane.Children.Contains(ActivityPane))
        {
            return;
        }

        _viewModel.Status = "Activity pane closed";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ReopenActivityPane()
    {
        if (!RightAnchorablePane.Children.Contains(ActivityPane))
        {
            RightAnchorablePane.Children.Add(ActivityPane);
        }

        ActivityPane.IsSelected = true;
        ActivityPane.IsActive = true;
        _viewModel.Status = "Activity pane reopened";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void DockManager_ActiveContentChanged(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockActiveContentChangedCount++;
        _viewModel.LastActiveContentTitle = DockLayoutRoot.LastFocusedDocument?.Title ??
            Convert.ToString(DockManager.ActiveContent, CultureInfo.InvariantCulture) ??
            string.Empty;
    }

    private void DockManager_DocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        _viewModel.AvalonDockDocumentClosingCount++;
        _viewModel.LastClosingDocumentContentId = e.Document?.ContentId ?? string.Empty;

        if (ReferenceEquals(e.Document, OverviewDocument) &&
            _viewModel.CancelNextOverviewClose)
        {
            e.Cancel = true;
            _viewModel.CancelNextOverviewClose = false;
            _viewModel.AvalonDockDocumentCloseCanceledCount++;
            _viewModel.Status = "Overview document close canceled";
            _viewModel.Activity.Add(_viewModel.Status);
        }
    }

    private void DockManager_DocumentClosed(object? sender, DocumentClosedEventArgs e)
    {
        _viewModel.AvalonDockDocumentClosedCount++;
        _viewModel.LastClosedDocumentContentId = e.Document?.ContentId ?? string.Empty;
    }

    private void DockManager_Floated(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockFloatedCount++;
    }

    private void DockManager_Docked(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockDockedCount++;
    }

    private void DockManager_LayoutChanging(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockLayoutChangingCount++;
    }

    private void DockManager_LayoutChanged(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockLayoutChangedCount++;
    }

    private void SourceDockManager_ActiveContentChanged(object? sender, EventArgs e)
    {
        _viewModel.SourceActiveContentChangedCount++;
        _viewModel.LastSourceActiveTitle =
            (_viewModel.SourceActiveContent as ToolkitDockItem)?.Title ??
            (SourceDockManager.ActiveContent as ToolkitDockItem)?.Title ??
            string.Empty;
    }

    private void OverviewDocument_Closed(object? sender, EventArgs e)
    {
        _viewModel.OverviewDocumentClosedCount++;
    }

    private void PropertyPane_Hiding(object? sender, CancelEventArgs e)
    {
        _viewModel.AvalonDockAnchorableHidingCount++;
        _viewModel.LastAvalonDockAnchorableLifecycleTarget = PropertyPane.ContentId ?? PropertyPane.Title;
    }

    private void PropertyPane_IsVisibleChanged(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockAnchorableIsVisibleChangedCount++;
        _viewModel.LastAvalonDockAnchorableLifecycleTarget = PropertyPane.ContentId ?? PropertyPane.Title;
    }

    private void ActivityPane_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.AvalonDockAnchorableClosingCount++;
        _viewModel.LastClosingAnchorableContentId = ActivityPane.ContentId ?? string.Empty;
    }

    private void ActivityPane_Closed(object? sender, EventArgs e)
    {
        _viewModel.AvalonDockAnchorableClosedCount++;
        _viewModel.LastClosedAnchorableContentId = ActivityPane.ContentId ?? string.Empty;
    }

    private void ToggleEditorFloatButton_Click(object sender, RoutedEventArgs e)
    {
        if (EditorDocument.IsFloating)
        {
            EditorDocument.DockAsDocument();
            ToggleEditorFloatButton.Content = "Float editor";
            _viewModel.Status = "Editor document docked";
            _viewModel.Activity.Add("Docked editor document");
        }
        else
        {
            EditorDocument.Float();
            ToggleEditorFloatButton.Content = "Dock editor";
            _viewModel.Status = "Editor document floated";
            _viewModel.Activity.Add("Floated editor document");
        }
    }

    private void TogglePropertyPaneButton_Click(object sender, RoutedEventArgs e)
    {
        TogglePropertyPane();
    }

    private void TogglePropertyPane()
    {
        if (PropertyPane.IsHidden)
        {
            PropertyPane.Show();
            _viewModel.Status = "Property pane shown";
            _viewModel.Activity.Add("Shown property pane");
        }
        else
        {
            PropertyPane.Hide();
            _viewModel.Status = "Property pane hidden";
            _viewModel.Activity.Add("Hidden property pane");
        }
    }

    private void ToggleActivityAutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        ActivityPane.ToggleAutoHide();
        _viewModel.Status = ActivityPane.IsAutoHidden ? "Activity pane auto-hidden" : "Activity pane docked";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ToggleAgendaAutoHideButton_Click(object sender, RoutedEventArgs e)
    {
        AgendaPane.ToggleAutoHide();
        _viewModel.Status = AgendaPane.IsAutoHidden ? "Agenda pane auto-hidden" : "Agenda pane docked";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void CycleDockThemeButton_Click(object sender, RoutedEventArgs e)
    {
        CycleAvalonDockTheme();
    }

    internal void CycleAvalonDockTheme()
    {
        int nextIndex = (_avalonDockThemeIndex + 1) % AvalonDockThemeNames.Length;
        SetAvalonDockTheme(AvalonDockThemeNames[nextIndex], recordSwitch: true);
    }

    private void SetAvalonDockTheme(string themeName, bool recordSwitch)
    {
        int nextIndex = Array.IndexOf(AvalonDockThemeNames, themeName);
        if (nextIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(themeName), themeName, "Unknown AvalonDock theme.");
        }

        _avalonDockThemeIndex = nextIndex;
        DockManager.Theme = CreateAvalonDockTheme(themeName);
        SourceDockManager.Theme = CreateAvalonDockTheme(themeName);
        _viewModel.ActiveDockThemeName = themeName;

        if (recordSwitch)
        {
            _viewModel.DockThemeSwitchCount++;
            _viewModel.Status = $"AvalonDock theme switched to {themeName}";
            _viewModel.Activity.Add(_viewModel.Status);
        }
    }

    private static Theme CreateAvalonDockTheme(string themeName)
    {
        return themeName switch
        {
            "Aero" => new AeroTheme(),
            "Metro" => new MetroTheme(),
            "VS2010" => new VS2010Theme(),
            _ => throw new ArgumentOutOfRangeException(nameof(themeName), themeName, "Unknown AvalonDock theme.")
        };
    }

    private void MarkReviewedButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedDocument.Body += Environment.NewLine + "Reviewed through Xceed DropDownButton.";
        _viewModel.Status = "Document marked reviewed";
        _viewModel.Activity.Add("Marked selected document reviewed");
        ActionDropDownButton.IsOpen = false;
    }

    private void SplitActionButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Status = $"Applied owner {_viewModel.SelectedOwner}";
        _viewModel.Activity.Add(_viewModel.Status);
        SplitActionButton.IsOpen = false;
    }

    private void AssignSdkOwnerButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedOwner = "SDK";
        _viewModel.Status = "Owner set to SDK";
        _viewModel.Activity.Add(_viewModel.Status);
        SplitActionButton.IsOpen = false;
    }

    private void ToolkitWizard_PageChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.WizardPageChanges++;
        _viewModel.WizardStatus = ToolkitWizard.CurrentPage?.Title ?? "No wizard page";
    }

    private void ToolkitWizard_Finish(object sender, CancelRoutedEventArgs e)
    {
        _viewModel.WizardFinishes++;
        _viewModel.WizardStatus = "Wizard finished";
        _viewModel.Status = "Wizard finished";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void ToolkitWizard_Cancel(object sender, RoutedEventArgs e)
    {
        _viewModel.WizardCancels++;
        _viewModel.WizardStatus = "Wizard canceled";
        _viewModel.Status = "Wizard canceled";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    private void DocumentCountSpinner_Spin(object sender, SpinEventArgs e)
    {
        ApplyDocumentSpinnerDelta(e.Direction == SpinDirection.Increase ? 1 : -1);
    }

    private void ApplyDocumentSpinnerDelta(int delta)
    {
        _viewModel.SpinnerCount += delta;
        _viewModel.Status = $"Spinner count {_viewModel.SpinnerCount}";
        _viewModel.Activity.Add(_viewModel.Status);
    }

    internal void ExerciseDocumentCountSpinner()
    {
        int spinnerCountBefore = ViewModel.SpinnerCount;
        ApplyDocumentSpinnerDelta(1);
        AssertEqual(spinnerCountBefore + 1, ViewModel.SpinnerCount, "Toolkit ButtonSpinner increased count");
        ApplyDocumentSpinnerDelta(-1);
        AssertEqual(spinnerCountBefore, ViewModel.SpinnerCount, "Toolkit ButtonSpinner restored count");
    }

    private void ShowChildWindowButton_Click(object sender, RoutedEventArgs e)
    {
        ShowToolkitChildWindow();
    }

    private void AcceptChildWindowButton_Click(object sender, RoutedEventArgs e)
    {
        ChildWindowInputTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        ToolkitChildWindow.DialogResult = true;
    }

    private void ToolkitChildWindow_Closing(object? sender, EventArgs e)
    {
        _viewModel.ChildWindowClosingCount++;
    }

    private void ToolkitChildWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.ChildWindowClosedCount++;
        _viewModel.LastChildWindowDialogResult = ToolkitChildWindow.DialogResult;
        _viewModel.ChildWindowStatus = ToolkitChildWindow.DialogResult == true
            ? "ChildWindow accepted"
            : "ChildWindow closed";
        _viewModel.WindowContainerStatus = _viewModel.ChildWindowStatus;
        _viewModel.Status = _viewModel.WindowContainerStatus;
        _viewModel.Activity.Add(_viewModel.WindowContainerStatus);
    }

    internal void ShowToolkitChildWindow()
    {
        _viewModel.ChildWindowShowCount++;
        _viewModel.ChildWindowStatus = "ChildWindow open";
        _viewModel.WindowContainerStatus = _viewModel.ChildWindowStatus;
        _viewModel.Status = _viewModel.WindowContainerStatus;
        _viewModel.Activity.Add(_viewModel.WindowContainerStatus);
        ChildWindowInputTextBox.Text = $"Child input {_viewModel.ChildWindowShowCount}";
        ToolkitChildWindow.FocusedElement = ChildWindowInputTextBox;
        ToolkitChildWindow.Show();
    }

    internal void ExerciseToolkitChildWindow()
    {
        int showCountBefore = ViewModel.ChildWindowShowCount;
        int closingCountBefore = ViewModel.ChildWindowClosingCount;
        int closedCountBefore = ViewModel.ChildWindowClosedCount;

        ShowToolkitChildWindow();
        ValidateToolkitChildWindowState(expectedOpen: true);
        AssertEqual(showCountBefore + 1, ViewModel.ChildWindowShowCount, "Toolkit ChildWindow show count");

        AcceptChildWindowButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        ValidateToolkitChildWindowState(expectedOpen: false);
        AssertEqual(closingCountBefore + 1, ViewModel.ChildWindowClosingCount, "Toolkit ChildWindow closing count");
        AssertEqual(closedCountBefore + 1, ViewModel.ChildWindowClosedCount, "Toolkit ChildWindow closed count");
        AssertEqual(true, ViewModel.LastChildWindowDialogResult, "Toolkit ChildWindow dialog result");
        AssertEqual("ChildWindow accepted", ViewModel.ChildWindowStatus, "Toolkit ChildWindow accepted status");
    }

    private void ToggleWindowControlButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleToolkitWindowControl();
    }

    private void ActivateWindowControlButton_Click(object sender, RoutedEventArgs e)
    {
        ActivateToolkitWindowControl();
    }

    private void ToolkitWindowControl_Activated(object sender, RoutedEventArgs e)
    {
        ViewModel.WindowControlActivatedCount++;
        ViewModel.WindowControlStatus = "WindowControl activated";
        ViewModel.Status = ViewModel.WindowControlStatus;
        ViewModel.Activity.Add(ViewModel.WindowControlStatus);
    }

    private void ToolkitWindowControl_HeaderMouseLeftButtonClicked(object sender, MouseButtonEventArgs e)
    {
        ViewModel.WindowControlHeaderClickCount++;
        ViewModel.WindowControlStatus = "WindowControl header clicked";
        ViewModel.Status = ViewModel.WindowControlStatus;
        ViewModel.Activity.Add(ViewModel.WindowControlStatus);
    }

    private void ToolkitWindowControl_HeaderIconClicked(object sender, MouseButtonEventArgs e)
    {
        ViewModel.WindowControlHeaderIconClickCount++;
        ViewModel.WindowControlStatus = "WindowControl header icon clicked";
        ViewModel.Status = ViewModel.WindowControlStatus;
        ViewModel.Activity.Add(ViewModel.WindowControlStatus);
    }

    private void ToolkitWindowControl_HeaderIconDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        ViewModel.WindowControlHeaderIconDoubleClickCount++;
        ViewModel.WindowControlStatus = "WindowControl header icon double-clicked";
        ViewModel.Status = ViewModel.WindowControlStatus;
        ViewModel.Activity.Add(ViewModel.WindowControlStatus);
    }

    private void ToolkitWindowControl_HeaderMouseLeftButtonDoubleClicked(object sender, MouseButtonEventArgs e)
    {
        ViewModel.WindowControlHeaderDoubleClickCount++;
        ViewModel.WindowControlStatus = "WindowControl header double-clicked";
        ViewModel.Status = ViewModel.WindowControlStatus;
        ViewModel.Activity.Add(ViewModel.WindowControlStatus);
    }

    private void ToolkitWindowControl_HeaderMouseRightButtonClicked(object sender, MouseButtonEventArgs e)
    {
        ViewModel.WindowControlHeaderRightClickCount++;
        ViewModel.WindowControlStatus = "WindowControl header right-clicked";
        ViewModel.Status = ViewModel.WindowControlStatus;
        ViewModel.Activity.Add(ViewModel.WindowControlStatus);
    }

    private void ToolkitWindowControl_HeaderDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        ViewModel.WindowControlHeaderDragCount++;
        ViewModel.WindowControlStatus = "WindowControl header dragged";
        ViewModel.Status = ViewModel.WindowControlStatus;
        ViewModel.Activity.Add(ViewModel.WindowControlStatus);
    }

    private void ToolkitWindowControl_CloseButtonClicked(object sender, RoutedEventArgs e)
    {
        ViewModel.WindowControlCloseButtonClickCount++;
        CloseToolkitWindowControl("WindowControl closed");
    }

    internal void ToggleToolkitWindowControl()
    {
        ViewModel.WindowControlToggleCount++;
        if (ToolkitWindowControl.Visibility == Visibility.Visible)
        {
            CloseToolkitWindowControl("WindowControl hidden");
            return;
        }

        ShowToolkitWindowControl();
    }

    internal void ShowToolkitWindowControl()
    {
        ViewModel.ToolkitWindowControlVisibility = Visibility.Visible;
        ToolkitWindowControl.SetCurrentValue(VisibilityProperty, Visibility.Visible);
        ViewModel.WindowControlStatus = "WindowControl visible";
        ViewModel.Status = ViewModel.WindowControlStatus;
        ViewModel.Activity.Add(ViewModel.WindowControlStatus);
    }

    internal void CloseToolkitWindowControl(string status)
    {
        ViewModel.ToolkitWindowControlVisibility = Visibility.Collapsed;
        ToolkitWindowControl.SetCurrentValue(VisibilityProperty, Visibility.Collapsed);
        ViewModel.WindowControlStatus = status;
        ViewModel.Status = status;
        ViewModel.Activity.Add(status);
    }

    internal void ActivateToolkitWindowControl()
    {
        if (ToolkitWindowControl.Visibility != Visibility.Visible)
        {
            ShowToolkitWindowControl();
        }

        ToolkitWindowControl.IsActive = false;
        ToolkitWindowControl.IsActive = true;
        ToolkitWindowControl.Focus();
        WindowControlInputTextBox.Focus();
    }

    internal void RaiseToolkitWindowControlHeaderClick()
    {
        RaiseToolkitWindowControlMouseEvent(WindowControl.HeaderMouseLeftButtonClickedEvent, MouseButton.Left);
    }

    internal void RaiseToolkitWindowControlHeaderIconClick()
    {
        RaiseToolkitWindowControlMouseEvent(WindowControl.HeaderIconClickedEvent, MouseButton.Left);
    }

    internal void RaiseToolkitWindowControlHeaderIconDoubleClick()
    {
        RaiseToolkitWindowControlMouseEvent(WindowControl.HeaderIconDoubleClickedEvent, MouseButton.Left);
    }

    internal void RaiseToolkitWindowControlHeaderDoubleClick()
    {
        RaiseToolkitWindowControlMouseEvent(WindowControl.HeaderMouseLeftButtonDoubleClickedEvent, MouseButton.Left);
    }

    internal void RaiseToolkitWindowControlHeaderRightClick()
    {
        RaiseToolkitWindowControlMouseEvent(WindowControl.HeaderMouseRightButtonClickedEvent, MouseButton.Right);
    }

    internal void RaiseToolkitWindowControlHeaderDrag()
    {
        var args = new System.Windows.Controls.Primitives.DragDeltaEventArgs(8.0, 4.0)
        {
            RoutedEvent = WindowControl.HeaderDragDeltaEvent,
            Source = ToolkitWindowControl
        };
        ToolkitWindowControl.RaiseEvent(args);
    }

    private void RaiseToolkitWindowControlMouseEvent(RoutedEvent routedEvent, MouseButton mouseButton)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, mouseButton)
        {
            RoutedEvent = routedEvent,
            Source = ToolkitWindowControl
        };
        ToolkitWindowControl.RaiseEvent(args);
    }

    internal Button GetToolkitWindowControlButton(string partName)
    {
        ToolkitWindowControl.ApplyTemplate();
        ToolkitWindowControl.UpdateLayout();
        return ToolkitWindowControl.Template?.FindName(partName, ToolkitWindowControl) as Button
            ?? throw new InvalidOperationException($"Expected Toolkit WindowControl template button '{partName}'.");
    }

    internal void ValidateToolkitWindowControlState(bool expectedVisible, bool expectLoaded)
    {
        AssertEqual(true, ToolkitPrimitiveWindowContainer.Children.Contains(ToolkitWindowControl), "Toolkit WindowControl WindowContainer membership");
        AssertEqual(expectedVisible ? Visibility.Visible : Visibility.Collapsed, ToolkitWindowControl.Visibility, "Toolkit WindowControl visibility");
        AssertEqual(ViewModel.ToolkitWindowControlVisibility, ToolkitWindowControl.Visibility, "Toolkit WindowControl visibility binding");
        AssertEqual("Toolkit window control", Convert.ToString(ToolkitWindowControl.Caption, CultureInfo.InvariantCulture), "Toolkit WindowControl caption");
        AssertEqual(Visibility.Visible, ToolkitWindowControl.CloseButtonVisibility, "Toolkit WindowControl close button visibility");
        AssertEqual(System.Windows.WindowStyle.SingleBorderWindow, ToolkitWindowControl.WindowStyle, "Toolkit WindowControl style");
        AssertEqual(new Thickness(1), ToolkitWindowControl.WindowBorderThickness, "Toolkit WindowControl border thickness");
        AssertEqual(new Thickness(2), ToolkitWindowControl.WindowThickness, "Toolkit WindowControl window thickness");
        AssertEqual(true, ToolkitWindowControl.CaptionIcon != null, "Toolkit WindowControl caption icon");
        AssertEqual(ViewModel.WindowControlText, WindowControlInputTextBox.Text, "Toolkit WindowControl text binding");

        if (BindingOperations.GetBindingExpression(ToolkitWindowControl, VisibilityProperty) is null)
        {
            throw new InvalidOperationException("Expected Toolkit WindowControl visibility to bind to the view model.");
        }

        if (BindingOperations.GetBindingExpression(WindowControlInputTextBox, TextBox.TextProperty) is null)
        {
            throw new InvalidOperationException("Expected Toolkit WindowControl text box to bind to the view model.");
        }

        if (expectLoaded && expectedVisible)
        {
            ToolkitWindowControl.ApplyTemplate();
            ToolkitWindowControl.UpdateLayout();
            if (ToolkitWindowControl.ActualWidth <= 0 ||
                ToolkitWindowControl.ActualHeight <= 0)
            {
                throw new InvalidOperationException("Expected loaded Toolkit WindowControl to participate in layout.");
            }

            _ = GetToolkitWindowControlButton("PART_CloseButton");
            if (ToolkitWindowControl.Template?.FindName("PART_HeaderThumb", ToolkitWindowControl) is not System.Windows.Controls.Primitives.Thumb)
            {
                throw new InvalidOperationException("Expected Toolkit WindowControl template to expose the header thumb.");
            }
        }
    }

    internal void ExerciseToolkitWindowControl()
    {
        ShowToolkitWindowControl();
        ValidateToolkitWindowControlState(expectedVisible: true, expectLoaded: true);

        WindowControlInputTextBox.Text = "WindowControl primitive input";
        WindowControlInputTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        AssertEqual("WindowControl primitive input", ViewModel.WindowControlText, "Toolkit WindowControl text source update");

        int activatedCountBefore = ViewModel.WindowControlActivatedCount;
        ActivateToolkitWindowControl();
        if (ViewModel.WindowControlActivatedCount <= activatedCountBefore)
        {
            throw new InvalidOperationException("Expected Toolkit WindowControl Activated event to fire.");
        }

        int headerClickCountBefore = ViewModel.WindowControlHeaderClickCount;
        RaiseToolkitWindowControlHeaderClick();
        AssertEqual(headerClickCountBefore + 1, ViewModel.WindowControlHeaderClickCount, "Toolkit WindowControl header click count");

        int headerIconClickCountBefore = ViewModel.WindowControlHeaderIconClickCount;
        RaiseToolkitWindowControlHeaderIconClick();
        AssertEqual(headerIconClickCountBefore + 1, ViewModel.WindowControlHeaderIconClickCount, "Toolkit WindowControl header icon click count");

        int headerIconDoubleClickCountBefore = ViewModel.WindowControlHeaderIconDoubleClickCount;
        RaiseToolkitWindowControlHeaderIconDoubleClick();
        AssertEqual(headerIconDoubleClickCountBefore + 1, ViewModel.WindowControlHeaderIconDoubleClickCount, "Toolkit WindowControl header icon double-click count");

        int headerDoubleClickCountBefore = ViewModel.WindowControlHeaderDoubleClickCount;
        RaiseToolkitWindowControlHeaderDoubleClick();
        AssertEqual(headerDoubleClickCountBefore + 1, ViewModel.WindowControlHeaderDoubleClickCount, "Toolkit WindowControl header double-click count");

        int headerRightClickCountBefore = ViewModel.WindowControlHeaderRightClickCount;
        RaiseToolkitWindowControlHeaderRightClick();
        AssertEqual(headerRightClickCountBefore + 1, ViewModel.WindowControlHeaderRightClickCount, "Toolkit WindowControl header right-click count");

        int headerDragCountBefore = ViewModel.WindowControlHeaderDragCount;
        RaiseToolkitWindowControlHeaderDrag();
        AssertEqual(headerDragCountBefore + 1, ViewModel.WindowControlHeaderDragCount, "Toolkit WindowControl header drag count");

        int closeClickCountBefore = ViewModel.WindowControlCloseButtonClickCount;
        Button closeButton = GetToolkitWindowControlButton("PART_CloseButton");
        closeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, closeButton));
        AssertEqual(closeClickCountBefore + 1, ViewModel.WindowControlCloseButtonClickCount, "Toolkit WindowControl close button count");
        ValidateToolkitWindowControlState(expectedVisible: false, expectLoaded: true);

        ToggleToolkitWindowControl();
        ValidateToolkitWindowControlState(expectedVisible: true, expectLoaded: true);
    }

    private void ShowToolkitMessageBoxButton_Click(object sender, RoutedEventArgs e)
    {
        ShowToolkitMessageBox();
    }

    private void ShowStaticToolkitMessageBoxButton_Click(object sender, RoutedEventArgs e)
    {
        ShowStaticToolkitMessageBoxWithWindowOwner(autoCloseButtonPartName: null);
    }

    private void ToolkitMessageBox_Closed(object? sender, EventArgs e)
    {
        _viewModel.ToolkitMessageBoxClosedCount++;
        _viewModel.LastToolkitMessageBoxResult = ToolkitMessageBox.MessageBoxResult;
        _viewModel.ToolkitMessageBoxStatus = $"MessageBox {ToolkitMessageBox.MessageBoxResult}";
        _viewModel.WindowContainerStatus = _viewModel.ToolkitMessageBoxStatus;
        _viewModel.Status = _viewModel.WindowContainerStatus;
        _viewModel.Activity.Add(_viewModel.WindowContainerStatus);
    }

    internal void ShowToolkitMessageBox()
    {
        _viewModel.ToolkitMessageBoxShowCount++;
        _viewModel.ToolkitMessageBoxStatus = "MessageBox open";
        _viewModel.WindowContainerStatus = _viewModel.ToolkitMessageBoxStatus;
        _viewModel.Status = _viewModel.WindowContainerStatus;
        _viewModel.Activity.Add(_viewModel.WindowContainerStatus);
        ToolkitMessageBox.ShowMessageBox(
            "MessageBox inside Xceed WindowContainer",
            "Toolkit message",
            MessageBoxButton.OK,
            MessageBoxImage.None,
            MessageBoxResult.OK);
    }

    internal void ExerciseToolkitMessageBox()
    {
        int showCountBefore = ViewModel.ToolkitMessageBoxShowCount;
        int closedCountBefore = ViewModel.ToolkitMessageBoxClosedCount;

        ShowToolkitMessageBox();
        ValidateToolkitMessageBoxState(expectedOpen: true);
        AssertEqual(showCountBefore + 1, ViewModel.ToolkitMessageBoxShowCount, "Toolkit MessageBox show count");

        Button okButton = GetToolkitMessageBoxButton("PART_OkButton");
        okButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, okButton));
        ValidateToolkitMessageBoxState(expectedOpen: false);
        AssertEqual(closedCountBefore + 1, ViewModel.ToolkitMessageBoxClosedCount, "Toolkit MessageBox closed count");
        AssertEqual(MessageBoxResult.OK, ViewModel.LastToolkitMessageBoxResult, "Toolkit MessageBox result");
        AssertEqual("MessageBox OK", ViewModel.ToolkitMessageBoxStatus, "Toolkit MessageBox OK status");
    }

    internal void ExerciseStaticToolkitMessageBoxes()
    {
        int showCountBefore = ViewModel.StaticToolkitMessageBoxShowCount;
        int closedCountBefore = ViewModel.StaticToolkitMessageBoxClosedCount;

        MessageBoxResult windowOwnerResult = ShowStaticToolkitMessageBoxWithWindowOwner("PART_OkButton");
        AssertEqual(MessageBoxResult.OK, windowOwnerResult, "Toolkit static MessageBox window-owner result");
        AssertEqual(showCountBefore + 1, ViewModel.StaticToolkitMessageBoxShowCount, "Toolkit static MessageBox window-owner show count");
        AssertEqual(closedCountBefore + 1, ViewModel.StaticToolkitMessageBoxClosedCount, "Toolkit static MessageBox window-owner closed count");

        MessageBoxResult handleOwnerResult = ShowStaticToolkitMessageBoxWithOwnerHandle("PART_NoButton");
        AssertEqual(MessageBoxResult.No, handleOwnerResult, "Toolkit static MessageBox handle-owner result");
        AssertEqual(showCountBefore + 2, ViewModel.StaticToolkitMessageBoxShowCount, "Toolkit static MessageBox handle-owner show count");
        AssertEqual(closedCountBefore + 2, ViewModel.StaticToolkitMessageBoxClosedCount, "Toolkit static MessageBox handle-owner closed count");
        ValidateStaticToolkitMessageBoxState(expectedValidated: true);
    }

    internal MessageBoxResult ShowStaticToolkitMessageBoxWithWindowOwner(string? autoCloseButtonPartName)
    {
        return ShowStaticToolkitMessageBox(
            "Window owner static MessageBox",
            "Toolkit static owner message",
            "Toolkit static owner",
            autoCloseButtonPartName,
            () => ToolkitMessageBoxControl.Show(
                this,
                "Toolkit static owner message",
                "Toolkit static owner",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information,
                MessageBoxResult.OK));
    }

    internal MessageBoxResult ShowStaticToolkitMessageBoxWithOwnerHandle(string? autoCloseButtonPartName)
    {
        IntPtr ownerHandle = new WindowInteropHelper(this).EnsureHandle();
        if (ownerHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Expected ProGPU portable WindowInteropHelper to expose a non-zero owner handle.");
        }

        ViewModel.LastStaticToolkitMessageBoxOwnerHandle = ownerHandle;
        return ShowStaticToolkitMessageBox(
            "Owner handle static MessageBox",
            "Toolkit static handle message",
            "Toolkit static handle",
            autoCloseButtonPartName,
            () => ToolkitMessageBoxControl.Show(
                ownerHandle,
                "Toolkit static handle message",
                "Toolkit static handle",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No));
    }

    private MessageBoxResult ShowStaticToolkitMessageBox(
        string label,
        string expectedText,
        string expectedCaption,
        string? autoCloseButtonPartName,
        Func<MessageBoxResult> show)
    {
        ViewModel.StaticToolkitMessageBoxShowCount++;
        ViewModel.StaticToolkitMessageBoxStatus = $"{label} open";
        ViewModel.WindowContainerStatus = ViewModel.StaticToolkitMessageBoxStatus;
        ViewModel.Status = ViewModel.StaticToolkitMessageBoxStatus;
        ViewModel.Activity.Add(ViewModel.StaticToolkitMessageBoxStatus);

        DispatcherTimer? autoCloseTimer = null;
        Exception? autoCloseError = null;
        int autoCloseAttempts = 0;

        if (!string.IsNullOrEmpty(autoCloseButtonPartName))
        {
            autoCloseTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(10)
            };
            autoCloseTimer.Tick += (_, _) =>
            {
                autoCloseAttempts++;
                if (TryClickStaticToolkitMessageBoxButton(expectedText, expectedCaption, autoCloseButtonPartName))
                {
                    autoCloseTimer.Stop();
                    return;
                }

                if (autoCloseAttempts > 250)
                {
                    autoCloseTimer.Stop();
                    autoCloseError = new TimeoutException(
                        $"Timed out waiting for Toolkit static MessageBox '{expectedCaption}' button '{autoCloseButtonPartName}'.");
                    CloseStaticToolkitMessageBoxWindow(expectedCaption);
                }
            };
            autoCloseTimer.Start();
        }

        MessageBoxResult result;
        try
        {
            result = show();
        }
        finally
        {
            autoCloseTimer?.Stop();
        }

        if (autoCloseError != null)
        {
            throw autoCloseError;
        }

        ViewModel.StaticToolkitMessageBoxClosedCount++;
        ViewModel.LastStaticToolkitMessageBoxResult = result;
        ViewModel.StaticToolkitMessageBoxStatus = $"{label} {result}";
        ViewModel.WindowContainerStatus = ViewModel.StaticToolkitMessageBoxStatus;
        ViewModel.Status = ViewModel.StaticToolkitMessageBoxStatus;
        ViewModel.Activity.Add(ViewModel.StaticToolkitMessageBoxStatus);
        return result;
    }

    private bool TryClickStaticToolkitMessageBoxButton(string expectedText, string expectedCaption, string buttonPartName)
    {
        ToolkitMessageBoxControl? messageBox = FindStaticToolkitMessageBox(expectedCaption);
        if (messageBox == null)
        {
            return false;
        }

        AssertEqual(expectedText, messageBox.Text, "Toolkit static MessageBox text");
        AssertEqual(expectedCaption, Convert.ToString(messageBox.Caption, CultureInfo.InvariantCulture), "Toolkit static MessageBox caption");
        PresentationSource? source = PresentationSource.FromVisual(messageBox);
        if (source is not HwndSource ||
            source.CompositionTarget == null)
        {
            throw new InvalidOperationException("Expected Toolkit static MessageBox window to expose the portable public HwndSource facade.");
        }

        messageBox.ApplyTemplate();
        messageBox.UpdateLayout();
        if (messageBox.Template?.FindName(buttonPartName, messageBox) is not Button button ||
            !button.IsEnabled)
        {
            return false;
        }

        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
        return true;
    }

    private static ToolkitMessageBoxControl? FindStaticToolkitMessageBox(string expectedCaption)
    {
        if (Application.Current == null)
        {
            return null;
        }

        foreach (Window window in Application.Current.Windows)
        {
            if (window.Content is ToolkitMessageBoxControl messageBox &&
                string.Equals(
                    Convert.ToString(messageBox.Caption, CultureInfo.InvariantCulture),
                    expectedCaption,
                    StringComparison.Ordinal))
            {
                return messageBox;
            }
        }

        return null;
    }

    private static void CloseStaticToolkitMessageBoxWindow(string expectedCaption)
    {
        if (Application.Current == null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows.OfType<Window>().ToArray())
        {
            if (window.Content is ToolkitMessageBoxControl messageBox &&
                string.Equals(
                    Convert.ToString(messageBox.Caption, CultureInfo.InvariantCulture),
                    expectedCaption,
                    StringComparison.Ordinal))
            {
                window.Close();
                return;
            }
        }
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyZoomboxZoomIn();
    }

    private void FitZoomboxButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyZoomboxFit();
    }

    private void BackZoomboxButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyZoomboxBack();
    }

    private void ToolkitZoombox_CurrentViewChanged(object sender, ZoomboxViewChangedEventArgs e)
    {
        _viewModel.ZoomboxViewChangedCount++;
        _viewModel.LastZoomboxScale = ToolkitZoombox.Scale;
        _viewModel.ZoomboxStatus = FormatZoomboxStatus("View changed");
        _viewModel.Status = _viewModel.ZoomboxStatus;
        _viewModel.Activity.Add(_viewModel.ZoomboxStatus);
    }

    private void ToolkitZoombox_ViewStackIndexChanged(object sender, IndexChangedEventArgs e)
    {
        _viewModel.ZoomboxViewStackIndexChangedCount++;
        _viewModel.LastZoomboxViewStackIndex = ToolkitZoombox.ViewStackIndex;
        _viewModel.ZoomboxStatus = FormatZoomboxStatus("View stack changed");
        _viewModel.Status = _viewModel.ZoomboxStatus;
        _viewModel.Activity.Add(_viewModel.ZoomboxStatus);
    }

    internal void ApplyZoomboxZoomIn()
    {
        _viewModel.ZoomboxCommandCount++;
        ToolkitZoombox.Zoom(25.0);
        UpdateZoomboxStatus("Zoomed in");
    }

    internal void ApplyZoomboxFit()
    {
        _viewModel.ZoomboxCommandCount++;
        ToolkitZoombox.FitToBounds();
        UpdateZoomboxStatus("Fit content");
    }

    internal void ApplyZoomboxBack()
    {
        _viewModel.ZoomboxCommandCount++;
        if (ToolkitZoombox.HasBackStack)
        {
            ToolkitZoombox.GoBack();
            UpdateZoomboxStatus("Back");
            return;
        }

        UpdateZoomboxStatus("Back unavailable");
    }

    internal void ExerciseToolkitZoomboxAndMagnifier()
    {
        ValidateToolkitZoomboxAndMagnifierState(expectLoaded: true);

        int commandCountBefore = ViewModel.ZoomboxCommandCount;
        int viewChangedCountBefore = ViewModel.ZoomboxViewChangedCount;
        double scaleBefore = ToolkitZoombox.Scale;

        ApplyZoomboxZoomIn();
        PumpDispatcherUntil(
            this,
            () => ViewModel.ZoomboxViewChangedCount > viewChangedCountBefore ||
                  !Equals(ToolkitZoombox.Scale, scaleBefore),
            TimeSpan.FromSeconds(2),
            "Toolkit Zoombox zoom-in view change");
        if (ViewModel.ZoomboxCommandCount <= commandCountBefore)
        {
            throw new InvalidOperationException("Expected Toolkit Zoombox zoom command count to advance.");
        }

        int stackChangedCountBefore = ViewModel.ZoomboxViewStackIndexChangedCount;
        ToolkitZoombox.ZoomTo(new Rect(32, 24, 128, 72));
        PumpDispatcherUntil(
            this,
            () => ViewModel.ZoomboxViewChangedCount > viewChangedCountBefore + 1 ||
                  ToolkitZoombox.CurrentView.ViewKind == ZoomboxViewKind.Region,
            TimeSpan.FromSeconds(2),
            "Toolkit Zoombox region view");
        AssertEqual(ZoomboxViewKind.Region, ToolkitZoombox.CurrentView.ViewKind, "Toolkit Zoombox region view kind");

        ApplyZoomboxFit();
        PumpDispatcherUntil(
            this,
            () => ToolkitZoombox.CurrentView.ViewKind == ZoomboxViewKind.Fit,
            TimeSpan.FromSeconds(2),
            "Toolkit Zoombox fit view");
        AssertEqual(ZoomboxViewKind.Fit, ToolkitZoombox.CurrentView.ViewKind, "Toolkit Zoombox fit view kind");

        ApplyZoomboxBack();
        if (ToolkitZoombox.ViewStackCount > 1 &&
            ViewModel.ZoomboxViewStackIndexChangedCount <= stackChangedCountBefore)
        {
            throw new InvalidOperationException("Expected Toolkit Zoombox view-stack change event to fire.");
        }

        ToolkitMagnifier.Freeze(true);
        AssertEqual(true, ToolkitMagnifier.IsFrozen, "Toolkit Magnifier frozen state");
        ToolkitMagnifier.Freeze(false);
        AssertEqual(false, ToolkitMagnifier.IsFrozen, "Toolkit Magnifier unfrozen state");
        ValidateToolkitZoomboxAndMagnifierState(expectLoaded: true);
    }

    internal void ValidateToolkitZoomboxAndMagnifierState(bool expectLoaded)
    {
        AssertEqual(false, ToolkitZoombox.IsAnimated, "Toolkit Zoombox animation state");
        AssertEqual(true, ToolkitZoombox.IsUsingScrollBars, "Toolkit Zoombox scrollbar state");
        AssertEqual(false, ToolkitZoombox.AutoWrapContentWithViewbox, "Toolkit Zoombox viewbox wrapping state");
        AssertEqual(true, ToolkitZoombox.KeepContentInBounds, "Toolkit Zoombox content bounds state");
        AssertEqual(0.25, ToolkitZoombox.MinScale, "Toolkit Zoombox minimum scale");
        AssertEqual(4.0, ToolkitZoombox.MaxScale, "Toolkit Zoombox maximum scale");
        AssertEqual(32.0, ToolkitZoombox.PanDistance, "Toolkit Zoombox pan distance");
        AssertEqual(25.0, ToolkitZoombox.ZoomPercentage, "Toolkit Zoombox zoom percentage");
        AssertEqual(ZoomboxContentRoot, ToolkitZoombox.Content, "Toolkit Zoombox content");
        AssertEqual(ZoomboxContentRoot, ToolkitMagnifier.Target, "Toolkit Magnifier target");
        AssertEqual(ToolkitMagnifier, MagnifierManager.GetMagnifier(ZoomboxContentRoot), "Toolkit Magnifier manager attachment");
        AssertEqual(FrameType.Circle, ToolkitMagnifier.FrameType, "Toolkit Magnifier frame type");
        AssertEqual(36.0, ToolkitMagnifier.Radius, "Toolkit Magnifier radius");
        AssertEqual(1.6, ToolkitMagnifier.ZoomFactor, "Toolkit Magnifier zoom factor");
        AssertEqual(true, ToolkitMagnifier.IsUsingZoomOnMouseWheel, "Toolkit Magnifier mouse wheel zoom state");
        AssertEqual(0.15, ToolkitMagnifier.ZoomFactorOnMouseWheel, "Toolkit Magnifier mouse wheel zoom factor");

        if (expectLoaded)
        {
            ToolkitZoombox.ApplyTemplate();
            ToolkitZoombox.UpdateLayout();
            if (ToolkitZoombox.ActualWidth <= 0 ||
                ToolkitZoombox.ActualHeight <= 0 ||
                ZoomboxContentRoot.ActualWidth <= 0 ||
                ZoomboxContentRoot.ActualHeight <= 0)
            {
                throw new InvalidOperationException("Expected loaded Toolkit Zoombox and content to participate in layout.");
            }
        }
    }

    private void UpdateZoomboxStatus(string prefix)
    {
        _viewModel.LastZoomboxScale = ToolkitZoombox.Scale;
        _viewModel.LastZoomboxViewStackIndex = ToolkitZoombox.ViewStackIndex;
        _viewModel.ZoomboxStatus = FormatZoomboxStatus(prefix);
        _viewModel.Status = _viewModel.ZoomboxStatus;
        _viewModel.Activity.Add(_viewModel.ZoomboxStatus);
    }

    private string FormatZoomboxStatus(string prefix)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}: scale {1:0.##}, view {2}, stack {3}/{4}",
            prefix,
            ToolkitZoombox.Scale,
            ToolkitZoombox.CurrentView.ViewKind,
            ToolkitZoombox.ViewStackIndex,
            ToolkitZoombox.ViewStackCount);
    }

    internal void ValidateToolkitPanelState(bool expectLoaded)
    {
        AssertEqual(Orientation.Horizontal, ToolkitWrapPanel.Orientation, "Toolkit WrapPanel orientation");
        AssertEqual(false, ToolkitWrapPanel.IsChildOrderReversed, "Toolkit WrapPanel child order");
        AssertEqual(64.0, ToolkitWrapPanel.ItemWidth, "Toolkit WrapPanel item width");
        AssertEqual(34.0, ToolkitWrapPanel.ItemHeight, "Toolkit WrapPanel item height");
        AssertEqual(3, ToolkitWrapPanel.Children.Count, "Toolkit WrapPanel child count");

        if (expectLoaded)
        {
            ToolkitWrapPanel.UpdateLayout();
            if (ToolkitWrapPanel.ActualWidth <= 0 ||
                ToolkitWrapPanel.ActualHeight <= 0)
            {
                throw new InvalidOperationException("Expected Toolkit panel controls to participate in loaded layout.");
            }
        }
    }

    internal void ValidateToolkitScrollClipState(bool expectLoaded)
    {
        AssertEqual(ScrollBarVisibility.Auto, ToolkitPaneScrollViewer.VerticalScrollBarVisibility, "Toolkit pane vertical scrollbar visibility");
        if (!ReferenceEquals(ToolkitPaneScrollViewer.Content, ToolkitPaneContentPanel))
        {
            throw new InvalidOperationException("Expected Toolkit pane ScrollViewer to host the named content panel.");
        }

        if (!expectLoaded)
        {
            return;
        }

        ToolkitPaneScrollViewer.UpdateLayout();
        if (ToolkitPaneScrollViewer.ViewportHeight <= 0 ||
            ToolkitPaneScrollViewer.ScrollableHeight <= 0)
        {
            throw new InvalidOperationException("Expected Toolkit pane ScrollViewer to expose a clipped scrollable viewport.");
        }

        ValidateRequiredScrollContentPresenterClip(ToolkitPaneScrollViewer, "Toolkit pane ScrollViewer");

        double targetOffset = Math.Min(120.0, ToolkitPaneScrollViewer.ScrollableHeight);
        ToolkitPaneScrollViewer.ScrollToVerticalOffset(targetOffset);
        ToolkitPaneScrollViewer.UpdateLayout();

        if (ToolkitPaneScrollViewer.VerticalOffset <= 0)
        {
            throw new InvalidOperationException("Expected Toolkit pane ScrollViewer to apply a non-zero vertical offset.");
        }

        ToolkitPaneScrollViewer.ScrollToVerticalOffset(0);
        ToolkitPaneScrollViewer.UpdateLayout();
    }

    private void AddCollectionEntryButton_Click(object sender, RoutedEventArgs e)
    {
        AddToolkitCollectionEntry();
    }

    private void SelectCollectionEntryButton_Click(object sender, RoutedEventArgs e)
    {
        SelectSecondToolkitCollectionEntry();
    }

    private void OpenCollectionDialogButton_CollectionUpdated(object sender, RoutedEventArgs e)
    {
        ViewModel.CollectionDialogUpdateCount++;
        ViewModel.CollectionControlStatus = $"Dialog updated {ViewModel.CollectionEntries.Count} entries";
        ViewModel.Status = ViewModel.CollectionControlStatus;
        ViewModel.Activity.Add(ViewModel.CollectionControlStatus);
    }

    internal ToolkitCollectionEntry AddToolkitCollectionEntry()
    {
        int index = ViewModel.CollectionEntries.Count + 1;
        var entry = new ToolkitCollectionEntry($"Entry {index}", "Generated", index * 10);
        ViewModel.CollectionEntries.Add(entry);
        ViewModel.SelectedCollectionEntry = entry;
        ToolkitCollectionControl.SelectedItem = entry;
        ViewModel.CollectionControlStatus = $"Added {entry.Name}";
        ViewModel.Status = ViewModel.CollectionControlStatus;
        ViewModel.Activity.Add(ViewModel.CollectionControlStatus);
        return entry;
    }

    internal void SelectSecondToolkitCollectionEntry()
    {
        if (ViewModel.CollectionEntries.Count < 2)
        {
            AddToolkitCollectionEntry();
        }

        var entry = ViewModel.CollectionEntries[1];
        ViewModel.SelectedCollectionEntry = entry;
        ToolkitCollectionControl.SelectedItem = entry;
        ViewModel.CollectionControlStatus = $"Selected {entry.Name}";
        ViewModel.Status = ViewModel.CollectionControlStatus;
        ViewModel.Activity.Add(ViewModel.CollectionControlStatus);
    }

    internal void ExerciseToolkitCollectionControl()
    {
        ValidateToolkitCollectionControlState(expectLoaded: true);

        int countBefore = ViewModel.CollectionEntries.Count;
        var addedEntry = AddToolkitCollectionEntry();
        PumpDispatcherUntil(
            this,
            () => ViewModel.CollectionEntries.Count == countBefore + 1 &&
                  ReferenceEquals(ToolkitCollectionControl.SelectedItem, addedEntry),
            TimeSpan.FromSeconds(2),
            "Toolkit CollectionControl add entry");
        AssertEqual($"Added {addedEntry.Name}", ViewModel.CollectionControlStatus, "Toolkit CollectionControl add status");

        SelectSecondToolkitCollectionEntry();
        AssertEqual(ViewModel.CollectionEntries[1], ViewModel.SelectedCollectionEntry, "Toolkit CollectionControl selected view-model entry");
        AssertEqual(ViewModel.SelectedCollectionEntry, ToolkitCollectionControl.SelectedItem, "Toolkit CollectionControl selected item");

        bool persistedChanges = ToolkitCollectionControl.PersistChanges();
        AssertEqual(false, persistedChanges, "Toolkit CollectionControl persisted changes state after external collection update");
        ValidateToolkitCollectionControlState(expectLoaded: true);
        ExerciseToolkitCollectionDialogButton();
    }

    internal void ValidateToolkitCollectionControlState(bool expectLoaded)
    {
        if (!ReferenceEquals(ToolkitCollectionControl.ItemsSource, ViewModel.CollectionEntries))
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControl ItemsSource to bind the view-model collection.");
        }

        AssertEqual(typeof(ToolkitCollectionEntry), ToolkitCollectionControl.ItemsSourceType, "Toolkit CollectionControl items source type");
        AssertEqual(false, ToolkitCollectionControl.IsReadOnly, "Toolkit CollectionControl read-only state");
        AssertEqual("Entry properties", Convert.ToString(ToolkitCollectionControl.PropertiesLabel, CultureInfo.InvariantCulture), "Toolkit CollectionControl properties label");
        AssertEqual("Entry type", Convert.ToString(ToolkitCollectionControl.TypeSelectionLabel, CultureInfo.InvariantCulture), "Toolkit CollectionControl type-selection label");
        AssertEqual(ViewModel.SelectedCollectionEntry, ToolkitCollectionControl.SelectedItem, "Toolkit CollectionControl selected item binding");

        if (ToolkitCollectionControl.NewItemTypes is null ||
            !ToolkitCollectionControl.NewItemTypes.OfType<Type>().Contains(typeof(ToolkitCollectionEntry)))
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControl NewItemTypes to include the sample entry type.");
        }

        if (expectLoaded)
        {
            ToolkitCollectionControl.ApplyTemplate();
            ToolkitCollectionControl.UpdateLayout();
            if (ToolkitCollectionControl.ActualWidth <= 0 ||
                ToolkitCollectionControl.ActualHeight <= 0 ||
                ToolkitCollectionControl.PropertyGrid == null)
            {
                throw new InvalidOperationException("Expected loaded Toolkit CollectionControl and inner PropertyGrid to participate in layout.");
            }
        }
    }

    internal void ValidateToolkitDataGridState(bool expectLoaded)
    {
        AssertEqual(100_000, ViewModel.DataGridItemCount, "Toolkit DataGrid item count");
        AssertEqual(ViewModel.DataGridItems, ToolkitDataGrid.ItemsSource, "Toolkit DataGrid items source");
        AssertEqual(ViewModel.DataGridItems.Count, ToolkitDataGrid.Items.Count, "Toolkit DataGrid realized item view count");
        AssertEqual(ViewModel.DataGridItems[0], ToolkitDataGrid.Items[0], "Toolkit DataGrid first row");
        AssertEqual(ViewModel.DataGridItems[ViewModel.DataGridItems.Count - 1], ToolkitDataGrid.Items[ViewModel.DataGridItems.Count - 1], "Toolkit DataGrid last row");
        AssertEqual(ViewModel.SelectedDataGridItem, ToolkitDataGrid.SelectedItem, "Toolkit DataGrid selected item binding");
        AssertEqual(false, ToolkitDataGrid.AutoGenerateColumns, "Toolkit DataGrid generated-column mode");
        AssertEqual(true, ToolkitDataGrid.IsReadOnly, "Toolkit DataGrid read-only state");
        AssertEqual(true, ToolkitDataGrid.EnableRowVirtualization, "Toolkit DataGrid row virtualization");
        AssertEqual(true, ToolkitDataGrid.EnableColumnVirtualization, "Toolkit DataGrid column virtualization");
        AssertEqual(true, VirtualizingPanel.GetIsVirtualizing(ToolkitDataGrid), "Toolkit DataGrid virtualizing panel flag");
        AssertEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(ToolkitDataGrid), "Toolkit DataGrid virtualization mode");
        AssertEqual(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(ToolkitDataGrid), "Toolkit DataGrid scroll unit");
        AssertEqual(true, ScrollViewer.GetCanContentScroll(ToolkitDataGrid), "Toolkit DataGrid content scrolling");
        AssertEqual(6, ToolkitDataGrid.Columns.Count, "Toolkit DataGrid column count");

        if (BindingOperations.GetBindingExpression(ToolkitDataGrid, ItemsControl.ItemsSourceProperty) is null)
        {
            throw new InvalidOperationException("Expected Toolkit DataGrid ItemsSource binding expression.");
        }

        if (BindingOperations.GetBindingExpression(ToolkitDataGrid, System.Windows.Controls.Primitives.Selector.SelectedItemProperty) is null)
        {
            throw new InvalidOperationException("Expected Toolkit DataGrid SelectedItem binding expression.");
        }

        if (expectLoaded)
        {
            DataGridDocument.IsSelected = true;
            DataGridDocument.IsActive = true;
            DockManager.UpdateLayout();
            ToolkitDataGrid.UpdateLayout();
            if (ToolkitDataGrid.ActualWidth <= 0 ||
                ToolkitDataGrid.ActualHeight <= 0)
            {
                throw new InvalidOperationException("Expected loaded Toolkit DataGrid document to participate in layout.");
            }

            ValidateToolkitDataGridVirtualizingScroll();
        }
    }

    private void ValidateToolkitDataGridVirtualizingScroll()
    {
        ScrollViewer scrollViewer = GetRequiredToolkitDataGridScrollViewer();
        if (scrollViewer.ViewportHeight <= 0 ||
            scrollViewer.ScrollableHeight <= 0)
        {
            throw new InvalidOperationException("Expected Toolkit DataGrid to expose a clipped scrollable viewport for the 100k-row document.");
        }

        ValidateToolkitDataGridRealizedRowCount("initial");

        ToolkitDataGrid.ScrollIntoView(ViewModel.DataGridItems[ViewModel.DataGridItems.Count - 1]);
        Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        ToolkitDataGrid.UpdateLayout();
        scrollViewer.UpdateLayout();
        ValidateRequiredScrollContentPresenterClip(scrollViewer, "Toolkit DataGrid ScrollViewer");

        if (scrollViewer.VerticalOffset <= 0)
        {
            throw new InvalidOperationException("Expected Toolkit DataGrid ScrollIntoView to apply a non-zero vertical offset.");
        }

        ValidateToolkitDataGridRealizedRowCount("after large scroll");

        ToolkitDataGrid.ScrollIntoView(ViewModel.DataGridItems[0]);
        Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        ToolkitDataGrid.UpdateLayout();
        scrollViewer.UpdateLayout();
    }

    private ScrollViewer GetRequiredToolkitDataGridScrollViewer()
    {
        ToolkitDataGrid.ApplyTemplate();
        ToolkitDataGrid.UpdateLayout();
        return EnumerateVisualDescendants<ScrollViewer>(ToolkitDataGrid).FirstOrDefault()
            ?? throw new InvalidOperationException("Expected Toolkit DataGrid template to expose a ScrollViewer.");
    }

    private static void ValidateRequiredScrollContentPresenterClip(ScrollViewer scrollViewer, string description)
    {
        scrollViewer.ApplyTemplate();
        scrollViewer.UpdateLayout();
        var presenter = EnumerateVisualDescendants<ScrollContentPresenter>(scrollViewer)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Expected {description} to expose a ScrollContentPresenter.");
        ValidateRequiredVisualClip(presenter, $"{description} content presenter");
    }

    private static void ValidateRequiredVisualClip(Visual visual, string description)
    {
        if (VisualTreeHelper.GetClip(visual) is not Geometry clip)
        {
            throw new InvalidOperationException($"Expected {description} to have a WPF internal VisualClip.");
        }

        Rect bounds = clip.Bounds;
        if (bounds.IsEmpty ||
            bounds.Width <= 0 ||
            bounds.Height <= 0 ||
            double.IsInfinity(bounds.Width) ||
            double.IsInfinity(bounds.Height) ||
            double.IsNaN(bounds.Width) ||
            double.IsNaN(bounds.Height))
        {
            throw new InvalidOperationException($"Expected {description} to have a finite non-empty VisualClip, got {bounds}.");
        }
    }

    private void ValidateToolkitDataGridRealizedRowCount(string phase)
    {
        int realizedRows = EnumerateVisualDescendants<DataGridRow>(ToolkitDataGrid).Count();
        if (realizedRows <= 0)
        {
            throw new InvalidOperationException($"Expected Toolkit DataGrid to realize visible rows during {phase} validation.");
        }

        if (realizedRows >= 500)
        {
            throw new InvalidOperationException($"Expected Toolkit DataGrid virtualization to keep realized rows bounded during {phase} validation, got {realizedRows}.");
        }
    }

    internal void ExerciseToolkitCollectionDialogButton()
    {
        ValidateToolkitCollectionDialogButtonState(expectLoaded: true);

        using var dialogScope = CreateToolkitCollectionControlDialog();
        ValidateToolkitCollectionDialogInstance(dialogScope.Dialog, expectLoaded: false);

        int updateCountBefore = ViewModel.CollectionDialogUpdateCount;
        OpenCollectionDialogButton.RaiseEvent(new RoutedEventArgs(CollectionControlButton.CollectionUpdatedEvent, OpenCollectionDialogButton));
        AssertEqual(updateCountBefore + 1, ViewModel.CollectionDialogUpdateCount, "Toolkit CollectionControlButton collection-updated event count");
        AssertEqual($"Dialog updated {ViewModel.CollectionEntries.Count} entries", ViewModel.CollectionControlStatus, "Toolkit CollectionControlButton collection-updated status");

        if (!IsLoaded)
        {
            return;
        }

        bool sawCanceledDialog = false;
        var closeTimer = new DispatcherTimer(DispatcherPriority.Send, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        closeTimer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows
                .OfType<CollectionControlDialog>()
                .FirstOrDefault(window => !ReferenceEquals(window, this) && window.IsVisible);
            if (dialog is null)
            {
                return;
            }

            sawCanceledDialog = true;
            ValidateToolkitCollectionDialogInstance(dialog, expectLoaded: true);
            dialog.Close();
            closeTimer.Stop();
        };

        closeTimer.Start();
        try
        {
            OpenCollectionDialogButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, OpenCollectionDialogButton));
        }
        finally
        {
            closeTimer.Stop();
        }

        if (!sawCanceledDialog)
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlButton click to open a CollectionControlDialog.");
        }

        AssertEqual(updateCountBefore + 1, ViewModel.CollectionDialogUpdateCount, "Toolkit CollectionControlButton canceled dialog update count");

        ExerciseToolkitCollectionDialogOkPersistence(updateCountBefore + 1);
        ExerciseToolkitCollectionDialogCancelRollback(updateCountBefore + 2);
    }

    internal void ExerciseToolkitCollectionDialogOkPersistence(int updateCountBefore)
    {
        int countBefore = ViewModel.CollectionEntries.Count;
        bool acceptedDialog = false;
        var acceptTimer = new DispatcherTimer(DispatcherPriority.Send, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        acceptTimer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows
                .OfType<CollectionControlDialog>()
                .FirstOrDefault(window => !ReferenceEquals(window, this) && window.IsVisible);
            if (dialog is null)
            {
                return;
            }

            acceptedDialog = true;
            ValidateToolkitCollectionDialogInstance(dialog, expectLoaded: true);

            var innerControl = dialog.CollectionControl;
            ApplicationCommands.New.Execute(typeof(ToolkitCollectionEntry), innerControl);
            var addedEntry = innerControl.Items.OfType<ToolkitCollectionEntry>().Last();
            addedEntry.Name = $"Dialog Entry {countBefore + 1}";
            addedEntry.Category = "Dialog";
            addedEntry.Weight = 100 + countBefore;
            innerControl.SelectedItem = addedEntry;

            Button okButton = FindCollectionDialogButton(dialog, "OK", isDefault: true);
            okButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, okButton));
            acceptTimer.Stop();
        };

        acceptTimer.Start();
        try
        {
            OpenCollectionDialogButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, OpenCollectionDialogButton));
        }
        finally
        {
            acceptTimer.Stop();
        }

        if (!acceptedDialog)
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlButton OK path to open a CollectionControlDialog.");
        }

        AssertEqual(updateCountBefore + 1, ViewModel.CollectionDialogUpdateCount, "Toolkit CollectionControlButton accepted dialog update count");
        AssertEqual(countBefore + 1, ViewModel.CollectionEntries.Count, "Toolkit CollectionControlDialog persisted entry count");
        var persistedEntry = ViewModel.CollectionEntries[^1];
        AssertEqual($"Dialog Entry {countBefore + 1}", persistedEntry.Name, "Toolkit CollectionControlDialog persisted entry name");
        AssertEqual("Dialog", persistedEntry.Category, "Toolkit CollectionControlDialog persisted entry category");
        AssertEqual(100 + countBefore, persistedEntry.Weight, "Toolkit CollectionControlDialog persisted entry weight");
        AssertEqual($"Dialog updated {ViewModel.CollectionEntries.Count} entries", ViewModel.CollectionControlStatus, "Toolkit CollectionControlDialog accepted status");
        ValidateToolkitCollectionControlState(expectLoaded: true);
        ValidateToolkitCollectionDialogButtonState(expectLoaded: true);
    }

    internal void ExerciseToolkitCollectionDialogCancelRollback(int updateCountBefore)
    {
        var originalEntries = ViewModel.CollectionEntries
            .Select(entry => new ToolkitCollectionEntry(entry.Name, entry.Category, entry.Weight))
            .ToList();
        bool canceledDialog = false;
        var cancelTimer = new DispatcherTimer(DispatcherPriority.Send, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        cancelTimer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows
                .OfType<CollectionControlDialog>()
                .FirstOrDefault(window => !ReferenceEquals(window, this) && window.IsVisible);
            if (dialog is null)
            {
                return;
            }

            canceledDialog = true;
            ValidateToolkitCollectionDialogInstance(dialog, expectLoaded: true);

            var innerControl = dialog.CollectionControl;
            ApplicationCommands.New.Execute(typeof(ToolkitCollectionEntry), innerControl);
            var canceledEntry = innerControl.Items.OfType<ToolkitCollectionEntry>().Last();
            canceledEntry.Name = "Canceled Dialog Entry";
            canceledEntry.Category = "Rollback";
            canceledEntry.Weight = 999;

            var firstEntry = innerControl.Items.OfType<ToolkitCollectionEntry>().First();
            firstEntry.Name = "Canceled Mutation";
            firstEntry.Category = "Rollback";
            firstEntry.Weight = -1;
            innerControl.SelectedItem = canceledEntry;

            Button cancelButton = FindCollectionDialogButton(dialog, "Cancel", isDefault: false);
            cancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, cancelButton));
            cancelTimer.Stop();
        };

        cancelTimer.Start();
        try
        {
            OpenCollectionDialogButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, OpenCollectionDialogButton));
        }
        finally
        {
            cancelTimer.Stop();
        }

        if (!canceledDialog)
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlButton Cancel path to open a CollectionControlDialog.");
        }

        AssertEqual(updateCountBefore, ViewModel.CollectionDialogUpdateCount, "Toolkit CollectionControlButton canceled dialog update count after rollback");
        AssertEqual(originalEntries.Count, ViewModel.CollectionEntries.Count, "Toolkit CollectionControlDialog rollback entry count");
        for (int i = 0; i < originalEntries.Count; i++)
        {
            AssertEqual(originalEntries[i].Name, ViewModel.CollectionEntries[i].Name, $"Toolkit CollectionControlDialog rollback entry {i} name");
            AssertEqual(originalEntries[i].Category, ViewModel.CollectionEntries[i].Category, $"Toolkit CollectionControlDialog rollback entry {i} category");
            AssertEqual(originalEntries[i].Weight, ViewModel.CollectionEntries[i].Weight, $"Toolkit CollectionControlDialog rollback entry {i} weight");
        }

        if (ViewModel.CollectionEntries.Any(entry => string.Equals(entry.Name, "Canceled Dialog Entry", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlDialog cancel to discard the added entry.");
        }

        ViewModel.SelectedCollectionEntry = ViewModel.CollectionEntries[0];
        ToolkitCollectionControl.SelectedItem = ViewModel.SelectedCollectionEntry;
        ValidateToolkitCollectionControlState(expectLoaded: true);
        ValidateToolkitCollectionDialogButtonState(expectLoaded: true);
    }

    internal void ValidateToolkitCollectionDialogButtonState(bool expectLoaded)
    {
        if (!ReferenceEquals(OpenCollectionDialogButton.ItemsSource, ViewModel.CollectionEntries))
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlButton ItemsSource to bind the view-model collection.");
        }

        AssertEqual(typeof(ToolkitCollectionEntry), OpenCollectionDialogButton.ItemsSourceType, "Toolkit CollectionControlButton items source type");
        AssertEqual(false, OpenCollectionDialogButton.IsReadOnly, "Toolkit CollectionControlButton read-only state");
        AssertEqual("Open dialog", Convert.ToString(OpenCollectionDialogButton.Content, CultureInfo.InvariantCulture), "Toolkit CollectionControlButton content");

        if (OpenCollectionDialogButton.NewItemTypes is null ||
            !OpenCollectionDialogButton.NewItemTypes.OfType<Type>().Contains(typeof(ToolkitCollectionEntry)))
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlButton NewItemTypes to include the sample entry type.");
        }

        if (expectLoaded)
        {
            OpenCollectionDialogButton.ApplyTemplate();
            OpenCollectionDialogButton.UpdateLayout();
            if (OpenCollectionDialogButton.ActualWidth <= 0 ||
                OpenCollectionDialogButton.ActualHeight <= 0)
            {
                throw new InvalidOperationException("Expected loaded Toolkit CollectionControlButton to participate in layout.");
            }
        }
    }

    private ToolkitCollectionDialogScope CreateToolkitCollectionControlDialog()
    {
        var dialog = new CollectionControlDialog
        {
            ItemsSource = ViewModel.CollectionEntries,
            ItemsSourceType = typeof(ToolkitCollectionEntry),
            NewItemTypes = ViewModel.CollectionEntryTypes.ToList(),
            IsReadOnly = false,
            EditorDefinitions = OpenCollectionDialogButton.EditorDefinitions
        };

        return new ToolkitCollectionDialogScope(dialog);
    }

    private void ValidateToolkitCollectionDialogInstance(CollectionControlDialog dialog, bool expectLoaded)
    {
        if (!ReferenceEquals(dialog.ItemsSource, ViewModel.CollectionEntries))
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlDialog ItemsSource to bind the view-model collection.");
        }

        AssertEqual(typeof(ToolkitCollectionEntry), dialog.ItemsSourceType, "Toolkit CollectionControlDialog items source type");
        AssertEqual(false, dialog.IsReadOnly, "Toolkit CollectionControlDialog read-only state");
        if (dialog.NewItemTypes is null ||
            !dialog.NewItemTypes.OfType<Type>().Contains(typeof(ToolkitCollectionEntry)))
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlDialog NewItemTypes to include the sample entry type.");
        }

        var innerControl = dialog.CollectionControl;
        if (innerControl is null)
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlDialog to create an embedded CollectionControl.");
        }

        innerControl.ApplyTemplate();
        dialog.UpdateLayout();
        innerControl.UpdateLayout();

        if (!expectLoaded)
        {
            return;
        }

        if (!ReferenceEquals(innerControl.ItemsSource, ViewModel.CollectionEntries))
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlDialog embedded CollectionControl to bind ItemsSource.");
        }

        AssertEqual(typeof(ToolkitCollectionEntry), innerControl.ItemsSourceType, "Toolkit CollectionControlDialog embedded items source type");
        AssertEqual(false, innerControl.IsReadOnly, "Toolkit CollectionControlDialog embedded read-only state");
        if (innerControl.NewItemTypes is null ||
            !innerControl.NewItemTypes.OfType<Type>().Contains(typeof(ToolkitCollectionEntry)))
        {
            throw new InvalidOperationException("Expected Toolkit CollectionControlDialog embedded NewItemTypes to include the sample entry type.");
        }
    }

    private static Button FindCollectionDialogButton(CollectionControlDialog dialog, string content, bool isDefault)
    {
        dialog.ApplyTemplate();
        dialog.UpdateLayout();

        return EnumerateVisualDescendants<Button>(dialog)
            .FirstOrDefault(button =>
                button.IsDefault == isDefault &&
                string.Equals(Convert.ToString(button.Content, CultureInfo.InvariantCulture), content, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Expected Toolkit CollectionControlDialog '{content}' button.");
    }

    private static IEnumerable<T> EnumerateVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in EnumerateVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void SerializeLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.LastSerializedLayout = SerializeCurrentLayout();
        _viewModel.Status = "AvalonDock layout serialized";
        _viewModel.Activity.Add("Serialized AvalonDock layout");
    }

    internal string SerializeCurrentLayout()
    {
        using var stream = new MemoryStream();
        var serializer = new XmlLayoutSerializer(DockManager);
        serializer.Serialize(stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static DockingManager RoundTripLayout(string layoutXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutXml);

        var manager = new DockingManager();
        var serializer = new XmlLayoutSerializer(manager);
        serializer.LayoutSerializationCallback += (_, args) =>
        {
            args.Content ??= new TextBlock
            {
                Text = args.Model.ContentId,
                Margin = new Thickness(8)
            };
        };

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(layoutXml));
        serializer.Deserialize(stream);
        return manager;
    }

    internal ToolkitViewModel ViewModel => _viewModel;

    internal Magnifier ToolkitMagnifier => (Magnifier)FindResource("ToolkitMagnifierResource");

    internal void ValidateEditorFloatingState(bool expectedFloating)
    {
        bool documentPaneContainsEditor = DocumentPane.Children.Any(document => ReferenceEquals(document, EditorDocument));
        if (expectedFloating)
        {
            AssertEqual(true, EditorDocument.IsFloating, "AvalonDock editor document floating state");
            AssertEqual(false, documentPaneContainsEditor, "AvalonDock editor document pane membership while floating");
            if (DockLayoutRoot.FloatingWindows.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected one AvalonDock floating window model, got {DockLayoutRoot.FloatingWindows.Count}.");
            }

            AssertEqual("Dock editor", Convert.ToString(ToggleEditorFloatButton.Content, CultureInfo.InvariantCulture), "AvalonDock float toggle content");
            if (ViewModel.AvalonDockFloatedCount <= 0)
            {
                throw new InvalidOperationException("Expected AvalonDock Floated event to fire for the editor document.");
            }
        }
        else
        {
            AssertEqual(false, EditorDocument.IsFloating, "AvalonDock editor document floating state");
            AssertEqual(true, documentPaneContainsEditor, "AvalonDock editor document pane membership after docking");
            if (DockLayoutRoot.FloatingWindows.Count != 0)
            {
                throw new InvalidOperationException(
                    $"Expected no AvalonDock floating window models after docking, got {DockLayoutRoot.FloatingWindows.Count}.");
            }

            AssertEqual("Float editor", Convert.ToString(ToggleEditorFloatButton.Content, CultureInfo.InvariantCulture), "AvalonDock float toggle content");
            if (ViewModel.AvalonDockDockedCount <= 0)
            {
                throw new InvalidOperationException("Expected AvalonDock Docked event to fire for the editor document.");
            }
        }
    }

    internal void ValidatePropertyPaneAnchorableLifecycle(
        int hidingCountBefore,
        int visibleChangedCountBefore,
        bool expectedHidden)
    {
        AssertEqual(expectedHidden, PropertyPane.IsHidden, "AvalonDock property pane hidden state");
        AssertEqual(
            PropertyPane.ContentId ?? PropertyPane.Title,
            ViewModel.LastAvalonDockAnchorableLifecycleTarget,
            "AvalonDock anchorable lifecycle target");

        if (expectedHidden)
        {
            if (!DockLayoutRoot.Hidden.Contains(PropertyPane))
            {
                throw new InvalidOperationException("Expected AvalonDock property pane to be in the hidden collection.");
            }

            AssertEqual(
                hidingCountBefore + 1,
                ViewModel.AvalonDockAnchorableHidingCount,
                "AvalonDock property pane Hiding event count");
            if (ViewModel.AvalonDockAnchorableIsVisibleChangedCount <= visibleChangedCountBefore)
            {
                throw new InvalidOperationException("Expected AvalonDock property pane IsVisibleChanged event to fire while hiding.");
            }
        }
        else
        {
            if (DockLayoutRoot.Hidden.Contains(PropertyPane))
            {
                throw new InvalidOperationException("Expected AvalonDock property pane to leave the hidden collection.");
            }

            if (ViewModel.AvalonDockAnchorableIsVisibleChangedCount <= visibleChangedCountBefore)
            {
                throw new InvalidOperationException("Expected AvalonDock property pane IsVisibleChanged event to fire while showing.");
            }
        }
    }

    internal void ValidateActivityPaneClosedState(int closingCountBefore, int closedCountBefore)
    {
        if (RightAnchorablePane.Children.Contains(ActivityPane))
        {
            throw new InvalidOperationException("Expected AvalonDock activity anchorable to leave its pane after close.");
        }

        AssertEqual("activity", ViewModel.LastClosingAnchorableContentId, "AvalonDock last closing anchorable content id");
        AssertEqual("activity", ViewModel.LastClosedAnchorableContentId, "AvalonDock last closed anchorable content id");
        AssertEqual(
            closingCountBefore + 1,
            ViewModel.AvalonDockAnchorableClosingCount,
            "AvalonDock activity anchorable Closing event count");
        AssertEqual(
            closedCountBefore + 1,
            ViewModel.AvalonDockAnchorableClosedCount,
            "AvalonDock activity anchorable Closed event count");
    }

    internal void ValidateActivityPaneReopenedState()
    {
        if (!RightAnchorablePane.Children.Contains(ActivityPane))
        {
            throw new InvalidOperationException("Expected AvalonDock activity anchorable to be restored to its pane.");
        }

        AssertEqual(true, ActivityPane.IsSelected, "AvalonDock activity anchorable selected state after reopen");
        AssertEqual(true, ActivityPane.IsActive, "AvalonDock activity anchorable active state after reopen");
    }

    internal void ValidateOverviewDocumentLifecycleState(bool expectedOpen)
    {
        bool documentPaneContainsOverview = DocumentPane.Children.Any(document => ReferenceEquals(document, OverviewDocument));
        AssertEqual(expectedOpen, documentPaneContainsOverview, "AvalonDock overview document pane membership");

        if (expectedOpen)
        {
            AssertEqual(true, OverviewDocument.IsSelected, "AvalonDock overview document selected state after reopen");
            AssertEqual(true, OverviewDocument.IsActive, "AvalonDock overview document active state after reopen");
        }
        else
        {
            AssertEqual("overview", ViewModel.LastClosedDocumentContentId, "AvalonDock last closed document content id");
            if (ViewModel.AvalonDockDocumentClosedCount <= 0 ||
                ViewModel.OverviewDocumentClosedCount <= 0)
            {
                throw new InvalidOperationException("Expected AvalonDock document closed events to fire for the overview document.");
            }
        }
    }

    internal void ValidateOverviewCloseCanceledState(int expectedDocumentCount, int expectedClosedCount)
    {
        if (!DocumentPane.Children.Contains(OverviewDocument))
        {
            throw new InvalidOperationException("Expected canceled AvalonDock overview close to keep the document in the pane.");
        }

        AssertEqual(expectedDocumentCount, DocumentPane.ChildrenCount, "AvalonDock document count after canceled overview close");
        AssertEqual(expectedClosedCount, ViewModel.OverviewDocumentClosedCount, "AvalonDock overview closed count after canceled close");
        AssertEqual("overview", ViewModel.LastClosingDocumentContentId, "AvalonDock last closing document content id");
        if (ViewModel.AvalonDockDocumentClosingCount <= 0 ||
            ViewModel.AvalonDockDocumentCloseCanceledCount <= 0)
        {
            throw new InvalidOperationException("Expected AvalonDock document closing and cancellation events to fire for the overview document.");
        }

        AssertEqual(false, ViewModel.CancelNextOverviewClose, "AvalonDock cancel next close reset state");
        AssertEqual("Overview document close canceled", ViewModel.Status, "AvalonDock canceled close status");
    }

    internal void ValidateToolkitPopupState(bool expectedOpen)
    {
        AssertEqual(expectedOpen, CategoryPicker.IsDropDownOpen, "Toolkit CheckComboBox dropdown state");
        AssertEqual(expectedOpen, ReminderTimePicker.IsOpen, "Toolkit TimePicker popup state");
        AssertEqual(expectedOpen, AccentColorPicker.IsOpen, "Toolkit ColorPicker popup state");
        AssertEqual(expectedOpen, EstimateEditor.IsOpen, "Toolkit CalculatorUpDown popup state");
        AssertEqual(expectedOpen, ActionDropDownButton.IsOpen, "Toolkit DropDownButton popup state");

        if (expectedOpen)
        {
            var popupSource = PresentationSource.FromVisual(ActionDropDownContentRoot);
            if (popupSource is not HwndSource ||
                popupSource.CompositionTarget == null)
            {
                throw new InvalidOperationException(
                    "Expected Xceed dropdown content to be rooted in the portable public HwndSource facade while open.");
            }
        }
    }

    internal void ValidateToolkitSplitButtonPopupState(bool expectedOpen)
    {
        AssertEqual(expectedOpen, SplitActionButton.IsOpen, "Toolkit SplitButton popup state");

        if (expectedOpen)
        {
            var splitPopupSource = PresentationSource.FromVisual(SplitActionDropDownContentRoot);
            if (splitPopupSource is not HwndSource ||
                splitPopupSource.CompositionTarget == null)
            {
                throw new InvalidOperationException(
                    "Expected Xceed SplitButton dropdown content to be rooted in the portable public HwndSource facade while open.");
            }

            if (OwnerPickerList.Items.Count != ViewModel.Owners.Count)
            {
                throw new InvalidOperationException("Expected Toolkit SplitButton list content to bind all owners while open.");
            }
        }
    }

    internal void ValidateToolkitResourceThemeState(bool expectLoaded)
    {
        string filterWatermark = RequireResourceString("ToolkitFilterWatermark", "Toolkit filter watermark");
        SolidColorBrush rangeLowerBrush = RequireResourceBrush("ToolkitRangeLowerBrush", "Toolkit lower range brush");
        SolidColorBrush rangeHigherBrush = RequireResourceBrush("ToolkitRangeHigherBrush", "Toolkit higher range brush");

        AssertEqual(filterWatermark, Convert.ToString(FilterTextBox.Watermark, CultureInfo.InvariantCulture), "Toolkit WatermarkTextBox watermark resource");
        AssertBrushColor(rangeLowerBrush, PriorityRangeSlider.LowerRangeBackground, "Toolkit RangeSlider lower range background");
        AssertBrushColor(rangeHigherBrush, PriorityRangeSlider.HigherRangeBackground, "Toolkit RangeSlider higher range background");

        if (expectLoaded)
        {
            FilterTextBox.ApplyTemplate();
            ActionDropDownButton.ApplyTemplate();
            SplitActionButton.ApplyTemplate();
            PriorityRangeSlider.ApplyTemplate();
            FilterTextBox.UpdateLayout();
            ActionDropDownButton.UpdateLayout();
            SplitActionButton.UpdateLayout();
            PriorityRangeSlider.UpdateLayout();

            if (FilterTextBox.ActualWidth <= 0 ||
                ActionDropDownButton.ActualWidth <= 0 ||
                SplitActionButton.ActualWidth <= 0 ||
                PriorityRangeSlider.ActualWidth <= 0)
            {
                throw new InvalidOperationException("Expected loaded Toolkit resource-themed controls to participate in layout.");
            }
        }
    }

    internal void ExerciseToolkitResourceTheme()
    {
        ValidateToolkitResourceThemeState(expectLoaded: true);

        System.Windows.ResourceDictionary resources = Application.Current?.Resources ?? Resources;
        object originalFilterWatermark = resources["ToolkitFilterWatermark"];
        object originalRangeLowerBrush = resources["ToolkitRangeLowerBrush"];
        object originalRangeHigherBrush = resources["ToolkitRangeHigherBrush"];

        const string updatedFilterWatermark = "Filter SDK documents";
        var updatedRangeLowerBrush = new SolidColorBrush(Colors.LightSkyBlue);
        var updatedRangeHigherBrush = new SolidColorBrush(Colors.PaleGreen);

        try
        {
            resources["ToolkitFilterWatermark"] = updatedFilterWatermark;
            resources["ToolkitRangeLowerBrush"] = updatedRangeLowerBrush;
            resources["ToolkitRangeHigherBrush"] = updatedRangeHigherBrush;
            PumpDispatcherUntil(
                this,
                () => string.Equals(
                          updatedFilterWatermark,
                          Convert.ToString(FilterTextBox.Watermark, CultureInfo.InvariantCulture),
                          StringComparison.Ordinal) &&
                      BrushColorEquals(updatedRangeLowerBrush, PriorityRangeSlider.LowerRangeBackground) &&
                      BrushColorEquals(updatedRangeHigherBrush, PriorityRangeSlider.HigherRangeBackground),
                TimeSpan.FromSeconds(2),
                "Toolkit dynamic resource replacement");
            ValidateToolkitResourceThemeState(expectLoaded: true);

            ViewModel.ToolkitResourceThemeUpdateCount++;
            ViewModel.Status = $"Toolkit resources updated {ViewModel.ToolkitResourceThemeUpdateCount}";
            ViewModel.Activity.Add(ViewModel.Status);
        }
        finally
        {
            resources["ToolkitFilterWatermark"] = originalFilterWatermark;
            resources["ToolkitRangeLowerBrush"] = originalRangeLowerBrush;
            resources["ToolkitRangeHigherBrush"] = originalRangeHigherBrush;
        }

        PumpDispatcherUntil(
            this,
            () => string.Equals(
                      Convert.ToString(originalFilterWatermark, CultureInfo.InvariantCulture),
                      Convert.ToString(FilterTextBox.Watermark, CultureInfo.InvariantCulture),
                      StringComparison.Ordinal) &&
                  BrushColorEquals((SolidColorBrush)originalRangeLowerBrush, PriorityRangeSlider.LowerRangeBackground) &&
                  BrushColorEquals((SolidColorBrush)originalRangeHigherBrush, PriorityRangeSlider.HigherRangeBackground),
            TimeSpan.FromSeconds(2),
            "Toolkit dynamic resource restore");
        ValidateToolkitResourceThemeState(expectLoaded: true);
    }

    private SolidColorBrush RequireResourceBrush(string resourceKey, string description)
    {
        return TryFindResource(resourceKey) as SolidColorBrush
            ?? throw new InvalidOperationException($"Expected {description} resource '{resourceKey}' to resolve to a SolidColorBrush.");
    }

    private string RequireResourceString(string resourceKey, string description)
    {
        return TryFindResource(resourceKey) as string
            ?? throw new InvalidOperationException($"Expected {description} resource '{resourceKey}' to resolve to a string.");
    }

    private static void AssertBrushColor(SolidColorBrush expected, Brush? actual, string description)
    {
        if (actual is not SolidColorBrush actualBrush)
        {
            throw new InvalidOperationException($"Expected {description} to resolve to a SolidColorBrush.");
        }

        AssertEqual(expected.Color, actualBrush.Color, description);
    }

    private static bool BrushColorEquals(SolidColorBrush expected, Brush? actual)
    {
        return actual is SolidColorBrush actualBrush && actualBrush.Color == expected.Color;
    }

    internal void ValidateToolkitChildWindowState(bool expectedOpen)
    {
        AssertEqual(true, ToolkitChildWindowContainer.Children.Contains(ToolkitChildWindow), "Toolkit WindowContainer child membership");
        AssertEqual(true, ToolkitChildWindow.IsModal, "Toolkit ChildWindow modal state");
        AssertEqual(Xceed.Wpf.Toolkit.WindowStartupLocation.Center, ToolkitChildWindow.WindowStartupLocation, "Toolkit ChildWindow startup location");
        AssertEqual(ChildWindowInputTextBox, ToolkitChildWindow.FocusedElement, "Toolkit ChildWindow focused element");

        if (ToolkitChildWindowContainer.ActualWidth > 0 &&
            ToolkitChildWindowContainer.ActualHeight > 0 &&
            (ToolkitChildWindow.ActualWidth <= 0 || ToolkitChildWindow.ActualHeight <= 0) &&
            expectedOpen)
        {
            throw new InvalidOperationException("Expected open Toolkit ChildWindow to participate in layout.");
        }

        if (expectedOpen)
        {
            AssertEqual(Xceed.Wpf.Toolkit.WindowState.Open, ToolkitChildWindow.WindowState, "Toolkit ChildWindow open state");
            AssertEqual("ChildWindow open", ViewModel.ChildWindowStatus, "Toolkit ChildWindow open status");
            AssertEqual((bool?)null, ToolkitChildWindow.DialogResult, "Toolkit ChildWindow dialog result while open");
            if (!ChildWindowInputTextBox.Text.Contains("Child input", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected Toolkit ChildWindow input content to be initialized.");
            }
        }
        else
        {
            AssertEqual(Xceed.Wpf.Toolkit.WindowState.Closed, ToolkitChildWindow.WindowState, "Toolkit ChildWindow closed state");
            if (ViewModel.ChildWindowShowCount > 0 &&
                (ViewModel.ChildWindowClosingCount <= 0 || ViewModel.ChildWindowClosedCount <= 0))
            {
                throw new InvalidOperationException("Expected Toolkit ChildWindow closing and closed events to fire.");
            }
        }
    }

    internal void ValidateToolkitMessageBoxState(bool expectedOpen)
    {
        AssertEqual(true, ToolkitChildWindowContainer.Children.Contains(ToolkitMessageBox), "Toolkit MessageBox WindowContainer membership");
        AssertEqual("OK", Convert.ToString(ToolkitMessageBox.OkButtonContent, CultureInfo.InvariantCulture), "Toolkit MessageBox OK button content");

        if (expectedOpen)
        {
            AssertEqual(Visibility.Visible, ToolkitMessageBox.Visibility, "Toolkit MessageBox open visibility");
            AssertEqual("Toolkit message", Convert.ToString(ToolkitMessageBox.Caption, CultureInfo.InvariantCulture), "Toolkit MessageBox caption");
            AssertEqual("MessageBox inside Xceed WindowContainer", ToolkitMessageBox.Text, "Toolkit MessageBox text");
            AssertEqual(MessageBoxResult.None, ToolkitMessageBox.MessageBoxResult, "Toolkit MessageBox result while open");
            AssertEqual("MessageBox open", ViewModel.ToolkitMessageBoxStatus, "Toolkit MessageBox open status");
            if (GetToolkitMessageBoxButton("PART_OkButton") is not { IsDefault: true })
            {
                throw new InvalidOperationException("Expected Toolkit MessageBox OK button to be the default button.");
            }
        }
        else
        {
            AssertEqual(Visibility.Collapsed, ToolkitMessageBox.Visibility, "Toolkit MessageBox closed visibility");
            if (ViewModel.ToolkitMessageBoxShowCount > 0 &&
                ViewModel.ToolkitMessageBoxClosedCount <= 0)
            {
                throw new InvalidOperationException("Expected Toolkit MessageBox Closed event to fire.");
            }
        }
    }

    internal Button GetToolkitMessageBoxButton(string partName)
    {
        ToolkitMessageBox.ApplyTemplate();
        ToolkitMessageBox.UpdateLayout();
        return ToolkitMessageBox.Template?.FindName(partName, ToolkitMessageBox) as Button
            ?? throw new InvalidOperationException($"Expected Toolkit MessageBox template button '{partName}'.");
    }

    internal void ValidateStaticToolkitMessageBoxState(bool expectedValidated)
    {
        if (!expectedValidated)
        {
            AssertEqual(0, ViewModel.StaticToolkitMessageBoxShowCount, "Toolkit static MessageBox initial show count");
            AssertEqual(0, ViewModel.StaticToolkitMessageBoxClosedCount, "Toolkit static MessageBox initial closed count");
            AssertEqual(MessageBoxResult.None, ViewModel.LastStaticToolkitMessageBoxResult, "Toolkit static MessageBox initial result");
            AssertEqual("Static message idle", ViewModel.StaticToolkitMessageBoxStatus, "Toolkit static MessageBox initial status");
            return;
        }

        AssertEqual(ViewModel.StaticToolkitMessageBoxShowCount, ViewModel.StaticToolkitMessageBoxClosedCount, "Toolkit static MessageBox balanced lifecycle count");
        if (ViewModel.StaticToolkitMessageBoxShowCount < 2)
        {
            throw new InvalidOperationException("Expected Toolkit static MessageBox validation to cover both Window owner and IntPtr owner overloads.");
        }

        AssertEqual(MessageBoxResult.No, ViewModel.LastStaticToolkitMessageBoxResult, "Toolkit static MessageBox final result");
        AssertEqual("Owner handle static MessageBox No", ViewModel.StaticToolkitMessageBoxStatus, "Toolkit static MessageBox final status");
        if (ViewModel.LastStaticToolkitMessageBoxOwnerHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Expected Toolkit static MessageBox owner-handle overload to use a non-zero portable owner handle.");
        }
    }

    internal void ValidateAvalonDockDocumentContextMenuState(bool expectedOpen)
    {
        AssertEqual(expectedOpen, DockDocumentContextMenu.IsOpen, "AvalonDock document context menu open state");
        if (DockManager.DocumentContextMenu != DockDocumentContextMenu)
        {
            throw new InvalidOperationException("Expected AvalonDock DockingManager to expose the sample document context menu.");
        }

        if (expectedOpen)
        {
            var menuSource = PresentationSource.FromVisual(DockContextActivateEditorMenuItem);
            if (menuSource is not HwndSource ||
                menuSource.CompositionTarget == null)
            {
                throw new InvalidOperationException(
                    "Expected AvalonDock document context menu to be rooted in the portable public HwndSource facade while open.");
            }

            DockContextActivateEditorMenuItem.GetBindingExpression(MenuItem.CommandTargetProperty)?.UpdateTarget();
            DockContextCloseOverviewMenuItem.GetBindingExpression(MenuItem.CommandTargetProperty)?.UpdateTarget();
            AssertEqual((ICommand)ToolkitDockCommands.ActivateEditor, DockContextActivateEditorMenuItem.Command, "AvalonDock context menu activate command");
            AssertEqual((ICommand)ToolkitDockCommands.CloseOverview, DockContextCloseOverviewMenuItem.Command, "AvalonDock context menu close command");
            AssertEqual((IInputElement)DockManager, DockContextActivateEditorMenuItem.CommandTarget, "AvalonDock context menu activate command target");
            AssertEqual((IInputElement)DockManager, DockContextCloseOverviewMenuItem.CommandTarget, "AvalonDock context menu close command target");

            int canExecuteCountBefore = ViewModel.AvalonDockContextMenuCommandCanExecuteCount;
            if (!CanExecuteDockContextCommand(DockContextActivateEditorMenuItem) ||
                !CanExecuteDockContextCommand(DockContextCloseOverviewMenuItem))
            {
                throw new InvalidOperationException("Expected AvalonDock document context menu commands to be executable while startup documents are open.");
            }

            if (ViewModel.AvalonDockContextMenuCommandCanExecuteCount <= canExecuteCountBefore)
            {
                throw new InvalidOperationException("Expected AvalonDock document context menu commands to route CanExecute through the window command bindings.");
            }
        }
    }

    internal void ExerciseAvalonDockDocumentContextMenuCommands()
    {
        bool closeMenuAfterExercise = false;
        if (!DockDocumentContextMenu.IsOpen)
        {
            DockDocumentContextMenu.PlacementTarget = DockManager;
            DockDocumentContextMenu.IsOpen = true;
            closeMenuAfterExercise = true;
        }

        ValidateAvalonDockDocumentContextMenuState(expectedOpen: true);

        int executedCountBefore = ViewModel.AvalonDockContextMenuCommandExecutedCount;
        ExecuteDockContextCommand(DockContextActivateEditorMenuItem);
        AssertEqual(executedCountBefore + 1, ViewModel.AvalonDockContextMenuCommandExecutedCount, "AvalonDock context menu command executed count");
        AssertEqual("ActivateEditor", ViewModel.LastAvalonDockContextMenuCommand, "AvalonDock last context menu command");
        AssertEqual(true, EditorDocument.IsSelected, "AvalonDock context menu activate command selected editor");
        AssertEqual(true, EditorDocument.IsActive, "AvalonDock context menu activate command activated editor");
        AssertEqual("Editor document activated", ViewModel.Status, "AvalonDock context menu activate command status");

        if (closeMenuAfterExercise)
        {
            DockDocumentContextMenu.IsOpen = false;
        }
    }

    internal void ValidateAvalonDockAnchorableContextMenuState(bool expectedOpen)
    {
        AssertEqual(expectedOpen, DockAnchorableContextMenu.IsOpen, "AvalonDock anchorable context menu open state");
        if (DockManager.AnchorableContextMenu != DockAnchorableContextMenu)
        {
            throw new InvalidOperationException("Expected AvalonDock DockingManager to expose the sample anchorable context menu.");
        }

        if (expectedOpen)
        {
            var menuSource = PresentationSource.FromVisual(DockAnchorContextActivateToolkitMenuItem);
            if (menuSource is not HwndSource ||
                menuSource.CompositionTarget == null)
            {
                throw new InvalidOperationException(
                    "Expected AvalonDock anchorable context menu to be rooted in the portable public HwndSource facade while open.");
            }

            DockAnchorContextActivateToolkitMenuItem.GetBindingExpression(MenuItem.CommandTargetProperty)?.UpdateTarget();
            DockAnchorContextTogglePropertyMenuItem.GetBindingExpression(MenuItem.CommandTargetProperty)?.UpdateTarget();
            AssertEqual((ICommand)ToolkitDockCommands.ActivateToolkitPane, DockAnchorContextActivateToolkitMenuItem.Command, "AvalonDock anchorable context menu activate command");
            AssertEqual((ICommand)ToolkitDockCommands.TogglePropertyPane, DockAnchorContextTogglePropertyMenuItem.Command, "AvalonDock anchorable context menu toggle property command");
            AssertEqual((IInputElement)DockManager, DockAnchorContextActivateToolkitMenuItem.CommandTarget, "AvalonDock anchorable context menu activate command target");
            AssertEqual((IInputElement)DockManager, DockAnchorContextTogglePropertyMenuItem.CommandTarget, "AvalonDock anchorable context menu toggle command target");

            int canExecuteCountBefore = ViewModel.AvalonDockAnchorableContextMenuCommandCanExecuteCount;
            if (!CanExecuteDockContextCommand(DockAnchorContextActivateToolkitMenuItem) ||
                !CanExecuteDockContextCommand(DockAnchorContextTogglePropertyMenuItem))
            {
                throw new InvalidOperationException("Expected AvalonDock anchorable context menu commands to be executable.");
            }

            if (ViewModel.AvalonDockAnchorableContextMenuCommandCanExecuteCount <= canExecuteCountBefore)
            {
                throw new InvalidOperationException("Expected AvalonDock anchorable context menu commands to route CanExecute through the window command bindings.");
            }
        }
    }

    internal void ExerciseAvalonDockAnchorableContextMenuCommands()
    {
        bool closeMenuAfterExercise = false;
        if (!DockAnchorableContextMenu.IsOpen)
        {
            DockAnchorableContextMenu.PlacementTarget = DockManager;
            DockAnchorableContextMenu.IsOpen = true;
            closeMenuAfterExercise = true;
        }

        if (PropertyPane.IsHidden)
        {
            PropertyPane.Show();
            Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
        }

        ValidateAvalonDockAnchorableContextMenuState(expectedOpen: true);

        int executedCountBefore = ViewModel.AvalonDockAnchorableContextMenuCommandExecutedCount;
        ExecuteDockContextCommand(DockAnchorContextActivateToolkitMenuItem);
        AssertEqual(executedCountBefore + 1, ViewModel.AvalonDockAnchorableContextMenuCommandExecutedCount, "AvalonDock anchorable context menu command executed count");
        AssertEqual("ActivateToolkitPane", ViewModel.LastAvalonDockAnchorableContextMenuCommand, "AvalonDock last anchorable context menu command");
        AssertEqual(true, ToolkitPane.IsActive, "AvalonDock anchorable context menu activated Toolkit pane");
        AssertEqual("Toolkit pane activated", ViewModel.Status, "AvalonDock anchorable context menu activate status");

        ExecuteDockContextCommand(DockAnchorContextTogglePropertyMenuItem);
        AssertEqual(executedCountBefore + 2, ViewModel.AvalonDockAnchorableContextMenuCommandExecutedCount, "AvalonDock anchorable context menu toggle command executed count");
        AssertEqual("TogglePropertyPane", ViewModel.LastAvalonDockAnchorableContextMenuCommand, "AvalonDock last anchorable context menu toggle command");
        AssertEqual(true, PropertyPane.IsHidden, "AvalonDock anchorable context menu hidden property pane");
        AssertEqual("Property pane hidden", ViewModel.Status, "AvalonDock anchorable context menu toggle status");

        PropertyPane.Show();
        Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);

        if (closeMenuAfterExercise)
        {
            DockAnchorableContextMenu.IsOpen = false;
        }
    }

    internal void ExerciseAvalonDockKeyboardNavigation()
    {
        var keyBinding = InputBindings
            .OfType<KeyBinding>()
            .SingleOrDefault(binding => ReferenceEquals(binding.Command, ToolkitDockCommands.CycleDockContent))
            ?? throw new InvalidOperationException("Expected AvalonDock keyboard navigation KeyBinding.");
        AssertEqual(Key.F9, keyBinding.Key, "AvalonDock keyboard navigation key");
        AssertEqual(ModifierKeys.None, keyBinding.Modifiers, "AvalonDock keyboard navigation modifiers");

        ActivateEditorDocument();
        int canExecuteCountBefore = ViewModel.AvalonDockKeyboardNavigationCanExecuteCount;
        if (!ToolkitDockCommands.CycleDockContent.CanExecute(null, this))
        {
            throw new InvalidOperationException("Expected AvalonDock keyboard navigation command to be executable.");
        }

        if (ViewModel.AvalonDockKeyboardNavigationCanExecuteCount <= canExecuteCountBefore)
        {
            throw new InvalidOperationException("Expected AvalonDock keyboard navigation command to route CanExecute.");
        }

        int navigationCountBefore = ViewModel.AvalonDockKeyboardNavigationCount;
        ToolkitDockCommands.CycleDockContent.Execute(null, this);
        ValidateAvalonDockKeyboardNavigationTarget(OverviewDocument, navigationCountBefore + 1);

        ToolkitDockCommands.CycleDockContent.Execute(null, this);
        ValidateAvalonDockKeyboardNavigationTarget(EditorDocument, navigationCountBefore + 2);
    }

    internal void ValidateAvalonDockKeyboardNavigationTarget(LayoutContent expectedContent, int expectedNavigationCount)
    {
        if (expectedContent is LayoutDocument)
        {
            AssertEqual(true, expectedContent.IsSelected, "AvalonDock keyboard navigation selected document target");
        }

        if (expectedContent.Parent is ILayoutContentSelector selector &&
            !ReferenceEquals(selector.SelectedContent, expectedContent))
        {
            throw new InvalidOperationException(
                $"Expected AvalonDock keyboard navigation to select '{expectedContent.Title}' in its pane.");
        }

        bool activeContentMatches = ReferenceEquals(DockManager.ActiveContent, expectedContent.Content) ||
            ReferenceEquals(DockManager.ActiveContent, expectedContent) ||
            ReferenceEquals(DockLayoutRoot.ActiveContent, expectedContent);
        if (!activeContentMatches &&
            !expectedContent.IsActive)
        {
            throw new InvalidOperationException(
                $"Expected AvalonDock keyboard navigation to activate '{expectedContent.Title}', but active content was '{DockManager.ActiveContent}'.");
        }

        AssertEqual(expectedNavigationCount, ViewModel.AvalonDockKeyboardNavigationCount, "AvalonDock keyboard navigation count");
        AssertEqual(expectedContent.ContentId ?? expectedContent.Title, ViewModel.LastAvalonDockKeyboardNavigationTarget, "AvalonDock keyboard navigation last target");
        AssertEqual($"Keyboard dock navigation: {expectedContent.Title}", ViewModel.Status, "AvalonDock keyboard navigation status");
    }

    internal void ExerciseAvalonDockAnchorableKeyboardNavigation()
    {
        var keyBinding = InputBindings
            .OfType<KeyBinding>()
            .SingleOrDefault(binding => ReferenceEquals(binding.Command, ToolkitDockCommands.CycleDockAnchorable))
            ?? throw new InvalidOperationException("Expected AvalonDock anchorable keyboard navigation KeyBinding.");
        AssertEqual(Key.F10, keyBinding.Key, "AvalonDock anchorable keyboard navigation key");
        AssertEqual(ModifierKeys.None, keyBinding.Modifiers, "AvalonDock anchorable keyboard navigation modifiers");

        DockManager.ActiveContent = ToolkitPane.Content;
        SelectAvalonDockAnchorable(ToolkitPane);
        ToolkitPane.IsActive = true;
        _avalonDockAnchorableKeyboardNavigationIndex = 0;
        ViewModel.LastAvalonDockAnchorableKeyboardNavigationTarget = ToolkitPane.ContentId ?? ToolkitPane.Title;
        int canExecuteCountBefore = ViewModel.AvalonDockAnchorableKeyboardNavigationCanExecuteCount;
        if (!ToolkitDockCommands.CycleDockAnchorable.CanExecute(null, this))
        {
            throw new InvalidOperationException("Expected AvalonDock anchorable keyboard navigation command to be executable.");
        }

        if (ViewModel.AvalonDockAnchorableKeyboardNavigationCanExecuteCount <= canExecuteCountBefore)
        {
            throw new InvalidOperationException("Expected AvalonDock anchorable keyboard navigation command to route CanExecute.");
        }

        int navigationCountBefore = ViewModel.AvalonDockAnchorableKeyboardNavigationCount;
        ToolkitDockCommands.CycleDockAnchorable.Execute(null, this);
        Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
        ValidateAvalonDockAnchorableKeyboardNavigationTarget(PropertyPane, navigationCountBefore + 1);

        ToolkitDockCommands.CycleDockAnchorable.Execute(null, this);
        Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
        ValidateAvalonDockAnchorableKeyboardNavigationTarget(ActivityPane, navigationCountBefore + 2);
    }

    internal void ValidateAvalonDockAnchorableKeyboardNavigationTarget(LayoutAnchorable expectedContent, int expectedNavigationCount)
    {
        bool selectedByPane = expectedContent.IsSelected ||
            expectedContent.Parent is ILayoutContentSelector activeSelector &&
            ReferenceEquals(activeSelector.SelectedContent, expectedContent);
        if (!selectedByPane && !expectedContent.IsActive)
        {
            throw new InvalidOperationException(
                $"Expected AvalonDock anchorable keyboard navigation to select or activate '{expectedContent.Title}'. State: {FormatAvalonDockAnchorableState(expectedContent)}");
        }

        if (expectedContent.Parent is ILayoutContentSelector selector &&
            !ReferenceEquals(selector.SelectedContent, expectedContent) &&
            !expectedContent.IsActive)
        {
            throw new InvalidOperationException(
                $"Expected AvalonDock anchorable keyboard navigation to select '{expectedContent.Title}' in its pane. State: {FormatAvalonDockAnchorableState(expectedContent)}");
        }

        bool activeContentMatches = ReferenceEquals(DockManager.ActiveContent, expectedContent.Content) ||
            ReferenceEquals(DockManager.ActiveContent, expectedContent);
        if (!activeContentMatches &&
            !expectedContent.IsActive)
        {
            throw new InvalidOperationException(
                $"Expected AvalonDock anchorable keyboard navigation to activate '{expectedContent.Title}', but active content was '{FormatAvalonDockActiveContent(DockManager.ActiveContent)}'. State: {FormatAvalonDockAnchorableState(expectedContent)}");
        }

        AssertEqual(expectedNavigationCount, ViewModel.AvalonDockAnchorableKeyboardNavigationCount, "AvalonDock anchorable keyboard navigation count");
        AssertEqual(expectedContent.ContentId ?? expectedContent.Title, ViewModel.LastAvalonDockAnchorableKeyboardNavigationTarget, "AvalonDock anchorable keyboard navigation last target");
        AssertEqual($"Keyboard anchorable navigation: {expectedContent.Title}", ViewModel.Status, "AvalonDock anchorable keyboard navigation status");
    }

    internal void ExerciseAvalonDockAutoHideOverlayKeyboardNavigation()
    {
        var keyBinding = InputBindings
            .OfType<KeyBinding>()
            .SingleOrDefault(binding => ReferenceEquals(binding.Command, ToolkitDockCommands.CycleAutoHideOverlay))
            ?? throw new InvalidOperationException("Expected AvalonDock auto-hide overlay keyboard navigation KeyBinding.");
        AssertEqual(Key.F11, keyBinding.Key, "AvalonDock auto-hide overlay keyboard navigation key");
        AssertEqual(ModifierKeys.None, keyBinding.Modifiers, "AvalonDock auto-hide overlay keyboard navigation modifiers");

        EnsureAutoHideOverlayAnchorables();
        _avalonDockAutoHideOverlayIndex = -1;
        ViewModel.LastAvalonDockAutoHideOverlayTarget = string.Empty;
        int canExecuteCountBefore = ViewModel.AvalonDockAutoHideOverlayCanExecuteCount;
        if (!ToolkitDockCommands.CycleAutoHideOverlay.CanExecute(null, this))
        {
            throw new InvalidOperationException("Expected AvalonDock auto-hide overlay keyboard navigation command to be executable.");
        }

        if (ViewModel.AvalonDockAutoHideOverlayCanExecuteCount <= canExecuteCountBefore)
        {
            throw new InvalidOperationException("Expected AvalonDock auto-hide overlay keyboard navigation command to route CanExecute.");
        }

        int overlayCountBefore = ViewModel.AvalonDockAutoHideOverlayCount;
        ToolkitDockCommands.CycleAutoHideOverlay.Execute(null, this);
        Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
        ValidateAvalonDockAutoHideCommandTarget(AgendaPane, overlayCountBefore + 1);

        ToolkitDockCommands.CycleAutoHideOverlay.Execute(null, this);
        Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
        ValidateAvalonDockAutoHideCommandTarget(ContactsPane, overlayCountBefore + 2);
        HideAvalonDockAutoHideOverlay(ContactsPane);
    }

    internal void ValidateAvalonDockAutoHideCommandTarget(LayoutAnchorable expectedContent, int expectedOverlayCount)
    {
        if (!expectedContent.IsAutoHidden)
        {
            throw new InvalidOperationException($"Expected AvalonDock auto-hide overlay target '{expectedContent.Title}' to remain auto-hidden.");
        }

        AssertEqual(expectedOverlayCount, ViewModel.AvalonDockAutoHideOverlayCount, "AvalonDock auto-hide overlay count");
        AssertEqual(expectedContent.ContentId ?? expectedContent.Title, ViewModel.LastAvalonDockAutoHideOverlayTarget, "AvalonDock auto-hide overlay last target");
        AssertEqual($"Auto-hide overlay shown: {expectedContent.Title}", ViewModel.Status, "AvalonDock auto-hide overlay status");
    }

    internal void ValidateAvalonDockAutoHideOverlayTarget(LayoutAnchorable expectedContent, int expectedOverlayCount)
    {
        ValidateAvalonDockAutoHideCommandTarget(expectedContent, expectedOverlayCount);

        object? overlayModel = GetAvalonDockAutoHideWindowModel();
        if (!AutoHideOverlayModelContains(overlayModel, expectedContent))
        {
            throw new InvalidOperationException(
                $"Expected AvalonDock auto-hide overlay to host '{expectedContent.Title}', but model was '{FormatAvalonDockActiveContent(overlayModel)}'.");
        }
    }

    private void EnsureAutoHideOverlayAnchorables()
    {
        foreach (LayoutAnchorable anchorable in GetAutoHideOverlayAnchorables())
        {
            if (!anchorable.IsAutoHidden)
            {
                anchorable.ToggleAutoHide();
                Dispatcher.Invoke(static () => { }, DispatcherPriority.Background);
            }
        }
    }

    private object? GetAvalonDockAutoHideWindowModel()
    {
        AvalonDockAutoHideWindowControl? autoHideWindow = DockManager.AutoHideWindow;
        return autoHideWindow?.Model;
    }

    private static bool AutoHideOverlayModelContains(object? overlayModel, LayoutAnchorable expectedContent)
    {
        if (ReferenceEquals(overlayModel, expectedContent))
        {
            return true;
        }

        if (overlayModel is LayoutAnchorablePane pane)
        {
            return pane.Children.Contains(expectedContent);
        }

        if (overlayModel is ILayoutContainer container)
        {
            return ContainsLayoutContent(container, expectedContent);
        }

        return false;
    }

    private static bool ContainsLayoutContent(ILayoutContainer container, LayoutContent expectedContent)
    {
        foreach (ILayoutElement child in container.Children)
        {
            if (ReferenceEquals(child, expectedContent))
            {
                return true;
            }

            if (child is ILayoutContainer childContainer &&
                ContainsLayoutContent(childContainer, expectedContent))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatAvalonDockActiveContent(object? activeContent)
    {
        return activeContent switch
        {
            null => "<null>",
            LayoutContent content => $"LayoutContent:{content.ContentId ?? content.Title}",
            FrameworkElement element => $"FrameworkElement:{element.Name}",
            _ => activeContent.ToString() ?? "<active content>"
        };
    }

    private string FormatAvalonDockAnchorableState(LayoutAnchorable anchorable)
    {
        string selected = anchorable.Parent is ILayoutContentSelector selector
            ? FormatAvalonDockActiveContent(selector.SelectedContent)
            : "<no selector>";
        return $"contentId={anchorable.ContentId}, isSelected={anchorable.IsSelected}, isActive={anchorable.IsActive}, selected={selected}, lastTarget={ViewModel.LastAvalonDockAnchorableKeyboardNavigationTarget}";
    }

    internal void ValidateToolkitAutomationState(bool expectLoaded)
    {
        AssertEqual("ToolkitDockManagerAutomation", AutomationProperties.GetAutomationId(DockManager), "Toolkit DockingManager automation id");
        AssertEqual("Toolkit AvalonDock manager", AutomationProperties.GetName(DockManager), "Toolkit DockingManager automation name");
        AssertEqual("ToolkitActivateEditorButtonAutomation", AutomationProperties.GetAutomationId(ActivateEditorButton), "Toolkit activate editor automation id");
        AssertEqual("ToolkitDockDocumentContextMenuAutomation", AutomationProperties.GetAutomationId(DockDocumentContextMenu), "Toolkit document context menu automation id");
        AssertEqual("ToolkitDocumentListAutomation", AutomationProperties.GetAutomationId(DocumentList), "Toolkit document list automation id");
        AssertEqual("Toolkit documents", AutomationProperties.GetName(DocumentList), "Toolkit document list automation name");
        AssertEqual("ToolkitEditorTextBoxAutomation", AutomationProperties.GetAutomationId(EditorTextBox), "Toolkit editor text box automation id");
        AssertEqual("Toolkit editor body", AutomationProperties.GetName(EditorTextBox), "Toolkit editor text box automation name");

        if (!expectLoaded)
        {
            return;
        }

        AutomationPeer? dockPeer = UIElementAutomationPeer.CreatePeerForElement(DockManager);
        if (dockPeer != null)
        {
            AssertEqual("Toolkit AvalonDock manager", dockPeer.GetName(), "Toolkit DockingManager automation peer name");
        }

        AutomationPeer activateButtonPeer = RequireAutomationPeer(ActivateEditorButton, "Toolkit activate editor button");
        if (activateButtonPeer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider)
        {
            throw new InvalidOperationException("Expected Toolkit activate editor button automation peer to expose Invoke.");
        }

        AutomationPeer documentListPeer = RequireAutomationPeer(DocumentList, "Toolkit document list");
        if (documentListPeer.GetPattern(PatternInterface.Selection) is not ISelectionProvider)
        {
            throw new InvalidOperationException("Expected Toolkit document list automation peer to expose Selection.");
        }

        AutomationPeer editorTextBoxPeer = RequireAutomationPeer(EditorTextBox, "Toolkit editor text box");
        if (editorTextBoxPeer.GetPattern(PatternInterface.Value) is not IValueProvider)
        {
            throw new InvalidOperationException("Expected Toolkit editor text box automation peer to expose Value.");
        }
    }

    private static AutomationPeer RequireAutomationPeer(UIElement element, string description)
    {
        return UIElementAutomationPeer.CreatePeerForElement(element)
            ?? throw new InvalidOperationException($"Expected {description} to create a WPF automation peer.");
    }

    private static bool CanExecuteDockContextCommand(MenuItem menuItem)
    {
        if (menuItem.Command is not RoutedCommand command ||
            menuItem.CommandTarget is not IInputElement target)
        {
            throw new InvalidOperationException($"Expected AvalonDock context menu item '{menuItem.Name}' to use a routed command target.");
        }

        return command.CanExecute(menuItem.CommandParameter, target);
    }

    private static void ExecuteDockContextCommand(MenuItem menuItem)
    {
        if (menuItem.Command is not RoutedCommand command ||
            menuItem.CommandTarget is not IInputElement target)
        {
            throw new InvalidOperationException($"Expected AvalonDock context menu item '{menuItem.Name}' to execute through a routed command target.");
        }

        command.Execute(menuItem.CommandParameter, target);
    }

    internal void ValidateAvalonDockLayoutReplacementEvents(string layoutXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutXml);

        int layoutChangingCountBefore = ViewModel.AvalonDockLayoutChangingCount;
        int layoutChangedCountBefore = ViewModel.AvalonDockLayoutChangedCount;

        var serializer = new XmlLayoutSerializer(DockManager);
        serializer.LayoutSerializationCallback += (_, args) =>
        {
            args.Content ??= new TextBlock
            {
                Text = args.Model.ContentId,
                Margin = new Thickness(8)
            };
        };

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(layoutXml));
        serializer.Deserialize(stream);

        if (ViewModel.AvalonDockLayoutChangingCount <= layoutChangingCountBefore ||
            ViewModel.AvalonDockLayoutChangedCount <= layoutChangedCountBefore)
        {
            throw new InvalidOperationException("Expected AvalonDock layout changing/changed events to fire when DockingManager.Layout changes.");
        }
    }

    internal void ValidateSourceBackedAvalonDockState(bool mutateSources)
    {
        if (!ReferenceEquals(SourceDockManager.DocumentsSource, ViewModel.SourceDocuments))
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock documents source to use the view-model collection.");
        }

        if (!ReferenceEquals(SourceDockManager.AnchorablesSource, ViewModel.SourceAnchorables))
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock anchorables source to use the view-model collection.");
        }

        AssertEqual(ViewModel.SourceDocuments.Count, SourceDocumentPane.ChildrenCount, "AvalonDock source document count");
        AssertEqual(ViewModel.SourceAnchorables.Count, SourceAnchorablePane.ChildrenCount, "AvalonDock source anchorable count");

        var firstDocument = ViewModel.SourceDocuments.First();
        var generatedDocument = FindGeneratedDocument(firstDocument);
        AssertEqual(firstDocument.Title, generatedDocument.Title, "AvalonDock source document title");
        AssertEqual(firstDocument.ContentId, generatedDocument.ContentId, "AvalonDock source document content id");

        var firstAnchorable = ViewModel.SourceAnchorables.First();
        var generatedAnchorable = FindGeneratedAnchorable(firstAnchorable);
        AssertEqual(firstAnchorable.Title, generatedAnchorable.Title, "AvalonDock source anchorable title");
        AssertEqual(firstAnchorable.ContentId, generatedAnchorable.ContentId, "AvalonDock source anchorable content id");

        var generatedDocumentItem = SourceDockManager.GetLayoutItemFromModel(generatedDocument);
        var generatedAnchorableItem = SourceDockManager.GetLayoutItemFromModel(generatedAnchorable);
        if (generatedDocumentItem == null ||
            generatedAnchorableItem == null)
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock layout items to be discoverable from their generated layout models.");
        }

        ValidateSourceDockTitleTemplateSelectorState(
            firstDocument,
            generatedDocument,
            firstAnchorable,
            generatedAnchorable);
        ValidateSourceBackedAvalonDockLayoutItemCommands(
            generatedDocument,
            generatedDocumentItem,
            generatedAnchorable,
            generatedAnchorableItem);
        ValidateSourceLayoutUpdateStrategyState(requireInsertedCallbacks: false);
        ValidateSourceBackedAvalonDockDynamicMetadata(
            firstDocument,
            generatedDocument,
            firstAnchorable,
            generatedAnchorable);

        int activeContentChangesBefore = ViewModel.SourceActiveContentChangedCount;
        SourceDockManager.ActiveContent = firstDocument;
        AssertEqual(firstDocument, ViewModel.SourceActiveContent, "AvalonDock source active document binding");
        AssertEqual(firstDocument.Title, ViewModel.LastSourceActiveTitle, "AvalonDock source active document title");
        if (ViewModel.SourceActiveContentChangedCount <= activeContentChangesBefore)
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock ActiveContentChanged to fire for a source document.");
        }

        activeContentChangesBefore = ViewModel.SourceActiveContentChangedCount;
        SourceDockManager.ActiveContent = firstAnchorable;
        AssertEqual(firstAnchorable, ViewModel.SourceActiveContent, "AvalonDock source active anchorable binding");
        AssertEqual(firstAnchorable.Title, ViewModel.LastSourceActiveTitle, "AvalonDock source active anchorable title");
        if (ViewModel.SourceActiveContentChangedCount <= activeContentChangesBefore)
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock ActiveContentChanged to fire for a source anchorable.");
        }

        if (mutateSources)
        {
            int beforeInsertDocumentCount = ViewModel.SourceLayoutStrategy.BeforeInsertDocumentCount;
            int afterInsertDocumentCount = ViewModel.SourceLayoutStrategy.AfterInsertDocumentCount;
            int beforeInsertAnchorableCount = ViewModel.SourceLayoutStrategy.BeforeInsertAnchorableCount;
            int afterInsertAnchorableCount = ViewModel.SourceLayoutStrategy.AfterInsertAnchorableCount;

            int documentCountBeforeAdd = SourceDocumentPane.ChildrenCount;
            var addedDocument = ViewModel.AddSourceDocument();
            PumpDispatcherUntil(
                this,
                () => SourceDocumentPane.ChildrenCount == documentCountBeforeAdd + 1,
                TimeSpan.FromSeconds(2),
                "AvalonDock source document insertion");
            var generatedAddedDocument = FindGeneratedDocument(addedDocument);
            AssertEqual(addedDocument.Title, generatedAddedDocument.Title, "AvalonDock added source document title");
            AssertEqual(addedDocument.ContentId, generatedAddedDocument.ContentId, "AvalonDock added source document content id");
            if (SourceDockManager.GetLayoutItemFromModel(generatedAddedDocument) == null)
            {
                throw new InvalidOperationException("Expected added source document to have a generated AvalonDock LayoutItem.");
            }

            SourceDockManager.ActiveContent = addedDocument;
            AssertEqual(addedDocument, ViewModel.SourceActiveContent, "AvalonDock added source active document binding");

            int anchorableCountBeforeAdd = SourceAnchorablePane.ChildrenCount;
            var addedAnchorable = ViewModel.AddSourceAnchorable();
            PumpDispatcherUntil(
                this,
                () => SourceAnchorablePane.ChildrenCount == anchorableCountBeforeAdd + 1,
                TimeSpan.FromSeconds(2),
                "AvalonDock source anchorable insertion");
            var generatedAddedAnchorable = FindGeneratedAnchorable(addedAnchorable);
            AssertEqual(addedAnchorable.Title, generatedAddedAnchorable.Title, "AvalonDock added source anchorable title");
            AssertEqual(addedAnchorable.ContentId, generatedAddedAnchorable.ContentId, "AvalonDock added source anchorable content id");
            if (SourceDockManager.GetLayoutItemFromModel(generatedAddedAnchorable) == null)
            {
                throw new InvalidOperationException("Expected added source anchorable to have a generated AvalonDock LayoutItem.");
            }

            SourceDockManager.ActiveContent = addedAnchorable;
            AssertEqual(addedAnchorable, ViewModel.SourceActiveContent, "AvalonDock added source active anchorable binding");

            if (ViewModel.SourceLayoutStrategy.BeforeInsertDocumentCount <= beforeInsertDocumentCount ||
                ViewModel.SourceLayoutStrategy.AfterInsertDocumentCount <= afterInsertDocumentCount ||
                ViewModel.SourceLayoutStrategy.BeforeInsertAnchorableCount <= beforeInsertAnchorableCount ||
                ViewModel.SourceLayoutStrategy.AfterInsertAnchorableCount <= afterInsertAnchorableCount)
            {
                throw new InvalidOperationException(
                    "Expected AvalonDock layout update strategy callbacks to fire for added source-backed document and anchorable.");
            }

            ValidateSourceLayoutUpdateStrategyState(requireInsertedCallbacks: true);
            ExerciseSourceBackedAvalonDockTabGroupCommands();
        }
    }

    internal void ExerciseSourceBackedAvalonDockTabGroupCommands()
    {
        var target = EnsureSecondSourceDocument();
        var generatedDocument = FindGeneratedDocument(target);
        if (SourceDockManager.GetLayoutItemFromModel(generatedDocument) is not AvalonDockDocumentItem documentItem)
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock document to expose a generated document item.");
        }

        ExecuteSourceDocumentTabGroupRoundTrip(
            documentItem,
            generatedDocument,
            documentItem.NewHorizontalTabGroupCommand,
            Orientation.Vertical,
            "new horizontal tab group");
        ExecuteSourceDocumentTabGroupRoundTrip(
            documentItem,
            generatedDocument,
            documentItem.NewVerticalTabGroupCommand,
            Orientation.Horizontal,
            "new vertical tab group");

        ViewModel.SourceTabGroupCommandCount += 2;
        ViewModel.Status = $"Exercised source tab groups {ViewModel.SourceTabGroupCommandCount}";
        ViewModel.Activity.Add(ViewModel.Status);
    }

    private ToolkitDockItem EnsureSecondSourceDocument()
    {
        if (ViewModel.SourceDocuments.Count >= 2)
        {
            return ViewModel.SourceDocuments[1];
        }

        int documentCountBeforeAdd = SourceDocumentPane.ChildrenCount;
        var addedDocument = ViewModel.AddSourceDocument();
        PumpDispatcherUntil(
            this,
            () => SourceDocumentPane.ChildrenCount == documentCountBeforeAdd + 1,
            TimeSpan.FromSeconds(2),
            "AvalonDock source document insertion for tab-group commands");
        return addedDocument;
    }

    private void ExecuteSourceDocumentTabGroupRoundTrip(
        AvalonDockDocumentItem documentItem,
        LayoutDocument document,
        ICommand? newGroupCommand,
        Orientation expectedOrientation,
        string description)
    {
        if (newGroupCommand == null || !newGroupCommand.CanExecute(null))
        {
            throw new InvalidOperationException($"Expected AvalonDock {description} command to be executable.");
        }

        if (document.Parent is not LayoutDocumentPane originalPane)
        {
            throw new InvalidOperationException("Expected AvalonDock source document to start in a document pane.");
        }

        int originalPaneChildren = originalPane.ChildrenCount;
        newGroupCommand.Execute(null);

        if (document.Parent is not LayoutDocumentPane generatedPane ||
            ReferenceEquals(generatedPane, originalPane) ||
            generatedPane.Parent is not LayoutDocumentPaneGroup generatedGroup)
        {
            throw new InvalidOperationException($"Expected AvalonDock {description} command to create a sibling document pane.");
        }

        AssertEqual(expectedOrientation, generatedGroup.Orientation, $"AvalonDock {description} group orientation");
        AssertEqual(1, generatedPane.ChildrenCount, $"AvalonDock {description} generated pane child count");
        if (!generatedPane.Children.Contains(document))
        {
            throw new InvalidOperationException($"Expected AvalonDock {description} command to move the source document into the generated pane.");
        }

        if (documentItem.MoveToPreviousTabGroupCommand == null ||
            !documentItem.MoveToPreviousTabGroupCommand.CanExecute(null))
        {
            throw new InvalidOperationException("Expected AvalonDock move-to-previous-tab-group command to be executable after tab-group creation.");
        }

        documentItem.MoveToPreviousTabGroupCommand.Execute(null);
        PumpDispatcherUntil(
            this,
            () => ReferenceEquals(document.Parent, originalPane) &&
                  originalPane.Children.Contains(document),
            TimeSpan.FromSeconds(2),
            $"AvalonDock {description} move-to-previous round trip");
        AssertEqual(originalPaneChildren, originalPane.ChildrenCount, $"AvalonDock {description} restored pane child count");
    }

    internal void ValidateSourceLayoutUpdateStrategyState(bool requireInsertedCallbacks)
    {
        if (!ReferenceEquals(SourceDockManager.LayoutUpdateStrategy, ViewModel.SourceLayoutStrategy))
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock manager to use the view-model layout update strategy.");
        }

        var strategy = ViewModel.SourceLayoutStrategy;
        if (!requireInsertedCallbacks)
        {
            return;
        }

        if (strategy.BeforeInsertDocumentCount == 0 ||
            strategy.AfterInsertDocumentCount == 0)
        {
            throw new InvalidOperationException(
                "Expected AvalonDock layout update strategy to observe inserted source documents.");
        }

        if (strategy.BeforeInsertAnchorableCount == 0 ||
            strategy.AfterInsertAnchorableCount == 0)
        {
            throw new InvalidOperationException(
                "Expected AvalonDock layout update strategy to observe inserted source anchorables.");
        }

        if (string.IsNullOrWhiteSpace(strategy.LastInsertedDocumentContentId) ||
            string.IsNullOrWhiteSpace(strategy.LastInsertedAnchorableContentId))
        {
            throw new InvalidOperationException("Expected AvalonDock layout update strategy to record inserted content ids.");
        }
    }

    internal void ValidateSourceDockTitleTemplateSelectorState(
        ToolkitDockItem sourceDocument,
        LayoutDocument generatedDocument,
        ToolkitDockItem sourceAnchorable,
        LayoutAnchorable generatedAnchorable)
    {
        if (SourceDockManager.DocumentTitleTemplateSelector is not ToolkitDockTitleTemplateSelector selector ||
            !ReferenceEquals(SourceDockManager.AnchorableTitleTemplateSelector, selector))
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock title template selectors to use the sample selector.");
        }

        if (!ReferenceEquals(selector.DocumentTemplate, TryFindResource("SourceDocumentTitleTemplate")) ||
            !ReferenceEquals(selector.AnchorableTemplate, TryFindResource("SourceAnchorableTitleTemplate")))
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock title selector templates to resolve from app resources.");
        }

        AssertEqual(selector.DocumentTemplate, selector.SelectTemplate(sourceDocument, SourceDockManager), "AvalonDock source document title template");
        AssertEqual(selector.DocumentTemplate, selector.SelectTemplate(generatedDocument, SourceDockManager), "AvalonDock generated document title template");
        AssertEqual(selector.AnchorableTemplate, selector.SelectTemplate(sourceAnchorable, SourceDockManager), "AvalonDock source anchorable title template");
        AssertEqual(selector.AnchorableTemplate, selector.SelectTemplate(generatedAnchorable, SourceDockManager), "AvalonDock generated anchorable title template");
    }

    internal void ValidateSourceBackedAvalonDockLayoutItemCommands(
        LayoutDocument generatedDocument,
        AvalonDockLayoutItem generatedDocumentItem,
        LayoutAnchorable generatedAnchorable,
        AvalonDockLayoutItem generatedAnchorableItem)
    {
        AssertEqual(generatedDocument.Title, generatedDocumentItem.Title, "AvalonDock source document LayoutItem title");
        AssertEqual(generatedDocument.ContentId, generatedDocumentItem.ContentId, "AvalonDock source document LayoutItem content id");
        AssertEqual(generatedDocument, generatedDocumentItem.LayoutElement, "AvalonDock source document LayoutItem layout element");
        AssertEqual(true, generatedDocumentItem.CanClose, "AvalonDock source document LayoutItem close capability");

        if (generatedDocumentItem is not AvalonDockDocumentItem documentItem ||
            documentItem.ActivateCommand == null ||
            documentItem.CloseCommand == null ||
            documentItem.CloseAllButThisCommand == null ||
            documentItem.FloatCommand == null ||
            documentItem.NewHorizontalTabGroupCommand == null ||
            documentItem.NewVerticalTabGroupCommand == null ||
            documentItem.MoveToNextTabGroupCommand == null ||
            documentItem.MoveToPreviousTabGroupCommand == null)
        {
            throw new InvalidOperationException("Expected generated AvalonDock document LayoutItem to expose default document commands.");
        }

        documentItem.ActivateCommand.Execute(null);
        AssertEqual(true, generatedDocument.IsActive, "AvalonDock source document LayoutItem activate command");

        AssertEqual(generatedAnchorable.Title, generatedAnchorableItem.Title, "AvalonDock source anchorable LayoutItem title");
        AssertEqual(generatedAnchorable.ContentId, generatedAnchorableItem.ContentId, "AvalonDock source anchorable LayoutItem content id");
        AssertEqual(generatedAnchorable, generatedAnchorableItem.LayoutElement, "AvalonDock source anchorable LayoutItem layout element");

        if (generatedAnchorableItem is not AvalonDockAnchorableItem anchorableItem ||
            anchorableItem.ActivateCommand == null ||
            anchorableItem.HideCommand == null ||
            anchorableItem.AutoHideCommand == null ||
            anchorableItem.DockCommand == null ||
            anchorableItem.FloatCommand == null ||
            anchorableItem.DockAsDocumentCommand == null)
        {
            throw new InvalidOperationException("Expected generated AvalonDock anchorable LayoutItem to expose default anchorable commands.");
        }

        anchorableItem.ActivateCommand.Execute(null);
        AssertEqual(true, generatedAnchorable.IsActive, "AvalonDock source anchorable LayoutItem activate command");

        anchorableItem.AutoHideCommand.Execute(null);
        AssertEqual(true, generatedAnchorable.IsAutoHidden, "AvalonDock source anchorable LayoutItem auto-hide command");
        if (!SourceDockLayoutRoot.LeftSide.Children.Any(group => group.Children.Contains(generatedAnchorable)) &&
            !SourceDockLayoutRoot.RightSide.Children.Any(group => group.Children.Contains(generatedAnchorable)) &&
            !SourceDockLayoutRoot.TopSide.Children.Any(group => group.Children.Contains(generatedAnchorable)) &&
            !SourceDockLayoutRoot.BottomSide.Children.Any(group => group.Children.Contains(generatedAnchorable)))
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock anchorable auto-hide command to move the model into a side group.");
        }

        anchorableItem.DockCommand.Execute(null);
        AssertEqual(false, generatedAnchorable.IsAutoHidden, "AvalonDock source anchorable LayoutItem dock command");
        if (generatedAnchorable.Parent is not LayoutAnchorablePane)
        {
            throw new InvalidOperationException("Expected source-backed AvalonDock anchorable dock command to restore pane membership.");
        }
    }

    private void ValidateSourceBackedAvalonDockDynamicMetadata(
        ToolkitDockItem sourceDocument,
        LayoutDocument generatedDocument,
        ToolkitDockItem sourceAnchorable,
        LayoutAnchorable generatedAnchorable)
    {
        string originalDocumentTitle = sourceDocument.Title;
        bool originalDocumentCanClose = sourceDocument.CanClose;
        string originalAnchorableTitle = sourceAnchorable.Title;
        bool originalAnchorableCanClose = sourceAnchorable.CanClose;

        sourceDocument.Title = "Source Overview Updated";
        sourceDocument.CanClose = false;
        sourceAnchorable.Title = "Source Tool Updated";
        sourceAnchorable.CanClose = true;

        PumpDispatcherUntil(
            this,
            () => string.Equals(generatedDocument.Title, sourceDocument.Title, StringComparison.Ordinal) &&
                  generatedDocument.CanClose == sourceDocument.CanClose &&
                  string.Equals(generatedAnchorable.Title, sourceAnchorable.Title, StringComparison.Ordinal) &&
                  generatedAnchorable.CanClose == sourceAnchorable.CanClose,
            TimeSpan.FromSeconds(2),
            "AvalonDock generated layout item metadata update");

        AssertEqual(sourceDocument.Title, generatedDocument.Title, "AvalonDock source document dynamic title");
        AssertEqual(sourceDocument.CanClose, generatedDocument.CanClose, "AvalonDock source document dynamic close policy");
        AssertEqual(sourceAnchorable.Title, generatedAnchorable.Title, "AvalonDock source anchorable dynamic title");
        AssertEqual(sourceAnchorable.CanClose, generatedAnchorable.CanClose, "AvalonDock source anchorable dynamic close policy");

        sourceDocument.Title = originalDocumentTitle;
        sourceDocument.CanClose = originalDocumentCanClose;
        sourceAnchorable.Title = originalAnchorableTitle;
        sourceAnchorable.CanClose = originalAnchorableCanClose;

        PumpDispatcherUntil(
            this,
            () => string.Equals(generatedDocument.Title, originalDocumentTitle, StringComparison.Ordinal) &&
                  generatedDocument.CanClose == originalDocumentCanClose &&
                  string.Equals(generatedAnchorable.Title, originalAnchorableTitle, StringComparison.Ordinal) &&
                  generatedAnchorable.CanClose == originalAnchorableCanClose,
            TimeSpan.FromSeconds(2),
            "AvalonDock generated layout item metadata restore");
    }

    internal void ValidateAvalonDockThemeState(string expectedThemeName)
    {
        AssertEqual(expectedThemeName, ViewModel.ActiveDockThemeName, "AvalonDock active theme name");
        AssertAvalonDockTheme(DockManager.Theme, expectedThemeName, "primary DockingManager");
        AssertAvalonDockTheme(SourceDockManager.Theme, expectedThemeName, "source-backed DockingManager");

        if (TryFindResource("ToolkitAccentBrush") is not SolidColorBrush ||
            TryFindResource("ToolkitSubtleBrush") is not SolidColorBrush)
        {
            throw new InvalidOperationException("Expected Toolkit application theme brushes to resolve after AvalonDock theme switching.");
        }

        if (DockManager.DocumentHeaderTemplate is null ||
            SourceDockManager.LayoutItemTemplate is null ||
            SourceDockManager.LayoutItemContainerStyle is null)
        {
            throw new InvalidOperationException("Expected AvalonDock templates and layout-item styles to remain loaded after theme switching.");
        }
    }

    internal void ValidateAvalonDockManagerOptionState()
    {
        AssertEqual(true, DockManager.AllowMixedOrientation, "AvalonDock mixed orientation option");
        AssertEqual(7.0, DockManager.GridSplitterWidth, "AvalonDock grid splitter width");
        AssertEqual(6.0, DockManager.GridSplitterHeight, "AvalonDock grid splitter height");
        AssertEqual(false, DockManager.ShowSystemMenu, "AvalonDock system menu option");
        AssertEqual(750, DockManager.AutoHideWindowClosingTimer, "AvalonDock auto-hide close timer option");

        AssertEqual(true, SourceDockManager.AllowMixedOrientation, "source AvalonDock mixed orientation option");
        AssertEqual(5.0, SourceDockManager.GridSplitterWidth, "source AvalonDock grid splitter width");
        AssertEqual(5.0, SourceDockManager.GridSplitterHeight, "source AvalonDock grid splitter height");
    }

    private static void AssertAvalonDockTheme(Theme theme, string expectedThemeName, string managerName)
    {
        string expectedTypeName = expectedThemeName switch
        {
            "Aero" => nameof(AeroTheme),
            "Metro" => nameof(MetroTheme),
            "VS2010" => nameof(VS2010Theme),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedThemeName), expectedThemeName, "Unknown AvalonDock theme.")
        };
        string expectedAssemblyName = expectedThemeName switch
        {
            "Aero" => "Xceed.Wpf.AvalonDock.Themes.Aero",
            "Metro" => "Xceed.Wpf.AvalonDock.Themes.Metro",
            "VS2010" => "Xceed.Wpf.AvalonDock.Themes.VS2010",
            _ => throw new ArgumentOutOfRangeException(nameof(expectedThemeName), expectedThemeName, "Unknown AvalonDock theme.")
        };

        string actualTypeName = theme switch
        {
            AeroTheme => nameof(AeroTheme),
            MetroTheme => nameof(MetroTheme),
            VS2010Theme => nameof(VS2010Theme),
            _ => "<unknown theme>"
        };
        AssertEqual(expectedTypeName, actualTypeName, $"{managerName} AvalonDock theme type");

        var resourceUri = Convert.ToString(theme.GetResourceUri(), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(resourceUri) ||
            !resourceUri.Contains(expectedAssemblyName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {managerName} AvalonDock theme resource URI to come from {expectedAssemblyName}, got '{resourceUri}'.");
        }
    }

    private LayoutDocument FindGeneratedDocument(ToolkitDockItem sourceDocument)
    {
        return SourceDocumentPane.Children
            .OfType<LayoutDocument>()
            .FirstOrDefault(document => ReferenceEquals(document.Content, sourceDocument))
            ?? throw new InvalidOperationException($"Expected AvalonDock source document '{sourceDocument.ContentId}' to generate a LayoutDocument.");
    }

    private LayoutAnchorable FindGeneratedAnchorable(ToolkitDockItem sourceAnchorable)
    {
        return SourceAnchorablePane.Children
            .OfType<LayoutAnchorable>()
            .FirstOrDefault(anchorable => ReferenceEquals(anchorable.Content, sourceAnchorable))
            ?? throw new InvalidOperationException($"Expected AvalonDock source anchorable '{sourceAnchorable.ContentId}' to generate a LayoutAnchorable.");
    }

    internal void ValidateToolkitInputEditorState()
    {
        AssertEqual(ViewModel.QuickSearchText, QuickSearchTextBox.Text, "Toolkit AutoSelectTextBox text binding target");
        AssertEqual(AutoSelectBehavior.OnFocus, QuickSearchTextBox.AutoSelectBehavior, "Toolkit AutoSelectTextBox behavior");
        AssertEqual(ViewModel.AccessCode, AccessCodeBox.Password, "Toolkit WatermarkPasswordBox password state");
        AssertEqual(ViewModel.ReferenceCode, ReferenceMaskTextBox.Text, "Toolkit MaskedTextBox text binding target");
        AssertEqual("LL-0000", ReferenceMaskTextBox.Mask, "Toolkit MaskedTextBox mask");
        AssertEqual(ViewModel.ReminderTime, ReminderTimePicker.Value, "Toolkit TimePicker value binding target");
        AssertEqual(ViewModel.ReviewedAt, ReviewedAtEditor.Value, "Toolkit DateTimeUpDown value binding target");
        AssertEqual(ViewModel.Effort, EffortEditor.Value, "Toolkit TimeSpanUpDown value binding target");
        AssertEqual(ViewModel.ByteScore, ByteScoreEditor.Value, "Toolkit ByteUpDown value binding target");
        AssertEqual(ViewModel.DoubleScale, DoubleScaleEditor.Value, "Toolkit DoubleUpDown value binding target");
        AssertEqual(ViewModel.WorkItemId, WorkItemIdEditor.Value, "Toolkit LongUpDown value binding target");
        AssertEqual(ViewModel.Budget, BudgetEditor.Value, "Toolkit DecimalUpDown value binding target");
        AssertEqual(ViewModel.AccentColor, AccentColorCanvas.SelectedColor, "Toolkit ColorCanvas selected color binding target");
        AssertEqual(ViewModel.RichNotes, ToolkitRichTextBox.Text, "Toolkit RichTextBox text binding target");
        AssertEqual(ViewModel.MultiLineNotes, MultiLineNotesEditor.Text, "Toolkit MultiLineTextEditor text binding target");
        AssertEqual(ViewModel.SelectedOwner, OwnerComboBox.SelectedItem as string, "Toolkit WatermarkComboBox selected item binding target");
        AssertEqual(ViewModel.PriorityRangeStart, PriorityRangeSlider.LowerValue, "Toolkit RangeSlider lower value binding target");
        AssertEqual(ViewModel.PriorityRangeEnd, PriorityRangeSlider.HigherValue, "Toolkit RangeSlider higher value binding target");
        AssertEqual(true, DocumentCountSpinner.ShowSpinner, "Toolkit ButtonSpinner spinner visibility");
        AssertEqual("Right", Convert.ToString(DocumentCountSpinner.SpinnerLocation, CultureInfo.InvariantCulture), "Toolkit ButtonSpinner spinner location");

        if (ToolkitRichTextBox.TextFormatter is not PlainTextFormatter)
        {
            throw new InvalidOperationException("Expected Toolkit RichTextBox to use PlainTextFormatter.");
        }

        if (OwnerComboBox.Items.Count != ViewModel.Owners.Count)
        {
            throw new InvalidOperationException("Expected Toolkit WatermarkComboBox to bind all owners.");
        }

        if (FlagListBox.Items.Count != ViewModel.Flags.Count)
        {
            throw new InvalidOperationException("Expected Toolkit CheckListBox to bind all flags.");
        }

        foreach (string selectedFlag in ViewModel.SelectedFlags)
        {
            if (!FlagListBox.SelectedItems.Contains(selectedFlag))
            {
                throw new InvalidOperationException($"Expected Toolkit CheckListBox selected item '{selectedFlag}'.");
            }
        }
    }

    internal void ValidateToolkitWizardState(bool expectLoaded)
    {
        if (ToolkitWizard.Items.Count != 2)
        {
            throw new InvalidOperationException($"Expected Toolkit Wizard to contain two pages, got {ToolkitWizard.Items.Count}.");
        }

        AssertEqual("Scope", WizardScopePage.Title, "Toolkit Wizard first page title");
        AssertEqual("Choose owner and priority range", WizardScopePage.Description, "Toolkit Wizard first page description");
        AssertEqual(WizardPageType.Interior, WizardScopePage.PageType, "Toolkit Wizard first page type");
        AssertEqual(false, WizardScopePage.CanFinish.GetValueOrDefault(), "Toolkit Wizard first page finish capability");
        AssertEqual("Review", WizardReviewPage.Title, "Toolkit Wizard review page title");
        AssertEqual("Confirm Toolkit state", WizardReviewPage.Description, "Toolkit Wizard review page description");
        AssertEqual(WizardPageType.Interior, WizardReviewPage.PageType, "Toolkit Wizard review page type");
        AssertEqual(true, WizardReviewPage.CanFinish.GetValueOrDefault(), "Toolkit Wizard review page finish capability");
        AssertEqual(false, ToolkitWizard.FinishButtonClosesWindow, "Toolkit Wizard finish close behavior");
        AssertEqual(false, ToolkitWizard.CancelButtonClosesWindow, "Toolkit Wizard cancel close behavior");

        if (expectLoaded && ToolkitWizard.CurrentPage == null)
        {
            throw new InvalidOperationException("Expected loaded Toolkit Wizard to select an initial page.");
        }
    }

    internal void ExerciseToolkitWizard()
    {
        ValidateToolkitWizardState(expectLoaded: true);

        int pageChangesBefore = ViewModel.WizardPageChanges;
        ToolkitWizard.CurrentPage = WizardReviewPage;
        AssertEqual(WizardReviewPage, ToolkitWizard.CurrentPage, "Toolkit Wizard current page after review navigation");
        if (ViewModel.WizardPageChanges <= pageChangesBefore)
        {
            throw new InvalidOperationException("Expected Toolkit Wizard page change event to update the view model.");
        }

        int finishesBefore = ViewModel.WizardFinishes;
        ToolkitWizard.RaiseEvent(new CancelRoutedEventArgs { RoutedEvent = Wizard.FinishEvent });
        if (ViewModel.WizardFinishes <= finishesBefore ||
            !string.Equals(ViewModel.WizardStatus, "Wizard finished", StringComparison.Ordinal) ||
            !string.Equals(ViewModel.Status, "Wizard finished", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Toolkit Wizard finish event to update sample state.");
        }

        int cancelsBefore = ViewModel.WizardCancels;
        ToolkitWizard.RaiseEvent(new RoutedEventArgs(Wizard.CancelEvent));
        if (ViewModel.WizardCancels <= cancelsBefore ||
            !string.Equals(ViewModel.WizardStatus, "Wizard canceled", StringComparison.Ordinal) ||
            !string.Equals(ViewModel.Status, "Wizard canceled", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Toolkit Wizard cancel event to update sample state.");
        }
    }

    private void OnToolkitWindowLoaded(object sender, RoutedEventArgs e)
    {
        StartLiveValidationIfRequired();
    }

    private void StartLiveValidationIfRequired()
    {
        if (_liveValidationStarted ||
            Environment.GetEnvironmentVariable(LiveValidationEnvironmentVariable) != "1")
        {
            return;
        }

        _liveValidationStarted = true;
        Console.WriteLine("ProGPU WPF Toolkit live input validation started.");
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await ValidateRequiredLiveToolkitAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Environment.Exit(1);
                }
            });
    }

    private async Task ValidateRequiredLiveToolkitAsync()
    {
        for (int attempt = 0; attempt < LiveValidationStartupMaxAttempts; attempt++)
        {
            await Task.Delay(LiveValidationRetryDelay);
            if (!ProGpuWpfDiagnostics.TryGetWindowHost(this, out var liveHost) || liveHost == null)
            {
                continue;
            }

            if (!liveHost.HasPresentedFrame)
            {
                WakeLiveRenderHost(liveHost);
                continue;
            }

            Console.WriteLine("ProGPU WPF Toolkit live input validation frame ready.");
            string geometryStatus = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () => ValidateLiveRenderSurfaceGeometryCore(liveHost),
                DispatcherPriority.Send);
            Console.WriteLine($"ProGPU WPF Toolkit live input validation geometry ready: {geometryStatus}.");
            string displayMetricsStatus = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () => ValidateLiveDisplayMetricsCore(liveHost),
                DispatcherPriority.Send);
            string inputStatus = await ValidateLiveInputAsync(liveHost);
            string successStatus = $"ProGPU WPF Toolkit live input validation succeeded: {geometryStatus}.";
            string detailStatus = $"ProGPU WPF Toolkit live input validation details: {displayMetricsStatus}; {inputStatus}.";
            Console.WriteLine(successStatus);
            Console.WriteLine(detailStatus);
            WriteLiveValidationStatus($"{successStatus}{Environment.NewLine}{detailStatus}{Environment.NewLine}");
            Console.Out.Flush();
            Environment.Exit(0);
            return;
        }

        Console.Error.WriteLine("Expected the Toolkit app to present a stable ProGPU frame before live input validation.");
        Console.Error.Flush();
        Environment.Exit(1);
    }

    private static void WriteLiveValidationStatus(string status)
    {
        string? statusPath = Environment.GetEnvironmentVariable(LiveValidationStatusPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(statusPath))
        {
            return;
        }

        string? statusDirectory = Path.GetDirectoryName(statusPath);
        if (!string.IsNullOrEmpty(statusDirectory))
        {
            Directory.CreateDirectory(statusDirectory);
        }

        File.WriteAllText(statusPath, status);
    }

    private async Task<string> ValidateLiveInputAsync(ProGpuWpfWindowHost liveHost)
    {
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: filter focus.");
        string lastTargetState = "not checked";
        bool focusedFilter = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            focusedFilter = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    if (!TryRaiseLiveMouseClick(liveHost, FilterTextBox, "FilterTextBox", out lastTargetState))
                    {
                        return false;
                    }

                    FilterTextBox.Text = string.Empty;
                    FilterTextBox.CaretIndex = 0;
                    FilterTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    return true;
                },
                DispatcherPriority.Send);
            if (focusedFilter)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!focusedFilter)
        {
            throw new InvalidOperationException(
                $"Expected Toolkit live filter TextBox to become visible and hit-testable before injecting input, but last state was: {lastTargetState}.");
        }

        Console.WriteLine("ProGPU WPF Toolkit live input validation step: filter text.");
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                if (!FilterTextBox.IsKeyboardFocusWithin)
                {
                    throw new InvalidOperationException(
                        $"Expected Toolkit live host click to focus FilterTextBox, but focused '{DescribeInputElement(Keyboard.FocusedElement)}'. {lastTargetState}.");
                }

                foreach (char character in "Dock")
                {
                    string key = char.ToUpperInvariant(character).ToString();
                    RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: key);
                    RaiseHostInput(liveHost, WpfInputEventKind.TextInput, character: character);
                    RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: key);
                }
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual("Dock", FilterTextBox.Text, "Toolkit live WatermarkTextBox text");
                AssertEqual("Dock", ViewModel.FilterText, "Toolkit live FilterText binding source");
            },
            DispatcherPriority.Send);

        Console.WriteLine("ProGPU WPF Toolkit live input validation step: popups.");
        await ValidateLivePopupControlsAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: AvalonDock document menu.");
        await ValidateLiveAvalonDockDocumentContextMenuAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: AvalonDock anchorable menu.");
        await ValidateLiveAvalonDockAnchorableContextMenuAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: editors.");
        await ValidateLiveInputEditorsAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: resources.");
        await ValidateLiveToolkitResourceThemeAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: wizard.");
        await ValidateLiveWizardAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: child window.");
        await ValidateLiveToolkitChildWindowAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: message box.");
        await ValidateLiveToolkitMessageBoxAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: window control.");
        await ValidateLiveToolkitWindowControlAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: zoombox.");
        await ValidateLiveToolkitZoomboxAndMagnifierAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: scroll clips.");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateToolkitScrollClipState(expectLoaded: true),
            DispatcherPriority.Send);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: panels.");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateToolkitPanelState(expectLoaded: true),
            DispatcherPriority.Send);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: automation.");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateToolkitAutomationState(expectLoaded: true),
            DispatcherPriority.Send);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: data grid.");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateToolkitDataGridState(expectLoaded: true),
            DispatcherPriority.Send);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: collection control.");
        await ValidateLiveToolkitCollectionControlAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: source-backed AvalonDock.");
        await ValidateLiveSourceBackedAvalonDockAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: AvalonDock themes.");
        await ValidateLiveAvalonDockThemeSwitchingAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: AvalonDock options.");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockManagerOptionState(),
            DispatcherPriority.Send);

        Console.WriteLine("ProGPU WPF Toolkit live input validation step: add document.");
        int documentsBeforeAdd = ViewModel.DocumentCount;
        await ClickLiveControlAsync(liveHost, AddDocumentButton, "AddDocumentButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(documentsBeforeAdd + 1, ViewModel.DocumentCount, "Toolkit live added document count");
                AssertEqual($"Added Generated {documentsBeforeAdd + 1}", ViewModel.Status, "Toolkit live Add document status");
                AssertEqual(documentsBeforeAdd + 1, DocumentPane.ChildrenCount, "Toolkit live AvalonDock document pane count");
            },
            DispatcherPriority.Send);

        Console.WriteLine("ProGPU WPF Toolkit live input validation step: activate editor.");
        await ClickLiveControlAsync(liveHost, ActivateEditorButton, "ActivateEditorButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(true, EditorDocument.IsSelected, "Toolkit live editor document selected state");
                AssertEqual(true, EditorDocument.IsActive, "Toolkit live editor document active state");
                if (ViewModel.AvalonDockActiveContentChangedCount <= 0)
                {
                    throw new InvalidOperationException("Expected Toolkit live AvalonDock active content event to fire.");
                }
            },
            DispatcherPriority.Send);

        Console.WriteLine("ProGPU WPF Toolkit live input validation step: AvalonDock keyboard navigation.");
        await ValidateLiveAvalonDockKeyboardNavigationAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: AvalonDock anchorable keyboard navigation.");
        await ValidateLiveAvalonDockAnchorableKeyboardNavigationAsync(liveHost);
        Console.WriteLine("ProGPU WPF Toolkit live input validation step: AvalonDock auto-hide overlay.");
        await ValidateLiveAvalonDockAutoHideOverlayAsync(liveHost);

        Console.WriteLine("ProGPU WPF Toolkit live input validation step: overview lifecycle.");
        await ValidateLiveOverviewDocumentLifecycleAsync(liveHost);

        Console.WriteLine("ProGPU WPF Toolkit live input validation step: float editor.");
        await ClickLiveControlAsync(liveHost, ToggleEditorFloatButton, "ToggleEditorFloatButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => EditorDocument.IsFloating && DockLayoutRoot.FloatingWindows.Count == 1,
            "Toolkit live editor document floating window model");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateEditorFloatingState(expectedFloating: true),
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, ToggleEditorFloatButton, "ToggleEditorFloatButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => !EditorDocument.IsFloating && DockLayoutRoot.FloatingWindows.Count == 0,
            "Toolkit live editor document docked model");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateEditorFloatingState(expectedFloating: false),
            DispatcherPriority.Send);

        int propertyPaneHidingCountBefore = ViewModel.AvalonDockAnchorableHidingCount;
        int propertyPaneVisibleChangedCountBefore = ViewModel.AvalonDockAnchorableIsVisibleChangedCount;
        await ClickLiveControlAsync(liveHost, TogglePropertyPaneButton, "TogglePropertyPaneButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidatePropertyPaneAnchorableLifecycle(
                    propertyPaneHidingCountBefore,
                    propertyPaneVisibleChangedCountBefore,
                    expectedHidden: true);
            },
            DispatcherPriority.Send);

        propertyPaneVisibleChangedCountBefore = ViewModel.AvalonDockAnchorableIsVisibleChangedCount;
        await ClickLiveControlAsync(liveHost, TogglePropertyPaneButton, "TogglePropertyPaneButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidatePropertyPaneAnchorableLifecycle(
                ViewModel.AvalonDockAnchorableHidingCount,
                propertyPaneVisibleChangedCountBefore,
                expectedHidden: false),
            DispatcherPriority.Send);

        int activityPaneCountBeforeClose = RightAnchorablePane.ChildrenCount;
        int activityPaneClosingCountBefore = ViewModel.AvalonDockAnchorableClosingCount;
        int activityPaneClosedCountBefore = ViewModel.AvalonDockAnchorableClosedCount;
        await ClickLiveControlAsync(liveHost, CloseActivityPaneButton, "CloseActivityPaneButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateActivityPaneClosedState(activityPaneClosingCountBefore, activityPaneClosedCountBefore);
                AssertEqual(activityPaneCountBeforeClose - 1, RightAnchorablePane.ChildrenCount, "Toolkit live activity anchorable count after close");
                AssertEqual("Activity pane closed", ViewModel.Status, "Toolkit live activity close status");
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, ReopenActivityPaneButton, "ReopenActivityPaneButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateActivityPaneReopenedState();
                AssertEqual(activityPaneCountBeforeClose, RightAnchorablePane.ChildrenCount, "Toolkit live activity anchorable count after reopen");
                AssertEqual("Activity pane reopened", ViewModel.Status, "Toolkit live activity reopen status");
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, ToggleActivityAutoHideButton, "ToggleActivityAutoHideButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(true, ActivityPane.IsAutoHidden, "Toolkit live activity pane auto-hide state");
                if (DockLayoutRoot.RightSide.ChildrenCount == 0)
                {
                    throw new InvalidOperationException("Expected Toolkit live activity pane to move into the AvalonDock right auto-hide side.");
                }
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, ToggleAgendaAutoHideButton, "ToggleAgendaAutoHideButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(false, AgendaPane.IsAutoHidden, "Toolkit live agenda pane docked state");
                AssertEqual(true, AgendaPane.IsVisible, "Toolkit live agenda pane visible state");
                if (AgendaPane.Parent is LayoutAnchorGroup)
                {
                    throw new InvalidOperationException("Expected Toolkit live agenda pane to leave the AvalonDock left auto-hide group.");
                }
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, SerializeLayoutButton, "SerializeLayoutButton");
        return await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                if (!ViewModel.LastSerializedLayout.Contains("<LayoutRoot", StringComparison.Ordinal) ||
                    !ViewModel.LastSerializedLayout.Contains("ContentId=\"overview\"", StringComparison.Ordinal) ||
                    !ViewModel.LastSerializedLayout.Contains("ContentId=\"editor\"", StringComparison.Ordinal) ||
                    !ViewModel.LastSerializedLayout.Contains("ContentId=\"activity\"", StringComparison.Ordinal) ||
                    !ViewModel.LastSerializedLayout.Contains("ContentId=\"agenda\"", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Expected Toolkit live AvalonDock serialization to include document content ids.");
                }

                var roundTripped = RoundTripLayout(ViewModel.LastSerializedLayout);
                if (roundTripped.Layout.RootPanel is null ||
                    roundTripped.Layout.RootPanel.ChildrenCount != DockLayoutRoot.RootPanel.ChildrenCount)
                {
                    throw new InvalidOperationException("Expected Toolkit live AvalonDock deserialization to restore root panel shape.");
                }

                ValidateAvalonDockLayoutReplacementEvents(ViewModel.LastSerializedLayout);

                return "host mouse/text input, binding update, Toolkit popup/dropdown editors, Toolkit masked/time/updown/checklist/rich/multiline/spinner editors, Toolkit auto-select/password/numeric/color-canvas controls, Toolkit selector/range/split controls, Toolkit resource theme updates, Toolkit wizard navigation, Toolkit child window lifecycle, Toolkit message box lifecycle, Toolkit window control primitive, Toolkit zoombox and magnifier, Toolkit panels, Toolkit DataGrid 100k virtualization, Toolkit/AvalonDock automation peers, Toolkit collection control and dialog button, AvalonDock source-backed documents/anchorables, AvalonDock manager options, AvalonDock layout update strategy and dynamic metadata, AvalonDock title selectors and layout item commands, AvalonDock tab group commands, AvalonDock keyboard navigation, AvalonDock anchorable keyboard navigation, AvalonDock auto-hide overlay keyboard navigation, AvalonDock theme switching, AvalonDock document context menu commands and close cancellation, AvalonDock anchorable context menu commands, AvalonDock anchorable lifecycle events, AvalonDock anchorable close/reopen, document activation, document close/reopen, floating document window, anchorable hide/show, auto-hide side groups, layout replacement events, and layout serialization updated";
            },
            DispatcherPriority.Send);
    }

    private async Task ValidateLivePopupControlsAsync(ProGpuWpfWindowHost liveHost)
    {
        await ClickLiveControlAsync(liveHost, ActionDropDownButton, "ActionDropDownButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ActionDropDownButton.IsOpen,
            "Toolkit live DropDownButton popup open state");

        await ValidateLivePopupOpenCloseAsync(
            liveHost,
            () => ActionDropDownButton.IsOpen,
            value => ActionDropDownButton.IsOpen = value,
            "Toolkit live DropDownButton popup");
        await ValidateLivePopupOpenCloseAsync(
            liveHost,
            () => CategoryPicker.IsDropDownOpen,
            value => CategoryPicker.IsDropDownOpen = value,
            "Toolkit live CheckComboBox dropdown");
        await ValidateLivePopupOpenCloseAsync(
            liveHost,
            () => ReminderTimePicker.IsOpen,
            value => ReminderTimePicker.IsOpen = value,
            "Toolkit live TimePicker popup");
        await ValidateLivePopupOpenCloseAsync(
            liveHost,
            () => AccentColorPicker.IsOpen,
            value => AccentColorPicker.IsOpen = value,
            "Toolkit live ColorPicker popup",
            () => AccentColorPicker.SelectedColor = Colors.MediumSeaGreen);
        await ValidateLivePopupOpenCloseAsync(
            liveHost,
            () => EstimateEditor.IsOpen,
            value => EstimateEditor.IsOpen = value,
            "Toolkit live CalculatorUpDown popup",
            () => EstimateEditor.Value = 42.25m);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateToolkitPopupState(expectedOpen: false);
                AssertEqual(Colors.MediumSeaGreen, ViewModel.AccentColor, "Toolkit live ColorPicker selected color binding source");
                AssertEqual(42.25m, ViewModel.Estimate, "Toolkit live CalculatorUpDown value binding source");
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => SplitActionButton.IsOpen = true,
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => SplitActionButton.IsOpen,
            "Toolkit live SplitButton dropdown open state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateToolkitSplitButtonPopupState(expectedOpen: true),
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => SplitActionButton.IsOpen = false,
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => !SplitActionButton.IsOpen,
            "Toolkit live SplitButton dropdown closed state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateToolkitSplitButtonPopupState(expectedOpen: false),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveAvalonDockDocumentContextMenuAsync(ProGpuWpfWindowHost liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                DockDocumentContextMenu.PlacementTarget = DockManager;
                DockDocumentContextMenu.IsOpen = true;
            },
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => DockDocumentContextMenu.IsOpen,
            "Toolkit live AvalonDock document context menu open state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateAvalonDockDocumentContextMenuState(expectedOpen: true);
                DockContextCancelNextCloseMenuItem.IsChecked = true;
                DockContextCancelNextCloseMenuItem.GetBindingExpression(MenuItem.IsCheckedProperty)?.UpdateSource();
                AssertEqual(true, ViewModel.CancelNextOverviewClose, "Toolkit live AvalonDock context menu cancellation binding");
                ExerciseAvalonDockDocumentContextMenuCommands();
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => DockDocumentContextMenu.IsOpen = false,
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => !DockDocumentContextMenu.IsOpen,
            "Toolkit live AvalonDock document context menu closed state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockDocumentContextMenuState(expectedOpen: false),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveAvalonDockAnchorableContextMenuAsync(ProGpuWpfWindowHost liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                DockAnchorableContextMenu.PlacementTarget = DockManager;
                DockAnchorableContextMenu.IsOpen = true;
            },
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => DockAnchorableContextMenu.IsOpen,
            "Toolkit live AvalonDock anchorable context menu open state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateAvalonDockAnchorableContextMenuState(expectedOpen: true);
                ExerciseAvalonDockAnchorableContextMenuCommands();
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => DockAnchorableContextMenu.IsOpen = false,
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(
            liveHost,
            () => !DockAnchorableContextMenu.IsOpen,
            "Toolkit live AvalonDock anchorable context menu closed state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockAnchorableContextMenuState(expectedOpen: false),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveAvalonDockKeyboardNavigationAsync(ProGpuWpfWindowHost liveHost)
    {
        int navigationCountBefore = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ActivateEditorDocument();
                ActivateEditorButton.Focus();
                Keyboard.Focus(ActivateEditorButton);
                return ViewModel.AvalonDockKeyboardNavigationCount;
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "F9");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "F9");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockKeyboardNavigationTarget(OverviewDocument, navigationCountBefore + 1),
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "F9");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "F9");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockKeyboardNavigationTarget(EditorDocument, navigationCountBefore + 2),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveAvalonDockAnchorableKeyboardNavigationAsync(ProGpuWpfWindowHost liveHost)
    {
        int navigationCountBefore = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                DockManager.ActiveContent = ToolkitPane.Content;
                SelectAvalonDockAnchorable(ToolkitPane);
                ToolkitPane.IsActive = true;
                _avalonDockAnchorableKeyboardNavigationIndex = 0;
                ViewModel.LastAvalonDockAnchorableKeyboardNavigationTarget = ToolkitPane.ContentId ?? ToolkitPane.Title;
                ActivateEditorButton.Focus();
                Keyboard.Focus(ActivateEditorButton);
                return ViewModel.AvalonDockAnchorableKeyboardNavigationCount;
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "F10");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "F10");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockAnchorableKeyboardNavigationTarget(PropertyPane, navigationCountBefore + 1),
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "F10");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "F10");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockAnchorableKeyboardNavigationTarget(ActivityPane, navigationCountBefore + 2),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveAvalonDockAutoHideOverlayAsync(ProGpuWpfWindowHost liveHost)
    {
        int overlayCountBefore = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                EnsureAutoHideOverlayAnchorables();
                _avalonDockAutoHideOverlayIndex = -1;
                ViewModel.LastAvalonDockAutoHideOverlayTarget = string.Empty;
                ActivateEditorButton.Focus();
                Keyboard.Focus(ActivateEditorButton);
                return ViewModel.AvalonDockAutoHideOverlayCount;
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "F11");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "F11");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockAutoHideCommandTarget(AgendaPane, overlayCountBefore + 1),
            DispatcherPriority.Send);
        AvalonDockLayoutAnchorControl agendaAnchorControl = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => FindAutoHideAnchorControl(AgendaPane),
            DispatcherPriority.Send);
        await ClickLiveControlAsync(liveHost, agendaAnchorControl, "AgendaAutoHideAnchorControl");
        await WaitForLiveConditionAsync(
            liveHost,
            () => AutoHideOverlayModelContains(GetAvalonDockAutoHideWindowModel(), AgendaPane),
            "Toolkit live AvalonDock agenda auto-hide overlay model");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockAutoHideOverlayTarget(AgendaPane, overlayCountBefore + 1),
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "F11");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "F11");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateAvalonDockAutoHideCommandTarget(ContactsPane, overlayCountBefore + 2),
            DispatcherPriority.Send);
        AvalonDockLayoutAnchorControl contactsAnchorControl = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => FindAutoHideAnchorControl(ContactsPane),
            DispatcherPriority.Send);
        await ClickLiveControlAsync(liveHost, contactsAnchorControl, "ContactsAutoHideAnchorControl");
        await WaitForLiveConditionAsync(
            liveHost,
            () => AutoHideOverlayModelContains(GetAvalonDockAutoHideWindowModel(), ContactsPane),
            "Toolkit live AvalonDock contacts auto-hide overlay model");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateAvalonDockAutoHideOverlayTarget(ContactsPane, overlayCountBefore + 2);
                HideAvalonDockAutoHideOverlay(ContactsPane);
            },
            DispatcherPriority.Send);
    }

    private async Task ValidateLivePopupOpenCloseAsync(
        ProGpuWpfWindowHost liveHost,
        Func<bool> isOpen,
        Action<bool> setOpen,
        string description,
        Action? whileOpen = null)
    {
        if (!await InvokeWithLiveHostWakeAsync(liveHost, isOpen, DispatcherPriority.Send))
        {
            await InvokeWithLiveHostWakeAsync(
                liveHost,
                () => setOpen(true),
                DispatcherPriority.Send);
        }

        await WaitForLiveConditionAsync(liveHost, isOpen, $"{description} open state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                whileOpen?.Invoke();
                setOpen(false);
            },
            DispatcherPriority.Send);
        await WaitForLiveConditionAsync(liveHost, () => !isOpen(), $"{description} closed state");
    }

    private async Task ValidateLiveInputEditorsAsync(ProGpuWpfWindowHost liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                QuickSearchTextBox.Text = "Live quick search";
                QuickSearchTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                AccessCodeBox.Password = "live-code";
                ViewModel.AccessCode = AccessCodeBox.Password;
                ReferenceMaskTextBox.Text = "AB-1234";
                ReferenceMaskTextBox.GetBindingExpression(MaskedTextBox.TextProperty)?.UpdateSource();

                ReminderTimePicker.Value = DateTime.Today.AddHours(15).AddMinutes(45);
                ReviewedAtEditor.Value = DateTime.Today.AddHours(16).AddMinutes(30);
                EffortEditor.Value = TimeSpan.FromMinutes(135);
                ByteScoreEditor.Value = 72;
                DoubleScaleEditor.Value = 4.5;
                WorkItemIdEditor.Value = 16384L;
                BudgetEditor.Value = 256.50m;
                AccentColorCanvas.SelectedColor = Colors.DarkCyan;

                OwnerComboBox.SelectedItem = "ProGPU";
                OwnerComboBox.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();

                PriorityRangeSlider.LowerValue = 3.0;
                PriorityRangeSlider.HigherValue = 7.0;
                PriorityRangeSlider.GetBindingExpression(RangeSlider.LowerValueProperty)?.UpdateSource();
                PriorityRangeSlider.GetBindingExpression(RangeSlider.HigherValueProperty)?.UpdateSource();

                if (!ViewModel.SelectedFlags.Contains("Reviewed"))
                {
                    ViewModel.SelectedFlags.Add("Reviewed");
                }

                ToolkitRichTextBox.Text = "Live rich notes from Toolkit RichTextBox";
                ToolkitRichTextBox.GetBindingExpression(ToolkitRichTextBox.TextProperty)?.UpdateSource();
                MultiLineNotesEditor.Text = "Live multiline notes from Toolkit MultiLineTextEditor";
                MultiLineNotesEditor.GetBindingExpression(MultiLineTextEditor.TextProperty)?.UpdateSource();

                ExerciseDocumentCountSpinner();
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateToolkitInputEditorState();
                AssertEqual("Live quick search", ViewModel.QuickSearchText, "Toolkit live AutoSelectTextBox binding source");
                AssertEqual("live-code", ViewModel.AccessCode, "Toolkit live WatermarkPasswordBox password state");
                AssertEqual("AB-1234", ViewModel.ReferenceCode, "Toolkit live MaskedTextBox binding source");
                AssertEqual(DateTime.Today.AddHours(15).AddMinutes(45), ViewModel.ReminderTime, "Toolkit live TimePicker binding source");
                AssertEqual(DateTime.Today.AddHours(16).AddMinutes(30), ViewModel.ReviewedAt, "Toolkit live DateTimeUpDown binding source");
                AssertEqual(TimeSpan.FromMinutes(135), ViewModel.Effort, "Toolkit live TimeSpanUpDown binding source");
                AssertEqual((byte)72, ViewModel.ByteScore.GetValueOrDefault(), "Toolkit live ByteUpDown binding source");
                AssertEqual(4.5, ViewModel.DoubleScale.GetValueOrDefault(), "Toolkit live DoubleUpDown binding source");
                AssertEqual(16384L, ViewModel.WorkItemId.GetValueOrDefault(), "Toolkit live LongUpDown binding source");
                AssertEqual(256.50m, ViewModel.Budget.GetValueOrDefault(), "Toolkit live DecimalUpDown binding source");
                AssertEqual(Colors.DarkCyan, ViewModel.AccentColor.GetValueOrDefault(), "Toolkit live ColorCanvas binding source");
                AssertEqual("Live rich notes from Toolkit RichTextBox", ViewModel.RichNotes, "Toolkit live RichTextBox binding source");
                AssertEqual("Live multiline notes from Toolkit MultiLineTextEditor", ViewModel.MultiLineNotes, "Toolkit live MultiLineTextEditor binding source");
                AssertEqual("ProGPU", ViewModel.SelectedOwner, "Toolkit live WatermarkComboBox binding source");
                AssertEqual(3.0, ViewModel.PriorityRangeStart, "Toolkit live RangeSlider lower binding source");
                AssertEqual(7.0, ViewModel.PriorityRangeEnd, "Toolkit live RangeSlider higher binding source");
                if (!FlagListBox.SelectedItems.Contains("Reviewed"))
                {
                    throw new InvalidOperationException("Expected Toolkit live CheckListBox to select the added flag.");
                }
            },
            DispatcherPriority.Send);

        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            await ClickLiveControlAsync(liveHost, SplitActionButton, "SplitActionButton");
            if (await InvokeWithLiveHostWakeAsync(
                    liveHost,
                    () => string.Equals(ViewModel.Status, "Applied owner ProGPU", StringComparison.Ordinal),
                    DispatcherPriority.Send))
            {
                return;
            }

            WakeLiveRenderHost(liveHost);
            await Task.Delay(LiveValidationRetryDelay);
        }

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => AssertEqual("Applied owner ProGPU", ViewModel.Status, "Toolkit live SplitButton host click status"),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveWizardAsync(ProGpuWpfWindowHost liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ExerciseToolkitWizard(),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveToolkitResourceThemeAsync(ProGpuWpfWindowHost liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ExerciseToolkitResourceTheme(),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveToolkitChildWindowAsync(ProGpuWpfWindowHost liveHost)
    {
        int showCountBefore = ViewModel.ChildWindowShowCount;
        int closingCountBefore = ViewModel.ChildWindowClosingCount;
        int closedCountBefore = ViewModel.ChildWindowClosedCount;

        await ClickLiveControlAsync(liveHost, ShowChildWindowButton, "ShowChildWindowButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ToolkitChildWindow.WindowState == Xceed.Wpf.Toolkit.WindowState.Open,
            "Toolkit live ChildWindow open state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateToolkitChildWindowState(expectedOpen: true);
                AssertEqual(showCountBefore + 1, ViewModel.ChildWindowShowCount, "Toolkit live ChildWindow show count");
                if (!ToolkitChildWindow.IsKeyboardFocusWithin)
                {
                    throw new InvalidOperationException(
                        $"Expected Toolkit live ChildWindow to receive focus, but focused '{DescribeInputElement(Keyboard.FocusedElement)}'.");
                }
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, AcceptChildWindowButton, "AcceptChildWindowButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ToolkitChildWindow.WindowState == Xceed.Wpf.Toolkit.WindowState.Closed,
            "Toolkit live ChildWindow closed state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateToolkitChildWindowState(expectedOpen: false);
                AssertEqual(closingCountBefore + 1, ViewModel.ChildWindowClosingCount, "Toolkit live ChildWindow closing count");
                AssertEqual(closedCountBefore + 1, ViewModel.ChildWindowClosedCount, "Toolkit live ChildWindow closed count");
                AssertEqual(true, ViewModel.LastChildWindowDialogResult, "Toolkit live ChildWindow dialog result");
                AssertEqual("ChildWindow accepted", ViewModel.ChildWindowStatus, "Toolkit live ChildWindow accepted status");
            },
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveToolkitMessageBoxAsync(ProGpuWpfWindowHost liveHost)
    {
        int showCountBefore = ViewModel.ToolkitMessageBoxShowCount;
        int closedCountBefore = ViewModel.ToolkitMessageBoxClosedCount;

        await ClickLiveControlAsync(liveHost, ShowToolkitMessageBoxButton, "ShowToolkitMessageBoxButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ToolkitMessageBox.Visibility == Visibility.Visible,
            "Toolkit live MessageBox open state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateToolkitMessageBoxState(expectedOpen: true);
                AssertEqual(showCountBefore + 1, ViewModel.ToolkitMessageBoxShowCount, "Toolkit live MessageBox show count");
                Button okButton = GetToolkitMessageBoxButton("PART_OkButton");
                okButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, okButton));
            },
            DispatcherPriority.Send);

        await WaitForLiveConditionAsync(
            liveHost,
            () => ToolkitMessageBox.Visibility == Visibility.Collapsed,
            "Toolkit live MessageBox closed state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateToolkitMessageBoxState(expectedOpen: false);
                AssertEqual(closedCountBefore + 1, ViewModel.ToolkitMessageBoxClosedCount, "Toolkit live MessageBox closed count");
                AssertEqual(MessageBoxResult.OK, ViewModel.LastToolkitMessageBoxResult, "Toolkit live MessageBox result");
                AssertEqual("MessageBox OK", ViewModel.ToolkitMessageBoxStatus, "Toolkit live MessageBox OK status");
            },
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveToolkitWindowControlAsync(ProGpuWpfWindowHost liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ShowToolkitWindowControl();
                WindowControlInputTextBox.Text = string.Empty;
                WindowControlInputTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                ValidateToolkitWindowControlState(expectedVisible: true, expectLoaded: true);
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, WindowControlInputTextBox, "WindowControlInputTextBox");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                if (!WindowControlInputTextBox.IsKeyboardFocusWithin)
                {
                    throw new InvalidOperationException(
                        $"Expected Toolkit live WindowControl input to receive focus, but focused '{DescribeInputElement(Keyboard.FocusedElement)}'.");
                }

                foreach (char character in "Pane")
                {
                    string key = char.ToUpperInvariant(character).ToString();
                    RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: key);
                    RaiseHostInput(liveHost, WpfInputEventKind.TextInput, character: character);
                    RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: key);
                }
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual("Pane", WindowControlInputTextBox.Text, "Toolkit live WindowControl input text");
                AssertEqual("Pane", ViewModel.WindowControlText, "Toolkit live WindowControl input binding source");
            },
            DispatcherPriority.Send);

        int activatedCountBefore = ViewModel.WindowControlActivatedCount;
        await ClickLiveControlAsync(liveHost, ActivateWindowControlButton, "ActivateWindowControlButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ViewModel.WindowControlActivatedCount > activatedCountBefore,
            "Toolkit live WindowControl activation event");

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                int headerClickCountBefore = ViewModel.WindowControlHeaderClickCount;
                int headerIconClickCountBefore = ViewModel.WindowControlHeaderIconClickCount;
                int headerIconDoubleClickCountBefore = ViewModel.WindowControlHeaderIconDoubleClickCount;
                int headerDoubleClickCountBefore = ViewModel.WindowControlHeaderDoubleClickCount;
                int headerRightClickCountBefore = ViewModel.WindowControlHeaderRightClickCount;
                int headerDragCountBefore = ViewModel.WindowControlHeaderDragCount;
                RaiseToolkitWindowControlHeaderClick();
                RaiseToolkitWindowControlHeaderIconClick();
                RaiseToolkitWindowControlHeaderIconDoubleClick();
                RaiseToolkitWindowControlHeaderDoubleClick();
                RaiseToolkitWindowControlHeaderRightClick();
                RaiseToolkitWindowControlHeaderDrag();
                AssertEqual(headerClickCountBefore + 1, ViewModel.WindowControlHeaderClickCount, "Toolkit live WindowControl header click count");
                AssertEqual(headerIconClickCountBefore + 1, ViewModel.WindowControlHeaderIconClickCount, "Toolkit live WindowControl header icon click count");
                AssertEqual(headerIconDoubleClickCountBefore + 1, ViewModel.WindowControlHeaderIconDoubleClickCount, "Toolkit live WindowControl header icon double-click count");
                AssertEqual(headerDoubleClickCountBefore + 1, ViewModel.WindowControlHeaderDoubleClickCount, "Toolkit live WindowControl header double-click count");
                AssertEqual(headerRightClickCountBefore + 1, ViewModel.WindowControlHeaderRightClickCount, "Toolkit live WindowControl header right-click count");
                AssertEqual(headerDragCountBefore + 1, ViewModel.WindowControlHeaderDragCount, "Toolkit live WindowControl header drag count");
            },
            DispatcherPriority.Send);

        int closeClickCountBefore = ViewModel.WindowControlCloseButtonClickCount;
        Button closeButton = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => GetToolkitWindowControlButton("PART_CloseButton"),
            DispatcherPriority.Send);
        await ClickLiveControlAsync(liveHost, closeButton, "ToolkitWindowControlCloseButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ToolkitWindowControl.Visibility == Visibility.Collapsed &&
                  ViewModel.WindowControlCloseButtonClickCount == closeClickCountBefore + 1,
            "Toolkit live WindowControl close button event");

        await ClickLiveControlAsync(liveHost, ToggleWindowControlButton, "ToggleWindowControlButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ToolkitWindowControl.Visibility == Visibility.Visible,
            "Toolkit live WindowControl toggle visible state");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateToolkitWindowControlState(expectedVisible: true, expectLoaded: true);
                CloseToolkitWindowControl("WindowControl live validated");
            },
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveToolkitZoomboxAndMagnifierAsync(ProGpuWpfWindowHost liveHost)
    {
        int commandCountBefore = ViewModel.ZoomboxCommandCount;
        int viewChangedCountBefore = ViewModel.ZoomboxViewChangedCount;

        await ClickLiveControlAsync(liveHost, ZoomInButton, "ZoomInButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ViewModel.ZoomboxCommandCount > commandCountBefore &&
                  ViewModel.ZoomboxViewChangedCount > viewChangedCountBefore,
            "Toolkit live Zoombox zoom input");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ValidateToolkitZoomboxAndMagnifierState(expectLoaded: true);
                if (string.Equals(ViewModel.ZoomboxStatus, "Zoombox idle", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Expected Toolkit live Zoombox status to update after zoom input.");
                }
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, FitZoomboxButton, "FitZoomboxButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ToolkitZoombox.CurrentView.ViewKind == ZoomboxViewKind.Fit,
            "Toolkit live Zoombox fit view");

        await ClickLiveControlAsync(liveHost, BackZoomboxButton, "BackZoomboxButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ToolkitMagnifier.Freeze(true);
                AssertEqual(true, ToolkitMagnifier.IsFrozen, "Toolkit live Magnifier frozen state");
                ToolkitMagnifier.Freeze(false);
                AssertEqual(false, ToolkitMagnifier.IsFrozen, "Toolkit live Magnifier unfrozen state");
                ValidateToolkitZoomboxAndMagnifierState(expectLoaded: true);
            },
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveToolkitCollectionControlAsync(ProGpuWpfWindowHost liveHost)
    {
        int countBefore = ViewModel.CollectionEntries.Count;

        await ClickLiveControlAsync(liveHost, AddCollectionEntryButton, "AddCollectionEntryButton");
        await WaitForLiveConditionAsync(
            liveHost,
            () => ViewModel.CollectionEntries.Count == countBefore + 1 &&
                  ReferenceEquals(ViewModel.SelectedCollectionEntry, ViewModel.CollectionEntries[^1]),
            "Toolkit live CollectionControl add entry");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual($"Added Entry {countBefore + 1}", ViewModel.CollectionControlStatus, "Toolkit live CollectionControl add status");
                ValidateToolkitCollectionControlState(expectLoaded: true);
                ValidateToolkitCollectionDialogButtonState(expectLoaded: true);
            },
            DispatcherPriority.Send);

        await ClickLiveControlAsync(liveHost, SelectCollectionEntryButton, "SelectCollectionEntryButton");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(ViewModel.CollectionEntries[1], ViewModel.SelectedCollectionEntry, "Toolkit live CollectionControl selected view-model entry");
                AssertEqual(ViewModel.SelectedCollectionEntry, ToolkitCollectionControl.SelectedItem, "Toolkit live CollectionControl selected item");
                AssertEqual($"Selected {ViewModel.CollectionEntries[1].Name}", ViewModel.CollectionControlStatus, "Toolkit live CollectionControl select status");
                ValidateToolkitCollectionControlState(expectLoaded: true);
                ValidateToolkitCollectionDialogButtonState(expectLoaded: true);
            },
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveSourceBackedAvalonDockAsync(ProGpuWpfWindowHost liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => ValidateSourceBackedAvalonDockState(mutateSources: true),
            DispatcherPriority.Send);
    }

    private async Task ValidateLiveAvalonDockThemeSwitchingAsync(ProGpuWpfWindowHost liveHost)
    {
        int themeSwitchCountBefore = ViewModel.DockThemeSwitchCount;
        string[] expectedThemes = ["Metro", "VS2010", "Aero"];
        foreach (string expectedTheme in expectedThemes)
        {
            await ClickLiveControlAsync(liveHost, CycleDockThemeButton, "CycleDockThemeButton");
            await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    ValidateAvalonDockThemeState(expectedTheme);
                    AssertEqual(
                        $"AvalonDock theme switched to {expectedTheme}",
                        ViewModel.Status,
                        "Toolkit live AvalonDock theme switch status");
                },
                DispatcherPriority.Send);
        }

        if (ViewModel.DockThemeSwitchCount < themeSwitchCountBefore + expectedThemes.Length)
        {
            throw new InvalidOperationException("Expected live AvalonDock theme switch count to advance for each theme.");
        }
    }

    private async Task ValidateLiveOverviewDocumentLifecycleAsync(ProGpuWpfWindowHost liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                int documentCountBeforeCanceledClose = DocumentPane.ChildrenCount;
                int closedCountBeforeCanceledClose = ViewModel.OverviewDocumentClosedCount;
                ViewModel.CancelNextOverviewClose = true;
                CloseOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                ValidateOverviewCloseCanceledState(documentCountBeforeCanceledClose, closedCountBeforeCanceledClose);
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                int documentCountBeforeClose = DocumentPane.ChildrenCount;
                CloseOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                ValidateOverviewDocumentLifecycleState(expectedOpen: false);
                AssertEqual(documentCountBeforeClose - 1, DocumentPane.ChildrenCount, "Toolkit live AvalonDock document count after overview close");
                AssertEqual("Overview document closed", ViewModel.Status, "Toolkit live overview close status");
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                int documentCountBeforeReopen = DocumentPane.ChildrenCount;
                ReopenOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                ValidateOverviewDocumentLifecycleState(expectedOpen: true);
                AssertEqual(documentCountBeforeReopen + 1, DocumentPane.ChildrenCount, "Toolkit live AvalonDock document count after overview reopen");
                AssertEqual("Overview document reopened", ViewModel.Status, "Toolkit live overview reopen status");
            },
            DispatcherPriority.Send);
    }

    private async Task WaitForLiveConditionAsync(ProGpuWpfWindowHost liveHost, Func<bool> condition, string description)
    {
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            if (await InvokeWithLiveHostWakeAsync(liveHost, condition, DispatcherPriority.Background))
            {
                return;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        throw new InvalidOperationException($"Timed out waiting for {description}.");
    }

    private async Task ClickLiveControlAsync(ProGpuWpfWindowHost liveHost, FrameworkElement target, string targetName)
    {
        string lastTargetState = "not checked";
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            bool sentClick = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () => TryRaiseLiveMouseClick(liveHost, target, targetName, out lastTargetState),
                DispatcherPriority.Send);
            if (sentClick)
            {
                await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
                return;
            }

            // BringIntoView can move a deeply scrolled editor after the render request that
            // started this attempt.  Publish that new layout before retrying so the managed
            // input tree and the retained GPU owner index agree on the click coordinate.
            WakeLiveRenderHost(liveHost);
            await Task.Delay(LiveValidationRetryDelay);
        }

        throw new InvalidOperationException(
            $"Expected Toolkit live target {targetName} to become visible and hit-testable, but last state was: {lastTargetState}.");
    }

    private bool TryRaiseLiveMouseClick(ProGpuWpfWindowHost liveHost, FrameworkElement target, string targetName, out string targetState)
    {
        Point initialCenter = target.TranslatePoint(
            new Point(Math.Max(1.0, target.ActualWidth) / 2.0, Math.Max(1.0, target.ActualHeight) / 2.0),
            this);
        target.BringIntoView();
        target.UpdateLayout();

        targetState =
            $"{targetName}.IsVisible={target.IsVisible}, " +
            $"{targetName}.ActualSize={target.ActualWidth:0.###}x{target.ActualHeight:0.###}, " +
            $"{targetName}.IsEnabled={target.IsEnabled}, " +
            $"{targetName}.Focusable={target.Focusable}, " +
            $"{targetName}.IsHitTestVisible={target.IsHitTestVisible}";
        if (!target.IsVisible ||
            target.ActualWidth <= 1.0 ||
            target.ActualHeight <= 1.0 ||
            !target.IsEnabled ||
            !target.IsHitTestVisible)
        {
            return false;
        }

        Point center = target.TranslatePoint(
            new Point(Math.Max(1.0, target.ActualWidth) / 2.0, Math.Max(1.0, target.ActualHeight) / 2.0),
            this);
        double layoutDeltaX = center.X - initialCenter.X;
        double layoutDeltaY = center.Y - initialCenter.Y;
        if (Math.Abs(layoutDeltaX) > 0.5 || Math.Abs(layoutDeltaY) > 0.5)
        {
            targetState += $", BringIntoViewDelta=({layoutDeltaX:0.###}, {layoutDeltaY:0.###})";
            return false;
        }

        object? hit = InputHitTest(center);
        targetState += $", Input=({center.X:0.###}, {center.Y:0.###}), InputHitTest={DescribeInputElement(hit)}";
        if (!TryLiveHostGpuHitWithinTarget(liveHost, center.X, center.Y, target, out string gpuHitState))
        {
            targetState += $", {gpuHitState}";
            return false;
        }

        targetState += $", {gpuHitState}";
        RaiseHostInput(liveHost, WpfInputEventKind.MouseMove, x: center.X, y: center.Y);
        RaiseHostInput(liveHost, WpfInputEventKind.MouseDown, x: center.X, y: center.Y, button: WpfMouseButton.Left);
        RaiseHostInput(liveHost, WpfInputEventKind.MouseUp, x: center.X, y: center.Y, button: WpfMouseButton.Left);
        return true;
    }

    private static bool TryLiveHostGpuHitWithinTarget(
        ProGpuWpfWindowHost liveHost,
        double x,
        double y,
        FrameworkElement target,
        out string state)
    {
        state = "GpuHitTest=<unavailable>";
        if (!ProGpuWpfDiagnostics.TryHitTestInputOwner(liveHost, x, y, out object? selectedOwner))
        {
            return false;
        }

        state = $"GpuInputOwner={DescribeInputElement(selectedOwner)}";
        if (selectedOwner == null || !IsInputElementWithinTarget(selectedOwner, target))
        {
            return false;
        }

        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(GpuOwnerBufferCapacity);
        try
        {
            if (!ProGpuWpfDiagnostics.TryHitTestOwners(liveHost, x, y, ownerBuffer, out int ownerCount))
            {
                return false;
            }

            var owners = ownerBuffer.AsSpan(0, ownerCount);
            state += $", GpuHitTest=[{DescribeInputElements(owners)}]";
            if (ownerCount == 0)
            {
                return false;
            }

            return true;
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }
    }

    private static string DescribeInputElements(ReadOnlySpan<object?> owners)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < owners.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(DescribeInputElement(owners[i]));
        }

        return builder.ToString();
    }

    private static bool IsInputElementWithinTarget(object hit, FrameworkElement target)
    {
        if (ReferenceEquals(hit, target))
        {
            return true;
        }

        var current = hit as DependencyObject;
        while (current != null)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            DependencyObject? parent = null;
            try
            {
                parent = VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
            }

            parent ??= LogicalTreeHelper.GetParent(current);
            current = parent;
        }

        return false;
    }

    private async Task<T> InvokeWithLiveHostWakeAsync<T>(
        ProGpuWpfWindowHost liveHost,
        Func<T> callback,
        DispatcherPriority priority)
    {
        if (Dispatcher.CheckAccess())
        {
            return callback();
        }

        DispatcherOperation<T> operation = Dispatcher.InvokeAsync(callback, priority);
        WakeLiveRenderHost(liveHost);
        return await operation;
    }

    private async Task InvokeWithLiveHostWakeAsync(
        ProGpuWpfWindowHost liveHost,
        Action callback,
        DispatcherPriority priority)
    {
        if (Dispatcher.CheckAccess())
        {
            callback();
            return;
        }

        DispatcherOperation operation = Dispatcher.InvokeAsync(callback, priority);
        WakeLiveRenderHost(liveHost);
        await operation;
    }

    private static void WakeLiveRenderHost(ProGpuWpfWindowHost liveHost)
    {
        if (!ProGpuWpfDiagnostics.TryRequestRender(liveHost))
        {
            throw new InvalidOperationException("Expected ProGPU WPF diagnostics to request a live Toolkit render.");
        }
    }

    private static string ValidateLiveRenderSurfaceGeometryCore(ProGpuWpfWindowHost liveHost)
    {
        if (!ProGpuWpfDiagnostics.TryGetRenderSurfaceGeometry(liveHost, out var geometry))
        {
            throw new InvalidOperationException("Expected ProGPU WPF diagnostics to resolve Toolkit render-surface geometry.");
        }

        uint logicalWidth = geometry.LogicalWidth;
        uint logicalHeight = geometry.LogicalHeight;
        uint pixelWidth = geometry.PixelWidth;
        uint pixelHeight = geometry.PixelHeight;
        double dpiScale = geometry.DpiScale;
        uint viewportX = geometry.ViewportX;
        uint viewportY = geometry.ViewportY;
        uint viewportWidth = geometry.ViewportWidth;
        uint viewportHeight = geometry.ViewportHeight;

        AssertEqual(980u, logicalWidth, "Toolkit live ProGPU WPF logical width");
        AssertEqual(640u, logicalHeight, "Toolkit live ProGPU WPF logical height");
        if (pixelWidth < logicalWidth || pixelHeight < logicalHeight)
        {
            throw new InvalidOperationException(
                $"Expected Toolkit live ProGPU WPF pixels to cover logical content, but got logical {logicalWidth}x{logicalHeight} and pixels {pixelWidth}x{pixelHeight}.");
        }

        if (viewportX != 0 || viewportY != 0 || viewportWidth != pixelWidth || viewportHeight != pixelHeight)
        {
            throw new InvalidOperationException(
                $"Expected Toolkit live ProGPU WPF viewport to use the full physical target, but got viewport {viewportWidth}x{viewportHeight}@{viewportX},{viewportY} for pixels {pixelWidth}x{pixelHeight}.");
        }

        return $"logical {logicalWidth}x{logicalHeight}, pixels {pixelWidth}x{pixelHeight}, viewport {viewportWidth}x{viewportHeight}@{viewportX},{viewportY}, dpi {dpiScale:0.###}";
    }

    private static string ValidateLiveDisplayMetricsCore(ProGpuWpfWindowHost liveHost)
    {
        IReadOnlyList<WpfMonitorInfo> monitors = liveHost.PlatformServices.Monitors.GetMonitors();
        if (monitors.Count == 0)
        {
            throw new InvalidOperationException("Expected the Toolkit live host to expose at least one monitor.");
        }

        WpfMonitorInfo primary = monitors.FirstOrDefault(monitor => monitor.IsPrimary);
        if (primary.Width <= 0 || primary.Height <= 0)
        {
            primary = monitors[0];
        }

        double coordinateScale = primary.UsesLogicalCoordinates || primary.DpiScale <= 0
            ? 1.0
            : primary.DpiScale;
        double expectedWidth = primary.Width / coordinateScale;
        double expectedHeight = primary.Height / coordinateScale;
        double actualWidth = SystemParameters.PrimaryScreenWidth;
        double actualHeight = SystemParameters.PrimaryScreenHeight;
        const double crossBackendTolerance = 4.0;
        if (Math.Abs(actualWidth - expectedWidth) > crossBackendTolerance ||
            Math.Abs(actualHeight - expectedHeight) > crossBackendTolerance)
        {
            throw new InvalidOperationException(
                $"Expected Toolkit SystemParameters primary screen {expectedWidth:0.###}x{expectedHeight:0.###} DIPs from monitor geometry, but got {actualWidth:0.###}x{actualHeight:0.###}.");
        }

        return $"screen {actualWidth:0.###}x{actualHeight:0.###} DIPs";
    }

    private static void RaiseHostInput(
        ProGpuWpfWindowHost liveHost,
        WpfInputEventKind kind,
        string? key = null,
        char? character = null,
        double x = 0.0,
        double y = 0.0,
        WpfMouseButton button = WpfMouseButton.None)
    {
        var input = new WpfInputEventArgs(
            kind,
            key,
            character: character,
            x: x,
            y: y,
            button: button);
        if (!ProGpuWpfDiagnostics.TryRaiseInput(liveHost, input))
        {
            throw new InvalidOperationException("Expected ProGPU WPF diagnostics to inject Toolkit live input.");
        }
    }

    private static string DescribeInputElement(object? element)
    {
        if (element == null)
        {
            return "<null>";
        }

        if (element is FrameworkElement frameworkElement && !string.IsNullOrEmpty(frameworkElement.Name))
        {
            return $"FrameworkElement#{frameworkElement.Name}";
        }

        return element is IInputElement
            ? "IInputElement"
            : element.ToString() ?? "<input element>";
    }

    private static void PumpDispatcherUntil(
        DispatcherObject dispatcherObject,
        Func<bool> condition,
        TimeSpan timeout,
        string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            dispatcherObject.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.Background);

            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException($"Timed out waiting for {description}.");
            }

            System.Threading.Thread.Sleep(1);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expected}' but was '{actual}'.");
        }
    }
}

internal sealed class ToolkitViewModel : INotifyPropertyChanged
{
    private ToolkitDocument _selectedDocument;
    private int _priority = 4;
    private string _filterText = string.Empty;
    private string _quickSearchText = "Search";
    private string _accessCode = "sdk";
    private string _selectedOwner = "WPF";
    private double _priorityRangeStart = 2.0;
    private double _priorityRangeEnd = 8.0;
    private DateTime? _dueDate = DateTime.Today.AddDays(7).AddHours(9);
    private DateTime? _reminderTime = DateTime.Today.AddHours(10).AddMinutes(15);
    private DateTime? _reviewedAt = DateTime.Today.AddHours(11);
    private TimeSpan? _effort = TimeSpan.FromMinutes(90);
    private string _referenceCode = "PR-2048";
    private Color? _accentColor = Colors.SteelBlue;
    private decimal? _estimate = 12.5m;
    private byte? _byteScore = 12;
    private double? _doubleScale = 1.5;
    private long? _workItemId = 4096L;
    private decimal? _budget = 64.25m;
    private string _richNotes = "Toolkit rich notes";
    private string _multiLineNotes = "Toolkit multiline notes";
    private int _spinnerCount = 2;
    private int _toolkitResourceThemeUpdateCount;
    private bool _isBusy;
    private int _childWindowShowCount;
    private int _childWindowClosingCount;
    private int _childWindowClosedCount;
    private bool? _lastChildWindowDialogResult;
    private string _childWindowStatus = "ChildWindow idle";
    private int _toolkitMessageBoxShowCount;
    private int _toolkitMessageBoxClosedCount;
    private MessageBoxResult _lastToolkitMessageBoxResult = MessageBoxResult.None;
    private string _toolkitMessageBoxStatus = "MessageBox idle";
    private int _staticToolkitMessageBoxShowCount;
    private int _staticToolkitMessageBoxClosedCount;
    private MessageBoxResult _lastStaticToolkitMessageBoxResult = MessageBoxResult.None;
    private string _staticToolkitMessageBoxStatus = "Static message idle";
    private IntPtr _lastStaticToolkitMessageBoxOwnerHandle = IntPtr.Zero;
    private string _windowContainerStatus = "WindowContainer idle";
    private Visibility _toolkitWindowControlVisibility = Visibility.Visible;
    private string _windowControlStatus = "WindowControl visible";
    private string _windowControlText = "WindowControl primitive";
    private int _windowControlToggleCount;
    private int _windowControlActivatedCount;
    private int _windowControlHeaderClickCount;
    private int _windowControlHeaderIconClickCount;
    private int _windowControlHeaderIconDoubleClickCount;
    private int _windowControlHeaderDoubleClickCount;
    private int _windowControlHeaderRightClickCount;
    private int _windowControlHeaderDragCount;
    private int _windowControlCloseButtonClickCount;
    private int _zoomboxCommandCount;
    private int _zoomboxViewChangedCount;
    private int _zoomboxViewStackIndexChangedCount;
    private int _lastZoomboxViewStackIndex = -1;
    private double _lastZoomboxScale = 1.0;
    private string _zoomboxStatus = "Zoombox idle";
    private ToolkitDataGridItem _selectedDataGridItem = null!;
    private ToolkitCollectionEntry _selectedCollectionEntry = null!;
    private string _collectionControlStatus = "CollectionControl idle";
    private int _collectionDialogUpdateCount;
    private int _wizardPageChanges;
    private int _wizardFinishes;
    private int _wizardCancels;
    private string _wizardStatus = "Wizard idle";
    private int _avalonDockActiveContentChangedCount;
    private string _lastActiveContentTitle = string.Empty;
    private int _avalonDockDocumentClosingCount;
    private int _avalonDockDocumentClosedCount;
    private int _avalonDockDocumentCloseCanceledCount;
    private int _overviewDocumentClosedCount;
    private int _avalonDockFloatedCount;
    private int _avalonDockDockedCount;
    private int _avalonDockLayoutChangingCount;
    private int _avalonDockLayoutChangedCount;
    private int _avalonDockAnchorableHidingCount;
    private int _avalonDockAnchorableIsVisibleChangedCount;
    private string _lastAvalonDockAnchorableLifecycleTarget = string.Empty;
    private int _avalonDockAnchorableClosingCount;
    private int _avalonDockAnchorableClosedCount;
    private int _avalonDockContextMenuCommandCanExecuteCount;
    private int _avalonDockContextMenuCommandExecutedCount;
    private string _lastAvalonDockContextMenuCommand = string.Empty;
    private int _avalonDockAnchorableContextMenuCommandCanExecuteCount;
    private int _avalonDockAnchorableContextMenuCommandExecutedCount;
    private string _lastAvalonDockAnchorableContextMenuCommand = string.Empty;
    private int _avalonDockKeyboardNavigationCanExecuteCount;
    private int _avalonDockKeyboardNavigationCount;
    private string _lastAvalonDockKeyboardNavigationTarget = string.Empty;
    private int _avalonDockAnchorableKeyboardNavigationCanExecuteCount;
    private int _avalonDockAnchorableKeyboardNavigationCount;
    private string _lastAvalonDockAnchorableKeyboardNavigationTarget = string.Empty;
    private int _avalonDockAutoHideOverlayCanExecuteCount;
    private int _avalonDockAutoHideOverlayCount;
    private string _lastAvalonDockAutoHideOverlayTarget = string.Empty;
    private bool _cancelNextOverviewClose;
    private string _lastClosingDocumentContentId = string.Empty;
    private string _lastClosedDocumentContentId = string.Empty;
    private string _lastClosingAnchorableContentId = string.Empty;
    private string _lastClosedAnchorableContentId = string.Empty;
    private object? _sourceActiveContent;
    private int _sourceActiveContentChangedCount;
    private string _lastSourceActiveTitle = string.Empty;
    private int _sourceTabGroupCommandCount;
    private string _activeDockThemeName = "Aero";
    private int _dockThemeSwitchCount;
    private string _status = "Toolkit sample ready";
    private string _lastSerializedLayout = string.Empty;

    public ToolkitViewModel()
    {
        Documents =
        [
            new("Overview", "WPF", DateTime.Today, "No-source-change SDK app consuming Extended WPF Toolkit."),
            new("AvalonDock", "Xceed", DateTime.Today.AddDays(-1), "DockingManager layout with documents and anchorables."),
            new("DataGrid 100k", "WPF Toolkit", DateTime.Today.AddDays(-2), "Virtualized 100,000-row DataGrid performance document.")
        ];
        DataGridItems = CreateDataGridItems(100_000);
        SourceDocuments =
        [
            new("Source Overview", "source-overview", "Generated from DockingManager.DocumentsSource.", canClose: true),
            new("Source Editor", "source-editor", "Another source-backed document view model.", canClose: true)
        ];
        SourceAnchorables =
        [
            new("Source Tool", "source-tool", "Generated from DockingManager.AnchorablesSource.", canClose: false)
        ];
        CollectionEntries =
        [
            new("Alpha", "Framework", 10),
            new("Beta", "Rendering", 20)
        ];
        CollectionEntryTypes = [typeof(ToolkitCollectionEntry)];
        Owners = ["WPF", "ProGPU", "SDK", "Xceed"];
        Categories = ["Framework", "Toolkit", "AvalonDock", "Rendering"];
        SelectedCategories = ["Toolkit", "AvalonDock"];
        Flags = ["Pinned", "Reviewed", "Blocked", "Urgent"];
        SelectedFlags = ["Pinned"];
        Activity = ["Toolkit package loaded", "AvalonDock layout loaded"];
        _selectedDocument = Documents[0];
        _selectedDataGridItem = DataGridItems[0];
        _sourceActiveContent = SourceDocuments[0];
        _selectedCollectionEntry = CollectionEntries[0];
        Documents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(DocumentCount));
        SourceDocuments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SourceDocumentCount));
        SourceAnchorables.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SourceAnchorableCount));
        CollectionEntries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CollectionEntryCount));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ToolkitDocument> Documents { get; }

    public ObservableCollection<ToolkitDockItem> SourceDocuments { get; }

    public ObservableCollection<ToolkitDockItem> SourceAnchorables { get; }

    public ObservableCollection<ToolkitCollectionEntry> CollectionEntries { get; }

    public IReadOnlyList<ToolkitDataGridItem> DataGridItems { get; }

    public ObservableCollection<Type> CollectionEntryTypes { get; }

    public ToolkitLayoutUpdateStrategy SourceLayoutStrategy { get; } = new();

    public ObservableCollection<string> Categories { get; }

    public ObservableCollection<string> Owners { get; }

    public ObservableCollection<string> SelectedCategories { get; }

    public ObservableCollection<string> Flags { get; }

    public ObservableCollection<string> SelectedFlags { get; }

    public ObservableCollection<string> Activity { get; }

    public ToolkitDocument SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (!ReferenceEquals(_selectedDocument, value))
            {
                _selectedDocument = value;
                OnPropertyChanged();
            }
        }
    }

    public int DocumentCount => Documents.Count;

    public int SourceDocumentCount => SourceDocuments.Count;

    public int SourceAnchorableCount => SourceAnchorables.Count;

    public int CollectionEntryCount => CollectionEntries.Count;

    public int DataGridItemCount => DataGridItems.Count;

    public ToolkitDataGridItem SelectedDataGridItem
    {
        get => _selectedDataGridItem;
        set
        {
            if (!ReferenceEquals(_selectedDataGridItem, value))
            {
                _selectedDataGridItem = value;
                OnPropertyChanged();
            }
        }
    }

    public ToolkitDockItem AddSourceDocument()
    {
        int index = SourceDocuments.Count + 1;
        var document = new ToolkitDockItem(
            $"Source Generated {index}",
            $"source-generated-{index}",
            $"Generated source-backed AvalonDock document {index}.",
            canClose: true);
        SourceDocuments.Add(document);
        SourceActiveContent = document;
        return document;
    }

    public ToolkitDockItem AddSourceAnchorable()
    {
        int index = SourceAnchorables.Count + 1;
        var anchorable = new ToolkitDockItem(
            $"Source Tool {index}",
            $"source-tool-{index}",
            $"Generated source-backed AvalonDock anchorable {index}.",
            canClose: false);
        SourceAnchorables.Add(anchorable);
        SourceActiveContent = anchorable;
        return anchorable;
    }

    public int Priority
    {
        get => _priority;
        set
        {
            if (_priority != value)
            {
                _priority = value;
                OnPropertyChanged();
            }
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText != value)
            {
                _filterText = value;
                OnPropertyChanged();
            }
        }
    }

    public string QuickSearchText
    {
        get => _quickSearchText;
        set
        {
            if (!string.Equals(_quickSearchText, value, StringComparison.Ordinal))
            {
                _quickSearchText = value;
                OnPropertyChanged();
            }
        }
    }

    public string AccessCode
    {
        get => _accessCode;
        set
        {
            if (!string.Equals(_accessCode, value, StringComparison.Ordinal))
            {
                _accessCode = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedOwner
    {
        get => _selectedOwner;
        set
        {
            if (!string.Equals(_selectedOwner, value, StringComparison.Ordinal))
            {
                _selectedOwner = value;
                OnPropertyChanged();
            }
        }
    }

    public double PriorityRangeStart
    {
        get => _priorityRangeStart;
        set
        {
            if (!Equals(_priorityRangeStart, value))
            {
                _priorityRangeStart = value;
                OnPropertyChanged();
            }
        }
    }

    public double PriorityRangeEnd
    {
        get => _priorityRangeEnd;
        set
        {
            if (!Equals(_priorityRangeEnd, value))
            {
                _priorityRangeEnd = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set
        {
            if (_dueDate != value)
            {
                _dueDate = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? ReminderTime
    {
        get => _reminderTime;
        set
        {
            if (_reminderTime != value)
            {
                _reminderTime = value;
                OnPropertyChanged();
            }
        }
    }

    public string ReferenceCode
    {
        get => _referenceCode;
        set
        {
            if (!string.Equals(_referenceCode, value, StringComparison.Ordinal))
            {
                _referenceCode = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? ReviewedAt
    {
        get => _reviewedAt;
        set
        {
            if (_reviewedAt != value)
            {
                _reviewedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public TimeSpan? Effort
    {
        get => _effort;
        set
        {
            if (_effort != value)
            {
                _effort = value;
                OnPropertyChanged();
            }
        }
    }

    public Color? AccentColor
    {
        get => _accentColor;
        set
        {
            if (_accentColor != value)
            {
                _accentColor = value;
                OnPropertyChanged();
            }
        }
    }

    public decimal? Estimate
    {
        get => _estimate;
        set
        {
            if (_estimate != value)
            {
                _estimate = value;
                OnPropertyChanged();
            }
        }
    }

    public byte? ByteScore
    {
        get => _byteScore;
        set
        {
            if (_byteScore != value)
            {
                _byteScore = value;
                OnPropertyChanged();
            }
        }
    }

    public double? DoubleScale
    {
        get => _doubleScale;
        set
        {
            if (_doubleScale != value)
            {
                _doubleScale = value;
                OnPropertyChanged();
            }
        }
    }

    public long? WorkItemId
    {
        get => _workItemId;
        set
        {
            if (_workItemId != value)
            {
                _workItemId = value;
                OnPropertyChanged();
            }
        }
    }

    public decimal? Budget
    {
        get => _budget;
        set
        {
            if (_budget != value)
            {
                _budget = value;
                OnPropertyChanged();
            }
        }
    }

    public string RichNotes
    {
        get => _richNotes;
        set
        {
            if (!string.Equals(_richNotes, value, StringComparison.Ordinal))
            {
                _richNotes = value;
                OnPropertyChanged();
            }
        }
    }

    public string MultiLineNotes
    {
        get => _multiLineNotes;
        set
        {
            if (!string.Equals(_multiLineNotes, value, StringComparison.Ordinal))
            {
                _multiLineNotes = value;
                OnPropertyChanged();
            }
        }
    }

    public int SpinnerCount
    {
        get => _spinnerCount;
        set
        {
            if (_spinnerCount != value)
            {
                _spinnerCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int ToolkitResourceThemeUpdateCount
    {
        get => _toolkitResourceThemeUpdateCount;
        set
        {
            if (_toolkitResourceThemeUpdateCount != value)
            {
                _toolkitResourceThemeUpdateCount = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
            }
        }
    }

    public int ChildWindowShowCount
    {
        get => _childWindowShowCount;
        set
        {
            if (_childWindowShowCount != value)
            {
                _childWindowShowCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int ChildWindowClosingCount
    {
        get => _childWindowClosingCount;
        set
        {
            if (_childWindowClosingCount != value)
            {
                _childWindowClosingCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int ChildWindowClosedCount
    {
        get => _childWindowClosedCount;
        set
        {
            if (_childWindowClosedCount != value)
            {
                _childWindowClosedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public bool? LastChildWindowDialogResult
    {
        get => _lastChildWindowDialogResult;
        set
        {
            if (_lastChildWindowDialogResult != value)
            {
                _lastChildWindowDialogResult = value;
                OnPropertyChanged();
            }
        }
    }

    public string ChildWindowStatus
    {
        get => _childWindowStatus;
        set
        {
            if (!string.Equals(_childWindowStatus, value, StringComparison.Ordinal))
            {
                _childWindowStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public int ToolkitMessageBoxShowCount
    {
        get => _toolkitMessageBoxShowCount;
        set
        {
            if (_toolkitMessageBoxShowCount != value)
            {
                _toolkitMessageBoxShowCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int ToolkitMessageBoxClosedCount
    {
        get => _toolkitMessageBoxClosedCount;
        set
        {
            if (_toolkitMessageBoxClosedCount != value)
            {
                _toolkitMessageBoxClosedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public MessageBoxResult LastToolkitMessageBoxResult
    {
        get => _lastToolkitMessageBoxResult;
        set
        {
            if (_lastToolkitMessageBoxResult != value)
            {
                _lastToolkitMessageBoxResult = value;
                OnPropertyChanged();
            }
        }
    }

    public string ToolkitMessageBoxStatus
    {
        get => _toolkitMessageBoxStatus;
        set
        {
            if (!string.Equals(_toolkitMessageBoxStatus, value, StringComparison.Ordinal))
            {
                _toolkitMessageBoxStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public int StaticToolkitMessageBoxShowCount
    {
        get => _staticToolkitMessageBoxShowCount;
        set
        {
            if (_staticToolkitMessageBoxShowCount != value)
            {
                _staticToolkitMessageBoxShowCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int StaticToolkitMessageBoxClosedCount
    {
        get => _staticToolkitMessageBoxClosedCount;
        set
        {
            if (_staticToolkitMessageBoxClosedCount != value)
            {
                _staticToolkitMessageBoxClosedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public MessageBoxResult LastStaticToolkitMessageBoxResult
    {
        get => _lastStaticToolkitMessageBoxResult;
        set
        {
            if (_lastStaticToolkitMessageBoxResult != value)
            {
                _lastStaticToolkitMessageBoxResult = value;
                OnPropertyChanged();
            }
        }
    }

    public string StaticToolkitMessageBoxStatus
    {
        get => _staticToolkitMessageBoxStatus;
        set
        {
            if (!string.Equals(_staticToolkitMessageBoxStatus, value, StringComparison.Ordinal))
            {
                _staticToolkitMessageBoxStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public IntPtr LastStaticToolkitMessageBoxOwnerHandle
    {
        get => _lastStaticToolkitMessageBoxOwnerHandle;
        set
        {
            if (_lastStaticToolkitMessageBoxOwnerHandle != value)
            {
                _lastStaticToolkitMessageBoxOwnerHandle = value;
                OnPropertyChanged();
            }
        }
    }

    public string WindowContainerStatus
    {
        get => _windowContainerStatus;
        set
        {
            if (!string.Equals(_windowContainerStatus, value, StringComparison.Ordinal))
            {
                _windowContainerStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public Visibility ToolkitWindowControlVisibility
    {
        get => _toolkitWindowControlVisibility;
        set
        {
            if (_toolkitWindowControlVisibility != value)
            {
                _toolkitWindowControlVisibility = value;
                OnPropertyChanged();
            }
        }
    }

    public string WindowControlStatus
    {
        get => _windowControlStatus;
        set
        {
            if (!string.Equals(_windowControlStatus, value, StringComparison.Ordinal))
            {
                _windowControlStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public string WindowControlText
    {
        get => _windowControlText;
        set
        {
            if (!string.Equals(_windowControlText, value, StringComparison.Ordinal))
            {
                _windowControlText = value;
                OnPropertyChanged();
            }
        }
    }

    public int WindowControlToggleCount
    {
        get => _windowControlToggleCount;
        set
        {
            if (_windowControlToggleCount != value)
            {
                _windowControlToggleCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int WindowControlActivatedCount
    {
        get => _windowControlActivatedCount;
        set
        {
            if (_windowControlActivatedCount != value)
            {
                _windowControlActivatedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int WindowControlHeaderClickCount
    {
        get => _windowControlHeaderClickCount;
        set
        {
            if (_windowControlHeaderClickCount != value)
            {
                _windowControlHeaderClickCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int WindowControlHeaderIconClickCount
    {
        get => _windowControlHeaderIconClickCount;
        set
        {
            if (_windowControlHeaderIconClickCount != value)
            {
                _windowControlHeaderIconClickCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int WindowControlHeaderIconDoubleClickCount
    {
        get => _windowControlHeaderIconDoubleClickCount;
        set
        {
            if (_windowControlHeaderIconDoubleClickCount != value)
            {
                _windowControlHeaderIconDoubleClickCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int WindowControlHeaderDoubleClickCount
    {
        get => _windowControlHeaderDoubleClickCount;
        set
        {
            if (_windowControlHeaderDoubleClickCount != value)
            {
                _windowControlHeaderDoubleClickCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int WindowControlHeaderRightClickCount
    {
        get => _windowControlHeaderRightClickCount;
        set
        {
            if (_windowControlHeaderRightClickCount != value)
            {
                _windowControlHeaderRightClickCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int WindowControlHeaderDragCount
    {
        get => _windowControlHeaderDragCount;
        set
        {
            if (_windowControlHeaderDragCount != value)
            {
                _windowControlHeaderDragCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int WindowControlCloseButtonClickCount
    {
        get => _windowControlCloseButtonClickCount;
        set
        {
            if (_windowControlCloseButtonClickCount != value)
            {
                _windowControlCloseButtonClickCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int ZoomboxCommandCount
    {
        get => _zoomboxCommandCount;
        set
        {
            if (_zoomboxCommandCount != value)
            {
                _zoomboxCommandCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int ZoomboxViewChangedCount
    {
        get => _zoomboxViewChangedCount;
        set
        {
            if (_zoomboxViewChangedCount != value)
            {
                _zoomboxViewChangedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int ZoomboxViewStackIndexChangedCount
    {
        get => _zoomboxViewStackIndexChangedCount;
        set
        {
            if (_zoomboxViewStackIndexChangedCount != value)
            {
                _zoomboxViewStackIndexChangedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int LastZoomboxViewStackIndex
    {
        get => _lastZoomboxViewStackIndex;
        set
        {
            if (_lastZoomboxViewStackIndex != value)
            {
                _lastZoomboxViewStackIndex = value;
                OnPropertyChanged();
            }
        }
    }

    public double LastZoomboxScale
    {
        get => _lastZoomboxScale;
        set
        {
            if (!Equals(_lastZoomboxScale, value))
            {
                _lastZoomboxScale = value;
                OnPropertyChanged();
            }
        }
    }

    public string ZoomboxStatus
    {
        get => _zoomboxStatus;
        set
        {
            if (!string.Equals(_zoomboxStatus, value, StringComparison.Ordinal))
            {
                _zoomboxStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public ToolkitCollectionEntry SelectedCollectionEntry
    {
        get => _selectedCollectionEntry;
        set
        {
            if (!ReferenceEquals(_selectedCollectionEntry, value))
            {
                _selectedCollectionEntry = value;
                OnPropertyChanged();
            }
        }
    }

    public string CollectionControlStatus
    {
        get => _collectionControlStatus;
        set
        {
            if (!string.Equals(_collectionControlStatus, value, StringComparison.Ordinal))
            {
                _collectionControlStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public int CollectionDialogUpdateCount
    {
        get => _collectionDialogUpdateCount;
        set
        {
            if (_collectionDialogUpdateCount != value)
            {
                _collectionDialogUpdateCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int WizardPageChanges
    {
        get => _wizardPageChanges;
        set
        {
            if (_wizardPageChanges != value)
            {
                _wizardPageChanges = value;
                OnPropertyChanged();
            }
        }
    }

    public int WizardFinishes
    {
        get => _wizardFinishes;
        set
        {
            if (_wizardFinishes != value)
            {
                _wizardFinishes = value;
                OnPropertyChanged();
            }
        }
    }

    public int WizardCancels
    {
        get => _wizardCancels;
        set
        {
            if (_wizardCancels != value)
            {
                _wizardCancels = value;
                OnPropertyChanged();
            }
        }
    }

    public string WizardStatus
    {
        get => _wizardStatus;
        set
        {
            if (!string.Equals(_wizardStatus, value, StringComparison.Ordinal))
            {
                _wizardStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockActiveContentChangedCount
    {
        get => _avalonDockActiveContentChangedCount;
        set
        {
            if (_avalonDockActiveContentChangedCount != value)
            {
                _avalonDockActiveContentChangedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastActiveContentTitle
    {
        get => _lastActiveContentTitle;
        set
        {
            if (!string.Equals(_lastActiveContentTitle, value, StringComparison.Ordinal))
            {
                _lastActiveContentTitle = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockDocumentClosingCount
    {
        get => _avalonDockDocumentClosingCount;
        set
        {
            if (_avalonDockDocumentClosingCount != value)
            {
                _avalonDockDocumentClosingCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockDocumentClosedCount
    {
        get => _avalonDockDocumentClosedCount;
        set
        {
            if (_avalonDockDocumentClosedCount != value)
            {
                _avalonDockDocumentClosedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockDocumentCloseCanceledCount
    {
        get => _avalonDockDocumentCloseCanceledCount;
        set
        {
            if (_avalonDockDocumentCloseCanceledCount != value)
            {
                _avalonDockDocumentCloseCanceledCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int OverviewDocumentClosedCount
    {
        get => _overviewDocumentClosedCount;
        set
        {
            if (_overviewDocumentClosedCount != value)
            {
                _overviewDocumentClosedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockFloatedCount
    {
        get => _avalonDockFloatedCount;
        set
        {
            if (_avalonDockFloatedCount != value)
            {
                _avalonDockFloatedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockDockedCount
    {
        get => _avalonDockDockedCount;
        set
        {
            if (_avalonDockDockedCount != value)
            {
                _avalonDockDockedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockLayoutChangingCount
    {
        get => _avalonDockLayoutChangingCount;
        set
        {
            if (_avalonDockLayoutChangingCount != value)
            {
                _avalonDockLayoutChangingCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockLayoutChangedCount
    {
        get => _avalonDockLayoutChangedCount;
        set
        {
            if (_avalonDockLayoutChangedCount != value)
            {
                _avalonDockLayoutChangedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockAnchorableHidingCount
    {
        get => _avalonDockAnchorableHidingCount;
        set
        {
            if (_avalonDockAnchorableHidingCount != value)
            {
                _avalonDockAnchorableHidingCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockAnchorableIsVisibleChangedCount
    {
        get => _avalonDockAnchorableIsVisibleChangedCount;
        set
        {
            if (_avalonDockAnchorableIsVisibleChangedCount != value)
            {
                _avalonDockAnchorableIsVisibleChangedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastAvalonDockAnchorableLifecycleTarget
    {
        get => _lastAvalonDockAnchorableLifecycleTarget;
        set
        {
            if (!string.Equals(_lastAvalonDockAnchorableLifecycleTarget, value, StringComparison.Ordinal))
            {
                _lastAvalonDockAnchorableLifecycleTarget = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockAnchorableClosingCount
    {
        get => _avalonDockAnchorableClosingCount;
        set
        {
            if (_avalonDockAnchorableClosingCount != value)
            {
                _avalonDockAnchorableClosingCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockAnchorableClosedCount
    {
        get => _avalonDockAnchorableClosedCount;
        set
        {
            if (_avalonDockAnchorableClosedCount != value)
            {
                _avalonDockAnchorableClosedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockContextMenuCommandCanExecuteCount
    {
        get => _avalonDockContextMenuCommandCanExecuteCount;
        set
        {
            if (_avalonDockContextMenuCommandCanExecuteCount != value)
            {
                _avalonDockContextMenuCommandCanExecuteCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockContextMenuCommandExecutedCount
    {
        get => _avalonDockContextMenuCommandExecutedCount;
        set
        {
            if (_avalonDockContextMenuCommandExecutedCount != value)
            {
                _avalonDockContextMenuCommandExecutedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastAvalonDockContextMenuCommand
    {
        get => _lastAvalonDockContextMenuCommand;
        set
        {
            if (!string.Equals(_lastAvalonDockContextMenuCommand, value, StringComparison.Ordinal))
            {
                _lastAvalonDockContextMenuCommand = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockAnchorableContextMenuCommandCanExecuteCount
    {
        get => _avalonDockAnchorableContextMenuCommandCanExecuteCount;
        set
        {
            if (_avalonDockAnchorableContextMenuCommandCanExecuteCount != value)
            {
                _avalonDockAnchorableContextMenuCommandCanExecuteCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockAnchorableContextMenuCommandExecutedCount
    {
        get => _avalonDockAnchorableContextMenuCommandExecutedCount;
        set
        {
            if (_avalonDockAnchorableContextMenuCommandExecutedCount != value)
            {
                _avalonDockAnchorableContextMenuCommandExecutedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastAvalonDockAnchorableContextMenuCommand
    {
        get => _lastAvalonDockAnchorableContextMenuCommand;
        set
        {
            if (!string.Equals(_lastAvalonDockAnchorableContextMenuCommand, value, StringComparison.Ordinal))
            {
                _lastAvalonDockAnchorableContextMenuCommand = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockKeyboardNavigationCanExecuteCount
    {
        get => _avalonDockKeyboardNavigationCanExecuteCount;
        set
        {
            if (_avalonDockKeyboardNavigationCanExecuteCount != value)
            {
                _avalonDockKeyboardNavigationCanExecuteCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockKeyboardNavigationCount
    {
        get => _avalonDockKeyboardNavigationCount;
        set
        {
            if (_avalonDockKeyboardNavigationCount != value)
            {
                _avalonDockKeyboardNavigationCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastAvalonDockKeyboardNavigationTarget
    {
        get => _lastAvalonDockKeyboardNavigationTarget;
        set
        {
            if (!string.Equals(_lastAvalonDockKeyboardNavigationTarget, value, StringComparison.Ordinal))
            {
                _lastAvalonDockKeyboardNavigationTarget = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockAnchorableKeyboardNavigationCanExecuteCount
    {
        get => _avalonDockAnchorableKeyboardNavigationCanExecuteCount;
        set
        {
            if (_avalonDockAnchorableKeyboardNavigationCanExecuteCount != value)
            {
                _avalonDockAnchorableKeyboardNavigationCanExecuteCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockAnchorableKeyboardNavigationCount
    {
        get => _avalonDockAnchorableKeyboardNavigationCount;
        set
        {
            if (_avalonDockAnchorableKeyboardNavigationCount != value)
            {
                _avalonDockAnchorableKeyboardNavigationCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastAvalonDockAnchorableKeyboardNavigationTarget
    {
        get => _lastAvalonDockAnchorableKeyboardNavigationTarget;
        set
        {
            if (!string.Equals(_lastAvalonDockAnchorableKeyboardNavigationTarget, value, StringComparison.Ordinal))
            {
                _lastAvalonDockAnchorableKeyboardNavigationTarget = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockAutoHideOverlayCanExecuteCount
    {
        get => _avalonDockAutoHideOverlayCanExecuteCount;
        set
        {
            if (_avalonDockAutoHideOverlayCanExecuteCount != value)
            {
                _avalonDockAutoHideOverlayCanExecuteCount = value;
                OnPropertyChanged();
            }
        }
    }

    public int AvalonDockAutoHideOverlayCount
    {
        get => _avalonDockAutoHideOverlayCount;
        set
        {
            if (_avalonDockAutoHideOverlayCount != value)
            {
                _avalonDockAutoHideOverlayCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastAvalonDockAutoHideOverlayTarget
    {
        get => _lastAvalonDockAutoHideOverlayTarget;
        set
        {
            if (!string.Equals(_lastAvalonDockAutoHideOverlayTarget, value, StringComparison.Ordinal))
            {
                _lastAvalonDockAutoHideOverlayTarget = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CancelNextOverviewClose
    {
        get => _cancelNextOverviewClose;
        set
        {
            if (_cancelNextOverviewClose != value)
            {
                _cancelNextOverviewClose = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastClosingDocumentContentId
    {
        get => _lastClosingDocumentContentId;
        set
        {
            if (!string.Equals(_lastClosingDocumentContentId, value, StringComparison.Ordinal))
            {
                _lastClosingDocumentContentId = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastClosedDocumentContentId
    {
        get => _lastClosedDocumentContentId;
        set
        {
            if (!string.Equals(_lastClosedDocumentContentId, value, StringComparison.Ordinal))
            {
                _lastClosedDocumentContentId = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastClosingAnchorableContentId
    {
        get => _lastClosingAnchorableContentId;
        set
        {
            if (!string.Equals(_lastClosingAnchorableContentId, value, StringComparison.Ordinal))
            {
                _lastClosingAnchorableContentId = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastClosedAnchorableContentId
    {
        get => _lastClosedAnchorableContentId;
        set
        {
            if (!string.Equals(_lastClosedAnchorableContentId, value, StringComparison.Ordinal))
            {
                _lastClosedAnchorableContentId = value;
                OnPropertyChanged();
            }
        }
    }

    public object? SourceActiveContent
    {
        get => _sourceActiveContent;
        set
        {
            if (!ReferenceEquals(_sourceActiveContent, value))
            {
                _sourceActiveContent = value;
                OnPropertyChanged();
            }
        }
    }

    public int SourceActiveContentChangedCount
    {
        get => _sourceActiveContentChangedCount;
        set
        {
            if (_sourceActiveContentChangedCount != value)
            {
                _sourceActiveContentChangedCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastSourceActiveTitle
    {
        get => _lastSourceActiveTitle;
        set
        {
            if (!string.Equals(_lastSourceActiveTitle, value, StringComparison.Ordinal))
            {
                _lastSourceActiveTitle = value;
                OnPropertyChanged();
            }
        }
    }

    public int SourceTabGroupCommandCount
    {
        get => _sourceTabGroupCommandCount;
        set
        {
            if (_sourceTabGroupCommandCount != value)
            {
                _sourceTabGroupCommandCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string ActiveDockThemeName
    {
        get => _activeDockThemeName;
        set
        {
            if (!string.Equals(_activeDockThemeName, value, StringComparison.Ordinal))
            {
                _activeDockThemeName = value;
                OnPropertyChanged();
            }
        }
    }

    public int DockThemeSwitchCount
    {
        get => _dockThemeSwitchCount;
        set
        {
            if (_dockThemeSwitchCount != value)
            {
                _dockThemeSwitchCount = value;
                OnPropertyChanged();
            }
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (!string.Equals(_status, value, StringComparison.Ordinal))
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastSerializedLayout
    {
        get => _lastSerializedLayout;
        set
        {
            if (!string.Equals(_lastSerializedLayout, value, StringComparison.Ordinal))
            {
                _lastSerializedLayout = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static IReadOnlyList<ToolkitDataGridItem> CreateDataGridItems(int count)
    {
        var items = new List<ToolkitDataGridItem>(count);
        string[] owners = ["WPF", "ProGPU", "Xceed", "SDK"];
        string[] categories = ["Framework", "Rendering", "Toolkit", "AvalonDock"];
        DateTime baseline = DateTime.Today;

        for (int i = 0; i < count; i++)
        {
            items.Add(new ToolkitDataGridItem(
                i + 1,
                $"Row {i + 1:000000}",
                owners[i % owners.Length],
                categories[(i / owners.Length) % categories.Length],
                (i * 17) % 100,
                baseline.AddDays(-(i % 365))));
        }

        return items;
    }
}

internal sealed class ToolkitDockItem : INotifyPropertyChanged
{
    private string _title;
    private string _body;
    private bool _canClose;

    public ToolkitDockItem(string title, string contentId, string body, bool canClose)
    {
        _title = title;
        ContentId = contentId;
        _body = body;
        _canClose = canClose;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        set
        {
            if (!string.Equals(_title, value, StringComparison.Ordinal))
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    public string ContentId { get; }

    public bool CanClose
    {
        get => _canClose;
        set
        {
            if (_canClose != value)
            {
                _canClose = value;
                OnPropertyChanged();
            }
        }
    }

    public string Body
    {
        get => _body;
        set
        {
            if (!string.Equals(_body, value, StringComparison.Ordinal))
            {
                _body = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ToolkitDataGridItem
{
    public ToolkitDataGridItem(int id, string title, string owner, string category, int score, DateTime updated)
    {
        Id = id;
        Title = title;
        Owner = owner;
        Category = category;
        Score = score;
        Updated = updated;
    }

    public int Id { get; }

    public string Title { get; }

    public string Owner { get; }

    public string Category { get; }

    public int Score { get; }

    public DateTime Updated { get; }
}

public sealed class ToolkitCollectionEntry : INotifyPropertyChanged
{
    private string _name;
    private string _category;
    private int _weight;

    public ToolkitCollectionEntry()
        : this("Entry", "Generated", 0)
    {
    }

    public ToolkitCollectionEntry(string name, string category, int weight)
    {
        _name = name;
        _category = category;
        _weight = weight;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (!string.Equals(_name, value, StringComparison.Ordinal))
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public string Category
    {
        get => _category;
        set
        {
            if (!string.Equals(_category, value, StringComparison.Ordinal))
            {
                _category = value;
                OnPropertyChanged();
            }
        }
    }

    public int Weight
    {
        get => _weight;
        set
        {
            if (_weight != value)
            {
                _weight = value;
                OnPropertyChanged();
            }
        }
    }

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "{0} ({1})", Name, Category);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class ToolkitCollectionDialogScope : IDisposable
{
    public ToolkitCollectionDialogScope(CollectionControlDialog dialog)
    {
        Dialog = dialog;
    }

    public CollectionControlDialog Dialog { get; }

    public void Dispose()
    {
        if (Dialog.IsVisible)
        {
            Dialog.Close();
        }
    }
}

internal sealed class ToolkitLayoutUpdateStrategy : ILayoutUpdateStrategy
{
    public int BeforeInsertDocumentCount { get; private set; }

    public int AfterInsertDocumentCount { get; private set; }

    public int BeforeInsertAnchorableCount { get; private set; }

    public int AfterInsertAnchorableCount { get; private set; }

    public string LastInsertedDocumentContentId { get; private set; } = string.Empty;

    public string LastInsertedAnchorableContentId { get; private set; } = string.Empty;

    public bool BeforeInsertDocument(
        LayoutRoot layout,
        LayoutDocument anchorableToShow,
        ILayoutContainer destinationContainer)
    {
        BeforeInsertDocumentCount++;
        LastInsertedDocumentContentId = ResolveContentId(anchorableToShow);

        var documentPane = FindFirst<LayoutDocumentPane>(layout.RootPanel) ??
            destinationContainer as LayoutDocumentPane;
        if (documentPane == null ||
            documentPane.Children.Contains(anchorableToShow))
        {
            return false;
        }

        documentPane.Children.Add(anchorableToShow);
        return true;
    }

    public void AfterInsertDocument(LayoutRoot layout, LayoutDocument anchorableShown)
    {
        AfterInsertDocumentCount++;
        LastInsertedDocumentContentId = ResolveContentId(anchorableShown, LastInsertedDocumentContentId);
    }

    public bool BeforeInsertAnchorable(
        LayoutRoot layout,
        LayoutAnchorable anchorableToShow,
        ILayoutContainer destinationContainer)
    {
        BeforeInsertAnchorableCount++;
        LastInsertedAnchorableContentId = ResolveContentId(anchorableToShow);

        var anchorablePane = FindFirst<LayoutAnchorablePane>(layout.RootPanel) ??
            destinationContainer as LayoutAnchorablePane;
        if (anchorablePane == null ||
            anchorablePane.Children.Contains(anchorableToShow))
        {
            return false;
        }

        anchorablePane.Children.Add(anchorableToShow);
        return true;
    }

    public void AfterInsertAnchorable(LayoutRoot layout, LayoutAnchorable anchorableShown)
    {
        AfterInsertAnchorableCount++;
        LastInsertedAnchorableContentId = ResolveContentId(anchorableShown, LastInsertedAnchorableContentId);
    }

    private static string ResolveContentId(LayoutContent content, string fallback = "")
    {
        return content.ContentId ??
            (content.Content as ToolkitDockItem)?.ContentId ??
            fallback;
    }

    private static T? FindFirst<T>(ILayoutContainer? container)
        where T : class
    {
        if (container == null)
        {
            return null;
        }

        if (container is T match)
        {
            return match;
        }

        foreach (ILayoutElement child in container.Children)
        {
            if (child is ILayoutContainer childContainer)
            {
                var childMatch = FindFirst<T>(childContainer);
                if (childMatch != null)
                {
                    return childMatch;
                }
            }
        }

        return null;
    }
}

public sealed class ToolkitDockTitleTemplateSelector : DataTemplateSelector
{
    public DataTemplate? DocumentTemplate { get; set; }

    public DataTemplate? AnchorableTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            LayoutDocument => DocumentTemplate,
            LayoutAnchorable => AnchorableTemplate,
            AvalonDockLayoutItem { LayoutElement: LayoutDocument } => DocumentTemplate,
            AvalonDockLayoutItem { LayoutElement: LayoutAnchorable } => AnchorableTemplate,
            ToolkitDockItem dockItem when IsAnchorable(dockItem) => AnchorableTemplate,
            ToolkitDockItem => DocumentTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }

    private static bool IsAnchorable(ToolkitDockItem dockItem)
    {
        return dockItem.ContentId.Contains("tool", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ToolkitDocument : INotifyPropertyChanged
{
    private string _body;

    public ToolkitDocument(string title, string owner, DateTime modified, string body)
    {
        Title = title;
        Owner = owner;
        Modified = modified;
        _body = body;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string Owner { get; }

    public DateTime Modified { get; }

    public string Body
    {
        get => _body;
        set
        {
            if (!string.Equals(_body, value, StringComparison.Ordinal))
            {
                _body = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Body)));
            }
        }
    }
}

internal static class ToolkitSelfTest
{
    public static void Validate(MainWindow window, bool expectLoaded = false)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Dispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));

        Require<DockingManager>(window, "DockManager");
        Require<IntegerUpDown>(window, "PriorityEditor");
        Require<WatermarkTextBox>(window, "FilterTextBox");
        Require<AutoSelectTextBox>(window, "QuickSearchTextBox");
        Require<WatermarkPasswordBox>(window, "AccessCodeBox");
        Require<WatermarkComboBox>(window, "OwnerComboBox");
        Require<RangeSlider>(window, "PriorityRangeSlider");
        Require<DateTimePicker>(window, "DueDatePicker");
        Require<TimePicker>(window, "ReminderTimePicker");
        Require<DateTimeUpDown>(window, "ReviewedAtEditor");
        Require<TimeSpanUpDown>(window, "EffortEditor");
        Require<MaskedTextBox>(window, "ReferenceMaskTextBox");
        Require<CheckComboBox>(window, "CategoryPicker");
        Require<CheckListBox>(window, "FlagListBox");
        Require<ColorPicker>(window, "AccentColorPicker");
        Require<CalculatorUpDown>(window, "EstimateEditor");
        Require<ByteUpDown>(window, "ByteScoreEditor");
        Require<DoubleUpDown>(window, "DoubleScaleEditor");
        Require<LongUpDown>(window, "WorkItemIdEditor");
        Require<DecimalUpDown>(window, "BudgetEditor");
        Require<ColorCanvas>(window, "AccentColorCanvas");
        Require<DropDownButton>(window, "ActionDropDownButton");
        Require<Button>(window, "MarkReviewedButton");
        Require<SplitButton>(window, "SplitActionButton");
        Require<ListBox>(window, "OwnerPickerList");
        Require<Button>(window, "AssignSdkOwnerButton");
        Require<Wizard>(window, "ToolkitWizard");
        Require<WizardPage>(window, "WizardScopePage");
        Require<WizardPage>(window, "WizardReviewPage");
        Require<ToolkitRichTextBox>(window, "ToolkitRichTextBox");
        Require<MultiLineTextEditor>(window, "MultiLineNotesEditor");
        Require<ButtonSpinner>(window, "DocumentCountSpinner");
        Require<BusyIndicator>(window, "BusyIndicator");
        Require<WindowContainer>(window, "ToolkitChildWindowContainer");
        Require<ChildWindow>(window, "ToolkitChildWindow");
        Require<Button>(window, "ShowChildWindowButton");
        Require<Button>(window, "ShowToolkitMessageBoxButton");
        Require<Button>(window, "ShowStaticToolkitMessageBoxButton");
        Require<Button>(window, "ToggleWindowControlButton");
        Require<TextBox>(window, "ChildWindowInputTextBox");
        Require<Button>(window, "AcceptChildWindowButton");
        Require<ToolkitMessageBoxControl>(window, "ToolkitMessageBox");
        Require<WindowContainer>(window, "ToolkitPrimitiveWindowContainer");
        Require<WindowControl>(window, "ToolkitWindowControl");
        Require<TextBox>(window, "WindowControlInputTextBox");
        Require<Button>(window, "ActivateWindowControlButton");
        Require<TextBlock>(window, "WindowControlStatusText");
        Require<TextBlock>(window, "StaticToolkitMessageBoxStatusText");
        Require<ToolkitZoomboxControl>(window, "ToolkitZoombox");
        Require<Grid>(window, "ZoomboxContentRoot");
        Require<Button>(window, "ZoomInButton");
        Require<Button>(window, "FitZoomboxButton");
        Require<Button>(window, "BackZoomboxButton");
        Require<TextBlock>(window, "ZoomboxStatusText");
        if (window.FindResource("ToolkitMagnifierResource") is not Magnifier)
        {
            throw new InvalidOperationException("Expected Toolkit Magnifier resource to be available for manager attachment.");
        }
        Require<ScrollViewer>(window, "ToolkitPaneScrollViewer");
        Require<StackPanel>(window, "ToolkitPaneContentPanel");
        Require<ToolkitWrapPanel>(window, "ToolkitWrapPanel");
        Require<CollectionControl>(window, "ToolkitCollectionControl");
        Require<Button>(window, "AddCollectionEntryButton");
        Require<Button>(window, "SelectCollectionEntryButton");
        Require<CollectionControlButton>(window, "OpenCollectionDialogButton");
        Require<TextBlock>(window, "CollectionControlStatusText");
        Require<PropertyGrid>(window, "DocumentPropertyGrid");
        Require<LayoutDocument>(window, "DataGridDocument");
        Require<DataGrid>(window, "ToolkitDataGrid");
        Require<Button>(window, "AddSourceDocumentButton");
        Require<Button>(window, "ActivateSourceToolButton");
        Require<Button>(window, "ExerciseSourceTabGroupsButton");
        Require<DockingManager>(window, "SourceDockManager");
        Require<LayoutDocumentPane>(window, "SourceDocumentPane");
        Require<LayoutAnchorablePane>(window, "SourceAnchorablePane");
        Require<LayoutAnchorablePane>(window, "RightAnchorablePane");
        Require<ContextMenu>(window, "DockDocumentContextMenu");
        Require<MenuItem>(window, "DockContextActivateEditorMenuItem");
        Require<MenuItem>(window, "DockContextCloseOverviewMenuItem");
        Require<MenuItem>(window, "DockContextCancelNextCloseMenuItem");
        Require<ContextMenu>(window, "DockAnchorableContextMenu");
        Require<MenuItem>(window, "DockAnchorContextActivateToolkitMenuItem");
        Require<MenuItem>(window, "DockAnchorContextTogglePropertyMenuItem");
        Require<Button>(window, "ActivateEditorButton");
        Require<Button>(window, "CloseOverviewDocumentButton");
        Require<Button>(window, "ReopenOverviewDocumentButton");
        Require<Button>(window, "ToggleEditorFloatButton");
        Require<Button>(window, "TogglePropertyPaneButton");
        Require<Button>(window, "CloseActivityPaneButton");
        Require<Button>(window, "ReopenActivityPaneButton");
        Require<Button>(window, "ToggleActivityAutoHideButton");
        Require<Button>(window, "ToggleAgendaAutoHideButton");
        Require<Button>(window, "CycleDockThemeButton");
        Require<Button>(window, "SerializeLayoutButton");

        window.ValidateAvalonDockThemeState("Aero");
        window.ValidateAvalonDockManagerOptionState();

        if (window.DockManager.DocumentHeaderTemplate is null)
        {
            throw new InvalidOperationException("Expected AvalonDock document header template to be loaded from sample XAML.");
        }

        if (window.DockLayoutRoot.RootPanel is null || window.DockLayoutRoot.RootPanel.ChildrenCount != 3)
        {
            throw new InvalidOperationException("Expected AvalonDock root panel with toolkit, document, and property panes.");
        }

        if (!window.AgendaPane.IsAutoHidden ||
            !window.ContactsPane.IsAutoHidden ||
            window.DockLayoutRoot.LeftSide.ChildrenCount != 1)
        {
            throw new InvalidOperationException("Expected startup AvalonDock side anchorables to be auto-hidden on the left side.");
        }

        if (window.DocumentPane.ChildrenCount != 3)
        {
            throw new InvalidOperationException($"Expected three startup AvalonDock documents, got {window.DocumentPane.ChildrenCount}.");
        }

        if (window.OverviewDocument.IconSource is null ||
            window.EditorDocument.IconSource is null ||
            window.DataGridDocument.IconSource is null ||
            window.ToolkitPane.IconSource is null)
        {
            throw new InvalidOperationException("Expected AvalonDock icon resources to bind into documents and anchorables.");
        }

        if (window.DocumentPropertyGrid.SelectedObject is ToolkitDocument selected)
        {
            if (!string.Equals(selected.Title, "Overview", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected toolkit PropertyGrid to bind to the selected document.");
            }
        }
        else if (expectLoaded)
        {
            throw new InvalidOperationException("Expected loaded toolkit PropertyGrid to bind to the selected document.");
        }
        else if (BindingOperations.GetBindingExpression(window.DocumentPropertyGrid, PropertyGrid.SelectedObjectProperty) is null)
        {
            throw new InvalidOperationException("Expected toolkit PropertyGrid SelectedObject binding expression.");
        }

        if (window.ViewModel.Documents.Count != 3 ||
            window.ViewModel.DataGridItems.Count != 100_000 ||
            window.ViewModel.Owners.Count != 4 ||
            window.ViewModel.Categories.Count != 4 ||
            window.ViewModel.SelectedCategories.Count != 2 ||
            window.ViewModel.Flags.Count != 4 ||
            window.ViewModel.SelectedFlags.Count != 1)
        {
            throw new InvalidOperationException("Expected toolkit sample view-model collections to be initialized.");
        }

        if (window.PriorityEditor.Value != window.ViewModel.Priority)
        {
            throw new InvalidOperationException("Expected IntegerUpDown value binding to initialize.");
        }

        if (window.AccentColorPicker.SelectedColor != window.ViewModel.AccentColor ||
            window.EstimateEditor.Value != window.ViewModel.Estimate)
        {
            throw new InvalidOperationException("Expected Toolkit popup editor bindings to initialize.");
        }

        window.ValidateToolkitInputEditorState();
        window.ValidateToolkitWizardState(expectLoaded);
        window.ValidateToolkitChildWindowState(expectedOpen: false);
        window.ValidateToolkitMessageBoxState(expectedOpen: false);
        window.ValidateStaticToolkitMessageBoxState(expectedValidated: false);
        window.ValidateToolkitWindowControlState(expectedVisible: true, expectLoaded);
        window.ValidateToolkitZoomboxAndMagnifierState(expectLoaded);
        window.ValidateToolkitScrollClipState(expectLoaded);
        window.ValidateToolkitPanelState(expectLoaded);
        window.ValidateToolkitCollectionControlState(expectLoaded);
        window.ValidateToolkitDataGridState(expectLoaded);
        window.ValidateToolkitCollectionDialogButtonState(expectLoaded);
        window.ValidateToolkitResourceThemeState(expectLoaded);
        window.ValidateSourceBackedAvalonDockState(mutateSources: true);
        window.ValidateToolkitAutomationState(expectLoaded);

        if (expectLoaded)
        {
            window.CategoryPicker.IsDropDownOpen = true;
            window.ReminderTimePicker.IsOpen = true;
            window.AccentColorPicker.IsOpen = true;
            window.EstimateEditor.IsOpen = true;
            window.ActionDropDownButton.IsOpen = true;
            PumpDispatcherUntil(
                window,
                () => window.CategoryPicker.IsDropDownOpen &&
                      window.ReminderTimePicker.IsOpen &&
                      window.AccentColorPicker.IsOpen &&
                      window.EstimateEditor.IsOpen &&
                      window.ActionDropDownButton.IsOpen,
                TimeSpan.FromSeconds(2),
                "Toolkit popup-backed controls open state");
            window.ValidateToolkitPopupState(expectedOpen: true);

            window.AccentColorPicker.SelectedColor = Colors.MediumSeaGreen;
            window.EstimateEditor.Value = 42.25m;
            window.CategoryPicker.IsDropDownOpen = false;
            window.ReminderTimePicker.IsOpen = false;
            window.AccentColorPicker.IsOpen = false;
            window.EstimateEditor.IsOpen = false;
            window.ActionDropDownButton.IsOpen = false;
            PumpDispatcherUntil(
                window,
                () => !window.CategoryPicker.IsDropDownOpen &&
                      !window.ReminderTimePicker.IsOpen &&
                      !window.AccentColorPicker.IsOpen &&
                      !window.EstimateEditor.IsOpen &&
                      !window.ActionDropDownButton.IsOpen,
                TimeSpan.FromSeconds(2),
                "Toolkit popup-backed controls closed state");
            window.ValidateToolkitPopupState(expectedOpen: false);

            window.SplitActionButton.IsOpen = true;
            PumpDispatcherUntil(
                window,
                () => window.SplitActionButton.IsOpen,
                TimeSpan.FromSeconds(2),
                "Toolkit SplitButton dropdown open state");
            window.ValidateToolkitSplitButtonPopupState(expectedOpen: true);

            window.SplitActionButton.IsOpen = false;
            PumpDispatcherUntil(
                window,
                () => !window.SplitActionButton.IsOpen,
                TimeSpan.FromSeconds(2),
                "Toolkit SplitButton dropdown closed state");
            window.ValidateToolkitSplitButtonPopupState(expectedOpen: false);
            window.ExerciseToolkitResourceTheme();

            window.DockDocumentContextMenu.PlacementTarget = window.DockManager;
            window.DockDocumentContextMenu.IsOpen = true;
            PumpDispatcherUntil(
                window,
                () => window.DockDocumentContextMenu.IsOpen,
                TimeSpan.FromSeconds(2),
                "AvalonDock document context menu open state");
            window.ValidateAvalonDockDocumentContextMenuState(expectedOpen: true);

            window.DockContextCancelNextCloseMenuItem.IsChecked = true;
            window.DockContextCancelNextCloseMenuItem.GetBindingExpression(MenuItem.IsCheckedProperty)?.UpdateSource();
            if (!window.ViewModel.CancelNextOverviewClose)
            {
                throw new InvalidOperationException("Expected AvalonDock context menu checkable item to update close-cancellation binding.");
            }

            window.ExerciseAvalonDockDocumentContextMenuCommands();

            window.DockDocumentContextMenu.IsOpen = false;
            PumpDispatcherUntil(
                window,
                () => !window.DockDocumentContextMenu.IsOpen,
                TimeSpan.FromSeconds(2),
                "AvalonDock document context menu closed state");
            window.ValidateAvalonDockDocumentContextMenuState(expectedOpen: false);

            window.DockAnchorableContextMenu.PlacementTarget = window.DockManager;
            window.DockAnchorableContextMenu.IsOpen = true;
            PumpDispatcherUntil(
                window,
                () => window.DockAnchorableContextMenu.IsOpen,
                TimeSpan.FromSeconds(2),
                "AvalonDock anchorable context menu open state");
            window.ValidateAvalonDockAnchorableContextMenuState(expectedOpen: true);
            window.ExerciseAvalonDockAnchorableContextMenuCommands();
            window.DockAnchorableContextMenu.IsOpen = false;
            PumpDispatcherUntil(
                window,
                () => !window.DockAnchorableContextMenu.IsOpen,
                TimeSpan.FromSeconds(2),
                "AvalonDock anchorable context menu closed state");
            window.ValidateAvalonDockAnchorableContextMenuState(expectedOpen: false);

            window.ExerciseAvalonDockKeyboardNavigation();
            window.ExerciseAvalonDockAnchorableKeyboardNavigation();
            window.ExerciseAvalonDockAutoHideOverlayKeyboardNavigation();

            if (window.ViewModel.AccentColor != Colors.MediumSeaGreen ||
                window.ViewModel.Estimate != 42.25m)
            {
                throw new InvalidOperationException("Expected Toolkit popup editor changes to update bindings.");
            }

            window.QuickSearchTextBox.Text = "Application.Run quick search";
            window.QuickSearchTextBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            window.AccessCodeBox.Password = "run-code";
            window.ViewModel.AccessCode = window.AccessCodeBox.Password;
            window.ReferenceMaskTextBox.Text = "ZX-9876";
            window.ReferenceMaskTextBox.GetBindingExpression(MaskedTextBox.TextProperty)?.UpdateSource();
            window.ReminderTimePicker.Value = DateTime.Today.AddHours(16);
            window.ReviewedAtEditor.Value = DateTime.Today.AddHours(17);
            window.EffortEditor.Value = TimeSpan.FromHours(3);
            window.ByteScoreEditor.Value = 64;
            window.DoubleScaleEditor.Value = 2.5;
            window.WorkItemIdEditor.Value = 8192L;
            window.BudgetEditor.Value = 128.75m;
            window.AccentColorCanvas.SelectedColor = Colors.CadetBlue;
            window.OwnerComboBox.SelectedItem = "ProGPU";
            window.OwnerComboBox.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();
            window.PriorityRangeSlider.LowerValue = 3.0;
            window.PriorityRangeSlider.HigherValue = 7.0;
            window.PriorityRangeSlider.GetBindingExpression(RangeSlider.LowerValueProperty)?.UpdateSource();
            window.PriorityRangeSlider.GetBindingExpression(RangeSlider.HigherValueProperty)?.UpdateSource();
            window.ToolkitRichTextBox.Text = "Application.Run rich notes";
            window.ToolkitRichTextBox.GetBindingExpression(ToolkitRichTextBox.TextProperty)?.UpdateSource();
            window.MultiLineNotesEditor.Text = "Application.Run multiline notes";
            window.MultiLineNotesEditor.GetBindingExpression(MultiLineTextEditor.TextProperty)?.UpdateSource();
            int spinnerCountBefore = window.ViewModel.SpinnerCount;
            window.ExerciseDocumentCountSpinner();
            if (!window.ViewModel.SelectedFlags.Contains("Urgent"))
            {
                window.ViewModel.SelectedFlags.Add("Urgent");
            }

            window.ValidateToolkitInputEditorState();
            if (!string.Equals(window.ViewModel.QuickSearchText, "Application.Run quick search", StringComparison.Ordinal) ||
                !string.Equals(window.ViewModel.AccessCode, "run-code", StringComparison.Ordinal) ||
                !string.Equals(window.ViewModel.ReferenceCode, "ZX-9876", StringComparison.Ordinal) ||
                window.ViewModel.ReminderTime != DateTime.Today.AddHours(16) ||
                window.ViewModel.ReviewedAt != DateTime.Today.AddHours(17) ||
                window.ViewModel.Effort != TimeSpan.FromHours(3) ||
                window.ViewModel.ByteScore != 64 ||
                window.ViewModel.DoubleScale != 2.5 ||
                window.ViewModel.WorkItemId != 8192L ||
                window.ViewModel.Budget != 128.75m ||
                window.ViewModel.AccentColor != Colors.CadetBlue ||
                !string.Equals(window.ViewModel.SelectedOwner, "ProGPU", StringComparison.Ordinal) ||
                window.ViewModel.PriorityRangeStart != 3.0 ||
                window.ViewModel.PriorityRangeEnd != 7.0 ||
                !string.Equals(window.ViewModel.RichNotes, "Application.Run rich notes", StringComparison.Ordinal) ||
                !string.Equals(window.ViewModel.MultiLineNotes, "Application.Run multiline notes", StringComparison.Ordinal) ||
                window.ViewModel.SpinnerCount != spinnerCountBefore ||
                !window.FlagListBox.SelectedItems.Contains("Urgent"))
            {
                throw new InvalidOperationException("Expected Toolkit input editor changes to update bindings and selection state.");
            }

            window.ExerciseToolkitChildWindow();
            window.ExerciseToolkitMessageBox();
            window.ExerciseStaticToolkitMessageBoxes();
            window.ExerciseToolkitWindowControl();
            window.ExerciseToolkitZoomboxAndMagnifier();
            window.ExerciseToolkitCollectionControl();
        }

        int themeSwitchCountBefore = window.ViewModel.DockThemeSwitchCount;
        window.CycleDockThemeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateAvalonDockThemeState("Metro");
        window.CycleDockThemeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateAvalonDockThemeState("VS2010");
        window.CycleDockThemeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateAvalonDockThemeState("Aero");
        if (window.ViewModel.DockThemeSwitchCount < themeSwitchCountBefore + 3)
        {
            throw new InvalidOperationException("Expected AvalonDock theme switch count to advance for all package themes.");
        }

        int documentsBeforeAdd = window.ViewModel.DocumentCount;
        window.AddDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (window.DocumentPane.ChildrenCount != documentsBeforeAdd + 1 ||
            window.ViewModel.Documents.Count != documentsBeforeAdd + 1)
        {
            throw new InvalidOperationException("Expected AvalonDock document insertion to update model and layout.");
        }

        if (!string.Equals(window.ViewModel.Status, $"Added Generated {documentsBeforeAdd + 1}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Add document command to update sample status.");
        }

        window.ActivateEditorButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (!window.EditorDocument.IsSelected || !window.EditorDocument.IsActive)
        {
            throw new InvalidOperationException("Expected AvalonDock document activation to update selected/active document state.");
        }

        if (window.ViewModel.AvalonDockActiveContentChangedCount <= 0)
        {
            throw new InvalidOperationException("Expected AvalonDock ActiveContentChanged event to fire after document activation.");
        }

        int documentCountBeforeCanceledOverviewClose = window.DocumentPane.ChildrenCount;
        int overviewClosedCountBeforeCanceledClose = window.ViewModel.OverviewDocumentClosedCount;
        window.ViewModel.CancelNextOverviewClose = true;
        window.CloseOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateOverviewCloseCanceledState(
            documentCountBeforeCanceledOverviewClose,
            overviewClosedCountBeforeCanceledClose);

        int documentCountBeforeOverviewClose = window.DocumentPane.ChildrenCount;
        window.CloseOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateOverviewDocumentLifecycleState(expectedOpen: false);
        if (window.DocumentPane.ChildrenCount != documentCountBeforeOverviewClose - 1 ||
            !string.Equals(window.ViewModel.Status, "Overview document closed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected AvalonDock overview close command to remove the document and update status.");
        }

        int documentCountBeforeOverviewReopen = window.DocumentPane.ChildrenCount;
        window.ReopenOverviewDocumentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateOverviewDocumentLifecycleState(expectedOpen: true);
        if (window.DocumentPane.ChildrenCount != documentCountBeforeOverviewReopen + 1 ||
            !string.Equals(window.ViewModel.Status, "Overview document reopened", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected AvalonDock overview reopen command to restore the document and update status.");
        }

        string bodyBeforeReview = window.ViewModel.SelectedDocument.Body;
        window.ActionDropDownButton.IsOpen = true;
        window.MarkReviewedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (window.ActionDropDownButton.IsOpen ||
            string.Equals(window.ViewModel.SelectedDocument.Body, bodyBeforeReview, StringComparison.Ordinal) ||
            !string.Equals(window.ViewModel.Status, "Document marked reviewed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Toolkit DropDownButton command to update the selected document and close the dropdown.");
        }

        window.SplitActionButton.IsOpen = true;
        window.AssignSdkOwnerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (window.SplitActionButton.IsOpen ||
            !string.Equals(window.ViewModel.SelectedOwner, "SDK", StringComparison.Ordinal) ||
            !string.Equals(window.ViewModel.Status, "Owner set to SDK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Toolkit SplitButton dropdown command to update owner selection and close the dropdown.");
        }

        window.SplitActionButton.RaiseEvent(new RoutedEventArgs(SplitButton.ClickEvent));
        if (!string.Equals(window.ViewModel.Status, "Applied owner SDK", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Toolkit SplitButton primary command to update sample status.");
        }

        if (expectLoaded)
        {
            window.ExerciseToolkitWizard();
        }

        if (expectLoaded)
        {
            window.ToggleEditorFloatButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            PumpDispatcherUntil(
                window,
                () => window.EditorDocument.IsFloating && window.DockLayoutRoot.FloatingWindows.Count == 1,
                TimeSpan.FromSeconds(2),
                "AvalonDock editor document floating window model");
            window.ValidateEditorFloatingState(expectedFloating: true);

            window.ToggleEditorFloatButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            PumpDispatcherUntil(
                window,
                () => !window.EditorDocument.IsFloating && window.DockLayoutRoot.FloatingWindows.Count == 0,
                TimeSpan.FromSeconds(2),
                "AvalonDock editor document docked model");
            window.ValidateEditorFloatingState(expectedFloating: false);
        }

        int propertyPaneHidingCountBefore = window.ViewModel.AvalonDockAnchorableHidingCount;
        int propertyPaneVisibleChangedCountBefore = window.ViewModel.AvalonDockAnchorableIsVisibleChangedCount;
        window.TogglePropertyPaneButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidatePropertyPaneAnchorableLifecycle(
            propertyPaneHidingCountBefore,
            propertyPaneVisibleChangedCountBefore,
            expectedHidden: true);

        propertyPaneVisibleChangedCountBefore = window.ViewModel.AvalonDockAnchorableIsVisibleChangedCount;
        window.TogglePropertyPaneButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidatePropertyPaneAnchorableLifecycle(
            window.ViewModel.AvalonDockAnchorableHidingCount,
            propertyPaneVisibleChangedCountBefore,
            expectedHidden: false);

        int activityPaneCountBeforeClose = window.RightAnchorablePane.ChildrenCount;
        int activityPaneClosingCountBefore = window.ViewModel.AvalonDockAnchorableClosingCount;
        int activityPaneClosedCountBefore = window.ViewModel.AvalonDockAnchorableClosedCount;
        window.CloseActivityPaneButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateActivityPaneClosedState(activityPaneClosingCountBefore, activityPaneClosedCountBefore);
        if (window.RightAnchorablePane.ChildrenCount != activityPaneCountBeforeClose - 1 ||
            !string.Equals(window.ViewModel.Status, "Activity pane closed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected AvalonDock activity anchorable close command to remove the pane and update status.");
        }

        window.ReopenActivityPaneButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        window.ValidateActivityPaneReopenedState();
        if (window.RightAnchorablePane.ChildrenCount != activityPaneCountBeforeClose ||
            !string.Equals(window.ViewModel.Status, "Activity pane reopened", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected AvalonDock activity anchorable reopen command to restore the pane and update status.");
        }

        window.ToggleActivityAutoHideButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (!window.ActivityPane.IsAutoHidden || window.DockLayoutRoot.RightSide.ChildrenCount == 0)
        {
            throw new InvalidOperationException("Expected AvalonDock activity anchorable to auto-hide into the right side.");
        }

        window.ToggleAgendaAutoHideButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (window.AgendaPane.IsAutoHidden || window.AgendaPane.Parent is LayoutAnchorGroup)
        {
            throw new InvalidOperationException("Expected AvalonDock agenda anchorable to dock back from the left auto-hide side.");
        }

        window.SerializeLayoutButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        if (!window.ViewModel.LastSerializedLayout.Contains("<LayoutRoot", StringComparison.Ordinal) ||
            !window.ViewModel.LastSerializedLayout.Contains("ContentId=\"overview\"", StringComparison.Ordinal) ||
            !window.ViewModel.LastSerializedLayout.Contains("ContentId=\"editor\"", StringComparison.Ordinal) ||
            !window.ViewModel.LastSerializedLayout.Contains("ContentId=\"activity\"", StringComparison.Ordinal) ||
            !window.ViewModel.LastSerializedLayout.Contains("ContentId=\"agenda\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected AvalonDock layout serialization to include document content ids.");
        }

        var roundTripped = MainWindow.RoundTripLayout(window.ViewModel.LastSerializedLayout);
        if (roundTripped.Layout.RootPanel is null ||
            roundTripped.Layout.RootPanel.ChildrenCount != window.DockLayoutRoot.RootPanel.ChildrenCount)
        {
            throw new InvalidOperationException("Expected AvalonDock layout deserialization to restore the root panel shape.");
        }

        window.ValidateAvalonDockLayoutReplacementEvents(window.ViewModel.LastSerializedLayout);

        if (expectLoaded && !window.IsLoaded)
        {
            throw new InvalidOperationException("Expected Toolkit app window to be loaded during Application.Run validation.");
        }
    }

    private static T Require<T>(FrameworkElement root, string name)
        where T : class
    {
        return root.FindName(name) as T
            ?? throw new InvalidOperationException($"Expected {typeof(T).FullName} named {name}.");
    }

    private static void PumpDispatcherUntil(
        DispatcherObject dispatcherObject,
        Func<bool> condition,
        TimeSpan timeout,
        string description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            dispatcherObject.Dispatcher.Invoke(
                static () => { },
                DispatcherPriority.Background);

            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException($"Timed out waiting for {description}.");
            }

            System.Threading.Thread.Sleep(1);
        }
    }
}

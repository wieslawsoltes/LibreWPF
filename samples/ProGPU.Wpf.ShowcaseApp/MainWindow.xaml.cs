using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.ProGPU;
using System.Windows.Media.ProGPU.Platform;
using System.Windows.Navigation;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using ProGPU.Wpf.Interop;
using WpfCalendar = System.Windows.Controls.Calendar;

namespace ProGPU.Wpf.ShowcaseApp;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand RefreshStatusCommand =
        new("Refresh status", nameof(RefreshStatusCommand), typeof(MainWindow));

    private const string LiveValidationEnvironmentVariable = "PROGPU_WPF_SHOWCASE_LIVE_VALIDATE";
    private const string LivePerformanceValidationEnvironmentVariable = "PROGPU_WPF_SHOWCASE_PERFORMANCE_VALIDATE";
    private const string LiveValidationStatusPathEnvironmentVariable = "PROGPU_WPF_SHOWCASE_LIVE_VALIDATE_STATUS_PATH";
    private const string LiveNativeDragStatusPathEnvironmentVariable = "PROGPU_WPF_SHOWCASE_NATIVE_DRAG_STATUS_PATH";
    private const int LiveValidationMaxAttempts = 600;
    private static readonly TimeSpan LiveValidationRetryDelay = TimeSpan.FromMilliseconds(16);
    private static readonly FrameworkThemeDefinition[] s_frameworkThemes =
    [
        new("Aero", "/PresentationFramework.Aero;component/themes/Aero.NormalColor.xaml"),
        new("Aero2", "/PresentationFramework.Aero2;component/Themes/Aero2.NormalColor.xaml"),
        new("AeroLite", "/PresentationFramework.AeroLite;component/Themes/AeroLite.NormalColor.xaml"),
        new("Classic", "/PresentationFramework.Classic;component/Themes/Classic.xaml"),
        new("Fluent", "/PresentationFramework.Fluent;component/Themes/Fluent.xaml"),
        new("Luna", "/PresentationFramework.Luna;component/Themes/Luna.NormalColor.xaml"),
        new("Royale", "/PresentationFramework.Royale;component/Themes/Royale.NormalColor.xaml")
    ];
    private bool _liveValidationStarted;
    private ResourceDictionary? _activeFrameworkThemeDictionary;
    private string _activeFrameworkThemeName = "Fluent";

    private readonly record struct FrameworkThemeDefinition(string Name, string Source);

    private readonly record struct LivePopupSurfaceSnapshot(
        bool IsReady,
        bool HasPortableSnapshot,
        ProGpuWpfDiagnostics.CompositionLayerSnapshot Composition,
        ProGpuWpfDiagnostics.PortablePopupSnapshot Portable);

    internal int EditorPasswordChangedCount { get; private set; }

    internal int DataObjectRoundTripCount { get; private set; }

    internal string? LastDataObjectText { get; private set; }

    internal string? LastDataObjectCustomText { get; private set; }

    internal int ClipboardRoundTripCount { get; private set; }

    internal string? LastClipboardText { get; private set; }

    internal bool LastClipboardContainsText { get; private set; }

    internal bool LastClipboardIsCurrent { get; private set; }

    internal int BindingTargetUpdatedCount { get; private set; }

    internal string? LastBindingTargetUpdatedName { get; private set; }

    internal string? LastBindingTargetUpdatedPropertyName { get; private set; }

    internal int BindingSourceUpdatedCount { get; private set; }

    internal string? LastBindingSourceUpdatedName { get; private set; }

    internal string? LastBindingSourceUpdatedPropertyName { get; private set; }

    internal int SelectorSelectionChangedCount { get; private set; }

    internal int MultiSelectorSelectionChangedCount { get; private set; }

    internal int SelectorExpanderExpandedCount { get; private set; }

    internal int SelectorExpanderCollapsedCount { get; private set; }

    internal int SelectorMouseWheelCount { get; private set; }

    internal string? LastSelectorMouseWheelSenderName { get; private set; }

    internal string? LastSelectorMouseWheelRoutedEventName { get; private set; }

    internal int LastSelectorMouseWheelDelta { get; private set; }

    internal int InputToggleCheckedCount { get; private set; }

    internal int InputToggleUncheckedCount { get; private set; }

    internal int CategoryRadioCheckedCount { get; private set; }

    internal string? LastCategoryRadioName { get; private set; }

    internal int InputRepeatButtonClickCount { get; private set; }

    internal int InputThumbDragStartedCount { get; private set; }

    internal int InputThumbDragDeltaCount { get; private set; }

    internal int InputThumbDragCompletedCount { get; private set; }

    internal int InputBubbledThumbDragDeltaCount { get; private set; }

    internal string? LastInputThumbDragStartedSenderName { get; private set; }

    internal string? LastInputThumbDragDeltaSenderName { get; private set; }

    internal string? LastInputThumbDragCompletedSenderName { get; private set; }

    internal string? LastInputBubbledThumbDragDeltaSenderName { get; private set; }

    internal string? LastInputBubbledThumbDragDeltaOriginalSourceName { get; private set; }

    internal string? LastInputThumbDragStartedRoutedEventName { get; private set; }

    internal string? LastInputThumbDragDeltaRoutedEventName { get; private set; }

    internal string? LastInputThumbDragCompletedRoutedEventName { get; private set; }

    internal string? LastInputBubbledThumbDragDeltaRoutedEventName { get; private set; }

    internal double LastInputThumbDragStartedHorizontalOffset { get; private set; }

    internal double LastInputThumbDragStartedVerticalOffset { get; private set; }

    internal double LastInputThumbDragDeltaHorizontalChange { get; private set; }

    internal double LastInputThumbDragDeltaVerticalChange { get; private set; }

    internal double LastInputThumbDragCompletedHorizontalChange { get; private set; }

    internal double LastInputThumbDragCompletedVerticalChange { get; private set; }

    internal bool LastInputThumbDragCompletedCanceled { get; private set; }

    internal double LastInputBubbledThumbDragDeltaHorizontalChange { get; private set; }

    internal double LastInputBubbledThumbDragDeltaVerticalChange { get; private set; }

    internal int ShowcasePreviewDragEnterCount { get; private set; }

    internal int ShowcaseDragEnterCount { get; private set; }

    internal int ShowcasePreviewDragOverCount { get; private set; }

    internal int ShowcaseDragOverCount { get; private set; }

    internal int ShowcasePreviewDropCount { get; private set; }

    internal int ShowcaseDropCount { get; private set; }

    internal string? LastShowcasePreviewDragEnterEventName { get; private set; }

    internal string? LastShowcaseDragEnterEventName { get; private set; }

    internal string? LastShowcasePreviewDragOverEventName { get; private set; }

    internal string? LastShowcaseDragOverEventName { get; private set; }

    internal string? LastShowcasePreviewDropEventName { get; private set; }

    internal string? LastShowcaseDropEventName { get; private set; }

    internal string? LastShowcaseDropText { get; private set; }

    internal int LastShowcaseDropFileCount { get; private set; }

    internal string? LastShowcaseDropFirstFile { get; private set; }

    internal string? LastShowcaseDropAllowedEffects { get; private set; }

    internal string? LastShowcaseDropEffects { get; private set; }

    internal double LastShowcaseDropX { get; private set; }

    internal double LastShowcaseDropY { get; private set; }

    internal int InputDateSelectionChangedCount { get; private set; }

    internal string? LastDateSelectionSenderName { get; private set; }

    internal int ShowcaseRoutedEventSourceCount { get; private set; }

    internal int ShowcaseRoutedEventScopeCount { get; private set; }

    internal int ShowcaseRoutedEventHandledTooCount { get; private set; }

    internal string? LastShowcaseRoutedEventSenderName { get; private set; }

    internal string? LastShowcaseRoutedEventOriginalSourceName { get; private set; }

    internal string? LastShowcaseRoutedEventPayload { get; private set; }

    internal string? LastShowcaseRoutedEventName { get; private set; }

    internal int ShowcaseStyleEventSetterClickCount { get; private set; }

    internal string? LastShowcaseStyleEventSetterSenderName { get; private set; }

    internal string? LastShowcaseStyleEventSetterRoutedEventName { get; private set; }

    internal int DocumentLinkRequestNavigateCount { get; private set; }

    internal string? LastDocumentLinkRequestNavigateText { get; private set; }

    internal string? LastDocumentLinkRequestNavigateUri { get; private set; }

    internal string? LastDocumentLinkRequestNavigateRoutedEventName { get; private set; }

    internal int ShowcaseTabSelectionChangedCount { get; private set; }

    internal string? LastShowcaseTabHeader { get; private set; }

    internal int ExplicitExplorerTreeExpandedCount { get; private set; }

    internal int ExplicitExplorerTreeCollapsedCount { get; private set; }

    internal int ExplicitExplorerTreeSelectedCount { get; private set; }

    internal int ExplicitExplorerTreeUnselectedCount { get; private set; }

    internal string? LastExplicitExplorerTreeSenderName { get; private set; }

    internal string? LastExplicitExplorerTreeRoutedEventName { get; private set; }

    internal string? LastExplicitExplorerTreeHeader { get; private set; }

    internal int MessageBoxShownCount { get; private set; }

    internal MessageBoxResult LastMessageBoxResult { get; private set; } = MessageBoxResult.None;

    internal int FileDialogShownCount { get; private set; }

    internal bool? LastOpenFileDialogResult { get; private set; }

    internal bool? LastSaveFileDialogResult { get; private set; }

    internal bool? LastFolderDialogResult { get; private set; }

    internal string LastOpenFileDialogFileName { get; private set; } = string.Empty;

    internal string LastOpenFileDialogSafeFileName { get; private set; } = string.Empty;

    internal string LastSaveFileDialogFileName { get; private set; } = string.Empty;

    internal string LastSaveFileDialogSafeFileName { get; private set; } = string.Empty;

    internal string LastFolderDialogFolderName { get; private set; } = string.Empty;

    internal string LastFolderDialogSafeFolderName { get; private set; } = string.Empty;

    internal int SystemCommandCanExecuteCount { get; private set; }

    internal int SystemCommandExecutedCount { get; private set; }

    internal string? LastSystemCommandName { get; private set; }

    internal string? LastSystemCommandParameter { get; private set; }

    internal int ChromeCaptionMouseDownCount { get; private set; }

    internal string ChromeDragMoveStatus { get; private set; } = "Idle";

    public MainWindow()
    {
        var viewModel = new MainViewModel();
        DataContext = viewModel;
        InitializeComponent();
        InitializeFrameworkThemeState();

        SelectorScrollViewer.AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnSelectorScrollViewerMouseWheel), true);
        ShowcaseRoutedEventScope.AddHandler(
            ShowcaseRoutedEventButton.ShowcaseActivatedEvent,
            new ShowcaseRoutedEventHandler(OnShowcaseRoutedEventScopeHandledToo),
            handledEventsToo: true);

        if (FindResource("ItemsViewSource") is CollectionViewSource itemsViewSource)
        {
            itemsViewSource.Source = viewModel.Items;
        }

        StartLiveValidationIfRequired();
    }

    private void OnShowcaseWindowLoaded(object sender, RoutedEventArgs e)
    {
        StartLiveValidationIfRequired();
    }

    internal static IReadOnlyList<string> FrameworkThemeNames
    {
        get
        {
            var names = new string[s_frameworkThemes.Length];
            for (int i = 0; i < s_frameworkThemes.Length; i++)
            {
                names[i] = s_frameworkThemes[i].Name;
            }

            return names;
        }
    }

    internal string ActiveFrameworkThemeName => _activeFrameworkThemeName;

    internal void ApplyFrameworkTheme(string themeName)
    {
        FrameworkThemeDefinition theme = FindFrameworkTheme(themeName);
        var replacement = new ResourceDictionary
        {
            Source = new Uri(theme.Source, UriKind.Relative)
        };
        var application = Application.Current
            ?? throw new InvalidOperationException("Expected an Application while switching the Showcase framework theme.");
        Collection<ResourceDictionary> merged = application.Resources.MergedDictionaries;
        int currentIndex = FindFrameworkThemeDictionaryIndex(merged);
        if (currentIndex >= 0)
        {
            merged[currentIndex] = replacement;
        }
        else
        {
            merged.Insert(0, replacement);
        }

        _activeFrameworkThemeDictionary = replacement;
        _activeFrameworkThemeName = theme.Name;
        UpdateFrameworkThemeMenuChecks();
        foreach (Window window in application.Windows)
        {
            window.InvalidateMeasure();
            window.InvalidateVisual();
        }
    }

    private void OnFrameworkThemeMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string themeName })
        {
            throw new InvalidOperationException("Expected a framework theme MenuItem with a string Tag.");
        }

        ApplyFrameworkTheme(themeName);
    }

    private void InitializeFrameworkThemeState()
    {
        var application = Application.Current;
        if (application == null)
        {
            UpdateFrameworkThemeMenuChecks();
            return;
        }

        int index = FindFrameworkThemeDictionaryIndex(application.Resources.MergedDictionaries);
        if (index >= 0)
        {
            _activeFrameworkThemeDictionary = application.Resources.MergedDictionaries[index];
            string source = _activeFrameworkThemeDictionary.Source?.OriginalString ?? string.Empty;
            for (int i = 0; i < s_frameworkThemes.Length; i++)
            {
                if (string.Equals(s_frameworkThemes[i].Source, source, StringComparison.OrdinalIgnoreCase))
                {
                    _activeFrameworkThemeName = s_frameworkThemes[i].Name;
                    break;
                }
            }
        }

        UpdateFrameworkThemeMenuChecks();
    }

    private int FindFrameworkThemeDictionaryIndex(Collection<ResourceDictionary> merged)
    {
        if (_activeFrameworkThemeDictionary != null)
        {
            int activeIndex = merged.IndexOf(_activeFrameworkThemeDictionary);
            if (activeIndex >= 0)
            {
                return activeIndex;
            }
        }

        for (int dictionaryIndex = 0; dictionaryIndex < merged.Count; dictionaryIndex++)
        {
            string source = merged[dictionaryIndex].Source?.OriginalString ?? string.Empty;
            for (int themeIndex = 0; themeIndex < s_frameworkThemes.Length; themeIndex++)
            {
                if (string.Equals(s_frameworkThemes[themeIndex].Source, source, StringComparison.OrdinalIgnoreCase))
                {
                    return dictionaryIndex;
                }
            }
        }

        return -1;
    }

    private static FrameworkThemeDefinition FindFrameworkTheme(string themeName)
    {
        for (int i = 0; i < s_frameworkThemes.Length; i++)
        {
            if (string.Equals(s_frameworkThemes[i].Name, themeName, StringComparison.Ordinal))
            {
                return s_frameworkThemes[i];
            }
        }

        throw new ArgumentOutOfRangeException(nameof(themeName), themeName, "Unknown Showcase framework theme.");
    }

    private void UpdateFrameworkThemeMenuChecks()
    {
        for (int i = 0; i < s_frameworkThemes.Length; i++)
        {
            if (FindName($"{s_frameworkThemes[i].Name}ThemeMenuItem") is MenuItem item)
            {
                item.IsChecked = string.Equals(
                    s_frameworkThemes[i].Name,
                    _activeFrameworkThemeName,
                    StringComparison.Ordinal);
            }
        }
    }

    private void StartLiveValidationIfRequired()
    {
        if (_liveValidationStarted)
        {
            return;
        }

        if (Environment.GetEnvironmentVariable(LiveValidationEnvironmentVariable) != "1")
        {
            return;
        }

        _liveValidationStarted = true;
        Console.WriteLine("ProGPU WPF Showcase live input validation started.");
        _ = Task.Run(
            async () =>
            {
                try
                {
                    await ValidateRequiredLiveShowcaseAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Environment.Exit(1);
                }
            });
    }

    private void OnOverviewNavigationClick(object sender, RoutedEventArgs e)
    {
        NavigationFrame.Navigate(new Uri("OverviewPage.xaml", UriKind.Relative));
    }

    private void OnDetailsNavigationClick(object sender, RoutedEventArgs e)
    {
        NavigationFrame.Navigate(new Uri("DetailsPage.xaml", UriKind.Relative));
    }

    private void OnBackNavigationClick(object sender, RoutedEventArgs e)
    {
        if (NavigationFrame.CanGoBack)
        {
            NavigationFrame.GoBack();
        }
    }

    private void OnForwardNavigationClick(object sender, RoutedEventArgs e)
    {
        if (NavigationFrame.CanGoForward)
        {
            NavigationFrame.GoForward();
        }
    }

    private void OnAboutMenuItemClick(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutWindow();
        if (IsVisible)
        {
            dialog.Owner = this;
        }

        dialog.ShowDialog();
    }

    private void OnMessageBoxButtonClick(object sender, RoutedEventArgs e)
    {
        MessageBoxShownCount++;
        LastMessageBoxResult = MessageBox.Show(
            this,
            "Portable MessageBox from the ProGPU WPF Showcase app.",
            "ProGPU WPF Showcase",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information,
            MessageBoxResult.OK,
            MessageBoxOptions.None);
        MessageBoxStatusText.Text = $"MessageBox result: {LastMessageBoxResult}";
    }

    private void OnFileDialogButtonClick(object sender, RoutedEventArgs e)
    {
        FileDialogShownCount++;

        var openDialog = new OpenFileDialog
        {
            Title = "Open Showcase file",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };
        LastOpenFileDialogResult = openDialog.ShowDialog();
        LastOpenFileDialogFileName = LastOpenFileDialogResult == true ? openDialog.FileName : string.Empty;
        LastOpenFileDialogSafeFileName = LastOpenFileDialogResult == true ? openDialog.SafeFileName : string.Empty;

        var saveDialog = new SaveFileDialog
        {
            Title = "Save Showcase file",
            DefaultExt = "txt",
            FileName = "saved",
            Filter = "Text files (*.txt)|*.txt",
            OverwritePrompt = false
        };
        LastSaveFileDialogResult = saveDialog.ShowDialog(this);
        LastSaveFileDialogFileName = LastSaveFileDialogResult == true ? saveDialog.FileName : string.Empty;
        LastSaveFileDialogSafeFileName = LastSaveFileDialogResult == true ? saveDialog.SafeFileName : string.Empty;

        var folderDialog = new OpenFolderDialog
        {
            Title = "Select Showcase folder"
        };
        LastFolderDialogResult = folderDialog.ShowDialog(this);
        LastFolderDialogFolderName = LastFolderDialogResult == true ? folderDialog.FolderName : string.Empty;
        LastFolderDialogSafeFolderName = LastFolderDialogResult == true ? folderDialog.SafeFolderName : string.Empty;

        FileDialogStatusText.Text =
            $"File dialogs: {LastOpenFileDialogSafeFileName} | {LastSaveFileDialogSafeFileName} | {LastFolderDialogSafeFolderName}";
    }

    private void OnDocumentLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        DocumentLinkRequestNavigateCount++;
        LastDocumentLinkRequestNavigateText = sender is Hyperlink link
            ? new TextRange(link.ContentStart, link.ContentEnd).Text.Trim()
            : "RequestNavigateSource";
        LastDocumentLinkRequestNavigateUri = e.Uri?.ToString();
        LastDocumentLinkRequestNavigateRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnItemsViewSourceFilter(object sender, FilterEventArgs e)
    {
        e.Accepted = DataContext is not MainViewModel { ShowActiveOnly: true }
            || e.Item is ShowcaseItem { IsActive: true };
    }

    private void OnActiveOnlyFilterChanged(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && DataContext is MainViewModel viewModel)
        {
            viewModel.ShowActiveOnly = checkBox.IsChecked == true;
        }

        RefreshItemsView();
    }

    private void OnShowcaseBindingTargetUpdated(object sender, DataTransferEventArgs e)
    {
        BindingTargetUpdatedCount++;
        LastBindingTargetUpdatedName = GetElementName(e.TargetObject);
        LastBindingTargetUpdatedPropertyName = e.Property?.Name;
    }

    private void OnShowcaseBindingSourceUpdated(object sender, DataTransferEventArgs e)
    {
        BindingSourceUpdatedCount++;
        LastBindingSourceUpdatedName = GetElementName(e.TargetObject);
        LastBindingSourceUpdatedPropertyName = e.Property?.Name;
    }

    private void RefreshItemsView()
    {
        if (FindResource("ItemsViewSource") is CollectionViewSource itemsViewSource)
        {
            itemsViewSource.View?.Refresh();
        }
    }

    private void OnRefreshStatusCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = DataContext is MainViewModel { ActionsEnabled: true };
        e.Handled = true;
    }

    private void OnRefreshStatusCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.RefreshCommandStatus();
        }

        e.Handled = true;
    }

    private void OnSystemCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        SystemCommandCanExecuteCount++;
        e.CanExecute = true;
        e.Handled = true;
    }

    private void OnSystemCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        SystemCommandExecutedCount++;
        LastSystemCommandName = (e.Command as RoutedCommand)?.Name;
        LastSystemCommandParameter = e.Parameter?.ToString();

        if (ReferenceEquals(e.Command, SystemCommands.MaximizeWindowCommand))
        {
            SystemCommands.MaximizeWindow(this);
        }
        else if (ReferenceEquals(e.Command, SystemCommands.MinimizeWindowCommand))
        {
            SystemCommands.MinimizeWindow(this);
        }
        else if (ReferenceEquals(e.Command, SystemCommands.RestoreWindowCommand))
        {
            SystemCommands.RestoreWindow(this);
        }
        else if (ReferenceEquals(e.Command, SystemCommands.ShowSystemMenuCommand))
        {
            SystemCommands.ShowSystemMenu(this, new Point(12.0, 24.0));
        }
        else
        {
            throw new InvalidOperationException($"Unexpected Showcase system command '{e.Command}'.");
        }

        e.Handled = true;
    }

    private void OnChromeCaptionMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ChromeCaptionMouseDownCount++;

        if (e.ChangedButton != MouseButton.Left ||
            Mouse.LeftButton != MouseButtonState.Pressed ||
            WindowState != WindowState.Normal)
        {
            ChromeDragMoveStatus = "Skipped";
            return;
        }

        try
        {
            DragMove();
            ChromeDragMoveStatus = "Requested";
        }
        catch (InvalidOperationException ex)
        {
            ChromeDragMoveStatus = string.IsNullOrWhiteSpace(ex.Message)
                ? "Unavailable"
                : ex.Message;
        }
    }

    private void OnBindingGroupCommitClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.BindingGroupStatus = BindingGroupPanel.BindingGroup?.CommitEdit() == true
                ? "Group committed"
                : "Group has validation errors";
        }
    }

    private void OnEditorPasswordChanged(object sender, RoutedEventArgs e)
    {
        EditorPasswordChangedCount++;
    }

    private void OnDataObjectRoundTripClick(object sender, RoutedEventArgs e)
    {
        var payload = DataObjectPayloadTextBox.Text;
        var dataObject = new DataObject();
        dataObject.SetText(payload);
        dataObject.SetData("ProGPU.Wpf.ShowcaseApp.CustomText", $"custom:{payload}");

        LastDataObjectText = dataObject.GetData(DataFormats.UnicodeText)?.ToString();
        LastDataObjectCustomText = dataObject.GetData("ProGPU.Wpf.ShowcaseApp.CustomText")?.ToString();
        DataObjectRoundTripCount++;
        DataObjectStatusText.Text = $"{LastDataObjectText} | {LastDataObjectCustomText}";
        e.Handled = true;
    }

    private void OnClipboardRoundTripClick(object sender, RoutedEventArgs e)
    {
        var payload = DataObjectPayloadTextBox.Text + " clipboard";
        Clipboard.Clear();
        Clipboard.SetText(payload);

        LastClipboardContainsText = Clipboard.ContainsText();
        LastClipboardText = Clipboard.GetText();
        IDataObject? currentDataObject = Clipboard.GetDataObject();
        LastClipboardIsCurrent = currentDataObject != null && Clipboard.IsCurrent(currentDataObject);
        Clipboard.Flush();

        ClipboardRoundTripCount++;
        DataObjectStatusText.Text = $"Clipboard: {LastClipboardText}";
        e.Handled = true;
    }

    private void OnSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectorSelectionChangedCount++;
    }

    private void OnShowcaseTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource))
        {
            return;
        }

        ShowcaseTabSelectionChangedCount++;
        LastShowcaseTabHeader = sender is TabControl { SelectedItem: TabItem { Header: object header } }
            ? header.ToString()
            : null;
    }

    private void OnMultiSelectorSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MultiSelectorSelectionChangedCount++;
    }

    private void OnSelectorExpanderExpanded(object sender, RoutedEventArgs e)
    {
        SelectorExpanderExpandedCount++;
    }

    private void OnSelectorExpanderCollapsed(object sender, RoutedEventArgs e)
    {
        SelectorExpanderCollapsedCount++;
    }

    private void OnSelectorScrollViewerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        SelectorMouseWheelCount++;
        LastSelectorMouseWheelSenderName = GetElementName(sender);
        LastSelectorMouseWheelRoutedEventName = e.RoutedEvent?.Name;
        LastSelectorMouseWheelDelta = e.Delta;
    }

    private void OnInputToggleChecked(object sender, RoutedEventArgs e)
    {
        InputToggleCheckedCount++;
    }

    private void OnInputToggleUnchecked(object sender, RoutedEventArgs e)
    {
        InputToggleUncheckedCount++;
    }

    private void OnCategoryRadioChecked(object sender, RoutedEventArgs e)
    {
        CategoryRadioCheckedCount++;
        LastCategoryRadioName = (sender as FrameworkElement)?.Name;

        if (DataContext is MainViewModel viewModel && sender is FrameworkElement { Tag: string category })
        {
            viewModel.SelectedCategory = category;
        }
    }

    private void OnInputRepeatButtonClick(object sender, RoutedEventArgs e)
    {
        InputRepeatButtonClickCount++;
    }

    private void OnInputThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        InputThumbDragStartedCount++;
        LastInputThumbDragStartedSenderName = GetElementName(sender);
        LastInputThumbDragStartedRoutedEventName = e.RoutedEvent?.Name;
        LastInputThumbDragStartedHorizontalOffset = e.HorizontalOffset;
        LastInputThumbDragStartedVerticalOffset = e.VerticalOffset;
    }

    private void OnInputThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        InputThumbDragDeltaCount++;
        LastInputThumbDragDeltaSenderName = GetElementName(sender);
        LastInputThumbDragDeltaRoutedEventName = e.RoutedEvent?.Name;
        LastInputThumbDragDeltaHorizontalChange = e.HorizontalChange;
        LastInputThumbDragDeltaVerticalChange = e.VerticalChange;
        InputDragStatusText.Text = $"Dragged {e.HorizontalChange}, {e.VerticalChange}";
    }

    private void OnInputThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        InputThumbDragCompletedCount++;
        LastInputThumbDragCompletedSenderName = GetElementName(sender);
        LastInputThumbDragCompletedRoutedEventName = e.RoutedEvent?.Name;
        LastInputThumbDragCompletedHorizontalChange = e.HorizontalChange;
        LastInputThumbDragCompletedVerticalChange = e.VerticalChange;
        LastInputThumbDragCompletedCanceled = e.Canceled;
    }

    private void OnInputBubbledThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        InputBubbledThumbDragDeltaCount++;
        LastInputBubbledThumbDragDeltaSenderName = GetElementName(sender);
        LastInputBubbledThumbDragDeltaOriginalSourceName = GetElementName(e.OriginalSource);
        LastInputBubbledThumbDragDeltaRoutedEventName = e.RoutedEvent?.Name;
        LastInputBubbledThumbDragDeltaHorizontalChange = e.HorizontalChange;
        LastInputBubbledThumbDragDeltaVerticalChange = e.VerticalChange;
    }

    private void OnShowcaseDropTargetPreviewDragEnter(object sender, DragEventArgs e)
    {
        ShowcasePreviewDragEnterCount++;
        LastShowcasePreviewDragEnterEventName = e.RoutedEvent?.Name;
        LastShowcaseDropAllowedEffects = e.AllowedEffects.ToString();
    }

    private void OnShowcaseDropTargetDragEnter(object sender, DragEventArgs e)
    {
        ShowcaseDragEnterCount++;
        LastShowcaseDragEnterEventName = e.RoutedEvent?.Name;
        LastShowcaseDropAllowedEffects = e.AllowedEffects.ToString();
        e.Effects = DragDropEffects.Move;
    }

    private void OnShowcaseDropTargetPreviewDragOver(object sender, DragEventArgs e)
    {
        ShowcasePreviewDragOverCount++;
        LastShowcasePreviewDragOverEventName = e.RoutedEvent?.Name;
        LastShowcaseDropAllowedEffects = e.AllowedEffects.ToString();
    }

    private void OnShowcaseDropTargetDragOver(object sender, DragEventArgs e)
    {
        ShowcaseDragOverCount++;
        LastShowcaseDragOverEventName = e.RoutedEvent?.Name;
        LastShowcaseDropAllowedEffects = e.AllowedEffects.ToString();
        e.Effects = DragDropEffects.Move;
    }

    private void OnShowcaseDropTargetPreviewDrop(object sender, DragEventArgs e)
    {
        ShowcasePreviewDropCount++;
        LastShowcasePreviewDropEventName = e.RoutedEvent?.Name;
        LastShowcaseDropAllowedEffects = e.AllowedEffects.ToString();
    }

    private void OnShowcaseDropTargetDrop(object sender, DragEventArgs e)
    {
        ShowcaseDropCount++;
        LastShowcaseDropEventName = e.RoutedEvent?.Name;
        LastShowcaseDropText = e.Data.GetDataPresent(DataFormats.UnicodeText)
            ? e.Data.GetData(DataFormats.UnicodeText) as string
            : e.Data.GetData(DataFormats.Text) as string;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        LastShowcaseDropFileCount = files?.Length ?? 0;
        LastShowcaseDropFirstFile = files is { Length: > 0 } ? files[0] : null;
        LastShowcaseDropAllowedEffects = e.AllowedEffects.ToString();
        var position = e.GetPosition(ShowcaseDropTarget);
        LastShowcaseDropX = position.X;
        LastShowcaseDropY = position.Y;
        e.Effects = DragDropEffects.Move;
        LastShowcaseDropEffects = e.Effects.ToString();
        ShowcaseDropTargetText.Text = $"{LastShowcaseDropText} ({LastShowcaseDropFileCount})";
        e.Handled = true;
    }

    private void OnInputDateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        InputDateSelectionChangedCount++;
        LastDateSelectionSenderName = (sender as FrameworkElement)?.Name;
    }

    private void OnExplicitExplorerTreeExpanded(object sender, RoutedEventArgs e)
    {
        ExplicitExplorerTreeExpandedCount++;
        RecordExplicitExplorerTreeEvent(sender, e);
    }

    private void OnExplicitExplorerTreeCollapsed(object sender, RoutedEventArgs e)
    {
        ExplicitExplorerTreeCollapsedCount++;
        RecordExplicitExplorerTreeEvent(sender, e);
    }

    private void OnExplicitExplorerTreeSelected(object sender, RoutedEventArgs e)
    {
        ExplicitExplorerTreeSelectedCount++;
        RecordExplicitExplorerTreeEvent(sender, e);
    }

    private void OnExplicitExplorerTreeUnselected(object sender, RoutedEventArgs e)
    {
        ExplicitExplorerTreeUnselectedCount++;
        RecordExplicitExplorerTreeEvent(sender, e);
    }

    private void RecordExplicitExplorerTreeEvent(object sender, RoutedEventArgs e)
    {
        LastExplicitExplorerTreeSenderName = GetElementName(sender);
        LastExplicitExplorerTreeRoutedEventName = e.RoutedEvent?.Name;
        LastExplicitExplorerTreeHeader = sender is TreeViewItem { Header: object header }
            ? header.ToString()
            : null;
        ExplicitExplorerTreeStatusText.Text =
            $"{LastExplicitExplorerTreeRoutedEventName}: {LastExplicitExplorerTreeHeader}";
    }

    private void OnShowcaseRoutedEventSource(object sender, ShowcaseRoutedEventArgs e)
    {
        ShowcaseRoutedEventSourceCount++;
        LastShowcaseRoutedEventName = e.RoutedEvent?.Name;
        LastShowcaseRoutedEventPayload = e.Payload;
        LastShowcaseRoutedEventSenderName = GetElementName(sender);
        LastShowcaseRoutedEventOriginalSourceName = GetElementName(e.OriginalSource);
    }

    private void OnShowcaseRoutedEventScope(object sender, ShowcaseRoutedEventArgs e)
    {
        ShowcaseRoutedEventScopeCount++;
        LastShowcaseRoutedEventName = e.RoutedEvent?.Name;
        LastShowcaseRoutedEventPayload = e.Payload;
        LastShowcaseRoutedEventSenderName = GetElementName(sender);
        LastShowcaseRoutedEventOriginalSourceName = GetElementName(e.OriginalSource);
        ShowcaseRoutedEventStatusText.Text = $"Handled {e.Payload}";
        e.Handled = true;
    }

    private void OnShowcaseRoutedEventScopeHandledToo(object sender, ShowcaseRoutedEventArgs e)
    {
        ShowcaseRoutedEventHandledTooCount++;
        LastShowcaseRoutedEventName = e.RoutedEvent?.Name;
        LastShowcaseRoutedEventPayload = e.Payload;
        LastShowcaseRoutedEventSenderName = GetElementName(sender);
        LastShowcaseRoutedEventOriginalSourceName = GetElementName(e.OriginalSource);
    }

    private void OnShowcaseStyleEventSetterClick(object sender, RoutedEventArgs e)
    {
        ShowcaseStyleEventSetterClickCount++;
        LastShowcaseStyleEventSetterSenderName = GetElementName(sender);
        LastShowcaseStyleEventSetterRoutedEventName = e.RoutedEvent?.Name;
        EventSetterStatusText.Text = "EventSetter clicked";
        e.Handled = true;
    }

    private async Task ValidateRequiredLiveShowcaseAsync()
    {
        int presentedSampleCount = 0;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            await Task.Delay(LiveValidationRetryDelay);
            if (!ProGpuWpfDiagnostics.TryGetWindowHost(this, out var liveHost) || liveHost == null)
            {
                continue;
            }

            if (!liveHost.HasPresentedFrame)
            {
                continue;
            }

            presentedSampleCount++;
            if (presentedSampleCount < 5)
            {
                continue;
            }

            Console.WriteLine("ProGPU WPF Showcase live input validation frame ready.");
            string geometryStatus = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    ShowcaseSelfTest.ValidateNativeSdkSelection(liveHost);
                    return ValidateLiveRenderSurfaceGeometryCore(liveHost, 760, 560);
                },
                DispatcherPriority.Send);
            Console.WriteLine("ProGPU WPF Showcase live input validation geometry ready.");
            string windowingStatus = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () => ValidateLiveWindowingCapabilitiesCore(liveHost),
                DispatcherPriority.Send);
            Console.WriteLine("ProGPU WPF Showcase live windowing capabilities ready.");
            string resizeStatus = await ValidateLiveNativeResizeAsync(liveHost);
            Console.WriteLine("ProGPU WPF Showcase live native resize validation ready.");
            string inputStatus;
            if (IsLiveExternalNativeDragRequested())
            {
                string nativeDragStatus = await ValidateLiveExternalNativeDragAsync(liveHost);
                string frameworkThemeStatus = await ValidateLiveFrameworkThemesAsync(liveHost);
                string popupStatus = await ValidateLivePopupSurfacesAsync(liveHost);
                inputStatus = $"{nativeDragStatus}; {frameworkThemeStatus}; {popupStatus}";
            }
            else
            {
                inputStatus = await ValidateLiveInputAsync(liveHost);
            }

            string performanceStatus = string.Empty;
            if (Environment.GetEnvironmentVariable(LivePerformanceValidationEnvironmentVariable) == "1")
            {
                performanceStatus = await ValidateLivePerformanceAsync(liveHost);
                Console.WriteLine(performanceStatus);
            }

            string successStatus = $"ProGPU WPF Showcase live input validation succeeded: {geometryStatus}.";
            string detailStatus =
                $"ProGPU WPF Showcase live input validation details: {windowingStatus}; {resizeStatus}; {inputStatus}" +
                (performanceStatus.Length == 0 ? "." : $"; {performanceStatus}.");
            Console.WriteLine(successStatus);
            Console.WriteLine(detailStatus);
            WriteLiveValidationStatus($"{successStatus}{Environment.NewLine}{detailStatus}{Environment.NewLine}");
            Console.Out.Flush();
            Environment.Exit(0);
            return;
        }

        Console.Error.WriteLine("Expected the Showcase app to present a stable ProGPU frame before live input validation.");
        Console.Error.Flush();
        Environment.Exit(1);
    }

    private static string ValidateLiveWindowingCapabilitiesCore(ProGpuWpfWindowHost liveHost)
    {
        if (!ProGpuWpfDiagnostics.TryGetWindowingCapabilities(liveHost, out var capabilities))
        {
            throw new InvalidOperationException("Expected the live Showcase host to publish typed windowing capabilities.");
        }

        if (OperatingSystem.IsLinux() &&
            capabilities.IsWaylandDesktopSession &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROGPU_WPF_LINUX_WINDOWING")))
        {
            AssertEqual(
                ProGpuWpfWindowingBackend.X11,
                capabilities.Backend,
                "Showcase live default Wayland-session backend with XWayland DISPLAY");
            AssertEqual(true, capabilities.SupportsGlobalPosition, "Showcase live XWayland global positioning");
            AssertEqual(true, capabilities.SupportsInteractiveMove, "Showcase live XWayland interactive move");
            AssertEqual(true, capabilities.SupportsNativePopupWindows, "Showcase live XWayland native popups");
            AssertEqual(false, capabilities.UsesOwnerCompositedPopups, "Showcase live XWayland owner-composited popups");
        }

        return
            $"windowing backend {capabilities.Backend}, " +
            $"wayland session {capabilities.IsWaylandDesktopSession}, " +
            $"global position {capabilities.SupportsGlobalPosition}, " +
            $"interactive move {capabilities.SupportsInteractiveMove}, " +
            $"native popups {capabilities.SupportsNativePopupWindows}, " +
            $"owner-composited popups {capabilities.UsesOwnerCompositedPopups}";
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

    private async Task<string> ValidateLiveExternalNativeDragAsync(ProGpuWpfWindowHost liveHost)
    {
        string? statusPath = Environment.GetEnvironmentVariable(LiveNativeDragStatusPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(statusPath))
        {
            return "external native drag not requested";
        }

        string? statusDirectory = Path.GetDirectoryName(statusPath);
        if (!string.IsNullOrEmpty(statusDirectory))
        {
            Directory.CreateDirectory(statusDirectory);
        }

        File.WriteAllText(statusPath, "ready");
        Console.WriteLine("ProGPU WPF Showcase external native drag ready.");
        Console.Out.Flush();

        bool completed = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            await Task.Delay(LiveValidationRetryDelay);
            if (File.Exists(statusPath) &&
                string.Equals(File.ReadAllText(statusPath).Trim(), "completed", StringComparison.Ordinal))
            {
                completed = true;
                break;
            }
        }

        if (!completed)
        {
            throw new InvalidOperationException("Expected the external native drag driver to report completion.");
        }

        int dispatcherCheckpoint = 0;
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => dispatcherCheckpoint++,
            DispatcherPriority.Background);
        AssertEqual(1, dispatcherCheckpoint, "Showcase live dispatcher checkpoint after external native drag");
        Console.WriteLine("ProGPU WPF Showcase external native drag dispatcher checkpoint passed.");
        return "external 36-step native drag returned to dispatcher processing";
    }

    private static bool IsLiveExternalNativeDragRequested()
    {
        return !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(LiveNativeDragStatusPathEnvironmentVariable));
    }

    private async Task<string> ValidateLiveNativeResizeAsync(ProGpuWpfWindowHost liveHost)
    {
        var initialLayout = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => CaptureLiveLayoutSize(liveHost),
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => SetLiveNativeWindowSize(liveHost, 900, 640),
            DispatcherPriority.Send);
        var resizedLayout = await WaitForLiveNativeResizeAsync(
            liveHost,
            requestedWidth: 900,
            requestedHeight: 640,
            description: "resized",
            layoutReady: layout =>
                layout.ContentWidth >= initialLayout.ContentWidth + 80.0 &&
                layout.ContentHeight >= initialLayout.ContentHeight + 40.0);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => SetLiveNativeWindowSize(liveHost, 760, 560),
            DispatcherPriority.Send);
        var restoredLayout = await WaitForLiveNativeResizeAsync(
            liveHost,
            requestedWidth: 760,
            requestedHeight: 560,
            description: "restored",
            layoutReady: layout =>
                layout.ContentWidth <= resizedLayout.ContentWidth - 80.0 &&
                layout.ContentHeight <= resizedLayout.ContentHeight - 40.0);

        return
            $"native resize relaid out WPF content to {resizedLayout.GeometryStatus} " +
            $"and restored {restoredLayout.GeometryStatus}";
    }

    private async Task<LiveLayoutSize> WaitForLiveNativeResizeAsync(
        ProGpuWpfWindowHost liveHost,
        uint requestedWidth,
        uint requestedHeight,
        string description,
        Func<LiveLayoutSize, bool> layoutReady)
    {
        string lastState = "not checked";
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            await Task.Delay(LiveValidationRetryDelay);
            try
            {
                var layout = await InvokeWithLiveHostWakeAsync(
                    liveHost,
                    () =>
                    {
                        var current = CaptureLiveLayoutSize(liveHost);
                        bool geometryReady = NativeResizeGeometryIsReady(
                            current.Geometry,
                            requestedWidth,
                            requestedHeight);
                        bool layoutSizeReady = layoutReady(current);
                        lastState =
                            $"{description}: {current.GeometryStatus}, " +
                            $"window actual {current.WindowWidth:0.###}x{current.WindowHeight:0.###}, " +
                            $"content actual {current.ContentWidth:0.###}x{current.ContentHeight:0.###}, " +
                            $"layoutReady={layoutSizeReady}";
                        return geometryReady && layoutSizeReady ? current : default;
                    },
                    DispatcherPriority.Send);

                if (layout.IsValid)
                {
                    return layout;
                }
            }
            catch (Exception ex)
            {
                lastState = $"{description}: {ex.Message}";
            }
        }

        throw new InvalidOperationException(
            $"Expected Showcase live native resize to reach requested client size {requestedWidth}x{requestedHeight}, but last state was: {lastState}.");
    }

    private async Task<string> ValidateLiveInputAsync(ProGpuWpfWindowHost liveHost)
    {
        TextBox? textBox = null;
        MainViewModel? viewModel = null;
        Point inputPoint = new();
        object? inputHit = null;
        string lastTargetState = "not checked";

        Console.WriteLine("ProGPU WPF Showcase live input validation locating TextBox.");
        bool sentPointerInput = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            sentPointerInput = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    Console.WriteLine("ProGPU WPF Showcase live input validation TextBox dispatcher entered.");
                    textBox = Require<TextBox>(FindName("NameTextBox"), "Showcase live input TextBox");
                    viewModel = Require<MainViewModel>(DataContext, "Showcase live input view model");

                    lastTargetState =
                        $"TextBox.IsVisible={textBox.IsVisible}, " +
                        $"TextBox.ActualSize={textBox.ActualWidth:0.###}x{textBox.ActualHeight:0.###}, " +
                        $"TextBox.IsEnabled={textBox.IsEnabled}, " +
                        $"TextBox.Focusable={textBox.Focusable}, " +
                        $"TextBox.IsHitTestVisible={textBox.IsHitTestVisible}";
                    Console.WriteLine($"ProGPU WPF Showcase live input validation TextBox state: {lastTargetState}.");
                    if (!textBox.IsVisible ||
                        textBox.ActualWidth <= 1.0 ||
                        textBox.ActualHeight <= 1.0 ||
                        !textBox.IsEnabled ||
                        !textBox.Focusable ||
                        !textBox.IsHitTestVisible)
                    {
                        return false;
                    }

                    Point center = textBox.TranslatePoint(
                        new Point(Math.Max(1.0, textBox.ActualWidth) / 2.0, Math.Max(1.0, textBox.ActualHeight) / 2.0),
                        this);
                    object? hit = InputHitTest(center);
                    lastTargetState += $", Input=({center.X:0.###}, {center.Y:0.###}), InputHitTest={DescribeInputElement(hit)}";
                    Console.WriteLine($"ProGPU WPF Showcase live input validation TextBox hit: {lastTargetState}.");
                    if (hit == null)
                    {
                        return false;
                    }

                    viewModel.NewItemName = string.Empty;
                    UpdateBinding(textBox, TextBox.TextProperty);
                    textBox.Text = string.Empty;
                    textBox.CaretIndex = 0;
                    UpdateSource(textBox, TextBox.TextProperty);

                    inputPoint = center;
                    inputHit = hit;
                    Console.WriteLine($"ProGPU WPF Showcase live input validation raising pointer at {center.X:0.###}, {center.Y:0.###}.");
                    RaiseHostInput(liveHost, WpfInputEventKind.MouseMove, x: center.X, y: center.Y);
                    RaiseHostInput(liveHost, WpfInputEventKind.MouseDown, x: center.X, y: center.Y, button: WpfMouseButton.Left);
                    RaiseHostInput(liveHost, WpfInputEventKind.MouseUp, x: center.X, y: center.Y, button: WpfMouseButton.Left);
                    Console.WriteLine("ProGPU WPF Showcase live input validation pointer raised.");
                    return true;
                },
                DispatcherPriority.Send);
            if (sentPointerInput)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!sentPointerInput)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live input target to become visible and hit-testable before injecting input, but last state was: {lastTargetState}.");
        }

        Console.WriteLine($"ProGPU WPF Showcase live input validation pointer sent: {lastTargetState}.");
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        LivePresentedFrameState textInputFrameBefore = await CaptureLivePresentedFrameStateAsync(liveHost);
        long textInputRenderWakeupsBefore = 0;
        long textInputRenderWakeupsAfter = 0;

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                if (!ReferenceEquals(Keyboard.FocusedElement, textBox))
                {
                    throw new InvalidOperationException(
                        $"Expected Showcase live host click to focus NameTextBox, but focused '{DescribeInputElement(Keyboard.FocusedElement)}'. " +
                        $"Input=({inputPoint.X:0.###}, {inputPoint.Y:0.###}), " +
                        $"InputHitTest={DescribeInputElement(inputHit)}, " +
                        $"Mouse.DirectlyOver={DescribeInputElement(Mouse.DirectlyOver)}, " +
                        $"TextBox.IsVisible={textBox?.IsVisible}, " +
                        $"TextBox.IsEnabled={textBox?.IsEnabled}, " +
                        $"TextBox.Focusable={textBox?.Focusable}, " +
                        $"TextBox.IsHitTestVisible={textBox?.IsHitTestVisible}.");
                }

                textInputRenderWakeupsBefore = ReadLiveRenderSchedulerWakeupCount(liveHost);
                foreach (char character in "Live")
                {
                    string key = char.ToUpperInvariant(character).ToString();
                    RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: key);
                    RaiseHostInput(liveHost, WpfInputEventKind.TextInput, character: character);
                    RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: key);
                }
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "Back");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "Back");
                textInputRenderWakeupsAfter = ReadLiveRenderSchedulerWakeupCount(liveHost);
                Console.WriteLine("ProGPU WPF Showcase live input validation text sent.");
            },
            DispatcherPriority.Send);
        var textInputFrameAfter = await WaitForLiveInputPresentedFrameAsync(
            liveHost,
            textInputFrameBefore,
            "TextBox keyboard input");

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live input view model after host Back key");
                AssertEqual("Liv", Require<TextBox>(textBox, "Showcase live input TextBox").Text, "Showcase live TextBox text after host Back key");
                AssertEqual("Liv", model.NewItemName, "Showcase live view-model source after host Back key");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "E");
                RaiseHostInput(liveHost, WpfInputEventKind.TextInput, character: 'e');
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "E");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        int refreshCountBeforeCommand = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live input view model before command");
                int refreshCountBefore = model.RefreshCount;
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "LeftCtrl", modifiers: WpfInputModifiers.Control);
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "R", modifiers: WpfInputModifiers.Control);
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "R", modifiers: WpfInputModifiers.Control);
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "LeftCtrl");
                Console.WriteLine("ProGPU WPF Showcase live input validation Ctrl+R sent.");
                return refreshCountBefore;
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        string textInputStatus = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live input view model");
                AssertEqual("Live", Require<TextBox>(textBox, "Showcase live input TextBox").Text, "Showcase live TextBox text after host input");
                AssertEqual("Live", model.NewItemName, "Showcase live view-model source after host input");
                AssertEqual(refreshCountBeforeCommand + 1, model.RefreshCount, "Showcase live routed KeyBinding command refresh count");
                AssertEqual(
                    $"Refresh command {refreshCountBeforeCommand + 1}",
                    Require<TextBlock>(FindName("CommandStatusText"), "Showcase live command status TextBlock").Text,
                    "Showcase live routed KeyBinding command status");
                return
                    "input TextBox focus, Back key editing, text binding, and Ctrl+R routed command updated; " +
                    $"keyboard input observed render wakeups {textInputRenderWakeupsBefore}->{textInputRenderWakeupsAfter} " +
                    $"and presented ProGPU frame scene {textInputFrameBefore.SceneChangeVersion}->{textInputFrameAfter.SceneChangeVersion}, " +
                    $"wpf {textInputFrameBefore.RetainedWpfChangeVersion}->{textInputFrameAfter.RetainedWpfChangeVersion}, " +
                    $"flat {textInputFrameBefore.FlatDrawingChangeVersion}->{textInputFrameAfter.FlatDrawingChangeVersion}";
            },
            DispatcherPriority.Send);

        string selectionStatus = await ValidateLiveTextSelectionAsync(liveHost, Require<TextBox>(textBox, "Showcase selection TextBox"));
        string controlMouseStatus = await ValidateLiveControlMouseInputAsync(liveHost);
        string mouseBindingStatus = await ValidateLiveMouseBindingAsync(liveHost);
        string discreteControlStatus = await ValidateLiveDiscreteInputControlsAsync(liveHost);
        string toolBarStatus = await ValidateLiveToolBarInputAsync(liveHost);
        string frameworkThemeStatus = await ValidateLiveFrameworkThemesAsync(liveHost);
        string popupStatus = await ValidateLivePopupSurfacesAsync(liveHost);
        string keyboardNavigationStatus = await ValidateLiveKeyboardNavigationAsync(liveHost);
        string wheelAndCaptureStatus = await ValidateLiveWheelAndCaptureInputAsync(liveHost);
        return $"{textInputStatus}; {selectionStatus}; {controlMouseStatus}; {mouseBindingStatus}; {discreteControlStatus}; {toolBarStatus}; {frameworkThemeStatus}; {popupStatus}; {keyboardNavigationStatus}; {wheelAndCaptureStatus}";
    }

    private async Task<string> ValidateLiveTextSelectionAsync(ProGpuWpfWindowHost liveHost, TextBox textBox)
    {
        var before = await CaptureLivePresentedFrameStateAsync(liveHost);
        await InvokeWithLiveHostWakeAsync(liveHost, () =>
        {
            AssertEqual("Live", textBox.Text, "Showcase selection initial text");
            RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "LeftCtrl", modifiers: WpfInputModifiers.Control);
            RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "A", modifiers: WpfInputModifiers.Control);
            RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "A", modifiers: WpfInputModifiers.Control);
            RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "LeftCtrl");
        }, DispatcherPriority.Send);
        await WaitForLiveInputPresentedFrameAsync(liveHost, before, "TextBox SelectAll highlight");

        await InvokeWithLiveHostWakeAsync(liveHost, () =>
        {
            AssertEqual("Live", textBox.SelectedText, "Showcase host SelectAll selection");
            Rect caret = textBox.GetRectFromCharacterIndex(2);
            if (caret.IsEmpty || caret.Height <= 0 || !double.IsFinite(caret.X) || !double.IsFinite(caret.Y))
                throw new InvalidOperationException("Showcase selected text did not publish actual source caret geometry.");
            Point point = textBox.TranslatePoint(new Point(caret.X + 0.05, caret.Y + caret.Height / 2), this);
            // The actual presented renderer index must reach editor content through
            // the highlight, not return the selection/caret adorner as its owner.
            if (!ProGpuWpfDiagnostics.TryHitTestOwner(liveHost, point.X, point.Y, out var owner) ||
                owner is not DependencyObject dependency ||
                (!ReferenceEquals(owner, textBox) && !textBox.IsAncestorOf(dependency)))
                throw new InvalidOperationException($"Showcase selected text input resolved outside its editor: {DescribeInputElement(owner)}.");
            RaiseHostInput(liveHost, WpfInputEventKind.MouseMove, x: point.X, y: point.Y);
            RaiseHostInput(liveHost, WpfInputEventKind.MouseDown, x: point.X, y: point.Y, button: WpfMouseButton.Left);
            RaiseHostInput(liveHost, WpfInputEventKind.MouseUp, x: point.X, y: point.Y, button: WpfMouseButton.Left);
        }, DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        before = await CaptureLivePresentedFrameStateAsync(liveHost);
        await InvokeWithLiveHostWakeAsync(liveHost, () =>
        {
            AssertEqual(0, textBox.SelectionLength, "Showcase selected-text click clears selection");
            AssertEqual(2, textBox.CaretIndex, "Showcase selected-text click source caret position");
            RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "LeftShift", modifiers: WpfInputModifiers.Shift);
            RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "End", modifiers: WpfInputModifiers.Shift);
            RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "End", modifiers: WpfInputModifiers.Shift);
            RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "LeftShift");
        }, DispatcherPriority.Send);
        await WaitForLiveInputPresentedFrameAsync(liveHost, before, "TextBox Shift+End selection");
        before = await CaptureLivePresentedFrameStateAsync(liveHost);
        await InvokeWithLiveHostWakeAsync(liveHost, () =>
        {
            AssertEqual(2, textBox.SelectionStart, "Showcase keyboard selection source start");
            AssertEqual("ve", textBox.SelectedText, "Showcase keyboard selection source range");
            RaiseHostInput(liveHost, WpfInputEventKind.TextInput, character: 'v');
            RaiseHostInput(liveHost, WpfInputEventKind.TextInput, character: 'e');
        }, DispatcherPriority.Send);
        await WaitForLiveInputPresentedFrameAsync(liveHost, before, "TextBox selected-text replacement");
        await InvokeWithLiveHostWakeAsync(liveHost, () =>
        {
            AssertEqual("Live", textBox.Text, "Showcase replacement retains unselected prefix");
            AssertEqual(0, textBox.SelectionLength, "Showcase replacement clears selection");
            AssertEqual(4, textBox.CaretIndex, "Showcase replacement caret position");
            AssertEqual("Live", Require<MainViewModel>(DataContext, "Showcase selection model").NewItemName,
                "Showcase selected-text replacement binding");
        }, DispatcherPriority.Send);
        return "input SelectAll, click-through selection, source caret, Shift+End and replacement presented";
    }

    private async Task<string> ValidateLiveControlMouseInputAsync(ProGpuWpfWindowHost liveHost)
    {
        Button? addItemButton = null;
        CheckBox? enabledCheckBox = null;
        TextBox? textBox = null;
        MainViewModel? viewModel = null;
        int itemCountBeforeAdd = 0;
        string lastTargetState = "not checked";

        bool sentAddClick = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            sentAddClick = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    var tabControl = Require<TabControl>(FindName("ShowcaseTabControl"), "Showcase live controls TabControl");
                    tabControl.SelectedIndex = 0;
                    UpdateLayout();

                    addItemButton = Require<Button>(FindName("AddItemButton"), "Showcase live Add item Button");
                    enabledCheckBox = Require<CheckBox>(FindName("EnabledCheckBox"), "Showcase live Actions CheckBox");
                    textBox = Require<TextBox>(FindName("NameTextBox"), "Showcase live Add item TextBox");
                    viewModel = Require<MainViewModel>(DataContext, "Showcase live controls view model");

                    viewModel.ActionsEnabled = true;
                    viewModel.SelectedCategory = "Input";
                    viewModel.NewItemName = "MouseAdded";
                    UpdateBinding(textBox, TextBox.TextProperty);
                    UpdateBinding(enabledCheckBox, ToggleButton.IsCheckedProperty);
                    UpdateLayout();

                    itemCountBeforeAdd = viewModel.Items.Count;
                    return TryRaiseLiveMouseClick(
                        liveHost,
                        Require<Button>(addItemButton, "Showcase live Add item Button"),
                        "AddItemButton",
                        out lastTargetState);
                },
                DispatcherPriority.Send);
            if (sentAddClick)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!sentAddClick)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live Add item Button to become clickable before injecting input, but last state was: {lastTargetState}.");
        }

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live controls view model after Add click");
                AssertEqual(itemCountBeforeAdd + 1, model.Items.Count, "Showcase live Add item Button command item count");
                AssertEqual("MouseAdded", model.SelectedItem?.Name, "Showcase live Add item Button command selected name");
                AssertEqual("Input", model.SelectedItem?.Category, "Showcase live Add item Button command selected category");
                AssertEqual(true, model.ActionsEnabled, "Showcase live controls actions before CheckBox click");

                if (!TryRaiseLiveMouseClick(liveHost, Require<CheckBox>(enabledCheckBox, "Showcase live Actions CheckBox"), "EnabledCheckBox", out lastTargetState))
                {
                    throw new InvalidOperationException(
                        $"Expected Showcase live Actions CheckBox to be clickable before disabling actions, but state was: {lastTargetState}.");
                }
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live controls view model after CheckBox disable click");
                var checkBox = Require<CheckBox>(enabledCheckBox, "Showcase live Actions CheckBox after disable click");
                AssertEqual(false, model.ActionsEnabled, "Showcase live Actions CheckBox disabled view-model state");
                AssertEqual(false, checkBox.IsChecked == true, "Showcase live Actions CheckBox disabled checked state");

                if (!TryRaiseLiveMouseClick(liveHost, checkBox, "EnabledCheckBox", out lastTargetState))
                {
                    throw new InvalidOperationException(
                        $"Expected Showcase live Actions CheckBox to be clickable before reenabling actions, but state was: {lastTargetState}.");
                }
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        return await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live controls view model after CheckBox enable click");
                var checkBox = Require<CheckBox>(enabledCheckBox, "Showcase live Actions CheckBox after enable click");
                AssertEqual(true, model.ActionsEnabled, "Showcase live Actions CheckBox restored view-model state");
                AssertEqual(true, checkBox.IsChecked == true, "Showcase live Actions CheckBox restored checked state");
                return "Add item Button and Actions CheckBox mouse clicks updated WPF command/binding state";
            },
            DispatcherPriority.Send);
    }

    private async Task<string> ValidateLiveDiscreteInputControlsAsync(ProGpuWpfWindowHost liveHost)
    {
        RadioButton? frameworkRadioButton = null;
        RadioButton? renderingRadioButton = null;
        RepeatButton? inputRepeatButton = null;
        MainViewModel? viewModel = null;
        int radioEventsBefore = 0;
        int repeatClicksBefore = 0;
        string lastTargetState = "not checked";

        LivePresentedFrameState inputTabFrameBefore = await CaptureLivePresentedFrameStateAsync(liveHost);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var tabControl = Require<TabControl>(FindName("ShowcaseTabControl"), "Showcase live discrete controls TabControl");
                tabControl.SelectedIndex = 4;
                UpdateLayout();

                frameworkRadioButton = Require<RadioButton>(
                    FindName("FrameworkRadioButton"),
                    "Showcase live Framework RadioButton");
                renderingRadioButton = Require<RadioButton>(
                    FindName("RenderingRadioButton"),
                    "Showcase live Rendering RadioButton");
                inputRepeatButton = Require<RepeatButton>(
                    FindName("InputRepeatButton"),
                    "Showcase live input RepeatButton");
                viewModel = Require<MainViewModel>(DataContext, "Showcase live discrete controls view model");

                frameworkRadioButton.IsChecked = true;
                UpdateLayout();
                radioEventsBefore = CategoryRadioCheckedCount;
                repeatClicksBefore = InputRepeatButtonClickCount;
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await WaitForLiveInputPresentedFrameAsync(
            liveHost,
            inputTabFrameBefore,
            "discrete input controls tab activation");

        bool sentRenderingClick = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            sentRenderingClick = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    return TryRaiseLiveMouseClick(
                        liveHost,
                        Require<RadioButton>(renderingRadioButton, "Showcase live Rendering RadioButton before host click"),
                        "RenderingRadioButton",
                        out lastTargetState);
                },
                DispatcherPriority.Send);
            if (sentRenderingClick)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!sentRenderingClick)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live Rendering RadioButton to become clickable before injecting input, but last state was: {lastTargetState}.");
        }

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live discrete controls view model after Rendering click");
                AssertEqual(false, Require<RadioButton>(frameworkRadioButton, "Showcase live Framework RadioButton after Rendering click").IsChecked == true, "Showcase live Framework RadioButton unchecked by host click");
                AssertEqual(true, Require<RadioButton>(renderingRadioButton, "Showcase live Rendering RadioButton after host click").IsChecked == true, "Showcase live Rendering RadioButton checked by host click");
                AssertEqual("RenderingRadioButton", LastCategoryRadioName, "Showcase live Rendering RadioButton checked sender");
                AssertEqual("Rendering", model.SelectedCategory, "Showcase live Rendering RadioButton selected category");
                AssertLiveGreaterThan(radioEventsBefore, CategoryRadioCheckedCount, "Showcase live RadioButton checked event count after Rendering click");

                if (!TryRaiseLiveMouseClick(
                    liveHost,
                    Require<RadioButton>(frameworkRadioButton, "Showcase live Framework RadioButton before restore click"),
                    "FrameworkRadioButton",
                    out lastTargetState))
                {
                    throw new InvalidOperationException(
                        $"Expected Showcase live Framework RadioButton to be clickable before restoring category, but state was: {lastTargetState}.");
                }
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live discrete controls view model after Framework click");
                AssertEqual(true, Require<RadioButton>(frameworkRadioButton, "Showcase live Framework RadioButton after restore click").IsChecked == true, "Showcase live Framework RadioButton restored by host click");
                AssertEqual(false, Require<RadioButton>(renderingRadioButton, "Showcase live Rendering RadioButton after restore click").IsChecked == true, "Showcase live Rendering RadioButton unchecked by restore click");
                AssertEqual("FrameworkRadioButton", LastCategoryRadioName, "Showcase live Framework RadioButton checked sender");
                AssertEqual("Framework", model.SelectedCategory, "Showcase live Framework RadioButton selected category");

                if (!TryRaiseLiveMouseClick(
                    liveHost,
                    Require<RepeatButton>(inputRepeatButton, "Showcase live input RepeatButton before host click"),
                    "InputRepeatButton",
                    out lastTargetState))
                {
                    throw new InvalidOperationException(
                        $"Expected Showcase live input RepeatButton to be clickable before injecting input, but state was: {lastTargetState}.");
                }
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        return await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(
                    repeatClicksBefore + 1,
                    InputRepeatButtonClickCount,
                    "Showcase live RepeatButton host click count");
                return "RadioButton group selection and RepeatButton click updated through host mouse input";
            },
            DispatcherPriority.Send);
    }

    private async Task<string> ValidateLiveToolBarInputAsync(ProGpuWpfWindowHost liveHost)
    {
        ToolBar? toolBar = null;
        Button? refreshButton = null;
        ToggleButton? toolBarToggleButton = null;
        MainViewModel? viewModel = null;
        int refreshCountBefore = 0;
        int uncheckedEventsBefore = 0;
        int checkedEventsBefore = 0;
        string lastTargetState = "not checked";

        bool sentRefreshClick = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            sentRefreshClick = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    var tabControl = Require<TabControl>(FindName("ShowcaseTabControl"), "Showcase live ToolBar TabControl");
                    tabControl.SelectedIndex = 4;
                    UpdateLayout();

                    toolBar = Require<ToolBar>(FindName("ShowcaseToolBar"), "Showcase live ToolBar");
                    refreshButton = Require<Button>(
                        FindName("ToolBarRefreshButton"),
                        "Showcase live ToolBar refresh Button");
                    toolBarToggleButton = Require<ToggleButton>(
                        FindName("ToolBarToggleButton"),
                        "Showcase live ToolBar ToggleButton");
                    viewModel = Require<MainViewModel>(DataContext, "Showcase live ToolBar view model");

                    viewModel.ActionsEnabled = true;
                    UpdateBinding(toolBarToggleButton, ToggleButton.IsCheckedProperty);
                    UpdateLayout();

                    refreshCountBefore = viewModel.RefreshCount;
                    uncheckedEventsBefore = InputToggleUncheckedCount;
                    checkedEventsBefore = InputToggleCheckedCount;
                    return TryRaiseLiveMouseClick(
                        liveHost,
                        refreshButton,
                        "ToolBarRefreshButton",
                        out lastTargetState);
                },
                DispatcherPriority.Send);
            if (sentRefreshClick)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!sentRefreshClick)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live ToolBar refresh Button to become clickable before injecting input, but last state was: {lastTargetState}.");
        }

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live ToolBar view model after refresh click");
                AssertEqual(3, Require<ToolBar>(toolBar, "Showcase live ToolBar after refresh click").Items.Count, "Showcase live ToolBar item count");
                AssertEqual(refreshCountBefore + 1, model.RefreshCount, "Showcase live ToolBar refresh command count");
                AssertEqual(
                    $"Refresh command {refreshCountBefore + 1}",
                    Require<TextBlock>(FindName("CommandStatusText"), "Showcase live ToolBar command status TextBlock").Text,
                    "Showcase live ToolBar refresh command status");

                if (!TryRaiseLiveMouseClick(
                    liveHost,
                    Require<ToggleButton>(toolBarToggleButton, "Showcase live ToolBar ToggleButton before disable click"),
                    "ToolBarToggleButton",
                    out lastTargetState))
                {
                    throw new InvalidOperationException(
                        $"Expected Showcase live ToolBar ToggleButton to be clickable before disabling actions, but state was: {lastTargetState}.");
                }
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live ToolBar view model after disable click");
                var toggle = Require<ToggleButton>(toolBarToggleButton, "Showcase live ToolBar ToggleButton after disable click");
                AssertEqual(false, model.ActionsEnabled, "Showcase live ToolBar ToggleButton disabled view-model state");
                AssertEqual(false, toggle.IsChecked == true, "Showcase live ToolBar ToggleButton disabled checked state");
                AssertLiveGreaterThan(uncheckedEventsBefore, InputToggleUncheckedCount, "Showcase live ToolBar ToggleButton unchecked event count");

                if (!TryRaiseLiveMouseClick(
                    liveHost,
                    toggle,
                    "ToolBarToggleButton",
                    out lastTargetState))
                {
                    throw new InvalidOperationException(
                        $"Expected Showcase live ToolBar ToggleButton to be clickable before restoring actions, but state was: {lastTargetState}.");
                }
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        return await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live ToolBar view model after restore click");
                var toggle = Require<ToggleButton>(toolBarToggleButton, "Showcase live ToolBar ToggleButton after restore click");
                AssertEqual(true, model.ActionsEnabled, "Showcase live ToolBar ToggleButton restored view-model state");
                AssertEqual(true, toggle.IsChecked == true, "Showcase live ToolBar ToggleButton restored checked state");
                AssertLiveGreaterThan(checkedEventsBefore, InputToggleCheckedCount, "Showcase live ToolBar ToggleButton checked event count");
                return "ToolBar refresh command and toggle binding updated through host mouse input";
            },
            DispatcherPriority.Send);
    }

    private async Task<string> ValidateLivePopupSurfacesAsync(ProGpuWpfWindowHost liveHost)
    {
        await CloseLivePopupSurfacesAsync(liveHost);
        await WaitForLivePopupLayerChildCountAsync(
            liveHost,
            expectedPopupChildren: 0,
            exact: true,
            "initial closed popup layer");

        try
        {
            var menuSnapshot = await ValidateLiveMenuPopupSurfaceAsync(liveHost);
            var comboSnapshot = await ValidateLiveComboBoxPopupSurfaceAsync(liveHost);
            var directPopupSnapshot = await ValidateLiveDirectPopupSurfaceAsync(liveHost);
            return
                "Menu, ComboBox dropdown, and direct Popup opened through ProGPU popup surfaces " +
                $"(retained children {menuSnapshot.Composition.PopupLayerChildCount}/" +
                $"{comboSnapshot.Composition.PopupLayerChildCount}/{directPopupSnapshot.Composition.PopupLayerChildCount}; " +
                $"native windows {menuSnapshot.Portable.NativeWindowCount}/" +
                $"{comboSnapshot.Portable.NativeWindowCount}/{directPopupSnapshot.Portable.NativeWindowCount})";
        }
        finally
        {
            await CloseLivePopupSurfacesAsync(liveHost);
        }
    }

    private async Task<string> ValidateLiveFrameworkThemesAsync(ProGpuWpfWindowHost liveHost)
    {
        await CloseLivePopupSurfacesAsync(liveHost);
        var validatedThemes = new List<string>(s_frameworkThemes.Length);
        bool allMenusUsedNativeWindows = true;
        for (int i = 0; i < s_frameworkThemes.Length; i++)
        {
            FrameworkThemeDefinition theme = s_frameworkThemes[i];
            await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    var themeItem = Require<MenuItem>(
                        FindName($"{theme.Name}ThemeMenuItem"),
                        $"Showcase live {theme.Name} theme MenuItem");
                    themeItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, themeItem));
                    UpdateLayout();
                    AssertEqual(theme.Name, ActiveFrameworkThemeName, $"Showcase live active {theme.Name} framework theme");
                    AssertEqual(true, themeItem.IsChecked, $"Showcase live checked {theme.Name} framework theme item");
                    AssertEqual(
                        theme.Source,
                        _activeFrameworkThemeDictionary?.Source?.OriginalString,
                        $"Showcase live {theme.Name} framework theme source");

                    var menu = Require<Menu>(FindName("MainMenu"), $"Showcase live {theme.Name} main Menu");
                    var fileMenuItem = Require<MenuItem>(FindName("FileMenuItem"), $"Showcase live {theme.Name} File MenuItem");
                    var comboBox = Require<ComboBox>(FindName("SelectedValueComboBox"), $"Showcase live {theme.Name} ComboBox");
                    menu.ApplyTemplate();
                    fileMenuItem.ApplyTemplate();
                    comboBox.ApplyTemplate();
                    AssertEqual(true, menu.Template != null, $"Showcase live {theme.Name} Menu template available");
                    AssertEqual(true, fileMenuItem.Template != null, $"Showcase live {theme.Name} MenuItem template available");
                    AssertEqual(true, comboBox.Template != null, $"Showcase live {theme.Name} ComboBox template available");
                    fileMenuItem.IsSubmenuOpen = true;
                    WakeLiveRenderHost(liveHost);
                },
                DispatcherPriority.Send);

            LivePopupSurfaceSnapshot snapshot = await WaitForLivePopupLayerChildCountAsync(
                liveHost,
                expectedPopupChildren: 1,
                exact: false,
                $"{theme.Name} File menu popup layer");
            bool usesNativeWindow = snapshot.Portable.NativeWindowCount >= 1;
            bool hasPopupLayerContent = snapshot.Composition.PopupLayerChildCount >= 1;
            AssertEqual(
                true,
                usesNativeWindow || hasPopupLayerContent,
                $"Showcase live {theme.Name} menu popup presentation");
            if (OperatingSystem.IsMacOS())
            {
                AssertEqual(true, usesNativeWindow, $"Showcase live {theme.Name} macOS native menu popup count");
            }

            allMenusUsedNativeWindows &= usesNativeWindow;
            validatedThemes.Add(theme.Name);
            await CloseLivePopupSurfacesAsync(liveHost);
        }

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                ApplyFrameworkTheme("Fluent");
                UpdateLayout();
                WakeLiveRenderHost(liveHost);
            },
            DispatcherPriority.Send);
        string popupMode = allMenusUsedNativeWindows
            ? "native menu popups"
            : "owner-surface menu popups";
        return $"runtime framework themes switched and rendered {popupMode}: {string.Join(", ", validatedThemes)}";
    }

    private async Task<LivePopupSurfaceSnapshot> ValidateLiveMenuPopupSurfaceAsync(
        ProGpuWpfWindowHost liveHost)
    {
        bool opened = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var fileMenuItem = Require<MenuItem>(FindName("FileMenuItem"), "Showcase live File MenuItem");
                fileMenuItem.IsSubmenuOpen = true;
                UpdateLayout();
                WakeLiveRenderHost(liveHost);
                return fileMenuItem.IsSubmenuOpen;
            },
            DispatcherPriority.Send);
        if (!opened)
        {
            throw new InvalidOperationException("Expected Showcase live File menu popup to open.");
        }

        var snapshot = await WaitForLivePopupLayerChildCountAsync(
            liveHost,
            expectedPopupChildren: 1,
            exact: false,
            "File menu popup layer");
        bool usesNativePopup = snapshot.Portable.NativeWindowCount >= 1;

        Point addItemCenter = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => GetLivePopupInputCenter(
                Require<MenuItem>(FindName("AddMenuItem"), "Showcase live Add MenuItem"),
                "Showcase live Add MenuItem",
                usesNativePopup),
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () => RaiseLivePopupInput(
                liveHost,
                WpfInputEventKind.MouseMove,
                addItemCenter.X,
                addItemCenter.Y,
                usesNativePopup),
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        int itemCountBefore = await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var fileMenuItem = Require<MenuItem>(FindName("FileMenuItem"), "Showcase live File MenuItem after popup pointer transfer");
                AssertEqual(true, fileMenuItem.IsSubmenuOpen, "Showcase live File menu remains open after popup pointer transfer");
                return Require<MainViewModel>(DataContext, "Showcase live menu view model before popup click").Items.Count;
            },
            DispatcherPriority.Send);
        bool observedMenuCommand = false;
        string lastMenuCommandState = "not checked";
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    var model = Require<MainViewModel>(DataContext, "Showcase live menu view model before popup click attempt");
                    var fileMenuItem = Require<MenuItem>(FindName("FileMenuItem"), "Showcase live File MenuItem before popup click attempt");
                    lastMenuCommandState = $"items={model.Items.Count}, menuOpen={fileMenuItem.IsSubmenuOpen}";
                    if (model.Items.Count != itemCountBefore || !fileMenuItem.IsSubmenuOpen)
                    {
                        return;
                    }

                    addItemCenter = GetLivePopupInputCenter(
                        Require<MenuItem>(FindName("AddMenuItem"), "Showcase live Add MenuItem click attempt"),
                        "Showcase live Add MenuItem",
                        usesNativePopup);
                    RaiseLivePopupInput(
                        liveHost,
                        WpfInputEventKind.MouseMove,
                        addItemCenter.X,
                        addItemCenter.Y,
                        usesNativePopup);
                    RaiseLivePopupInput(
                        liveHost,
                        WpfInputEventKind.MouseDown,
                        addItemCenter.X,
                        addItemCenter.Y,
                        usesNativePopup,
                        WpfMouseButton.Left);
                    RaiseLivePopupInput(
                        liveHost,
                        WpfInputEventKind.MouseUp,
                        addItemCenter.X,
                        addItemCenter.Y,
                        usesNativePopup,
                        WpfMouseButton.Left);
                },
                DispatcherPriority.Send);
            await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
            observedMenuCommand = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    var model = Require<MainViewModel>(DataContext, "Showcase live menu view model while awaiting popup click");
                    return model.Items.Count == itemCountBefore + 1 &&
                        !Require<MenuItem>(FindName("FileMenuItem"), "Showcase live File MenuItem while awaiting popup click").IsSubmenuOpen;
                },
                DispatcherPriority.Send);
            if (observedMenuCommand)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!observedMenuCommand)
        {
            throw new InvalidOperationException(
                $"Expected the Showcase live Add MenuItem popup command and close transition to complete before timeout; last state was {lastMenuCommandState}.");
        }

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(DataContext, "Showcase live menu view model after popup click");
                AssertEqual(itemCountBefore + 1, model.Items.Count, "Showcase live Add MenuItem popup click item count");
                AssertEqual(
                    false,
                    Require<MenuItem>(FindName("FileMenuItem"), "Showcase live File MenuItem after popup click").IsSubmenuOpen,
                    "Showcase live File menu closes after popup item click");
            },
            DispatcherPriority.Send);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                Require<MenuItem>(FindName("FileMenuItem"), "Showcase live File MenuItem after popup validation").IsSubmenuOpen = false;
                UpdateLayout();
                WakeLiveRenderHost(liveHost);
            },
            DispatcherPriority.Send);
        await WaitForLivePopupLayerChildCountAsync(
            liveHost,
            expectedPopupChildren: 0,
            exact: true,
            "closed File menu popup layer");
        return snapshot;
    }

    private Point GetLivePopupInputCenter(
        FrameworkElement target,
        string description,
        bool usesNativePopup)
    {
        if (!target.IsVisible ||
            target.ActualWidth <= 1.0 ||
            target.ActualHeight <= 1.0 ||
            !target.IsEnabled ||
            !target.IsHitTestVisible)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be an interactive popup element, but state was " +
                $"IsVisible={target.IsVisible}, ActualSize={target.ActualWidth:0.###}x{target.ActualHeight:0.###}, " +
                $"IsEnabled={target.IsEnabled}, IsHitTestVisible={target.IsHitTestVisible}.");
        }

        PresentationSource source = PresentationSource.FromVisual(target)
            ?? throw new InvalidOperationException($"Expected {description} to have a popup presentation source.");
        if (source.RootVisual is not UIElement root)
        {
            throw new InvalidOperationException($"Expected {description} popup presentation source to expose a UIElement root.");
        }

        Point localCenter = target.TranslatePoint(
            new Point(target.ActualWidth / 2.0, target.ActualHeight / 2.0),
            root);
        if (usesNativePopup)
        {
            // Native popup diagnostics accept popup-local coordinates and convert
            // them through the bridge's settled logical origin. This avoids stale
            // owner/screen geometry while a transient Cocoa window is positioning.
            return localCenter;
        }

        // Owner-composited popups share the owner input surface.
        Point screenCenter = target.PointToScreen(
            new Point(target.ActualWidth / 2.0, target.ActualHeight / 2.0));
        Point ownerScreenOrigin = PointToScreen(new Point(0.0, 0.0));
        return new Point(
            screenCenter.X - ownerScreenOrigin.X,
            screenCenter.Y - ownerScreenOrigin.Y);
    }

    private static void RaiseLivePopupInput(
        ProGpuWpfWindowHost liveHost,
        WpfInputEventKind kind,
        double x,
        double y,
        bool usesNativePopup,
        WpfMouseButton button = WpfMouseButton.None)
    {
        if (!usesNativePopup)
        {
            RaiseHostInput(liveHost, kind, x: x, y: y, button: button);
            return;
        }

        var input = new WpfInputEventArgs(kind, x: x, y: y, button: button);
        if (!ProGpuWpfDiagnostics.TryRaiseTopmostNativePopupLocalInput(liveHost, input))
        {
            throw new InvalidOperationException(
                $"Expected a visible native popup for {kind} input at ({x:0.###}, {y:0.###}).");
        }
    }

    private async Task<LivePopupSurfaceSnapshot> ValidateLiveComboBoxPopupSurfaceAsync(
        ProGpuWpfWindowHost liveHost)
    {
        string lastState = "not checked";
        bool opened = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            opened = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    var tabControl = Require<TabControl>(FindName("ShowcaseTabControl"), "Showcase live popup TabControl");
                    tabControl.SelectedIndex = 3;
                    UpdateLayout();

                    var comboBox = Require<ComboBox>(FindName("SelectedValueComboBox"), "Showcase live SelectedValue ComboBox");
                    comboBox.ApplyTemplate();
                    UpdateLayout();
                    lastState =
                        $"IsLoaded={comboBox.IsLoaded}, " +
                        $"IsVisible={comboBox.IsVisible}, " +
                        $"ActualSize={comboBox.ActualWidth:0.###}x{comboBox.ActualHeight:0.###}, " +
                        $"IsEnabled={comboBox.IsEnabled}, " +
                        $"IsHitTestVisible={comboBox.IsHitTestVisible}";
                    if (!comboBox.IsLoaded ||
                        !comboBox.IsVisible ||
                        comboBox.ActualWidth <= 1.0 ||
                        comboBox.ActualHeight <= 1.0 ||
                        !comboBox.IsEnabled ||
                        !comboBox.IsHitTestVisible)
                    {
                        WakeLiveRenderHost(liveHost);
                        return false;
                    }

                    comboBox.Focus();
                    comboBox.IsDropDownOpen = true;
                    UpdateLayout();
                    WakeLiveRenderHost(liveHost);
                    lastState += $", IsDropDownOpen={comboBox.IsDropDownOpen}";
                    return comboBox.IsDropDownOpen;
                },
                DispatcherPriority.Send);
            if (opened)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!opened)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live SelectedValue ComboBox dropdown to open, but last state was: {lastState}.");
        }

        var snapshot = await WaitForLivePopupLayerChildCountAsync(
            liveHost,
            expectedPopupChildren: 1,
            exact: false,
            "ComboBox dropdown popup layer");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                Require<ComboBox>(FindName("SelectedValueComboBox"), "Showcase live SelectedValue ComboBox after popup validation").IsDropDownOpen = false;
                UpdateLayout();
                WakeLiveRenderHost(liveHost);
            },
            DispatcherPriority.Send);
        await WaitForLivePopupLayerChildCountAsync(
            liveHost,
            expectedPopupChildren: 0,
            exact: true,
            "closed ComboBox dropdown popup layer");
        return snapshot;
    }

    private async Task<LivePopupSurfaceSnapshot> ValidateLiveDirectPopupSurfaceAsync(
        ProGpuWpfWindowHost liveHost)
    {
        string lastState = "not checked";
        bool opened = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            opened = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    var tabControl = Require<TabControl>(FindName("ShowcaseTabControl"), "Showcase live direct Popup TabControl");
                    tabControl.SelectedIndex = 4;
                    UpdateLayout();

                    var owner = Require<Button>(FindName("PopupOwnerButton"), "Showcase live direct Popup owner");
                    var popup = Require<Popup>(FindName("InputPopup"), "Showcase live direct Popup");
                    lastState =
                        $"Owner.IsLoaded={owner.IsLoaded}, " +
                        $"Owner.IsVisible={owner.IsVisible}, " +
                        $"Owner.ActualSize={owner.ActualWidth:0.###}x{owner.ActualHeight:0.###}, " +
                        $"Owner.IsEnabled={owner.IsEnabled}, " +
                        $"Owner.IsHitTestVisible={owner.IsHitTestVisible}";
                    if (!owner.IsLoaded ||
                        !owner.IsVisible ||
                        owner.ActualWidth <= 1.0 ||
                        owner.ActualHeight <= 1.0 ||
                        !owner.IsEnabled ||
                        !owner.IsHitTestVisible)
                    {
                        WakeLiveRenderHost(liveHost);
                        return false;
                    }

                    popup.IsOpen = true;
                    UpdateLayout();
                    WakeLiveRenderHost(liveHost);
                    lastState += $", Popup.IsOpen={popup.IsOpen}";
                    return popup.IsOpen;
                },
                DispatcherPriority.Send);
            if (opened)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!opened)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live direct Popup to open, but last state was: {lastState}.");
        }

        var snapshot = await WaitForLivePopupLayerChildCountAsync(
            liveHost,
            expectedPopupChildren: 1,
            exact: false,
            "direct Popup layer");
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                Require<Popup>(FindName("InputPopup"), "Showcase live direct Popup after popup validation").IsOpen = false;
                UpdateLayout();
                WakeLiveRenderHost(liveHost);
            },
            DispatcherPriority.Send);
        await WaitForLivePopupLayerChildCountAsync(
            liveHost,
            expectedPopupChildren: 0,
            exact: true,
            "closed direct Popup layer");
        return snapshot;
    }

    private async Task CloseLivePopupSurfacesAsync(ProGpuWpfWindowHost liveHost)
    {
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                if (FindName("FileMenuItem") is MenuItem fileMenuItem)
                {
                    fileMenuItem.IsSubmenuOpen = false;
                }

                if (FindName("SelectedValueComboBox") is ComboBox comboBox)
                {
                    comboBox.IsDropDownOpen = false;
                }

                if (FindName("InputPopup") is Popup popup)
                {
                    popup.IsOpen = false;
                }

                UpdateLayout();
                WakeLiveRenderHost(liveHost);
            },
            DispatcherPriority.Send);
    }

    private async Task<LivePopupSurfaceSnapshot> WaitForLivePopupLayerChildCountAsync(
        ProGpuWpfWindowHost liveHost,
        int expectedPopupChildren,
        bool exact,
        string description)
    {
        string lastState = "not checked";
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            await Task.Delay(LiveValidationRetryDelay);
            var snapshot = await InvokeWithLiveNativeLoopWakeAsync(
                liveHost,
                () =>
                {
                    if (!ProGpuWpfDiagnostics.TryGetCompositionLayerSnapshot(liveHost, out var composition))
                    {
                        lastState = $"{description}: no composition target";
                        return default;
                    }

                    bool hasPortableSnapshot = ProGpuWpfDiagnostics.TryGetPortablePopupSnapshot(
                        liveHost,
                        out var portable);

                    lastState =
                        $"{description}: scene children {composition.SceneRootChildCount}, " +
                        $"layer order retained={composition.RetainedLayerIndex}, flat={composition.FlatLayerIndex}, popup={composition.PopupLayerIndex}, " +
                        $"popup children {composition.PopupLayerChildCount}, retained children {composition.RetainedLayerChildCount}, " +
                        $"portable={hasPortableSnapshot}, open={portable.OpenCount}, visible={portable.VisibleCount}, " +
                        $"native={portable.NativeWindowCount}, presented={portable.PresentedNativeWindowCount}, " +
                        $"gpuHitTests={portable.NativeWindowGpuHitTestCount}, gpuOwners={portable.NativeWindowGpuHitTestOwnerCount}";
                    bool isReady = IsLivePopupSurfaceSnapshotReady(
                        composition,
                        hasPortableSnapshot,
                        portable,
                        expectedPopupChildren,
                        exact);
                    return new LivePopupSurfaceSnapshot(
                        isReady,
                        hasPortableSnapshot,
                        composition,
                        portable);
                },
                DispatcherPriority.Background);

            if (snapshot.IsReady)
            {
                return snapshot;
            }
        }

        string expectedText = exact
            ? $"exactly {expectedPopupChildren}"
            : $"at least {expectedPopupChildren}";
        throw new InvalidOperationException(
            $"Expected Showcase live {description} to present {expectedText} retained popup child visual(s) " +
            $"or an equivalent native popup surface, but last state was: {lastState}.");
    }

    private static bool IsLivePopupSurfaceSnapshotReady(
        ProGpuWpfDiagnostics.CompositionLayerSnapshot composition,
        bool hasPortableSnapshot,
        ProGpuWpfDiagnostics.PortablePopupSnapshot portable,
        int expectedPopupChildren,
        bool exact)
    {
        if (!composition.HasCompositionTarget ||
            composition.SceneRootChildCount < 3 ||
            composition.RetainedLayerIndex < 0 ||
            composition.FlatLayerIndex < 0 ||
            composition.PopupLayerIndex <= composition.FlatLayerIndex ||
            !hasPortableSnapshot)
        {
            return false;
        }

        if (exact && expectedPopupChildren == 0)
        {
            return composition.PopupLayerChildCount == 0 &&
                portable.VisibleCount == 0 &&
                portable.NativeWindowCount == 0 &&
                portable.PresentedNativeWindowCount == 0 &&
                portable.NativeWindowGpuHitTestCount == 0 &&
                portable.NativeWindowGpuHitTestOwnerCount == 0;
        }

        bool retainedReady = exact
            ? composition.PopupLayerChildCount == expectedPopupChildren
            : composition.PopupLayerChildCount >= expectedPopupChildren;
        bool nativeReady = portable.VisibleCount >= expectedPopupChildren &&
            portable.NativeWindowCount >= expectedPopupChildren &&
            portable.PresentedNativeWindowCount >= expectedPopupChildren &&
            portable.NativeWindowGpuHitTestCount >= expectedPopupChildren &&
            portable.NativeWindowGpuHitTestOwnerCount >= expectedPopupChildren;
        bool usesNativePopup = portable.VisibleCount > 0 || portable.NativeWindowCount > 0;
        return usesNativePopup ? nativeReady : retainedReady;
    }

    private async Task<string> ValidateLiveMouseBindingAsync(ProGpuWpfWindowHost liveHost)
    {
        TextBlock? titleText = null;
        MainViewModel? viewModel = null;
        int refreshCountBefore = 0;
        var mouseBindingTrace = new List<string>();
        string lastTargetState = "not checked";

        bool sentDoubleClick = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            sentDoubleClick = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    var tabControl = Require<TabControl>(FindName("ShowcaseTabControl"), "Showcase live mouse-binding TabControl");
                    tabControl.SelectedIndex = 0;
                    UpdateLayout();

                    titleText = Require<TextBlock>(FindName("ShowcaseTitleText"), "Showcase live MouseBinding title TextBlock");
                    viewModel = Require<MainViewModel>(DataContext, "Showcase live MouseBinding view model");
                    refreshCountBefore = viewModel.RefreshCount;

                    FrameworkElement target = titleText;
                    MouseButtonEventHandler previewHandler = (sender, args) =>
                        mouseBindingTrace.Add(DescribeLiveMouseBindingEvent("preview", sender, args));
                    MouseButtonEventHandler bubbleHandler = (sender, args) =>
                        mouseBindingTrace.Add(DescribeLiveMouseBindingEvent("bubble", sender, args));
                    AddHandler(Mouse.PreviewMouseDownEvent, previewHandler, handledEventsToo: true);
                    AddHandler(Mouse.MouseDownEvent, bubbleHandler, handledEventsToo: true);
                    try
                    {
                        if (!TryRaiseLiveMouseClick(liveHost, target, "ShowcaseTitleText", out lastTargetState))
                        {
                            return false;
                        }

                        return TryRaiseLiveMouseClick(liveHost, target, "ShowcaseTitleText", out lastTargetState);
                    }
                    finally
                    {
                        RemoveHandler(Mouse.PreviewMouseDownEvent, previewHandler);
                        RemoveHandler(Mouse.MouseDownEvent, bubbleHandler);
                    }
                },
                DispatcherPriority.Send);
            if (sentDoubleClick)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!sentDoubleClick)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live title TextBlock to become double-clickable before injecting MouseBinding input, but last state was: {lastTargetState}.");
        }

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        return await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var model = Require<MainViewModel>(viewModel, "Showcase live MouseBinding view model after double-click");
                if (model.RefreshCount != refreshCountBefore + 1)
                {
                    string trace = mouseBindingTrace.Count == 0
                        ? "no mouse-down routed events recorded"
                        : string.Join(" | ", mouseBindingTrace);
                    throw new InvalidOperationException(
                        $"Expected Showcase live routed MouseBinding command refresh count to be '{refreshCountBefore + 1}', " +
                        $"but found '{model.RefreshCount}'. {lastTargetState}. Trace: {trace}.");
                }

                AssertEqual(
                    $"Refresh command {refreshCountBefore + 1}",
                    Require<TextBlock>(FindName("CommandStatusText"), "Showcase live MouseBinding command status TextBlock").Text,
                    "Showcase live routed MouseBinding command status");
                return "LeftDoubleClick MouseBinding routed command through host mouse input";
            },
            DispatcherPriority.Send);
    }

    private string DescribeLiveMouseBindingEvent(string stage, object? sender, MouseButtonEventArgs args)
    {
        Point windowPosition = args.GetPosition(this);
        return
            $"{stage}:{args.RoutedEvent.Name}, " +
            $"button={args.ChangedButton}, " +
            $"clicks={args.ClickCount}, " +
            $"handled={args.Handled}, " +
            $"source={DescribeInputElement(args.Source)}, " +
            $"original={DescribeInputElement(args.OriginalSource)}, " +
            $"sender={DescribeInputElement(sender)}, " +
            $"position=({windowPosition.X:0.###},{windowPosition.Y:0.###})";
    }

    private bool TryRaiseLiveMouseClick(
        ProGpuWpfWindowHost liveHost,
        FrameworkElement target,
        string description,
        out string targetState)
    {
        targetState =
            $"{description}.IsVisible={target.IsVisible}, " +
            $"{description}.ActualSize={target.ActualWidth:0.###}x{target.ActualHeight:0.###}, " +
            $"{description}.IsEnabled={target.IsEnabled}, " +
            $"{description}.IsHitTestVisible={target.IsHitTestVisible}";
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
        object? hit = InputHitTest(center);
        targetState += $", Input=({center.X:0.###}, {center.Y:0.###}), InputHitTest={DescribeInputElement(hit)}";
        if (hit == null)
        {
            return false;
        }

        RaiseHostInput(liveHost, WpfInputEventKind.MouseMove, x: center.X, y: center.Y);
        RaiseHostInput(liveHost, WpfInputEventKind.MouseDown, x: center.X, y: center.Y, button: WpfMouseButton.Left);
        RaiseHostInput(liveHost, WpfInputEventKind.MouseUp, x: center.X, y: center.Y, button: WpfMouseButton.Left);
        return true;
    }

    private bool TryRaiseLiveMouseWheel(
        ProGpuWpfWindowHost liveHost,
        FrameworkElement target,
        string description,
        double deltaY,
        out string targetState)
    {
        targetState =
            $"{description}.IsVisible={target.IsVisible}, " +
            $"{description}.ActualSize={target.ActualWidth:0.###}x{target.ActualHeight:0.###}, " +
            $"{description}.IsEnabled={target.IsEnabled}, " +
            $"{description}.IsHitTestVisible={target.IsHitTestVisible}";
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
        object? hit = InputHitTest(center);
        targetState += $", Input=({center.X:0.###}, {center.Y:0.###}), InputHitTest={DescribeInputElement(hit)}";
        if (hit is not DependencyObject hitElement ||
            (!ReferenceEquals(hitElement, target) && !target.IsAncestorOf(hitElement)))
        {
            return false;
        }

        RaiseHostInput(liveHost, WpfInputEventKind.MouseMove, x: center.X, y: center.Y);
        RaiseHostInput(liveHost, WpfInputEventKind.MouseWheel, x: center.X, y: center.Y, deltaY: deltaY);
        return true;
    }

    private bool TryRaiseLiveThumbDrag(
        ProGpuWpfWindowHost liveHost,
        Thumb target,
        string description,
        double horizontalDelta,
        double verticalDelta,
        out string targetState)
    {
        Point initialCenter = target.TranslatePoint(
            new Point(Math.Max(1.0, target.ActualWidth) / 2.0, Math.Max(1.0, target.ActualHeight) / 2.0),
            this);
        target.BringIntoView();
        target.UpdateLayout();

        targetState =
            $"{description}.IsVisible={target.IsVisible}, " +
            $"{description}.ActualSize={target.ActualWidth:0.###}x{target.ActualHeight:0.###}, " +
            $"{description}.IsEnabled={target.IsEnabled}, " +
            $"{description}.IsHitTestVisible={target.IsHitTestVisible}";
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

        Point moved = new(center.X + horizontalDelta, center.Y + verticalDelta);
        object? hit = InputHitTest(center);
        targetState += $", Input=({center.X:0.###}, {center.Y:0.###}), InputHitTest={DescribeInputElement(hit)}";
        if (hit == null || !IsInputElementWithinTarget(hit, target))
        {
            return false;
        }

        int startedBefore = InputThumbDragStartedCount;
        int deltaBefore = InputThumbDragDeltaCount;
        int completedBefore = InputThumbDragCompletedCount;
        int bubbledDeltaBefore = InputBubbledThumbDragDeltaCount;

        RaiseHostInput(liveHost, WpfInputEventKind.MouseMove, x: center.X, y: center.Y);
        targetState +=
            $", DirectlyOver={DescribeInputElement(Mouse.DirectlyOver)}, " +
            $"IsMouseOver={target.IsMouseOver}";
        if (!target.IsMouseOver)
        {
            return false;
        }

        RaiseHostInput(liveHost, WpfInputEventKind.MouseDown, x: center.X, y: center.Y, button: WpfMouseButton.Left);
        targetState +=
            $", AfterDown.IsDragging={target.IsDragging}, " +
            $"AfterDown.Captured={DescribeInputElement(Mouse.Captured)}, " +
            $"AfterDown.Started={InputThumbDragStartedCount - startedBefore}";
        if (!target.IsDragging ||
            !ReferenceEquals(Mouse.Captured, target) ||
            InputThumbDragStartedCount <= startedBefore)
        {
            RaiseHostInput(liveHost, WpfInputEventKind.MouseUp, x: center.X, y: center.Y, button: WpfMouseButton.Left);
            targetState += ", MouseUp cleanup after incomplete drag start";
            return false;
        }

        RaiseHostInput(liveHost, WpfInputEventKind.MouseMove, x: moved.X, y: moved.Y);
        targetState +=
            $", AfterMove.Delta={InputThumbDragDeltaCount - deltaBefore}, " +
            $"AfterMove.Bubbled={InputBubbledThumbDragDeltaCount - bubbledDeltaBefore}, " +
            $"AfterMove.IsDragging={target.IsDragging}, " +
            $"AfterMove.Captured={DescribeInputElement(Mouse.Captured)}";
        if (!target.IsDragging ||
            !ReferenceEquals(Mouse.Captured, target) ||
            InputThumbDragDeltaCount <= deltaBefore ||
            InputBubbledThumbDragDeltaCount <= bubbledDeltaBefore)
        {
            RaiseHostInput(liveHost, WpfInputEventKind.MouseUp, x: moved.X, y: moved.Y, button: WpfMouseButton.Left);
            targetState += ", MouseUp cleanup after incomplete drag move";
            return false;
        }

        RaiseHostInput(liveHost, WpfInputEventKind.MouseUp, x: moved.X, y: moved.Y, button: WpfMouseButton.Left);
        bool completed =
            InputThumbDragCompletedCount > completedBefore &&
            !target.IsDragging &&
            Mouse.Captured is null &&
            Mouse.LeftButton == MouseButtonState.Released;
        targetState +=
            $", AfterUp.Completed={InputThumbDragCompletedCount - completedBefore}, " +
            $"AfterUp.IsDragging={target.IsDragging}, " +
            $"AfterUp.Captured={DescribeInputElement(Mouse.Captured)}, " +
            $"AfterUp.Left={Mouse.LeftButton}";
        return completed;
    }

    private async Task<string> ValidateLiveKeyboardNavigationAsync(ProGpuWpfWindowHost liveHost)
    {
        TextBox? firstBox = null;
        Button? secondButton = null;
        TextBox? thirdBox = null;
        StackPanel? panel = null;
        string lastTargetState = "not checked";

        bool preparedNavigation = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            preparedNavigation = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    var tabControl = Require<TabControl>(FindName("ShowcaseTabControl"), "Showcase live keyboard navigation TabControl");
                    tabControl.SelectedIndex = 4;
                    UpdateLayout();

                    var navigationPanel = Require<StackPanel>(
                        FindName("KeyboardNavigationPanel"),
                        "Showcase live keyboard navigation panel");
                    panel = navigationPanel;
                    firstBox = Require<TextBox>(
                        FindName("KeyboardNavigationFirstBox"),
                        "Showcase live keyboard navigation first TextBox");
                    secondButton = Require<Button>(
                        FindName("KeyboardNavigationSecondButton"),
                        "Showcase live keyboard navigation second Button");
                    thirdBox = Require<TextBox>(
                        FindName("KeyboardNavigationThirdBox"),
                        "Showcase live keyboard navigation third TextBox");

                    lastTargetState =
                        $"First.IsVisible={firstBox.IsVisible}, " +
                        $"First.ActualSize={firstBox.ActualWidth:0.###}x{firstBox.ActualHeight:0.###}, " +
                        $"Second.IsVisible={secondButton.IsVisible}, " +
                        $"Third.IsVisible={thirdBox.IsVisible}";
                    if (!firstBox.IsVisible ||
                        firstBox.ActualWidth <= 1.0 ||
                        firstBox.ActualHeight <= 1.0 ||
                        !firstBox.IsEnabled ||
                        !firstBox.Focusable ||
                        !firstBox.IsHitTestVisible ||
                        !secondButton.IsVisible ||
                        !secondButton.Focusable ||
                        !thirdBox.IsVisible ||
                        !thirdBox.Focusable)
                    {
                        return false;
                    }

                    FocusManager.SetFocusedElement(navigationPanel, firstBox);
                    return ReferenceEquals(firstBox, Keyboard.Focus(firstBox));
                },
                DispatcherPriority.Send);
            if (preparedNavigation)
            {
                break;
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!preparedNavigation)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live keyboard-navigation target to become visible and focusable before injecting input, but last state was: {lastTargetState}.");
        }

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                if (!ReferenceEquals(Keyboard.FocusedElement, firstBox))
                {
                    throw new InvalidOperationException(
                        $"Expected Showcase live setup to focus KeyboardNavigationFirstBox before Tab input, but focused '{DescribeInputElement(Keyboard.FocusedElement)}'. " +
                        $"LogicalFocusedElement={DescribeInputElement(FocusManager.GetFocusedElement(Require<StackPanel>(panel, "Showcase live keyboard navigation panel")))}, " +
                        $"Mouse.DirectlyOver={DescribeInputElement(Mouse.DirectlyOver)}.");
                }

                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "Tab");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "Tab");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(secondButton, Keyboard.FocusedElement, "Showcase live Tab focus moved to second target");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "Tab");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "Tab");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(thirdBox, Keyboard.FocusedElement, "Showcase live Tab focus moved to third target");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyDown, key: "Tab");
                RaiseHostInput(liveHost, WpfInputEventKind.KeyUp, key: "Tab");
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        return await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertEqual(firstBox, Keyboard.FocusedElement, "Showcase live Tab focus cycled to first target");
                return "Tab keyboard navigation cycled focus through live host input";
            },
            DispatcherPriority.Send);
    }

    private async Task<string> ValidateLiveWheelAndCaptureInputAsync(ProGpuWpfWindowHost liveHost)
    {
        ScrollViewer? selectorScrollViewer = null;
        Thumb? inputDragThumb = null;
        StackPanel? inputThumbPanel = null;
        TextBlock? inputDragStatusText = null;
        int wheelCountBefore = 0;
        int dragStartedBefore = 0;
        int dragDeltaBefore = 0;
        int dragCompletedBefore = 0;
        int bubbledDragDeltaBefore = 0;
        string lastTargetState = "not checked";

        LivePresentedFrameState selectorTabFrameBefore = await CaptureLivePresentedFrameStateAsync(liveHost);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var tabControl = Require<TabControl>(FindName("ShowcaseTabControl"), "Showcase live wheel TabControl");
                tabControl.SelectedIndex = 3;
                var expander = Require<Expander>(FindName("SelectorExpander"), "Showcase live wheel Expander");
                expander.IsExpanded = true;
                UpdateLayout();

                selectorScrollViewer = Require<ScrollViewer>(
                    FindName("SelectorScrollViewer"),
                    "Showcase live selector ScrollViewer");
                wheelCountBefore = SelectorMouseWheelCount;
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await WaitForLiveInputPresentedFrameAsync(
            liveHost,
            selectorTabFrameBefore,
            "selector wheel tab activation");

        bool sentWheelInput = false;
        bool observedWheelInput = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            sentWheelInput = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    return TryRaiseLiveMouseWheel(
                        liveHost,
                        Require<ScrollViewer>(selectorScrollViewer, "Showcase live selector ScrollViewer before wheel input"),
                        "SelectorScrollViewer",
                        deltaY: -1.0,
                        out lastTargetState);
                },
                DispatcherPriority.Send);
            if (sentWheelInput)
            {
                await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
                observedWheelInput = await InvokeWithLiveHostWakeAsync(
                    liveHost,
                    () => SelectorMouseWheelCount > wheelCountBefore,
                    DispatcherPriority.Send);
                if (observedWheelInput)
                {
                    break;
                }
            }

            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!sentWheelInput)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live selector ScrollViewer to become wheel-testable before injecting input, but last state was: {lastTargetState}.");
        }

        if (!observedWheelInput)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live selector ScrollViewer to observe routed wheel input before timeout, but last state was: {lastTargetState}.");
        }

        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                AssertLiveGreaterThan(wheelCountBefore, SelectorMouseWheelCount, "Showcase live ScrollViewer MouseWheel event count");
                AssertEqual("SelectorScrollViewer", LastSelectorMouseWheelSenderName, "Showcase live ScrollViewer MouseWheel sender");
                AssertEqual("MouseWheel", LastSelectorMouseWheelRoutedEventName, "Showcase live ScrollViewer MouseWheel routed event");
                AssertEqual(-120, LastSelectorMouseWheelDelta, "Showcase live ScrollViewer MouseWheel delta");
            },
            DispatcherPriority.Send);

        string clippedScrollStatus = await ValidateLiveClippedScrollAsync(liveHost,
            Require<ScrollViewer>(selectorScrollViewer, "Showcase clipped scroll viewer"));

        LivePresentedFrameState inputThumbTabFrameBefore = await CaptureLivePresentedFrameStateAsync(liveHost);
        await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var tabControl = Require<TabControl>(FindName("ShowcaseTabControl"), "Showcase live Thumb TabControl");
                tabControl.SelectedIndex = 4;
                UpdateLayout();

                inputThumbPanel = Require<StackPanel>(FindName("InputThumbPanel"), "Showcase live input Thumb panel");
                inputDragThumb = Require<Thumb>(FindName("InputDragThumb"), "Showcase live input drag Thumb");
                inputDragStatusText = Require<TextBlock>(FindName("InputDragStatusText"), "Showcase live input drag status");
                dragStartedBefore = InputThumbDragStartedCount;
                dragDeltaBefore = InputThumbDragDeltaCount;
                dragCompletedBefore = InputThumbDragCompletedCount;
                bubbledDragDeltaBefore = InputBubbledThumbDragDeltaCount;
            },
            DispatcherPriority.Send);
        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
        await WaitForLiveInputPresentedFrameAsync(
            liveHost,
            inputThumbTabFrameBefore,
            "input Thumb tab activation");

        bool sentDragInput = false;
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            sentDragInput = await InvokeWithLiveHostWakeAsync(
                liveHost,
                () =>
                {
                    return TryRaiseLiveThumbDrag(
                        liveHost,
                        Require<Thumb>(inputDragThumb, "Showcase live input drag Thumb attempt"),
                        "InputDragThumb",
                        horizontalDelta: 18.0,
                        verticalDelta: 12.0,
                        out lastTargetState);
                },
                DispatcherPriority.Send);
            if (sentDragInput)
            {
                break;
            }

            WakeLiveRenderHost(liveHost);
            await Task.Delay(LiveValidationRetryDelay);
        }

        if (!sentDragInput)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live input Thumb to become drag-testable before injecting input, but last state was: {lastTargetState}.");
        }

        await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);

        return await InvokeWithLiveHostWakeAsync(
            liveHost,
            () =>
            {
                var thumb = Require<Thumb>(inputDragThumb, "Showcase live input drag Thumb after drag");
                Console.WriteLine(
                    "ProGPU WPF Showcase live Thumb state after drag: " +
                    $"started={InputThumbDragStartedCount - dragStartedBefore}, " +
                    $"delta={InputThumbDragDeltaCount - dragDeltaBefore}, " +
                    $"completed={InputThumbDragCompletedCount - dragCompletedBefore}, " +
                    $"bubbled={InputBubbledThumbDragDeltaCount - bubbledDragDeltaBefore}, " +
                    $"isDragging={thumb.IsDragging}, " +
                    $"captured={DescribeInputElement(Mouse.Captured)}, " +
                    $"left={Mouse.LeftButton}, " +
                    $"status='{Require<TextBlock>(inputDragStatusText, "Showcase live input drag status").Text}'.");
                AssertEqual(false, thumb.IsDragging, "Showcase live input Thumb dragging released state");
                AssertEqual(null, Mouse.Captured, "Showcase live input Thumb mouse capture released");
                AssertLiveGreaterThan(dragStartedBefore, InputThumbDragStartedCount, "Showcase live input Thumb DragStarted event count");
                AssertLiveGreaterThan(dragDeltaBefore, InputThumbDragDeltaCount, "Showcase live input Thumb DragDelta event count");
                AssertLiveGreaterThan(dragCompletedBefore, InputThumbDragCompletedCount, "Showcase live input Thumb DragCompleted event count");
                AssertLiveGreaterThan(bubbledDragDeltaBefore, InputBubbledThumbDragDeltaCount, "Showcase live input Thumb bubbled DragDelta event count");
                AssertEqual("InputDragThumb", LastInputThumbDragStartedSenderName, "Showcase live input Thumb DragStarted sender");
                AssertEqual("InputDragThumb", LastInputThumbDragDeltaSenderName, "Showcase live input Thumb DragDelta sender");
                AssertEqual("InputDragThumb", LastInputThumbDragCompletedSenderName, "Showcase live input Thumb DragCompleted sender");
                AssertEqual("InputThumbPanel", LastInputBubbledThumbDragDeltaSenderName, "Showcase live input Thumb bubbled sender");
                AssertEqual("InputDragThumb", LastInputBubbledThumbDragDeltaOriginalSourceName, "Showcase live input Thumb bubbled original source");
                AssertEqual("DragStarted", LastInputThumbDragStartedRoutedEventName, "Showcase live input Thumb DragStarted routed event");
                AssertEqual("DragDelta", LastInputThumbDragDeltaRoutedEventName, "Showcase live input Thumb DragDelta routed event");
                AssertEqual("DragCompleted", LastInputThumbDragCompletedRoutedEventName, "Showcase live input Thumb DragCompleted routed event");
                AssertEqual("DragDelta", LastInputBubbledThumbDragDeltaRoutedEventName, "Showcase live input Thumb bubbled DragDelta routed event");
                AssertLiveClose(18.0, LastInputThumbDragDeltaHorizontalChange, 1.25, "Showcase live input Thumb DragDelta horizontal change");
                AssertLiveClose(12.0, LastInputThumbDragDeltaVerticalChange, 1.25, "Showcase live input Thumb DragDelta vertical change");
                AssertEqual(false, LastInputThumbDragCompletedCanceled, "Showcase live input Thumb DragCompleted canceled state");
                AssertLiveContains("Dragged ", Require<TextBlock>(inputDragStatusText, "Showcase live input drag status").Text, "Showcase live input Thumb drag status");
                AssertEqual(true, ReferenceEquals(inputThumbPanel, thumb.Parent), "Showcase live input Thumb parent");
                return $"{clippedScrollStatus}; MouseWheel routed through SelectorScrollViewer and Thumb drag captured, moved, and released through host mouse input";
            },
            DispatcherPriority.Send);
    }

    private async Task<string> ValidateLiveClippedScrollAsync(ProGpuWpfWindowHost liveHost, ScrollViewer viewer)
    {
        TextBlock text = await InvokeWithLiveHostWakeAsync(liveHost,
            () => Require<TextBlock>(FindName("SelectorScrollText"), "Showcase clipped scroll text"), DispatcherPriority.Send);
        string originalText = string.Empty;
        double originalOffset = 0;
        bool changedContent = false;
        try
        {
            var before = await CaptureLivePresentedFrameStateAsync(liveHost);
            await InvokeWithLiveHostWakeAsync(liveHost, () =>
            {
                originalText = text.Text;
                originalOffset = viewer.VerticalOffset;
                // Exercise a normal content edit in the complete application. The
                // original short details can fit entirely and cannot prove scrolling.
                var paragraphs = new string[12];
                Array.Fill(paragraphs, originalText);
                changedContent = true;
                text.Text = string.Join("\n", paragraphs);
                viewer.ScrollToTop();
                UpdateLayout();
            }, DispatcherPriority.Send);
            await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
            await WaitForLiveInputPresentedFrameAsync(liveHost, before, "scrolling details content expansion");

            ScrollContentPresenter presenter = await InvokeWithLiveHostWakeAsync(liveHost, () =>
            {
                if (viewer.CanContentScroll || viewer.ScrollableHeight <= viewer.ViewportHeight || viewer.VerticalOffset != 0)
                    throw new InvalidOperationException($"Showcase details must have overflowing pixel-scrolled content: " +
                        $"logical={viewer.CanContentScroll}, range={viewer.ScrollableHeight}, viewport={viewer.ViewportHeight}, offset={viewer.VerticalOffset}.");
                return Require<ScrollContentPresenter>(viewer.Template.FindName("PART_ScrollContentPresenter", viewer),
                    "Showcase actual scroll content presenter");
            }, DispatcherPriority.Send);
            double initialTop = await InvokeWithLiveHostWakeAsync(liveHost,
                () => text.TranslatePoint(new Point(), presenter).Y, DispatcherPriority.Send);
            before = await CaptureLivePresentedFrameStateAsync(liveHost);
            await InvokeWithLiveHostWakeAsync(liveHost, () =>
            {
                AssertPresentedScrollPoint(liveHost, presenter, text, new Point(5, presenter.ActualHeight / 2), true);
                if (!TryRaiseLiveMouseWheel(liveHost, presenter, "Showcase clipped details presenter", -1, out string state))
                    throw new InvalidOperationException($"Showcase scrolling details cannot receive wheel input: {state}.");
            }, DispatcherPriority.Send);
            await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
            await WaitForLiveInputPresentedFrameAsync(liveHost, before, "wheel-driven clipped content movement");

            double scrolledOffset = await InvokeWithLiveHostWakeAsync(liveHost, () =>
            {
                if (viewer.VerticalOffset <= 2)
                    throw new InvalidOperationException("Showcase wheel event did not actually scroll its overflowing content.");
                double movedTop = text.TranslatePoint(new Point(), presenter).Y;
                AssertLiveClose(-viewer.VerticalOffset, movedTop - initialTop, 0.01,
                    "Showcase source scroll transform applies offset once");
                AssertPresentedScrollPoint(liveHost, presenter, text, new Point(5, presenter.ActualHeight / 2), true);
                // This point is inside the text's arranged rectangle but above the
                // actual source viewport. All-owner queries catch leaks even when
                // another foreground visual would hide them from a first-hit query.
                Point clipped = new(5, -2);
                Point textPoint = presenter.TranslatePoint(clipped, text);
                if (textPoint.Y < 0 || textPoint.Y >= text.ActualHeight)
                    throw new InvalidOperationException("Showcase clipped probe did not intersect the scrolled source content.");
                AssertPresentedScrollPoint(liveHost, presenter, text, clipped, false);
                return viewer.VerticalOffset;
            }, DispatcherPriority.Send);

            before = await CaptureLivePresentedFrameStateAsync(liveHost);
            await InvokeWithLiveHostWakeAsync(liveHost, () =>
            {
                text.Text = "Updated scrolled content";
                UpdateLayout();
            }, DispatcherPriority.Send);
            await InvokeWithLiveHostWakeAsync(liveHost, static () => { }, DispatcherPriority.Background);
            await WaitForLiveInputPresentedFrameAsync(liveHost, before, "scrolled content replacement");
            await InvokeWithLiveHostWakeAsync(liveHost, () =>
            {
                AssertEqual("Updated scrolled content", text.Text, "Showcase scrolled content replacement");
                AssertLiveClose(0, viewer.VerticalOffset, 0.01, "Showcase shortened content clamps scroll offset");
                AssertPresentedScrollPoint(liveHost, presenter, text, new Point(5, Math.Min(5, text.ActualHeight / 2)), true);
                AssertPresentedScrollPoint(liveHost, presenter, text, new Point(5, presenter.ActualHeight - 2), false);
            }, DispatcherPriority.Send);
            return $"overflowing details scrolled {scrolledOffset:0.###} DIPs, presented, clipped input and replaced content";
        }
        finally
        {
            if (changedContent)
                await InvokeWithLiveHostWakeAsync(liveHost, () =>
                {
                    text.Text = originalText;
                    viewer.ScrollToVerticalOffset(originalOffset);
                    UpdateLayout();
                }, DispatcherPriority.Send);
        }
    }

    private void AssertPresentedScrollPoint(ProGpuWpfWindowHost liveHost, ScrollContentPresenter presenter,
        TextBlock text, Point localPoint, bool expectedText)
    {
        Point point = presenter.TranslatePoint(localPoint, this);
        ProGpuWpfDiagnostics.TryHitTestOwners(liveHost, point.X, point.Y, out object?[] owners);
        // Device index upload is demand-driven: inspect residency after the real
        // query, not before the first query against this newly presented scene.
        if (!ProGpuWpfDiagnostics.TryGetGpuHitTestCacheSnapshot(liveHost, out var index) ||
            !index.HasIndex || !index.HasDeviceIndex)
            throw new InvalidOperationException("Showcase scroll input requires the presented renderer's device index.");
        bool found = false;
        for (int i = 0; i < owners.Length; i++) found |= ReferenceEquals(owners[i], text);
        AssertEqual(expectedText, found, "Showcase presented scroll source point coverage");
        if (!expectedText)
        {
            ProGpuWpfDiagnostics.TryQueryHitTestBoundsOwners(liveHost,
                point.X - 0.25, point.Y - 0.25, point.X + 0.25, point.Y + 0.25, out object?[] regionOwners);
            for (int i = 0; i < regionOwners.Length; i++)
                if (ReferenceEquals(regionOwners[i], text))
                    throw new InvalidOperationException("Showcase clipped or replaced text leaked into presented region input.");
        }
    }

    private async Task<LivePresentedFrameState> CaptureLivePresentedFrameStateAsync(ProGpuWpfWindowHost liveHost)
    {
        return await InvokeWithLiveNativeLoopWakeAsync(
            liveHost,
            () => ReadLivePresentedFrameState(liveHost),
            DispatcherPriority.Send);
    }

    private async Task<LivePresentedFrameState> WaitForLiveInputPresentedFrameAsync(
        ProGpuWpfWindowHost liveHost,
        LivePresentedFrameState previousFrame,
        string description)
    {
        string lastState = "not checked";
        for (int attempt = 0; attempt < LiveValidationMaxAttempts; attempt++)
        {
            await Task.Delay(LiveValidationRetryDelay);
            var currentFrame = await InvokeWithLiveNativeLoopWakeAsync(
                liveHost,
                () =>
                {
                    var current = ReadLivePresentedFrameState(liveHost);
                    long renderWakeups = ReadLiveRenderSchedulerWakeupCount(liveHost);
                    lastState =
                        $"{description}: {FormatLivePresentedFrameState(current)}, " +
                        $"render wakeups {renderWakeups}";
                    return current;
                },
                DispatcherPriority.Background);

            if (LivePresentedFrameContentChanged(previousFrame, currentFrame))
            {
                return currentFrame;
            }
        }

        throw new InvalidOperationException(
            $"Expected Showcase live {description} to present a new ProGPU frame-state change without a resize, " +
            $"but previous frame was {FormatLivePresentedFrameState(previousFrame)} and last state was: {lastState}.");
    }

    private static bool LivePresentedFrameContentChanged(
        LivePresentedFrameState previousFrame,
        LivePresentedFrameState currentFrame)
    {
        return currentFrame.HasPresentedFrame &&
            currentFrame.LogicalWidth == previousFrame.LogicalWidth &&
            currentFrame.LogicalHeight == previousFrame.LogicalHeight &&
            currentFrame.PixelWidth == previousFrame.PixelWidth &&
            currentFrame.PixelHeight == previousFrame.PixelHeight &&
            (currentFrame.SceneChangeVersion > previousFrame.SceneChangeVersion ||
             currentFrame.RetainedWpfChangeVersion > previousFrame.RetainedWpfChangeVersion ||
             currentFrame.FlatDrawingChangeVersion > previousFrame.FlatDrawingChangeVersion);
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

    private async Task<T> InvokeWithLiveNativeLoopWakeAsync<T>(
        ProGpuWpfWindowHost liveHost,
        Func<T> callback,
        DispatcherPriority priority)
    {
        if (Dispatcher.CheckAccess())
        {
            return callback();
        }

        DispatcherOperation<T> operation = Dispatcher.InvokeAsync(callback, priority);
        WakeLiveNativeLoop(liveHost);
        return await operation;
    }

    private static void WakeLiveRenderHost(ProGpuWpfWindowHost liveHost)
    {
        if (!ProGpuWpfDiagnostics.TryRequestRender(liveHost))
        {
            throw new InvalidOperationException("Expected ProGPU WPF diagnostics to request a live Showcase render.");
        }
    }

    private static void WakeLiveNativeLoop(ProGpuWpfWindowHost liveHost)
    {
        if (!ProGpuWpfDiagnostics.TryWakeNativeLoop(liveHost))
        {
            throw new InvalidOperationException("Expected ProGPU WPF diagnostics to wake the live Showcase native loop.");
        }
    }

    private async Task<string> ValidateLivePerformanceAsync(ProGpuWpfWindowHost liveHost)
    {
        const int warmupFrameCount = 16;
        const int measuredFrameCount = 120;
        for (int frame = 0; frame < warmupFrameCount; frame++)
        {
            _ = await PresentLivePerformanceFrameAsync(liveHost);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var memoryBefore = ReadLiveMemorySnapshot(liveHost);
        var firstFrame = await PresentLivePerformanceFrameAsync(liveHost);
        var cpuFrameTimes = new double[measuredFrameCount];
        var compileTimes = new double[measuredFrameCount];
        var uploadTimes = new double[measuredFrameCount];
        var encodeTimes = new double[measuredFrameCount];
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        using Process process = Process.GetCurrentProcess();
        TimeSpan processCpuBefore = process.TotalProcessorTime;
        var stopwatch = Stopwatch.StartNew();

        LivePerformanceSnapshot lastFrame = firstFrame;
        for (int frame = 0; frame < measuredFrameCount; frame++)
        {
            lastFrame = await PresentLivePerformanceFrameAsync(liveHost);
            cpuFrameTimes[frame] = lastFrame.CompositorCpuFrameTimeMs;
            compileTimes[frame] = lastFrame.VisualTreeCompileCpuTimeMs;
            uploadTimes[frame] = lastFrame.GpuUploadCpuTimeMs;
            encodeTimes[frame] = lastFrame.RenderPassEncodingCpuTimeMs;
        }

        stopwatch.Stop();
        TimeSpan processCpuAfter = process.TotalProcessorTime;
        long allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        var memoryAfter = ReadLiveMemorySnapshot(liveHost);

        if (lastFrame.PresentedFrameCount - firstFrame.PresentedFrameCount < measuredFrameCount)
        {
            throw new InvalidOperationException(
                $"Expected {measuredFrameCount} measured Showcase presentations, but observed " +
                $"{lastFrame.PresentedFrameCount - firstFrame.PresentedFrameCount}.");
        }

        if (lastFrame.PathAtlasGrowthCount != firstFrame.PathAtlasGrowthCount)
        {
            throw new InvalidOperationException(
                $"Expected the warmed Showcase path atlas to remain stable, but growth count changed from " +
                $"{firstFrame.PathAtlasGrowthCount} to {lastFrame.PathAtlasGrowthCount}.");
        }

        ulong gpuGrowthBytes = memoryAfter.KnownWpfAndCompositorGpuBytes >= memoryBefore.KnownWpfAndCompositorGpuBytes
            ? memoryAfter.KnownWpfAndCompositorGpuBytes - memoryBefore.KnownWpfAndCompositorGpuBytes
            : 0;
        const ulong maximumSteadyStateGpuGrowthBytes = 1024UL * 1024UL;
        if (gpuGrowthBytes > maximumSteadyStateGpuGrowthBytes)
        {
            throw new InvalidOperationException(
                $"Expected warmed Showcase tracked GPU ownership to grow by at most " +
                $"{maximumSteadyStateGpuGrowthBytes} bytes, but it grew by {gpuGrowthBytes} bytes.");
        }

        Array.Sort(cpuFrameTimes);
        Array.Sort(compileTimes);
        Array.Sort(uploadTimes);
        Array.Sort(encodeTimes);
        double elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, double.Epsilon);
        double processCpuSeconds = (processCpuAfter - processCpuBefore).TotalSeconds;
        double oneCoreCpuPercent = processCpuSeconds / elapsedSeconds * 100.0;
        double machineCpuPercent = oneCoreCpuPercent / Math.Max(1, Environment.ProcessorCount);
        double allocatedBytesPerFrame = (double)allocatedBytes / measuredFrameCount;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"Showcase performance validation succeeded: frames {measuredFrameCount}, " +
            $"wall {stopwatch.Elapsed.TotalMilliseconds:0.###} ms, " +
            $"process CPU {processCpuSeconds * 1000.0:0.###} ms " +
            $"({oneCoreCpuPercent:0.##}% of one core, {machineCpuPercent:0.##}% of machine), " +
            $"allocated {allocatedBytes} bytes ({allocatedBytesPerFrame:0.##}/frame), " +
            $"compositor CPU p50/p95 {Percentile(cpuFrameTimes, 0.50):0.###}/{Percentile(cpuFrameTimes, 0.95):0.###} ms, " +
            $"compile {Percentile(compileTimes, 0.50):0.###}/{Percentile(compileTimes, 0.95):0.###} ms, " +
            $"upload {Percentile(uploadTimes, 0.50):0.###}/{Percentile(uploadTimes, 0.95):0.###} ms, " +
            $"encode {Percentile(encodeTimes, 0.50):0.###}/{Percentile(encodeTimes, 0.95):0.###} ms, " +
            $"draws {lastFrame.DrawCallsCount}, commands {lastFrame.RecordedCommandCount}, " +
            $"vertices {lastFrame.VectorVerticesCount} vector/{lastFrame.TextVerticesCount} text, " +
            $"scene cache {(lastFrame.SceneCacheHit ? "hit" : lastFrame.SceneCacheMissReason ?? "miss")}, " +
            $"path atlas {lastFrame.PathAtlasCachedCount} entries/{lastFrame.PathAtlasGrowthCount} growths, " +
            $"glyph batches {lastFrame.GlyphRasterBatchSubmissions}, " +
            $"managed heap {memoryBefore.ManagedHeapBytes}->{memoryAfter.ManagedHeapBytes}, " +
            $"working set {memoryBefore.ProcessWorkingSetBytes}->{memoryAfter.ProcessWorkingSetBytes}, " +
            $"tracked GPU {memoryBefore.KnownWpfAndCompositorGpuBytes}->{memoryAfter.KnownWpfAndCompositorGpuBytes} bytes.");
    }

    private static async Task<LivePerformanceSnapshot> PresentLivePerformanceFrameAsync(
        ProGpuWpfWindowHost liveHost)
    {
        if (!ProGpuWpfDiagnostics.TryGetPerformanceSnapshot(liveHost, out var before))
        {
            throw new InvalidOperationException("Expected the live Showcase host to publish typed performance diagnostics.");
        }

        WakeLiveRenderHost(liveHost);
        for (int attempt = 0; attempt < 300; attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(2));
            if (ProGpuWpfDiagnostics.TryGetPerformanceSnapshot(liveHost, out var current) &&
                current.PresentedFrameCount > before.PresentedFrameCount)
            {
                return new LivePerformanceSnapshot(
                    current.PresentedFrameCount,
                    current.CompositorCpuFrameTimeMs,
                    current.VisualTreeCompileCpuTimeMs,
                    current.GpuUploadCpuTimeMs,
                    current.RenderPassEncodingCpuTimeMs,
                    current.DrawCallsCount,
                    current.RecordedCommandCount,
                    current.VectorVerticesCount,
                    current.TextVerticesCount,
                    current.SceneCacheHit,
                    current.SceneCacheMissReason,
                    current.PathAtlasCachedCount,
                    current.PathAtlasGrowthCount,
                    current.GlyphRasterBatchSubmissions);
            }
        }

        throw new InvalidOperationException("Expected the requested Showcase performance frame to be presented.");
    }

    private static LiveMemorySnapshot ReadLiveMemorySnapshot(ProGpuWpfWindowHost liveHost)
    {
        if (!ProGpuWpfDiagnostics.TryGetMemorySnapshot(liveHost, out var snapshot))
        {
            throw new InvalidOperationException("Expected the live Showcase host to publish typed memory diagnostics.");
        }

        return new LiveMemorySnapshot(
            snapshot.ManagedHeapBytes,
            snapshot.ProcessWorkingSetBytes,
            snapshot.KnownWpfAndCompositorGpuBytes);
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
        {
            return 0.0;
        }

        int index = Math.Clamp(
            (int)Math.Ceiling(percentile * sortedValues.Length) - 1,
            0,
            sortedValues.Length - 1);
        return sortedValues[index];
    }

    private readonly record struct LivePerformanceSnapshot(
        long PresentedFrameCount,
        double CompositorCpuFrameTimeMs,
        double VisualTreeCompileCpuTimeMs,
        double GpuUploadCpuTimeMs,
        double RenderPassEncodingCpuTimeMs,
        int DrawCallsCount,
        int RecordedCommandCount,
        int VectorVerticesCount,
        int TextVerticesCount,
        bool SceneCacheHit,
        string? SceneCacheMissReason,
        int PathAtlasCachedCount,
        uint PathAtlasGrowthCount,
        ulong GlyphRasterBatchSubmissions);

    private readonly record struct LiveMemorySnapshot(
        long ManagedHeapBytes,
        long ProcessWorkingSetBytes,
        ulong KnownWpfAndCompositorGpuBytes);

    private static long ReadLiveRenderSchedulerWakeupCount(ProGpuWpfWindowHost liveHost)
    {
        if (!ProGpuWpfDiagnostics.TryGetRenderSchedulerWakeupCount(liveHost, out var wakeupCount))
        {
            throw new InvalidOperationException("Expected ProGPU WPF diagnostics to read the live Showcase render wakeup count.");
        }

        return wakeupCount;
    }

    private static LivePresentedFrameState ReadLivePresentedFrameState(ProGpuWpfWindowHost liveHost)
    {
        var frameState = liveHost.LastPresentedFrameState;
        return new LivePresentedFrameState(
            liveHost.HasPresentedFrame,
            frameState.LogicalWidth,
            frameState.LogicalHeight,
            frameState.PixelWidth,
            frameState.PixelHeight,
            frameState.DpiScale,
            frameState.SceneChangeVersion,
            frameState.RetainedWpfChangeVersion,
            frameState.FlatDrawingChangeVersion);
    }

    private static string FormatLivePresentedFrameState(LivePresentedFrameState frame)
    {
        return
            $"presented={frame.HasPresentedFrame}, " +
            $"logical {frame.LogicalWidth}x{frame.LogicalHeight}, " +
            $"pixels {frame.PixelWidth}x{frame.PixelHeight}, " +
            $"dpi {frame.DpiScale:0.###}, " +
            $"scene {frame.SceneChangeVersion}, " +
            $"wpf {frame.RetainedWpfChangeVersion}, " +
            $"flat {frame.FlatDrawingChangeVersion}";
    }

    private static string DescribeInputElement(object? element)
    {
        if (element == null)
        {
            return "<null>";
        }

        if (element is FrameworkElement frameworkElement && !string.IsNullOrEmpty(frameworkElement.Name))
        {
            return frameworkElement.Name;
        }

        return element is IInputElement
            ? "InputElement"
            : "Element";
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

    private LiveLayoutSize CaptureLiveLayoutSize(ProGpuWpfWindowHost liveHost)
    {
        FrameworkElement contentElement = Require<FrameworkElement>(
            Content,
            "Showcase live layout root content");
        var geometry = ReadLiveRenderSurfaceGeometry(liveHost);
        var geometryStatus = FormatLiveRenderSurfaceGeometry(geometry);
        return new LiveLayoutSize(
            IsValid: true,
            geometry,
            geometryStatus,
            ActualWidth,
            ActualHeight,
            contentElement.ActualWidth,
            contentElement.ActualHeight);
    }

    private static void SetLiveNativeWindowSize(ProGpuWpfWindowHost liveHost, int width, int height)
    {
        liveHost.SetClientSize(width, height);
        WakeLiveRenderHost(liveHost);
    }

    private static string ValidateLiveRenderSurfaceGeometryCore(
        ProGpuWpfWindowHost liveHost,
        uint expectedLogicalWidth,
        uint expectedLogicalHeight)
    {
        var geometry = ReadLiveRenderSurfaceGeometry(liveHost);
        var logicalWidth = geometry.LogicalWidth;
        var logicalHeight = geometry.LogicalHeight;
        var pixelWidth = geometry.PixelWidth;
        var pixelHeight = geometry.PixelHeight;
        var dpiScale = geometry.DpiScale;
        var viewportX = geometry.ViewportX;
        var viewportY = geometry.ViewportY;
        var viewportWidth = geometry.ViewportWidth;
        var viewportHeight = geometry.ViewportHeight;

        AssertEqual(expectedLogicalWidth, logicalWidth, "Showcase live ProGPU WPF logical width");
        AssertEqual(expectedLogicalHeight, logicalHeight, "Showcase live ProGPU WPF logical height");
        if (pixelWidth < logicalWidth || pixelHeight < logicalHeight)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live ProGPU WPF pixels to cover logical content, but got logical {logicalWidth}x{logicalHeight} and pixels {pixelWidth}x{pixelHeight}.");
        }

        if (dpiScale > 1.01 &&
            (pixelWidth <= logicalWidth || pixelHeight <= logicalHeight))
        {
            throw new InvalidOperationException(
                $"Expected Showcase live ProGPU WPF high-DPI pixels to exceed logical size, but got logical {logicalWidth}x{logicalHeight}, pixels {pixelWidth}x{pixelHeight}, DPI {dpiScale}.");
        }

        if (viewportX != 0 || viewportY != 0 || viewportWidth != pixelWidth || viewportHeight != pixelHeight)
        {
            throw new InvalidOperationException(
                $"Expected Showcase live ProGPU WPF viewport to use the full physical target, but got viewport {viewportWidth}x{viewportHeight}@{viewportX},{viewportY} for pixels {pixelWidth}x{pixelHeight}.");
        }

        return FormatLiveRenderSurfaceGeometry(geometry);
    }

    private static LiveRenderSurfaceGeometry ReadLiveRenderSurfaceGeometry(ProGpuWpfWindowHost liveHost)
    {
        if (!ProGpuWpfDiagnostics.TryGetRenderSurfaceGeometry(liveHost, out var geometry))
        {
            throw new InvalidOperationException("Expected ProGPU WPF diagnostics to resolve Showcase render-surface geometry.");
        }

        return new LiveRenderSurfaceGeometry(
            geometry.LogicalWidth,
            geometry.LogicalHeight,
            geometry.PixelWidth,
            geometry.PixelHeight,
            geometry.DpiScale,
            geometry.ViewportX,
            geometry.ViewportY,
            geometry.ViewportWidth,
            geometry.ViewportHeight);
    }

    private static bool LiveRenderSurfaceGeometryIsReady(
        LiveRenderSurfaceGeometry geometry,
        uint expectedLogicalWidth,
        uint expectedLogicalHeight)
    {
        return geometry.LogicalWidth == expectedLogicalWidth &&
               geometry.LogicalHeight == expectedLogicalHeight &&
               geometry.PixelWidth >= geometry.LogicalWidth &&
               geometry.PixelHeight >= geometry.LogicalHeight &&
               geometry.ViewportX == 0 &&
               geometry.ViewportY == 0 &&
               geometry.ViewportWidth == geometry.PixelWidth &&
               geometry.ViewportHeight == geometry.PixelHeight &&
               (geometry.DpiScale <= 1.01 ||
                (geometry.PixelWidth > geometry.LogicalWidth &&
                 geometry.PixelHeight > geometry.LogicalHeight));
    }

    private static bool NativeResizeGeometryIsReady(
        LiveRenderSurfaceGeometry geometry,
        uint requestedWidth,
        uint requestedHeight)
    {
        var logicalRequestedPixelWidth = ComputeExpectedPixelSize(requestedWidth, geometry.DpiScale);
        var logicalRequestedPixelHeight = ComputeExpectedPixelSize(requestedHeight, geometry.DpiScale);
        var pixelRequestedLogicalWidth = ComputeExpectedLogicalSize(requestedWidth, geometry.DpiScale);
        var pixelRequestedLogicalHeight = ComputeExpectedLogicalSize(requestedHeight, geometry.DpiScale);

        bool matchesLogicalRequest =
            IsClose(geometry.LogicalWidth, requestedWidth, tolerance: 2u) &&
            IsClose(geometry.LogicalHeight, requestedHeight, tolerance: 2u) &&
            IsClose(geometry.PixelWidth, logicalRequestedPixelWidth, tolerance: 2u) &&
            IsClose(geometry.PixelHeight, logicalRequestedPixelHeight, tolerance: 2u);

        bool matchesFramebufferRequest =
            IsClose(geometry.PixelWidth, requestedWidth, tolerance: 2u) &&
            IsClose(geometry.PixelHeight, requestedHeight, tolerance: 2u) &&
            IsClose(geometry.LogicalWidth, pixelRequestedLogicalWidth, tolerance: 2u) &&
            IsClose(geometry.LogicalHeight, pixelRequestedLogicalHeight, tolerance: 2u);

        return (matchesLogicalRequest || matchesFramebufferRequest) &&
               geometry.ViewportX == 0 &&
               geometry.ViewportY == 0 &&
               geometry.ViewportWidth == geometry.PixelWidth &&
               geometry.ViewportHeight == geometry.PixelHeight &&
               (geometry.DpiScale <= 1.01 ||
                (geometry.PixelWidth > geometry.LogicalWidth &&
                 geometry.PixelHeight > geometry.LogicalHeight));
    }

    private static uint ComputeExpectedPixelSize(uint logicalSize, double dpiScale)
    {
        if (dpiScale <= 0.0 || !double.IsFinite(dpiScale))
        {
            return logicalSize;
        }

        return Math.Max(1u, (uint)Math.Round(logicalSize * dpiScale));
    }

    private static uint ComputeExpectedLogicalSize(uint pixelSize, double dpiScale)
    {
        if (dpiScale <= 0.0 || !double.IsFinite(dpiScale))
        {
            return pixelSize;
        }

        return Math.Max(1u, (uint)Math.Round(pixelSize / dpiScale));
    }

    private static bool IsClose(uint actual, uint expected, uint tolerance)
    {
        return actual >= expected
            ? actual - expected <= tolerance
            : expected - actual <= tolerance;
    }

    private static string FormatLiveRenderSurfaceGeometry(LiveRenderSurfaceGeometry geometry)
    {
        return
            $"logical {geometry.LogicalWidth}x{geometry.LogicalHeight}, " +
            $"pixels {geometry.PixelWidth}x{geometry.PixelHeight}, " +
            $"viewport {geometry.ViewportWidth}x{geometry.ViewportHeight}@{geometry.ViewportX},{geometry.ViewportY}, " +
            $"dpi {geometry.DpiScale:0.###}";
    }

    private readonly record struct LiveRenderSurfaceGeometry(
        uint LogicalWidth,
        uint LogicalHeight,
        uint PixelWidth,
        uint PixelHeight,
        double DpiScale,
        uint ViewportX,
        uint ViewportY,
        uint ViewportWidth,
        uint ViewportHeight);

    private readonly record struct LiveLayoutSize(
        bool IsValid,
        LiveRenderSurfaceGeometry Geometry,
        string GeometryStatus,
        double WindowWidth,
        double WindowHeight,
        double ContentWidth,
        double ContentHeight);

    private readonly record struct LivePresentedFrameState(
        bool HasPresentedFrame,
        uint LogicalWidth,
        uint LogicalHeight,
        uint PixelWidth,
        uint PixelHeight,
        double DpiScale,
        long SceneChangeVersion,
        long RetainedWpfChangeVersion,
        long FlatDrawingChangeVersion);

    private static void RaiseHostInput(
        ProGpuWpfWindowHost liveHost,
        WpfInputEventKind kind,
        string? key = null,
        char? character = null,
        double x = 0.0,
        double y = 0.0,
        double deltaX = 0.0,
        double deltaY = 0.0,
        WpfMouseButton button = WpfMouseButton.None,
        WpfInputModifiers modifiers = WpfInputModifiers.None)
    {
        var input = new WpfInputEventArgs(
            kind,
            key,
            character: character,
            x: x,
            y: y,
            deltaX: deltaX,
            deltaY: deltaY,
            button: button,
            modifiers: modifiers);
        if (!ProGpuWpfDiagnostics.TryRaiseInput(liveHost, input))
        {
            throw new InvalidOperationException("Expected ProGPU WPF diagnostics to inject Showcase live input.");
        }
    }

    private static void UpdateBinding(DependencyObject target, DependencyProperty property)
    {
        BindingOperations.GetBindingExpression(target, property)?.UpdateTarget();
    }

    private static void UpdateSource(DependencyObject target, DependencyProperty property)
    {
        BindingOperations.GetBindingExpression(target, property)?.UpdateSource();
    }

    private static T Require<T>(object? value, string description)
    {
        return value is T typed
            ? typed
            : throw new InvalidOperationException($"Expected {description} to be {typeof(T).Name}.");
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expected}', but found '{actual}'.");
        }
    }

    private static void AssertLiveGreaterThan(int minimumExclusive, int actual, string description)
    {
        if (actual <= minimumExclusive)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be greater than '{minimumExclusive}', but found '{actual}'.");
        }
    }

    private static void AssertLiveClose(double expected, double actual, double tolerance, string description)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be close to '{expected}', but found '{actual}'.");
        }
    }

    private static void AssertLiveContains(string expectedText, string actualText, string description)
    {
        if (!actualText.Contains(expectedText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {description} to contain '{expectedText}', but found '{actualText}'.");
        }
    }

    private static string? GetElementName(object? value)
    {
        return value is FrameworkElement element ? element.Name : null;
    }
}

public sealed class MainViewModel : INotifyPropertyChanged, IDataErrorInfo, INotifyDataErrorInfo
{
    private string _newItemName = "Gamma";
    private ShowcaseItem? _selectedItem;
    private string _selectedCategory = "Framework";
    private bool _actionsEnabled = true;
    private bool _showActiveOnly;
    private double _progress = 35.0;
    private int _refreshCount;
    private string _validationText = "valid: ready";
    private string _dataErrorText = "data: ready";
    private string _notifyDataErrorText = "notify: ready";
    private string _bindingGroupFirstName = "group: Ada";
    private string _bindingGroupLastName = "group: Lovelace";
    private string _bindingGroupStatus = "Group ready";
    private string _bindingTransferText = "Transfer initial";
    private DateTime? _selectedDate = new(2026, 6, 23);
    private int _selectedTabIndex;
    private string? _nullDisplayText;

    public MainViewModel()
    {
        Items =
        [
            new ShowcaseItem("Alpha", "Framework", true),
            new ShowcaseItem("Beta", "Rendering", false)
        ];
        Categories = ["Framework", "Rendering", "Input"];
        FormattedItems = ["Alpha", "Beta"];
        Nodes =
        [
            new ShowcaseNode(
                "Application",
                "WPF",
                new ShowcaseNode("Startup", "Lifecycle"),
                new ShowcaseNode("Resources", "XAML")),
            new ShowcaseNode(
                "Platform",
                "ProGPU",
                new ShowcaseNode("Window", "Silk.NET"),
                new ShowcaseNode("Rendering", "WebGPU"))
        ];
        _selectedItem = Items[0];
        AddItemCommand = new RelayCommand(AddItem, () => ActionsEnabled);
        ResetCommand = new RelayCommand(Reset);
        RequeryCommand = new ShowcaseRequeryCommand();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public ObservableCollection<ShowcaseItem> Items { get; }

    public ObservableCollection<string> Categories { get; }

    public ObservableCollection<string> FormattedItems { get; }

    public ObservableCollection<ShowcaseNode> Nodes { get; }

    public ICommand AddItemCommand { get; }

    public ICommand ResetCommand { get; }

    public ShowcaseRequeryCommand RequeryCommand { get; }

    public string NewItemName
    {
        get => _newItemName;
        set => SetField(ref _newItemName, value);
    }

    public ShowcaseItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetField(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string SelectedCategory
    {
        get => _selectedCategory;
        set => SetField(ref _selectedCategory, value);
    }

    public bool ActionsEnabled
    {
        get => _actionsEnabled;
        set
        {
            if (SetField(ref _actionsEnabled, value) && AddItemCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ShowActiveOnly
    {
        get => _showActiveOnly;
        set => SetField(ref _showActiveOnly, value);
    }

    public string ValidationText
    {
        get => _validationText;
        set => SetField(ref _validationText, value);
    }

    public string DataErrorText
    {
        get => _dataErrorText;
        set => SetField(ref _dataErrorText, value);
    }

    public string NotifyDataErrorText
    {
        get => _notifyDataErrorText;
        set
        {
            if (SetField(ref _notifyDataErrorText, value))
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(NotifyDataErrorText)));
            }
        }
    }

    public string BindingGroupFirstName
    {
        get => _bindingGroupFirstName;
        set => SetField(ref _bindingGroupFirstName, value);
    }

    public string BindingGroupLastName
    {
        get => _bindingGroupLastName;
        set => SetField(ref _bindingGroupLastName, value);
    }

    public string BindingGroupStatus
    {
        get => _bindingGroupStatus;
        set => SetField(ref _bindingGroupStatus, value);
    }

    public string BindingTransferText
    {
        get => _bindingTransferText;
        set => SetField(ref _bindingTransferText, value);
    }

    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set => SetField(ref _selectedDate, value);
    }

    public string? NullDisplayText
    {
        get => _nullDisplayText;
        set => SetField(ref _nullDisplayText, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetField(ref _selectedTabIndex, value);
    }

    public double Progress
    {
        get => _progress;
        set
        {
            if (SetField(ref _progress, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText => SelectedItem == null
        ? $"Progress {Progress:0}%"
        : $"{SelectedItem.Name} selected, progress {Progress:0}%";

    public int RefreshCount
    {
        get => _refreshCount;
        private set
        {
            if (SetField(ref _refreshCount, value))
            {
                OnPropertyChanged(nameof(CommandStatusText));
            }
        }
    }

    public string CommandStatusText => RefreshCount == 0
        ? "Commands idle"
        : $"Refresh command {RefreshCount}";

    public string Error => string.Empty;

    public string this[string columnName] => columnName == nameof(DataErrorText) && !DataErrorText.StartsWith("data:", StringComparison.Ordinal)
        ? "Data value must start with data:"
        : string.Empty;

    public bool HasErrors
    {
        get
        {
            foreach (object _ in GetErrors(null))
            {
                return true;
            }

            return false;
        }
    }

    public IEnumerable GetErrors(string? propertyName)
    {
        if ((propertyName is null || propertyName == nameof(NotifyDataErrorText)) &&
            !NotifyDataErrorText.StartsWith("notify:", StringComparison.Ordinal))
        {
            yield return "Notify value must start with notify:";
        }
    }

    public void RefreshCommandStatus()
    {
        RefreshCount++;
    }

    private void AddItem()
    {
        string name = string.IsNullOrWhiteSpace(NewItemName)
            ? $"Item {Items.Count + 1}"
            : NewItemName.Trim();
        var item = new ShowcaseItem(name, SelectedCategory, true);
        Items.Add(item);
        SelectedItem = item;
        NewItemName = string.Empty;
    }

    private void Reset()
    {
        Items.Clear();
        Items.Add(new ShowcaseItem("Alpha", "Framework", true));
        Items.Add(new ShowcaseItem("Beta", "Rendering", false));
        FormattedItems.Clear();
        FormattedItems.Add("Alpha");
        FormattedItems.Add("Beta");
        SelectedItem = Items[0];
        SelectedCategory = Categories[0];
        NewItemName = "Gamma";
        Progress = 35.0;
        ActionsEnabled = true;
        ShowActiveOnly = false;
        RefreshCount = 0;
        ValidationText = "valid: ready";
        DataErrorText = "data: ready";
        NotifyDataErrorText = "notify: ready";
        BindingGroupFirstName = "group: Ada";
        BindingGroupLastName = "group: Lovelace";
        BindingGroupStatus = "Group ready";
        BindingTransferText = "Transfer initial";
        SelectedDate = new DateTime(2026, 6, 23);
        SelectedTabIndex = 0;
        NullDisplayText = null;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ShowcaseItem(string Name, string Category, bool IsActive);

public sealed class ShowcaseNode
{
    public ShowcaseNode(string name, string kind, params ShowcaseNode[] children)
    {
        Name = name;
        Kind = kind;
        Children = new ObservableCollection<ShowcaseNode>(children);
    }

    public string Name { get; }

    public string Kind { get; }

    public ObservableCollection<ShowcaseNode> Children { get; }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public void Execute(object? parameter)
    {
        _execute();
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class ShowcaseRequeryCommand : ICommand
{
    public int CanExecuteProbeCount { get; private set; }

    public int ExecuteCount { get; private set; }

    public bool CanExecuteValue { get; set; }

    public object? LastParameter { get; private set; }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter)
    {
        CanExecuteProbeCount++;
        return CanExecuteValue;
    }

    public void Execute(object? parameter)
    {
        ExecuteCount++;
        LastParameter = parameter;
    }
}

public sealed class ShowcaseActiveTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool active && active ? "Active" : "Inactive";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class ShowcaseItemSummaryConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string name = values.Length > 0 && values[0] is string itemName ? itemName : "None";
        string category = values.Length > 1 && values[1] is string itemCategory ? itemCategory : "Uncategorized";
        double progress = values.Length > 2 && values[2] is double itemProgress ? itemProgress : 0.0;

        return string.Create(
            culture,
            $"{name} / {category} / {progress:0}%");
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class ShowcaseItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ActiveTemplate { get; set; }

    public DataTemplate? InactiveTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item is ShowcaseItem { IsActive: true }
            ? ActiveTemplate
            : InactiveTemplate;
    }
}

public sealed class ShowcaseItemContainerStyleSelector : StyleSelector
{
    public Style? ActiveStyle { get; set; }

    public Style? InactiveStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
    {
        return item is ShowcaseItem { IsActive: true }
            ? ActiveStyle
            : InactiveStyle;
    }
}

public sealed class ShowcaseNonEmptyValidationRule : ValidationRule
{
    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        return value is string text && text.StartsWith("valid:", StringComparison.Ordinal)
            ? ValidationResult.ValidResult
            : new ValidationResult(false, "Value must start with valid:");
    }
}

public sealed class ShowcaseBindingGroupValidationRule : ValidationRule
{
    public string FirstProperty { get; set; } = string.Empty;

    public string SecondProperty { get; set; } = string.Empty;

    public string RequiredPrefix { get; set; } = string.Empty;

    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        if (value is not BindingGroup bindingGroup)
        {
            return new ValidationResult(false, "Expected a BindingGroup value.");
        }

        foreach (object item in bindingGroup.Items)
        {
            if (!HasRequiredPrefix(bindingGroup, item, FirstProperty) ||
                !HasRequiredPrefix(bindingGroup, item, SecondProperty))
            {
                return new ValidationResult(false, $"Grouped values must start with '{RequiredPrefix}'.");
            }
        }

        return ValidationResult.ValidResult;
    }

    private bool HasRequiredPrefix(BindingGroup bindingGroup, object item, string propertyName)
    {
        object value = bindingGroup.GetValue(item, propertyName);
        string text = value?.ToString() ?? string.Empty;
        return text.StartsWith(RequiredPrefix, StringComparison.Ordinal);
    }
}

public static class ShowcaseResourceFactory
{
    public static string CreateSummary(string prefix, int value)
    {
        return $"{prefix}:{value}";
    }
}

public static class ShowcaseCompositeItemsProvider
{
    public static ObservableCollection<string> Items { get; } =
    [
        "Composite alpha",
        "Composite beta"
    ];
}

public sealed class ShowcaseTextExtension : MarkupExtension
{
    public string Prefix { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return string.IsNullOrEmpty(Prefix)
            ? Value
            : $"{Prefix} {Value}";
    }
}

internal sealed class ShowcaseStatusAdorner : Adorner
{
    public ShowcaseStatusAdorner(UIElement adornedElement, string status)
        : base(adornedElement)
    {
        Status = status;
        IsHitTestVisible = false;
    }

    public string Status { get; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = AdornedElement.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var rect = new Rect(size);
        var pen = new Pen(Brushes.DodgerBlue, 2);
        drawingContext.DrawRectangle(null, pen, rect);
        drawingContext.DrawEllipse(
            Brushes.DodgerBlue,
            null,
            new Point(rect.Right, rect.Top),
            4,
            4);
    }
}

internal static class ShowcaseSelfTest
{
    internal static void ValidateNativeSdkSelection(ProGpuWpfWindowHost? presentedHost = null)
    {
#if PROGPU_WPF_NATIVE_MIL
        if (!string.Equals(AppContext.GetData("LibreWPF.RequestedRendererMode") as string,
                "NativeMilWgpu", StringComparison.Ordinal))
            throw new InvalidOperationException("The native SDK executable is missing its requested renderer record.");

        if (PortableWpfRuntime.ConfiguredMediaBackend != PortableWpfMediaBackend.Portable ||
            !PortableWpfRuntime.IsMediaBackendFrozen)
            throw new InvalidOperationException("Native SDK startup did not select and freeze portable source media.");

        if (presentedHost != null &&
            (!presentedHost.HasPresentedFrame || presentedHost.LastNativeMilSessionFrame == null))
            throw new InvalidOperationException("The explicitly selected native SDK host did not present a native MIL session frame.");
#endif
    }

    public static void Validate(MainWindow window, bool expectLoadedStoryboardApplied = false)
    {
        ArgumentNullException.ThrowIfNull(window);
        ValidateNativeSdkSelection();

        var viewModel = window.DataContext as MainViewModel
            ?? throw new InvalidOperationException("Expected Showcase DataContext.");
        var application = Application.Current
            ?? throw new InvalidOperationException("Expected current Application.");
        AssertEqual(ShutdownMode.OnMainWindowClose, application.ShutdownMode, "Application ShutdownMode");
        ValidateApplicationRunState(application, window, expectLoadedStoryboardApplied);
        ValidateAppConfiguration();
        ValidateRuntimeNameScope(window);
        var themeResources = Require<ResourceDictionary>(
            FindMergedResourceDictionary(
                application.Resources.MergedDictionaries,
                source => source.EndsWith("Resources/Theme.xaml", StringComparison.OrdinalIgnoreCase)),
            "app merged theme ResourceDictionary");
        AssertEqual(true, themeResources.Contains("ShowcasePanelBrush"), "app theme panel brush key");
        AssertEqual(true, themeResources.Contains(typeof(Button)), "app theme implicit Button style key");
        AssertEqual(true, themeResources.Contains("SelectedItemTemplate"), "app theme selected item template key");
        AssertEqual(true, themeResources.Contains("ShowcaseBasedOnButtonStyle"), "app theme BasedOn Button style key");
        AssertEqual(true, themeResources.Contains("ShowcaseTemplateButtonStyle"), "app theme template Button style key");
        AssertEqual(true, themeResources.Contains("ShowcaseTriggerTextBlockStyle"), "app theme trigger TextBlock style key");
        AssertEqual(true, themeResources.Contains("ShowcaseMultiTriggerTextBlockStyle"), "app theme MultiTrigger TextBlock style key");
        AssertEqual(true, themeResources.Contains("ShowcaseMultiDataTriggerTextBlockStyle"), "app theme MultiDataTrigger TextBlock style key");
        var panelBrush = Require<SolidColorBrush>(window.FindResource("ShowcasePanelBrush"), "Showcase panel brush");
        var buttonStyle = Require<Style>(application.TryFindResource(typeof(Button)), "app Button style");
        var implicitItemTemplate = Require<DataTemplate>(
            application.TryFindResource(new DataTemplateKey(typeof(ShowcaseItem))),
            "implicit item DataTemplate");
        var basedOnButtonStyle = Require<Style>(
            application.TryFindResource("ShowcaseBasedOnButtonStyle"),
            "BasedOn Button style");
        var templateButtonStyle = Require<Style>(
            application.TryFindResource("ShowcaseTemplateButtonStyle"),
            "template Button style");
        var triggerTextBlockStyle = Require<Style>(
            application.TryFindResource("ShowcaseTriggerTextBlockStyle"),
            "trigger TextBlock style");
        var multiTriggerTextBlockStyle = Require<Style>(
            application.TryFindResource("ShowcaseMultiTriggerTextBlockStyle"),
            "MultiTrigger TextBlock style");
        var multiDataTriggerTextBlockStyle = Require<Style>(
            application.TryFindResource("ShowcaseMultiDataTriggerTextBlockStyle"),
            "MultiDataTrigger TextBlock style");
        var eventSetterButtonStyle = Require<Style>(
            window.FindResource("ShowcaseEventSetterButtonStyle"),
            "EventSetter Button style");
        var activeTextConverter = Require<ShowcaseActiveTextConverter>(
            window.FindResource("ShowcaseActiveTextConverter"),
            "active text converter");
        var itemSummaryConverter = Require<ShowcaseItemSummaryConverter>(
            window.FindResource("ShowcaseItemSummaryConverter"),
            "item summary converter");
        var activeItemTemplate = Require<DataTemplate>(
            window.FindResource("ShowcaseActiveItemTemplate"),
            "active selector item DataTemplate");
        var inactiveItemTemplate = Require<DataTemplate>(
            window.FindResource("ShowcaseInactiveItemTemplate"),
            "inactive selector item DataTemplate");
        var itemTemplateSelector = Require<ShowcaseItemTemplateSelector>(
            window.FindResource("ShowcaseItemTemplateSelector"),
            "item template selector");
        var selectorItemContainerStyle = Require<Style>(
            window.FindResource("ShowcaseSelectorItemContainerStyle"),
            "selector item container style");
        var activeItemContainerStyle = Require<Style>(
            window.FindResource("ShowcaseActiveItemContainerStyle"),
            "active item container style");
        var inactiveItemContainerStyle = Require<Style>(
            window.FindResource("ShowcaseInactiveItemContainerStyle"),
            "inactive item container style");
        var itemContainerStyleSelector = Require<ShowcaseItemContainerStyleSelector>(
            window.FindResource("ShowcaseItemContainerStyleSelector"),
            "item container style selector");
        var selectedItemTemplate = Require<DataTemplate>(
            application.TryFindResource("SelectedItemTemplate"),
            "selected item DataTemplate");
        AssertEqual(Color.FromRgb(0xF4, 0xF7, 0xFB), panelBrush.Color, "Showcase panel brush color");
        AssertEqual(typeof(Button), buttonStyle.TargetType, "app Button implicit style target type");
        AssertEqual(typeof(Button), basedOnButtonStyle.TargetType, "BasedOn Button style target type");
        AssertEqual(buttonStyle, basedOnButtonStyle.BasedOn, "BasedOn Button style base style");
        AssertEqual(typeof(Button), templateButtonStyle.TargetType, "template Button style target type");
        AssertEqual(typeof(TextBlock), triggerTextBlockStyle.TargetType, "trigger TextBlock style target type");
        AssertEqual(typeof(TextBlock), multiTriggerTextBlockStyle.TargetType, "MultiTrigger TextBlock style target type");
        AssertEqual(typeof(TextBlock), multiDataTriggerTextBlockStyle.TargetType, "MultiDataTrigger TextBlock style target type");
        AssertEqual(typeof(Button), eventSetterButtonStyle.TargetType, "EventSetter Button style target type");
        AssertEqual(typeof(ShowcaseItem), implicitItemTemplate.DataType, "implicit item template data type");
        AssertEqual(typeof(ShowcaseItem), selectedItemTemplate.DataType, "selected item template data type");
        var mainMenu = Require<Menu>(window.FindName("MainMenu"), "main Menu");
        var fileMenuItem = Require<MenuItem>(window.FindName("FileMenuItem"), "file MenuItem");
        var viewMenuItem = Require<MenuItem>(window.FindName("ViewMenuItem"), "view MenuItem");
        var windowMenuItem = Require<MenuItem>(window.FindName("WindowMenuItem"), "window MenuItem");
        var themeMenuItem = Require<MenuItem>(window.FindName("ThemeMenuItem"), "theme MenuItem");
        var addMenuItem = Require<MenuItem>(window.FindName("AddMenuItem"), "add MenuItem");
        var resetMenuItem = Require<MenuItem>(window.FindName("ResetMenuItem"), "reset MenuItem");
        var aboutMenuItem = Require<MenuItem>(window.FindName("AboutMenuItem"), "about MenuItem");
        var refreshMenuItem = Require<MenuItem>(window.FindName("RefreshMenuItem"), "refresh MenuItem");
        var actionsEnabledMenuItem = Require<MenuItem>(
            window.FindName("ActionsEnabledMenuItem"),
            "actions enabled MenuItem");
        var windowMaximizeMenuItem = Require<MenuItem>(
            window.FindName("WindowMaximizeMenuItem"),
            "window maximize MenuItem");
        var windowMinimizeMenuItem = Require<MenuItem>(
            window.FindName("WindowMinimizeMenuItem"),
            "window minimize MenuItem");
        var windowRestoreMenuItem = Require<MenuItem>(
            window.FindName("WindowRestoreMenuItem"),
            "window restore MenuItem");
        var windowSystemMenuItem = Require<MenuItem>(
            window.FindName("WindowSystemMenuItem"),
            "window system menu MenuItem");
        var commandStatusText = Require<TextBlock>(
            window.FindName("CommandStatusText"),
            "command status TextBlock");
        var requeryCommandButton = Require<Button>(
            window.FindName("RequeryCommandButton"),
            "requery command Button");
        var messageBoxButton = Require<Button>(
            window.FindName("ShowcaseMessageBoxButton"),
            "MessageBox Button");
        var messageBoxStatusText = Require<TextBlock>(
            window.FindName("MessageBoxStatusText"),
            "MessageBox status TextBlock");
        var fileDialogButton = Require<Button>(
            window.FindName("ShowcaseFileDialogButton"),
            "file dialog Button");
        var fileDialogStatusText = Require<TextBlock>(
            window.FindName("FileDialogStatusText"),
            "file dialog status TextBlock");
        var chromeCaptionRegion = Require<Border>(
            window.FindName("ChromeCaptionRegion"),
            "chrome caption region Border");
        var chromeHitTestButton = Require<Button>(
            window.FindName("ChromeHitTestButton"),
            "chrome hit-test Button");
        var chromeResizeGrip = Require<Thumb>(
            window.FindName("ChromeResizeGrip"),
            "chrome resize-grip Thumb");
        var showcaseTabControl = Require<TabControl>(
            window.FindName("ShowcaseTabControl"),
            "Showcase TabControl");
        var nameTextBox = Require<TextBox>(window.FindName("NameTextBox"), "name TextBox");
        var itemsList = Require<ListBox>(window.FindName("ItemsList"), "items ListBox");
        var itemsDataGrid = Require<DataGrid>(window.FindName("ItemsDataGrid"), "items DataGrid");
        var selectedItemSummaryText = Require<TextBlock>(
            window.FindName("SelectedItemSummaryText"),
            "selected item summary TextBlock");
        var activeOnlyCheckBox = Require<CheckBox>(
            window.FindName("ActiveOnlyCheckBox"),
            "active-only CheckBox");
        var groupedItemsList = Require<ListBox>(
            window.FindName("GroupedItemsList"),
            "grouped items ListBox");
        var formattedItemsList = Require<ListBox>(
            window.FindName("FormattedItemsList"),
            "formatted items ListBox");
        var priorityBindingText = Require<TextBlock>(
            window.FindName("PriorityBindingText"),
            "priority binding TextBlock");
        var fallbackBindingText = Require<TextBlock>(
            window.FindName("FallbackBindingText"),
            "fallback binding TextBlock");
        var targetNullBindingText = Require<TextBlock>(
            window.FindName("TargetNullBindingText"),
            "target-null binding TextBlock");
        var bindingTargetUpdatedText = Require<TextBlock>(
            window.FindName("BindingTargetUpdatedText"),
            "binding TargetUpdated TextBlock");
        var bindingSourceUpdatedTextBox = Require<TextBox>(
            window.FindName("BindingSourceUpdatedTextBox"),
            "binding SourceUpdated TextBox");
        var relativeSelfBindingText = Require<TextBlock>(
            window.FindName("RelativeSelfBindingText"),
            "relative self binding TextBlock");
        var relativeAncestorBorder = Require<Border>(
            window.FindName("RelativeAncestorBorder"),
            "relative ancestor Border");
        var relativeAncestorBindingText = Require<TextBlock>(
            window.FindName("RelativeAncestorBindingText"),
            "relative ancestor binding TextBlock");
        var selectorGroupBox = Require<GroupBox>(
            window.FindName("SelectorGroupBox"),
            "selector GroupBox");
        var selectedValueComboBox = Require<ComboBox>(
            window.FindName("SelectedValueComboBox"),
            "selected value ComboBox");
        var multiSelectItemsList = Require<ListBox>(
            window.FindName("MultiSelectItemsList"),
            "multi-select ListBox");
        var selectorExpander = Require<Expander>(
            window.FindName("SelectorExpander"),
            "selector Expander");
        var selectorScrollViewer = Require<ScrollViewer>(
            window.FindName("SelectorScrollViewer"),
            "selector ScrollViewer");
        var selectorScrollText = Require<TextBlock>(
            window.FindName("SelectorScrollText"),
            "selector scroll TextBlock");
        var showcaseToolBarTray = Require<ToolBarTray>(
            window.FindName("ShowcaseToolBarTray"),
            "Showcase ToolBarTray");
        var showcaseToolBar = Require<ToolBar>(
            window.FindName("ShowcaseToolBar"),
            "Showcase ToolBar");
        var toolBarRefreshButton = Require<Button>(
            window.FindName("ToolBarRefreshButton"),
            "toolbar refresh Button");
        var toolBarSeparator = Require<Separator>(
            window.FindName("ToolBarSeparator"),
            "toolbar Separator");
        var toolBarToggleButton = Require<ToggleButton>(
            window.FindName("ToolBarToggleButton"),
            "toolbar ToggleButton");
        var popupOwnerButton = Require<Button>(
            window.FindName("PopupOwnerButton"),
            "popup owner Button");
        var inputPopup = Require<Popup>(
            window.FindName("InputPopup"),
            "input Popup");
        var inputToggleButton = Require<ToggleButton>(
            window.FindName("InputToggleButton"),
            "input ToggleButton");
        var frameworkRadioButton = Require<RadioButton>(
            window.FindName("FrameworkRadioButton"),
            "framework RadioButton");
        var renderingRadioButton = Require<RadioButton>(
            window.FindName("RenderingRadioButton"),
            "rendering RadioButton");
        var inputRepeatButton = Require<RepeatButton>(
            window.FindName("InputRepeatButton"),
            "input RepeatButton");
        var inputThumbPanel = Require<StackPanel>(
            window.FindName("InputThumbPanel"),
            "input Thumb panel");
        var inputDragThumb = Require<Thumb>(
            window.FindName("InputDragThumb"),
            "input drag Thumb");
        var inputDragStatusText = Require<TextBlock>(
            window.FindName("InputDragStatusText"),
            "input drag status TextBlock");
        var showcaseDropTarget = Require<Border>(
            window.FindName("ShowcaseDropTarget"),
            "Showcase drop target Border");
        var showcaseDropTargetText = Require<TextBlock>(
            window.FindName("ShowcaseDropTargetText"),
            "Showcase drop target TextBlock");
        var inputCalendar = Require<WpfCalendar>(
            window.FindName("InputCalendar"),
            "input Calendar");
        var inputDatePicker = Require<DatePicker>(
            window.FindName("InputDatePicker"),
            "input DatePicker");
        var keyboardNavigationPanel = Require<StackPanel>(
            window.FindName("KeyboardNavigationPanel"),
            "keyboard navigation StackPanel");
        var keyboardNavigationAccessLabel = Require<Label>(
            window.FindName("KeyboardNavigationAccessLabel"),
            "keyboard navigation access Label");
        var keyboardNavigationFirstBox = Require<TextBox>(
            window.FindName("KeyboardNavigationFirstBox"),
            "first keyboard navigation TextBox");
        var keyboardNavigationSecondButton = Require<Button>(
            window.FindName("KeyboardNavigationSecondButton"),
            "second keyboard navigation Button");
        var keyboardNavigationThirdBox = Require<TextBox>(
            window.FindName("KeyboardNavigationThirdBox"),
            "third keyboard navigation TextBox");
        var showcaseDockPanel = Require<DockPanel>(
            window.FindName("ShowcaseDockPanel"),
            "Showcase DockPanel");
        var dockTopBand = Require<Border>(
            window.FindName("DockTopBand"),
            "dock top Border");
        var dockLeftBand = Require<Border>(
            window.FindName("DockLeftBand"),
            "dock left Border");
        var dockRightBand = Require<Border>(
            window.FindName("DockRightBand"),
            "dock right Border");
        var dockFillText = Require<TextBlock>(
            window.FindName("DockFillText"),
            "dock fill TextBlock");
        var showcaseWrapPanel = Require<WrapPanel>(
            window.FindName("ShowcaseWrapPanel"),
            "Showcase WrapPanel");
        var showcaseUniformGrid = Require<UniformGrid>(
            window.FindName("ShowcaseUniformGrid"),
            "Showcase UniformGrid");
        var showcaseShapeCanvas = Require<Canvas>(
            window.FindName("ShowcaseShapeCanvas"),
            "Showcase shape Canvas");
        var showcaseShapeRectangle = Require<System.Windows.Shapes.Rectangle>(
            window.FindName("ShowcaseShapeRectangle"),
            "Showcase shape Rectangle");
        var showcaseShapeEllipse = Require<System.Windows.Shapes.Ellipse>(
            window.FindName("ShowcaseShapeEllipse"),
            "Showcase shape Ellipse");
        var showcaseShapeLine = Require<System.Windows.Shapes.Line>(
            window.FindName("ShowcaseShapeLine"),
            "Showcase shape Line");
        var showcaseShapePath = Require<System.Windows.Shapes.Path>(
            window.FindName("ShowcaseShapePath"),
            "Showcase shape Path");
        var showcaseGridSplitterGrid = Require<Grid>(
            window.FindName("ShowcaseGridSplitterGrid"),
            "Showcase GridSplitter grid");
        var splitterLeftColumn = Require<ColumnDefinition>(
            window.FindName("SplitterLeftColumn"),
            "splitter left ColumnDefinition");
        var splitterRightColumn = Require<ColumnDefinition>(
            window.FindName("SplitterRightColumn"),
            "splitter right ColumnDefinition");
        var splitterLeftPane = Require<Border>(
            window.FindName("SplitterLeftPane"),
            "splitter left Border");
        var showcaseGridSplitter = Require<GridSplitter>(
            window.FindName("ShowcaseGridSplitter"),
            "Showcase GridSplitter");
        var splitterRightPane = Require<Border>(
            window.FindName("SplitterRightPane"),
            "splitter right Border");
        var showcaseViewbox = Require<Viewbox>(
            window.FindName("ShowcaseViewbox"),
            "Showcase Viewbox");
        var viewboxText = Require<TextBlock>(
            window.FindName("ViewboxText"),
            "viewbox TextBlock");
        var componentResourceText = Require<TextBlock>(
            window.FindName("ComponentResourceText"),
            "component resource TextBlock");
        var localizedResourceText = Require<TextBlock>(
            window.FindName("LocalizedResourceText"),
            "localized resource TextBlock");
        var resourceAccessText = Require<AccessText>(
            window.FindName("ResourceAccessText"),
            "resource AccessText");
        var objectProviderText = Require<TextBlock>(
            window.FindName("ObjectProviderText"),
            "object data provider TextBlock");
        var xmlProviderText = Require<TextBlock>(
            window.FindName("XmlProviderText"),
            "XML data provider TextBlock");
        var resourceArrayItemsControl = Require<ItemsControl>(
            window.FindName("ResourceArrayItemsControl"),
            "resource array ItemsControl");
        var resourceCompositeItemsControl = Require<ItemsControl>(
            window.FindName("ResourceCompositeItemsControl"),
            "resource composite ItemsControl");
        var nullIntrinsicText = Require<TextBlock>(
            window.FindName("NullIntrinsicText"),
            "null intrinsic TextBlock");
        var markupExtensionText = Require<TextBlock>(
            window.FindName("MarkupExtensionText"),
            "MarkupExtension TextBlock");
        var packResourceText = Require<TextBlock>(
            window.FindName("PackResourceText"),
            "pack resource TextBlock");
        var componentPackResourceText = Require<TextBlock>(
            window.FindName("ComponentPackResourceText"),
            "component pack resource TextBlock");
        var startupResourceText = Require<TextBlock>(
            window.FindName("StartupResourceText"),
            "startup resource TextBlock");
        var systemParameterText = Require<TextBlock>(
            window.FindName("SystemParameterText"),
            "SystemParameters TextBlock");
        var systemFontText = Require<TextBlock>(
            window.FindName("SystemFontText"),
            "SystemFonts TextBlock");
        var systemColorBorder = Require<Border>(
            window.FindName("SystemColorBorder"),
            "SystemColors Border");
        var systemColorText = Require<TextBlock>(
            window.FindName("SystemColorText"),
            "SystemColors TextBlock");
        var showcaseThemedControl = Require<ShowcaseThemedControl>(
            window.FindName("ShowcaseThemedControl"),
            "Showcase themed control");
        var drawingImageControl = Require<Image>(
            window.FindName("ShowcaseDrawingImageControl"),
            "Showcase DrawingImage Image");
        var drawingImageBrushBorder = Require<Border>(
            window.FindName("ShowcaseDrawingImageBrushBorder"),
            "Showcase DrawingImageBrush Border");
        var resourceDynamicBorder = Require<Border>(
            window.FindName("ResourceDynamicBorder"),
            "resource DynamicResource Border");
        var selectedItemContent = Require<ContentControl>(
            window.FindName("SelectedItemContent"),
            "selected item ContentControl");
        var implicitTemplateContent = Require<ContentControl>(
            window.FindName("ImplicitTemplateContent"),
            "implicit template ContentControl");
        var selectorItemsList = Require<ListBox>(
            window.FindName("SelectorItemsList"),
            "selector items ListBox");
        var styleSelectorItemsList = Require<ListBox>(
            window.FindName("StyleSelectorItemsList"),
            "style selector items ListBox");
        var templateButton = Require<Button>(window.FindName("TemplateButton"), "template Button");
        var basedOnStyleButton = Require<Button>(
            window.FindName("BasedOnStyleButton"),
            "BasedOn style Button");
        var styleTriggerText = Require<TextBlock>(
            window.FindName("StyleTriggerText"),
            "style trigger TextBlock");
        var multiTriggerText = Require<TextBlock>(
            window.FindName("MultiTriggerText"),
            "MultiTrigger TextBlock");
        var multiDataTriggerText = Require<TextBlock>(
            window.FindName("MultiDataTriggerText"),
            "MultiDataTrigger TextBlock");
        var eventSetterStyleButton = Require<Button>(
            window.FindName("EventSetterStyleButton"),
            "EventSetter style Button");
        var eventSetterStatusText = Require<TextBlock>(
            window.FindName("EventSetterStatusText"),
            "EventSetter status TextBlock");
        var localThemeScope = Require<StackPanel>(
            window.FindName("LocalThemeScope"),
            "local theme scope StackPanel");
        var localThemeText = Require<TextBlock>(
            window.FindName("LocalThemeText"),
            "local theme TextBlock");
        var validationTextBox = Require<TextBox>(
            window.FindName("ValidationTextBox"),
            "validation TextBox");
        var validationEchoText = Require<TextBlock>(
            window.FindName("ValidationEchoText"),
            "validation echo TextBlock");
        var dataErrorTextBox = Require<TextBox>(
            window.FindName("DataErrorTextBox"),
            "IDataErrorInfo TextBox");
        var dataErrorEchoText = Require<TextBlock>(
            window.FindName("DataErrorEchoText"),
            "IDataErrorInfo echo TextBlock");
        var notifyDataErrorTextBox = Require<TextBox>(
            window.FindName("NotifyDataErrorTextBox"),
            "INotifyDataErrorInfo TextBox");
        var notifyDataErrorEchoText = Require<TextBlock>(
            window.FindName("NotifyDataErrorEchoText"),
            "INotifyDataErrorInfo echo TextBlock");
        var bindingGroupPanel = Require<StackPanel>(
            window.FindName("BindingGroupPanel"),
            "BindingGroup panel");
        var bindingGroupFirstBox = Require<TextBox>(
            window.FindName("BindingGroupFirstBox"),
            "BindingGroup first TextBox");
        var bindingGroupLastBox = Require<TextBox>(
            window.FindName("BindingGroupLastBox"),
            "BindingGroup last TextBox");
        var bindingGroupCommitButton = Require<Button>(
            window.FindName("BindingGroupCommitButton"),
            "BindingGroup commit Button");
        var bindingGroupStatusText = Require<TextBlock>(
            window.FindName("BindingGroupStatusText"),
            "BindingGroup status TextBlock");
        var bindingGroupFirstEchoText = Require<TextBlock>(
            window.FindName("BindingGroupFirstEchoText"),
            "BindingGroup first echo TextBlock");
        var bindingGroupLastEchoText = Require<TextBlock>(
            window.FindName("BindingGroupLastEchoText"),
            "BindingGroup last echo TextBlock");
        var showcaseAdornerDecorator = Require<AdornerDecorator>(
            window.FindName("ShowcaseAdornerDecorator"),
            "Showcase AdornerDecorator");
        var showcaseAdornerTarget = Require<Border>(
            window.FindName("ShowcaseAdornerTarget"),
            "Showcase adorner target Border");
        var showcaseAdornerStatusText = Require<TextBlock>(
            window.FindName("ShowcaseAdornerStatusText"),
            "Showcase adorner status TextBlock");
        var loadedStoryboardText = Require<TextBlock>(
            window.FindName("LoadedStoryboardText"),
            "loaded storyboard TextBlock");
        var clickStoryboardButton = Require<Button>(
            window.FindName("ClickStoryboardButton"),
            "click storyboard Button");
        var dropShadowEffectBorder = Require<Border>(
            window.FindName("ShowcaseDropShadowEffectBorder"),
            "Showcase DropShadowEffect Border");
        var blurEffectBorder = Require<Border>(
            window.FindName("ShowcaseBlurEffectBorder"),
            "Showcase BlurEffect Border");
        var summaryPanel = Require<SummaryPanel>(window.FindName("SummaryPanel"), "summary Panel");
        var dependencyPropertyManagerText = Require<ShowcaseHeaderTextBlock>(
            window.FindName("DependencyPropertyManagerText"),
            "dependency property manager TextBlock");
        var showcaseRoutedEventScope = Require<StackPanel>(
            window.FindName("ShowcaseRoutedEventScope"),
            "Showcase routed-event scope StackPanel");
        var showcaseRoutedEventButton = Require<ShowcaseRoutedEventButton>(
            window.FindName("ShowcaseRoutedEventButton"),
            "Showcase routed-event Button");
        var showcaseRoutedEventStatusText = Require<TextBlock>(
            window.FindName("ShowcaseRoutedEventStatusText"),
            "Showcase routed-event status TextBlock");
        var summaryHeaderText = Require<TextBlock>(
            summaryPanel.FindName("SummaryHeaderText"),
            "summary header text");
        var summaryNameText = Require<TextBlock>(
            summaryPanel.FindName("SummaryNameText"),
            "summary name text");
        var summaryCategoryText = Require<TextBlock>(
            summaryPanel.FindName("SummaryCategoryText"),
            "summary category text");
        var summaryProgressText = Require<TextBlock>(
            summaryPanel.FindName("SummaryProgressText"),
            "summary progress text");
        var nodesTreeView = Require<TreeView>(window.FindName("NodesTreeView"), "nodes TreeView");
        var explicitExplorerTreeView = Require<TreeView>(
            window.FindName("ExplicitExplorerTreeView"),
            "explicit explorer TreeView");
        var explicitExplorerAlpha = Require<TreeViewItem>(
            window.FindName("ExplicitExplorerAlpha"),
            "explicit explorer alpha TreeViewItem");
        var explicitExplorerAlphaChild = Require<TreeViewItem>(
            window.FindName("ExplicitExplorerAlphaChild"),
            "explicit explorer alpha child TreeViewItem");
        var explicitExplorerBeta = Require<TreeViewItem>(
            window.FindName("ExplicitExplorerBeta"),
            "explicit explorer beta TreeViewItem");
        var explicitExplorerTreeStatusText = Require<TextBlock>(
            window.FindName("ExplicitExplorerTreeStatusText"),
            "explicit explorer tree status TextBlock");
        var explorerListView = Require<ListView>(
            window.FindName("ExplorerListView"),
            "explorer ListView");
        var navigationFrame = Require<Frame>(window.FindName("NavigationFrame"), "navigation Frame");
        var detailsNavigationButton = Require<Button>(
            window.FindName("DetailsNavigationButton"),
            "details navigation Button");
        var backNavigationButton = Require<Button>(
            window.FindName("BackNavigationButton"),
            "back navigation Button");
        var forwardNavigationButton = Require<Button>(
            window.FindName("ForwardNavigationButton"),
            "forward navigation Button");
        var editorPasswordBox = Require<PasswordBox>(
            window.FindName("EditorPasswordBox"),
            "editor PasswordBox");
        var editorRichTextBox = Require<RichTextBox>(
            window.FindName("EditorRichTextBox"),
            "editor RichTextBox");
        var dataObjectPayloadTextBox = Require<TextBox>(
            window.FindName("DataObjectPayloadTextBox"),
            "DataObject payload TextBox");
        var dataObjectRoundTripButton = Require<Button>(
            window.FindName("DataObjectRoundTripButton"),
            "DataObject round-trip Button");
        var clipboardRoundTripButton = Require<Button>(
            window.FindName("ClipboardRoundTripButton"),
            "Clipboard round-trip Button");
        var selectAllPayloadButton = Require<Button>(
            window.FindName("SelectAllPayloadButton"),
            "SelectAll payload Button");
        var copyPayloadButton = Require<Button>(
            window.FindName("CopyPayloadButton"),
            "copy payload Button");
        var cutPayloadButton = Require<Button>(
            window.FindName("CutPayloadButton"),
            "cut payload Button");
        var pastePayloadButton = Require<Button>(
            window.FindName("PastePayloadButton"),
            "paste payload Button");
        var undoPayloadButton = Require<Button>(
            window.FindName("UndoPayloadButton"),
            "undo payload Button");
        var redoPayloadButton = Require<Button>(
            window.FindName("RedoPayloadButton"),
            "redo payload Button");
        var copyRichTextButton = Require<Button>(
            window.FindName("CopyRichTextButton"),
            "copy rich text Button");
        var pasteRichTextButton = Require<Button>(
            window.FindName("PasteRichTextButton"),
            "paste rich text Button");
        var dataObjectStatusText = Require<TextBlock>(
            window.FindName("DataObjectStatusText"),
            "DataObject status TextBlock");
        var documentViewer = Require<FlowDocumentScrollViewer>(
            window.FindName("DocumentViewer"),
            "document FlowDocumentScrollViewer");
        var documentPageViewer = Require<FlowDocumentPageViewer>(
            window.FindName("DocumentPageViewer"),
            "document FlowDocumentPageViewer");
        var documentReader = Require<FlowDocumentReader>(
            window.FindName("DocumentReader"),
            "document FlowDocumentReader");
        var enabledCheckBox = Require<CheckBox>(window.FindName("EnabledCheckBox"), "enabled CheckBox");
        var progressSlider = Require<Slider>(window.FindName("ProgressSlider"), "progress Slider");
        Require<ComboBox>(window.FindName("CategoryCombo"), "category ComboBox");
        AssertEqual(4, mainMenu.Items.Count, "main menu item count");
        AssertEqual(5, fileMenuItem.Items.Count, "file menu item count");
        AssertEqual(3, viewMenuItem.Items.Count, "view menu item count");
        AssertEqual(5, windowMenuItem.Items.Count, "window menu item count");
        AssertEqual(viewModel.AddItemCommand, addMenuItem.Command, "add menu command binding");
        AssertEqual("Ctrl+N", addMenuItem.InputGestureText, "add menu input gesture text");
        AssertEqual(viewModel.ResetCommand, resetMenuItem.Command, "reset menu command binding");
        AssertEqual("_About", aboutMenuItem.Header, "about menu item header");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshMenuItem.Command, "refresh menu routed command");
        AssertEqual("Ctrl+R", refreshMenuItem.InputGestureText, "refresh menu input gesture text");
        AssertEqual(5, window.CommandBindings.Count, "window command binding count");
        AssertEqual(MainWindow.RefreshStatusCommand, window.CommandBindings[0].Command, "window routed command binding");
        AssertEqual(SystemCommands.MaximizeWindowCommand, window.CommandBindings[1].Command, "window maximize command binding");
        AssertEqual(SystemCommands.MinimizeWindowCommand, window.CommandBindings[2].Command, "window minimize command binding");
        AssertEqual(SystemCommands.RestoreWindowCommand, window.CommandBindings[3].Command, "window restore command binding");
        AssertEqual(SystemCommands.ShowSystemMenuCommand, window.CommandBindings[4].Command, "window system-menu command binding");
        AssertEqual(2, window.InputBindings.Count, "window input binding count");
        ValidateShowcaseSystemCommands(
            window,
            windowMaximizeMenuItem,
            windowMinimizeMenuItem,
            windowRestoreMenuItem,
            windowSystemMenuItem);
        ValidateShowcaseWindowChrome(
            window,
            chromeCaptionRegion,
            chromeHitTestButton,
            chromeResizeGrip);
        ValidateMessageBox(window, messageBoxButton, messageBoxStatusText);
        ValidateFileDialogs(window, fileDialogButton, fileDialogStatusText);
        var refreshKeyBinding = Require<KeyBinding>(window.InputBindings[0], "refresh KeyBinding");
        AssertEqual(Key.R, refreshKeyBinding.Key, "refresh key binding key");
        AssertEqual(ModifierKeys.Control, refreshKeyBinding.Modifiers, "refresh key binding modifiers");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshKeyBinding.Command, "refresh key binding command");
        var refreshMouseBinding = Require<MouseBinding>(window.InputBindings[1], "refresh MouseBinding");
        AssertEqual(MouseAction.LeftDoubleClick, refreshMouseBinding.MouseAction, "refresh mouse binding action");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshMouseBinding.Command, "refresh mouse binding command");
        AssertEqual("showcase mouse binding payload", refreshMouseBinding.CommandParameter, "refresh mouse binding parameter");
        AssertEqual(window, refreshMouseBinding.CommandTarget, "refresh mouse binding target");
        ValidateShowcaseTabControl(window, viewModel, showcaseTabControl);
        AssertEqual(true, actionsEnabledMenuItem.IsCheckable, "actions menu checkable state");
        AssertEqual(true, actionsEnabledMenuItem.IsChecked, "actions menu initial checked state");
        AssertEqual(viewModel.Items, itemsDataGrid.ItemsSource, "DataGrid items source");
        AssertEqual(3, itemsDataGrid.Columns.Count, "DataGrid column count");
        AssertEqual("Name", GetColumnBindingPath(itemsDataGrid.Columns[0]), "DataGrid name column binding");
        AssertEqual("Category", GetColumnBindingPath(itemsDataGrid.Columns[1]), "DataGrid category column binding");
        AssertEqual("IsActive", GetColumnBindingPath(itemsDataGrid.Columns[2]), "DataGrid active column binding");
        ValidateCollectionView(window, viewModel, groupedItemsList, activeOnlyCheckBox, activeTextConverter);
        ValidateFormattedItemsList(window, viewModel, formattedItemsList);
        ValidateSelectedSummaryBinding(selectedItemSummaryText, itemSummaryConverter);
        AssertEqual(viewModel.SelectedItem, selectedItemContent.Content, "selected item content");
        AssertEqual(
            selectedItemTemplate,
            selectedItemContent.ContentTemplate,
            "selected item content template");
        ValidateSelectedItemTemplate(selectedItemTemplate);
        ValidateImplicitItemTemplate(viewModel, implicitTemplateContent, implicitItemTemplate);
        ValidateTemplateSelector(
            viewModel,
            selectorItemsList,
            activeItemTemplate,
            inactiveItemTemplate,
            itemTemplateSelector,
            selectorItemContainerStyle);
        ValidateItemContainerStyleSelector(
            viewModel,
            styleSelectorItemsList,
            activeItemContainerStyle,
            inactiveItemContainerStyle,
            itemContainerStyleSelector);
        ValidateBasedOnButton(basedOnStyleButton, basedOnButtonStyle);
        ValidateStyleTriggersAndEventSetter(
            window,
            viewModel,
            styleTriggerText,
            triggerTextBlockStyle,
            multiTriggerText,
            multiTriggerTextBlockStyle,
            multiDataTriggerText,
            multiDataTriggerTextBlockStyle,
            eventSetterStyleButton,
            eventSetterButtonStyle,
            eventSetterStatusText);
        ValidateLocalThemeResources(window, localThemeScope, localThemeText);
        ValidateTemplateButton(window, templateButton, templateButtonStyle);
        ValidateValidation(window, viewModel, validationTextBox, validationEchoText);
        ValidateDataErrorValidation(window, viewModel, dataErrorTextBox, dataErrorEchoText);
        ValidateNotifyDataErrorValidation(
            window,
            viewModel,
            notifyDataErrorTextBox,
            notifyDataErrorEchoText);
        ValidateBindingGroup(
            window,
            viewModel,
            bindingGroupPanel,
            bindingGroupFirstBox,
            bindingGroupLastBox,
            bindingGroupCommitButton,
            bindingGroupStatusText,
            bindingGroupFirstEchoText,
            bindingGroupLastEchoText);
        ValidateAdornerLayer(
            window,
            showcaseAdornerDecorator,
            showcaseAdornerTarget,
            showcaseAdornerStatusText);
        ValidateStoryboards(window, loadedStoryboardText, clickStoryboardButton, expectLoadedStoryboardApplied);
        ValidateNativeEffects(dropShadowEffectBorder, blurEffectBorder);
        AssertEqual(viewModel.Nodes, nodesTreeView.ItemsSource, "TreeView items source");
        AssertEqual(2, viewModel.Nodes.Count, "TreeView root node count");
        AssertEqual("Startup", viewModel.Nodes[0].Children[0].Name, "TreeView first child node");
        var nodeTemplate = Require<HierarchicalDataTemplate>(
            nodesTreeView.ItemTemplate,
            "node hierarchical data template");
        AssertEqual("Children", GetTemplateItemsSourcePath(nodeTemplate), "TreeView hierarchical template ItemsSource path");
        ValidateExplicitExplorerTree(
            window,
            explicitExplorerTreeView,
            explicitExplorerAlpha,
            explicitExplorerAlphaChild,
            explicitExplorerBeta,
            explicitExplorerTreeStatusText);
        var explorerGridView = Require<GridView>(explorerListView.View, "explorer GridView");
        AssertEqual(viewModel.Items, explorerListView.ItemsSource, "explorer ListView ItemsSource");
        AssertEqual(viewModel.SelectedItem, explorerListView.SelectedItem, "explorer ListView selected item");
        AssertEqual(false, explorerGridView.AllowsColumnReorder, "explorer GridView column reorder state");
        AssertEqual(3, explorerGridView.Columns.Count, "explorer GridView column count");
        AssertEqual("Name", explorerGridView.Columns[0].Header, "explorer GridView name header");
        AssertEqual("Name", GetGridViewColumnBindingPath(explorerGridView.Columns[0]), "explorer GridView name binding");
        AssertEqual("Category", explorerGridView.Columns[1].Header, "explorer GridView category header");
        AssertEqual("Category", GetGridViewColumnBindingPath(explorerGridView.Columns[1]), "explorer GridView category binding");
        AssertEqual("Active", explorerGridView.Columns[2].Header, "explorer GridView active header");
        AssertEqual("IsActive", GetGridViewColumnBindingPath(explorerGridView.Columns[2]), "explorer GridView active binding");
        DrainDispatcher(window);
        AssertEqual("Commands idle", commandStatusText.Text, "initial command status text");
        AssertEqual("Overview tools", ShowcaseStateProperties.GetSectionName(dependencyPropertyManagerText), "inherited attached section value");
        AssertEqual(
            BaseValueSource.Inherited,
            DependencyPropertyHelper.GetValueSource(
                dependencyPropertyManagerText,
                ShowcaseStateProperties.SectionNameProperty).BaseValueSource,
            "inherited attached section value source");
        AssertEqual("Overview tools", dependencyPropertyManagerText.Text, "inherited attached section text");
        AssertEqual(100d, ShowcaseStateProperties.GetImportance(dependencyPropertyManagerText), "coerced attached importance value");
        AssertGreaterThan(0, ShowcaseStateProperties.ImportanceChangedCount, "attached importance changed callback count");
        AssertEqual("StatusText", GetBindingPath(dependencyPropertyManagerText, ShowcaseHeaderTextBlock.HeaderTextProperty), "AddOwner header binding path");
        AssertEqual("Alpha selected, progress 35%", dependencyPropertyManagerText.HeaderText, "AddOwner initial header property");
        AssertEqual(FontWeights.SemiBold, dependencyPropertyManagerText.FontWeight, "metadata override FontWeight value");
        AssertEqual(
            Brushes.DarkSlateBlue,
            ShowcaseHeaderTextBlock.ForegroundProperty.GetMetadata(typeof(ShowcaseHeaderTextBlock)).DefaultValue,
            "metadata override Foreground default value");
        AssertEqual(new ShowcaseTypedOffset(12.5, 24.25), dependencyPropertyManagerText.TypedOffset, "TypeConverter dependency property value");
        AssertEqual(
            BaseValueSource.Local,
            DependencyPropertyHelper.GetValueSource(
                dependencyPropertyManagerText,
                ShowcaseHeaderTextBlock.TypedOffsetProperty).BaseValueSource,
            "TypeConverter dependency property value source");
        ValidateShowcaseRoutedEvent(window, showcaseRoutedEventScope, showcaseRoutedEventButton, showcaseRoutedEventStatusText);
        AssertEqual("StatusText", GetBindingPath(summaryPanel, SummaryPanel.HeaderTextProperty), "summary header binding path");
        AssertEqual("Alpha selected, progress 35%", summaryPanel.HeaderText, "summary initial header property");
        AssertEqual("Alpha selected, progress 35%", summaryHeaderText.Text, "summary initial header text");
        AssertEqual("Name: Alpha", summaryNameText.Text, "summary initial name text");
        AssertEqual("Category: Framework", summaryCategoryText.Text, "summary initial category text");
        AssertEqual("Progress: 35%", summaryProgressText.Text, "summary initial progress text");
        summaryPanel.SetCurrentValue(SummaryPanel.HeaderTextProperty, "Manual dependency property header");
        DrainDispatcher(window);
        AssertEqual("Manual dependency property header", summaryHeaderText.Text, "summary SetCurrentValue header text");
        UpdateBinding(summaryPanel, SummaryPanel.HeaderTextProperty);
        DrainDispatcher(window);
        AssertEqual("Alpha selected, progress 35%", summaryHeaderText.Text, "summary rebound header text");
        AssertEqual("Alpha / Framework / 35%", selectedItemSummaryText.Text, "initial selected summary text");
        ValidateBindingFallbacks(
            window,
            viewModel,
            priorityBindingText,
            fallbackBindingText,
            targetNullBindingText,
            bindingTargetUpdatedText,
            bindingSourceUpdatedTextBox,
            relativeSelfBindingText,
            relativeAncestorBorder,
            relativeAncestorBindingText);
        ValidateSelectorControls(
            window,
            viewModel,
            selectorGroupBox,
            selectedValueComboBox,
            multiSelectItemsList,
            selectorExpander,
            selectorScrollViewer,
            selectorScrollText);
        ValidateInputControls(
            window,
            viewModel,
            showcaseToolBarTray,
            showcaseToolBar,
            toolBarRefreshButton,
            toolBarSeparator,
            toolBarToggleButton,
            popupOwnerButton,
            inputPopup,
            inputToggleButton,
            frameworkRadioButton,
            renderingRadioButton,
            inputRepeatButton,
            inputThumbPanel,
            inputDragThumb,
            inputDragStatusText,
            showcaseDropTarget,
            showcaseDropTargetText,
            inputCalendar,
            inputDatePicker,
            keyboardNavigationPanel,
            keyboardNavigationAccessLabel,
            keyboardNavigationFirstBox,
            keyboardNavigationSecondButton,
            keyboardNavigationThirdBox);
        ValidateLayoutControls(
            showcaseDockPanel,
            dockTopBand,
            dockLeftBand,
            dockRightBand,
            dockFillText,
            showcaseWrapPanel,
            showcaseUniformGrid,
            showcaseShapeCanvas,
            showcaseShapeRectangle,
            showcaseShapeEllipse,
            showcaseShapeLine,
            showcaseShapePath,
            showcaseGridSplitterGrid,
            splitterLeftColumn,
            splitterRightColumn,
            splitterLeftPane,
            showcaseGridSplitter,
            splitterRightPane,
            showcaseViewbox,
            viewboxText);
        ValidateResourceControls(
            window,
            componentResourceText,
            localizedResourceText,
            resourceAccessText,
            objectProviderText,
            xmlProviderText,
            resourceArrayItemsControl,
            resourceCompositeItemsControl,
            nullIntrinsicText,
            markupExtensionText,
            packResourceText,
            componentPackResourceText,
            startupResourceText,
            systemParameterText,
            systemFontText,
            systemColorBorder,
            systemColorText,
            showcaseThemedControl,
            drawingImageControl,
            drawingImageBrushBorder,
            resourceDynamicBorder,
            expectLoadedStoryboardApplied);
        ValidateItemsContextMenu(window, viewModel, itemsList);
        ValidateApplicationLoadComponent();
        ValidateLooseXamlReaderWriter();
        ValidateDispatcherOperations(window);
        ValidateNavigation(
            window,
            navigationFrame,
            detailsNavigationButton,
            backNavigationButton,
            forwardNavigationButton);
        ValidateSecondaryWindow(window, aboutMenuItem);
        ValidateEditor(
            window,
            editorPasswordBox,
            editorRichTextBox,
            dataObjectPayloadTextBox,
            dataObjectRoundTripButton,
            clipboardRoundTripButton,
            selectAllPayloadButton,
            copyPayloadButton,
            cutPayloadButton,
            pastePayloadButton,
            undoPayloadButton,
            redoPayloadButton,
            copyRichTextButton,
            pasteRichTextButton,
            dataObjectStatusText);
        ValidateDocument(window, documentViewer, documentPageViewer, documentReader);

        AssertEqual(true, MainWindow.RefreshStatusCommand.CanExecute(null, window), "refresh command initial CanExecute state");
        MainWindow.RefreshStatusCommand.Execute(null, window);
        DrainDispatcher(window);
        AssertEqual(1, viewModel.RefreshCount, "refresh command execution count");
        AssertEqual("Refresh command 1", commandStatusText.Text, "refreshed command status text");
        MainWindow.RefreshStatusCommand.Execute(refreshMouseBinding.CommandParameter, refreshMouseBinding.CommandTarget);
        DrainDispatcher(window);
        AssertEqual(2, viewModel.RefreshCount, "refresh mouse binding command execution count");
        AssertEqual("Refresh command 2", commandStatusText.Text, "refresh mouse binding status text");

        int initialCount = viewModel.Items.Count;
        viewModel.NewItemName = "Validated";
        viewModel.SelectedCategory = "Input";
        addMenuItem.Command.Execute(addMenuItem.CommandParameter);

        AssertEqual(initialCount + 1, viewModel.Items.Count, "added item count");
        AssertEqual("Validated", viewModel.SelectedItem?.Name, "selected item name");
        AssertEqual("Input", viewModel.SelectedItem?.Category, "selected item category");
        AssertEqual(true, viewModel.SelectedItem?.IsActive ?? false, "selected item active state");
        DrainDispatcher(window);
        AssertEqual(viewModel.SelectedItem, implicitTemplateContent.Content, "implicit item content updated selected item");
        AssertEqual(viewModel.SelectedItem, explorerListView.SelectedItem, "explorer ListView updated selected item");
        actionsEnabledMenuItem.IsChecked = false;
        AssertEqual(false, viewModel.ActionsEnabled, "actions menu unchecked view model state");
        AssertEqual(false, MainWindow.RefreshStatusCommand.CanExecute(null, window), "refresh command disabled CanExecute state");
        actionsEnabledMenuItem.IsChecked = true;
        AssertEqual(true, viewModel.ActionsEnabled, "actions menu checked view model state");
        AssertEqual(true, MainWindow.RefreshStatusCommand.CanExecute(null, window), "refresh command reenabled CanExecute state");
        ValidateRequeryCommand(window, viewModel, requeryCommandButton);

        viewModel.Progress = 72.0;
        DrainDispatcher(window);
        AssertEqual("Validated selected, progress 72%", viewModel.StatusText, "status text");
        AssertEqual("Validated selected, progress 72%", dependencyPropertyManagerText.HeaderText, "AddOwner updated header property");
        AssertEqual("Validated selected, progress 72%", summaryPanel.HeaderText, "summary updated header property");
        AssertEqual("Validated selected, progress 72%", summaryHeaderText.Text, "summary updated header text");
        AssertEqual("Validated", priorityBindingText.Text, "updated priority binding selected item text");
        AssertEqual("Name: Validated", summaryNameText.Text, "summary updated name text");
        AssertEqual("Category: Input", summaryCategoryText.Text, "summary updated category text");
        AssertEqual("Progress: 72%", summaryProgressText.Text, "summary updated progress text");
        AssertEqual("Validated / Input / 72%", selectedItemSummaryText.Text, "updated selected summary text");
        ValidateAutomationMetadataAndPeers(
            window,
            viewModel,
            nameTextBox,
            requeryCommandButton,
            enabledCheckBox,
            progressSlider);
        ValidateFrameworkThemeSwitching(window, application, themeMenuItem);
    }

    private static void ValidateApplicationRunState(
        Application application,
        MainWindow window,
        bool expectStartupUriWindow)
    {
        if (!expectStartupUriWindow)
        {
            return;
        }

        AssertEqual(window, application.MainWindow, "Application MainWindow");
        int openWindowCount = 0;
        bool containsMainWindow = false;
        foreach (Window candidate in application.Windows)
        {
            openWindowCount++;
            containsMainWindow |= ReferenceEquals(candidate, window);
        }

        AssertEqual(1, openWindowCount, "Application Windows count after StartupUri activation");
        AssertEqual(true, containsMainWindow, "Application Windows contains StartupUri MainWindow");
        AssertEqual(true, window.IsVisible, "StartupUri MainWindow visible");
    }

    private static void ValidateAppConfiguration()
    {
        AssertEqual("Showcase app config value", ConfigurationManager.AppSettings["ShowcaseAppSetting"], "ConfigurationManager app setting");
        AssertEqual("73", ConfigurationManager.AppSettings["ShowcaseNumericSetting"], "ConfigurationManager numeric app setting");
    }

    private static void ValidateRuntimeNameScope(Window window)
    {
        const string runtimeName = "ShowcaseRuntimeRegisteredName";
        var registeredButton = new Button { Content = "Runtime registered name" };
        var replacementText = new TextBlock { Text = "Runtime replacement name" };

        window.RegisterName(runtimeName, registeredButton);
        try
        {
            AssertEqual(registeredButton, window.FindName(runtimeName), "runtime namescope registered object");
            window.UnregisterName(runtimeName);
            AssertEqual<object?>(null, window.FindName(runtimeName), "runtime namescope unregistered object");
            window.RegisterName(runtimeName, replacementText);
            AssertEqual(replacementText, window.FindName(runtimeName), "runtime namescope replacement object");
        }
        finally
        {
            if (ReferenceEquals(replacementText, window.FindName(runtimeName)) ||
                ReferenceEquals(registeredButton, window.FindName(runtimeName)))
            {
                window.UnregisterName(runtimeName);
            }
        }
    }

    private static void ValidateCollectionView(
        Window window,
        MainViewModel viewModel,
        ListBox groupedItemsList,
        CheckBox activeOnlyCheckBox,
        ShowcaseActiveTextConverter activeTextConverter)
    {
        var itemsViewSource = Require<CollectionViewSource>(
            window.FindResource("ItemsViewSource"),
            "items CollectionViewSource");
        AssertEqual(viewModel.Items, itemsViewSource.Source, "CollectionViewSource source");
        AssertEqual(2, itemsViewSource.SortDescriptions.Count, "CollectionViewSource sort count");
        AssertEqual("Category", itemsViewSource.SortDescriptions[0].PropertyName, "first sort property");
        AssertEqual(ListSortDirection.Ascending, itemsViewSource.SortDescriptions[0].Direction, "first sort direction");
        AssertEqual("Name", itemsViewSource.SortDescriptions[1].PropertyName, "second sort property");
        AssertEqual(1, itemsViewSource.GroupDescriptions.Count, "CollectionViewSource group count");
        var groupDescription = Require<PropertyGroupDescription>(
            itemsViewSource.GroupDescriptions[0],
            "items PropertyGroupDescription");
        AssertEqual("Category", groupDescription.PropertyName, "items group property");
        AssertEqual(false, activeOnlyCheckBox.IsChecked == true, "active-only initial check state");
        AssertEqual(false, viewModel.ShowActiveOnly, "active-only initial view model state");
        AssertEqual(itemsViewSource.View, groupedItemsList.ItemsSource, "grouped ListBox ItemsSource view");
        ValidateGroupedItemsGroupStyle(groupedItemsList.GroupStyle);
        ValidateGroupedItemTemplate(groupedItemsList.ItemTemplate, activeTextConverter);

        var initialItems = CopyItems(itemsViewSource.View);
        AssertEqual(2, initialItems.Count, "initial collection view item count");
        AssertEqual("Alpha", initialItems[0].Name, "initial collection view first item");
        AssertEqual("Beta", initialItems[1].Name, "initial collection view second item");
        AssertEqual(2, itemsViewSource.View.Groups?.Count ?? -1, "initial collection view group count");
        var firstGroup = Require<CollectionViewGroup>(
            itemsViewSource.View.Groups?[0],
            "first collection view group");
        AssertEqual("Framework", firstGroup.Name, "first collection view group name");

        activeOnlyCheckBox.IsChecked = true;
        DrainDispatcher(window);
        var filteredItems = CopyItems(itemsViewSource.View);
        AssertEqual(true, viewModel.ShowActiveOnly, "active-only checked view model state");
        AssertEqual(1, filteredItems.Count, "filtered collection view item count");
        AssertEqual("Alpha", filteredItems[0].Name, "filtered collection view first item");

        activeOnlyCheckBox.IsChecked = false;
        DrainDispatcher(window);
        var restoredItems = CopyItems(itemsViewSource.View);
        AssertEqual(false, viewModel.ShowActiveOnly, "active-only restored view model state");
        AssertEqual(2, restoredItems.Count, "restored collection view item count");
    }

    private static void ValidateGroupedItemsGroupStyle(Collection<GroupStyle> groupStyles)
    {
        AssertEqual(1, groupStyles.Count, "grouped ListBox GroupStyle count");
        var groupStyle = Require<GroupStyle>(groupStyles[0], "grouped ListBox GroupStyle");
        var headerTemplate = Require<DataTemplate>(
            groupStyle.HeaderTemplate,
            "grouped ListBox GroupStyle HeaderTemplate");
        var root = Require<Border>(
            headerTemplate.LoadContent(),
            "grouped ListBox GroupStyle HeaderTemplate root");
        var headerText = Require<TextBlock>(
            root.Child,
            "grouped ListBox GroupStyle HeaderTemplate TextBlock");

        AssertEqual(new Thickness(0, 8, 0, 4), root.Margin, "grouped ListBox GroupStyle header margin");
        AssertEqual(new Thickness(6, 3, 6, 3), root.Padding, "grouped ListBox GroupStyle header padding");
        AssertEqual(FontWeights.SemiBold, headerText.FontWeight, "grouped ListBox GroupStyle header weight");
        AssertEqual("Name", GetTextBindingPath(headerText), "grouped ListBox GroupStyle header binding path");
    }

    private static void ValidateFormattedItemsList(
        Window window,
        MainViewModel viewModel,
        ListBox listBox)
    {
        AssertEqual(viewModel.FormattedItems, listBox.ItemsSource, "formatted ListBox ItemsSource");
        AssertEqual(3, listBox.AlternationCount, "formatted ListBox AlternationCount");
        AssertEqual("formatted {0}", listBox.ItemStringFormat, "formatted ListBox ItemStringFormat");
        AssertEqual(2, listBox.Items.Count, "formatted ListBox initial item count");
        AssertEqual("Alpha", listBox.Items[0], "formatted ListBox first item");

        viewModel.FormattedItems.Add("Gamma");
        DrainDispatcher(window);
        AssertEqual(3, listBox.Items.Count, "formatted ListBox collection-change item count");
        AssertEqual("Gamma", listBox.Items[2], "formatted ListBox collection-change item");

        viewModel.FormattedItems.Remove("Gamma");
        DrainDispatcher(window);
        AssertEqual(2, listBox.Items.Count, "formatted ListBox restored item count");
    }

    private static void ValidateItemsContextMenu(Window window, MainViewModel viewModel, ListBox itemsList)
    {
        var contextMenu = Require<ContextMenu>(itemsList.ContextMenu, "items ContextMenu");
        AssertEqual("ItemsContextMenu", contextMenu.Name, "items ContextMenu name");
        AssertEqual(4, contextMenu.Items.Count, "items ContextMenu item count");

        var addItem = Require<MenuItem>(contextMenu.Items[0], "context add MenuItem");
        var refreshItem = Require<MenuItem>(contextMenu.Items[1], "context refresh MenuItem");
        Require<Separator>(contextMenu.Items[2], "context menu separator");
        var actionsItem = Require<MenuItem>(contextMenu.Items[3], "context actions MenuItem");

        AssertEqual("ContextAddMenuItem", addItem.Name, "context add MenuItem name");
        AssertEqual("_Add item", addItem.Header, "context add MenuItem header");
        AssertEqual("ContextRefreshMenuItem", refreshItem.Name, "context refresh MenuItem name");
        AssertEqual("_Refresh status", refreshItem.Header, "context refresh MenuItem header");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshItem.Command, "context refresh routed command");
        AssertEqual("ContextActionsEnabledMenuItem", actionsItem.Name, "context actions MenuItem name");
        AssertEqual(true, actionsItem.IsCheckable, "context actions checkable state");

        var contextDataContextBinding = Require<Binding>(
            BindingOperations.GetBinding(contextMenu, FrameworkElement.DataContextProperty),
            "context menu DataContext binding");
        var contextDataContextSource = Require<RelativeSource>(
            contextDataContextBinding.RelativeSource,
            "context menu DataContext RelativeSource");
        AssertEqual("PlacementTarget.DataContext", contextDataContextBinding.Path.Path, "context menu DataContext path");
        AssertEqual(RelativeSourceMode.Self, contextDataContextSource.Mode, "context menu DataContext source");

        var addCommandBinding = Require<Binding>(
            BindingOperations.GetBinding(addItem, MenuItem.CommandProperty),
            "context add command binding");
        AssertEqual("AddItemCommand", addCommandBinding.Path.Path, "context add command path");

        var refreshTargetBinding = Require<Binding>(
            BindingOperations.GetBinding(refreshItem, MenuItem.CommandTargetProperty),
            "context refresh command target binding");
        var refreshTargetSource = Require<RelativeSource>(
            refreshTargetBinding.RelativeSource,
            "context refresh command target RelativeSource");
        AssertEqual("PlacementTarget", refreshTargetBinding.Path.Path, "context refresh command target path");
        AssertEqual(
            RelativeSourceMode.FindAncestor,
            refreshTargetSource.Mode,
            "context refresh command target source");
        AssertEqual(
            typeof(ContextMenu),
            refreshTargetSource.AncestorType,
            "context refresh command target ancestor");

        var actionsCheckedBinding = Require<Binding>(
            BindingOperations.GetBinding(actionsItem, MenuItem.IsCheckedProperty),
            "context actions checked binding");
        AssertEqual("ActionsEnabled", actionsCheckedBinding.Path.Path, "context actions checked path");

        contextMenu.PlacementTarget = itemsList;
        UpdateBinding(contextMenu, FrameworkElement.DataContextProperty);
        UpdateBinding(addItem, MenuItem.CommandProperty);
        UpdateBinding(refreshItem, MenuItem.CommandTargetProperty);
        UpdateBinding(actionsItem, MenuItem.IsCheckedProperty);
        DrainDispatcher(window);

        AssertEqual(viewModel, contextMenu.DataContext, "context menu inherited DataContext");
        AssertEqual(viewModel.AddItemCommand, addItem.Command, "context add command resolved command");
        AssertEqual(itemsList, refreshItem.CommandTarget, "context refresh command target");
        AssertEqual(true, actionsItem.IsChecked, "context actions initial checked state");
        AssertEqual(
            true,
            MainWindow.RefreshStatusCommand.CanExecute(null, refreshItem.CommandTarget),
            "context refresh command target CanExecute state");

        int initialCount = viewModel.Items.Count;
        viewModel.NewItemName = "Context added";
        viewModel.SelectedCategory = "Input";
        addItem.Command.Execute(addItem.CommandParameter);
        DrainDispatcher(window);
        AssertEqual(initialCount + 1, viewModel.Items.Count, "context add command item count");
        AssertEqual("Context added", viewModel.SelectedItem?.Name, "context add selected item name");
        AssertEqual("Input", viewModel.SelectedItem?.Category, "context add selected item category");

        actionsItem.IsChecked = false;
        DrainDispatcher(window);
        AssertEqual(false, viewModel.ActionsEnabled, "context actions unchecked view model state");
        actionsItem.IsChecked = true;
        DrainDispatcher(window);
        AssertEqual(true, viewModel.ActionsEnabled, "context actions checked view model state");
    }

    private static void ValidateShowcaseSystemCommands(
        MainWindow window,
        MenuItem maximizeItem,
        MenuItem minimizeItem,
        MenuItem restoreItem,
        MenuItem systemMenuItem)
    {
        var originalState = window.WindowState;
        int initialCanExecuteCount = window.SystemCommandCanExecuteCount;
        int initialExecutedCount = window.SystemCommandExecutedCount;

        try
        {
            ExecuteShowcaseSystemCommand(
                window,
                maximizeItem,
                SystemCommands.MaximizeWindowCommand,
                "showcase maximize",
                WindowState.Maximized,
                "maximize");
            ExecuteShowcaseSystemCommand(
                window,
                minimizeItem,
                SystemCommands.MinimizeWindowCommand,
                "showcase minimize",
                WindowState.Minimized,
                "minimize");
            ExecuteShowcaseSystemCommand(
                window,
                restoreItem,
                SystemCommands.RestoreWindowCommand,
                "showcase restore",
                WindowState.Normal,
                "restore");
            ExecuteShowcaseSystemCommand(
                window,
                systemMenuItem,
                SystemCommands.ShowSystemMenuCommand,
                "showcase system menu",
                WindowState.Normal,
                "show system menu");
        }
        finally
        {
            window.WindowState = originalState;
            DrainDispatcher(window);
        }

        AssertGreaterThan(
            initialCanExecuteCount,
            window.SystemCommandCanExecuteCount,
            "Showcase SystemCommands CanExecute count");
        AssertEqual(
            initialExecutedCount + 4,
            window.SystemCommandExecutedCount,
            "Showcase SystemCommands executed count");
    }

    private static void ValidateShowcaseWindowChrome(
        MainWindow window,
        Border captionRegion,
        Button hitTestButton,
        Thumb resizeGrip)
    {
        var chrome = Require<WindowChrome>(
            window.FindResource("ShowcaseWindowChromeMetadata"),
            "Showcase WindowChrome metadata resource");

        AssertEqual(32.0, chrome.CaptionHeight, "Showcase WindowChrome caption height");
        AssertEqual(new CornerRadius(8.0), chrome.CornerRadius, "Showcase WindowChrome corner radius");
        AssertEqual(new Thickness(0.0), chrome.GlassFrameThickness, "Showcase WindowChrome glass thickness");
        AssertEqual(NonClientFrameEdges.None, chrome.NonClientFrameEdges, "Showcase WindowChrome non-client edges");
        AssertEqual(new Thickness(6.0), chrome.ResizeBorderThickness, "Showcase WindowChrome resize border");
        AssertEqual(false, chrome.UseAeroCaptionButtons, "Showcase WindowChrome caption buttons");
        AssertEqual(
            true,
            WindowChrome.GetIsHitTestVisibleInChrome(hitTestButton),
            "Showcase WindowChrome hit-test attached value");
        AssertEqual(
            ResizeGripDirection.BottomRight,
            WindowChrome.GetResizeGripDirection(resizeGrip),
            "Showcase WindowChrome resize-grip direction");
        AssertEqual(
            "ProGPU chrome metadata",
            Require<TextBlock>(
                Require<Grid>(captionRegion.Child, "Showcase WindowChrome caption Grid").Children[0],
                "Showcase WindowChrome caption TextBlock").Text,
            "Showcase WindowChrome caption text");
        AssertEqual(0, window.ChromeCaptionMouseDownCount, "Showcase WindowChrome caption initial mouse count");
        AssertEqual("Idle", window.ChromeDragMoveStatus, "Showcase WindowChrome drag initial status");
    }

    private static void ExecuteShowcaseSystemCommand(
        MainWindow window,
        MenuItem menuItem,
        RoutedCommand command,
        string parameter,
        WindowState expectedState,
        string description)
    {
        AssertEqual(command, menuItem.Command, $"Showcase SystemCommands {description} command");
        AssertEqual(window, menuItem.CommandTarget, $"Showcase SystemCommands {description} target");
        AssertEqual(parameter, menuItem.CommandParameter, $"Showcase SystemCommands {description} parameter");
        AssertEqual(
            true,
            command.CanExecute(parameter, window),
            $"Showcase SystemCommands {description} CanExecute");

        int initialExecutedCount = window.SystemCommandExecutedCount;
        command.Execute(parameter, window);
        DrainDispatcher(window);

        AssertEqual(
            initialExecutedCount + 1,
            window.SystemCommandExecutedCount,
            $"Showcase SystemCommands {description} executed count");
        AssertEqual(command.Name, window.LastSystemCommandName, $"Showcase SystemCommands {description} name");
        AssertEqual(parameter, window.LastSystemCommandParameter, $"Showcase SystemCommands {description} parameter result");
        AssertEqual(expectedState, window.WindowState, $"Showcase SystemCommands {description} state");
    }

    private static void ValidateShowcaseTabControl(
        MainWindow window,
        MainViewModel viewModel,
        TabControl tabControl)
    {
        AssertEqual(viewModel.SelectedTabIndex, tabControl.SelectedIndex, "Showcase TabControl selected index");
        AssertEqual(15, tabControl.Items.Count, "Showcase TabControl item count");

        var controlsTab = Require<TabItem>(tabControl.Items[0], "Showcase controls TabItem");
        var documentTab = Require<TabItem>(tabControl.Items[14], "Showcase document TabItem");
        AssertEqual("Controls", controlsTab.Header, "Showcase first tab header");
        AssertEqual("Document", documentTab.Header, "Showcase last tab header");

        int initialSelectionEvents = window.ShowcaseTabSelectionChangedCount;
        tabControl.SelectedIndex = 1;
        DrainDispatcher(window);
        AssertEqual(1, viewModel.SelectedTabIndex, "Showcase TabControl selected index source update");
        AssertEqual("Views", window.LastShowcaseTabHeader, "Showcase TabControl selected header after control update");
        AssertGreaterThan(
            initialSelectionEvents,
            window.ShowcaseTabSelectionChangedCount,
            "Showcase TabControl control selection event count");

        int afterControlUpdateEvents = window.ShowcaseTabSelectionChangedCount;
        viewModel.SelectedTabIndex = 2;
        DrainDispatcher(window);
        AssertEqual(2, tabControl.SelectedIndex, "Showcase TabControl selected index target update");
        AssertEqual("Bindings", window.LastShowcaseTabHeader, "Showcase TabControl selected header after source update");
        AssertGreaterThan(
            afterControlUpdateEvents,
            window.ShowcaseTabSelectionChangedCount,
            "Showcase TabControl source selection event count");

        int afterSourceUpdateEvents = window.ShowcaseTabSelectionChangedCount;
        viewModel.SelectedTabIndex = 0;
        DrainDispatcher(window);
        AssertEqual(0, tabControl.SelectedIndex, "Showcase TabControl restored selected index");
        AssertEqual("Controls", window.LastShowcaseTabHeader, "Showcase TabControl restored selected header");
        AssertGreaterThan(
            afterSourceUpdateEvents,
            window.ShowcaseTabSelectionChangedCount,
            "Showcase TabControl restored selection event count");
    }

    private static void ValidateExplicitExplorerTree(
        MainWindow window,
        TreeView treeView,
        TreeViewItem alphaItem,
        TreeViewItem alphaChildItem,
        TreeViewItem betaItem,
        TextBlock statusText)
    {
        AssertEqual(2, treeView.Items.Count, "explicit explorer TreeView item count");
        AssertEqual(alphaItem, treeView.Items[0], "explicit explorer alpha item owner");
        AssertEqual(betaItem, treeView.Items[1], "explicit explorer beta item owner");
        AssertEqual("Alpha branch", alphaItem.Header, "explicit explorer alpha header");
        AssertEqual("Alpha child", alphaChildItem.Header, "explicit explorer alpha child header");
        AssertEqual("Beta branch", betaItem.Header, "explicit explorer beta header");
        AssertEqual(1, alphaItem.Items.Count, "explicit explorer alpha child count");
        AssertEqual(alphaChildItem, alphaItem.Items[0], "explicit explorer alpha child owner");
        AssertEqual("Tree idle", statusText.Text, "explicit explorer initial status");

        int initialExpandedEvents = window.ExplicitExplorerTreeExpandedCount;
        alphaItem.IsExpanded = true;
        DrainDispatcher(window);
        AssertEqual(true, alphaItem.IsExpanded, "explicit explorer alpha expanded state");
        AssertEqual("ExplicitExplorerAlpha", window.LastExplicitExplorerTreeSenderName, "explicit explorer expanded sender");
        AssertEqual("Expanded", window.LastExplicitExplorerTreeRoutedEventName, "explicit explorer expanded event name");
        AssertEqual("Expanded: Alpha branch", statusText.Text, "explicit explorer expanded status");
        AssertGreaterThan(
            initialExpandedEvents,
            window.ExplicitExplorerTreeExpandedCount,
            "explicit explorer expanded event count");

        int initialCollapsedEvents = window.ExplicitExplorerTreeCollapsedCount;
        alphaItem.IsExpanded = false;
        DrainDispatcher(window);
        AssertEqual(false, alphaItem.IsExpanded, "explicit explorer alpha collapsed state");
        AssertEqual("ExplicitExplorerAlpha", window.LastExplicitExplorerTreeSenderName, "explicit explorer collapsed sender");
        AssertEqual("Collapsed", window.LastExplicitExplorerTreeRoutedEventName, "explicit explorer collapsed event name");
        AssertEqual("Collapsed: Alpha branch", statusText.Text, "explicit explorer collapsed status");
        AssertGreaterThan(
            initialCollapsedEvents,
            window.ExplicitExplorerTreeCollapsedCount,
            "explicit explorer collapsed event count");

        int initialSelectedEvents = window.ExplicitExplorerTreeSelectedCount;
        alphaItem.IsSelected = true;
        DrainDispatcher(window);
        AssertEqual(true, alphaItem.IsSelected, "explicit explorer alpha selected state");
        AssertEqual(alphaItem, treeView.SelectedItem, "explicit explorer selected alpha item");
        AssertEqual("ExplicitExplorerAlpha", window.LastExplicitExplorerTreeSenderName, "explicit explorer alpha selected sender");
        AssertEqual("Selected", window.LastExplicitExplorerTreeRoutedEventName, "explicit explorer selected event name");
        AssertEqual("Selected: Alpha branch", statusText.Text, "explicit explorer selected status");
        AssertGreaterThan(
            initialSelectedEvents,
            window.ExplicitExplorerTreeSelectedCount,
            "explicit explorer selected event count");

        int selectedAfterAlpha = window.ExplicitExplorerTreeSelectedCount;
        int initialUnselectedEvents = window.ExplicitExplorerTreeUnselectedCount;
        betaItem.IsSelected = true;
        DrainDispatcher(window);
        AssertEqual(false, alphaItem.IsSelected, "explicit explorer alpha unselected state");
        AssertEqual(true, betaItem.IsSelected, "explicit explorer beta selected state");
        AssertEqual(betaItem, treeView.SelectedItem, "explicit explorer selected beta item");
        AssertEqual("ExplicitExplorerBeta", window.LastExplicitExplorerTreeSenderName, "explicit explorer beta selected sender");
        AssertEqual("Selected", window.LastExplicitExplorerTreeRoutedEventName, "explicit explorer beta selected event name");
        AssertEqual("Selected: Beta branch", statusText.Text, "explicit explorer beta selected status");
        AssertGreaterThan(
            selectedAfterAlpha,
            window.ExplicitExplorerTreeSelectedCount,
            "explicit explorer beta selected event count");
        AssertGreaterThan(
            initialUnselectedEvents,
            window.ExplicitExplorerTreeUnselectedCount,
            "explicit explorer alpha unselected event count");
    }

    private static void ValidateRequeryCommand(Window window, MainViewModel viewModel, Button button)
    {
        var command = viewModel.RequeryCommand;
        AssertEqual(command, button.Command, "requery command Button command binding");
        AssertEqual("showcase requery command payload", button.CommandParameter, "requery command Button parameter");

        var canExecuteChangedCount = 0;
        EventHandler handler = (_, _) => canExecuteChangedCount++;
        command.CanExecuteChanged += handler;
        try
        {
            command.CanExecuteValue = false;
            var disabledProbeBaseline = command.CanExecuteProbeCount;
            CommandManager.InvalidateRequerySuggested();
            DrainDispatcher(window);
            AssertGreaterThan(0, canExecuteChangedCount, "requery command CanExecuteChanged count");
            AssertEqual(false, command.CanExecute(button.CommandParameter), "requery command disabled CanExecute state");
            AssertGreaterThan(disabledProbeBaseline, command.CanExecuteProbeCount, "requery command disabled probe count");

            var firstRequeryCount = canExecuteChangedCount;
            var enabledProbeBaseline = command.CanExecuteProbeCount;
            command.CanExecuteValue = true;
            CommandManager.InvalidateRequerySuggested();
            DrainDispatcher(window);
            AssertGreaterThan(firstRequeryCount, canExecuteChangedCount, "requery command second CanExecuteChanged count");
            AssertEqual(true, command.CanExecute(button.CommandParameter), "requery command enabled CanExecute state");
            AssertGreaterThan(enabledProbeBaseline, command.CanExecuteProbeCount, "requery command enabled probe count");

            button.Command.Execute(button.CommandParameter);
            AssertEqual(1, command.ExecuteCount, "requery command execution count");
            AssertEqual("showcase requery command payload", command.LastParameter, "requery command execution parameter");
        }
        finally
        {
            command.CanExecuteChanged -= handler;
        }
    }

    private static void ValidateAutomationMetadataAndPeers(
        MainWindow window,
        MainViewModel viewModel,
        TextBox nameTextBox,
        Button requeryCommandButton,
        CheckBox enabledCheckBox,
        Slider progressSlider)
    {
        AssertEqual("ShowcaseNameTextBoxAutomation", AutomationProperties.GetAutomationId(nameTextBox), "Showcase automation TextBox id");
        AssertEqual("Showcase item name", AutomationProperties.GetName(nameTextBox), "Showcase automation TextBox name");
        AssertEqual("Enter the next Showcase item name", AutomationProperties.GetHelpText(nameTextBox), "Showcase automation TextBox help");

        var namePeer = Require<TextBoxAutomationPeer>(
            UIElementAutomationPeer.CreatePeerForElement(nameTextBox),
            "Showcase TextBox automation peer");
        AssertEqual("ShowcaseNameTextBoxAutomation", namePeer.GetAutomationId(), "Showcase TextBox peer id");
        AssertEqual("Showcase item name", namePeer.GetName(), "Showcase TextBox peer name");
        AssertEqual("Enter the next Showcase item name", namePeer.GetHelpText(), "Showcase TextBox peer help");

        var valueProvider = Require<IValueProvider>(
            namePeer.GetPattern(PatternInterface.Value),
            "Showcase TextBox value provider");
        AssertEqual(false, valueProvider.IsReadOnly, "Showcase TextBox value provider read-only state");
        valueProvider.SetValue("Automation item");
        DrainDispatcher(window);
        AssertEqual("Automation item", nameTextBox.Text, "Showcase TextBox automation value");
        UpdateSource(nameTextBox, TextBox.TextProperty);
        AssertEqual("Automation item", viewModel.NewItemName, "Showcase TextBox automation source update");

        AssertEqual(
            "ShowcaseRequeryCommandAutomation",
            AutomationProperties.GetAutomationId(requeryCommandButton),
            "Showcase automation Button id");
        AssertEqual("Showcase requery command", AutomationProperties.GetName(requeryCommandButton), "Showcase automation Button name");
        AssertEqual("Runs the requery command", AutomationProperties.GetHelpText(requeryCommandButton), "Showcase automation Button help");

        var buttonPeer = Require<ButtonAutomationPeer>(
            UIElementAutomationPeer.CreatePeerForElement(requeryCommandButton),
            "Showcase Button automation peer");
        var invokeProvider = Require<IInvokeProvider>(
            buttonPeer.GetPattern(PatternInterface.Invoke),
            "Showcase Button invoke provider");
        var command = viewModel.RequeryCommand;
        var buttonExecuteBaseline = command.ExecuteCount;
        invokeProvider.Invoke();
        DrainDispatcher(window);
        AssertEqual(buttonExecuteBaseline + 1, command.ExecuteCount, "Showcase Button automation invoke command count");
        AssertEqual("showcase requery command payload", command.LastParameter, "Showcase Button automation invoke parameter");

        AssertEqual(
            "ShowcaseActionsEnabledAutomation",
            AutomationProperties.GetAutomationId(enabledCheckBox),
            "Showcase automation CheckBox id");
        AssertEqual("Showcase actions enabled", AutomationProperties.GetName(enabledCheckBox), "Showcase automation CheckBox name");
        AssertEqual(
            "Toggles whether Showcase commands can execute",
            AutomationProperties.GetHelpText(enabledCheckBox),
            "Showcase automation CheckBox help");

        var checkBoxPeer = Require<CheckBoxAutomationPeer>(
            UIElementAutomationPeer.CreatePeerForElement(enabledCheckBox),
            "Showcase CheckBox automation peer");
        var toggleProvider = Require<IToggleProvider>(
            checkBoxPeer.GetPattern(PatternInterface.Toggle),
            "Showcase CheckBox toggle provider");
        AssertEqual(ToggleState.On, toggleProvider.ToggleState, "Showcase CheckBox automation initial state");
        toggleProvider.Toggle();
        DrainDispatcher(window);
        AssertEqual(ToggleState.Off, toggleProvider.ToggleState, "Showcase CheckBox automation off state");
        AssertEqual(false, enabledCheckBox.IsChecked == true, "Showcase CheckBox automation unchecked state");
        AssertEqual(false, viewModel.ActionsEnabled, "Showcase CheckBox automation unchecked source state");
        toggleProvider.Toggle();
        DrainDispatcher(window);
        AssertEqual(ToggleState.On, toggleProvider.ToggleState, "Showcase CheckBox automation restored state");
        AssertEqual(true, enabledCheckBox.IsChecked == true, "Showcase CheckBox automation checked state");
        AssertEqual(true, viewModel.ActionsEnabled, "Showcase CheckBox automation restored source state");

        AssertEqual(
            "ShowcaseProgressSliderAutomation",
            AutomationProperties.GetAutomationId(progressSlider),
            "Showcase automation Slider id");
        AssertEqual("Showcase progress", AutomationProperties.GetName(progressSlider), "Showcase automation Slider name");
        AssertEqual("Adjusts Showcase progress", AutomationProperties.GetHelpText(progressSlider), "Showcase automation Slider help");

        var sliderPeer = Require<SliderAutomationPeer>(
            UIElementAutomationPeer.CreatePeerForElement(progressSlider),
            "Showcase Slider automation peer");
        var rangeProvider = Require<IRangeValueProvider>(
            sliderPeer.GetPattern(PatternInterface.RangeValue),
            "Showcase Slider range provider");
        AssertEqual(false, rangeProvider.IsReadOnly, "Showcase Slider range read-only state");
        AssertEqual(0.0, rangeProvider.Minimum, "Showcase Slider range minimum");
        AssertEqual(100.0, rangeProvider.Maximum, "Showcase Slider range maximum");
        AssertEqual(2.0, rangeProvider.SmallChange, "Showcase Slider range small change");
        AssertEqual(10.0, rangeProvider.LargeChange, "Showcase Slider range large change");
        rangeProvider.SetValue(55.0);
        DrainDispatcher(window);
        AssertEqual(55.0, rangeProvider.Value, "Showcase Slider range value");
        AssertEqual(55.0, progressSlider.Value, "Showcase Slider automation value");
        AssertEqual(55.0, viewModel.Progress, "Showcase Slider automation source value");
    }

    private static void ValidateGroupedItemTemplate(DataTemplate template, ShowcaseActiveTextConverter converter)
    {
        var root = Require<FrameworkElement>(
            template.LoadContent(),
            "grouped item template root");
        var activeText = Require<TextBlock>(
            root.FindName("GroupedItemActiveText"),
            "grouped item active TextBlock");
        var binding = Require<Binding>(
            BindingOperations.GetBinding(activeText, TextBlock.TextProperty),
            "grouped item active binding");

        AssertEqual("IsActive", binding.Path.Path, "grouped item active binding path");
        AssertEqual(converter, binding.Converter, "grouped item active converter");
        AssertEqual("Active", converter.Convert(true, typeof(string), null!, CultureInfo.InvariantCulture), "active converter true text");
        AssertEqual("Inactive", converter.Convert(false, typeof(string), null!, CultureInfo.InvariantCulture), "active converter false text");
    }

    private static void ValidateSelectedSummaryBinding(TextBlock textBlock, ShowcaseItemSummaryConverter converter)
    {
        var binding = Require<MultiBinding>(
            BindingOperations.GetMultiBinding(textBlock, TextBlock.TextProperty),
            "selected summary MultiBinding");

        AssertEqual(converter, binding.Converter, "selected summary converter");
        AssertEqual(3, binding.Bindings.Count, "selected summary binding count");
        AssertEqual("SelectedItem.Name", GetBindingPath(binding.Bindings[0]), "selected summary name path");
        AssertEqual("SelectedItem.Category", GetBindingPath(binding.Bindings[1]), "selected summary category path");
        AssertEqual("Progress", GetBindingPath(binding.Bindings[2]), "selected summary progress path");
    }

    private static List<ShowcaseItem> CopyItems(IEnumerable source)
    {
        var items = new List<ShowcaseItem>();
        foreach (object? item in source)
        {
            items.Add(Require<ShowcaseItem>(item, "collection view item"));
        }

        return items;
    }

    private static void ValidateBindingFallbacks(
        MainWindow window,
        MainViewModel viewModel,
        TextBlock priorityText,
        TextBlock fallbackText,
        TextBlock targetNullText,
        TextBlock bindingTargetUpdatedText,
        TextBox bindingSourceUpdatedTextBox,
        TextBlock relativeSelfText,
        Border relativeAncestorBorder,
        TextBlock relativeAncestorText)
    {
        var priorityBinding = Require<PriorityBinding>(
            BindingOperations.GetPriorityBinding(priorityText, TextBlock.TextProperty),
            "Showcase PriorityBinding");
        AssertEqual("Priority fallback", priorityBinding.FallbackValue, "PriorityBinding fallback value");
        AssertEqual(2, priorityBinding.Bindings.Count, "PriorityBinding child binding count");
        AssertEqual("MissingPriorityText", GetBindingPath(priorityBinding.Bindings[0]), "PriorityBinding missing child path");
        AssertEqual("SelectedItem.Name", GetBindingPath(priorityBinding.Bindings[1]), "PriorityBinding selected child path");
        Require<PriorityBindingExpression>(
            BindingOperations.GetPriorityBindingExpression(priorityText, TextBlock.TextProperty),
            "Showcase PriorityBinding expression");

        var fallbackBinding = Require<Binding>(
            BindingOperations.GetBinding(fallbackText, TextBlock.TextProperty),
            "fallback TextBlock binding");
        AssertEqual("MissingFallbackText", fallbackBinding.Path.Path, "fallback binding path");
        AssertEqual("Fallback binding text", fallbackBinding.FallbackValue, "fallback binding value");

        var targetNullBinding = Require<Binding>(
            BindingOperations.GetBinding(targetNullText, TextBlock.TextProperty),
            "target-null TextBlock binding");
        AssertEqual("NullDisplayText", targetNullBinding.Path.Path, "target-null binding path");
        AssertEqual("Target null text", targetNullBinding.TargetNullValue, "target-null binding value");

        var targetUpdatedBinding = Require<Binding>(
            BindingOperations.GetBinding(bindingTargetUpdatedText, TextBlock.TextProperty),
            "TargetUpdated TextBlock binding");
        AssertEqual("BindingTransferText", targetUpdatedBinding.Path.Path, "TargetUpdated binding path");
        AssertEqual(true, targetUpdatedBinding.NotifyOnTargetUpdated, "TargetUpdated binding notification flag");

        var sourceUpdatedBinding = Require<Binding>(
            BindingOperations.GetBinding(bindingSourceUpdatedTextBox, TextBox.TextProperty),
            "SourceUpdated TextBox binding");
        AssertEqual("BindingTransferText", sourceUpdatedBinding.Path.Path, "SourceUpdated binding path");
        AssertEqual(BindingMode.TwoWay, sourceUpdatedBinding.Mode, "SourceUpdated binding mode");
        AssertEqual(UpdateSourceTrigger.Explicit, sourceUpdatedBinding.UpdateSourceTrigger, "SourceUpdated trigger mode");
        AssertEqual(true, sourceUpdatedBinding.NotifyOnSourceUpdated, "SourceUpdated binding notification flag");
        var sourceUpdatedExpression = Require<BindingExpression>(
            bindingSourceUpdatedTextBox.GetBindingExpression(TextBox.TextProperty),
            "SourceUpdated TextBox binding expression");

        var selfBinding = Require<Binding>(
            BindingOperations.GetBinding(relativeSelfText, TextBlock.TextProperty),
            "relative self binding");
        var selfSource = Require<RelativeSource>(
            selfBinding.RelativeSource,
            "relative self binding source");
        AssertEqual(RelativeSourceMode.Self, selfSource.Mode, "relative self binding mode");
        AssertEqual("Tag", selfBinding.Path.Path, "relative self binding path");

        var ancestorBinding = Require<Binding>(
            BindingOperations.GetBinding(relativeAncestorText, TextBlock.TextProperty),
            "relative ancestor binding");
        var ancestorSource = Require<RelativeSource>(
            ancestorBinding.RelativeSource,
            "relative ancestor binding source");
        AssertEqual(RelativeSourceMode.FindAncestor, ancestorSource.Mode, "relative ancestor binding mode");
        AssertEqual(typeof(Border), ancestorSource.AncestorType, "relative ancestor binding type");
        AssertEqual("Tag", ancestorBinding.Path.Path, "relative ancestor binding path");

        DrainDispatcher(window);
        AssertEqual("Alpha", priorityText.Text, "initial priority binding text");
        AssertEqual("Fallback binding text", fallbackText.Text, "fallback binding text");
        AssertEqual("Target null text", targetNullText.Text, "target-null binding text");
        AssertEqual("Transfer initial", bindingTargetUpdatedText.Text, "initial TargetUpdated binding text");
        AssertEqual("Transfer initial", bindingSourceUpdatedTextBox.Text, "initial SourceUpdated binding text");
        AssertGreaterThan(0, window.BindingTargetUpdatedCount, "initial TargetUpdated handler count");
        AssertEqual("BindingTargetUpdatedText", window.LastBindingTargetUpdatedName, "initial TargetUpdated target name");
        AssertEqual(nameof(TextBlock.Text), window.LastBindingTargetUpdatedPropertyName, "initial TargetUpdated target property");
        AssertEqual("Self binding text", relativeSelfText.Text, "relative self binding text");
        AssertEqual("Ancestor binding text", relativeAncestorText.Text, "relative ancestor binding text");

        int targetUpdatedCountBeforeTransferChange = window.BindingTargetUpdatedCount;
        viewModel.BindingTransferText = "Transfer from source";
        DrainDispatcher(window);
        AssertEqual("Transfer from source", bindingTargetUpdatedText.Text, "source-changed TargetUpdated binding text");
        AssertGreaterThan(
            targetUpdatedCountBeforeTransferChange,
            window.BindingTargetUpdatedCount,
            "source-changed TargetUpdated handler count");

        int sourceUpdatedCountBeforeExplicitUpdate = window.BindingSourceUpdatedCount;
        bindingSourceUpdatedTextBox.Text = "Transfer from target";
        sourceUpdatedExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual("Transfer from target", viewModel.BindingTransferText, "SourceUpdated binding source value");
        AssertGreaterThan(
            sourceUpdatedCountBeforeExplicitUpdate,
            window.BindingSourceUpdatedCount,
            "explicit SourceUpdated handler count");
        AssertEqual("BindingSourceUpdatedTextBox", window.LastBindingSourceUpdatedName, "SourceUpdated target name");
        AssertEqual(nameof(TextBox.Text), window.LastBindingSourceUpdatedPropertyName, "SourceUpdated target property");

        viewModel.NullDisplayText = "Non-null binding text";
        DrainDispatcher(window);
        AssertEqual("Non-null binding text", targetNullText.Text, "non-null target binding text");
        viewModel.NullDisplayText = null;
        DrainDispatcher(window);
        AssertEqual("Target null text", targetNullText.Text, "restored target-null binding text");

        relativeAncestorBorder.Tag = "Updated ancestor binding text";
        DrainDispatcher(window);
        AssertEqual("Updated ancestor binding text", relativeAncestorText.Text, "updated ancestor binding text");
    }

    private static void ValidateSelectorControls(
        MainWindow window,
        MainViewModel viewModel,
        GroupBox groupBox,
        ComboBox selectedValueComboBox,
        ListBox multiSelectItemsList,
        Expander expander,
        ScrollViewer scrollViewer,
        TextBlock scrollText)
    {
        AssertEqual("Selector container", groupBox.Header, "selector GroupBox header");
        Require<Grid>(groupBox.Content, "selector GroupBox content");

        AssertEqual(viewModel.Items, selectedValueComboBox.ItemsSource, "selected-value ComboBox ItemsSource");
        AssertEqual("Name", selectedValueComboBox.DisplayMemberPath, "selected-value ComboBox display path");
        AssertEqual("Category", selectedValueComboBox.SelectedValuePath, "selected-value ComboBox value path");
        var selectedValueBinding = Require<Binding>(
            BindingOperations.GetBinding(selectedValueComboBox, Selector.SelectedValueProperty),
            "selected-value ComboBox binding");
        AssertEqual("SelectedCategory", selectedValueBinding.Path.Path, "selected-value ComboBox binding path");
        AssertEqual(BindingMode.TwoWay, selectedValueBinding.Mode, "selected-value ComboBox binding mode");

        DrainDispatcher(window);
        AssertEqual("Framework", selectedValueComboBox.SelectedValue, "selected-value ComboBox initial value");
        int initialSelectorEvents = window.SelectorSelectionChangedCount;
        selectedValueComboBox.SelectedItem = viewModel.Items[1];
        DrainDispatcher(window);
        AssertEqual(viewModel.Items[1], selectedValueComboBox.SelectedItem, "selected-value ComboBox selected item");
        AssertEqual("Rendering", selectedValueComboBox.SelectedValue, "selected-value ComboBox selected value");
        AssertEqual("Rendering", viewModel.SelectedCategory, "selected-value ComboBox updated source");
        AssertGreaterThan(
            initialSelectorEvents,
            window.SelectorSelectionChangedCount,
            "selected-value ComboBox SelectionChanged count");

        viewModel.SelectedCategory = "Framework";
        UpdateBinding(selectedValueComboBox, Selector.SelectedValueProperty);
        DrainDispatcher(window);
        AssertEqual("Framework", selectedValueComboBox.SelectedValue, "selected-value ComboBox restored value");
        AssertEqual(viewModel.Items[0], selectedValueComboBox.SelectedItem, "selected-value ComboBox restored item");

        AssertEqual(viewModel.Items, multiSelectItemsList.ItemsSource, "multi-select ListBox ItemsSource");
        AssertEqual("Name", multiSelectItemsList.DisplayMemberPath, "multi-select ListBox display path");
        AssertEqual(SelectionMode.Multiple, multiSelectItemsList.SelectionMode, "multi-select ListBox mode");
        AssertEqual(true, ScrollViewer.GetCanContentScroll(multiSelectItemsList), "multi-select ListBox logical scrolling");
        AssertEqual(true, VirtualizingPanel.GetIsVirtualizing(multiSelectItemsList), "multi-select ListBox virtualization enabled");
        AssertEqual(
            VirtualizationMode.Recycling,
            VirtualizingPanel.GetVirtualizationMode(multiSelectItemsList),
            "multi-select ListBox virtualization mode");
        var virtualizingPanel = Require<VirtualizingStackPanel>(
            multiSelectItemsList.ItemsPanel.LoadContent(),
            "multi-select ListBox virtualizing items panel");
        AssertEqual(Orientation.Vertical, virtualizingPanel.Orientation, "multi-select ListBox virtualizing panel orientation");
        int initialMultiEvents = window.MultiSelectorSelectionChangedCount;
        multiSelectItemsList.SelectedItems.Add(viewModel.Items[0]);
        multiSelectItemsList.SelectedItems.Add(viewModel.Items[1]);
        DrainDispatcher(window);
        AssertEqual(2, multiSelectItemsList.SelectedItems.Count, "multi-select ListBox selected count");
        AssertEqual(true, multiSelectItemsList.SelectedItems.Contains(viewModel.Items[0]), "multi-select ListBox first item");
        AssertEqual(true, multiSelectItemsList.SelectedItems.Contains(viewModel.Items[1]), "multi-select ListBox second item");
        AssertGreaterThan(
            initialMultiEvents,
            window.MultiSelectorSelectionChangedCount,
            "multi-select ListBox SelectionChanged add count");

        int afterAddMultiEvents = window.MultiSelectorSelectionChangedCount;
        multiSelectItemsList.SelectedItems.Remove(viewModel.Items[0]);
        DrainDispatcher(window);
        AssertEqual(1, multiSelectItemsList.SelectedItems.Count, "multi-select ListBox selected removal count");
        AssertEqual(true, multiSelectItemsList.SelectedItems.Contains(viewModel.Items[1]), "multi-select ListBox retained item");
        AssertGreaterThan(
            afterAddMultiEvents,
            window.MultiSelectorSelectionChangedCount,
            "multi-select ListBox SelectionChanged remove count");

        AssertEqual("Scrollable details", expander.Header, "selector Expander header");
        AssertEqual(false, expander.IsExpanded, "selector Expander initial state");
        AssertEqual(ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility, "selector ScrollViewer vertical visibility");
        AssertEqual(ScrollBarVisibility.Disabled, scrollViewer.HorizontalScrollBarVisibility, "selector ScrollViewer horizontal visibility");
        AssertContains("SelectedValuePath", scrollText.Text, "selector ScrollViewer text");

        int initialExpandedEvents = window.SelectorExpanderExpandedCount;
        expander.IsExpanded = true;
        DrainDispatcher(window);
        AssertEqual(true, expander.IsExpanded, "selector Expander expanded state");
        AssertGreaterThan(
            initialExpandedEvents,
            window.SelectorExpanderExpandedCount,
            "selector Expander expanded count");

        int initialCollapsedEvents = window.SelectorExpanderCollapsedCount;
        expander.IsExpanded = false;
        DrainDispatcher(window);
        AssertEqual(false, expander.IsExpanded, "selector Expander restored state");
        AssertGreaterThan(
            initialCollapsedEvents,
            window.SelectorExpanderCollapsedCount,
            "selector Expander collapsed count");
    }

    private static void ValidateResourceControls(
        Window window,
        TextBlock componentResourceText,
        TextBlock localizedResourceText,
        AccessText accessText,
        TextBlock objectProviderText,
        TextBlock xmlProviderText,
        ItemsControl arrayItemsControl,
        ItemsControl compositeItemsControl,
        TextBlock nullIntrinsicText,
        TextBlock markupExtensionText,
        TextBlock packResourceText,
        TextBlock componentPackResourceText,
        TextBlock startupResourceText,
        TextBlock systemParameterText,
        TextBlock systemFontText,
        Border systemColorBorder,
        TextBlock systemColorText,
        ShowcaseThemedControl themedControl,
        Image drawingImageControl,
        Border drawingImageBrushBorder,
        Border dynamicResourceBorder,
        bool expectStartupResources)
    {
        var componentKey = new ComponentResourceKey(typeof(MainWindow), "ShowcaseComponentAccentBrush");
        var appBrush = Require<SolidColorBrush>(
            Application.Current?.TryFindResource(componentKey),
            "ComponentResourceKey application brush");
        var windowBrush = Require<SolidColorBrush>(
            window.FindResource(componentKey),
            "ComponentResourceKey window brush");
        var textBrush = Require<SolidColorBrush>(
            componentResourceText.Foreground,
            "ComponentResourceKey TextBlock foreground");

        AssertEqual(Color.FromRgb(0x23, 0x6B, 0x46), appBrush.Color, "ComponentResourceKey application brush color");
        AssertEqual(appBrush.Color, windowBrush.Color, "ComponentResourceKey window brush color");
        AssertEqual(appBrush.Color, textBrush.Color, "ComponentResourceKey TextBlock foreground color");
        AssertEqual("Component resource brush", componentResourceText.Text, "ComponentResourceKey TextBlock text");

        AssertEqual("ShowcaseLocalizedResourceText", localizedResourceText.Uid, "localized TextBlock Uid");
        AssertEqual("Localized resource metadata", localizedResourceText.Text, "localized TextBlock text");
        AssertEqual("$Text (Readable Modifiable Text)", Localization.GetAttributes(localizedResourceText), "localized TextBlock attributes");
        AssertEqual("$Text (Showcase localization comment)", Localization.GetComments(localizedResourceText), "localized TextBlock comments");

        AssertEqual("_Resource access key", accessText.Text, "AccessText text");
        var objectProvider = Require<ObjectDataProvider>(
            window.FindResource("ShowcaseObjectDataProvider"),
            "Showcase ObjectDataProvider resource");
        AssertEqual(false, objectProvider.IsAsynchronous, "ObjectDataProvider synchronous flag");
        AssertEqual("CreateSummary", objectProvider.MethodName, "ObjectDataProvider method name");
        AssertEqual(typeof(ShowcaseResourceFactory), objectProvider.ObjectType, "ObjectDataProvider object type");
        AssertEqual(2, objectProvider.MethodParameters.Count, "ObjectDataProvider method parameter count");
        AssertEqual("showcase-provider", Require<string>(objectProvider.MethodParameters[0], "ObjectDataProvider first parameter"), "ObjectDataProvider first parameter");
        AssertEqual(9, Require<int>(objectProvider.MethodParameters[1], "ObjectDataProvider second parameter"), "ObjectDataProvider second parameter");
        DrainDispatcher(window);
        AssertEqual("showcase-provider:9", objectProvider.Data, "ObjectDataProvider data");
        AssertEqual("showcase-provider:9", objectProviderText.Text, "ObjectDataProvider bound text");
        var objectProviderBinding = Require<Binding>(
            BindingOperations.GetBinding(objectProviderText, TextBlock.TextProperty),
            "ObjectDataProvider TextBlock binding");
        AssertEqual(objectProvider, objectProviderBinding.Source, "ObjectDataProvider binding source");

        var xmlProvider = Require<XmlDataProvider>(
            window.FindResource("ShowcaseXmlDataProvider"),
            "Showcase XmlDataProvider resource");
        AssertEqual(false, xmlProvider.IsAsynchronous, "XmlDataProvider synchronous flag");
        AssertEqual("/showcase/item", xmlProvider.XPath, "XmlDataProvider XPath");
        DrainDispatcher(window);
        AssertEqual("showcase-xml", xmlProviderText.Text, "XmlDataProvider bound text");
        var xmlProviderBinding = Require<Binding>(
            BindingOperations.GetBinding(xmlProviderText, TextBlock.TextProperty),
            "XmlDataProvider TextBlock binding");
        AssertEqual(xmlProvider, xmlProviderBinding.Source, "XmlDataProvider binding source");
        AssertEqual("@name", xmlProviderBinding.XPath, "XmlDataProvider binding XPath");

        var arrayItems = Require<string[]>(window.FindResource("ShowcaseStringArray"), "Showcase x:Array resource");
        AssertEqual(2, arrayItems.Length, "x:Array resource length");
        AssertEqual("Array alpha", arrayItems[0], "x:Array first item");
        AssertEqual("Array beta", arrayItems[1], "x:Array second item");
        AssertEqual(arrayItems, arrayItemsControl.ItemsSource, "x:Array ItemsControl source");
        AssertEqual(2, arrayItemsControl.Items.Count, "x:Array ItemsControl count");

        var compositeCollection = Require<CompositeCollection>(
            window.FindResource("ShowcaseCompositeCollection"),
            "Showcase CompositeCollection resource");
        AssertEqual(compositeCollection, compositeItemsControl.ItemsSource, "CompositeCollection ItemsControl source");
        AssertEqual(3, compositeCollection.Count, "CompositeCollection declared item count");
        AssertEqual("Composite static", compositeItemsControl.Items[0], "CompositeCollection first flattened item");
        AssertEqual("Composite alpha", compositeItemsControl.Items[1], "CompositeCollection first container item");
        AssertEqual("Composite beta", compositeItemsControl.Items[2], "CompositeCollection second container item");
        AssertEqual("Composite final", compositeItemsControl.Items[3], "CompositeCollection final flattened item");
        ShowcaseCompositeItemsProvider.Items.Add("Composite gamma");
        DrainDispatcher(window);
        AssertEqual(5, compositeItemsControl.Items.Count, "CompositeCollection collection-change item count");
        AssertEqual("Composite gamma", compositeItemsControl.Items[3], "CompositeCollection appended container item");
        ShowcaseCompositeItemsProvider.Items.Remove("Composite gamma");
        DrainDispatcher(window);
        AssertEqual(4, compositeItemsControl.Items.Count, "CompositeCollection restored item count");
        AssertEqual(null, nullIntrinsicText.Tag, "x:Null TextBlock tag");
        AssertEqual("Null intrinsic target", nullIntrinsicText.Text, "x:Null TextBlock text");
        AssertEqual("Markup Extension", markupExtensionText.Text, "MarkupExtension TextBlock text");

        AssertEqual("Pack resource loaded from Assets/ShowcaseResource.txt", packResourceText.Text, "pack resource TextBlock text");
        var applicationResources = Application.Current?.Resources
            ?? throw new InvalidOperationException("Expected application resources.");
        var componentPackText = Require<string>(
            applicationResources["ShowcaseComponentPackText"],
            "component pack text resource");
        AssertEqual("Component pack dictionary ready", componentPackText, "component pack text resource");
        AssertEqual(componentPackText, componentPackResourceText.Text, "component pack TextBlock text");
        var componentPackBrush = Require<SolidColorBrush>(
            applicationResources["ShowcaseComponentPackBrush"],
            "component pack brush resource");
        AssertEqual(Color.FromRgb(0x6B, 0x4E, 0x23), componentPackBrush.Color, "component pack brush color");
        var componentPackForeground = Require<SolidColorBrush>(
            componentPackResourceText.Foreground,
            "component pack TextBlock foreground");
        AssertEqual(componentPackBrush.Color, componentPackForeground.Color, "component pack TextBlock foreground color");
        AssertEqual(FontWeights.SemiBold, componentPackResourceText.FontWeight, "component pack TextBlock FontWeight");

        if (expectStartupResources)
        {
            AssertEqual(1, App.StartupEventCount, "Application Startup event count");
            AssertEqual(0, App.StartupArgumentCount, "Application Startup argument count");
            AssertEqual(0, App.ExitEventCount, "Application Exit event count before shutdown");
            AssertEqual(-1, App.LastExitCode, "Application Exit code before shutdown");
            AssertEqual("Startup property ready", Application.Current.Properties["ShowcaseStartupProperty"], "startup application property");
            AssertEqual(0, Application.Current.Properties["ShowcaseStartupArgumentCount"], "startup argument count property");
            AssertEqual("Startup resource ready", applicationResources["ShowcaseStartupText"], "startup application text resource");
            AssertEqual("Startup resource ready", startupResourceText.Text, "startup DynamicResource text");
            var startupBrush = Require<SolidColorBrush>(
                applicationResources["ShowcaseStartupBrush"],
                "startup application brush resource");
            var startupForeground = Require<SolidColorBrush>(
                startupResourceText.Foreground,
                "startup DynamicResource foreground");
            AssertEqual(Color.FromRgb(0x45, 0x5A, 0x64), startupBrush.Color, "startup application brush color");
            AssertEqual(startupBrush.Color, startupForeground.Color, "startup DynamicResource foreground color");
        }

        AssertGreaterThan(
            0,
            (int)Math.Round(SystemParameters.PrimaryScreenWidth),
            "SystemParameters primary screen width");
        AssertContains(
            SystemParameters.PrimaryScreenWidth.ToString(CultureInfo.CurrentCulture),
            systemParameterText.Text,
            "SystemParameters TextBlock text");
        var primaryScreenWidthResource = Require<double>(
            window.TryFindResource(SystemParameters.PrimaryScreenWidthKey),
            "SystemParameters primary screen width resource");
        AssertEqual(
            SystemParameters.PrimaryScreenWidth,
            primaryScreenWidthResource,
            "SystemParameters primary screen width resource value");

        AssertEqual("System font sample", systemFontText.Text, "SystemFonts TextBlock text");
        AssertEqual(
            SystemFonts.MessageFontFamily.Source,
            systemFontText.FontFamily.Source,
            "SystemFonts message font family");
        AssertEqual(SystemFonts.MessageFontSize, systemFontText.FontSize, "SystemFonts message font size");

        var systemWindowBrush = Require<SolidColorBrush>(
            window.FindResource(SystemColors.WindowBrushKey),
            "SystemColors WindowBrush resource");
        var systemWindowTextBrush = Require<SolidColorBrush>(
            window.FindResource(SystemColors.WindowTextBrushKey),
            "SystemColors WindowTextBrush resource");
        var systemBorderBrush = Require<SolidColorBrush>(
            window.FindResource(SystemColors.ControlDarkBrushKey),
            "SystemColors ControlDarkBrush resource");
        var systemColorBackground = Require<SolidColorBrush>(
            systemColorBorder.Background,
            "SystemColors Border background");
        var systemColorForeground = Require<SolidColorBrush>(
            systemColorText.Foreground,
            "SystemColors TextBlock foreground");
        var systemColorBorderBrush = Require<SolidColorBrush>(
            systemColorBorder.BorderBrush,
            "SystemColors Border border brush");
        AssertEqual(systemWindowBrush.Color, systemColorBackground.Color, "SystemColors Border background color");
        AssertEqual(systemWindowTextBrush.Color, systemColorForeground.Color, "SystemColors TextBlock foreground color");
        AssertEqual(systemBorderBrush.Color, systemColorBorderBrush.Color, "SystemColors Border brush color");
        AssertEqual("System color sample", systemColorText.Text, "SystemColors TextBlock text");

        AssertEqual("Generic theme default style", themedControl.Text, "Showcase themed control text");
        themedControl.ApplyTemplate();
        var themedTemplate = Require<ControlTemplate>(
            themedControl.Template,
            "Showcase themed control default template");
        var themedText = Require<TextBlock>(
            themedTemplate.FindName("ThemeText", themedControl),
            "Showcase themed control template text");
        var themedRoot = Require<Border>(
            themedTemplate.FindName("ThemeRoot", themedControl),
            "Showcase themed control template root");
        AssertEqual("Generic theme default style", themedText.Text, "Showcase themed control template binding");
        var themedForeground = Require<SolidColorBrush>(
            themedText.Foreground,
            "Showcase themed control template foreground");
        AssertEqual(Color.FromRgb(0x31, 0x2E, 0x81), themedForeground.Color, "Showcase themed control foreground color");
        var themedBackground = Require<SolidColorBrush>(
            themedRoot.Background,
            "Showcase themed control template background");
        AssertEqual(Color.FromRgb(0xEE, 0xF2, 0xFF), themedBackground.Color, "Showcase themed control background color");
        var themedBorderBrush = Require<SolidColorBrush>(
            themedRoot.BorderBrush,
            "Showcase themed control template border brush");
        AssertEqual(Color.FromRgb(0x4F, 0x46, 0xE5), themedBorderBrush.Color, "Showcase themed control component resource color");
        AssertEqual(new Thickness(1), themedRoot.BorderThickness, "Showcase themed control border thickness");
        AssertEqual(new Thickness(8, 5, 8, 5), themedRoot.Padding, "Showcase themed control padding");

        var drawingImage = Require<DrawingImage>(
            window.FindResource("ShowcaseDrawingImage"),
            "Showcase DrawingImage resource");
        var drawingGroup = Require<DrawingGroup>(
            drawingImage.Drawing,
            "Showcase DrawingImage DrawingGroup");
        AssertEqual(2, drawingGroup.Children.Count, "Showcase DrawingImage child count");
        var backgroundDrawing = Require<GeometryDrawing>(
            drawingGroup.Children[0],
            "Showcase DrawingImage background drawing");
        var backgroundBrush = Require<SolidColorBrush>(
            backgroundDrawing.Brush,
            "Showcase DrawingImage background brush");
        AssertEqual(Color.FromRgb(0x2F, 0x80, 0xED), backgroundBrush.Color, "Showcase DrawingImage background color");
        Require<RectangleGeometry>(
            backgroundDrawing.Geometry,
            "Showcase DrawingImage background geometry");
        var glyphDrawing = Require<GeometryDrawing>(
            drawingGroup.Children[1],
            "Showcase DrawingImage glyph drawing");
        Require<PathGeometry>(
            glyphDrawing.Geometry,
            "Showcase DrawingImage glyph geometry");
        AssertEqual(drawingImage, drawingImageControl.Source, "Showcase Image source");
        AssertEqual(Stretch.Uniform, drawingImageControl.Stretch, "Showcase Image stretch");
        var drawingImageBrush = Require<ImageBrush>(
            window.FindResource("ShowcaseDrawingImageBrush"),
            "Showcase DrawingImageBrush resource");
        AssertEqual(drawingImage, drawingImageBrush.ImageSource, "Showcase DrawingImageBrush source");
        AssertEqual(Stretch.Uniform, drawingImageBrush.Stretch, "Showcase DrawingImageBrush stretch");
        AssertEqual(drawingImageBrush, drawingImageBrushBorder.Background, "Showcase DrawingImageBrush Border background");
        ValidateFreezableResources(window);

        var initialDynamicBrush = Require<SolidColorBrush>(
            dynamicResourceBorder.Background,
            "dynamic resource Border initial background");
        AssertEqual(Color.FromRgb(0xF4, 0xF7, 0xFB), initialDynamicBrush.Color, "dynamic resource Border initial background color");

        applicationResources["ShowcasePanelBrush"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xF2, 0xCC));
        DrainDispatcher(window);
        var updatedDynamicBrush = Require<SolidColorBrush>(
            dynamicResourceBorder.Background,
            "dynamic resource Border updated background");
        AssertEqual(Color.FromRgb(0xFF, 0xF2, 0xCC), updatedDynamicBrush.Color, "dynamic resource Border updated background color");

        applicationResources.Remove("ShowcasePanelBrush");
        DrainDispatcher(window);
        var restoredDynamicBrush = Require<SolidColorBrush>(
            dynamicResourceBorder.Background,
            "dynamic resource Border restored background");
        AssertEqual(Color.FromRgb(0xF4, 0xF7, 0xFB), restoredDynamicBrush.Color, "dynamic resource Border restored background color");

        var resourceUri = new Uri("pack://application:,,,/Assets/ShowcaseResource.txt", UriKind.Absolute);
        var resourceInfo = Application.GetResourceStream(resourceUri)
            ?? throw new InvalidOperationException("Expected Showcase pack resource stream.");
        using var reader = new StreamReader(resourceInfo.Stream);
        AssertEqual(
            "Showcase pack resource loaded through Application.GetResourceStream.",
            reader.ReadToEnd().Trim(),
            "pack resource stream text");

        var relativeContentText = ReadContentStreamText(new Uri("Assets/ShowcaseContent.txt", UriKind.Relative));
        var absoluteContentText = ReadContentStreamText(new Uri("pack://application:,,,/Assets/ShowcaseContent.txt", UriKind.Absolute));
        var relativeRemoteText = ReadRemoteStreamText(new Uri("Assets/ShowcaseContent.txt", UriKind.Relative));
        var absoluteRemoteText = ReadRemoteStreamText(new Uri("pack://siteoforigin:,,,/Assets/ShowcaseContent.txt", UriKind.Absolute));
        const string expectedContentText = "Showcase copied content loaded through Application.GetContentStream and GetRemoteStream.";
        AssertEqual(expectedContentText, relativeContentText, "relative content stream text");
        AssertEqual(expectedContentText, absoluteContentText, "absolute content stream text");
        AssertEqual(expectedContentText, relativeRemoteText, "relative remote stream text");
        AssertEqual(expectedContentText, absoluteRemoteText, "absolute remote stream text");
    }

    private static string ReadContentStreamText(Uri uri)
    {
        var contentInfo = Application.GetContentStream(uri)
            ?? throw new InvalidOperationException($"Expected Showcase content stream for '{uri}'.");
        using var reader = new StreamReader(contentInfo.Stream);
        return reader.ReadToEnd().Trim();
    }

    private static string ReadRemoteStreamText(Uri uri)
    {
        var remoteInfo = Application.GetRemoteStream(uri)
            ?? throw new InvalidOperationException($"Expected Showcase remote stream for '{uri}'.");
        using var reader = new StreamReader(remoteInfo.Stream);
        return reader.ReadToEnd().Trim();
    }

    private static void ValidateFreezableResources(Window window)
    {
        var firstSharedFalseBrush = Require<SolidColorBrush>(
            window.FindResource("ShowcaseSharedFalseBrush"),
            "x:Shared=false first brush");
        var secondSharedFalseBrush = Require<SolidColorBrush>(
            window.FindResource("ShowcaseSharedFalseBrush"),
            "x:Shared=false second brush");
        AssertEqual(false, ReferenceEquals(firstSharedFalseBrush, secondSharedFalseBrush), "x:Shared=false brush instance identity");
        AssertEqual(Color.FromRgb(0x8B, 0x5C, 0xF6), firstSharedFalseBrush.Color, "x:Shared=false first brush color");
        AssertEqual(Color.FromRgb(0x8B, 0x5C, 0xF6), secondSharedFalseBrush.Color, "x:Shared=false second brush color");

        var freezableBrush = Require<SolidColorBrush>(
            window.FindResource("ShowcaseFreezableBrush"),
            "Showcase Freezable brush");
        AssertEqual(true, freezableBrush.CanFreeze, "Freezable brush CanFreeze");
        freezableBrush.Freeze();
        AssertEqual(true, freezableBrush.IsFrozen, "Freezable brush frozen state");
        var mutableClone = Require<SolidColorBrush>(
            freezableBrush.Clone(),
            "Freezable brush clone");
        AssertEqual(false, mutableClone.IsFrozen, "Freezable brush clone mutable state");
        mutableClone.Opacity = 0.5;
        AssertEqual(0.5, mutableClone.Opacity, "Freezable brush clone opacity");
        var currentValueClone = Require<SolidColorBrush>(
            mutableClone.CloneCurrentValue(),
            "Freezable brush current-value clone");
        AssertEqual(0.5, currentValueClone.Opacity, "Freezable brush current-value clone opacity");
    }

    private static void ValidateLayoutControls(
        DockPanel dockPanel,
        Border dockTop,
        Border dockLeft,
        Border dockRight,
        TextBlock dockFillText,
        WrapPanel wrapPanel,
        UniformGrid uniformGrid,
        Canvas shapeCanvas,
        System.Windows.Shapes.Rectangle shapeRectangle,
        System.Windows.Shapes.Ellipse shapeEllipse,
        System.Windows.Shapes.Line shapeLine,
        System.Windows.Shapes.Path shapePath,
        Grid splitterGrid,
        ColumnDefinition splitterLeftColumn,
        ColumnDefinition splitterRightColumn,
        Border splitterLeftPane,
        GridSplitter gridSplitter,
        Border splitterRightPane,
        Viewbox viewbox,
        TextBlock viewboxText)
    {
        AssertEqual(true, dockPanel.LastChildFill, "DockPanel LastChildFill");
        AssertEqual(4, dockPanel.Children.Count, "DockPanel child count");
        AssertEqual(Dock.Top, DockPanel.GetDock(dockTop), "DockPanel top attached Dock");
        AssertEqual(Dock.Left, DockPanel.GetDock(dockLeft), "DockPanel left attached Dock");
        AssertEqual(Dock.Right, DockPanel.GetDock(dockRight), "DockPanel right attached Dock");
        AssertEqual("Fill content", dockFillText.Text, "DockPanel fill text");

        AssertEqual(Orientation.Horizontal, wrapPanel.Orientation, "WrapPanel orientation");
        AssertEqual(90.0, wrapPanel.ItemWidth, "WrapPanel item width");
        AssertEqual(28.0, wrapPanel.ItemHeight, "WrapPanel item height");
        AssertEqual(3, wrapPanel.Children.Count, "WrapPanel child count");
        var thirdWrapButton = Require<Button>(wrapPanel.Children[2], "third WrapPanel Button");
        AssertEqual("Three", thirdWrapButton.Content, "third WrapPanel button content");

        AssertEqual(2, uniformGrid.Rows, "UniformGrid rows");
        AssertEqual(3, uniformGrid.Columns, "UniformGrid columns");
        AssertEqual(1, uniformGrid.FirstColumn, "UniformGrid first column");
        AssertEqual(3, uniformGrid.Children.Count, "UniformGrid child count");
        var secondUniformText = Require<TextBlock>(uniformGrid.Children[1], "second UniformGrid TextBlock");
        AssertEqual("Beta", secondUniformText.Text, "UniformGrid second child text");

        AssertEqual(260.0, shapeCanvas.Width, "shape Canvas width");
        AssertEqual(132.0, shapeCanvas.Height, "shape Canvas height");
        AssertEqual(4, shapeCanvas.Children.Count, "shape Canvas child count");
        AssertEqual(12.0, Canvas.GetLeft(shapeRectangle), "shape Rectangle Canvas.Left");
        AssertEqual(12.0, Canvas.GetTop(shapeRectangle), "shape Rectangle Canvas.Top");
        AssertEqual(72.0, shapeRectangle.Width, "shape Rectangle width");
        AssertEqual(44.0, shapeRectangle.Height, "shape Rectangle height");
        AssertEqual(6.0, shapeRectangle.RadiusX, "shape Rectangle RadiusX");
        AssertEqual(6.0, shapeRectangle.RadiusY, "shape Rectangle RadiusY");
        AssertEqual(2.0, shapeRectangle.StrokeThickness, "shape Rectangle stroke thickness");
        AssertEqual(
            Color.FromRgb(0x2F, 0x80, 0xED),
            Require<SolidColorBrush>(shapeRectangle.Fill, "shape Rectangle fill").Color,
            "shape Rectangle fill color");
        AssertEqual(
            Color.FromRgb(0x14, 0x55, 0xA3),
            Require<SolidColorBrush>(shapeRectangle.Stroke, "shape Rectangle stroke").Color,
            "shape Rectangle stroke color");
        AssertEqual(104.0, Canvas.GetLeft(shapeEllipse), "shape Ellipse Canvas.Left");
        AssertEqual(16.0, Canvas.GetTop(shapeEllipse), "shape Ellipse Canvas.Top");
        AssertEqual(54.0, shapeEllipse.Width, "shape Ellipse width");
        AssertEqual(54.0, shapeEllipse.Height, "shape Ellipse height");
        AssertEqual(3.0, shapeEllipse.StrokeThickness, "shape Ellipse stroke thickness");
        AssertEqual(
            Color.FromRgb(0xE7, 0xF5, 0xEE),
            Require<SolidColorBrush>(shapeEllipse.Fill, "shape Ellipse fill").Color,
            "shape Ellipse fill color");
        AssertEqual(16.0, shapeLine.X1, "shape Line X1");
        AssertEqual(98.0, shapeLine.Y1, "shape Line Y1");
        AssertEqual(154.0, shapeLine.X2, "shape Line X2");
        AssertEqual(76.0, shapeLine.Y2, "shape Line Y2");
        AssertEqual(4.0, shapeLine.StrokeThickness, "shape Line stroke thickness");
        AssertEqual(PenLineCap.Round, shapeLine.StrokeStartLineCap, "shape Line start cap");
        AssertEqual(PenLineCap.Round, shapeLine.StrokeEndLineCap, "shape Line end cap");
        AssertEqual(178.0, Canvas.GetLeft(shapePath), "shape Path Canvas.Left");
        AssertEqual(20.0, Canvas.GetTop(shapePath), "shape Path Canvas.Top");
        AssertEqual(2.0, shapePath.StrokeThickness, "shape Path stroke thickness");
        AssertEqual(new Rect(0.0, 0.0, 48.0, 40.0), shapePath.Data.Bounds, "shape Path data bounds");

        AssertEqual(3, splitterGrid.ColumnDefinitions.Count, "GridSplitter grid column count");
        AssertEqual(splitterLeftColumn, splitterGrid.ColumnDefinitions[0], "GridSplitter left column reference");
        AssertEqual(splitterRightColumn, splitterGrid.ColumnDefinitions[2], "GridSplitter right column reference");
        AssertEqual(120.0, splitterLeftColumn.Width.Value, "GridSplitter left column width");
        AssertEqual(true, splitterRightColumn.Width.IsStar, "GridSplitter right column star width");
        AssertEqual(0, Grid.GetColumn(splitterLeftPane), "GridSplitter left pane column");
        AssertEqual(1, Grid.GetColumn(gridSplitter), "GridSplitter column");
        AssertEqual(2, Grid.GetColumn(splitterRightPane), "GridSplitter right pane column");
        AssertEqual(6.0, gridSplitter.Width, "GridSplitter width");
        AssertEqual(GridResizeBehavior.PreviousAndNext, gridSplitter.ResizeBehavior, "GridSplitter resize behavior");
        AssertEqual(false, gridSplitter.ShowsPreview, "GridSplitter preview state");
        AssertEqual(12.0, gridSplitter.KeyboardIncrement, "GridSplitter keyboard increment");
        AssertEqual(HorizontalAlignment.Stretch, gridSplitter.HorizontalAlignment, "GridSplitter horizontal alignment");
        AssertEqual(VerticalAlignment.Stretch, gridSplitter.VerticalAlignment, "GridSplitter vertical alignment");

        splitterLeftColumn.Width = new GridLength(150.0);
        AssertEqual(150.0, splitterLeftColumn.Width.Value, "GridSplitter left column updated width");
        splitterLeftColumn.Width = new GridLength(120.0);

        AssertEqual(Stretch.Uniform, viewbox.Stretch, "Viewbox stretch");
        AssertEqual(54.0, viewbox.MaxHeight, "Viewbox max height");
        AssertEqual(viewboxText, viewbox.Child, "Viewbox child reference");
        AssertEqual("Scaled layout content", viewboxText.Text, "Viewbox text");
    }

    private static void ValidateInputControls(
        MainWindow window,
        MainViewModel viewModel,
        ToolBarTray toolBarTray,
        ToolBar toolBar,
        Button refreshButton,
        Separator toolBarSeparator,
        ToggleButton toolBarToggle,
        Button popupOwnerButton,
        Popup inputPopup,
        ToggleButton inputToggle,
        RadioButton frameworkRadio,
        RadioButton renderingRadio,
        RepeatButton repeatButton,
        StackPanel inputThumbPanel,
        Thumb inputDragThumb,
        TextBlock inputDragStatusText,
        Border showcaseDropTarget,
        TextBlock showcaseDropTargetText,
        WpfCalendar calendar,
        DatePicker datePicker,
        StackPanel keyboardNavigationPanel,
        Label keyboardNavigationAccessLabel,
        TextBox keyboardNavigationFirstBox,
        Button keyboardNavigationSecondButton,
        TextBox keyboardNavigationThirdBox)
    {
        AssertEqual(1, toolBarTray.ToolBars.Count, "Showcase ToolBarTray toolbar count");
        AssertEqual(toolBar, toolBarTray.ToolBars[0], "Showcase ToolBarTray toolbar reference");
        AssertEqual("Showcase tools", toolBar.Header, "Showcase ToolBar header");
        AssertEqual(3, toolBar.Items.Count, "Showcase ToolBar item count");
        AssertEqual(refreshButton, toolBar.Items[0], "Showcase ToolBar refresh item");
        AssertEqual(toolBarSeparator, toolBar.Items[1], "Showcase ToolBar separator item");
        AssertEqual(toolBarToggle, toolBar.Items[2], "Showcase ToolBar toggle item");
        AssertEqual(MainWindow.RefreshStatusCommand, refreshButton.Command, "Showcase ToolBar refresh command");

        var toolTip = Require<ToolTip>(refreshButton.ToolTip, "toolbar refresh ToolTip");
        var toolTipText = Require<TextBlock>(toolTip.Content, "toolbar refresh ToolTip text");
        AssertEqual(PlacementMode.Bottom, toolTip.Placement, "toolbar refresh ToolTip placement");
        AssertEqual("Refresh status command", toolTipText.Text, "toolbar refresh ToolTip text");

        AssertEqual(popupOwnerButton, inputPopup.PlacementTarget, "input Popup placement target");
        AssertEqual(PlacementMode.Bottom, inputPopup.Placement, "input Popup placement");
        AssertEqual(false, inputPopup.StaysOpen, "input Popup StaysOpen");
        AssertEqual(true, inputPopup.AllowsTransparency, "input Popup AllowsTransparency");
        AssertEqual(false, inputPopup.IsOpen, "input Popup initial open state");
        var popupBorder = Require<Border>(inputPopup.Child, "input Popup Border");
        var popupText = Require<TextBlock>(popupBorder.Child, "input Popup TextBlock");
        AssertEqual("Popup content", popupText.Text, "input Popup text");

        ValidateToggleBinding(window, viewModel, toolBarToggle, "toolbar ToggleButton");
        ValidateToggleBinding(window, viewModel, inputToggle, "input ToggleButton");

        AssertEqual("ShowcaseCategory", frameworkRadio.GroupName, "framework RadioButton group");
        AssertEqual("ShowcaseCategory", renderingRadio.GroupName, "rendering RadioButton group");
        AssertEqual("Framework", frameworkRadio.Tag, "framework RadioButton tag");
        AssertEqual("Rendering", renderingRadio.Tag, "rendering RadioButton tag");
        AssertEqual(true, frameworkRadio.IsChecked == true, "framework RadioButton initial state");
        AssertEqual(false, renderingRadio.IsChecked == true, "rendering RadioButton initial state");

        int initialRadioEvents = window.CategoryRadioCheckedCount;
        renderingRadio.IsChecked = true;
        DrainDispatcher(window);
        AssertEqual(false, frameworkRadio.IsChecked == true, "framework RadioButton unchecked state");
        AssertEqual(true, renderingRadio.IsChecked == true, "rendering RadioButton checked state");
        AssertEqual("RenderingRadioButton", window.LastCategoryRadioName, "last checked RadioButton name");
        AssertEqual("Rendering", viewModel.SelectedCategory, "RadioButton updated selected category");
        AssertGreaterThan(initialRadioEvents, window.CategoryRadioCheckedCount, "RadioButton checked event count");

        frameworkRadio.IsChecked = true;
        DrainDispatcher(window);
        AssertEqual(true, frameworkRadio.IsChecked == true, "framework RadioButton restored state");
        AssertEqual(false, renderingRadio.IsChecked == true, "rendering RadioButton restored state");
        AssertEqual("FrameworkRadioButton", window.LastCategoryRadioName, "last restored RadioButton name");
        AssertEqual("Framework", viewModel.SelectedCategory, "RadioButton restored selected category");

        AssertEqual(180, repeatButton.Delay, "RepeatButton delay");
        AssertEqual(70, repeatButton.Interval, "RepeatButton interval");
        AssertEqual("Repeat action", repeatButton.Content, "RepeatButton content");
        int initialRepeatClicks = window.InputRepeatButtonClickCount;
        repeatButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, repeatButton));
        DrainDispatcher(window);
        AssertEqual(initialRepeatClicks + 1, window.InputRepeatButtonClickCount, "RepeatButton click count");

        ValidateThumbDragManager(window, inputThumbPanel, inputDragThumb, inputDragStatusText);
        ValidateDragDropManager(window, showcaseDropTarget, showcaseDropTargetText);

        var expectedInitialDate = new DateTime(2026, 6, 23);
        AssertEqual(CalendarSelectionMode.SingleDate, calendar.SelectionMode, "Calendar selection mode");
        AssertEqual(expectedInitialDate, calendar.SelectedDate, "Calendar initial selected date");
        AssertEqual(expectedInitialDate, datePicker.SelectedDate, "DatePicker initial selected date");
        AssertEqual(expectedInitialDate, viewModel.SelectedDate, "view model initial selected date");
        AssertEqual("SelectedDate", GetSelectedDateBindingPath(calendar), "Calendar SelectedDate binding path");
        AssertEqual("SelectedDate", GetSelectedDateBindingPath(datePicker), "DatePicker SelectedDate binding path");

        int initialDateEvents = window.InputDateSelectionChangedCount;
        datePicker.SelectedDate = new DateTime(2026, 6, 24);
        UpdateSource(datePicker, DatePicker.SelectedDateProperty);
        UpdateBinding(calendar, WpfCalendar.SelectedDateProperty);
        DrainDispatcher(window);
        AssertEqual(new DateTime(2026, 6, 24), viewModel.SelectedDate, "DatePicker updated view model date");
        AssertEqual(new DateTime(2026, 6, 24), calendar.SelectedDate, "DatePicker updated Calendar date");
        AssertEqual("InputDatePicker", window.LastDateSelectionSenderName, "DatePicker selection sender");
        AssertGreaterThan(initialDateEvents, window.InputDateSelectionChangedCount, "DatePicker selection event count");

        int afterDatePickerEvents = window.InputDateSelectionChangedCount;
        calendar.SelectedDate = new DateTime(2026, 6, 25);
        UpdateSource(calendar, WpfCalendar.SelectedDateProperty);
        UpdateBinding(datePicker, DatePicker.SelectedDateProperty);
        DrainDispatcher(window);
        AssertEqual(new DateTime(2026, 6, 25), viewModel.SelectedDate, "Calendar updated view model date");
        AssertEqual(new DateTime(2026, 6, 25), datePicker.SelectedDate, "Calendar updated DatePicker date");
        AssertEqual(1, calendar.SelectedDates.Count, "Calendar selected dates count");
        AssertEqual(new DateTime(2026, 6, 25), calendar.SelectedDates[0], "Calendar selected date collection item");
        AssertEqual("InputCalendar", window.LastDateSelectionSenderName, "Calendar selection sender");
        AssertGreaterThan(afterDatePickerEvents, window.InputDateSelectionChangedCount, "Calendar selection event count");

        var showcaseTabControl = Require<TabControl>(
            window.FindName("ShowcaseTabControl"),
            "Showcase TabControl for input validation");
        int previousTabIndex = showcaseTabControl.SelectedIndex;
        var inputTab = Require<TabItem>(showcaseTabControl.Items[4], "Showcase input TabItem");
        AssertEqual("Input", inputTab.Header, "Showcase input TabItem header");

        try
        {
            showcaseTabControl.SelectedIndex = 4;
            DrainDispatcher(window);
            keyboardNavigationPanel.UpdateLayout();
            ValidateKeyboardNavigation(
                window,
                keyboardNavigationPanel,
                keyboardNavigationAccessLabel,
                keyboardNavigationFirstBox,
                keyboardNavigationSecondButton,
                keyboardNavigationThirdBox);
        }
        finally
        {
            showcaseTabControl.SelectedIndex = previousTabIndex;
            DrainDispatcher(window);
        }
    }

    private static void ValidateThumbDragManager(
        MainWindow window,
        StackPanel inputThumbPanel,
        Thumb inputDragThumb,
        TextBlock inputDragStatusText)
    {
        AssertEqual(32.0, inputDragThumb.Width, "input Thumb width");
        AssertEqual(20.0, inputDragThumb.Height, "input Thumb height");
        AssertEqual("showcase drag thumb", inputDragThumb.Tag, "input Thumb tag");
        AssertEqual(false, inputDragThumb.Focusable, "input Thumb focusable metadata");
        AssertEqual(false, inputDragThumb.IsDragging, "input Thumb initial dragging state");
        AssertEqual("Drag idle", inputDragStatusText.Text, "input Thumb initial status");
        AssertEqual(0, window.InputThumbDragStartedCount, "input Thumb initial DragStarted count");
        AssertEqual(0, window.InputThumbDragDeltaCount, "input Thumb initial DragDelta count");
        AssertEqual(0, window.InputThumbDragCompletedCount, "input Thumb initial DragCompleted count");
        AssertEqual(0, window.InputBubbledThumbDragDeltaCount, "input Thumb initial bubbled DragDelta count");

        var started = new DragStartedEventArgs(1.5, 2.5)
        {
            RoutedEvent = Thumb.DragStartedEvent
        };
        var delta = new DragDeltaEventArgs(4.0, 6.0)
        {
            RoutedEvent = Thumb.DragDeltaEvent
        };
        var completed = new DragCompletedEventArgs(8.0, 10.0, true)
        {
            RoutedEvent = Thumb.DragCompletedEvent
        };

        inputDragThumb.RaiseEvent(started);
        inputDragThumb.RaiseEvent(delta);
        inputDragThumb.RaiseEvent(completed);
        DrainDispatcher(window);

        AssertEqual(1, window.InputThumbDragStartedCount, "input Thumb DragStarted handler count");
        AssertEqual("InputDragThumb", window.LastInputThumbDragStartedSenderName, "input Thumb DragStarted sender");
        AssertEqual("DragStarted", window.LastInputThumbDragStartedRoutedEventName, "input Thumb DragStarted routed event");
        AssertEqual(1.5, window.LastInputThumbDragStartedHorizontalOffset, "input Thumb DragStarted horizontal offset");
        AssertEqual(2.5, window.LastInputThumbDragStartedVerticalOffset, "input Thumb DragStarted vertical offset");

        AssertEqual(1, window.InputThumbDragDeltaCount, "input Thumb DragDelta handler count");
        AssertEqual("InputDragThumb", window.LastInputThumbDragDeltaSenderName, "input Thumb DragDelta sender");
        AssertEqual("DragDelta", window.LastInputThumbDragDeltaRoutedEventName, "input Thumb DragDelta routed event");
        AssertEqual(4.0, window.LastInputThumbDragDeltaHorizontalChange, "input Thumb DragDelta horizontal change");
        AssertEqual(6.0, window.LastInputThumbDragDeltaVerticalChange, "input Thumb DragDelta vertical change");
        AssertEqual("Dragged 4, 6", inputDragStatusText.Text, "input Thumb drag status text");

        AssertEqual(1, window.InputBubbledThumbDragDeltaCount, "input Thumb bubbled DragDelta handler count");
        AssertEqual("InputThumbPanel", window.LastInputBubbledThumbDragDeltaSenderName, "input Thumb bubbled sender");
        AssertEqual("InputDragThumb", window.LastInputBubbledThumbDragDeltaOriginalSourceName, "input Thumb bubbled original source");
        AssertEqual("DragDelta", window.LastInputBubbledThumbDragDeltaRoutedEventName, "input Thumb bubbled routed event");
        AssertEqual(4.0, window.LastInputBubbledThumbDragDeltaHorizontalChange, "input Thumb bubbled horizontal change");
        AssertEqual(6.0, window.LastInputBubbledThumbDragDeltaVerticalChange, "input Thumb bubbled vertical change");

        AssertEqual(1, window.InputThumbDragCompletedCount, "input Thumb DragCompleted handler count");
        AssertEqual("InputDragThumb", window.LastInputThumbDragCompletedSenderName, "input Thumb DragCompleted sender");
        AssertEqual("DragCompleted", window.LastInputThumbDragCompletedRoutedEventName, "input Thumb DragCompleted routed event");
        AssertEqual(8.0, window.LastInputThumbDragCompletedHorizontalChange, "input Thumb DragCompleted horizontal change");
        AssertEqual(10.0, window.LastInputThumbDragCompletedVerticalChange, "input Thumb DragCompleted vertical change");
        AssertEqual(true, window.LastInputThumbDragCompletedCanceled, "input Thumb DragCompleted canceled state");
        AssertEqual(true, ReferenceEquals(inputThumbPanel, inputDragThumb.Parent), "input Thumb logical parent");
    }

    private static void ValidateDragDropManager(MainWindow window, Border dropTarget, TextBlock dropTargetText)
    {
        AssertEqual(true, dropTarget.AllowDrop, "Showcase drop target AllowDrop");
        AssertEqual("Drop target idle", dropTargetText.Text, "Showcase drop target initial text");
        AssertEqual(0, window.ShowcasePreviewDragEnterCount, "Showcase initial PreviewDragEnter count");
        AssertEqual(0, window.ShowcaseDragEnterCount, "Showcase initial DragEnter count");
        AssertEqual(0, window.ShowcasePreviewDragOverCount, "Showcase initial PreviewDragOver count");
        AssertEqual(0, window.ShowcaseDragOverCount, "Showcase initial DragOver count");
        AssertEqual(0, window.ShowcasePreviewDropCount, "Showcase initial PreviewDrop count");
        AssertEqual(0, window.ShowcaseDropCount, "Showcase initial Drop count");

        var dataObject = new DataObject();
        dataObject.SetData(DataFormats.UnicodeText, "showcase drag text");
        dataObject.SetData(DataFormats.FileDrop, new[] { "/tmp/progpu-wpf-showcase-drop.txt" });
        var allowedEffects = DragDropEffects.Copy | DragDropEffects.Move;
        var point = new Point(12.0, 18.0);

        ProcessDragDropEvent(dropTarget, DragDrop.DragEnterEvent, dataObject, point, allowedEffects);
        ProcessDragDropEvent(dropTarget, DragDrop.DragOverEvent, dataObject, point, allowedEffects);
        ProcessDragDropEvent(dropTarget, DragDrop.DropEvent, dataObject, point, allowedEffects);
        DrainDispatcher(window);

        AssertEqual(1, window.ShowcasePreviewDragEnterCount, "Showcase PreviewDragEnter count");
        AssertEqual(1, window.ShowcaseDragEnterCount, "Showcase DragEnter count");
        AssertEqual(1, window.ShowcasePreviewDragOverCount, "Showcase PreviewDragOver count");
        AssertEqual(1, window.ShowcaseDragOverCount, "Showcase DragOver count");
        AssertEqual(1, window.ShowcasePreviewDropCount, "Showcase PreviewDrop count");
        AssertEqual(1, window.ShowcaseDropCount, "Showcase Drop count");
        AssertEqual("PreviewDragEnter", window.LastShowcasePreviewDragEnterEventName, "Showcase PreviewDragEnter event");
        AssertEqual("DragEnter", window.LastShowcaseDragEnterEventName, "Showcase DragEnter event");
        AssertEqual("PreviewDragOver", window.LastShowcasePreviewDragOverEventName, "Showcase PreviewDragOver event");
        AssertEqual("DragOver", window.LastShowcaseDragOverEventName, "Showcase DragOver event");
        AssertEqual("PreviewDrop", window.LastShowcasePreviewDropEventName, "Showcase PreviewDrop event");
        AssertEqual("Drop", window.LastShowcaseDropEventName, "Showcase Drop event");
        AssertEqual("showcase drag text", window.LastShowcaseDropText, "Showcase dropped UnicodeText");
        AssertEqual(1, window.LastShowcaseDropFileCount, "Showcase dropped file count");
        AssertEqual("/tmp/progpu-wpf-showcase-drop.txt", window.LastShowcaseDropFirstFile, "Showcase dropped first file");
        AssertEqual(allowedEffects.ToString(), window.LastShowcaseDropAllowedEffects, "Showcase drop allowed effects");
        AssertEqual(DragDropEffects.Move.ToString(), window.LastShowcaseDropEffects, "Showcase drop selected effect");
        AssertEqual(12.0, window.LastShowcaseDropX, "Showcase drop X position");
        AssertEqual(18.0, window.LastShowcaseDropY, "Showcase drop Y position");
        AssertEqual("showcase drag text (1)", dropTargetText.Text, "Showcase drop target updated text");
    }

    private static DragDropEffects ProcessDragDropEvent(
        DependencyObject target,
        RoutedEvent routedEvent,
        IDataObject dataObject,
        Point point,
        DragDropEffects allowedEffects)
    {
        return DragDrop.ProcessPortableDragDrop(
            target,
            routedEvent,
            dataObject,
            DragDropKeyStates.LeftMouseButton,
            allowedEffects,
            DragDropEffects.Move,
            point);
    }

    private static void ValidateKeyboardNavigation(
        Window window,
        StackPanel panel,
        Label accessLabel,
        TextBox firstBox,
        Button secondButton,
        TextBox thirdBox)
    {
        AssertEqual(true, FocusManager.GetIsFocusScope(panel), "keyboard navigation focus-scope flag");
        AssertEqual(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(panel), "keyboard navigation tab mode");
        AssertEqual(
            KeyboardNavigationMode.Cycle,
            KeyboardNavigation.GetControlTabNavigation(panel),
            "keyboard navigation control-tab mode");
        AssertEqual(
            KeyboardNavigationMode.Contained,
            KeyboardNavigation.GetDirectionalNavigation(panel),
            "keyboard navigation directional mode");
        AssertEqual(0, firstBox.TabIndex, "first keyboard navigation TabIndex");
        AssertEqual(1, secondButton.TabIndex, "second keyboard navigation TabIndex");
        AssertEqual(2, thirdBox.TabIndex, "third keyboard navigation TabIndex");
        AssertEqual("_First focus target", accessLabel.Content, "keyboard navigation access Label content");
        AssertEqual(firstBox, accessLabel.Target, "keyboard navigation access Label target");
        AssertEqual("First focus target", firstBox.Text, "first keyboard navigation text");
        AssertEqual("Second focus target", secondButton.Content, "second keyboard navigation content");
        AssertEqual("Third focus target", thirdBox.Text, "third keyboard navigation text");
        AssertEqual(firstBox, FocusManager.GetFocusedElement(panel), "initial keyboard navigation logical focus");

        FocusManager.SetFocusedElement(panel, secondButton);
        DrainDispatcher(window);
        AssertEqual(secondButton, FocusManager.GetFocusedElement(panel), "updated keyboard navigation logical focus");

        FocusManager.SetFocusedElement(panel, thirdBox);
        DrainDispatcher(window);
        AssertEqual(thirdBox, FocusManager.GetFocusedElement(panel), "third keyboard navigation logical focus");

        FocusManager.SetFocusedElement(panel, firstBox);
        DrainDispatcher(window);
        AssertEqual(firstBox, FocusManager.GetFocusedElement(panel), "restored keyboard navigation logical focus");

        var presentationSource = PresentationSource.FromVisual(window);
        if (presentationSource is null)
        {
            return;
        }

        AssertEqual(
            true,
            AccessKeyManager.IsKeyRegistered(presentationSource, "F"),
            "keyboard navigation access key registered");
        Keyboard.ClearFocus();
        AssertEqual(
            false,
            ReferenceEquals(firstBox, Keyboard.FocusedElement),
            "keyboard navigation focus cleared before access key");
        AssertEqual(
            false,
            AccessKeyManager.ProcessKey(presentationSource, "F", false),
            "keyboard navigation access key processed");
        AssertEqual(firstBox, Keyboard.FocusedElement, "keyboard navigation access key focused target");
        AssertEqual(
            firstBox,
            FocusManager.GetFocusedElement(panel),
            "keyboard navigation access key logical focus");

        AssertEqual(firstBox, Keyboard.Focus(firstBox), "keyboard navigation initial keyboard focus");
        AssertEqual(true, firstBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "keyboard navigation next move");
        AssertEqual(secondButton, Keyboard.FocusedElement, "keyboard navigation focused second target");
        AssertEqual(true, secondButton.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "keyboard navigation second next move");
        AssertEqual(thirdBox, Keyboard.FocusedElement, "keyboard navigation focused third target");
        AssertEqual(true, thirdBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)), "keyboard navigation cycle next move");
        AssertEqual(firstBox, Keyboard.FocusedElement, "keyboard navigation cycled first target");
        AssertEqual(true, firstBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)), "keyboard navigation previous move");
        AssertEqual(thirdBox, Keyboard.FocusedElement, "keyboard navigation cycled previous target");
        Keyboard.ClearFocus();
    }

    private static void ValidateToggleBinding(
        MainWindow window,
        MainViewModel viewModel,
        ToggleButton toggleButton,
        string description)
    {
        var binding = Require<Binding>(
            BindingOperations.GetBinding(toggleButton, ToggleButton.IsCheckedProperty),
            $"{description} IsChecked binding");
        AssertEqual("ActionsEnabled", binding.Path.Path, $"{description} IsChecked path");
        AssertEqual(BindingMode.TwoWay, binding.Mode, $"{description} IsChecked mode");
        AssertEqual(true, toggleButton.IsChecked == true, $"{description} initial checked state");

        int initialUncheckedEvents = window.InputToggleUncheckedCount;
        toggleButton.IsChecked = false;
        DrainDispatcher(window);
        AssertEqual(false, viewModel.ActionsEnabled, $"{description} unchecked view model state");
        AssertEqual(false, toggleButton.IsChecked == true, $"{description} unchecked state");
        AssertGreaterThan(initialUncheckedEvents, window.InputToggleUncheckedCount, $"{description} unchecked event count");

        int initialCheckedEvents = window.InputToggleCheckedCount;
        toggleButton.IsChecked = true;
        DrainDispatcher(window);
        AssertEqual(true, viewModel.ActionsEnabled, $"{description} restored view model state");
        AssertEqual(true, toggleButton.IsChecked == true, $"{description} restored checked state");
        AssertGreaterThan(initialCheckedEvents, window.InputToggleCheckedCount, $"{description} checked event count");
    }

    private static string GetColumnBindingPath(DataGridColumn column)
    {
        return column is DataGridBoundColumn { Binding: Binding binding }
            ? binding.Path.Path
            : throw new InvalidOperationException($"Expected {column.Header} column to have a Binding.");
    }

    private static string GetGridViewColumnBindingPath(GridViewColumn column)
    {
        return column.DisplayMemberBinding is Binding { Path: { } path }
            ? path.Path
            : throw new InvalidOperationException($"Expected {column.Header} column to have a display member Binding.");
    }

    private static string GetBindingPath(BindingBase binding)
    {
        return binding is Binding { Path: { } path }
            ? path.Path
            : throw new InvalidOperationException("Expected a standard Binding with a path.");
    }

    private static void ValidateSelectedItemTemplate(DataTemplate template)
    {
        var root = Require<FrameworkElement>(
            template.LoadContent(),
            "selected item template root");
        var nameText = Require<TextBlock>(
            root.FindName("TemplateNameText"),
            "selected item template name TextBlock");
        var categoryText = Require<TextBlock>(
            root.FindName("TemplateCategoryText"),
            "selected item template category TextBlock");
        var activeText = Require<TextBlock>(
            root.FindName("TemplateActiveText"),
            "selected item template active TextBlock");

        AssertEqual("Name", GetTextBindingPath(nameText), "selected item template name binding path");
        AssertEqual("Category", GetTextBindingPath(categoryText), "selected item template category binding path");
        AssertEqual("IsActive", GetTextBindingPath(activeText), "selected item template active binding path");
    }

    private static void ValidateImplicitItemTemplate(
        MainViewModel viewModel,
        ContentControl contentControl,
        DataTemplate template)
    {
        AssertEqual(viewModel.SelectedItem, contentControl.Content, "implicit item content");
        AssertEqual<DataTemplate?>(null, contentControl.ContentTemplate, "implicit item explicit ContentTemplate");

        var templateKey = Require<DataTemplateKey>(template.DataTemplateKey, "implicit item DataTemplate key");
        AssertEqual(typeof(ShowcaseItem), templateKey.DataType, "implicit item DataTemplate key type");

        var root = Require<FrameworkElement>(
            template.LoadContent(),
            "implicit item template root");
        var nameText = Require<TextBlock>(
            root.FindName("ImplicitTemplateNameText"),
            "implicit item template name TextBlock");
        var categoryText = Require<TextBlock>(
            root.FindName("ImplicitTemplateCategoryText"),
            "implicit item template category TextBlock");

        AssertEqual("Name", GetTextBindingPath(nameText), "implicit item template name binding path");
        AssertEqual("Category", GetTextBindingPath(categoryText), "implicit item template category binding path");
    }

    private static void ValidateTemplateSelector(
        MainViewModel viewModel,
        ListBox selectorItemsList,
        DataTemplate activeTemplate,
        DataTemplate inactiveTemplate,
        ShowcaseItemTemplateSelector selector,
        Style containerStyle)
    {
        AssertEqual(viewModel.Items, selectorItemsList.ItemsSource, "selector ListBox items source");
        AssertEqual(selector, selectorItemsList.ItemTemplateSelector, "selector ListBox template selector");
        AssertEqual(containerStyle, selectorItemsList.ItemContainerStyle, "selector ListBox item container style");
        AssertEqual(typeof(ListBoxItem), containerStyle.TargetType, "selector item container style target type");
        AssertEqual(activeTemplate, selector.ActiveTemplate, "active selector template");
        AssertEqual(inactiveTemplate, selector.InactiveTemplate, "inactive selector template");
        AssertEqual(activeTemplate, selector.SelectTemplate(viewModel.Items[0], selectorItemsList), "active selector result");
        AssertEqual(inactiveTemplate, selector.SelectTemplate(viewModel.Items[1], selectorItemsList), "inactive selector result");
        Require<WrapPanel>(
            selectorItemsList.ItemsPanel.LoadContent(),
            "selector ListBox ItemsPanel root");

        ValidateSelectorTemplate(
            activeTemplate,
            "SelectorActiveNameText",
            "active selector template binding path");
        ValidateSelectorTemplate(
            inactiveTemplate,
            "SelectorInactiveNameText",
            "inactive selector template binding path");
        ValidateSelectorItemContainerStyle(containerStyle);
    }

    private static void ValidateItemContainerStyleSelector(
        MainViewModel viewModel,
        ListBox styleSelectorItemsList,
        Style activeStyle,
        Style inactiveStyle,
        ShowcaseItemContainerStyleSelector selector)
    {
        AssertEqual(viewModel.Items, styleSelectorItemsList.ItemsSource, "style selector ListBox items source");
        AssertEqual("Name", styleSelectorItemsList.DisplayMemberPath, "style selector ListBox DisplayMemberPath");
        AssertEqual(
            selector,
            styleSelectorItemsList.ItemContainerStyleSelector,
            "style selector ListBox ItemContainerStyleSelector");
        AssertEqual(activeStyle, selector.ActiveStyle, "active item container selector style");
        AssertEqual(inactiveStyle, selector.InactiveStyle, "inactive item container selector style");
        AssertEqual(activeStyle, selector.SelectStyle(viewModel.Items[0], styleSelectorItemsList), "active item container selector result");
        AssertEqual(inactiveStyle, selector.SelectStyle(viewModel.Items[1], styleSelectorItemsList), "inactive item container selector result");
        AssertEqual(inactiveStyle, selector.SelectStyle(new object(), styleSelectorItemsList), "fallback item container selector result");

        ValidateSelectedItemContainerStyle(activeStyle, "active", "ActiveStyleContainer");
        ValidateSelectedItemContainerStyle(inactiveStyle, "inactive", "InactiveStyleContainer");
    }

    private static void ValidateSelectorTemplate(
        DataTemplate template,
        string name,
        string description)
    {
        var root = Require<FrameworkElement>(
            template.LoadContent(),
            description);
        var textBlock = Require<TextBlock>(
            root.FindName(name),
            description);

        AssertEqual("Name", GetTextBindingPath(textBlock), description);
    }

    private static void ValidateSelectedItemContainerStyle(
        Style style,
        string description,
        string expectedTag)
    {
        AssertEqual(typeof(ListBoxItem), style.TargetType, $"{description} item container style target type");
        AssertEqual(3, style.Setters.Count, $"{description} item container setter count");

        var tagSetter = Require<Setter>(
            style.Setters[0],
            $"{description} item container Tag setter");
        var marginSetter = Require<Setter>(
            style.Setters[1],
            $"{description} item container Margin setter");
        var alignmentSetter = Require<Setter>(
            style.Setters[2],
            $"{description} item container HorizontalContentAlignment setter");

        AssertEqual(FrameworkElement.TagProperty, tagSetter.Property, $"{description} item container Tag property");
        AssertEqual(expectedTag, tagSetter.Value, $"{description} item container Tag value");
        AssertEqual(FrameworkElement.MarginProperty, marginSetter.Property, $"{description} item container Margin property");
        AssertEqual(new Thickness(0, 0, 0, 4), marginSetter.Value, $"{description} item container Margin value");
        AssertEqual(Control.HorizontalContentAlignmentProperty, alignmentSetter.Property, $"{description} item container alignment property");
        AssertEqual(HorizontalAlignment.Stretch, alignmentSetter.Value, $"{description} item container alignment value");
    }

    private static void ValidateSelectorItemContainerStyle(Style style)
    {
        AssertEqual(2, style.Setters.Count, "selector item container setter count");
        var trigger = Require<DataTrigger>(
            style.Triggers[0],
            "selector item container DataTrigger");
        var triggerSetter = Require<Setter>(
            trigger.Setters[0],
            "selector item container trigger setter");

        AssertEqual("IsActive", GetBindingPath(trigger.Binding), "selector item container trigger binding");
        AssertEqual("True", trigger.Value?.ToString(), "selector item container trigger value");
        AssertEqual(FrameworkElement.TagProperty, triggerSetter.Property, "selector item container trigger property");
        AssertEqual("ActiveContainer", triggerSetter.Value, "selector item container trigger value");
    }

    private static void ValidateBasedOnButton(Button button, Style style)
    {
        AssertEqual(style, button.Style, "BasedOn Button style");
        AssertEqual("BasedOn style", button.Content, "BasedOn Button content");
        AssertEqual(3, style.Setters.Count, "BasedOn Button derived setter count");
        AssertEqual("BasedOnStyle", button.Tag, "BasedOn Button derived Tag setter");
        AssertEqual(104.0, button.MinWidth, "BasedOn Button inherited MinWidth setter");
        AssertEqual(new Thickness(10, 5, 10, 5), button.Padding, "BasedOn Button inherited Padding setter");

        var background = Require<SolidColorBrush>(button.Background, "BasedOn Button background");
        var foreground = Require<SolidColorBrush>(button.Foreground, "BasedOn Button foreground");
        AssertEqual(Color.FromRgb(0x24, 0x6B, 0xFE), background.Color, "BasedOn Button derived background color");
        AssertEqual(Colors.White, foreground.Color, "BasedOn Button derived foreground color");
    }

    private static void ValidateStyleTriggersAndEventSetter(
        MainWindow window,
        MainViewModel viewModel,
        TextBlock triggerText,
        Style triggerStyle,
        TextBlock multiTriggerText,
        Style multiTriggerStyle,
        TextBlock multiDataTriggerText,
        Style multiDataTriggerStyle,
        Button eventSetterButton,
        Style eventSetterStyle,
        TextBlock eventSetterStatus)
    {
        AssertEqual(triggerStyle, triggerText.Style, "style trigger TextBlock style");
        AssertEqual(2, triggerStyle.Setters.Count, "style trigger setter count");
        AssertEqual(2, triggerStyle.Triggers.Count, "style trigger count");
        var baseTextStyle = Require<Style>(
            Application.Current?.TryFindResource(typeof(TextBlock)),
            "implicit TextBlock style");
        AssertEqual(baseTextStyle, triggerStyle.BasedOn, "style trigger BasedOn TextBlock style");

        var propertyTrigger = Require<Trigger>(
            triggerStyle.Triggers[0],
            "property style Trigger");
        AssertEqual(FrameworkElement.TagProperty, propertyTrigger.Property, "property style Trigger property");
        AssertEqual("Active", propertyTrigger.Value, "property style Trigger value");

        var dataTrigger = Require<DataTrigger>(
            triggerStyle.Triggers[1],
            "data style Trigger");
        AssertEqual("ActionsEnabled", GetBindingPath(dataTrigger.Binding), "data style Trigger binding path");
        AssertEqual("False", dataTrigger.Value?.ToString(), "data style Trigger value");

        AssertEqual(multiTriggerStyle, multiTriggerText.Style, "MultiTrigger TextBlock style");
        AssertEqual(2, multiTriggerStyle.Setters.Count, "MultiTrigger style setter count");
        AssertEqual(1, multiTriggerStyle.Triggers.Count, "MultiTrigger style trigger count");
        var multiTrigger = Require<MultiTrigger>(
            multiTriggerStyle.Triggers[0],
            "MultiTrigger style trigger");
        AssertEqual(2, multiTrigger.Conditions.Count, "MultiTrigger condition count");
        AssertEqual(FrameworkElement.TagProperty, multiTrigger.Conditions[0].Property, "MultiTrigger first property");
        AssertEqual("Ready", multiTrigger.Conditions[0].Value, "MultiTrigger first value");
        AssertEqual(UIElement.IsEnabledProperty, multiTrigger.Conditions[1].Property, "MultiTrigger second property");
        AssertEqual("True", multiTrigger.Conditions[1].Value?.ToString(), "MultiTrigger second value");

        AssertEqual(multiDataTriggerStyle, multiDataTriggerText.Style, "MultiDataTrigger TextBlock style");
        AssertEqual(2, multiDataTriggerStyle.Setters.Count, "MultiDataTrigger style setter count");
        AssertEqual(1, multiDataTriggerStyle.Triggers.Count, "MultiDataTrigger style trigger count");
        var multiDataTrigger = Require<MultiDataTrigger>(
            multiDataTriggerStyle.Triggers[0],
            "MultiDataTrigger style trigger");
        AssertEqual(2, multiDataTrigger.Conditions.Count, "MultiDataTrigger condition count");
        AssertEqual("ActionsEnabled", GetBindingPath(multiDataTrigger.Conditions[0].Binding), "MultiDataTrigger first binding");
        AssertEqual("False", multiDataTrigger.Conditions[0].Value?.ToString(), "MultiDataTrigger first value");
        AssertEqual("SelectedCategory", GetBindingPath(multiDataTrigger.Conditions[1].Binding), "MultiDataTrigger second binding");
        AssertEqual("Input", multiDataTrigger.Conditions[1].Value, "MultiDataTrigger second value");

        DrainDispatcher(window);
        AssertEqual("style trigger inactive", triggerText.Text, "style trigger initial text");
        AssertEqual(
            Color.FromRgb(0x5B, 0x64, 0x72),
            Require<SolidColorBrush>(triggerText.Foreground, "style trigger initial foreground").Color,
            "style trigger initial foreground");
        AssertEqual("multi trigger inactive", multiTriggerText.Text, "MultiTrigger initial text");
        AssertEqual("multi data trigger inactive", multiDataTriggerText.Text, "MultiDataTrigger initial text");

        triggerText.Tag = "Active";
        DrainDispatcher(window);
        AssertEqual("property trigger active", triggerText.Text, "property style Trigger active text");
        AssertEqual(
            Color.FromRgb(0x24, 0x6B, 0xFE),
            Require<SolidColorBrush>(triggerText.Foreground, "property style Trigger foreground").Color,
            "property style Trigger foreground");

        multiTriggerText.Tag = "Ready";
        DrainDispatcher(window);
        AssertEqual("multi trigger active", multiTriggerText.Text, "MultiTrigger active text");
        AssertEqual(
            Color.FromRgb(0x23, 0x6B, 0x46),
            Require<SolidColorBrush>(multiTriggerText.Foreground, "MultiTrigger active foreground").Color,
            "MultiTrigger active foreground");

        multiTriggerText.IsEnabled = false;
        DrainDispatcher(window);
        AssertEqual("multi trigger inactive", multiTriggerText.Text, "MultiTrigger disabled condition text");
        multiTriggerText.IsEnabled = true;
        multiTriggerText.Tag = null;
        DrainDispatcher(window);
        AssertEqual("multi trigger inactive", multiTriggerText.Text, "MultiTrigger restored text");

        viewModel.ActionsEnabled = false;
        DrainDispatcher(window);
        AssertEqual("data trigger disabled", triggerText.Text, "data style Trigger disabled text");
        AssertEqual(
            Color.FromRgb(0xB4, 0x23, 0x18),
            Require<SolidColorBrush>(triggerText.Foreground, "data style Trigger foreground").Color,
            "data style Trigger foreground");

        viewModel.ActionsEnabled = true;
        DrainDispatcher(window);
        AssertEqual("property trigger active", triggerText.Text, "restored property style Trigger text");

        viewModel.SelectedCategory = "Input";
        viewModel.ActionsEnabled = false;
        DrainDispatcher(window);
        AssertEqual("multi data trigger active", multiDataTriggerText.Text, "MultiDataTrigger active text");
        AssertEqual(
            Color.FromRgb(0xB4, 0x23, 0x18),
            Require<SolidColorBrush>(multiDataTriggerText.Foreground, "MultiDataTrigger active foreground").Color,
            "MultiDataTrigger active foreground");

        viewModel.ActionsEnabled = true;
        viewModel.SelectedCategory = "Framework";
        DrainDispatcher(window);
        AssertEqual("multi data trigger inactive", multiDataTriggerText.Text, "MultiDataTrigger restored text");
        triggerText.Tag = null;
        DrainDispatcher(window);
        AssertEqual("style trigger inactive", triggerText.Text, "restored style trigger inactive text");

        AssertEqual(eventSetterStyle, eventSetterButton.Style, "EventSetter Button style");
        AssertEqual("EventSetter action", eventSetterButton.Content, "EventSetter Button content");
        AssertEqual("EventSetterStyle", eventSetterButton.Tag, "EventSetter Button setter Tag");
        AssertEqual(2, eventSetterStyle.Setters.Count, "EventSetter style setter count");
        var eventSetter = Require<EventSetter>(
            eventSetterStyle.Setters[1],
            "Button Click EventSetter");
        AssertEqual(ButtonBase.ClickEvent, eventSetter.Event, "EventSetter routed event");
        AssertEqual("EventSetter idle", eventSetterStatus.Text, "EventSetter initial status");
        AssertEqual(0, window.ShowcaseStyleEventSetterClickCount, "EventSetter initial click count");

        var clickArgs = new RoutedEventArgs(ButtonBase.ClickEvent, eventSetterButton);
        eventSetterButton.RaiseEvent(clickArgs);
        DrainDispatcher(window);
        AssertEqual(true, clickArgs.Handled, "EventSetter handled flag");
        AssertEqual(1, window.ShowcaseStyleEventSetterClickCount, "EventSetter click count");
        AssertEqual("EventSetterStyleButton", window.LastShowcaseStyleEventSetterSenderName, "EventSetter sender name");
        AssertEqual("Click", window.LastShowcaseStyleEventSetterRoutedEventName, "EventSetter routed event name");
        AssertEqual("EventSetter clicked", eventSetterStatus.Text, "EventSetter updated status");
    }

    private static void ValidateLocalThemeResources(
        Window window,
        StackPanel scope,
        TextBlock textBlock)
    {
        var appTextStyle = Require<Style>(
            Application.Current?.TryFindResource(typeof(TextBlock)),
            "application implicit TextBlock style");
        var localTextStyle = Require<Style>(
            scope.TryFindResource(typeof(TextBlock)),
            "local implicit TextBlock style");
        AssertEqual(appTextStyle, localTextStyle.BasedOn, "local implicit TextBlock BasedOn style");
        AssertEqual(localTextStyle, textBlock.Style, "local implicit TextBlock applied style");
        AssertEqual("Local implicit style", textBlock.Text, "local implicit TextBlock text");
        AssertEqual("LocalThemeScopeText", textBlock.Tag, "local implicit TextBlock Tag setter");

        var initialBrush = Require<SolidColorBrush>(
            textBlock.Foreground,
            "local implicit TextBlock initial foreground");
        AssertEqual(Color.FromRgb(0x7C, 0x2D, 0x12), initialBrush.Color, "local implicit TextBlock initial foreground color");

        scope.Resources["LocalThemeBrush"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x76, 0x6E));
        DrainDispatcher(window);
        var updatedBrush = Require<SolidColorBrush>(
            textBlock.Foreground,
            "local implicit TextBlock updated foreground");
        AssertEqual(Color.FromRgb(0x0F, 0x76, 0x6E), updatedBrush.Color, "local implicit TextBlock updated foreground color");

        scope.Resources["LocalThemeBrush"] = new SolidColorBrush(Color.FromRgb(0x7C, 0x2D, 0x12));
        DrainDispatcher(window);
        var restoredBrush = Require<SolidColorBrush>(
            textBlock.Foreground,
            "local implicit TextBlock restored foreground");
        AssertEqual(Color.FromRgb(0x7C, 0x2D, 0x12), restoredBrush.Color, "local implicit TextBlock restored foreground color");
    }

    private static void ValidateTemplateButton(Window window, Button button, Style style)
    {
        AssertEqual(style, button.Style, "template Button style");
        AssertEqual("Templated action", button.Content, "template Button content");

        button.ApplyTemplate();
        DrainDispatcher(window);
        var template = Require<ControlTemplate>(button.Template, "template Button ControlTemplate");
        var border = Require<Border>(
            template.FindName("TemplateBorder", button),
            "template Button border part");
        var contentPresenter = Require<ContentPresenter>(
            template.FindName("TemplateContentPresenter", button),
            "template Button content presenter part");

        AssertEqual(typeof(Button), template.TargetType, "template Button target type");
        AssertEqual(button.Background, border.Background, "template Button background TemplateBinding");
        AssertEqual("Templated action", contentPresenter.Content, "template Button content TemplateBinding");
        AssertEqual(1.0, border.Opacity, "template Button enabled opacity");
        AssertEqual(1.0, contentPresenter.Opacity, "template Button Normal visual state opacity");

        var visualStateGroups = VisualStateManager.GetVisualStateGroups(border);
        AssertEqual(1, visualStateGroups.Count, "template Button VisualStateGroup count");
        var commonStates = Require<VisualStateGroup>(
            visualStateGroups[0],
            "template Button CommonStates group");
        AssertEqual("CommonStates", commonStates.Name, "template Button VisualStateGroup name");
        AssertEqual(2, commonStates.States.Count, "template Button VisualState count");
        var normalState = Require<VisualState>(
            commonStates.States[0],
            "template Button Normal VisualState");
        var pressedState = Require<VisualState>(
            commonStates.States[1],
            "template Button Pressed VisualState");
        AssertEqual("Normal", normalState.Name, "template Button Normal VisualState name");
        AssertEqual("Pressed", pressedState.Name, "template Button Pressed VisualState name");
        AssertEqual(1, pressedState.Storyboard?.Children.Count ?? 0, "template Button Pressed storyboard child count");
        var pressedAnimation = Require<DoubleAnimation>(
            pressedState.Storyboard?.Children[0],
            "template Button Pressed DoubleAnimation");
        AssertEqual(
            "TemplateContentPresenter",
            Storyboard.GetTargetName(pressedAnimation),
            "template Button Pressed animation target");
        AssertEqual(
            "Opacity",
            Storyboard.GetTargetProperty(pressedAnimation).Path,
            "template Button Pressed animation property");
        AssertEqual(0.72, pressedAnimation.To, "template Button Pressed animation target opacity");
        AssertEqual(TimeSpan.Zero, pressedAnimation.Duration.TimeSpan, "template Button Pressed animation duration");

        button.IsEnabled = false;
        DrainDispatcher(window);
        AssertEqual(0.45, border.Opacity, "template Button disabled trigger opacity");
        button.IsEnabled = true;
        DrainDispatcher(window);
        AssertEqual(1.0, border.Opacity, "template Button restored opacity");
    }

    private static void ValidateValidation(
        Window window,
        MainViewModel viewModel,
        TextBox textBox,
        TextBlock echoText)
    {
        var binding = Require<Binding>(
            BindingOperations.GetBinding(textBox, TextBox.TextProperty),
            "validation TextBox binding");

        AssertEqual("ValidationText", binding.Path.Path, "validation binding path");
        AssertEqual(BindingMode.TwoWay, binding.Mode, "validation binding mode");
        AssertEqual(UpdateSourceTrigger.Explicit, binding.UpdateSourceTrigger, "validation update trigger");
        AssertEqual(true, binding.NotifyOnValidationError, "validation notification flag");
        AssertEqual(1, binding.ValidationRules.Count, "validation rule count");
        Require<ShowcaseNonEmptyValidationRule>(
            binding.ValidationRules[0],
            "Showcase non-empty validation rule");

        var errorTemplate = Require<ControlTemplate>(
            Validation.GetErrorTemplate(textBox),
            "validation error template");
        AssertEqual(
            window.FindResource("ShowcaseValidationErrorTemplate"),
            errorTemplate,
            "validation error template resource");

        DrainDispatcher(window);
        AssertEqual("valid: ready", textBox.Text, "initial validation TextBox text");
        AssertEqual("Current: valid: ready", echoText.Text, "initial validation echo text");

        var bindingExpression = Require<BindingExpression>(
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty),
            "validation TextBox binding expression");
        textBox.Text = "invalid";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual("valid: ready", viewModel.ValidationText, "invalid validation leaves source unchanged");
        AssertEqual(true, Validation.GetHasError(textBox), "invalid validation error flag");
        AssertEqual(1, Validation.GetErrors(textBox).Count, "invalid validation error count");
        AssertEqual(
            "Value must start with valid:",
            Validation.GetErrors(textBox)[0].ErrorContent,
            "invalid validation error content");

        textBox.Text = "valid: updated";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual(false, Validation.GetHasError(textBox), "valid validation clears error flag");
        AssertEqual("valid: updated", viewModel.ValidationText, "valid validation updates source");
        AssertEqual("Current: valid: updated", echoText.Text, "updated validation echo text");
    }

    private static void ValidateDataErrorValidation(
        Window window,
        MainViewModel viewModel,
        TextBox textBox,
        TextBlock echoText)
    {
        var binding = Require<Binding>(
            BindingOperations.GetBinding(textBox, TextBox.TextProperty),
            "IDataErrorInfo TextBox binding");

        AssertEqual("DataErrorText", binding.Path.Path, "IDataErrorInfo binding path");
        AssertEqual(BindingMode.TwoWay, binding.Mode, "IDataErrorInfo binding mode");
        AssertEqual(UpdateSourceTrigger.Explicit, binding.UpdateSourceTrigger, "IDataErrorInfo update trigger");
        AssertEqual(true, binding.NotifyOnValidationError, "IDataErrorInfo notification flag");
        AssertEqual(true, binding.ValidatesOnDataErrors, "IDataErrorInfo validation flag");

        DrainDispatcher(window);
        AssertEqual("data: ready", textBox.Text, "initial IDataErrorInfo TextBox text");
        AssertEqual("Data: data: ready", echoText.Text, "initial IDataErrorInfo echo text");

        var bindingExpression = Require<BindingExpression>(
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty),
            "IDataErrorInfo TextBox binding expression");
        textBox.Text = "broken";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual("broken", viewModel.DataErrorText, "invalid IDataErrorInfo source update");
        AssertEqual(true, Validation.GetHasError(textBox), "invalid IDataErrorInfo error flag");
        AssertEqual(
            "Data value must start with data:",
            GetSingleValidationErrorContent(textBox, "invalid IDataErrorInfo error"),
            "invalid IDataErrorInfo error content");
        AssertEqual("Data: broken", echoText.Text, "invalid IDataErrorInfo echo text");

        textBox.Text = "data: updated";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual(false, Validation.GetHasError(textBox), "valid IDataErrorInfo clears error flag");
        AssertEqual("data: updated", viewModel.DataErrorText, "valid IDataErrorInfo updates source");
        AssertEqual("Data: data: updated", echoText.Text, "updated IDataErrorInfo echo text");
    }

    private static void ValidateNotifyDataErrorValidation(
        Window window,
        MainViewModel viewModel,
        TextBox textBox,
        TextBlock echoText)
    {
        var binding = Require<Binding>(
            BindingOperations.GetBinding(textBox, TextBox.TextProperty),
            "INotifyDataErrorInfo TextBox binding");

        AssertEqual("NotifyDataErrorText", binding.Path.Path, "INotifyDataErrorInfo binding path");
        AssertEqual(BindingMode.TwoWay, binding.Mode, "INotifyDataErrorInfo binding mode");
        AssertEqual(UpdateSourceTrigger.Explicit, binding.UpdateSourceTrigger, "INotifyDataErrorInfo update trigger");
        AssertEqual(true, binding.NotifyOnValidationError, "INotifyDataErrorInfo notification flag");
        AssertEqual(true, binding.ValidatesOnNotifyDataErrors, "INotifyDataErrorInfo validation flag");

        DrainDispatcher(window);
        AssertEqual("notify: ready", textBox.Text, "initial INotifyDataErrorInfo TextBox text");
        AssertEqual("Notify: notify: ready", echoText.Text, "initial INotifyDataErrorInfo echo text");
        AssertEqual(false, viewModel.HasErrors, "initial INotifyDataErrorInfo source error state");

        var bindingExpression = Require<BindingExpression>(
            BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty),
            "INotifyDataErrorInfo TextBox binding expression");
        textBox.Text = "broken";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual("broken", viewModel.NotifyDataErrorText, "invalid INotifyDataErrorInfo source update");
        AssertEqual(true, viewModel.HasErrors, "invalid INotifyDataErrorInfo source error state");
        AssertEqual(true, Validation.GetHasError(textBox), "invalid INotifyDataErrorInfo error flag");
        AssertEqual(
            "Notify value must start with notify:",
            GetSingleValidationErrorContent(textBox, "invalid INotifyDataErrorInfo error"),
            "invalid INotifyDataErrorInfo error content");
        AssertEqual("Notify: broken", echoText.Text, "invalid INotifyDataErrorInfo echo text");

        textBox.Text = "notify: updated";
        bindingExpression.UpdateSource();
        DrainDispatcher(window);
        AssertEqual(false, viewModel.HasErrors, "valid INotifyDataErrorInfo source error state");
        AssertEqual(false, Validation.GetHasError(textBox), "valid INotifyDataErrorInfo clears error flag");
        AssertEqual(
            "notify: updated",
            viewModel.NotifyDataErrorText,
            "valid INotifyDataErrorInfo updates source");
        AssertEqual("Notify: notify: updated", echoText.Text, "updated INotifyDataErrorInfo echo text");
    }

    private static void ValidateBindingGroup(
        Window window,
        MainViewModel viewModel,
        StackPanel panel,
        TextBox firstBox,
        TextBox lastBox,
        Button commitButton,
        TextBlock statusText,
        TextBlock firstEchoText,
        TextBlock lastEchoText)
    {
        var bindingGroup = Require<BindingGroup>(panel.BindingGroup, "Showcase BindingGroup");
        AssertEqual("ShowcaseBindingGroup", bindingGroup.Name, "BindingGroup name");
        AssertEqual(1, bindingGroup.Items.Count, "BindingGroup item count");
        AssertEqual(viewModel, bindingGroup.Items[0], "BindingGroup source item");
        AssertEqual(1, bindingGroup.ValidationRules.Count, "BindingGroup validation rule count");
        var rule = Require<ShowcaseBindingGroupValidationRule>(
            bindingGroup.ValidationRules[0],
            "Showcase BindingGroup validation rule");

        AssertEqual("BindingGroupFirstName", rule.FirstProperty, "BindingGroup first property");
        AssertEqual("BindingGroupLastName", rule.SecondProperty, "BindingGroup last property");
        AssertEqual("group:", rule.RequiredPrefix, "BindingGroup required prefix");
        AssertEqual("BindingGroupFirstName", GetTextBoxBindingPath(firstBox), "BindingGroup first binding path");
        AssertEqual("BindingGroupLastName", GetTextBoxBindingPath(lastBox), "BindingGroup last binding path");

        DrainDispatcher(window);
        AssertEqual("group: Ada", firstBox.Text, "BindingGroup first initial text");
        AssertEqual("group: Lovelace", lastBox.Text, "BindingGroup last initial text");
        AssertEqual("group: Ada", viewModel.BindingGroupFirstName, "BindingGroup first initial source");
        AssertEqual("group: Lovelace", viewModel.BindingGroupLastName, "BindingGroup last initial source");
        AssertEqual("Group ready", statusText.Text, "BindingGroup initial status text");
        AssertEqual("First: group: Ada", firstEchoText.Text, "BindingGroup first initial echo");
        AssertEqual("Last: group: Lovelace", lastEchoText.Text, "BindingGroup last initial echo");
        AssertEqual(false, Validation.GetHasError(panel), "BindingGroup initial error state");
        AssertEqual(true, bindingGroup.ValidateWithoutUpdate(), "BindingGroup initial validation");

        firstBox.Text = "Ada";
        commitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, commitButton));
        DrainDispatcher(window);
        AssertEqual("group: Ada", viewModel.BindingGroupFirstName, "BindingGroup rejected first source");
        AssertEqual("group: Lovelace", viewModel.BindingGroupLastName, "BindingGroup rejected last source");
        AssertEqual(true, Validation.GetHasError(panel), "BindingGroup rejected error state");
        AssertEqual(1, Validation.GetErrors(panel).Count, "BindingGroup rejected error count");
        AssertEqual(
            "Grouped values must start with 'group:'.",
            Validation.GetErrors(panel)[0].ErrorContent,
            "BindingGroup rejected error content");
        AssertEqual("Group has validation errors", statusText.Text, "BindingGroup rejected status");

        firstBox.Text = "group: Grace";
        lastBox.Text = "group: Hopper";
        commitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, commitButton));
        DrainDispatcher(window);
        AssertEqual("group: Grace", viewModel.BindingGroupFirstName, "BindingGroup accepted first source");
        AssertEqual("group: Hopper", viewModel.BindingGroupLastName, "BindingGroup accepted last source");
        AssertEqual(false, Validation.GetHasError(panel), "BindingGroup accepted error state");
        AssertEqual("Group committed", statusText.Text, "BindingGroup accepted status");
        AssertEqual("First: group: Grace", firstEchoText.Text, "BindingGroup first accepted echo");
        AssertEqual("Last: group: Hopper", lastEchoText.Text, "BindingGroup last accepted echo");
    }

    private static void ValidateAdornerLayer(
        Window window,
        AdornerDecorator decorator,
        Border target,
        TextBlock statusText)
    {
        var targetText = Require<TextBlock>(target.Child, "Showcase adorner target TextBlock");
        AssertEqual("Adorner target", targetText.Text, "Showcase adorner target text");
        AssertEqual("Adorner idle", statusText.Text, "Showcase adorner initial status");

        var layer = Require<AdornerLayer>(
            AdornerLayer.GetAdornerLayer(target),
            "Showcase AdornerLayer");
        AssertEqual(layer, decorator.AdornerLayer, "Showcase AdornerDecorator layer");
        AssertEqual(null, layer.GetAdorners(target), "Showcase initial adorner collection");

        var adorner = new ShowcaseStatusAdorner(target, "managed adorner active");
        layer.Add(adorner);
        DrainDispatcher(window);

        var adorners = layer.GetAdorners(target)
            ?? throw new InvalidOperationException("Expected Showcase target adorners after Add.");
        AssertEqual(1, adorners.Length, "Showcase adorner count after Add");
        AssertEqual(adorner, adorners[0], "Showcase attached adorner instance");
        AssertEqual(target, adorner.AdornedElement, "Showcase adorner adorned element");
        AssertEqual("managed adorner active", adorner.Status, "Showcase adorner status");
        statusText.Text = $"Adorners: {adorners.Length}";

        layer.Remove(adorner);
        DrainDispatcher(window);
        AssertEqual(null, layer.GetAdorners(target), "Showcase adorner collection after Remove");
        AssertEqual("Adorners: 1", statusText.Text, "Showcase adorner status after Add");
    }

    private static void ValidateStoryboards(
        Window window,
        TextBlock loadedText,
        Button clickButton,
        bool expectLoadedStoryboardApplied)
    {
        var loadedTrigger = Require<EventTrigger>(
            loadedText.Triggers[0],
            "loaded storyboard EventTrigger");
        AssertEqual(FrameworkElement.LoadedEvent, loadedTrigger.RoutedEvent, "loaded storyboard routed event");
        ValidateStoryboardAction(
            loadedTrigger,
            "LoadedStoryboardText",
            0.42,
            "loaded storyboard");

        var clickTrigger = Require<EventTrigger>(
            clickButton.Triggers[0],
            "click storyboard EventTrigger");
        AssertEqual(Button.ClickEvent, clickTrigger.RoutedEvent, "click storyboard routed event");
        ValidateStoryboardAction(
            clickTrigger,
            "ClickStoryboardButton",
            0.58,
            "click storyboard");

        AssertClose(
            expectLoadedStoryboardApplied ? 0.42 : 1.0,
            loadedText.Opacity,
            0.0001,
            expectLoadedStoryboardApplied
                ? "loaded storyboard applied opacity"
                : "loaded storyboard initial opacity");
        AssertEqual(1.0, clickButton.Opacity, "click storyboard initial opacity");
    }

    private static void ValidateNativeEffects(Border dropShadowEffectBorder, Border blurEffectBorder)
    {
        var dropShadowEffect = Require<DropShadowEffect>(
            dropShadowEffectBorder.Effect,
            "Showcase DropShadowEffect");
        AssertEqual(9.0, dropShadowEffect.BlurRadius, "DropShadowEffect BlurRadius");
        AssertEqual(Color.FromRgb(0x33, 0x41, 0x55), dropShadowEffect.Color, "DropShadowEffect Color");
        AssertEqual(315.0, dropShadowEffect.Direction, "DropShadowEffect Direction");
        AssertEqual(0.55, dropShadowEffect.Opacity, "DropShadowEffect Opacity");
        AssertEqual(RenderingBias.Quality, dropShadowEffect.RenderingBias, "DropShadowEffect RenderingBias");
        AssertEqual(4.0, dropShadowEffect.ShadowDepth, "DropShadowEffect ShadowDepth");

        var blurEffect = Require<BlurEffect>(
            blurEffectBorder.Effect,
            "Showcase BlurEffect");
        AssertEqual(KernelType.Gaussian, blurEffect.KernelType, "BlurEffect KernelType");
        AssertEqual(2.5, blurEffect.Radius, "BlurEffect Radius");
        AssertEqual(RenderingBias.Quality, blurEffect.RenderingBias, "BlurEffect RenderingBias");
    }

    private static void ValidateShowcaseRoutedEvent(
        MainWindow window,
        StackPanel scope,
        ShowcaseRoutedEventButton button,
        TextBlock statusText)
    {
        AssertEqual(RoutingStrategy.Bubble, ShowcaseRoutedEventButton.ShowcaseActivatedEvent.RoutingStrategy, "Showcase routed event strategy");
        AssertEqual(nameof(ShowcaseRoutedEventButton.ShowcaseActivated), ShowcaseRoutedEventButton.ShowcaseActivatedEvent.Name, "Showcase routed event name");
        AssertEqual(typeof(ShowcaseRoutedEventHandler), ShowcaseRoutedEventButton.ShowcaseActivatedEvent.HandlerType, "Showcase routed event handler type");
        AssertEqual(typeof(ShowcaseRoutedEventButton), ShowcaseRoutedEventButton.ShowcaseActivatedEvent.OwnerType, "Showcase routed event owner type");
        AssertEqual("Routed event idle", statusText.Text, "Showcase routed event initial status");
        AssertEqual(0, button.ClassHandlerCount, "Showcase routed event initial class-handler count");
        AssertEqual(0, window.ShowcaseRoutedEventSourceCount, "Showcase routed event initial source count");
        AssertEqual(0, window.ShowcaseRoutedEventScopeCount, "Showcase routed event initial scope count");
        AssertEqual(0, window.ShowcaseRoutedEventHandledTooCount, "Showcase routed event initial handled-too count");

        var args = button.RaiseShowcaseActivated("showcase routed payload");
        DrainDispatcher(window);

        AssertEqual(true, args.Handled, "Showcase routed event handled flag");
        AssertEqual(1, button.ClassHandlerCount, "Showcase routed event class-handler count");
        AssertEqual(1, window.ShowcaseRoutedEventSourceCount, "Showcase routed event source handler count");
        AssertEqual(1, window.ShowcaseRoutedEventScopeCount, "Showcase routed event scope handler count");
        AssertEqual(1, window.ShowcaseRoutedEventHandledTooCount, "Showcase routed event handled-too handler count");
        AssertEqual("ShowcaseActivated", window.LastShowcaseRoutedEventName, "Showcase routed event last name");
        AssertEqual("showcase routed payload", window.LastShowcaseRoutedEventPayload, "Showcase routed event payload");
        AssertEqual(scope.Name, window.LastShowcaseRoutedEventSenderName, "Showcase routed event sender name");
        AssertEqual(button.Name, window.LastShowcaseRoutedEventOriginalSourceName, "Showcase routed event original source name");
        AssertEqual("Handled showcase routed payload", statusText.Text, "Showcase routed event status text");
    }

    private static void ValidateStoryboardAction(
        EventTrigger trigger,
        string targetName,
        double targetOpacity,
        string description)
    {
        AssertEqual(1, trigger.Actions.Count, $"{description} action count");
        var beginStoryboard = Require<BeginStoryboard>(
            trigger.Actions[0],
            $"{description} BeginStoryboard");
        var storyboard = Require<Storyboard>(
            beginStoryboard.Storyboard,
            $"{description} Storyboard");
        AssertEqual(1, storyboard.Children.Count, $"{description} animation count");
        var animation = Require<DoubleAnimation>(
            storyboard.Children[0],
            $"{description} DoubleAnimation");

        AssertEqual(targetName, Storyboard.GetTargetName(animation), $"{description} target name");
        AssertEqual("Opacity", Storyboard.GetTargetProperty(animation).Path, $"{description} target property");
        AssertEqual(TimeSpan.Zero, animation.Duration.TimeSpan, $"{description} duration");
        AssertEqual(targetOpacity, animation.To ?? double.NaN, $"{description} target value");
    }

    private static string GetTextBindingPath(TextBlock textBlock)
    {
        return BindingOperations.GetBinding(textBlock, TextBlock.TextProperty)?.Path.Path
            ?? throw new InvalidOperationException($"Expected {textBlock.Name} text to have a Binding.");
    }

    private static string GetTextBoxBindingPath(TextBox textBox)
    {
        return BindingOperations.GetBinding(textBox, TextBox.TextProperty)?.Path.Path
            ?? throw new InvalidOperationException($"Expected {textBox.Name} text to have a Binding.");
    }

    private static string GetBindingPath(DependencyObject target, DependencyProperty property)
    {
        return BindingOperations.GetBinding(target, property)?.Path.Path
            ?? throw new InvalidOperationException($"Expected {property.Name} to have a Binding.");
    }

    private static string GetSelectedDateBindingPath(Control control)
    {
        DependencyProperty property = control switch
        {
            WpfCalendar => WpfCalendar.SelectedDateProperty,
            DatePicker => DatePicker.SelectedDateProperty,
            _ => throw new InvalidOperationException("Unsupported selected-date control.")
        };

        return BindingOperations.GetBinding(control, property)?.Path.Path
            ?? throw new InvalidOperationException($"Expected {control.Name} SelectedDate to have a Binding.");
    }

    private static string GetTemplateItemsSourcePath(HierarchicalDataTemplate template)
    {
        return template.ItemsSource is Binding binding
            ? binding.Path.Path
            : throw new InvalidOperationException("Expected hierarchical data template to bind ItemsSource.");
    }

    private static void ValidateNavigation(
        Window window,
        Frame frame,
        Button detailsButton,
        Button backButton,
        Button forwardButton)
    {
        DrainDispatcher(window);
        var overviewPage = Require<OverviewPage>(frame.Content, "initial overview page");
        var overviewTitle = Require<TextBlock>(
            overviewPage.FindName("OverviewTitle"),
            "overview page title");
        AssertEqual("SDK overview page", overviewTitle.Text, "overview page title text");

        detailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, detailsButton));
        DrainDispatcher(window);
        var detailsPage = Require<DetailsPage>(frame.Content, "navigated details page");
        var detailsTitle = Require<TextBlock>(
            detailsPage.FindName("DetailsTitle"),
            "details page title");
        var detailsList = Require<ListBox>(
            detailsPage.FindName("DetailsList"),
            "details page list");
        AssertEqual("SDK details page", detailsTitle.Text, "details page title text");
        AssertEqual(3, detailsList.Items.Count, "details page list item count");
        AssertEqual(new Uri("DetailsPage.xaml", UriKind.Relative), frame.Source, "navigation frame source");
        AssertEqual(true, frame.CanGoBack, "navigation frame back stack state");
        AssertEqual(false, frame.CanGoForward, "navigation frame forward stack before journal back");
        AssertEqual(NavigationCommands.BrowseBack, backButton.Command, "navigation back command");
        AssertEqual(frame, backButton.CommandTarget, "navigation back command target");
        AssertEqual(NavigationCommands.BrowseForward, forwardButton.Command, "navigation forward command");
        AssertEqual(frame, forwardButton.CommandTarget, "navigation forward command target");

        NavigationCommands.BrowseBack.Execute(null, frame);
        DrainDispatcher(window);
        var journalOverviewPage = Require<OverviewPage>(frame.Content, "journal overview page");
        var journalOverviewTitle = Require<TextBlock>(
            journalOverviewPage.FindName("OverviewTitle"),
            "journal overview page title");
        AssertEqual("SDK overview page", journalOverviewTitle.Text, "journal overview page title text");
        AssertEqual(true, frame.CanGoForward, "navigation frame forward stack after journal back");

        NavigationCommands.BrowseForward.Execute(null, frame);
        DrainDispatcher(window);
        var journalDetailsPage = Require<DetailsPage>(frame.Content, "journal details page");
        var journalDetailsTitle = Require<TextBlock>(
            journalDetailsPage.FindName("DetailsTitle"),
            "journal details page title");
        AssertEqual("SDK details page", journalDetailsTitle.Text, "journal details page title text");
        AssertEqual(true, frame.CanGoBack, "navigation frame back stack after journal forward");
    }

    private static void ValidateApplicationLoadComponent()
    {
        var component = Application.LoadComponent(
            new Uri("/ProGPU.Wpf.ShowcaseApp;component/OverviewPage.xaml", UriKind.Relative));
        var overviewPage = Require<OverviewPage>(component, "Application.LoadComponent overview page");
        var overviewTitle = Require<TextBlock>(
            overviewPage.FindName("OverviewTitle"),
            "Application.LoadComponent overview title");
        AssertEqual("SDK overview page", overviewTitle.Text, "Application.LoadComponent overview title text");
    }

    private static void ValidateLooseXamlReaderWriter()
    {
        const string looseXaml = """
            <StackPanel
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Name="LooseXamlRoot"
                Orientation="Horizontal"
                Tag="loose-xaml">
                <TextBlock
                    x:Name="LooseXamlText"
                    Text="Loose XAML text" />
                <Button
                    x:Name="LooseXamlButton"
                    MinWidth="96"
                    Content="Loose action" />
            </StackPanel>
            """;

        var root = Require<StackPanel>(XamlReader.Parse(looseXaml), "loose XamlReader StackPanel");
        AssertEqual("LooseXamlRoot", root.Name, "loose XamlReader root name");
        AssertEqual(Orientation.Horizontal, root.Orientation, "loose XamlReader root orientation");
        AssertEqual("loose-xaml", root.Tag, "loose XamlReader root tag");
        AssertEqual(2, root.Children.Count, "loose XamlReader child count");

        var text = Require<TextBlock>(root.Children[0], "loose XamlReader TextBlock");
        var button = Require<Button>(root.Children[1], "loose XamlReader Button");
        AssertEqual("LooseXamlText", text.Name, "loose XamlReader TextBlock name");
        AssertEqual("Loose XAML text", text.Text, "loose XamlReader TextBlock text");
        AssertEqual("LooseXamlButton", button.Name, "loose XamlReader Button name");
        AssertEqual("Loose action", button.Content, "loose XamlReader Button content");
        AssertEqual(96.0, button.MinWidth, "loose XamlReader Button MinWidth");

        string serialized = XamlWriter.Save(root);
        AssertContains("LooseXamlRoot", serialized, "loose XamlWriter serialized root name");
        AssertContains("LooseXamlButton", serialized, "loose XamlWriter serialized Button name");

        var roundTripped = Require<StackPanel>(
            XamlReader.Parse(serialized),
            "loose XamlWriter round-trip StackPanel");
        AssertEqual("LooseXamlRoot", roundTripped.Name, "loose XamlWriter round-trip root name");
        AssertEqual(Orientation.Horizontal, roundTripped.Orientation, "loose XamlWriter round-trip orientation");
        AssertEqual(2, roundTripped.Children.Count, "loose XamlWriter round-trip child count");
        var roundTrippedButton = Require<Button>(
            roundTripped.Children[1],
            "loose XamlWriter round-trip Button");
        AssertEqual("Loose action", roundTrippedButton.Content, "loose XamlWriter round-trip Button content");
        AssertEqual(96.0, roundTrippedButton.MinWidth, "loose XamlWriter round-trip Button MinWidth");
    }

    private static void ValidateDispatcherOperations(Window window)
    {
        AssertEqual(true, window.Dispatcher.CheckAccess(), "dispatcher CheckAccess on validation thread");
        string invokeResult = window.Dispatcher.Invoke(
            static () => "dispatcher invoke result",
            DispatcherPriority.Send);
        AssertEqual("dispatcher invoke result", invokeResult, "dispatcher Invoke result");

        int beginInvokeCount = 0;
        var operation = window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => beginInvokeCount++));
        DrainDispatcher(window);
        AssertEqual(1, beginInvokeCount, "dispatcher BeginInvoke execution count");
        AssertEqual(DispatcherOperationStatus.Completed, operation.Status, "dispatcher BeginInvoke status");

        ValidateDispatcherTimer(window);
        ValidateDispatcherSynchronizationContext(window);
        ValidateDispatcherAsyncContinuation(window);
        ValidateDispatcherInvokeAsync(window);
        ValidateDispatcherUnhandledException(window);
    }

    private static void ValidateDispatcherTimer(Window window)
    {
        int tickCount = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Background, window.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(1)
        };
        timer.Tick += (_, _) =>
        {
            tickCount++;
            timer.Stop();
        };

        AssertEqual(false, timer.IsEnabled, "dispatcher timer initial IsEnabled state");
        timer.Start();
        AssertEqual(true, timer.IsEnabled, "dispatcher timer started IsEnabled state");
        PumpDispatcherUntil(window, () => tickCount > 0, TimeSpan.FromSeconds(1), "dispatcher timer tick");
        AssertEqual(1, tickCount, "dispatcher timer tick count");
        AssertEqual(false, timer.IsEnabled, "dispatcher timer stopped IsEnabled state");
    }

    private static void ValidateDispatcherSynchronizationContext(Window window)
    {
        SynchronizationContext? capturedContext = null;
        window.Dispatcher.Invoke(
            () => capturedContext = SynchronizationContext.Current,
            DispatcherPriority.Background);
        AssertEqual(
            typeof(DispatcherSynchronizationContext),
            capturedContext is DispatcherSynchronizationContext
                ? typeof(DispatcherSynchronizationContext)
                : null,
            "dispatcher synchronization context type");

        var context = new DispatcherSynchronizationContext(window.Dispatcher, DispatcherPriority.Background);
        var postedHasAccess = false;
        context.Post(
            _ => postedHasAccess = window.Dispatcher.CheckAccess(),
            state: null);
        DrainDispatcher(window);
        AssertEqual(true, postedHasAccess, "dispatcher synchronization context Post access");

        var sendHasAccess = false;
        context.Send(
            _ => sendHasAccess = window.Dispatcher.CheckAccess(),
            state: null);
        AssertEqual(true, sendHasAccess, "dispatcher synchronization context Send access");

        var copy = context.CreateCopy();
        AssertEqual(
            typeof(DispatcherSynchronizationContext),
            copy is DispatcherSynchronizationContext
                ? typeof(DispatcherSynchronizationContext)
                : null,
            "dispatcher synchronization context copy type");

        var copyPostHasAccess = false;
        copy.Post(
            _ => copyPostHasAccess = window.Dispatcher.CheckAccess(),
            state: null);
        DrainDispatcher(window);
        AssertEqual(true, copyPostHasAccess, "dispatcher synchronization context copy Post access");
    }

    private static void ValidateDispatcherAsyncContinuation(Window window)
    {
        Task? continuationTask = null;
        var continuationHasAccess = false;
        window.Dispatcher.Invoke(
            () => continuationTask = CaptureDispatcherContinuationAsync(
                window,
                hasAccess => continuationHasAccess = hasAccess),
            DispatcherPriority.Background);

        PumpDispatcherUntil(
            window,
            () => continuationTask?.IsCompleted == true,
            TimeSpan.FromSeconds(1),
            "dispatcher async continuation");
        continuationTask?.GetAwaiter().GetResult();
        AssertEqual(true, continuationHasAccess, "dispatcher async continuation access");
    }

    private static async Task CaptureDispatcherContinuationAsync(Window window, Action<bool> complete)
    {
        await Task.Yield();
        complete(window.Dispatcher.CheckAccess());
    }

    private static void ValidateDispatcherInvokeAsync(Window window)
    {
        DispatcherOperation<string> resultOperation = window.Dispatcher.InvokeAsync(
            () =>
            {
                AssertEqual(true, window.Dispatcher.CheckAccess(), "dispatcher InvokeAsync callback access");
                return "dispatcher invoke async result";
            },
            DispatcherPriority.Background);
        PumpDispatcherUntil(
            window,
            () => resultOperation.Status == DispatcherOperationStatus.Completed,
            TimeSpan.FromSeconds(1),
            "dispatcher InvokeAsync operation");
        AssertEqual(DispatcherOperationStatus.Completed, resultOperation.Status, "dispatcher InvokeAsync status");
        AssertEqual("dispatcher invoke async result", resultOperation.Result, "dispatcher InvokeAsync result");

        DispatcherOperation actionOperation = window.Dispatcher.InvokeAsync(
            () => AssertEqual(true, window.Dispatcher.CheckAccess(), "dispatcher InvokeAsync action access"),
            DispatcherPriority.Background);
        PumpDispatcherUntil(
            window,
            () => actionOperation.Status == DispatcherOperationStatus.Completed,
            TimeSpan.FromSeconds(1),
            "dispatcher InvokeAsync action operation");
        AssertEqual(DispatcherOperationStatus.Completed, actionOperation.Status, "dispatcher InvokeAsync action status");
    }

    private static void ValidateDispatcherUnhandledException(Window window)
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("Expected current application for dispatcher unhandled exception validation.");
        int exceptionCount = 0;
        object? eventSender = null;
        string? exceptionMessage = null;
        bool initialHandledState = true;
        DispatcherUnhandledExceptionEventHandler handler = (sender, e) =>
        {
            exceptionCount++;
            eventSender = sender;
            exceptionMessage = e.Exception.Message;
            initialHandledState = e.Handled;
            e.Handled = true;
        };

        application.DispatcherUnhandledException += handler;
        try
        {
            window.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => throw new InvalidOperationException("Showcase handled dispatcher exception")));
            PumpDispatcherUntil(
                window,
                () => exceptionCount > 0,
                TimeSpan.FromSeconds(1),
                "dispatcher unhandled exception event");
        }
        finally
        {
            application.DispatcherUnhandledException -= handler;
        }

        AssertEqual(1, exceptionCount, "dispatcher unhandled exception count");
        AssertEqual(window.Dispatcher, eventSender, "dispatcher unhandled exception sender");
        AssertEqual("Showcase handled dispatcher exception", exceptionMessage, "dispatcher unhandled exception message");
        AssertEqual(false, initialHandledState, "dispatcher unhandled exception initial handled state");
        AssertEqual(application, Application.Current, "dispatcher unhandled exception application remains current");
    }

    private static void ValidateMessageBox(MainWindow window, Button button, TextBlock statusText)
    {
        AssertEqual("Show MessageBox", button.Content, "MessageBox button content");
        AssertEqual("MessageBox idle", statusText.Text, "MessageBox initial status");

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (!PortableWpfServiceRegistry.TryGetMessageBoxService(
                PortableWpfServiceKey.PresentationFramework,
                out var messageBoxService))
        {
            throw new InvalidOperationException("Expected PresentationFramework portable MessageBox service registration.");
        }

        AssertEqual(
            PortableWpfServiceKey.PresentationFramework,
            messageBoxService.ServiceKey,
            "Showcase portable MessageBox service key");

        int requestCount = 0;
        object? requestOwner = null;
        string? requestText = null;
        string? requestCaption = null;
        string? requestButton = null;
        string? requestIcon = null;
        string? requestDefaultResult = null;
        string? requestOptions = null;
        string? requestFallbackResult = null;
        IDisposable registration = messageBoxService.Register(
            request =>
            {
                requestCount++;
                requestOwner = request.Owner;
                requestText = request.MessageBoxText;
                requestCaption = request.Caption;
                requestButton = request.Button;
                requestIcon = request.Icon;
                requestDefaultResult = request.DefaultResult;
                requestOptions = request.Options;
                requestFallbackResult = request.FallbackResult;
                return requestFallbackResult;
            });
        try
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            DrainDispatcher(window);

            AssertEqual(1, window.MessageBoxShownCount, "Showcase MessageBox shown count");
            AssertEqual(MessageBoxResult.OK, window.LastMessageBoxResult, "Showcase MessageBox result");
            AssertEqual("MessageBox result: OK", statusText.Text, "Showcase MessageBox status text");
            AssertEqual(1, requestCount, "Showcase MessageBox request count");
            AssertEqual(window, requestOwner, "Showcase MessageBox request owner");
            AssertEqual("Portable MessageBox from the ProGPU WPF Showcase app.", requestText, "Showcase MessageBox request text");
            AssertEqual("ProGPU WPF Showcase", requestCaption, "Showcase MessageBox request caption");
            AssertEqual("OKCancel", requestButton, "Showcase MessageBox request button");
            AssertEqual("Asterisk", requestIcon, "Showcase MessageBox request icon");
            AssertEqual("OK", requestDefaultResult, "Showcase MessageBox request default result");
            AssertEqual("None", requestOptions, "Showcase MessageBox request options");
            AssertEqual("OK", requestFallbackResult, "Showcase MessageBox request fallback result");
        }
        finally
        {
            registration.Dispose();
        }
    }

    private static void ValidateFileDialogs(MainWindow window, Button button, TextBlock statusText)
    {
        AssertEqual("Run file dialogs", button.Content, "file dialog button content");
        AssertEqual("File dialogs idle", statusText.Text, "file dialog initial status");

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (!PortableWpfServiceRegistry.TryGetFileDialogService(
                PortableWpfServiceKey.PresentationFramework,
                out var fileDialogService))
        {
            throw new InvalidOperationException("Expected PresentationFramework portable file dialog service registration.");
        }

        AssertEqual(
            PortableWpfServiceKey.PresentationFramework,
            fileDialogService.ServiceKey,
            "Showcase portable file dialog service key");

        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "progpu-wpf-showcase-file-dialog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        string openPath = Path.Combine(tempDirectory, "open.txt");
        string savePathWithoutExtension = Path.Combine(tempDirectory, "saved");
        string savePath = savePathWithoutExtension + ".txt";
        File.WriteAllText(openPath, "Showcase file dialog payload");

        int requestCount = 0;
        var seenKinds = new List<string>();
        var seenTitles = new List<string>();
        var seenFilters = new List<string>();
        var seenDefaultExtensions = new List<string>();
        var seenSuggestedItemNames = new List<string>();
        IDisposable registration = fileDialogService.Register(
            request =>
            {
                string kind = request.Kind;
                seenKinds.Add(kind);
                seenTitles.Add(request.Title);
                seenFilters.Add(request.Filter);
                seenDefaultExtensions.Add(request.DefaultExtension);
                seenSuggestedItemNames.Add(request.SuggestedItemName);
                requestCount++;

                return kind switch
                {
                    "SaveFile" => savePathWithoutExtension,
                    "PickFolder" => tempDirectory,
                    _ => openPath
                };
            });

        try
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
            DrainDispatcher(window);

            AssertEqual(1, window.FileDialogShownCount, "Showcase file dialog shown count");
            AssertEqual(true, window.LastOpenFileDialogResult, "Showcase OpenFileDialog result");
            AssertEqual(openPath, window.LastOpenFileDialogFileName, "Showcase OpenFileDialog FileName");
            AssertEqual("open.txt", window.LastOpenFileDialogSafeFileName, "Showcase OpenFileDialog SafeFileName");
            AssertEqual(true, window.LastSaveFileDialogResult, "Showcase SaveFileDialog result");
            AssertEqual(savePath, window.LastSaveFileDialogFileName, "Showcase SaveFileDialog FileName");
            AssertEqual("saved.txt", window.LastSaveFileDialogSafeFileName, "Showcase SaveFileDialog SafeFileName");
            AssertEqual(true, window.LastFolderDialogResult, "Showcase OpenFolderDialog result");
            AssertEqual(tempDirectory, window.LastFolderDialogFolderName, "Showcase OpenFolderDialog FolderName");
            AssertEqual(Path.GetFileName(tempDirectory), window.LastFolderDialogSafeFolderName, "Showcase OpenFolderDialog SafeFolderName");
            AssertEqual(
                $"File dialogs: open.txt | saved.txt | {Path.GetFileName(tempDirectory)}",
                statusText.Text,
                "Showcase file dialog status text");

            AssertEqual(3, requestCount, "Showcase file dialog request count");
            AssertEqual("OpenFile", seenKinds[0], "Showcase file dialog open request kind");
            AssertEqual("SaveFile", seenKinds[1], "Showcase file dialog save request kind");
            AssertEqual("PickFolder", seenKinds[2], "Showcase file dialog folder request kind");
            AssertEqual("Open Showcase file", seenTitles[0], "Showcase file dialog open title");
            AssertEqual("Save Showcase file", seenTitles[1], "Showcase file dialog save title");
            AssertEqual("Select Showcase folder", seenTitles[2], "Showcase file dialog folder title");
            AssertEqual("Text files (*.txt)|*.txt|All files (*.*)|*.*", seenFilters[0], "Showcase file dialog open filter");
            AssertEqual("Text files (*.txt)|*.txt", seenFilters[1], "Showcase file dialog save filter");
            AssertEqual(string.Empty, seenFilters[2], "Showcase file dialog folder filter");
            AssertEqual("txt", seenDefaultExtensions[1], "Showcase file dialog save default extension");
            AssertEqual("saved", seenSuggestedItemNames[1], "Showcase file dialog save suggested item");
        }
        finally
        {
            registration.Dispose();
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static void ValidateSecondaryWindow(MainWindow window, MenuItem aboutMenuItem)
    {
        AssertEqual("_About", aboutMenuItem.Header, "secondary window menu header");

        var dialog = new AboutWindow();
        AssertEqual(null, dialog.Owner, "secondary window initial owner");
        AssertEqual("About ProGPU WPF Showcase", dialog.Title, "secondary window title");
        AssertEqual(SizeToContent.Height, dialog.SizeToContent, "secondary window SizeToContent");
        AssertEqual(ResizeMode.NoResize, dialog.ResizeMode, "secondary window resize mode");
        AssertEqual(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation, "secondary window startup location");

        var titleText = Require<TextBlock>(
            dialog.FindName("AboutTitleText"),
            "secondary window title TextBlock");
        var bodyText = Require<TextBlock>(
            dialog.FindName("AboutBodyText"),
            "secondary window body TextBlock");
        var closeButton = Require<Button>(
            dialog.FindName("AboutCloseButton"),
            "secondary window close Button");

        AssertEqual("ProGPU WPF Showcase", titleText.Text, "secondary window title text");
        AssertEqual(
            "Standard secondary WPF Window compiled through the ProGPU SDK.",
            bodyText.Text,
            "secondary window body text");
        AssertEqual(TextWrapping.Wrap, bodyText.TextWrapping, "secondary window body wrapping");
        AssertEqual("OK", closeButton.Content, "secondary window close button content");
        AssertEqual(true, closeButton.IsDefault, "secondary window close button default state");
        AssertEqual(true, closeButton.IsCancel, "secondary window close button cancel state");

        if (!window.IsVisible || Application.Current is not { } application)
        {
            return;
        }

        dialog.Owner = window;
        int closingCount = 0;
        int closedCount = 0;
        bool closingCancelBefore = true;
        bool closingCancelAfter = true;
        dialog.Closing += (_, e) =>
        {
            closingCount++;
            closingCancelBefore = e.Cancel;
            closingCancelAfter = e.Cancel;
        };
        dialog.Closed += (_, _) => closedCount++;

        int initialWindowCount = CountApplicationWindows(application);
        AssertEqual(true, ApplicationContainsWindow(application, dialog), "Application Windows contains constructed secondary window");
        dialog.Show();
        DrainDispatcher(window);

        AssertEqual(true, dialog.IsVisible, "secondary window visibility after show");
        AssertEqual(window, dialog.Owner, "secondary window owner after show");
        AssertEqual(1, window.OwnedWindows.Count, "main window owned window count after secondary show");
        AssertEqual(dialog, window.OwnedWindows[0], "main window owned window entry after secondary show");
        AssertEqual(initialWindowCount, CountApplicationWindows(application), "Application Windows count after secondary show");
        AssertEqual(true, ApplicationContainsWindow(application, dialog), "Application Windows contains secondary window");

        dialog.Close();
        DrainDispatcher(window);

        AssertEqual(1, closingCount, "secondary window Closing count");
        AssertEqual(1, closedCount, "secondary window Closed count");
        AssertEqual(false, closingCancelBefore, "secondary window Closing initial cancel state");
        AssertEqual(false, closingCancelAfter, "secondary window Closing final cancel state");
        AssertEqual(false, dialog.IsVisible, "secondary window visibility after close");
        AssertEqual(0, window.OwnedWindows.Count, "main window owned window count after secondary close");
        AssertEqual(initialWindowCount - 1, CountApplicationWindows(application), "Application Windows count after secondary close");
        AssertEqual(false, ApplicationContainsWindow(application, dialog), "Application Windows excludes secondary window after close");

        var modalDialog = new AboutWindow
        {
            Owner = window
        };
        int modalLoadedCount = 0;
        int modalClosingCount = 0;
        int modalClosedCount = 0;
        bool modalOwnerDuringLoaded = false;
        bool modalInApplicationWindowsDuringLoaded = false;
        int ownerOwnedWindowsCountDuringModal = 0;
        modalDialog.Loaded += (_, _) =>
        {
            modalLoadedCount++;
            modalOwnerDuringLoaded = ReferenceEquals(window, modalDialog.Owner);
            modalInApplicationWindowsDuringLoaded = ApplicationContainsWindow(application, modalDialog);
            ownerOwnedWindowsCountDuringModal = window.OwnedWindows.Count;
            modalDialog.Dispatcher.BeginInvoke(
                DispatcherPriority.Render,
                new Action(() => modalDialog.DialogResult = true));
        };
        modalDialog.Closing += (_, e) =>
        {
            modalClosingCount++;
            AssertEqual(false, e.Cancel, "secondary modal dialog Closing cancel state");
        };
        modalDialog.Closed += (_, _) => modalClosedCount++;

        int modalInitialWindowCount = CountApplicationWindows(application);
        AssertEqual(true, ApplicationContainsWindow(application, modalDialog), "Application Windows contains constructed modal dialog");
        AssertEqual(1, window.OwnedWindows.Count, "main window owned window count before modal dialog");

        bool? modalResult = modalDialog.ShowDialog();
        DrainDispatcher(window);

        AssertEqual(true, modalResult, "secondary modal dialog result");
        AssertEqual(1, modalLoadedCount, "secondary modal dialog Loaded count");
        AssertEqual(1, modalClosingCount, "secondary modal dialog Closing count");
        AssertEqual(1, modalClosedCount, "secondary modal dialog Closed count");
        AssertEqual(true, modalOwnerDuringLoaded, "secondary modal dialog owner during Loaded");
        AssertEqual(true, modalInApplicationWindowsDuringLoaded, "secondary modal dialog Application.Windows during Loaded");
        AssertEqual(1, ownerOwnedWindowsCountDuringModal, "main window owned window count during modal dialog");
        AssertEqual(false, modalDialog.IsVisible, "secondary modal dialog visibility after close");
        AssertEqual(0, window.OwnedWindows.Count, "main window owned window count after modal dialog");
        AssertEqual(modalInitialWindowCount - 1, CountApplicationWindows(application), "Application Windows count after modal dialog");
        AssertEqual(false, ApplicationContainsWindow(application, modalDialog), "Application Windows excludes modal dialog after close");

        var hiddenDialog = new AboutWindow { Owner = window };
        void HideLoadedDialog(object sender, RoutedEventArgs e)
        {
            hiddenDialog.Loaded -= HideLoadedDialog;
            hiddenDialog.Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(hiddenDialog.Hide));
        }
        hiddenDialog.Loaded += HideLoadedDialog;
        int hiddenDialogClosed = 0;
        hiddenDialog.Closed += (_, _) => hiddenDialogClosed++;
        bool? hiddenResult = hiddenDialog.ShowDialog();
        AssertEqual(false, hiddenResult, "hidden modal dialog result");
        AssertEqual(false, hiddenDialog.IsVisible, "hidden modal dialog visibility");
        AssertEqual(0, hiddenDialogClosed, "hiding modal dialog does not close it");
        AssertEqual(true, ApplicationContainsWindow(application, hiddenDialog), "hidden modal dialog retains application ownership");
        hiddenDialog.Show();
        hiddenDialog.Close();
        DrainDispatcher(window);
        AssertEqual(1, hiddenDialogClosed, "hidden modal dialog can be reused and closed");
        AssertEqual(0, window.OwnedWindows.Count, "hidden modal dialog releases owner on close");
    }

    private static int CountApplicationWindows(Application application)
    {
        int count = 0;
        foreach (Window _ in application.Windows)
        {
            count++;
        }

        return count;
    }

    private static bool ApplicationContainsWindow(Application application, Window window)
    {
        foreach (Window candidate in application.Windows)
        {
            if (ReferenceEquals(candidate, window))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateEditor(
        MainWindow window,
        PasswordBox passwordBox,
        RichTextBox richTextBox,
        TextBox dataObjectPayloadTextBox,
        Button dataObjectRoundTripButton,
        Button clipboardRoundTripButton,
        Button selectAllPayloadButton,
        Button copyPayloadButton,
        Button cutPayloadButton,
        Button pastePayloadButton,
        Button undoPayloadButton,
        Button redoPayloadButton,
        Button copyRichTextButton,
        Button pasteRichTextButton,
        TextBlock dataObjectStatusText)
    {
        AssertEqual(16, passwordBox.MaxLength, "editor PasswordBox max length");
        AssertEqual('*', passwordBox.PasswordChar, "editor PasswordBox password char");
        AssertEqual(0, window.EditorPasswordChangedCount, "editor PasswordBox initial changed count");
        AssertEqual("data object payload", dataObjectPayloadTextBox.Text, "DataObject initial payload");
        AssertEqual(ApplicationCommands.SelectAll, selectAllPayloadButton.Command, "DataObject payload SelectAll command");
        AssertEqual(dataObjectPayloadTextBox, selectAllPayloadButton.CommandTarget, "DataObject payload SelectAll target");
        AssertEqual(ApplicationCommands.Copy, copyPayloadButton.Command, "TextBox payload Copy command");
        AssertEqual(dataObjectPayloadTextBox, copyPayloadButton.CommandTarget, "TextBox payload Copy command target");
        AssertEqual(ApplicationCommands.Cut, cutPayloadButton.Command, "TextBox payload Cut command");
        AssertEqual(dataObjectPayloadTextBox, cutPayloadButton.CommandTarget, "TextBox payload Cut command target");
        AssertEqual(ApplicationCommands.Paste, pastePayloadButton.Command, "TextBox payload Paste command");
        AssertEqual(dataObjectPayloadTextBox, pastePayloadButton.CommandTarget, "TextBox payload Paste command target");
        AssertEqual(ApplicationCommands.Undo, undoPayloadButton.Command, "TextBox payload Undo command");
        AssertEqual(dataObjectPayloadTextBox, undoPayloadButton.CommandTarget, "TextBox payload Undo command target");
        AssertEqual(ApplicationCommands.Redo, redoPayloadButton.Command, "TextBox payload Redo command");
        AssertEqual(dataObjectPayloadTextBox, redoPayloadButton.CommandTarget, "TextBox payload Redo command target");
        AssertEqual(ApplicationCommands.Copy, copyRichTextButton.Command, "RichTextBox Copy command");
        AssertEqual(richTextBox, copyRichTextButton.CommandTarget, "RichTextBox Copy command target");
        AssertEqual(ApplicationCommands.Paste, pasteRichTextButton.Command, "RichTextBox Paste command");
        AssertEqual(richTextBox, pasteRichTextButton.CommandTarget, "RichTextBox Paste command target");
        AssertEqual("DataObject idle", dataObjectStatusText.Text, "DataObject initial status");

        passwordBox.Password = "showcase-secret";
        DrainDispatcher(window);
        AssertEqual("showcase-secret", passwordBox.Password, "editor PasswordBox password");
        AssertEqual(10, passwordBox.SecurePassword.Length, "editor PasswordBox secure password length");
        AssertEqual(1, window.EditorPasswordChangedCount, "editor PasswordBox changed count");

        passwordBox.Clear();
        DrainDispatcher(window);
        AssertEqual(string.Empty, passwordBox.Password, "editor PasswordBox cleared password");
        AssertEqual(2, window.EditorPasswordChangedCount, "editor PasswordBox clear changed count");

        var document = Require<FlowDocument>(richTextBox.Document, "editor FlowDocument");
        AssertEqual(new Thickness(6), document.PagePadding, "editor FlowDocument page padding");
        var paragraph = Require<Paragraph>(document.Blocks.FirstBlock, "editor document paragraph");
        var plainRun = FindDirectRun(paragraph, "Editable plain text", "editor plain Run");
        var bold = FindDirectBold(paragraph, "editor Bold inline");
        var boldRun = Require<Run>(bold.Inlines.FirstInline, "editor bold Run");

        AssertEqual("Editable plain text", plainRun.Text, "editor plain Run text");
        AssertEqual("bold text", boldRun.Text, "editor bold Run text");
        var documentText = new TextRange(document.ContentStart, document.ContentEnd).Text;
        AssertContains("Editable plain text", documentText, "editor FlowDocument TextRange plain text");
        AssertContains("bold text", documentText, "editor FlowDocument TextRange bold text");

        richTextBox.Selection.Select(plainRun.ContentStart, plainRun.ContentEnd);
        AssertEqual(true, EditingCommands.ToggleBold.CanExecute(null, richTextBox), "editor RichTextBox ToggleBold CanExecute");
        EditingCommands.ToggleBold.Execute(null, richTextBox);
        AssertEqual(
            FontWeights.Bold,
            richTextBox.Selection.GetPropertyValue(TextElement.FontWeightProperty),
            "editor RichTextBox ToggleBold applied weight");
        EditingCommands.ToggleBold.Execute(null, richTextBox);
        AssertEqual(
            FontWeights.Normal,
            richTextBox.Selection.GetPropertyValue(TextElement.FontWeightProperty),
            "editor RichTextBox ToggleBold restored weight");

        AssertEqual(true, ApplicationCommands.Copy.CanExecute(null, richTextBox), "editor RichTextBox Copy CanExecute");
        ApplicationCommands.Copy.Execute(null, richTextBox);
        DrainDispatcher(window);
        AssertEqual(true, Clipboard.ContainsText(), "editor RichTextBox Copy clipboard text state");
        AssertContains("Editable plain text", Clipboard.GetText(), "editor RichTextBox copied clipboard text");

        var pastePosition = paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward) ?? paragraph.ContentEnd;
        richTextBox.Selection.Select(pastePosition, pastePosition);
        Clipboard.SetText(" pasted clipboard text");
        AssertEqual(true, ApplicationCommands.Paste.CanExecute(null, richTextBox), "editor RichTextBox Paste CanExecute");
        ApplicationCommands.Paste.Execute(null, richTextBox);
        DrainDispatcher(window);
        documentText = new TextRange(document.ContentStart, document.ContentEnd).Text;
        AssertContains("pasted clipboard text", documentText, "editor RichTextBox pasted clipboard text");
        Clipboard.Clear();

        dataObjectPayloadTextBox.Text = "showcase data object";
        dataObjectPayloadTextBox.Select(3, 4);
        ApplicationCommands.SelectAll.Execute(null, dataObjectPayloadTextBox);
        AssertEqual(0, dataObjectPayloadTextBox.SelectionStart, "DataObject payload SelectAll selection start");
        AssertEqual(dataObjectPayloadTextBox.Text.Length, dataObjectPayloadTextBox.SelectionLength, "DataObject payload SelectAll selection length");

        dataObjectPayloadTextBox.Text = "alpha beta gamma";
        dataObjectPayloadTextBox.Select(0, 5);
        AssertEqual(true, ApplicationCommands.Copy.CanExecute(null, dataObjectPayloadTextBox), "TextBox payload Copy CanExecute");
        ApplicationCommands.Copy.Execute(null, dataObjectPayloadTextBox);
        DrainDispatcher(window);
        AssertEqual("alpha", Clipboard.GetText(), "TextBox payload copied clipboard text");
        dataObjectPayloadTextBox.Select(6, 4);
        AssertEqual(true, ApplicationCommands.Cut.CanExecute(null, dataObjectPayloadTextBox), "TextBox payload Cut CanExecute");
        ApplicationCommands.Cut.Execute(null, dataObjectPayloadTextBox);
        DrainDispatcher(window);
        AssertEqual("beta", Clipboard.GetText(), "TextBox payload cut clipboard text");
        AssertEqual("alpha  gamma", dataObjectPayloadTextBox.Text, "TextBox payload cut result");
        dataObjectPayloadTextBox.Text = "alpha gamma";
        dataObjectPayloadTextBox.CaretIndex = 5;
        Clipboard.SetText(" beta");
        AssertEqual(true, ApplicationCommands.Paste.CanExecute(null, dataObjectPayloadTextBox), "TextBox payload Paste CanExecute");
        ApplicationCommands.Paste.Execute(null, dataObjectPayloadTextBox);
        DrainDispatcher(window);
        AssertEqual("alpha beta gamma", dataObjectPayloadTextBox.Text, "TextBox payload paste result");

        AssertEqual(true, dataObjectPayloadTextBox.ApplyTemplate(), "TextBox payload template application");
        AssertEqual(
            true,
            dataObjectPayloadTextBox.Template?.FindName("PART_ContentHost", dataObjectPayloadTextBox) is ScrollViewer,
            "TextBox payload content host");

        dataObjectPayloadTextBox.Text = "undo seed";
        dataObjectPayloadTextBox.Select(dataObjectPayloadTextBox.Text.Length, 0);
        dataObjectPayloadTextBox.BeginChange();
        try
        {
            dataObjectPayloadTextBox.SelectedText = " unit";
        }
        finally
        {
            dataObjectPayloadTextBox.EndChange();
        }

        DrainDispatcher(window);
        AssertEqual("undo seed unit", dataObjectPayloadTextBox.Text, "TextBox payload undo seed edit");
        AssertEqual(true, dataObjectPayloadTextBox.CanUndo, "TextBox payload CanUndo state");
        AssertEqual(true, ApplicationCommands.Undo.CanExecute(null, dataObjectPayloadTextBox), "TextBox payload Undo CanExecute");
        ApplicationCommands.Undo.Execute(null, dataObjectPayloadTextBox);
        DrainDispatcher(window);
        AssertEqual("undo seed", dataObjectPayloadTextBox.Text, "TextBox payload undo result");
        AssertEqual(true, dataObjectPayloadTextBox.CanRedo, "TextBox payload CanRedo state");
        AssertEqual(true, ApplicationCommands.Redo.CanExecute(null, dataObjectPayloadTextBox), "TextBox payload Redo CanExecute");
        ApplicationCommands.Redo.Execute(null, dataObjectPayloadTextBox);
        DrainDispatcher(window);
        AssertEqual("undo seed unit", dataObjectPayloadTextBox.Text, "TextBox payload redo result");
        Clipboard.Clear();

        dataObjectPayloadTextBox.Text = "showcase data object";
        dataObjectRoundTripButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, dataObjectRoundTripButton));
        DrainDispatcher(window);
        AssertEqual(1, window.DataObjectRoundTripCount, "DataObject round-trip count");
        AssertEqual("showcase data object", window.LastDataObjectText, "DataObject unicode text");
        AssertEqual("custom:showcase data object", window.LastDataObjectCustomText, "DataObject custom text");
        AssertEqual("showcase data object | custom:showcase data object", dataObjectStatusText.Text, "DataObject status text");

        clipboardRoundTripButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clipboardRoundTripButton));
        DrainDispatcher(window);
        AssertEqual(1, window.ClipboardRoundTripCount, "Clipboard round-trip count");
        AssertEqual(true, window.LastClipboardContainsText, "Clipboard contains text");
        AssertEqual("showcase data object clipboard", window.LastClipboardText, "Clipboard text");
        AssertEqual(true, window.LastClipboardIsCurrent, "Clipboard current data object");
        AssertEqual("Clipboard: showcase data object clipboard", dataObjectStatusText.Text, "Clipboard status text");
        Clipboard.Clear();
    }

    private static void ValidateDocument(
        MainWindow window,
        FlowDocumentScrollViewer documentViewer,
        FlowDocumentPageViewer documentPageViewer,
        FlowDocumentReader documentReader)
    {
        AssertEqual(ScrollBarVisibility.Auto, documentViewer.VerticalScrollBarVisibility, "document FlowDocumentScrollViewer vertical visibility");
        var document = Require<FlowDocument>(documentViewer.Document, "document FlowDocument");
        AssertEqual(new Thickness(12), document.PagePadding, "document FlowDocument page padding");
        AssertEqual(3, document.Blocks.Count, "document FlowDocument block count");

        var bodyParagraph = Require<Paragraph>(
            document.Blocks.FirstBlock?.NextBlock,
            "document body Paragraph");
        var hyperlink = FindDirectHyperlink(bodyParagraph, "document Hyperlink");
        AssertEqual(
            new Uri("https://github.com/wieslawsoltes/ProGPU", UriKind.Absolute),
            hyperlink.NavigateUri,
            "document Hyperlink NavigateUri");

        var documentText = new TextRange(document.ContentStart, document.ContentEnd).Text;
        AssertContains("Managed WPF document content", documentText, "document FlowDocument title text");
        AssertContains("ProGPU renderer", documentText, "document FlowDocument hyperlink text");
        AssertContains("Application and window lifecycle", documentText, "document FlowDocument list text");

        int initialNavigateCount = window.DocumentLinkRequestNavigateCount;
        hyperlink.RaiseEvent(new RequestNavigateEventArgs(hyperlink.NavigateUri, string.Empty));
        DrainDispatcher(window);
        AssertEqual(initialNavigateCount + 1, window.DocumentLinkRequestNavigateCount, "document Hyperlink RequestNavigate count");
        AssertEqual("ProGPU renderer", window.LastDocumentLinkRequestNavigateText, "document Hyperlink RequestNavigate text");
        AssertEqual(
            "https://github.com/wieslawsoltes/ProGPU",
            window.LastDocumentLinkRequestNavigateUri,
            "document Hyperlink RequestNavigate URI");
        AssertEqual(
            "RequestNavigate",
            window.LastDocumentLinkRequestNavigateRoutedEventName,
            "document Hyperlink RequestNavigate routed event");

        AssertEqual(125.0, documentPageViewer.Zoom, "document FlowDocumentPageViewer zoom");
        AssertEqual(50.0, documentPageViewer.MinZoom, "document FlowDocumentPageViewer min zoom");
        AssertEqual(250.0, documentPageViewer.MaxZoom, "document FlowDocumentPageViewer max zoom");
        var pageViewerDocument = Require<FlowDocument>(
            documentPageViewer.Document,
            "document FlowDocumentPageViewer FlowDocument");
        AssertEqual(new Thickness(5), pageViewerDocument.PagePadding, "document FlowDocumentPageViewer page padding");
        AssertEqual(360.0, pageViewerDocument.ColumnWidth, "document FlowDocumentPageViewer column width");
        AssertEqual(2, pageViewerDocument.Blocks.Count, "document FlowDocumentPageViewer block count");
        var pageViewerList = Require<System.Windows.Documents.List>(
            pageViewerDocument.Blocks.LastBlock,
            "document FlowDocumentPageViewer List");
        AssertEqual(TextMarkerStyle.Square, pageViewerList.MarkerStyle, "document FlowDocumentPageViewer list marker style");
        var pageViewerText = new TextRange(pageViewerDocument.ContentStart, pageViewerDocument.ContentEnd).Text;
        AssertContains("Page viewer document", pageViewerText, "document FlowDocumentPageViewer title text");
        AssertContains("Showcase page viewer item", pageViewerText, "document FlowDocumentPageViewer list text");

        AssertEqual(FlowDocumentReaderViewingMode.Scroll, documentReader.ViewingMode, "document FlowDocumentReader viewing mode");
        var readerDocument = Require<FlowDocument>(
            documentReader.Document,
            "document FlowDocumentReader FlowDocument");
        AssertEqual(new Thickness(3), readerDocument.PagePadding, "document FlowDocumentReader page padding");
        AssertEqual(1, readerDocument.Blocks.Count, "document FlowDocumentReader block count");
        var readerText = new TextRange(readerDocument.ContentStart, readerDocument.ContentEnd).Text;
        AssertContains("Showcase reader document", readerText, "document FlowDocumentReader text");
    }

    private static void DrainDispatcher(DispatcherObject dispatcherObject)
    {
        dispatcherObject.Dispatcher.Invoke(
            static () => { },
            DispatcherPriority.ApplicationIdle);
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

    private static void UpdateBinding(DependencyObject target, DependencyProperty property)
    {
        BindingOperations.GetBindingExpression(target, property)?.UpdateTarget();
    }

    private static void UpdateSource(DependencyObject target, DependencyProperty property)
    {
        BindingOperations.GetBindingExpression(target, property)?.UpdateSource();
    }

    private static string GetSingleValidationErrorContent(DependencyObject target, string description)
    {
        var errors = Validation.GetErrors(target);
        AssertEqual(1, errors.Count, $"{description} count");
        return errors[0].ErrorContent?.ToString() ?? string.Empty;
    }

    private static Run FindDirectRun(Paragraph paragraph, string text, string description)
    {
        foreach (Inline inline in paragraph.Inlines)
        {
            if (inline is Run run && run.Text == text)
            {
                return run;
            }
        }

        throw new InvalidOperationException($"Expected {description}.");
    }

    private static Bold FindDirectBold(Paragraph paragraph, string description)
    {
        foreach (Inline inline in paragraph.Inlines)
        {
            if (inline is Bold bold)
            {
                return bold;
            }
        }

        throw new InvalidOperationException($"Expected {description}.");
    }

    private static Hyperlink FindDirectHyperlink(Paragraph paragraph, string description)
    {
        foreach (Inline inline in paragraph.Inlines)
        {
            if (inline is Hyperlink hyperlink)
            {
                return hyperlink;
            }
        }

        throw new InvalidOperationException($"Expected {description}.");
    }

    private static void ValidateFrameworkThemeSwitching(
        MainWindow window,
        Application application,
        MenuItem themeMenuItem)
    {
        (string Name, string Source)[] expectedThemes =
        [
            ("Aero", "/PresentationFramework.Aero;component/themes/Aero.NormalColor.xaml"),
            ("Aero2", "/PresentationFramework.Aero2;component/Themes/Aero2.NormalColor.xaml"),
            ("AeroLite", "/PresentationFramework.AeroLite;component/Themes/AeroLite.NormalColor.xaml"),
            ("Classic", "/PresentationFramework.Classic;component/Themes/Classic.xaml"),
            ("Fluent", "/PresentationFramework.Fluent;component/Themes/Fluent.xaml"),
            ("Luna", "/PresentationFramework.Luna;component/Themes/Luna.NormalColor.xaml"),
            ("Royale", "/PresentationFramework.Royale;component/Themes/Royale.NormalColor.xaml")
        ];

        AssertEqual(expectedThemes.Length, MainWindow.FrameworkThemeNames.Count, "framework theme name count");
        AssertEqual(expectedThemes.Length, themeMenuItem.Items.Count, "framework theme menu item count");
        for (int themeIndex = 0; themeIndex < expectedThemes.Length; themeIndex++)
        {
            (string name, string source) = expectedThemes[themeIndex];
            AssertEqual(name, MainWindow.FrameworkThemeNames[themeIndex], $"framework theme name {themeIndex}");
            window.ApplyFrameworkTheme(name);
            window.UpdateLayout();

            AssertEqual(name, window.ActiveFrameworkThemeName, $"active {name} framework theme");
            for (int menuIndex = 0; menuIndex < expectedThemes.Length; menuIndex++)
            {
                MenuItem menuItem = Require<MenuItem>(
                    window.FindName($"{expectedThemes[menuIndex].Name}ThemeMenuItem"),
                    $"{expectedThemes[menuIndex].Name} framework theme MenuItem");
                AssertEqual(
                    menuIndex == themeIndex,
                    menuItem.IsChecked,
                    $"{expectedThemes[menuIndex].Name} framework theme checked state while {name} is active");
            }

            ResourceDictionary activeDictionary = Require<ResourceDictionary>(
                FindMergedResourceDictionary(
                    application.Resources.MergedDictionaries,
                    candidate => string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase)),
                $"{name} framework theme ResourceDictionary");
            AssertEqual(source, activeDictionary.Source?.OriginalString, $"{name} framework theme source");
            AssertEqual(true, themeMenuItem.Template != null, $"{name} theme MenuItem template");
        }

        window.ApplyFrameworkTheme("Fluent");
        window.UpdateLayout();
        AssertEqual("Fluent", window.ActiveFrameworkThemeName, "restored framework theme");
    }

    private static ResourceDictionary? FindMergedResourceDictionary(
        Collection<ResourceDictionary> mergedDictionaries,
        Func<string, bool> sourcePredicate)
    {
        for (int index = 0; index < mergedDictionaries.Count; index++)
        {
            ResourceDictionary candidate = mergedDictionaries[index];
            if (sourcePredicate(candidate.Source?.OriginalString ?? string.Empty))
            {
                return candidate;
            }
        }

        return null;
    }

    private static T Require<T>(object? value, string description)
    {
        return value is T typed
            ? typed
            : throw new InvalidOperationException($"Expected {description} to be {typeof(T).Name}.");
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expected}', but found '{actual}'.");
        }
    }

    private static void AssertClose(double expected, double actual, double tolerance, string description)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be close to '{expected}', but found '{actual}'.");
        }
    }

    private static void AssertGreaterThan(int minimumExclusive, int actual, string description)
    {
        if (actual <= minimumExclusive)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be greater than '{minimumExclusive}', but found '{actual}'.");
        }
    }

    private static void AssertContains(string expectedText, string actualText, string description)
    {
        if (!actualText.Contains(expectedText, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {description} to contain '{expectedText}', but found '{actualText}'.");
        }
    }
}

using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Navigation;

namespace ProGPU.Wpf.RealXamlCompilerHarness;

public partial class MainWindow : Window
{
    public static RoutedUICommand SmokeRoutedCommand { get; } = new(
        "Smoke routed command",
        "SmokeRoutedCommand",
        typeof(MainWindow));

    public static RoutedUICommand SmokeClassRoutedCommand { get; } = new(
        "Smoke class routed command",
        "SmokeClassRoutedCommand",
        typeof(MainWindow));

    public MainWindow()
    {
        DataContext = new SmokeViewModel();
        InitializeComponent();
        ClassRoutedEventScopePanel.AddHandler(
            SmokeClassRoutedEventSource.SmokeClassBubbledEvent,
            new SmokeRoutedEventHandler(OnClassRoutedEventScopeHandledToo),
            handledEventsToo: true);
    }

    public int RoutedCommandCanExecuteCount { get; private set; }

    public int RoutedCommandExecutionCount { get; private set; }

    public string? LastRoutedCommandParameter { get; private set; }

    public int XamlClickCount { get; private set; }

    public string? LastXamlClickSenderName { get; private set; }

    public string? LastXamlClickRoutedEventName { get; private set; }

    public int XamlGotMouseCaptureCount { get; private set; }

    public string? LastXamlGotMouseCaptureSenderName { get; private set; }

    public string? LastXamlGotMouseCaptureRoutedEventName { get; private set; }

    public int XamlLostMouseCaptureCount { get; private set; }

    public string? LastXamlLostMouseCaptureSenderName { get; private set; }

    public string? LastXamlLostMouseCaptureRoutedEventName { get; private set; }

    public int XamlMouseWheelCount { get; private set; }

    public int LastXamlMouseWheelDelta { get; private set; }

    public string? LastXamlMouseWheelSenderName { get; private set; }

    public string? LastXamlMouseWheelRoutedEventName { get; private set; }

    public int RepeatButtonClickCount { get; private set; }

    public string? LastRepeatButtonClickSenderName { get; private set; }

    public string? LastRepeatButtonClickRoutedEventName { get; private set; }

    public int ThumbDragStartedCount { get; private set; }

    public string? LastThumbDragStartedSenderName { get; private set; }

    public string? LastThumbDragStartedRoutedEventName { get; private set; }

    public double LastThumbDragStartedHorizontalOffset { get; private set; }

    public double LastThumbDragStartedVerticalOffset { get; private set; }

    public int ThumbDragDeltaCount { get; private set; }

    public string? LastThumbDragDeltaSenderName { get; private set; }

    public string? LastThumbDragDeltaRoutedEventName { get; private set; }

    public double LastThumbDragDeltaHorizontalChange { get; private set; }

    public double LastThumbDragDeltaVerticalChange { get; private set; }

    public int ThumbDragCompletedCount { get; private set; }

    public string? LastThumbDragCompletedSenderName { get; private set; }

    public string? LastThumbDragCompletedRoutedEventName { get; private set; }

    public double LastThumbDragCompletedHorizontalChange { get; private set; }

    public double LastThumbDragCompletedVerticalChange { get; private set; }

    public bool LastThumbDragCompletedCanceled { get; private set; }

    public int BubbledThumbDragDeltaCount { get; private set; }

    public string? LastBubbledThumbDragDeltaSenderName { get; private set; }

    public string? LastBubbledThumbDragDeltaOriginalSourceName { get; private set; }

    public string? LastBubbledThumbDragDeltaRoutedEventName { get; private set; }

    public double LastBubbledThumbDragDeltaHorizontalChange { get; private set; }

    public double LastBubbledThumbDragDeltaVerticalChange { get; private set; }

    public int CustomRoutedEventSourceCount { get; private set; }

    public int CustomRoutedEventScopeCount { get; private set; }

    public string? LastCustomRoutedEventSourceSenderName { get; private set; }

    public string? LastCustomRoutedEventSourceOriginalSourceName { get; private set; }

    public string? LastCustomRoutedEventSourceRoutedEventName { get; private set; }

    public bool LastCustomRoutedEventSourceHandled { get; private set; }

    public string? LastCustomRoutedEventSourcePayload { get; private set; }

    public string? LastCustomRoutedEventScopeSenderName { get; private set; }

    public string? LastCustomRoutedEventScopeOriginalSourceName { get; private set; }

    public string? LastCustomRoutedEventScopeRoutedEventName { get; private set; }

    public bool LastCustomRoutedEventScopeHandled { get; private set; }

    public string? LastCustomRoutedEventScopePayload { get; private set; }

    public int ClassRoutedEventSourceCount { get; private set; }

    public int ClassRoutedEventScopeCount { get; private set; }

    public int ClassRoutedEventHandledTooScopeCount { get; private set; }

    public string? LastClassRoutedEventSourceSenderName { get; private set; }

    public string? LastClassRoutedEventSourceOriginalSourceName { get; private set; }

    public string? LastClassRoutedEventSourceRoutedEventName { get; private set; }

    public bool LastClassRoutedEventSourceHandled { get; private set; }

    public string? LastClassRoutedEventSourcePayload { get; private set; }

    public string? LastClassRoutedEventScopeSenderName { get; private set; }

    public string? LastClassRoutedEventScopeOriginalSourceName { get; private set; }

    public string? LastClassRoutedEventScopeRoutedEventName { get; private set; }

    public bool LastClassRoutedEventScopeHandled { get; private set; }

    public string? LastClassRoutedEventScopePayload { get; private set; }

    public string? LastClassRoutedEventHandledTooScopeSenderName { get; private set; }

    public string? LastClassRoutedEventHandledTooScopeOriginalSourceName { get; private set; }

    public string? LastClassRoutedEventHandledTooScopeRoutedEventName { get; private set; }

    public bool LastClassRoutedEventHandledTooScopeHandled { get; private set; }

    public string? LastClassRoutedEventHandledTooScopePayload { get; private set; }

    public int DocumentLinkRequestNavigateCount { get; private set; }

    public string? LastDocumentLinkRequestNavigateSenderName { get; private set; }

    public string? LastDocumentLinkRequestNavigateUri { get; private set; }

    public string? LastDocumentLinkRequestNavigateRoutedEventName { get; private set; }

    public int StyledClickCount { get; private set; }

    public string? LastStyledClickSenderName { get; private set; }

    public string? LastStyledClickRoutedEventName { get; private set; }

    public int MenuClickCount { get; private set; }

    public string? LastMenuClickSenderName { get; private set; }

    public string? LastMenuClickRoutedEventName { get; private set; }

    public int MenuCheckableCheckedCount { get; private set; }

    public int MenuCheckableUncheckedCount { get; private set; }

    public string? LastMenuCheckableCheckedSenderName { get; private set; }

    public string? LastMenuCheckableUncheckedSenderName { get; private set; }

    public string? LastMenuCheckableCheckedRoutedEventName { get; private set; }

    public string? LastMenuCheckableUncheckedRoutedEventName { get; private set; }

    public int ContextMenuClickCount { get; private set; }

    public string? LastContextMenuClickSenderName { get; private set; }

    public string? LastContextMenuClickRoutedEventName { get; private set; }

    public int PasswordChangedCount { get; private set; }

    public string? LastPasswordChangedSenderName { get; private set; }

    public string? LastPasswordChangedRoutedEventName { get; private set; }

    public int ToggleChoiceCheckedCount { get; private set; }

    public int ToggleChoiceUncheckedCount { get; private set; }

    public string? LastToggleChoiceCheckedSenderName { get; private set; }

    public string? LastToggleChoiceCheckedRoutedEventName { get; private set; }

    public string? LastToggleChoiceUncheckedSenderName { get; private set; }

    public string? LastToggleChoiceUncheckedRoutedEventName { get; private set; }

    public int ChoiceRadioCheckedCount { get; private set; }

    public int ChoiceRadioUncheckedCount { get; private set; }

    public string? LastChoiceRadioCheckedSenderName { get; private set; }

    public string? LastChoiceRadioCheckedRoutedEventName { get; private set; }

    public string? LastChoiceRadioUncheckedSenderName { get; private set; }

    public string? LastChoiceRadioUncheckedRoutedEventName { get; private set; }

    public int ExplicitTreeExpandedCount { get; private set; }

    public int ExplicitTreeCollapsedCount { get; private set; }

    public string? LastExplicitTreeExpandedSenderName { get; private set; }

    public string? LastExplicitTreeExpandedRoutedEventName { get; private set; }

    public string? LastExplicitTreeCollapsedSenderName { get; private set; }

    public string? LastExplicitTreeCollapsedRoutedEventName { get; private set; }

    public int ExplicitTreeSelectedCount { get; private set; }

    public int ExplicitTreeUnselectedCount { get; private set; }

    public string? LastExplicitTreeSelectedSenderName { get; private set; }

    public string? LastExplicitTreeSelectedRoutedEventName { get; private set; }

    public string? LastExplicitTreeUnselectedSenderName { get; private set; }

    public string? LastExplicitTreeUnselectedRoutedEventName { get; private set; }

    public int ListBoxSelectionChangedCount { get; private set; }

    public string? LastListBoxSelectionSenderName { get; private set; }

    public string? LastListBoxSelectionRoutedEventName { get; private set; }

    public int LastListBoxSelectionAddedCount { get; private set; }

    public int LastListBoxSelectionRemovedCount { get; private set; }

    public string? LastListBoxSelectionAddedItem { get; private set; }

    public string? LastListBoxSelectionRemovedItem { get; private set; }

    public int MultiListBoxSelectionChangedCount { get; private set; }

    public string? LastMultiListBoxSelectionSenderName { get; private set; }

    public string? LastMultiListBoxSelectionRoutedEventName { get; private set; }

    public int LastMultiListBoxSelectionAddedCount { get; private set; }

    public int LastMultiListBoxSelectionRemovedCount { get; private set; }

    public string? LastMultiListBoxSelectionAddedItem { get; private set; }

    public string? LastMultiListBoxSelectionRemovedItem { get; private set; }

    public int ComboBoxSelectionChangedCount { get; private set; }

    public string? LastComboBoxSelectionSenderName { get; private set; }

    public string? LastComboBoxSelectionRoutedEventName { get; private set; }

    public int LastComboBoxSelectionAddedCount { get; private set; }

    public int LastComboBoxSelectionRemovedCount { get; private set; }

    public string? LastComboBoxSelectionAddedItem { get; private set; }

    public string? LastComboBoxSelectionRemovedItem { get; private set; }

    public int FrameNavigatingCount { get; private set; }

    public string? LastFrameNavigatingSenderName { get; private set; }

    public string? LastFrameNavigatingUri { get; private set; }

    public string? LastFrameNavigatingNavigationMode { get; private set; }

    public int FrameNavigatedCount { get; private set; }

    public string? LastFrameNavigatedSenderName { get; private set; }

    public string? LastFrameNavigatedUri { get; private set; }

    public string? LastFrameNavigatedContentType { get; private set; }

    public int FrameLoadCompletedCount { get; private set; }

    public string? LastFrameLoadCompletedSenderName { get; private set; }

    public string? LastFrameLoadCompletedUri { get; private set; }

    public string? LastFrameLoadCompletedContentType { get; private set; }

    public int DataGridClipboardRowEventCount { get; private set; }

    public int DataGridClipboardHeaderEventCount { get; private set; }

    public int DataGridClipboardLastCellCount { get; private set; }

    public string? LastDataGridClipboardItemName { get; private set; }

    public string? LastDataGridClipboardHeaderText { get; private set; }

    public string? LastDataGridClipboardRowText { get; private set; }

    public string? LastDataGridClipboardFirstColumnHeader { get; private set; }

    public string? LastDataGridClipboardFirstCellContent { get; private set; }

    public string? LastDataGridClipboardSecondCellContent { get; private set; }

    public string? LastDataGridClipboardThirdCellContent { get; private set; }

    public int BindingTransferTargetUpdatedCount { get; private set; }

    public string? LastBindingTransferTargetSenderName { get; private set; }

    public string? LastBindingTransferTargetRoutedEventName { get; private set; }

    public string? LastBindingTransferTargetPropertyName { get; private set; }

    public string? LastBindingTransferTargetObjectName { get; private set; }

    public int BindingTransferSourceUpdatedCount { get; private set; }

    public string? LastBindingTransferSourceSenderName { get; private set; }

    public string? LastBindingTransferSourceRoutedEventName { get; private set; }

    public string? LastBindingTransferSourcePropertyName { get; private set; }

    public string? LastBindingTransferSourceObjectName { get; private set; }

    public int ValidatedBoxValidationErrorCount { get; private set; }

    public string? LastValidatedBoxValidationErrorSenderName { get; private set; }

    public string? LastValidatedBoxValidationErrorRoutedEventName { get; private set; }

    public string? LastValidatedBoxValidationErrorAction { get; private set; }

    public string? LastValidatedBoxValidationErrorContent { get; private set; }

    public string? LastValidatedBoxValidationErrorRuleName { get; private set; }

    public int RuleValidatedBoxValidationErrorCount { get; private set; }

    public string? LastRuleValidatedBoxValidationErrorSenderName { get; private set; }

    public string? LastRuleValidatedBoxValidationErrorRoutedEventName { get; private set; }

    public string? LastRuleValidatedBoxValidationErrorAction { get; private set; }

    public string? LastRuleValidatedBoxValidationErrorContent { get; private set; }

    public string? LastRuleValidatedBoxValidationErrorRuleName { get; private set; }

    public int StoryboardTargetLoadedCount { get; private set; }

    public string? LastStoryboardTargetLoadedSenderName { get; private set; }

    public string? LastStoryboardTargetLoadedRoutedEventName { get; private set; }

    public int FilteredItemsFilterCount { get; private set; }

    private void OnSmokeCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        RoutedCommandCanExecuteCount++;
        e.CanExecute = true;
        e.Handled = true;
    }

    private void OnSmokeCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        RoutedCommandExecutionCount++;
        LastRoutedCommandParameter = e.Parameter?.ToString();
        e.Handled = true;
    }

    private void OnXamlClick(object sender, RoutedEventArgs e)
    {
        XamlClickCount++;
        LastXamlClickSenderName = sender is FrameworkElement element ? element.Name : null;
        LastXamlClickRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnXamlGotMouseCapture(object sender, MouseEventArgs e)
    {
        XamlGotMouseCaptureCount++;
        LastXamlGotMouseCaptureSenderName = sender is FrameworkElement element ? element.Name : null;
        LastXamlGotMouseCaptureRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnXamlLostMouseCapture(object sender, MouseEventArgs e)
    {
        XamlLostMouseCaptureCount++;
        LastXamlLostMouseCaptureSenderName = sender is FrameworkElement element ? element.Name : null;
        LastXamlLostMouseCaptureRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnXamlMouseWheel(object sender, MouseWheelEventArgs e)
    {
        XamlMouseWheelCount++;
        LastXamlMouseWheelDelta = e.Delta;
        LastXamlMouseWheelSenderName = sender is FrameworkElement element ? element.Name : null;
        LastXamlMouseWheelRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnRepeatButtonClick(object sender, RoutedEventArgs e)
    {
        RepeatButtonClickCount++;
        LastRepeatButtonClickSenderName = sender is FrameworkElement element ? element.Name : null;
        LastRepeatButtonClickRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnDragManagerThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        ThumbDragStartedCount++;
        LastThumbDragStartedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastThumbDragStartedRoutedEventName = e.RoutedEvent?.Name;
        LastThumbDragStartedHorizontalOffset = e.HorizontalOffset;
        LastThumbDragStartedVerticalOffset = e.VerticalOffset;
    }

    private void OnDragManagerThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        ThumbDragDeltaCount++;
        LastThumbDragDeltaSenderName = sender is FrameworkElement element ? element.Name : null;
        LastThumbDragDeltaRoutedEventName = e.RoutedEvent?.Name;
        LastThumbDragDeltaHorizontalChange = e.HorizontalChange;
        LastThumbDragDeltaVerticalChange = e.VerticalChange;
    }

    private void OnDragManagerThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        ThumbDragCompletedCount++;
        LastThumbDragCompletedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastThumbDragCompletedRoutedEventName = e.RoutedEvent?.Name;
        LastThumbDragCompletedHorizontalChange = e.HorizontalChange;
        LastThumbDragCompletedVerticalChange = e.VerticalChange;
        LastThumbDragCompletedCanceled = e.Canceled;
    }

    private void OnBubbledThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        BubbledThumbDragDeltaCount++;
        LastBubbledThumbDragDeltaSenderName = sender is FrameworkElement element ? element.Name : null;
        LastBubbledThumbDragDeltaOriginalSourceName = e.OriginalSource is FrameworkElement source ? source.Name : null;
        LastBubbledThumbDragDeltaRoutedEventName = e.RoutedEvent?.Name;
        LastBubbledThumbDragDeltaHorizontalChange = e.HorizontalChange;
        LastBubbledThumbDragDeltaVerticalChange = e.VerticalChange;
    }

    private void OnCustomRoutedEventSource(object sender, SmokeRoutedEventArgs e)
    {
        CustomRoutedEventSourceCount++;
        LastCustomRoutedEventSourceSenderName = DescribeElementName(sender);
        LastCustomRoutedEventSourceOriginalSourceName = DescribeElementName(e.OriginalSource);
        LastCustomRoutedEventSourceRoutedEventName = e.RoutedEvent?.Name;
        LastCustomRoutedEventSourceHandled = e.Handled;
        LastCustomRoutedEventSourcePayload = e.Payload;
    }

    private void OnCustomRoutedEventScope(object sender, SmokeRoutedEventArgs e)
    {
        CustomRoutedEventScopeCount++;
        LastCustomRoutedEventScopeSenderName = DescribeElementName(sender);
        LastCustomRoutedEventScopeOriginalSourceName = DescribeElementName(e.OriginalSource);
        LastCustomRoutedEventScopeRoutedEventName = e.RoutedEvent?.Name;
        LastCustomRoutedEventScopeHandled = e.Handled;
        LastCustomRoutedEventScopePayload = e.Payload;
        e.Handled = true;
    }

    private void OnClassRoutedEventSource(object sender, SmokeRoutedEventArgs e)
    {
        ClassRoutedEventSourceCount++;
        LastClassRoutedEventSourceSenderName = DescribeElementName(sender);
        LastClassRoutedEventSourceOriginalSourceName = DescribeElementName(e.OriginalSource);
        LastClassRoutedEventSourceRoutedEventName = e.RoutedEvent?.Name;
        LastClassRoutedEventSourceHandled = e.Handled;
        LastClassRoutedEventSourcePayload = e.Payload;
        e.Handled = true;
    }

    private void OnClassRoutedEventScope(object sender, SmokeRoutedEventArgs e)
    {
        ClassRoutedEventScopeCount++;
        LastClassRoutedEventScopeSenderName = DescribeElementName(sender);
        LastClassRoutedEventScopeOriginalSourceName = DescribeElementName(e.OriginalSource);
        LastClassRoutedEventScopeRoutedEventName = e.RoutedEvent?.Name;
        LastClassRoutedEventScopeHandled = e.Handled;
        LastClassRoutedEventScopePayload = e.Payload;
    }

    private void OnClassRoutedEventScopeHandledToo(object sender, SmokeRoutedEventArgs e)
    {
        ClassRoutedEventHandledTooScopeCount++;
        LastClassRoutedEventHandledTooScopeSenderName = DescribeElementName(sender);
        LastClassRoutedEventHandledTooScopeOriginalSourceName = DescribeElementName(e.OriginalSource);
        LastClassRoutedEventHandledTooScopeRoutedEventName = e.RoutedEvent?.Name;
        LastClassRoutedEventHandledTooScopeHandled = e.Handled;
        LastClassRoutedEventHandledTooScopePayload = e.Payload;
    }

    private void OnDocumentLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        DocumentLinkRequestNavigateCount++;
        LastDocumentLinkRequestNavigateSenderName = sender switch
        {
            FrameworkContentElement contentElement => contentElement.Name,
            FrameworkElement element => element.Name,
            _ => null,
        };
        LastDocumentLinkRequestNavigateUri = e.Uri?.ToString();
        LastDocumentLinkRequestNavigateRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnStyledButtonClick(object sender, RoutedEventArgs e)
    {
        StyledClickCount++;
        LastStyledClickSenderName = sender is FrameworkElement element ? element.Name : null;
        LastStyledClickRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        MenuClickCount++;
        LastMenuClickSenderName = sender is FrameworkElement element ? element.Name : null;
        LastMenuClickRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnMenuCheckableChecked(object sender, RoutedEventArgs e)
    {
        MenuCheckableCheckedCount++;
        LastMenuCheckableCheckedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastMenuCheckableCheckedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnMenuCheckableUnchecked(object sender, RoutedEventArgs e)
    {
        MenuCheckableUncheckedCount++;
        LastMenuCheckableUncheckedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastMenuCheckableUncheckedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnContextMenuClick(object sender, RoutedEventArgs e)
    {
        ContextMenuClickCount++;
        LastContextMenuClickSenderName = sender is FrameworkElement element ? element.Name : null;
        LastContextMenuClickRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        PasswordChangedCount++;
        LastPasswordChangedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastPasswordChangedRoutedEventName = e.RoutedEvent?.Name;
        e.Handled = true;
    }

    private void OnToggleChoiceChecked(object sender, RoutedEventArgs e)
    {
        ToggleChoiceCheckedCount++;
        LastToggleChoiceCheckedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastToggleChoiceCheckedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnToggleChoiceUnchecked(object sender, RoutedEventArgs e)
    {
        ToggleChoiceUncheckedCount++;
        LastToggleChoiceUncheckedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastToggleChoiceUncheckedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnChoiceRadioChecked(object sender, RoutedEventArgs e)
    {
        ChoiceRadioCheckedCount++;
        LastChoiceRadioCheckedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastChoiceRadioCheckedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnChoiceRadioUnchecked(object sender, RoutedEventArgs e)
    {
        ChoiceRadioUncheckedCount++;
        LastChoiceRadioUncheckedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastChoiceRadioUncheckedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnExplicitTreeExpanded(object sender, RoutedEventArgs e)
    {
        ExplicitTreeExpandedCount++;
        LastExplicitTreeExpandedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastExplicitTreeExpandedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnExplicitTreeCollapsed(object sender, RoutedEventArgs e)
    {
        ExplicitTreeCollapsedCount++;
        LastExplicitTreeCollapsedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastExplicitTreeCollapsedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnExplicitTreeSelected(object sender, RoutedEventArgs e)
    {
        ExplicitTreeSelectedCount++;
        LastExplicitTreeSelectedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastExplicitTreeSelectedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnExplicitTreeUnselected(object sender, RoutedEventArgs e)
    {
        ExplicitTreeUnselectedCount++;
        LastExplicitTreeUnselectedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastExplicitTreeUnselectedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnSelectionEventListBoxChanged(object sender, SelectionChangedEventArgs e)
    {
        ListBoxSelectionChangedCount++;
        LastListBoxSelectionSenderName = sender is FrameworkElement element ? element.Name : null;
        LastListBoxSelectionRoutedEventName = e.RoutedEvent?.Name;
        LastListBoxSelectionAddedCount = e.AddedItems.Count;
        LastListBoxSelectionRemovedCount = e.RemovedItems.Count;
        LastListBoxSelectionAddedItem = DescribeSelectionItem(e.AddedItems);
        LastListBoxSelectionRemovedItem = DescribeSelectionItem(e.RemovedItems);
    }

    private void OnMultiSelectionEventListBoxChanged(object sender, SelectionChangedEventArgs e)
    {
        MultiListBoxSelectionChangedCount++;
        LastMultiListBoxSelectionSenderName = sender is FrameworkElement element ? element.Name : null;
        LastMultiListBoxSelectionRoutedEventName = e.RoutedEvent?.Name;
        LastMultiListBoxSelectionAddedCount = e.AddedItems.Count;
        LastMultiListBoxSelectionRemovedCount = e.RemovedItems.Count;
        LastMultiListBoxSelectionAddedItem = DescribeSelectionItem(e.AddedItems);
        LastMultiListBoxSelectionRemovedItem = DescribeSelectionItem(e.RemovedItems);
    }

    private void OnSelectionEventComboBoxChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboBoxSelectionChangedCount++;
        LastComboBoxSelectionSenderName = sender is FrameworkElement element ? element.Name : null;
        LastComboBoxSelectionRoutedEventName = e.RoutedEvent?.Name;
        LastComboBoxSelectionAddedCount = e.AddedItems.Count;
        LastComboBoxSelectionRemovedCount = e.RemovedItems.Count;
        LastComboBoxSelectionAddedItem = DescribeSelectionItem(e.AddedItems);
        LastComboBoxSelectionRemovedItem = DescribeSelectionItem(e.RemovedItems);
    }

    private void OnSourceNavigationFrameNavigating(object sender, NavigatingCancelEventArgs e)
    {
        FrameNavigatingCount++;
        LastFrameNavigatingSenderName = DescribeElementName(sender);
        LastFrameNavigatingUri = e.Uri?.ToString();
        LastFrameNavigatingNavigationMode = e.NavigationMode.ToString();
    }

    private void OnSourceNavigationFrameNavigated(object sender, NavigationEventArgs e)
    {
        FrameNavigatedCount++;
        LastFrameNavigatedSenderName = DescribeElementName(sender);
        LastFrameNavigatedUri = e.Uri?.ToString();
        LastFrameNavigatedContentType = e.Content?.GetType().FullName;
    }

    private void OnSourceNavigationFrameLoadCompleted(object sender, NavigationEventArgs e)
    {
        FrameLoadCompletedCount++;
        LastFrameLoadCompletedSenderName = DescribeElementName(sender);
        LastFrameLoadCompletedUri = e.Uri?.ToString();
        LastFrameLoadCompletedContentType = e.Content?.GetType().FullName;
    }

    private static string? DescribeSelectionItem(IList items)
    {
        return items.Count > 0 ? DescribeSelectionItem(items[0]) : null;
    }

    private static string? DescribeSelectionItem(object? item)
    {
        return item is ContentControl contentControl
            ? contentControl.Content?.ToString()
            : item?.ToString();
    }

    private void OnItemsDataGridCopyingRowClipboardContent(object sender, DataGridRowClipboardEventArgs e)
    {
        DataGridClipboardRowEventCount++;
        DataGridClipboardLastCellCount = e.ClipboardRowContent.Count;

        if (e.IsColumnHeadersRow)
        {
            DataGridClipboardHeaderEventCount++;
            LastDataGridClipboardHeaderText = TrimClipboardRow(e.FormatClipboardCellValues(DataFormats.UnicodeText));
            LastDataGridClipboardFirstColumnHeader = e.ClipboardRowContent.Count > 0
                ? e.ClipboardRowContent[0].Column.Header?.ToString()
                : null;
            return;
        }

        LastDataGridClipboardItemName = e.Item is SmokeItem item ? item.Name : e.Item?.ToString();
        LastDataGridClipboardRowText = TrimClipboardRow(e.FormatClipboardCellValues(DataFormats.UnicodeText));
        LastDataGridClipboardFirstCellContent = e.ClipboardRowContent.Count > 0
            ? e.ClipboardRowContent[0].Content?.ToString()
            : null;
        LastDataGridClipboardSecondCellContent = e.ClipboardRowContent.Count > 1
            ? e.ClipboardRowContent[1].Content?.ToString()
            : null;
        LastDataGridClipboardThirdCellContent = e.ClipboardRowContent.Count > 2
            ? e.ClipboardRowContent[2].Content?.ToString()
            : null;
    }

    private static string TrimClipboardRow(string value)
    {
        return value.TrimEnd('\r', '\n');
    }

    private void OnBindingTransferTargetUpdated(object sender, DataTransferEventArgs e)
    {
        BindingTransferTargetUpdatedCount++;
        LastBindingTransferTargetSenderName = DescribeElementName(sender);
        LastBindingTransferTargetRoutedEventName = e.RoutedEvent?.Name;
        LastBindingTransferTargetPropertyName = e.Property.Name;
        LastBindingTransferTargetObjectName = DescribeElementName(e.TargetObject);
    }

    private void OnBindingTransferSourceUpdated(object sender, DataTransferEventArgs e)
    {
        BindingTransferSourceUpdatedCount++;
        LastBindingTransferSourceSenderName = DescribeElementName(sender);
        LastBindingTransferSourceRoutedEventName = e.RoutedEvent?.Name;
        LastBindingTransferSourcePropertyName = e.Property.Name;
        LastBindingTransferSourceObjectName = DescribeElementName(e.TargetObject);
    }

    private void OnValidatedBoxValidationError(object sender, ValidationErrorEventArgs e)
    {
        ValidatedBoxValidationErrorCount++;
        LastValidatedBoxValidationErrorSenderName = DescribeElementName(sender);
        LastValidatedBoxValidationErrorRoutedEventName = e.RoutedEvent?.Name;
        LastValidatedBoxValidationErrorAction = e.Action.ToString();
        LastValidatedBoxValidationErrorContent = e.Error.ErrorContent?.ToString();
        LastValidatedBoxValidationErrorRuleName = e.Error.RuleInError?.GetType().Name;
    }

    private void OnRuleValidatedBoxValidationError(object sender, ValidationErrorEventArgs e)
    {
        RuleValidatedBoxValidationErrorCount++;
        LastRuleValidatedBoxValidationErrorSenderName = DescribeElementName(sender);
        LastRuleValidatedBoxValidationErrorRoutedEventName = e.RoutedEvent?.Name;
        LastRuleValidatedBoxValidationErrorAction = e.Action.ToString();
        LastRuleValidatedBoxValidationErrorContent = e.Error.ErrorContent?.ToString();
        LastRuleValidatedBoxValidationErrorRuleName = e.Error.RuleInError?.GetType().Name;
    }

    private static string? DescribeElementName(object? value)
    {
        return value is FrameworkElement element ? element.Name : null;
    }

    private void OnStoryboardTargetLoaded(object sender, RoutedEventArgs e)
    {
        StoryboardTargetLoadedCount++;
        LastStoryboardTargetLoadedSenderName = sender is FrameworkElement element ? element.Name : null;
        LastStoryboardTargetLoadedRoutedEventName = e.RoutedEvent?.Name;
    }

    private void OnFilteredItemsViewFilter(object sender, FilterEventArgs e)
    {
        FilteredItemsFilterCount++;
        e.Accepted = e.Item is SmokeItem smokeItem &&
            string.Equals(smokeItem.Name, "item beta", StringComparison.Ordinal);
    }

    public sealed class SmokeViewModel : INotifyPropertyChanged, IDataErrorInfo
    {
        private string _greeting = "bound greeting from real WPF";
        private bool _isWarning;
        private bool _isCritical;
        private bool _isTriggerActionActive;
        private bool _isMultiTriggerActionReady;
        private bool _isMultiTriggerActionArmed;
        private SmokeItem? _selectedItem;
        private string _selectedCategory = "secondary group";
        private string _comboSelectedCategory = "secondary group";
        private double _rangeValue = 42.0;
        private string _validatedText = "valid binding text";
        private string _ruleValidatedText = "rule: valid binding text";
        private string _bindingTransferText = "binding transfer initial";
        private string _bindingGroupFirstName = "group: Ada";
        private string _bindingGroupLastName = "group: Lovelace";

        public SmokeViewModel()
        {
            Items.Add(new SmokeItem("item alpha"));
            Items.Add(new SmokeItem("item beta"));
            Labels.Add("label alpha");
            Labels.Add("label beta");
            Nodes.Add(new SmokeNode(
                "root node",
                new SmokeNode("child alpha"),
                new SmokeNode("child beta")));
            _selectedItem = Items[1];
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Greeting
        {
            get => _greeting;
            set
            {
                if (!string.Equals(_greeting, value, StringComparison.Ordinal))
                {
                    _greeting = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ButtonText => "run bound command";

        public string TriggerButtonText => "style trigger target";

        public string Error => string.Empty;

        public string this[string columnName] =>
            string.Equals(columnName, nameof(ValidatedText), StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(_validatedText)
                ? "ValidatedText is required"
                : string.Empty;

        public string ValidatedText
        {
            get => _validatedText;
            set
            {
                if (!string.Equals(_validatedText, value, StringComparison.Ordinal))
                {
                    _validatedText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string RuleValidatedText
        {
            get => _ruleValidatedText;
            set
            {
                if (!string.Equals(_ruleValidatedText, value, StringComparison.Ordinal))
                {
                    _ruleValidatedText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string BindingTransferText
        {
            get => _bindingTransferText;
            set
            {
                if (!string.Equals(_bindingTransferText, value, StringComparison.Ordinal))
                {
                    _bindingTransferText = value;
                    OnPropertyChanged();
                }
            }
        }

        public string BindingGroupFirstName
        {
            get => _bindingGroupFirstName;
            set
            {
                if (!string.Equals(_bindingGroupFirstName, value, StringComparison.Ordinal))
                {
                    _bindingGroupFirstName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string BindingGroupLastName
        {
            get => _bindingGroupLastName;
            set
            {
                if (!string.Equals(_bindingGroupLastName, value, StringComparison.Ordinal))
                {
                    _bindingGroupLastName = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsWarning
        {
            get => _isWarning;
            set
            {
                if (_isWarning != value)
                {
                    _isWarning = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsCritical
        {
            get => _isCritical;
            set
            {
                if (_isCritical != value)
                {
                    _isCritical = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsTriggerActionActive
        {
            get => _isTriggerActionActive;
            set
            {
                if (_isTriggerActionActive != value)
                {
                    _isTriggerActionActive = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsMultiTriggerActionReady
        {
            get => _isMultiTriggerActionReady;
            set
            {
                if (_isMultiTriggerActionReady != value)
                {
                    _isMultiTriggerActionReady = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsMultiTriggerActionArmed
        {
            get => _isMultiTriggerActionArmed;
            set
            {
                if (_isMultiTriggerActionArmed != value)
                {
                    _isMultiTriggerActionArmed = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<SmokeItem> Items { get; } = new();

        public ObservableCollection<string> Labels { get; } = new();

        public ObservableCollection<SmokeNode> Nodes { get; } = new();

        public SmokeDetail Detail { get; } = new("detail from implicit template");

        public SmokeItem? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (!ReferenceEquals(_selectedItem, value))
                {
                    _selectedItem = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (!string.Equals(_selectedCategory, value, StringComparison.Ordinal))
                {
                    _selectedCategory = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ComboSelectedCategory
        {
            get => _comboSelectedCategory;
            set
            {
                if (!string.Equals(_comboSelectedCategory, value, StringComparison.Ordinal))
                {
                    _comboSelectedCategory = value;
                    OnPropertyChanged();
                }
            }
        }

        public double RangeValue
        {
            get => _rangeValue;
            set
            {
                if (!double.Equals(_rangeValue, value))
                {
                    _rangeValue = value;
                    OnPropertyChanged();
                }
            }
        }

        public SmokeCommand SmokeCommand { get; } = new();

        public SmokeToggleCommand ToggleCommand { get; } = new();

        public SmokeRequeryCommand RequeryCommand { get; } = new();

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class SmokeCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public int ExecutionCount { get; private set; }

        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public void Execute(object? parameter)
        {
            ExecutionCount++;
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public sealed class SmokeToggleCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged;

        public bool CanExecuteValue { get; private set; }

        public int CanExecuteCount { get; private set; }

        public int CanExecuteChangedCount { get; private set; }

        public int ExecutionCount { get; private set; }

        public object? LastParameter { get; private set; }

        public bool CanExecute(object? parameter)
        {
            CanExecuteCount++;
            return CanExecuteValue;
        }

        public void Execute(object? parameter)
        {
            ExecutionCount++;
            LastParameter = parameter;
        }

        public void SetCanExecute(bool value)
        {
            if (CanExecuteValue != value)
            {
                CanExecuteValue = value;
                CanExecuteChangedCount++;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public sealed class SmokeRequeryCommand : ICommand
    {
        public bool CanExecuteValue { get; private set; }

        public int CanExecuteCount { get; private set; }

        public int ExecutionCount { get; private set; }

        public object? LastParameter { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter)
        {
            CanExecuteCount++;
            return CanExecuteValue;
        }

        public void Execute(object? parameter)
        {
            ExecutionCount++;
            LastParameter = parameter;
        }

        public void SetCanExecute(bool value)
        {
            CanExecuteValue = value;
        }
    }
}

public sealed class SmokeClassCommandTextBox : TextBox
{
    static SmokeClassCommandTextBox()
    {
        CommandManager.RegisterClassCommandBinding(
            typeof(SmokeClassCommandTextBox),
            new CommandBinding(
                MainWindow.SmokeClassRoutedCommand,
                OnSmokeClassCommandExecuted,
                OnSmokeClassCommandCanExecute));
        CommandManager.RegisterClassInputBinding(
            typeof(SmokeClassCommandTextBox),
            new KeyBinding(MainWindow.SmokeClassRoutedCommand, Key.F7, ModifierKeys.Control)
            {
                CommandParameter = "class input payload"
            });
    }

    public bool IsClassCommandEnabled { get; set; } = true;

    public int ClassCommandCanExecuteCount { get; private set; }

    public int ClassCommandExecutionCount { get; private set; }

    public object? LastClassCommandParameter { get; private set; }

    private static void OnSmokeClassCommandCanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (sender is SmokeClassCommandTextBox target)
        {
            target.ClassCommandCanExecuteCount++;
            e.CanExecute = target.IsClassCommandEnabled;
            e.Handled = true;
        }
    }

    private static void OnSmokeClassCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (sender is SmokeClassCommandTextBox target)
        {
            target.ClassCommandExecutionCount++;
            target.LastClassCommandParameter = e.Parameter;
            e.Handled = true;
        }
    }
}

public delegate void SmokeRoutedEventHandler(object sender, SmokeRoutedEventArgs e);

public sealed class SmokeRoutedEventArgs : RoutedEventArgs
{
    public SmokeRoutedEventArgs(RoutedEvent routedEvent, object source, string payload)
        : base(routedEvent, source)
    {
        Payload = payload;
    }

    public string Payload { get; }
}

public sealed class SmokeRoutedEventSource : Control
{
    public static readonly RoutedEvent SmokeBubbledEvent = EventManager.RegisterRoutedEvent(
        "SmokeBubbled",
        RoutingStrategy.Bubble,
        typeof(SmokeRoutedEventHandler),
        typeof(SmokeRoutedEventSource));

    public event SmokeRoutedEventHandler SmokeBubbled
    {
        add => AddHandler(SmokeBubbledEvent, value);
        remove => RemoveHandler(SmokeBubbledEvent, value);
    }

    public SmokeRoutedEventArgs RaiseSmokeBubbled(string payload)
    {
        var args = new SmokeRoutedEventArgs(SmokeBubbledEvent, this, payload);
        RaiseEvent(args);
        return args;
    }
}

public sealed class SmokeClassRoutedEventSource : Control
{
    public static readonly RoutedEvent SmokeClassBubbledEvent = EventManager.RegisterRoutedEvent(
        "SmokeClassBubbled",
        RoutingStrategy.Bubble,
        typeof(SmokeRoutedEventHandler),
        typeof(SmokeClassRoutedEventSource));

    static SmokeClassRoutedEventSource()
    {
        EventManager.RegisterClassHandler(
            typeof(SmokeClassRoutedEventSource),
            SmokeClassBubbledEvent,
            new SmokeRoutedEventHandler(OnSmokeClassBubbledClassHandler));
    }

    public event SmokeRoutedEventHandler SmokeClassBubbled
    {
        add => AddHandler(SmokeClassBubbledEvent, value);
        remove => RemoveHandler(SmokeClassBubbledEvent, value);
    }

    public int ClassHandlerCount { get; private set; }

    public string? LastClassHandlerSenderName { get; private set; }

    public string? LastClassHandlerOriginalSourceName { get; private set; }

    public string? LastClassHandlerRoutedEventName { get; private set; }

    public bool LastClassHandlerHandled { get; private set; }

    public string? LastClassHandlerPayload { get; private set; }

    public SmokeRoutedEventArgs RaiseSmokeClassBubbled(string payload)
    {
        var args = new SmokeRoutedEventArgs(SmokeClassBubbledEvent, this, payload);
        RaiseEvent(args);
        return args;
    }

    private static void OnSmokeClassBubbledClassHandler(object sender, SmokeRoutedEventArgs e)
    {
        if (sender is SmokeClassRoutedEventSource source)
        {
            source.ClassHandlerCount++;
            source.LastClassHandlerSenderName = DescribeElementName(sender);
            source.LastClassHandlerOriginalSourceName = DescribeElementName(e.OriginalSource);
            source.LastClassHandlerRoutedEventName = e.RoutedEvent?.Name;
            source.LastClassHandlerHandled = e.Handled;
            source.LastClassHandlerPayload = e.Payload;
        }
    }

    private static string? DescribeElementName(object? value)
    {
        return value is FrameworkElement element ? element.Name : null;
    }
}

public sealed class ProviderDataFactory
{
    public string CreateProviderGreeting(string prefix, string value)
    {
        return $"{prefix} data {value}";
    }
}

public static class SmokeCompositeItemsProvider
{
    public static ObservableCollection<SmokeItem> Items
    {
        get
        {
            return new ObservableCollection<SmokeItem>
            {
                new("item alpha"),
                new("item beta"),
            };
        }
    }
}

public sealed class SmokeTextExtension : MarkupExtension
{
    public string Prefix { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return $"{Prefix} {Value} extension";
    }
}

public sealed class SmokeUpperConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string prefix = parameter?.ToString() ?? string.Empty;
        string text = value?.ToString() ?? string.Empty;
        return $"{prefix}:{text.ToUpperInvariant()}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public sealed class SmokeJoinConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string prefix = parameter?.ToString() ?? string.Empty;
        string[] parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            parts[i] = values[i]?.ToString() ?? string.Empty;
        }

        return $"{prefix}:{string.Join("|", parts)}";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        object[] values = new object[targetTypes.Length];
        Array.Fill(values, Binding.DoNothing);
        return values;
    }
}

public sealed class SmokePrefixValidationRule : ValidationRule
{
    public string RequiredPrefix { get; set; } = string.Empty;

    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        string text = value?.ToString() ?? string.Empty;
        return text.StartsWith(RequiredPrefix, StringComparison.Ordinal)
            ? ValidationResult.ValidResult
            : new ValidationResult(false, $"Value must start with '{RequiredPrefix}'.");
    }
}

public sealed class SmokeBindingGroupValidationRule : ValidationRule
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
                return new ValidationResult(false, $"BindingGroup values must start with '{RequiredPrefix}'.");
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

public static class SmokeDependencyProperties
{
    public static readonly DependencyProperty InheritedLabelProperty = DependencyProperty.RegisterAttached(
        "InheritedLabel",
        typeof(string),
        typeof(SmokeDependencyProperties),
        new FrameworkPropertyMetadata("unset inherited label", FrameworkPropertyMetadataOptions.Inherits));

    public static string GetInheritedLabel(DependencyObject target)
    {
        return (string)target.GetValue(InheritedLabelProperty);
    }

    public static void SetInheritedLabel(DependencyObject target, string value)
    {
        target.SetValue(InheritedLabelProperty, value);
    }
}

public sealed class SmokeDependencyPropertyControl : Control
{
    public static readonly DependencyProperty CoercedLevelProperty = DependencyProperty.Register(
        nameof(CoercedLevel),
        typeof(int),
        typeof(SmokeDependencyPropertyControl),
        new FrameworkPropertyMetadata(
            0,
            OnCoercedLevelChanged,
            CoerceLevel));

    public int CoercedLevel
    {
        get => (int)GetValue(CoercedLevelProperty);
        set => SetValue(CoercedLevelProperty, value);
    }

    public int CoercedLevelChangedCount { get; private set; }

    public int LastOldCoercedLevel { get; private set; }

    public int LastNewCoercedLevel { get; private set; }

    private static object CoerceLevel(DependencyObject dependencyObject, object baseValue)
    {
        int value = (int)baseValue;
        return Math.Max(0, Math.Min(10, value));
    }

    private static void OnCoercedLevelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is SmokeDependencyPropertyControl control)
        {
            control.CoercedLevelChangedCount++;
            control.LastOldCoercedLevel = (int)e.OldValue;
            control.LastNewCoercedLevel = (int)e.NewValue;
        }
    }
}

public sealed class SmokeDependencyPropertyOwnerControl : Control
{
    public static readonly DependencyProperty OwnerLevelProperty =
        SmokeDependencyPropertyControl.CoercedLevelProperty.AddOwner(
            typeof(SmokeDependencyPropertyOwnerControl),
            new FrameworkPropertyMetadata(
                24,
                OnOwnerLevelChanged,
                CoerceOwnerLevel));

    public int OwnerLevel
    {
        get => (int)GetValue(OwnerLevelProperty);
        set => SetValue(OwnerLevelProperty, value);
    }

    public int OwnerLevelChangedCount { get; private set; }

    public int LastOldOwnerLevel { get; private set; }

    public int LastNewOwnerLevel { get; private set; }

    public bool HasOwnerLevelLocalValue => !ReferenceEquals(ReadLocalValue(OwnerLevelProperty), DependencyProperty.UnsetValue);

    public string OwnerLevelBaseValueSource => DependencyPropertyHelper.GetValueSource(this, OwnerLevelProperty).BaseValueSource.ToString();

    public bool IsOwnerLevelCoerced => DependencyPropertyHelper.GetValueSource(this, OwnerLevelProperty).IsCoerced;

    public bool IsOwnerLevelCurrent => DependencyPropertyHelper.GetValueSource(this, OwnerLevelProperty).IsCurrent;

    public void ClearOwnerLevelValue()
    {
        ClearValue(OwnerLevelProperty);
    }

    public void SetCurrentOwnerLevel(int value)
    {
        SetCurrentValue(OwnerLevelProperty, value);
    }

    private static object CoerceOwnerLevel(DependencyObject dependencyObject, object baseValue)
    {
        int value = (int)baseValue;
        return Math.Max(20, Math.Min(30, value));
    }

    private static void OnOwnerLevelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is SmokeDependencyPropertyOwnerControl control)
        {
            control.OwnerLevelChangedCount++;
            control.LastOldOwnerLevel = (int)e.OldValue;
            control.LastNewOwnerLevel = (int)e.NewValue;
        }
    }
}

public class SmokeMetadataBaseControl : Control
{
    public static readonly DependencyProperty ModeLabelProperty = DependencyProperty.Register(
        nameof(ModeLabel),
        typeof(string),
        typeof(SmokeMetadataBaseControl),
        new FrameworkPropertyMetadata("base metadata label"));

    public string ModeLabel
    {
        get => (string)GetValue(ModeLabelProperty);
        set => SetValue(ModeLabelProperty, value);
    }
}

public sealed class SmokeOverrideMetadataControl : SmokeMetadataBaseControl
{
    static SmokeOverrideMetadataControl()
    {
        ModeLabelProperty.OverrideMetadata(
            typeof(SmokeOverrideMetadataControl),
            new FrameworkPropertyMetadata("override metadata label"));
    }

    public string ModeLabelBaseValueSource => DependencyPropertyHelper.GetValueSource(this, ModeLabelProperty).BaseValueSource.ToString();
}

public sealed class SmokeItem : INotifyPropertyChanged
{
    private string _name;
    private string _category;
    private bool _isActive;

    public SmokeItem(string name)
    {
        _name = name;
        _category = string.Equals(name, "item beta", StringComparison.Ordinal)
            ? "secondary group"
            : "primary group";
        _isActive = string.Equals(name, "item beta", StringComparison.Ordinal);
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

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SmokeDetail
{
    public SmokeDetail(string title)
    {
        Title = title;
    }

    public string Title { get; }
}

public sealed class SmokeNode
{
    public SmokeNode(string name, params SmokeNode[] children)
    {
        Name = name;
        foreach (SmokeNode child in children)
        {
            Children.Add(child);
        }
    }

    public string Name { get; }

    public ObservableCollection<SmokeNode> Children { get; } = new();
}

public sealed class SmokeItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? AlphaTemplate { get; set; }

    public DataTemplate? DefaultTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is SmokeItem smokeItem &&
            string.Equals(smokeItem.Name, "item alpha", StringComparison.Ordinal) &&
            AlphaTemplate != null)
        {
            return AlphaTemplate;
        }

        return DefaultTemplate ?? base.SelectTemplate(item, container);
    }
}

public sealed class SmokeItemContainerStyleSelector : StyleSelector
{
    public Style? AlphaStyle { get; set; }

    public Style? DefaultStyle { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container)
    {
        if (item is SmokeItem smokeItem &&
            string.Equals(smokeItem.Name, "item alpha", StringComparison.Ordinal) &&
            AlphaStyle != null)
        {
            return AlphaStyle;
        }

        return DefaultStyle ?? base.SelectStyle(item, container);
    }
}

public sealed class SmokeDetailTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SelectedTemplate { get; set; }

    public DataTemplate? FallbackTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is SmokeDetail detail &&
            detail.Title.Contains("implicit", StringComparison.Ordinal) &&
            SelectedTemplate != null)
        {
            return SelectedTemplate;
        }

        return FallbackTemplate ?? base.SelectTemplate(item, container);
    }
}

public sealed class SmokeAdorner : Adorner
{
    public SmokeAdorner(UIElement adornedElement)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var adornedBounds = new Rect(AdornedElement.RenderSize);
        drawingContext.DrawRectangle(null, new Pen(Brushes.LimeGreen, 1.0), adornedBounds);
    }
}

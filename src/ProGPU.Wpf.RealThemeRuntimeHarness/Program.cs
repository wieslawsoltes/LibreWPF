using System.Collections;
using System.Reflection;
using System.Runtime.Loader;

internal static class Program
{
    private const string CompilerHarnessAssemblyName = "ProGPU.Wpf.RealXamlCompilerHarness";
    private const string ProGpuWpfAssemblyName = "ProGPU.Wpf";
    private const string ProGpuWpfInteropAssemblyName = "ProGPU.Wpf.Interop";
    private const string FluentThemeAssemblyName = "PresentationFramework.Fluent";
    private const string AppTypeName = "ProGPU.Wpf.RealXamlCompilerHarness.App";
    private const string MainWindowTypeName = "ProGPU.Wpf.RealXamlCompilerHarness.MainWindow";
    private const string PortableWindowActivationServiceTypeName = "System.Windows.PortableWindowActivationService";
    private const string FluentDictionaryUri = "/PresentationFramework.Fluent;component/Themes/Fluent.xaml";

    [STAThread]
    private static int Main()
    {
        try
        {
            string repoRoot = FindRepoRoot();
            string presentationFrameworkPath = FindArtifactAssembly(repoRoot, "PresentationFramework");
            string presentationCorePath = FindArtifactAssembly(repoRoot, "PresentationCore");
            string compilerHarnessPath = FindArtifactAssembly(repoRoot, CompilerHarnessAssemblyName);
            string fluentThemePath = FindArtifactAssembly(repoRoot, FluentThemeAssemblyName);
            string proGpuWpfPath = FindOutputAssembly(ProGpuWpfAssemblyName);
            string proGpuWpfInteropPath = FindOutputAssembly(ProGpuWpfInteropAssemblyName);

            RunHarness(
                repoRoot,
                presentationFrameworkPath,
                presentationCorePath,
                compilerHarnessPath,
                fluentThemePath,
                proGpuWpfPath,
                proGpuWpfInteropPath);
            Console.WriteLine("Real WPF Fluent theme runtime smoke succeeded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunHarness(
        string repoRoot,
        string presentationFrameworkPath,
        string presentationCorePath,
        string compilerHarnessPath,
        string fluentThemePath,
        string proGpuWpfPath,
        string proGpuWpfInteropPath)
    {
        var loadContext = new WpfAssemblyLoadContext(
            repoRoot,
            presentationFrameworkPath,
            presentationCorePath,
            compilerHarnessPath,
            fluentThemePath,
            proGpuWpfPath,
            proGpuWpfInteropPath);
        loadContext.LoadFromAssemblyPath(proGpuWpfInteropPath);
        Assembly presentationFramework = loadContext.LoadFromAssemblyPath(presentationFrameworkPath);
        Assembly proGpuWpf = loadContext.LoadFromAssemblyPath(proGpuWpfPath);
        Assembly windowsBase = loadContext.LoadFromAssemblyName(new AssemblyName("WindowsBase"));
        loadContext.LoadFromAssemblyPath(fluentThemePath);
        Assembly compilerHarness = loadContext.LoadFromAssemblyPath(compilerHarnessPath);

        object? application = null;
        object? activation = null;
        Type? activationServiceType = null;

        try
        {
            application = Create(compilerHarness, AppTypeName);
            Invoke(application, "InitializeComponent");

            object window = Create(compilerHarness, MainWindowTypeName);
            object themeDictionary = LoadFluentThemeDictionary(presentationFramework);
            MergeThemeDictionary(application, themeDictionary);
            ApplyRepresentativeFluentStyles(presentationFramework, application, window, themeDictionary);
            ValidateThemedRuntimeState(window, application, themeDictionary);
            ValidateThemedVisualReplay(proGpuWpf, windowsBase, window);

            RegisterPortableActivation(
                proGpuWpf,
                presentationFramework,
                window,
                out activationServiceType,
                out activation);
        }
        finally
        {
            if (activation != null)
            {
                Invoke(activation, "Dispose");
            }

            activationServiceType?.GetMethod(
                "Clear",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);

            if (application != null)
            {
                Invoke(application, "Shutdown");
            }

            loadContext.Unload();
        }
    }

    private static object LoadFluentThemeDictionary(Assembly presentationFramework)
    {
        object themeDictionary = Create(presentationFramework, "System.Windows.ResourceDictionary");
        SetProperty(themeDictionary, "Source", new Uri(FluentDictionaryUri, UriKind.Relative));

        object source = GetProperty(themeDictionary, "Source");
        AssertEqual(FluentDictionaryUri, source.ToString(), "Fluent theme dictionary source");
        AssertCollectionCount(GetProperty(themeDictionary, "Keys"), expectedMinimum: 20, "Fluent theme dictionary keys");
        return themeDictionary;
    }

    private static void MergeThemeDictionary(object application, object themeDictionary)
    {
        object resources = GetProperty(application, "Resources");
        AddToCollection(GetProperty(resources, "MergedDictionaries"), themeDictionary);
        AssertCollectionCount(GetProperty(resources, "MergedDictionaries"), expectedMinimum: 1, "application merged dictionaries");
    }

    private static void ApplyRepresentativeFluentStyles(
        Assembly presentationFramework,
        object application,
        object window,
        object themeDictionary)
    {
        object windowStyle = GetDictionaryValue(themeDictionary, "DefaultWindowStyle");
        object defaultButtonStyle = GetDictionaryValue(themeDictionary, "DefaultButtonStyle");
        object buttonStyle = GetDictionaryValue(themeDictionary, "AccentButtonStyle");
        object calendarStyle = GetDictionaryValue(themeDictionary, "DefaultCalendarStyle");
        object checkBoxStyle = GetDictionaryValue(themeDictionary, "DefaultCheckBoxStyle");
        object comboBoxStyle = GetDictionaryValue(themeDictionary, "DefaultComboBoxStyle");
        object contextMenuStyle = GetDictionaryValue(themeDictionary, "DefaultContextMenuStyle");
        object datePickerStyle = GetDictionaryValue(themeDictionary, "DefaultDatePickerStyle");
        object datePickerCalendarStyle = GetDictionaryValue(themeDictionary, "DatePickerCalendarStyle");
        object dataGridStyle = GetDictionaryValue(themeDictionary, "DefaultDataGridStyle");
        object dataGridCellStyle = GetDictionaryValue(themeDictionary, "DefaultDataGridCellStyle");
        object dataGridCheckBoxElementStyle = GetDictionaryValue(themeDictionary, "DataGridCheckBoxElementDefaultStyle");
        object dataGridCheckBoxEditingElementStyle = GetDictionaryValue(themeDictionary, "DataGridCheckBoxEditingElementDefaultStyle");
        object dataGridColumnFloatingHeaderStyle = GetDictionaryValue(themeDictionary, "DefaultDataGridColumnFloatingHeaderStyle");
        object dataGridColumnHeaderStyle = GetDictionaryValue(themeDictionary, "DefaultDataGridColumnHeaderStyle");
        object dataGridColumnHeadersPresenterStyle = GetDictionaryValue(themeDictionary, "DefaultDataGridColumnHeadersPresenterStyle");
        object dataGridCellsPresenterStyle = GetDictionaryValue(themeDictionary, "DefaultDataGridCellsPresenterStyle");
        object dataGridHeaderDropSeparatorStyle = GetDictionaryValue(themeDictionary, "DefaultDataGridHeaderDropSeparatorStyle");
        object dataGridRowHeaderStyle = GetDictionaryValue(themeDictionary, "DefaultDataGridRowHeaderStyle");
        object dataGridRowStyle = GetDictionaryValue(themeDictionary, "DefaultDataGridRowStyle");
        object expanderStyle = GetDictionaryValue(themeDictionary, "DefaultExpanderStyle");
        object groupBoxStyle = GetDictionaryValue(themeDictionary, "DefaultGroupBoxStyle");
        object listViewStyle = GetDictionaryValue(themeDictionary, "DefaultListViewStyle");
        object listViewItemStyle = GetDictionaryValue(themeDictionary, "DefaultListViewItemStyle");
        object menuStyle = GetDictionaryValue(themeDictionary, "DefaultMenuStyle");
        object menuItemStyle = GetDictionaryValue(themeDictionary, "DefaultMenuItemStyle");
        object passwordBoxStyle = GetDictionaryValue(themeDictionary, "DefaultPasswordBoxStyle");
        object radioButtonStyle = GetDictionaryValue(themeDictionary, "DefaultRadioButtonStyle");
        object repeatButtonStyle = GetDictionaryValue(themeDictionary, "DefaultRepeatButtonStyle");
        object gridSplitterStyle = GetDictionaryValue(themeDictionary, "DefaultGridSplitterStyle");
        object itemsControlStyle = GetDictionaryValue(themeDictionary, "DefaultItemsControlStyle");
        object labelStyle = GetDictionaryValue(themeDictionary, "DefaultLabelStyle");
        object listBoxStyle = GetDictionaryValue(themeDictionary, "DefaultListBoxStyle");
        object listBoxItemStyle = GetDictionaryValue(themeDictionary, "DefaultListBoxItemStyle");
        object resizeGripStyle = GetDictionaryValue(themeDictionary, "DefaultResizeGripStyle");
        object scrollBarStyle = GetDictionaryValue(themeDictionary, "DefaultScrollBarStyle");
        object scrollViewerStyle = GetDictionaryValue(themeDictionary, "DefaultScrollViewerStyle");
        object separatorStyle = GetDictionaryValue(themeDictionary, "DefaultSeparatorStyle");
        object statusBarItemStyle = GetDictionaryValue(themeDictionary, "DefaultStatusBarItemStyle");
        object tabControlStyle = GetDictionaryValue(themeDictionary, "DefaultTabControlStyle");
        object tabItemStyle = GetDictionaryValue(themeDictionary, "DefaultTabItemStyle");
        object textBoxStyle = GetDictionaryValue(themeDictionary, "DefaultTextBoxStyle");
        object thumbStyle = GetDictionaryValue(themeDictionary, "DefaultThumbStyle");
        object toggleButtonStyle = GetDictionaryValue(themeDictionary, "DefaultToggleButtonStyle");
        object toolTipStyle = GetDictionaryValue(themeDictionary, "DefaultToolTipStyle");
        object treeViewStyle = GetDictionaryValue(themeDictionary, "DefaultTreeViewStyle");
        object treeViewItemStyle = GetDictionaryValue(themeDictionary, "DefaultTreeViewItemStyle");
        object richTextBoxStyle = GetDictionaryValue(themeDictionary, "DefaultRichTextBoxStyle");
        Type buttonType = GetRequiredType(presentationFramework, "System.Windows.Controls.Button");
        Type calendarType = GetRequiredType(presentationFramework, "System.Windows.Controls.Calendar");
        Type checkBoxType = GetRequiredType(presentationFramework, "System.Windows.Controls.CheckBox");
        Type comboBoxType = GetRequiredType(presentationFramework, "System.Windows.Controls.ComboBox");
        Type contextMenuType = GetRequiredType(presentationFramework, "System.Windows.Controls.ContextMenu");
        Type dataGridType = GetRequiredType(presentationFramework, "System.Windows.Controls.DataGrid");
        Type dataGridColumnHeaderType = GetRequiredType(presentationFramework, "System.Windows.Controls.Primitives.DataGridColumnHeader");
        Type dataGridColumnHeadersPresenterType = GetRequiredType(presentationFramework, "System.Windows.Controls.Primitives.DataGridColumnHeadersPresenter");
        Type datePickerType = GetRequiredType(presentationFramework, "System.Windows.Controls.DatePicker");
        Type expanderType = GetRequiredType(presentationFramework, "System.Windows.Controls.Expander");
        Type gridSplitterType = GetRequiredType(presentationFramework, "System.Windows.Controls.GridSplitter");
        Type groupBoxType = GetRequiredType(presentationFramework, "System.Windows.Controls.GroupBox");
        Type itemsControlType = GetRequiredType(presentationFramework, "System.Windows.Controls.ItemsControl");
        Type labelType = GetRequiredType(presentationFramework, "System.Windows.Controls.Label");
        Type listBoxType = GetRequiredType(presentationFramework, "System.Windows.Controls.ListBox");
        Type listBoxItemType = GetRequiredType(presentationFramework, "System.Windows.Controls.ListBoxItem");
        Type listViewType = GetRequiredType(presentationFramework, "System.Windows.Controls.ListView");
        Type menuType = GetRequiredType(presentationFramework, "System.Windows.Controls.Menu");
        Type menuItemType = GetRequiredType(presentationFramework, "System.Windows.Controls.MenuItem");
        Type passwordBoxType = GetRequiredType(presentationFramework, "System.Windows.Controls.PasswordBox");
        Type radioButtonType = GetRequiredType(presentationFramework, "System.Windows.Controls.RadioButton");
        Type repeatButtonType = GetRequiredType(presentationFramework, "System.Windows.Controls.Primitives.RepeatButton");
        Type resizeGripType = GetRequiredType(presentationFramework, "System.Windows.Controls.Primitives.ResizeGrip");
        Type scrollBarType = GetRequiredType(presentationFramework, "System.Windows.Controls.Primitives.ScrollBar");
        Type scrollViewerType = GetRequiredType(presentationFramework, "System.Windows.Controls.ScrollViewer");
        Type separatorType = GetRequiredType(presentationFramework, "System.Windows.Controls.Separator");
        Type sliderType = GetRequiredType(presentationFramework, "System.Windows.Controls.Slider");
        Type statusBarType = GetRequiredType(presentationFramework, "System.Windows.Controls.Primitives.StatusBar");
        Type textBoxType = GetRequiredType(presentationFramework, "System.Windows.Controls.TextBox");
        Type toolTipType = GetRequiredType(presentationFramework, "System.Windows.Controls.ToolTip");
        Type toolBarType = GetRequiredType(presentationFramework, "System.Windows.Controls.ToolBar");
        Type toolBarTrayType = GetRequiredType(presentationFramework, "System.Windows.Controls.ToolBarTray");
        Type tabControlType = GetRequiredType(presentationFramework, "System.Windows.Controls.TabControl");
        Type thumbType = GetRequiredType(presentationFramework, "System.Windows.Controls.Primitives.Thumb");
        Type toggleButtonType = GetRequiredType(presentationFramework, "System.Windows.Controls.Primitives.ToggleButton");
        Type treeViewType = GetRequiredType(presentationFramework, "System.Windows.Controls.TreeView");
        Type progressBarType = GetRequiredType(presentationFramework, "System.Windows.Controls.ProgressBar");
        object implicitButtonStyle = GetDictionaryValue(themeDictionary, buttonType);
        object implicitCalendarStyle = GetDictionaryValue(themeDictionary, calendarType);
        object implicitDataGridStyle = GetDictionaryValue(themeDictionary, dataGridType);
        object implicitDatePickerStyle = GetDictionaryValue(themeDictionary, datePickerType);
        object implicitTextBoxStyle = GetDictionaryValue(themeDictionary, textBoxType);
        object sliderStyle = GetDictionaryValue(themeDictionary, sliderType);
        object progressBarStyle = GetDictionaryValue(themeDictionary, progressBarType);
        object statusBarStyle = GetDictionaryValue(themeDictionary, statusBarType);
        object toolBarStyle = GetDictionaryValue(themeDictionary, toolBarType);
        object toolBarTrayStyle = GetDictionaryValue(themeDictionary, toolBarTrayType);
        object toolBarButtonStyle = GetDictionaryValue(themeDictionary, GetStaticProperty(toolBarType, "ButtonStyleKey"));
        object toolBarToggleButtonStyle = GetDictionaryValue(themeDictionary, GetStaticProperty(toolBarType, "ToggleButtonStyleKey"));
        object toolBarSeparatorStyle = GetDictionaryValue(themeDictionary, GetStaticProperty(toolBarType, "SeparatorStyleKey"));
        object statusBarSeparatorStyle = GetDictionaryValue(themeDictionary, GetStaticProperty(statusBarType, "SeparatorStyleKey"));

        SetProperty(window, "Style", windowStyle);

        object content = GetProperty(window, "Content");
        object children = GetProperty(content, "Children");
        object richTextBox = Invoke(window, "FindName", "DocumentBox");
        AssertType(richTextBox, "System.Windows.Controls.RichTextBox", "compiled themed RichTextBox");
        SetProperty(richTextBox, "Style", richTextBoxStyle);

        object button = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(button, "Content", "themed button smoke");
        SetProperty(button, "Style", buttonStyle);
        object contextMenu = Create(presentationFramework, "System.Windows.Controls.ContextMenu");
        SetProperty(contextMenu, "Style", contextMenuStyle);
        object contextMenuItem = Create(presentationFramework, "System.Windows.Controls.MenuItem");
        SetProperty(contextMenuItem, "Header", "Theme context action");
        SetProperty(contextMenuItem, "Style", menuItemStyle);
        AddToCollection(GetProperty(contextMenu, "Items"), contextMenuItem);
        SetProperty(button, "ContextMenu", contextMenu);
        object toolTip = Create(presentationFramework, "System.Windows.Controls.ToolTip");
        SetProperty(toolTip, "Content", "Theme tooltip smoke");
        SetProperty(toolTip, "Style", toolTipStyle);
        SetProperty(button, "ToolTip", toolTip);
        AddToCollection(children, button);

        object textBox = Create(presentationFramework, "System.Windows.Controls.TextBox");
        SetProperty(textBox, "Text", "themed text box smoke");
        SetProperty(textBox, "Style", textBoxStyle);
        AddToCollection(children, textBox);

        object tabControl = Create(presentationFramework, "System.Windows.Controls.TabControl");
        object tabItems = GetProperty(tabControl, "Items");
        object firstTabItem = Create(presentationFramework, "System.Windows.Controls.TabItem");
        SetProperty(firstTabItem, "Header", "Theme tab one");
        SetProperty(firstTabItem, "Content", "Theme tab content one");
        SetProperty(firstTabItem, "Style", tabItemStyle);
        AddToCollection(tabItems, firstTabItem);
        object secondTabItem = Create(presentationFramework, "System.Windows.Controls.TabItem");
        SetProperty(secondTabItem, "Header", "Theme tab two");
        SetProperty(secondTabItem, "Content", "Theme tab content two");
        SetProperty(secondTabItem, "Style", tabItemStyle);
        AddToCollection(tabItems, secondTabItem);
        SetProperty(tabControl, "SelectedIndex", 1);
        SetProperty(tabControl, "Style", tabControlStyle);
        AddToCollection(children, tabControl);

        object listView = Create(presentationFramework, "System.Windows.Controls.ListView");
        object listViewItems = GetProperty(listView, "Items");
        object firstListViewItem = Create(presentationFramework, "System.Windows.Controls.ListViewItem");
        SetProperty(firstListViewItem, "Content", "Theme list item one");
        SetProperty(firstListViewItem, "Style", listViewItemStyle);
        AddToCollection(listViewItems, firstListViewItem);
        object secondListViewItem = Create(presentationFramework, "System.Windows.Controls.ListViewItem");
        SetProperty(secondListViewItem, "Content", "Theme list item two");
        SetProperty(secondListViewItem, "Style", listViewItemStyle);
        AddToCollection(listViewItems, secondListViewItem);
        SetProperty(listView, "SelectedIndex", 1);
        SetProperty(listView, "Style", listViewStyle);
        AddToCollection(children, listView);

        object treeView = Create(presentationFramework, "System.Windows.Controls.TreeView");
        object treeViewItems = GetProperty(treeView, "Items");
        object rootTreeViewItem = Create(presentationFramework, "System.Windows.Controls.TreeViewItem");
        SetProperty(rootTreeViewItem, "Header", "Theme tree root");
        SetProperty(rootTreeViewItem, "IsExpanded", true);
        SetProperty(rootTreeViewItem, "Style", treeViewItemStyle);
        object childTreeViewItem = Create(presentationFramework, "System.Windows.Controls.TreeViewItem");
        SetProperty(childTreeViewItem, "Header", "Theme tree child");
        SetProperty(childTreeViewItem, "Style", treeViewItemStyle);
        AddToCollection(GetProperty(rootTreeViewItem, "Items"), childTreeViewItem);
        AddToCollection(treeViewItems, rootTreeViewItem);
        SetProperty(treeView, "Style", treeViewStyle);
        AddToCollection(children, treeView);

        DateTime themeDate = new(2026, 1, 7);

        object calendar = Create(presentationFramework, "System.Windows.Controls.Calendar");
        SetProperty(calendar, "DisplayDate", themeDate);
        SetProperty(calendar, "SelectedDate", themeDate);
        SetEnumProperty(calendar, "FirstDayOfWeek", "Monday");
        SetProperty(calendar, "Style", calendarStyle);
        AddToCollection(children, calendar);

        object datePicker = Create(presentationFramework, "System.Windows.Controls.DatePicker");
        SetProperty(datePicker, "DisplayDate", themeDate);
        SetProperty(datePicker, "SelectedDate", themeDate);
        SetProperty(datePicker, "Style", datePickerStyle);
        AddToCollection(children, datePicker);

        object menu = Create(presentationFramework, "System.Windows.Controls.Menu");
        object menuItems = GetProperty(menu, "Items");
        object rootMenuItem = Create(presentationFramework, "System.Windows.Controls.MenuItem");
        SetProperty(rootMenuItem, "Header", "_Theme");
        SetProperty(rootMenuItem, "Style", menuItemStyle);
        object childMenuItem = Create(presentationFramework, "System.Windows.Controls.MenuItem");
        SetProperty(childMenuItem, "Header", "_Open");
        SetProperty(childMenuItem, "Style", menuItemStyle);
        AddToCollection(GetProperty(rootMenuItem, "Items"), childMenuItem);
        object menuSeparator = Create(presentationFramework, "System.Windows.Controls.Separator");
        SetProperty(menuSeparator, "Style", GetDictionaryValue(themeDictionary, GetStaticProperty(menuItemType, "SeparatorStyleKey")));
        AddToCollection(GetProperty(rootMenuItem, "Items"), menuSeparator);
        AddToCollection(menuItems, rootMenuItem);
        SetProperty(menu, "Style", menuStyle);
        AddToCollection(children, menu);

        object toolBarTray = Create(presentationFramework, "System.Windows.Controls.ToolBarTray");
        object toolBar = Create(presentationFramework, "System.Windows.Controls.ToolBar");
        object toolBarItems = GetProperty(toolBar, "Items");
        object toolBarButton = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(toolBarButton, "Content", "Theme tool");
        SetProperty(toolBarButton, "Style", toolBarButtonStyle);
        AddToCollection(toolBarItems, toolBarButton);
        object toolBarToggle = Create(presentationFramework, "System.Windows.Controls.Primitives.ToggleButton");
        SetProperty(toolBarToggle, "Content", "Pinned");
        SetProperty(toolBarToggle, "IsChecked", true);
        SetProperty(toolBarToggle, "Style", toolBarToggleButtonStyle);
        AddToCollection(toolBarItems, toolBarToggle);
        object toolBarSeparator = Create(presentationFramework, "System.Windows.Controls.Separator");
        SetProperty(toolBarSeparator, "Style", toolBarSeparatorStyle);
        AddToCollection(toolBarItems, toolBarSeparator);
        SetProperty(toolBar, "Style", toolBarStyle);
        AddToCollection(GetProperty(toolBarTray, "ToolBars"), toolBar);
        SetProperty(toolBarTray, "Style", toolBarTrayStyle);
        AddToCollection(children, toolBarTray);

        object statusBar = Create(presentationFramework, "System.Windows.Controls.Primitives.StatusBar");
        object statusItems = GetProperty(statusBar, "Items");
        object statusItem = Create(presentationFramework, "System.Windows.Controls.Primitives.StatusBarItem");
        SetProperty(statusItem, "Content", "Theme status");
        SetProperty(statusItem, "Style", statusBarItemStyle);
        AddToCollection(statusItems, statusItem);
        object statusSeparator = Create(presentationFramework, "System.Windows.Controls.Separator");
        SetProperty(statusSeparator, "Style", statusBarSeparatorStyle);
        AddToCollection(statusItems, statusSeparator);
        SetProperty(statusBar, "Style", statusBarStyle);
        AddToCollection(children, statusBar);

        object checkBox = Create(presentationFramework, "System.Windows.Controls.CheckBox");
        SetProperty(checkBox, "Content", "Theme check");
        SetProperty(checkBox, "IsChecked", true);
        SetProperty(checkBox, "Style", checkBoxStyle);
        AddToCollection(children, checkBox);

        object radioButton = Create(presentationFramework, "System.Windows.Controls.RadioButton");
        SetProperty(radioButton, "Content", "Theme radio");
        SetProperty(radioButton, "GroupName", "ThemeChoice");
        SetProperty(radioButton, "IsChecked", true);
        SetProperty(radioButton, "Style", radioButtonStyle);
        AddToCollection(children, radioButton);

        object toggleButton = Create(presentationFramework, "System.Windows.Controls.Primitives.ToggleButton");
        SetProperty(toggleButton, "Content", "Theme toggle");
        SetProperty(toggleButton, "IsChecked", true);
        SetProperty(toggleButton, "Style", toggleButtonStyle);
        AddToCollection(children, toggleButton);

        object repeatButton = Create(presentationFramework, "System.Windows.Controls.Primitives.RepeatButton");
        SetProperty(repeatButton, "Content", "Theme repeat");
        SetProperty(repeatButton, "Style", repeatButtonStyle);
        AddToCollection(children, repeatButton);

        object expander = Create(presentationFramework, "System.Windows.Controls.Expander");
        SetProperty(expander, "Header", "Theme expander");
        SetProperty(expander, "Content", "Theme expander content");
        SetProperty(expander, "IsExpanded", true);
        SetProperty(expander, "Style", expanderStyle);
        AddToCollection(children, expander);

        object groupBox = Create(presentationFramework, "System.Windows.Controls.GroupBox");
        SetProperty(groupBox, "Header", "Theme group");
        SetProperty(groupBox, "Content", "Theme group content");
        SetProperty(groupBox, "Style", groupBoxStyle);
        AddToCollection(children, groupBox);

        object scrollViewer = Create(presentationFramework, "System.Windows.Controls.ScrollViewer");
        SetProperty(scrollViewer, "Content", "Theme scroll content line one\nTheme scroll content line two\nTheme scroll content line three");
        SetEnumProperty(scrollViewer, "VerticalScrollBarVisibility", "Visible");
        SetEnumProperty(scrollViewer, "HorizontalScrollBarVisibility", "Auto");
        SetProperty(scrollViewer, "Style", scrollViewerStyle);
        AddToCollection(children, scrollViewer);

        object scrollBar = Create(presentationFramework, "System.Windows.Controls.Primitives.ScrollBar");
        SetEnumProperty(scrollBar, "Orientation", "Vertical");
        SetProperty(scrollBar, "Minimum", 0.0);
        SetProperty(scrollBar, "Maximum", 100.0);
        SetProperty(scrollBar, "ViewportSize", 25.0);
        SetProperty(scrollBar, "Value", 33.0);
        SetProperty(scrollBar, "SmallChange", 1.0);
        SetProperty(scrollBar, "LargeChange", 10.0);
        SetProperty(scrollBar, "Style", scrollBarStyle);
        AddToCollection(children, scrollBar);

        object defaultButton = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(defaultButton, "Content", "Theme default button");
        SetProperty(defaultButton, "Style", defaultButtonStyle);
        AddToCollection(children, defaultButton);

        object itemsControl = Create(presentationFramework, "System.Windows.Controls.ItemsControl");
        object itemsControlItems = GetProperty(itemsControl, "Items");
        AddToCollection(itemsControlItems, "Theme items one");
        AddToCollection(itemsControlItems, "Theme items two");
        object implicitButton = Create(presentationFramework, "System.Windows.Controls.Button");
        SetProperty(implicitButton, "Content", "Theme implicit button");
        AddToCollection(itemsControlItems, implicitButton);
        object implicitTextBox = Create(presentationFramework, "System.Windows.Controls.TextBox");
        SetProperty(implicitTextBox, "Text", "Theme implicit text");
        AddToCollection(itemsControlItems, implicitTextBox);
        SetProperty(itemsControl, "Style", itemsControlStyle);
        AddToCollection(children, itemsControl);

        object listBox = Create(presentationFramework, "System.Windows.Controls.ListBox");
        object listBoxItems = GetProperty(listBox, "Items");
        object firstListBoxItem = Create(presentationFramework, "System.Windows.Controls.ListBoxItem");
        SetProperty(firstListBoxItem, "Content", "Theme list box one");
        SetProperty(firstListBoxItem, "Style", listBoxItemStyle);
        AddToCollection(listBoxItems, firstListBoxItem);
        object secondListBoxItem = Create(presentationFramework, "System.Windows.Controls.ListBoxItem");
        SetProperty(secondListBoxItem, "Content", "Theme list box two");
        SetProperty(secondListBoxItem, "Style", listBoxItemStyle);
        AddToCollection(listBoxItems, secondListBoxItem);
        SetProperty(listBox, "SelectedIndex", 1);
        SetProperty(listBox, "Style", listBoxStyle);
        AddToCollection(children, listBox);

        object label = Create(presentationFramework, "System.Windows.Controls.Label");
        SetProperty(label, "Content", "Theme label");
        SetProperty(label, "Style", labelStyle);
        AddToCollection(children, label);

        object separator = Create(presentationFramework, "System.Windows.Controls.Separator");
        SetProperty(separator, "Style", separatorStyle);
        AddToCollection(children, separator);

        object gridSplitter = Create(presentationFramework, "System.Windows.Controls.GridSplitter");
        SetProperty(gridSplitter, "Width", 16.0);
        SetProperty(gridSplitter, "Height", 40.0);
        SetProperty(gridSplitter, "Style", gridSplitterStyle);
        AddToCollection(children, gridSplitter);

        object resizeGrip = Create(presentationFramework, "System.Windows.Controls.Primitives.ResizeGrip");
        SetProperty(resizeGrip, "Style", resizeGripStyle);
        AddToCollection(children, resizeGrip);

        object thumb = Create(presentationFramework, "System.Windows.Controls.Primitives.Thumb");
        SetProperty(thumb, "Width", 24.0);
        SetProperty(thumb, "Height", 12.0);
        SetProperty(thumb, "Style", thumbStyle);
        AddToCollection(children, thumb);

        object comboBox = Create(presentationFramework, "System.Windows.Controls.ComboBox");
        object comboBoxItems = GetProperty(comboBox, "Items");
        AddToCollection(comboBoxItems, "theme item one");
        AddToCollection(comboBoxItems, "theme item two");
        SetProperty(comboBox, "SelectedIndex", 1);
        SetProperty(comboBox, "Style", comboBoxStyle);
        AddToCollection(children, comboBox);

        object passwordBox = Create(presentationFramework, "System.Windows.Controls.PasswordBox");
        SetProperty(passwordBox, "Password", "theme-secret");
        SetProperty(passwordBox, "Style", passwordBoxStyle);
        AddToCollection(children, passwordBox);

        object slider = Create(presentationFramework, "System.Windows.Controls.Slider");
        SetProperty(slider, "Minimum", 0.0);
        SetProperty(slider, "Maximum", 100.0);
        SetProperty(slider, "Value", 42.0);
        SetProperty(slider, "Style", sliderStyle);
        AddToCollection(children, slider);

        object progressBar = Create(presentationFramework, "System.Windows.Controls.ProgressBar");
        SetProperty(progressBar, "Minimum", 0.0);
        SetProperty(progressBar, "Maximum", 100.0);
        SetProperty(progressBar, "Value", 64.0);
        SetProperty(progressBar, "Style", progressBarStyle);
        AddToCollection(children, progressBar);

        object dataGrid = Create(presentationFramework, "System.Windows.Controls.DataGrid");
        SetProperty(dataGrid, "AutoGenerateColumns", false);
        SetProperty(dataGrid, "CanUserAddRows", false);
        SetProperty(dataGrid, "CanUserResizeColumns", true);
        SetEnumProperty(dataGrid, "HeadersVisibility", "All");
        SetEnumProperty(dataGrid, "GridLinesVisibility", "All");
        SetProperty(dataGrid, "Style", dataGridStyle);
        object dataGridColumns = GetProperty(dataGrid, "Columns");
        object nameColumn = Create(presentationFramework, "System.Windows.Controls.DataGridTextColumn");
        SetProperty(nameColumn, "Header", "Name");
        SetProperty(nameColumn, "Binding", Create(presentationFramework, "System.Windows.Data.Binding", "Name"));
        AddToCollection(dataGridColumns, nameColumn);
        object activeColumn = Create(presentationFramework, "System.Windows.Controls.DataGridCheckBoxColumn");
        SetProperty(activeColumn, "Header", "Active");
        SetProperty(activeColumn, "Binding", Create(presentationFramework, "System.Windows.Data.Binding", "IsActive"));
        AddToCollection(dataGridColumns, activeColumn);
        object dataGridItems = GetProperty(dataGrid, "Items");
        AddToCollection(dataGridItems, new ThemeGridRow("Theme grid one", true));
        AddToCollection(dataGridItems, new ThemeGridRow("Theme grid two", false));
        SetProperty(dataGrid, "SelectedIndex", 1);
        AddToCollection(children, dataGrid);

        AssertSame(windowStyle, GetProperty(window, "Style"), "Window Fluent style");
        AssertSame(buttonStyle, GetProperty(button, "Style"), "Button Fluent style");
        AssertSame(contextMenuStyle, GetProperty(contextMenu, "Style"), "ContextMenu Fluent style");
        AssertSame(toolTipStyle, GetProperty(toolTip, "Style"), "ToolTip Fluent style");
        AssertSame(textBoxStyle, GetProperty(textBox, "Style"), "TextBox Fluent style");
        AssertSame(tabControlStyle, GetProperty(tabControl, "Style"), "TabControl Fluent style");
        AssertSame(listViewStyle, GetProperty(listView, "Style"), "ListView Fluent style");
        AssertSame(treeViewStyle, GetProperty(treeView, "Style"), "TreeView Fluent style");
        AssertSame(calendarStyle, GetProperty(calendar, "Style"), "Calendar Fluent style");
        AssertSame(datePickerStyle, GetProperty(datePicker, "Style"), "DatePicker Fluent style");
        AssertSame(menuStyle, GetProperty(menu, "Style"), "Menu Fluent style");
        AssertSame(toolBarStyle, GetProperty(toolBar, "Style"), "ToolBar Fluent style");
        AssertSame(toolBarTrayStyle, GetProperty(toolBarTray, "Style"), "ToolBarTray Fluent style");
        AssertSame(statusBarStyle, GetProperty(statusBar, "Style"), "StatusBar Fluent style");
        AssertSame(checkBoxStyle, GetProperty(checkBox, "Style"), "CheckBox Fluent style");
        AssertSame(radioButtonStyle, GetProperty(radioButton, "Style"), "RadioButton Fluent style");
        AssertSame(toggleButtonStyle, GetProperty(toggleButton, "Style"), "ToggleButton Fluent style");
        AssertSame(repeatButtonStyle, GetProperty(repeatButton, "Style"), "RepeatButton Fluent style");
        AssertSame(expanderStyle, GetProperty(expander, "Style"), "Expander Fluent style");
        AssertSame(groupBoxStyle, GetProperty(groupBox, "Style"), "GroupBox Fluent style");
        AssertSame(scrollViewerStyle, GetProperty(scrollViewer, "Style"), "ScrollViewer Fluent style");
        AssertSame(scrollBarStyle, GetProperty(scrollBar, "Style"), "ScrollBar Fluent style");
        AssertSame(defaultButtonStyle, GetProperty(defaultButton, "Style"), "Default Button Fluent style");
        AssertSame(itemsControlStyle, GetProperty(itemsControl, "Style"), "ItemsControl Fluent style");
        AssertSame(listBoxStyle, GetProperty(listBox, "Style"), "ListBox Fluent style");
        AssertSame(labelStyle, GetProperty(label, "Style"), "Label Fluent style");
        AssertSame(separatorStyle, GetProperty(separator, "Style"), "Separator Fluent style");
        AssertSame(gridSplitterStyle, GetProperty(gridSplitter, "Style"), "GridSplitter Fluent style");
        AssertSame(resizeGripStyle, GetProperty(resizeGrip, "Style"), "ResizeGrip Fluent style");
        AssertSame(thumbStyle, GetProperty(thumb, "Style"), "Thumb Fluent style");
        AssertSame(comboBoxStyle, GetProperty(comboBox, "Style"), "ComboBox Fluent style");
        AssertSame(passwordBoxStyle, GetProperty(passwordBox, "Style"), "PasswordBox Fluent style");
        AssertSame(sliderStyle, GetProperty(slider, "Style"), "Slider Fluent style");
        AssertSame(progressBarStyle, GetProperty(progressBar, "Style"), "ProgressBar Fluent style");
        AssertSame(dataGridStyle, GetProperty(dataGrid, "Style"), "DataGrid Fluent style");
        AssertSame(richTextBoxStyle, GetProperty(richTextBox, "Style"), "RichTextBox Fluent style");
        AssertSame(implicitButtonStyle, GetProperty(implicitButton, "Style"), "runtime implicit Button Fluent style");
        AssertSame(implicitTextBoxStyle, GetProperty(implicitTextBox, "Style"), "runtime implicit TextBox Fluent style");
        AssertStyleBasedOn(implicitButtonStyle, defaultButtonStyle, "implicit Button Fluent BasedOn default Button style");
        AssertStyleBasedOn(implicitCalendarStyle, calendarStyle, "implicit Calendar Fluent BasedOn default Calendar style");
        AssertStyleBasedOn(implicitDataGridStyle, dataGridStyle, "implicit DataGrid Fluent BasedOn default DataGrid style");
        AssertStyleBasedOn(implicitDatePickerStyle, datePickerStyle, "implicit DatePicker Fluent BasedOn default DatePicker style");
        AssertStyleBasedOn(implicitTextBoxStyle, textBoxStyle, "implicit TextBox Fluent BasedOn default TextBox style");
        AssertStyleBasedOn(datePickerCalendarStyle, calendarStyle, "DatePicker Calendar Fluent BasedOn default Calendar style");
        AssertStyleBasedOn(
            dataGridCheckBoxEditingElementStyle,
            dataGridCheckBoxElementStyle,
            "DataGrid CheckBox editing Fluent BasedOn element style");
        AssertSame(defaultButtonStyle, Invoke(application, "TryFindResource", "DefaultButtonStyle"), "application Fluent default Button resource lookup");
        AssertSame(buttonStyle, Invoke(application, "TryFindResource", "AccentButtonStyle"), "application Fluent resource lookup");
        AssertSame(textBoxStyle, Invoke(application, "TryFindResource", "DefaultTextBoxStyle"), "application Fluent TextBox resource lookup");
        AssertSame(calendarStyle, Invoke(application, "TryFindResource", "DefaultCalendarStyle"), "application Fluent Calendar resource lookup");
        AssertSame(comboBoxStyle, Invoke(application, "TryFindResource", "DefaultComboBoxStyle"), "application Fluent ComboBox resource lookup");
        AssertSame(contextMenuStyle, Invoke(application, "TryFindResource", "DefaultContextMenuStyle"), "application Fluent ContextMenu resource lookup");
        AssertSame(datePickerStyle, Invoke(application, "TryFindResource", "DefaultDatePickerStyle"), "application Fluent DatePicker resource lookup");
        AssertSame(dataGridStyle, Invoke(application, "TryFindResource", "DefaultDataGridStyle"), "application Fluent DataGrid resource lookup");
        AssertSame(dataGridCellStyle, Invoke(application, "TryFindResource", "DefaultDataGridCellStyle"), "application Fluent DataGridCell resource lookup");
        AssertSame(dataGridCheckBoxElementStyle, Invoke(application, "TryFindResource", "DataGridCheckBoxElementDefaultStyle"), "application Fluent DataGrid CheckBox element resource lookup");
        AssertSame(dataGridCheckBoxEditingElementStyle, Invoke(application, "TryFindResource", "DataGridCheckBoxEditingElementDefaultStyle"), "application Fluent DataGrid CheckBox editing resource lookup");
        AssertSame(dataGridColumnFloatingHeaderStyle, Invoke(application, "TryFindResource", "DefaultDataGridColumnFloatingHeaderStyle"), "application Fluent DataGrid floating header resource lookup");
        AssertSame(dataGridColumnHeaderStyle, Invoke(application, "TryFindResource", "DefaultDataGridColumnHeaderStyle"), "application Fluent DataGrid column header resource lookup");
        AssertSame(dataGridColumnHeadersPresenterStyle, Invoke(application, "TryFindResource", "DefaultDataGridColumnHeadersPresenterStyle"), "application Fluent DataGrid column headers presenter resource lookup");
        AssertSame(dataGridCellsPresenterStyle, Invoke(application, "TryFindResource", "DefaultDataGridCellsPresenterStyle"), "application Fluent DataGrid cells presenter resource lookup");
        AssertSame(dataGridHeaderDropSeparatorStyle, Invoke(application, "TryFindResource", "DefaultDataGridHeaderDropSeparatorStyle"), "application Fluent DataGrid drop separator resource lookup");
        AssertSame(dataGridRowHeaderStyle, Invoke(application, "TryFindResource", "DefaultDataGridRowHeaderStyle"), "application Fluent DataGrid row header resource lookup");
        AssertSame(dataGridRowStyle, Invoke(application, "TryFindResource", "DefaultDataGridRowStyle"), "application Fluent DataGrid row resource lookup");
        AssertSame(checkBoxStyle, Invoke(application, "TryFindResource", "DefaultCheckBoxStyle"), "application Fluent CheckBox resource lookup");
        AssertSame(expanderStyle, Invoke(application, "TryFindResource", "DefaultExpanderStyle"), "application Fluent Expander resource lookup");
        AssertSame(groupBoxStyle, Invoke(application, "TryFindResource", "DefaultGroupBoxStyle"), "application Fluent GroupBox resource lookup");
        AssertSame(menuStyle, Invoke(application, "TryFindResource", "DefaultMenuStyle"), "application Fluent Menu resource lookup");
        AssertSame(menuItemStyle, Invoke(application, "TryFindResource", "DefaultMenuItemStyle"), "application Fluent MenuItem resource lookup");
        AssertSame(statusBarItemStyle, Invoke(application, "TryFindResource", "DefaultStatusBarItemStyle"), "application Fluent StatusBarItem resource lookup");
        AssertSame(passwordBoxStyle, Invoke(application, "TryFindResource", "DefaultPasswordBoxStyle"), "application Fluent PasswordBox resource lookup");
        AssertSame(radioButtonStyle, Invoke(application, "TryFindResource", "DefaultRadioButtonStyle"), "application Fluent RadioButton resource lookup");
        AssertSame(repeatButtonStyle, Invoke(application, "TryFindResource", "DefaultRepeatButtonStyle"), "application Fluent RepeatButton resource lookup");
        AssertSame(gridSplitterStyle, Invoke(application, "TryFindResource", "DefaultGridSplitterStyle"), "application Fluent GridSplitter resource lookup");
        AssertSame(itemsControlStyle, Invoke(application, "TryFindResource", "DefaultItemsControlStyle"), "application Fluent ItemsControl resource lookup");
        AssertSame(labelStyle, Invoke(application, "TryFindResource", "DefaultLabelStyle"), "application Fluent Label resource lookup");
        AssertSame(listBoxStyle, Invoke(application, "TryFindResource", "DefaultListBoxStyle"), "application Fluent ListBox resource lookup");
        AssertSame(listBoxItemStyle, Invoke(application, "TryFindResource", "DefaultListBoxItemStyle"), "application Fluent ListBoxItem resource lookup");
        AssertSame(resizeGripStyle, Invoke(application, "TryFindResource", "DefaultResizeGripStyle"), "application Fluent ResizeGrip resource lookup");
        AssertSame(scrollBarStyle, Invoke(application, "TryFindResource", "DefaultScrollBarStyle"), "application Fluent ScrollBar resource lookup");
        AssertSame(scrollViewerStyle, Invoke(application, "TryFindResource", "DefaultScrollViewerStyle"), "application Fluent ScrollViewer resource lookup");
        AssertSame(separatorStyle, Invoke(application, "TryFindResource", "DefaultSeparatorStyle"), "application Fluent Separator resource lookup");
        AssertSame(thumbStyle, Invoke(application, "TryFindResource", "DefaultThumbStyle"), "application Fluent Thumb resource lookup");
        AssertSame(toggleButtonStyle, Invoke(application, "TryFindResource", "DefaultToggleButtonStyle"), "application Fluent ToggleButton resource lookup");
        AssertSame(toolTipStyle, Invoke(application, "TryFindResource", "DefaultToolTipStyle"), "application Fluent ToolTip resource lookup");
        AssertSame(implicitButtonStyle, Invoke(application, "TryFindResource", buttonType), "application Fluent Button implicit style lookup");
        AssertSame(implicitCalendarStyle, Invoke(application, "TryFindResource", calendarType), "application Fluent Calendar implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", checkBoxType), "System.Windows.Style", "application Fluent CheckBox implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", comboBoxType), "System.Windows.Style", "application Fluent ComboBox implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", contextMenuType), "System.Windows.Style", "application Fluent ContextMenu implicit style lookup");
        AssertSame(implicitDataGridStyle, Invoke(application, "TryFindResource", dataGridType), "application Fluent DataGrid implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", dataGridColumnHeaderType), "System.Windows.Style", "application Fluent DataGridColumnHeader implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", dataGridColumnHeadersPresenterType), "System.Windows.Style", "application Fluent DataGridColumnHeadersPresenter implicit style lookup");
        AssertSame(implicitDatePickerStyle, Invoke(application, "TryFindResource", datePickerType), "application Fluent DatePicker implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", expanderType), "System.Windows.Style", "application Fluent Expander implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", gridSplitterType), "System.Windows.Style", "application Fluent GridSplitter implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", groupBoxType), "System.Windows.Style", "application Fluent GroupBox implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", itemsControlType), "System.Windows.Style", "application Fluent ItemsControl implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", labelType), "System.Windows.Style", "application Fluent Label implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", listBoxType), "System.Windows.Style", "application Fluent ListBox implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", listBoxItemType), "System.Windows.Style", "application Fluent ListBoxItem implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", listViewType), "System.Windows.Style", "application Fluent ListView implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", menuType), "System.Windows.Style", "application Fluent Menu implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", passwordBoxType), "System.Windows.Style", "application Fluent PasswordBox implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", radioButtonType), "System.Windows.Style", "application Fluent RadioButton implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", repeatButtonType), "System.Windows.Style", "application Fluent RepeatButton implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", resizeGripType), "System.Windows.Style", "application Fluent ResizeGrip implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", scrollBarType), "System.Windows.Style", "application Fluent ScrollBar implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", scrollViewerType), "System.Windows.Style", "application Fluent ScrollViewer implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", separatorType), "System.Windows.Style", "application Fluent Separator implicit style lookup");
        AssertSame(sliderStyle, Invoke(application, "TryFindResource", sliderType), "application Fluent Slider implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", statusBarType), "System.Windows.Style", "application Fluent StatusBar implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", tabControlType), "System.Windows.Style", "application Fluent TabControl implicit style lookup");
        AssertSame(implicitTextBoxStyle, Invoke(application, "TryFindResource", textBoxType), "application Fluent TextBox implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", toolTipType), "System.Windows.Style", "application Fluent ToolTip implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", thumbType), "System.Windows.Style", "application Fluent Thumb implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", toggleButtonType), "System.Windows.Style", "application Fluent ToggleButton implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", toolBarType), "System.Windows.Style", "application Fluent ToolBar implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", toolBarTrayType), "System.Windows.Style", "application Fluent ToolBarTray implicit style lookup");
        AssertType(Invoke(application, "TryFindResource", treeViewType), "System.Windows.Style", "application Fluent TreeView implicit style lookup");
        AssertSame(progressBarStyle, Invoke(application, "TryFindResource", progressBarType), "application Fluent ProgressBar implicit style lookup");
    }

    private static void ValidateThemedRuntimeState(object window, object application, object themeDictionary)
    {
        object content = GetProperty(window, "Content");
        object children = GetProperty(content, "Children");
        AssertCollectionCount(children, expectedMinimum: 44, "themed stack panel children");

        int childCount = GetCollectionCount(children);
        object button = GetCollectionItem(children, childCount - 31);
        object textBox = GetCollectionItem(children, childCount - 30);
        object tabControl = GetCollectionItem(children, childCount - 29);
        object listView = GetCollectionItem(children, childCount - 28);
        object treeView = GetCollectionItem(children, childCount - 27);
        object calendar = GetCollectionItem(children, childCount - 26);
        object datePicker = GetCollectionItem(children, childCount - 25);
        object menu = GetCollectionItem(children, childCount - 24);
        object toolBarTray = GetCollectionItem(children, childCount - 23);
        object statusBar = GetCollectionItem(children, childCount - 22);
        object checkBox = GetCollectionItem(children, childCount - 21);
        object radioButton = GetCollectionItem(children, childCount - 20);
        object toggleButton = GetCollectionItem(children, childCount - 19);
        object repeatButton = GetCollectionItem(children, childCount - 18);
        object expander = GetCollectionItem(children, childCount - 17);
        object groupBox = GetCollectionItem(children, childCount - 16);
        object scrollViewer = GetCollectionItem(children, childCount - 15);
        object scrollBar = GetCollectionItem(children, childCount - 14);
        object defaultButton = GetCollectionItem(children, childCount - 13);
        object itemsControl = GetCollectionItem(children, childCount - 12);
        object listBox = GetCollectionItem(children, childCount - 11);
        object label = GetCollectionItem(children, childCount - 10);
        object separator = GetCollectionItem(children, childCount - 9);
        object gridSplitter = GetCollectionItem(children, childCount - 8);
        object resizeGrip = GetCollectionItem(children, childCount - 7);
        object thumb = GetCollectionItem(children, childCount - 6);
        object comboBox = GetCollectionItem(children, childCount - 5);
        object passwordBox = GetCollectionItem(children, childCount - 4);
        object slider = GetCollectionItem(children, childCount - 3);
        object progressBar = GetCollectionItem(children, childCount - 2);
        object dataGrid = GetCollectionItem(children, childCount - 1);
        object richTextBox = Invoke(window, "FindName", "DocumentBox");
        object buttonContextMenu = GetProperty(button, "ContextMenu");
        object buttonToolTip = GetProperty(button, "ToolTip");
        object themedToolBar = GetCollectionItem(GetProperty(toolBarTray, "ToolBars"), 0);
        Type menuItemType = GetRequiredType(menu.GetType().Assembly, "System.Windows.Controls.MenuItem");
        Type toolBarType = GetRequiredType(themedToolBar.GetType().Assembly, "System.Windows.Controls.ToolBar");
        DateTime themeDate = new(2026, 1, 7);
        AssertType(richTextBox, "System.Windows.Controls.RichTextBox", "compiled themed RichTextBox");
        AssertType(buttonContextMenu, "System.Windows.Controls.ContextMenu", "created themed ContextMenu");
        AssertType(buttonToolTip, "System.Windows.Controls.ToolTip", "created themed ToolTip");
        AssertType(textBox, "System.Windows.Controls.TextBox", "created themed TextBox");
        AssertType(tabControl, "System.Windows.Controls.TabControl", "created themed TabControl");
        AssertType(listView, "System.Windows.Controls.ListView", "created themed ListView");
        AssertType(treeView, "System.Windows.Controls.TreeView", "created themed TreeView");
        AssertType(calendar, "System.Windows.Controls.Calendar", "created themed Calendar");
        AssertType(datePicker, "System.Windows.Controls.DatePicker", "created themed DatePicker");
        AssertType(menu, "System.Windows.Controls.Menu", "created themed Menu");
        AssertType(toolBarTray, "System.Windows.Controls.ToolBarTray", "created themed ToolBarTray");
        AssertType(statusBar, "System.Windows.Controls.Primitives.StatusBar", "created themed StatusBar");
        AssertType(checkBox, "System.Windows.Controls.CheckBox", "created themed CheckBox");
        AssertType(radioButton, "System.Windows.Controls.RadioButton", "created themed RadioButton");
        AssertType(toggleButton, "System.Windows.Controls.Primitives.ToggleButton", "created themed ToggleButton");
        AssertType(repeatButton, "System.Windows.Controls.Primitives.RepeatButton", "created themed RepeatButton");
        AssertType(expander, "System.Windows.Controls.Expander", "created themed Expander");
        AssertType(groupBox, "System.Windows.Controls.GroupBox", "created themed GroupBox");
        AssertType(scrollViewer, "System.Windows.Controls.ScrollViewer", "created themed ScrollViewer");
        AssertType(scrollBar, "System.Windows.Controls.Primitives.ScrollBar", "created themed ScrollBar");
        AssertType(defaultButton, "System.Windows.Controls.Button", "created themed default Button");
        AssertType(itemsControl, "System.Windows.Controls.ItemsControl", "created themed ItemsControl");
        AssertType(listBox, "System.Windows.Controls.ListBox", "created themed ListBox");
        AssertType(label, "System.Windows.Controls.Label", "created themed Label");
        AssertType(separator, "System.Windows.Controls.Separator", "created themed Separator");
        AssertType(gridSplitter, "System.Windows.Controls.GridSplitter", "created themed GridSplitter");
        AssertType(resizeGrip, "System.Windows.Controls.Primitives.ResizeGrip", "created themed ResizeGrip");
        AssertType(thumb, "System.Windows.Controls.Primitives.Thumb", "created themed Thumb");
        AssertType(comboBox, "System.Windows.Controls.ComboBox", "created themed ComboBox");
        AssertType(passwordBox, "System.Windows.Controls.PasswordBox", "created themed PasswordBox");
        AssertType(slider, "System.Windows.Controls.Slider", "created themed Slider");
        AssertType(progressBar, "System.Windows.Controls.ProgressBar", "created themed ProgressBar");
        AssertType(dataGrid, "System.Windows.Controls.DataGrid", "created themed DataGrid");

        AssertType(GetDictionaryValue(themeDictionary, "DefaultWindowStyle"), "System.Windows.Style", "DefaultWindowStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultButtonStyle"), "System.Windows.Style", "DefaultButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "AccentButtonStyle"), "System.Windows.Style", "AccentButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultCalendarStyle"), "System.Windows.Style", "DefaultCalendarStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultCalendarButtonStyle"), "System.Windows.Style", "DefaultCalendarButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultCalendarDayButtonStyle"), "System.Windows.Style", "DefaultCalendarDayButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultCalendarItemStyle"), "System.Windows.Style", "DefaultCalendarItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultCheckBoxStyle"), "System.Windows.Style", "DefaultCheckBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultComboBoxStyle"), "System.Windows.Style", "DefaultComboBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultContextMenuStyle"), "System.Windows.Style", "DefaultContextMenuStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultComboBoxItemStyle"), "System.Windows.Style", "DefaultComboBoxItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultComboBoxTextBoxStyle"), "System.Windows.Style", "DefaultComboBoxTextBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultComboBoxToggleButtonStyle"), "System.Windows.Style", "DefaultComboBoxToggleButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultComboBoxTemplate"), "System.Windows.Controls.ControlTemplate", "DefaultComboBoxTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "EditableComboBoxTemplate"), "System.Windows.Controls.ControlTemplate", "EditableComboBoxTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDataGridStyle"), "System.Windows.Style", "DefaultDataGridStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDataGridCellStyle"), "System.Windows.Style", "DefaultDataGridCellStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DataGridCheckBoxElementDefaultStyle"), "System.Windows.Style", "DataGridCheckBoxElementDefaultStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DataGridCheckBoxEditingElementDefaultStyle"), "System.Windows.Style", "DataGridCheckBoxEditingElementDefaultStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDataGridColumnFloatingHeaderStyle"), "System.Windows.Style", "DefaultDataGridColumnFloatingHeaderStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDataGridHeaderDropSeparatorStyle"), "System.Windows.Style", "DefaultDataGridHeaderDropSeparatorStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDataGridColumnHeaderStyle"), "System.Windows.Style", "DefaultDataGridColumnHeaderStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDataGridColumnHeadersPresenterStyle"), "System.Windows.Style", "DefaultDataGridColumnHeadersPresenterStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDataGridCellsPresenterStyle"), "System.Windows.Style", "DefaultDataGridCellsPresenterStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDataGridRowHeaderStyle"), "System.Windows.Style", "DefaultDataGridRowHeaderStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDataGridRowStyle"), "System.Windows.Style", "DefaultDataGridRowStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDatePickerStyle"), "System.Windows.Style", "DefaultDatePickerStyle");
        object datePickerCalendarStyle = GetDictionaryValue(themeDictionary, "DatePickerCalendarStyle");
        AssertType(datePickerCalendarStyle, "System.Windows.Style", "DatePickerCalendarStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultDatePickerTextBoxStyle"), "System.Windows.Style", "DefaultDatePickerTextBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultExpanderStyle"), "System.Windows.Style", "DefaultExpanderStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultExpanderToggleButtonDownStyle"), "System.Windows.Controls.ControlTemplate", "DefaultExpanderToggleButtonDownStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultGridSplitterStyle"), "System.Windows.Style", "DefaultGridSplitterStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultGroupBoxStyle"), "System.Windows.Style", "DefaultGroupBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultItemsControlStyle"), "System.Windows.Style", "DefaultItemsControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultLabelStyle"), "System.Windows.Style", "DefaultLabelStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultListBoxStyle"), "System.Windows.Style", "DefaultListBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultListBoxItemStyle"), "System.Windows.Style", "DefaultListBoxItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultListViewStyle"), "System.Windows.Style", "DefaultListViewStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultListViewItemStyle"), "System.Windows.Style", "DefaultListViewItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "ListViewTemplate"), "System.Windows.Controls.ControlTemplate", "ListViewTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultMenuStyle"), "System.Windows.Style", "DefaultMenuStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultMenuItemStyle"), "System.Windows.Style", "DefaultMenuItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultPasswordBoxStyle"), "System.Windows.Style", "DefaultPasswordBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultPasswordBoxContextMenu"), "System.Windows.Controls.ContextMenu", "DefaultPasswordBoxContextMenu");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultRadioButtonStyle"), "System.Windows.Style", "DefaultRadioButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultRepeatButtonStyle"), "System.Windows.Style", "DefaultRepeatButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultResizeGripStyle"), "System.Windows.Style", "DefaultResizeGripStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultScrollBarStyle"), "System.Windows.Style", "DefaultScrollBarStyle");
        AssertType(GetDictionaryValue(themeDictionary, "HorizontalScrollBarTemplate"), "System.Windows.Controls.ControlTemplate", "HorizontalScrollBarTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "VerticalScrollBarTemplate"), "System.Windows.Controls.ControlTemplate", "VerticalScrollBarTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultScrollViewerStyle"), "System.Windows.Style", "DefaultScrollViewerStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultSeparatorStyle"), "System.Windows.Style", "DefaultSeparatorStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultStatusBarItemStyle"), "System.Windows.Style", "DefaultStatusBarItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTabControlStyle"), "System.Windows.Style", "DefaultTabControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTabItemStyle"), "System.Windows.Style", "DefaultTabItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTopTabControlStyle"), "System.Windows.Controls.ControlTemplate", "DefaultTopTabControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultBottomTabControlStyle"), "System.Windows.Controls.ControlTemplate", "DefaultBottomTabControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultLeftTabControlStyle"), "System.Windows.Controls.ControlTemplate", "DefaultLeftTabControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultRightTabControlStyle"), "System.Windows.Controls.ControlTemplate", "DefaultRightTabControlStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTextBoxStyle"), "System.Windows.Style", "DefaultTextBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTextBoxControlTemplate"), "System.Windows.Controls.ControlTemplate", "DefaultTextBoxControlTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultThumbStyle"), "System.Windows.Style", "DefaultThumbStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultToggleButtonStyle"), "System.Windows.Style", "DefaultToggleButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultToolTipStyle"), "System.Windows.Style", "DefaultToolTipStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTreeViewStyle"), "System.Windows.Style", "DefaultTreeViewStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultTreeViewItemStyle"), "System.Windows.Style", "DefaultTreeViewItemStyle");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultRichTextBoxStyle"), "System.Windows.Style", "DefaultRichTextBoxStyle");
        AssertType(GetDictionaryValue(themeDictionary, GetStaticProperty(menuItemType, "SeparatorStyleKey")), "System.Windows.Style", "MenuItem.SeparatorStyleKey");
        AssertType(GetDictionaryValue(themeDictionary, GetStaticProperty(statusBar.GetType(), "SeparatorStyleKey")), "System.Windows.Style", "StatusBar.SeparatorStyleKey");
        AssertType(GetDictionaryValue(themeDictionary, GetStaticProperty(toolBarType, "ButtonStyleKey")), "System.Windows.Style", "ToolBar.ButtonStyleKey");
        AssertType(GetDictionaryValue(themeDictionary, GetStaticProperty(toolBarType, "ToggleButtonStyleKey")), "System.Windows.Style", "ToolBar.ToggleButtonStyleKey");
        AssertType(GetDictionaryValue(themeDictionary, GetStaticProperty(toolBarType, "SeparatorStyleKey")), "System.Windows.Style", "ToolBar.SeparatorStyleKey");
        AssertType(GetDictionaryValue(themeDictionary, themedToolBar.GetType()), "System.Windows.Style", "implicit ToolBar Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, toolBarTray.GetType()), "System.Windows.Style", "implicit ToolBarTray Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, defaultButton.GetType()), "System.Windows.Style", "implicit Button Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, calendar.GetType()), "System.Windows.Style", "implicit Calendar Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, checkBox.GetType()), "System.Windows.Style", "implicit CheckBox Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, comboBox.GetType()), "System.Windows.Style", "implicit ComboBox Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, buttonContextMenu.GetType()), "System.Windows.Style", "implicit ContextMenu Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, dataGrid.GetType()), "System.Windows.Style", "implicit DataGrid Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, datePicker.GetType()), "System.Windows.Style", "implicit DatePicker Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, expander.GetType()), "System.Windows.Style", "implicit Expander Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, gridSplitter.GetType()), "System.Windows.Style", "implicit GridSplitter Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, groupBox.GetType()), "System.Windows.Style", "implicit GroupBox Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, itemsControl.GetType()), "System.Windows.Style", "implicit ItemsControl Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, label.GetType()), "System.Windows.Style", "implicit Label Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, listBox.GetType()), "System.Windows.Style", "implicit ListBox Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, GetCollectionItem(GetProperty(listBox, "Items"), 0).GetType()), "System.Windows.Style", "implicit ListBoxItem Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, listView.GetType()), "System.Windows.Style", "implicit ListView Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, menu.GetType()), "System.Windows.Style", "implicit Menu Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, passwordBox.GetType()), "System.Windows.Style", "implicit PasswordBox Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, radioButton.GetType()), "System.Windows.Style", "implicit RadioButton Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, repeatButton.GetType()), "System.Windows.Style", "implicit RepeatButton Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, resizeGrip.GetType()), "System.Windows.Style", "implicit ResizeGrip Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, scrollBar.GetType()), "System.Windows.Style", "implicit ScrollBar Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, scrollViewer.GetType()), "System.Windows.Style", "implicit ScrollViewer Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, separator.GetType()), "System.Windows.Style", "implicit Separator Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, statusBar.GetType()), "System.Windows.Style", "implicit StatusBar Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, slider.GetType()), "System.Windows.Style", "implicit Slider Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, tabControl.GetType()), "System.Windows.Style", "implicit TabControl Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, thumb.GetType()), "System.Windows.Style", "implicit Thumb Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, toggleButton.GetType()), "System.Windows.Style", "implicit ToggleButton Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, buttonToolTip.GetType()), "System.Windows.Style", "implicit ToolTip Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, treeView.GetType()), "System.Windows.Style", "implicit TreeView Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, progressBar.GetType()), "System.Windows.Style", "implicit ProgressBar Fluent style");
        AssertType(GetDictionaryValue(themeDictionary, "HorizontalSliderTemplate"), "System.Windows.Controls.ControlTemplate", "HorizontalSliderTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "VerticalSliderTemplate"), "System.Windows.Controls.ControlTemplate", "VerticalSliderTemplate");
        AssertType(GetDictionaryValue(themeDictionary, "SliderThumbStyle"), "System.Windows.Style", "SliderThumbStyle");
        AssertType(GetDictionaryValue(themeDictionary, "SliderButtonStyle"), "System.Windows.Style", "SliderButtonStyle");
        AssertType(GetDictionaryValue(themeDictionary, "WindowTemplateKey"), "System.Windows.Controls.ControlTemplate", "WindowTemplateKey");
        AssertType(GetDictionaryValue(themeDictionary, "DefaultControlContextMenu"), "System.Windows.Controls.ContextMenu", "DefaultControlContextMenu");

        AssertStyleTarget(GetProperty(window, "Style"), "System.Windows.Window", "Window Fluent style target");
        AssertStyleTarget(GetProperty(button, "Style"), "System.Windows.Controls.Button", "Button Fluent style target");
        AssertStyleTarget(GetProperty(buttonContextMenu, "Style"), "System.Windows.Controls.ContextMenu", "ContextMenu Fluent style target");
        AssertStyleTarget(GetProperty(buttonToolTip, "Style"), "System.Windows.Controls.ToolTip", "ToolTip Fluent style target");
        AssertStyleTarget(GetProperty(textBox, "Style"), "System.Windows.Controls.TextBox", "TextBox Fluent style target");
        AssertStyleTarget(GetProperty(tabControl, "Style"), "System.Windows.Controls.TabControl", "TabControl Fluent style target");
        AssertStyleTarget(GetProperty(listView, "Style"), "System.Windows.Controls.ListView", "ListView Fluent style target");
        AssertStyleTarget(GetProperty(treeView, "Style"), "System.Windows.Controls.TreeView", "TreeView Fluent style target");
        AssertStyleTarget(GetProperty(calendar, "Style"), "System.Windows.Controls.Calendar", "Calendar Fluent style target");
        AssertStyleTarget(GetProperty(datePicker, "Style"), "System.Windows.Controls.DatePicker", "DatePicker Fluent style target");
        AssertStyleTarget(GetProperty(menu, "Style"), "System.Windows.Controls.Menu", "Menu Fluent style target");
        AssertStyleTarget(GetProperty(GetCollectionItem(GetProperty(menu, "Items"), 0), "Style"), "System.Windows.Controls.MenuItem", "MenuItem Fluent style target");
        AssertStyleTarget(GetProperty(GetCollectionItem(GetProperty(GetCollectionItem(GetProperty(menu, "Items"), 0), "Items"), 1), "Style"), "System.Windows.Controls.Separator", "Menu separator Fluent style target");
        AssertStyleTarget(GetProperty(themedToolBar, "Style"), "System.Windows.Controls.ToolBar", "ToolBar Fluent style target");
        AssertStyleTarget(GetProperty(GetCollectionItem(GetProperty(themedToolBar, "Items"), 0), "Style"), "System.Windows.Controls.Button", "ToolBar button Fluent style target");
        AssertStyleTarget(GetProperty(GetCollectionItem(GetProperty(themedToolBar, "Items"), 1), "Style"), "System.Windows.Controls.Primitives.ToggleButton", "ToolBar toggle Fluent style target");
        AssertStyleTarget(GetProperty(GetCollectionItem(GetProperty(themedToolBar, "Items"), 2), "Style"), "System.Windows.Controls.Separator", "ToolBar separator Fluent style target");
        AssertStyleTarget(GetProperty(statusBar, "Style"), "System.Windows.Controls.Primitives.StatusBar", "StatusBar Fluent style target");
        AssertStyleTarget(GetProperty(GetCollectionItem(GetProperty(statusBar, "Items"), 0), "Style"), "System.Windows.Controls.Primitives.StatusBarItem", "StatusBarItem Fluent style target");
        AssertStyleTarget(GetProperty(GetCollectionItem(GetProperty(statusBar, "Items"), 1), "Style"), "System.Windows.Controls.Separator", "StatusBar separator Fluent style target");
        AssertStyleTarget(GetProperty(checkBox, "Style"), "System.Windows.Controls.CheckBox", "CheckBox Fluent style target");
        AssertStyleTarget(GetProperty(radioButton, "Style"), "System.Windows.Controls.RadioButton", "RadioButton Fluent style target");
        AssertStyleTarget(GetProperty(toggleButton, "Style"), "System.Windows.Controls.Primitives.ToggleButton", "ToggleButton Fluent style target");
        AssertStyleTarget(GetProperty(repeatButton, "Style"), "System.Windows.Controls.Primitives.RepeatButton", "RepeatButton Fluent style target");
        AssertStyleTarget(GetProperty(expander, "Style"), "System.Windows.Controls.Expander", "Expander Fluent style target");
        AssertStyleTarget(GetProperty(groupBox, "Style"), "System.Windows.Controls.GroupBox", "GroupBox Fluent style target");
        AssertStyleTarget(GetProperty(scrollViewer, "Style"), "System.Windows.Controls.ScrollViewer", "ScrollViewer Fluent style target");
        AssertStyleTarget(GetProperty(scrollBar, "Style"), "System.Windows.Controls.Primitives.ScrollBar", "ScrollBar Fluent style target");
        AssertStyleTarget(GetProperty(defaultButton, "Style"), "System.Windows.Controls.Primitives.ButtonBase", "Default Button Fluent style target");
        AssertStyleTarget(GetProperty(itemsControl, "Style"), "System.Windows.Controls.ItemsControl", "ItemsControl Fluent style target");
        AssertStyleTarget(GetProperty(listBox, "Style"), "System.Windows.Controls.ListBox", "ListBox Fluent style target");
        AssertStyleTarget(GetProperty(GetCollectionItem(GetProperty(listBox, "Items"), 0), "Style"), "System.Windows.Controls.ListBoxItem", "ListBoxItem Fluent style target");
        AssertStyleTarget(GetProperty(label, "Style"), "System.Windows.Controls.Label", "Label Fluent style target");
        AssertStyleTarget(GetProperty(separator, "Style"), "System.Windows.Controls.Separator", "Separator Fluent style target");
        AssertStyleTarget(GetProperty(gridSplitter, "Style"), "System.Windows.Controls.GridSplitter", "GridSplitter Fluent style target");
        AssertStyleTarget(GetProperty(resizeGrip, "Style"), "System.Windows.Controls.Primitives.ResizeGrip", "ResizeGrip Fluent style target");
        AssertStyleTarget(GetProperty(thumb, "Style"), "System.Windows.Controls.Primitives.Thumb", "Thumb Fluent style target");
        AssertStyleTarget(GetProperty(comboBox, "Style"), "System.Windows.Controls.ComboBox", "ComboBox Fluent style target");
        AssertStyleTarget(GetProperty(passwordBox, "Style"), "System.Windows.Controls.PasswordBox", "PasswordBox Fluent style target");
        AssertStyleTarget(GetProperty(slider, "Style"), "System.Windows.Controls.Slider", "Slider Fluent style target");
        AssertStyleTarget(GetProperty(progressBar, "Style"), "System.Windows.Controls.ProgressBar", "ProgressBar Fluent style target");
        AssertStyleTarget(GetProperty(dataGrid, "Style"), "System.Windows.Controls.DataGrid", "DataGrid Fluent style target");
        AssertStyleTarget(GetProperty(richTextBox, "Style"), "System.Windows.Controls.RichTextBox", "RichTextBox Fluent style target");

        Invoke(window, "ApplyTemplate");
        Invoke(button, "ApplyTemplate");
        Invoke(buttonContextMenu, "ApplyTemplate");
        ApplyItemsTemplates(buttonContextMenu, "themed ContextMenu items");
        Invoke(buttonToolTip, "ApplyTemplate");
        Invoke(textBox, "ApplyTemplate");
        Invoke(tabControl, "ApplyTemplate");
        ApplyItemsTemplates(tabControl, "themed TabControl items");
        Invoke(listView, "ApplyTemplate");
        ApplyItemsTemplates(listView, "themed ListView items");
        Invoke(treeView, "ApplyTemplate");
        ApplyItemsTemplates(treeView, "themed TreeView root items");
        Invoke(calendar, "ApplyTemplate");
        Invoke(datePicker, "ApplyTemplate");
        Invoke(menu, "ApplyTemplate");
        ApplyItemsTemplates(menu, "themed Menu items");
        Invoke(toolBarTray, "ApplyTemplate");
        Invoke(themedToolBar, "ApplyTemplate");
        ApplyItemsTemplates(themedToolBar, "themed ToolBar items");
        Invoke(statusBar, "ApplyTemplate");
        ApplyItemsTemplates(statusBar, "themed StatusBar items");
        Invoke(checkBox, "ApplyTemplate");
        Invoke(radioButton, "ApplyTemplate");
        Invoke(toggleButton, "ApplyTemplate");
        Invoke(repeatButton, "ApplyTemplate");
        Invoke(expander, "ApplyTemplate");
        Invoke(groupBox, "ApplyTemplate");
        Invoke(scrollViewer, "ApplyTemplate");
        Invoke(scrollBar, "ApplyTemplate");
        Invoke(defaultButton, "ApplyTemplate");
        Invoke(itemsControl, "ApplyTemplate");
        Invoke(listBox, "ApplyTemplate");
        ApplyItemsTemplates(listBox, "themed ListBox items");
        Invoke(separator, "ApplyTemplate");
        Invoke(gridSplitter, "ApplyTemplate");
        Invoke(resizeGrip, "ApplyTemplate");
        Invoke(thumb, "ApplyTemplate");
        Invoke(comboBox, "ApplyTemplate");
        Invoke(passwordBox, "ApplyTemplate");
        Invoke(slider, "ApplyTemplate");
        Invoke(progressBar, "ApplyTemplate");
        Invoke(dataGrid, "ApplyTemplate");
        Invoke(richTextBox, "ApplyTemplate");

        AssertType(GetProperty(window, "Template"), "System.Windows.Controls.ControlTemplate", "Window template");
        AssertType(GetProperty(button, "Template"), "System.Windows.Controls.ControlTemplate", "Button template");
        AssertType(GetProperty(buttonContextMenu, "Template"), "System.Windows.Controls.ControlTemplate", "ContextMenu template");
        AssertType(GetProperty(buttonToolTip, "Template"), "System.Windows.Controls.ControlTemplate", "ToolTip template");
        AssertType(GetProperty(textBox, "Template"), "System.Windows.Controls.ControlTemplate", "TextBox template");
        AssertType(GetProperty(tabControl, "Template"), "System.Windows.Controls.ControlTemplate", "TabControl template");
        AssertType(GetProperty(listView, "Template"), "System.Windows.Controls.ControlTemplate", "ListView template");
        AssertType(GetProperty(treeView, "Template"), "System.Windows.Controls.ControlTemplate", "TreeView template");
        AssertType(GetProperty(calendar, "Template"), "System.Windows.Controls.ControlTemplate", "Calendar template");
        AssertType(GetProperty(datePicker, "Template"), "System.Windows.Controls.ControlTemplate", "DatePicker template");
        AssertType(GetProperty(menu, "Template"), "System.Windows.Controls.ControlTemplate", "Menu template");
        AssertType(GetProperty(themedToolBar, "Template"), "System.Windows.Controls.ControlTemplate", "ToolBar template");
        AssertType(GetProperty(checkBox, "Template"), "System.Windows.Controls.ControlTemplate", "CheckBox template");
        AssertType(GetProperty(radioButton, "Template"), "System.Windows.Controls.ControlTemplate", "RadioButton template");
        AssertType(GetProperty(toggleButton, "Template"), "System.Windows.Controls.ControlTemplate", "ToggleButton template");
        AssertType(GetProperty(repeatButton, "Template"), "System.Windows.Controls.ControlTemplate", "RepeatButton template");
        AssertType(GetProperty(expander, "Template"), "System.Windows.Controls.ControlTemplate", "Expander template");
        AssertType(GetProperty(groupBox, "Template"), "System.Windows.Controls.ControlTemplate", "GroupBox template");
        AssertType(GetProperty(scrollViewer, "Template"), "System.Windows.Controls.ControlTemplate", "ScrollViewer template");
        AssertType(GetProperty(scrollBar, "Template"), "System.Windows.Controls.ControlTemplate", "ScrollBar template");
        AssertType(GetProperty(defaultButton, "Template"), "System.Windows.Controls.ControlTemplate", "Default Button template");
        AssertType(GetProperty(itemsControl, "Template"), "System.Windows.Controls.ControlTemplate", "ItemsControl template");
        AssertType(GetProperty(listBox, "Template"), "System.Windows.Controls.ControlTemplate", "ListBox template");
        AssertType(GetProperty(separator, "Template"), "System.Windows.Controls.ControlTemplate", "Separator template");
        AssertType(GetProperty(gridSplitter, "Template"), "System.Windows.Controls.ControlTemplate", "GridSplitter template");
        AssertType(GetProperty(resizeGrip, "Template"), "System.Windows.Controls.ControlTemplate", "ResizeGrip template");
        AssertType(GetProperty(thumb, "Template"), "System.Windows.Controls.ControlTemplate", "Thumb template");
        AssertType(GetProperty(comboBox, "Template"), "System.Windows.Controls.ControlTemplate", "ComboBox template");
        AssertType(GetProperty(passwordBox, "Template"), "System.Windows.Controls.ControlTemplate", "PasswordBox template");
        AssertType(GetProperty(slider, "Template"), "System.Windows.Controls.ControlTemplate", "Slider template");
        AssertType(GetProperty(progressBar, "Template"), "System.Windows.Controls.ControlTemplate", "ProgressBar template");
        AssertType(GetProperty(dataGrid, "Template"), "System.Windows.Controls.ControlTemplate", "DataGrid template");
        AssertType(GetProperty(richTextBox, "Template"), "System.Windows.Controls.ControlTemplate", "RichTextBox template");
        AssertStyleHasSetter(GetProperty(tabControl, "Style"), "Template", "TabControl Fluent template setter");
        AssertStyleHasSetter(GetProperty(listView, "Style"), "Template", "ListView Fluent template setter");
        AssertStyleHasSetter(GetProperty(treeView, "Style"), "Template", "TreeView Fluent template setter");
        AssertStyleHasSetter(GetProperty(calendar, "Style"), "Template", "Calendar Fluent template setter");
        AssertStyleHasSetter(GetProperty(datePicker, "Style"), "Template", "DatePicker Fluent template setter");
        AssertStyleHasSetter(GetProperty(datePicker, "Style"), "CalendarStyle", "DatePicker Fluent calendar-style setter");
        AssertStyleHasSetter(GetProperty(menu, "Style"), "Template", "Menu Fluent template setter");
        AssertStyleHasSetter(GetProperty(buttonContextMenu, "Style"), "Template", "ContextMenu Fluent template setter");
        AssertStyleHasSetter(GetProperty(buttonToolTip, "Style"), "Template", "ToolTip Fluent template setter");
        AssertStyleHasSetter(GetProperty(themedToolBar, "Style"), "Template", "ToolBar Fluent template setter");
        AssertStyleHasSetter(GetProperty(GetCollectionItem(GetProperty(statusBar, "Items"), 0), "Style"), "Template", "StatusBarItem Fluent template setter");
        AssertStyleHasSetter(GetProperty(checkBox, "Style"), "Template", "CheckBox Fluent template setter");
        AssertStyleHasSetter(GetProperty(radioButton, "Style"), "Template", "RadioButton Fluent template setter");
        AssertStyleHasSetter(GetProperty(toggleButton, "Style"), "Template", "ToggleButton Fluent template setter");
        AssertStyleHasSetter(GetProperty(repeatButton, "Style"), "Template", "RepeatButton Fluent template setter");
        AssertStyleHasSetter(GetProperty(expander, "Style"), "Template", "Expander Fluent template setter");
        AssertStyleHasSetter(GetProperty(groupBox, "Style"), "Template", "GroupBox Fluent template setter");
        AssertStyleHasSetter(GetProperty(scrollViewer, "Style"), "Template", "ScrollViewer Fluent template setter");
        AssertStyleHasTriggerSetter(GetProperty(scrollBar, "Style"), "Orientation", "Horizontal", "Template", "ScrollBar Fluent horizontal template trigger");
        AssertStyleHasTriggerSetter(GetProperty(scrollBar, "Style"), "Orientation", "Vertical", "Template", "ScrollBar Fluent vertical template trigger");
        AssertStyleHasSetter(GetProperty(defaultButton, "Style"), "Template", "Default Button Fluent template setter");
        AssertStyleHasSetter(GetProperty(itemsControl, "Style"), "Template", "ItemsControl Fluent template setter");
        AssertStyleHasSetter(GetProperty(listBox, "Style"), "Template", "ListBox Fluent template setter");
        AssertStyleHasSetter(GetProperty(GetCollectionItem(GetProperty(listBox, "Items"), 0), "Style"), "Template", "ListBoxItem Fluent template setter");
        AssertStyleHasSetter(GetProperty(separator, "Style"), "Template", "Separator Fluent template setter");
        AssertStyleHasSetter(GetProperty(gridSplitter, "Style"), "Template", "GridSplitter Fluent template setter");
        AssertStyleHasSetter(GetProperty(resizeGrip, "Style"), "Template", "ResizeGrip Fluent template setter");
        AssertStyleHasSetter(GetProperty(thumb, "Style"), "Template", "Thumb Fluent template setter");
        AssertStyleHasSetter(GetProperty(comboBox, "Style"), "Template", "ComboBox Fluent template setter");
        AssertStyleHasSetter(GetProperty(passwordBox, "Style"), "Template", "PasswordBox Fluent template setter");
        AssertStyleHasSetter(GetProperty(textBox, "Style"), "Template", "TextBox Fluent template setter");
        AssertStyleHasSetter(GetProperty(progressBar, "Style"), "Template", "ProgressBar Fluent template setter");
        AssertStyleHasSetter(GetProperty(dataGrid, "Style"), "Template", "DataGrid Fluent template setter");
        AssertStyleHasSetter(GetProperty(dataGrid, "Style"), "RowStyle", "DataGrid Fluent row-style setter");
        AssertStyleHasSetter(GetProperty(dataGrid, "Style"), "RowHeaderStyle", "DataGrid Fluent row-header-style setter");
        AssertStyleHasSetter(GetProperty(dataGrid, "Style"), "CellStyle", "DataGrid Fluent cell-style setter");
        AssertStyleHasSetter(GetProperty(dataGrid, "Style"), "ColumnHeaderStyle", "DataGrid Fluent column-header-style setter");
        AssertStyleHasSetter(GetProperty(dataGrid, "Style"), "DropLocationIndicatorStyle", "DataGrid Fluent drop-location-style setter");
        AssertStyleHasSetter(GetProperty(dataGrid, "Style"), "DragIndicatorStyle", "DataGrid Fluent drag-indicator-style setter");
        AssertStyleHasSetter(GetProperty(richTextBox, "Style"), "ContextMenu", "RichTextBox Fluent context-menu setter");
        AssertEqual("themed button smoke", GetProperty(button, "Content"), "themed button content");
        AssertEqual(1, GetCollectionCount(GetProperty(buttonContextMenu, "Items")), "themed ContextMenu item count");
        AssertEqual("Theme context action", GetProperty(GetCollectionItem(GetProperty(buttonContextMenu, "Items"), 0), "Header"), "themed ContextMenu item header");
        AssertEqual("Theme tooltip smoke", GetProperty(buttonToolTip, "Content"), "themed ToolTip content");
        AssertEqual("themed text box smoke", GetProperty(textBox, "Text"), "themed TextBox text");
        AssertEqual(2, GetCollectionCount(GetProperty(tabControl, "Items")), "themed TabControl item count");
        AssertEqual(1, GetProperty(tabControl, "SelectedIndex"), "themed TabControl selected index");
        AssertEqual("Theme tab two", GetProperty(GetCollectionItem(GetProperty(tabControl, "Items"), 1), "Header"), "themed TabItem header");
        AssertEqual(2, GetCollectionCount(GetProperty(listView, "Items")), "themed ListView item count");
        AssertEqual(1, GetProperty(listView, "SelectedIndex"), "themed ListView selected index");
        AssertEqual("Theme list item two", GetProperty(GetCollectionItem(GetProperty(listView, "Items"), 1), "Content"), "themed ListViewItem content");
        AssertEqual(1, GetCollectionCount(GetProperty(treeView, "Items")), "themed TreeView root item count");
        object rootTreeViewItem = GetCollectionItem(GetProperty(treeView, "Items"), 0);
        AssertEqual("Theme tree root", GetProperty(rootTreeViewItem, "Header"), "themed TreeViewItem root header");
        AssertEqual(true, GetProperty(rootTreeViewItem, "IsExpanded"), "themed TreeViewItem expanded state");
        AssertEqual("Theme tree child", GetProperty(GetCollectionItem(GetProperty(rootTreeViewItem, "Items"), 0), "Header"), "themed TreeViewItem child header");
        AssertEqual(themeDate, GetProperty(calendar, "DisplayDate"), "themed Calendar display date");
        AssertEqual(themeDate, GetProperty(calendar, "SelectedDate"), "themed Calendar selected date");
        AssertEqual("Monday", GetProperty(calendar, "FirstDayOfWeek").ToString(), "themed Calendar first day");
        AssertEqual(themeDate, GetProperty(datePicker, "DisplayDate"), "themed DatePicker display date");
        AssertEqual(themeDate, GetProperty(datePicker, "SelectedDate"), "themed DatePicker selected date");
        AssertSame(datePickerCalendarStyle, GetProperty(datePicker, "CalendarStyle"), "themed DatePicker calendar-style dynamic resource");
        AssertEqual(1, GetCollectionCount(GetProperty(menu, "Items")), "themed Menu root item count");
        AssertEqual("_Theme", GetProperty(GetCollectionItem(GetProperty(menu, "Items"), 0), "Header"), "themed MenuItem root header");
        AssertEqual("_Open", GetProperty(GetCollectionItem(GetProperty(GetCollectionItem(GetProperty(menu, "Items"), 0), "Items"), 0), "Header"), "themed MenuItem child header");
        AssertEqual(1, GetCollectionCount(GetProperty(toolBarTray, "ToolBars")), "themed ToolBarTray toolbar count");
        AssertEqual(3, GetCollectionCount(GetProperty(themedToolBar, "Items")), "themed ToolBar item count");
        AssertEqual(true, GetProperty(GetCollectionItem(GetProperty(themedToolBar, "Items"), 1), "IsChecked"), "themed ToolBar toggle checked state");
        AssertEqual(2, GetCollectionCount(GetProperty(statusBar, "Items")), "themed StatusBar item count");
        AssertEqual("Theme status", GetProperty(GetCollectionItem(GetProperty(statusBar, "Items"), 0), "Content"), "themed StatusBarItem content");
        AssertEqual("Theme check", GetProperty(checkBox, "Content"), "themed CheckBox content");
        AssertEqual(true, GetProperty(checkBox, "IsChecked"), "themed CheckBox checked state");
        AssertEqual("Theme radio", GetProperty(radioButton, "Content"), "themed RadioButton content");
        AssertEqual(true, GetProperty(radioButton, "IsChecked"), "themed RadioButton checked state");
        AssertEqual("ThemeChoice", GetProperty(radioButton, "GroupName"), "themed RadioButton group name");
        AssertEqual("Theme toggle", GetProperty(toggleButton, "Content"), "themed ToggleButton content");
        AssertEqual(true, GetProperty(toggleButton, "IsChecked"), "themed ToggleButton checked state");
        AssertEqual("Theme repeat", GetProperty(repeatButton, "Content"), "themed RepeatButton content");
        AssertEqual("Theme expander", GetProperty(expander, "Header"), "themed Expander header");
        AssertEqual("Theme expander content", GetProperty(expander, "Content"), "themed Expander content");
        AssertEqual(true, GetProperty(expander, "IsExpanded"), "themed Expander expanded state");
        AssertEqual("Theme group", GetProperty(groupBox, "Header"), "themed GroupBox header");
        AssertEqual("Theme group content", GetProperty(groupBox, "Content"), "themed GroupBox content");
        AssertEqual("Visible", GetProperty(scrollViewer, "VerticalScrollBarVisibility").ToString(), "themed ScrollViewer vertical visibility");
        AssertEqual("Auto", GetProperty(scrollViewer, "HorizontalScrollBarVisibility").ToString(), "themed ScrollViewer horizontal visibility");
        AssertEqual("Vertical", GetProperty(scrollBar, "Orientation").ToString(), "themed ScrollBar orientation");
        AssertEqual(0.0, GetProperty(scrollBar, "Minimum"), "themed ScrollBar minimum");
        AssertEqual(100.0, GetProperty(scrollBar, "Maximum"), "themed ScrollBar maximum");
        AssertEqual(25.0, GetProperty(scrollBar, "ViewportSize"), "themed ScrollBar viewport size");
        AssertEqual(33.0, GetProperty(scrollBar, "Value"), "themed ScrollBar value");
        AssertEqual("Theme default button", GetProperty(defaultButton, "Content"), "themed default Button content");
        AssertEqual(4, GetCollectionCount(GetProperty(itemsControl, "Items")), "themed ItemsControl item count");
        AssertEqual("Theme items two", GetCollectionItem(GetProperty(itemsControl, "Items"), 1), "themed ItemsControl item content");
        object implicitItemsButton = GetCollectionItem(GetProperty(itemsControl, "Items"), 2);
        object implicitItemsTextBox = GetCollectionItem(GetProperty(itemsControl, "Items"), 3);
        AssertType(implicitItemsButton, "System.Windows.Controls.Button", "runtime implicit themed Button item");
        AssertType(implicitItemsTextBox, "System.Windows.Controls.TextBox", "runtime implicit themed TextBox item");
        AssertEqual("Theme implicit button", GetProperty(implicitItemsButton, "Content"), "runtime implicit themed Button content");
        AssertEqual("Theme implicit text", GetProperty(implicitItemsTextBox, "Text"), "runtime implicit themed TextBox text");
        AssertSame(GetDictionaryValue(themeDictionary, implicitItemsButton.GetType()), GetProperty(implicitItemsButton, "Style"), "runtime implicit Button Fluent style");
        AssertSame(GetDictionaryValue(themeDictionary, implicitItemsTextBox.GetType()), GetProperty(implicitItemsTextBox, "Style"), "runtime implicit TextBox Fluent style");
        AssertEqual(2, GetCollectionCount(GetProperty(listBox, "Items")), "themed ListBox item count");
        AssertEqual(1, GetProperty(listBox, "SelectedIndex"), "themed ListBox selected index");
        AssertEqual("Theme list box two", GetProperty(GetCollectionItem(GetProperty(listBox, "Items"), 1), "Content"), "themed ListBoxItem content");
        AssertEqual("Theme label", GetProperty(label, "Content"), "themed Label content");
        AssertEqual(false, GetProperty(label, "Focusable"), "themed Label focusable");
        AssertEqual(false, GetProperty(separator, "Focusable"), "themed Separator focusable");
        AssertEqual(16.0, GetProperty(gridSplitter, "Width"), "themed GridSplitter width");
        AssertEqual(40.0, GetProperty(gridSplitter, "Height"), "themed GridSplitter height");
        AssertEqual(24.0, GetProperty(thumb, "Width"), "themed Thumb width");
        AssertEqual(12.0, GetProperty(thumb, "Height"), "themed Thumb height");
        AssertEqual(2, GetCollectionCount(GetProperty(comboBox, "Items")), "themed ComboBox item count");
        AssertEqual(1, GetProperty(comboBox, "SelectedIndex"), "themed ComboBox selected index");
        AssertEqual("theme item two", GetProperty(comboBox, "SelectedItem"), "themed ComboBox selected item");
        AssertEqual("theme-secret", GetProperty(passwordBox, "Password"), "themed PasswordBox password");
        AssertEqual(0.0, GetProperty(slider, "Minimum"), "themed Slider minimum");
        AssertEqual(100.0, GetProperty(slider, "Maximum"), "themed Slider maximum");
        AssertEqual(42.0, GetProperty(slider, "Value"), "themed Slider value");
        AssertEqual(64.0, GetProperty(progressBar, "Value"), "themed ProgressBar value");
        AssertEqual(false, GetProperty(dataGrid, "AutoGenerateColumns"), "themed DataGrid auto-generate columns");
        AssertEqual(false, GetProperty(dataGrid, "CanUserAddRows"), "themed DataGrid add rows");
        AssertEqual(true, GetProperty(dataGrid, "CanUserResizeColumns"), "themed DataGrid resize columns");
        AssertEqual("All", GetProperty(dataGrid, "HeadersVisibility").ToString(), "themed DataGrid headers visibility");
        AssertEqual("All", GetProperty(dataGrid, "GridLinesVisibility").ToString(), "themed DataGrid grid lines visibility");
        AssertEqual(2, GetCollectionCount(GetProperty(dataGrid, "Columns")), "themed DataGrid column count");
        object dataGridColumns = GetProperty(dataGrid, "Columns");
        object nameColumn = GetCollectionItem(dataGridColumns, 0);
        object activeColumn = GetCollectionItem(dataGridColumns, 1);
        AssertType(nameColumn, "System.Windows.Controls.DataGridTextColumn", "themed DataGrid text column");
        AssertType(activeColumn, "System.Windows.Controls.DataGridCheckBoxColumn", "themed DataGrid checkbox column");
        AssertEqual("Name", GetProperty(nameColumn, "Header"), "themed DataGrid text column header");
        AssertEqual("Active", GetProperty(activeColumn, "Header"), "themed DataGrid checkbox column header");
        AssertEqual("Name", GetProperty(GetProperty(GetProperty(nameColumn, "Binding"), "Path"), "Path"), "themed DataGrid text binding path");
        AssertEqual("IsActive", GetProperty(GetProperty(GetProperty(activeColumn, "Binding"), "Path"), "Path"), "themed DataGrid checkbox binding path");
        AssertEqual(2, GetCollectionCount(GetProperty(dataGrid, "Items")), "themed DataGrid item count");
        AssertEqual(1, GetProperty(dataGrid, "SelectedIndex"), "themed DataGrid selected index");
        object selectedGridRow = GetProperty(dataGrid, "SelectedItem");
        AssertType(selectedGridRow, typeof(ThemeGridRow).FullName!, "themed DataGrid selected row type");
        AssertEqual("Theme grid two", GetProperty(selectedGridRow, "Name"), "themed DataGrid selected row name");
        AssertEqual(false, GetProperty(selectedGridRow, "IsActive"), "themed DataGrid selected row active");

        object appResources = GetProperty(application, "Resources");
        object mergedDictionaries = GetProperty(appResources, "MergedDictionaries");
        AssertCollectionCount(mergedDictionaries, expectedMinimum: 2, "application merged dictionaries after Fluent merge");
        AssertCollectionContainsSame(mergedDictionaries, themeDictionary, "merged Fluent dictionary");
    }

    private static void ValidateThemedVisualReplay(Assembly proGpuWpf, Assembly windowsBase, object window)
    {
        const uint pixelWidth = 420;
        const uint pixelHeight = 340;

        object content = GetProperty(window, "Content");

        MeasureAndArrange(windowsBase, content, pixelWidth, pixelHeight);

        Type targetType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.ProGpuWpfCompositionTarget");
        MethodInfo createHeadless = targetType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(method => method.Name == "CreateHeadless");
        object?[] createHeadlessParameters = createHeadless.GetParameters().Length == 0
            ? Array.Empty<object?>()
            : new[] { createHeadless.GetParameters()[0].DefaultValue };
        using IDisposable target = (IDisposable)(createHeadless.Invoke(null, createHeadlessParameters)
            ?? throw new InvalidOperationException("CreateHeadless returned null."));
        object replayResult = Invoke(target, "ReplayVisualSubtreeRetained", content, pixelWidth, pixelHeight, null, null);
        object renderData = GetProperty(replayResult, "RenderData");
        object retainedRoot = GetProperty(target, "RetainedWpfVisualRoot");
        object retainedRootChildren = GetProperty(retainedRoot, "Children");

        AssertAtLeast(1, GetProperty(replayResult, "VisualCount"), "Fluent themed visual replay count");
        AssertAtLeast(1, GetProperty(replayResult, "ContentCount"), "Fluent themed visual replay content count");
        AssertAtLeast(1, GetProperty(renderData, "AppliedCount"), "Fluent themed render-data applied commands");
        AssertAtLeast(1, GetProperty(replayResult, "ChildEdgeCount"), "Fluent themed visual child edges");
        AssertAtLeast(1, GetProperty(target, "RetainedVisualBranchCount"), "retained Fluent themed visual branch map");
        AssertAtLeast(1, GetProperty(retainedRootChildren, "Count"), "retained Fluent themed visual root children");
        AssertAtLeast(1, CountRetainedCommands(retainedRoot), "retained Fluent themed ProGPU commands");
    }

    private static void MeasureAndArrange(Assembly windowsBase, object element, double width, double height)
    {
        object availableSize = Create(windowsBase, "System.Windows.Size", width, height);
        object finalRect = Create(windowsBase, "System.Windows.Rect", 0.0, 0.0, width, height);

        Invoke(element, "Measure", availableSize);
        Invoke(element, "Arrange", finalRect);
        Invoke(element, "UpdateLayout");

        AssertPositiveSize(GetProperty(element, "DesiredSize"), "themed content desired size");
        AssertPositiveSize(GetProperty(element, "RenderSize"), "themed content render size");
    }

    private static void ApplyItemsTemplates(object itemsOwner, string description)
    {
        object items = GetProperty(itemsOwner, "Items");
        AssertCollectionCount(items, expectedMinimum: 1, description);

        int count = GetCollectionCount(items);
        for (int i = 0; i < count; i++)
        {
            object item = GetCollectionItem(items, i);
            Invoke(item, "ApplyTemplate");

            object? childItems = GetOptionalProperty(item, "Items");
            if (childItems != null && GetCollectionCount(childItems) > 0)
            {
                ApplyItemsTemplates(item, $"{description} child items");
            }
        }
    }

    private static void RegisterPortableActivation(
        Assembly proGpuWpf,
        Assembly presentationFramework,
        object window,
        out Type activationServiceType,
        out object activation)
    {
        Type portableActivationType = GetRequiredType(proGpuWpf, "System.Windows.Media.ProGPU.WpfPortableWindowActivation");
        MethodInfo registerMethod = portableActivationType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .SingleOrDefault(static method =>
                method.Name == "TryRegisterPresentationFrameworkActivation" &&
                method.GetParameters().Length == 1)
            ?? throw new MissingMethodException(portableActivationType.FullName, "TryRegisterPresentationFrameworkActivation");
        object registered = registerMethod.Invoke(null, new object?[] { null })
            ?? throw new InvalidOperationException("ProGPU portable activation registration returned null.");
        if (!Convert.ToBoolean(registered))
        {
            throw new InvalidOperationException("Failed to register ProGPU portable activation with real PresentationFramework.");
        }

        activationServiceType = GetRequiredType(presentationFramework, PortableWindowActivationServiceTypeName);
        AssertEqual(true, GetStaticProperty(activationServiceType, "IsEnabled"), "portable activation enabled");

        MethodInfo tryActivate = activationServiceType.GetMethod(
            "TryActivate",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(activationServiceType.FullName, "TryActivate");
        object?[] parameters = { window, null };
        if (!Equals(true, tryActivate.Invoke(null, parameters)) || parameters[1] == null)
        {
            throw new InvalidOperationException("Real themed WPF window did not create a portable ProGPU activation.");
        }

        activation = parameters[1]!;
        if (!string.Equals(
                activation.GetType().FullName,
                "System.Windows.Media.ProGPU.WpfPortableWindowActivation",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected a ProGPU activation, got {activation.GetType().FullName}.");
        }

        object host = GetProperty(activation, "Host");
        AssertSame(window, GetProperty(activation, "Window"), "activation window");
        AssertEqual("ProGPU WPF XAML smoke", GetProperty(host, "Title"), "host title");
        AssertEqual(420, GetProperty(host, "Width"), "host width");
        AssertEqual(340, GetProperty(host, "Height"), "host height");
    }

    private static object Create(Assembly assembly, string typeName, params object?[] parameters)
    {
        Type type = GetRequiredType(assembly, typeName);
        return Activator.CreateInstance(type, parameters)
            ?? throw new InvalidOperationException($"Failed to create '{typeName}'.");
    }

    private static Type GetRequiredType(Assembly assembly, string typeName)
    {
        return assembly.GetType(typeName, throwOnError: true)
            ?? throw new TypeLoadException($"Could not load '{typeName}' from '{assembly.FullName}'.");
    }

    private static object GetProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Expected '{instance.GetType().FullName}.{propertyName}' to have a value.");
    }

    private static object GetStaticProperty(Type type, string propertyName)
    {
        return type.GetProperty(
            propertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException($"Expected '{type.FullName}.{propertyName}' to have a value.");
    }

    private static object GetDictionaryValue(object dictionary, object key)
    {
        if (dictionary is IDictionary nonGenericDictionary && nonGenericDictionary.Contains(key))
        {
            return nonGenericDictionary[key]
                ?? throw new InvalidOperationException($"Dictionary key '{key}' had a null value.");
        }

        object value = Invoke(dictionary, "get_Item", key);
        if (value == null)
        {
            throw new InvalidOperationException($"Dictionary key '{key}' had a null value.");
        }

        return value;
    }

    private static object GetCollectionItem(object collection, int index)
    {
        if (collection is IList list)
        {
            return list[index]
                ?? throw new InvalidOperationException($"Collection item {index} had a null value.");
        }

        return Invoke(collection, "get_Item", index);
    }

    private static object Invoke(object instance, string methodName, params object?[] parameters)
    {
        MethodInfo method = instance.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] candidateParameters = candidate.GetParameters();
                return candidateParameters.Length == parameters.Length;
            })
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);

        return method.Invoke(instance, parameters) ?? new object();
    }

    private static object InvokeStatic(Type type, string methodName, params object?[] parameters)
    {
        MethodInfo method = type.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] candidateParameters = candidate.GetParameters();
                return candidateParameters.Length == parameters.Length;
            })
            ?? throw new MissingMethodException(type.FullName, methodName);

        return method.Invoke(null, parameters) ?? new object();
    }

    private static void SetProperty(object instance, string propertyName, object value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, value);
    }

    private static void SetEnumProperty(object instance, string propertyName, string value)
    {
        PropertyInfo property = instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(instance.GetType().FullName, propertyName);
        property.SetValue(instance, Enum.Parse(property.PropertyType, value));
    }

    private static void AddToCollection(object collection, object item)
    {
        MethodInfo add = collection.GetType().GetMethod(
            "Add",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { item.GetType() },
            modifiers: null)
            ?? collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                    method.Name == "Add" &&
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType.IsAssignableFrom(item.GetType()))
            ?? throw new MissingMethodException(collection.GetType().FullName, "Add");
        add.Invoke(collection, new[] { item });
    }

    private static void AssertCollectionCount(object collection, int expectedMinimum, string description)
    {
        int count = GetCollectionCount(collection);
        if (count < expectedMinimum)
        {
            throw new InvalidOperationException($"Expected {description} to contain at least {expectedMinimum} items, got {count}.");
        }
    }

    private static int GetCollectionCount(object collection)
    {
        object countValue =
            collection is Array array ? array.Length :
            collection is ICollection nonGenericCollection ? nonGenericCollection.Count :
            GetProperty(collection, "Count");

        return Convert.ToInt32(countValue);
    }

    private static void AssertStyleTarget(object style, string expectedTargetTypeName, string description)
    {
        object targetType = GetProperty(style, "TargetType");
        AssertEqual(expectedTargetTypeName, targetType.ToString(), description);
    }

    private static void AssertStyleBasedOn(object style, object expectedBasedOn, string description)
    {
        AssertSame(expectedBasedOn, GetProperty(style, "BasedOn"), description);
    }

    private static void AssertStyleHasSetter(object style, string dependencyPropertyName, string description)
    {
        object setters = GetProperty(style, "Setters");
        if (setters is not IEnumerable enumerable)
        {
            throw new InvalidOperationException($"Expected {description} to expose enumerable setters.");
        }

        foreach (object setterBase in enumerable)
        {
            if (!string.Equals(setterBase.GetType().FullName, "System.Windows.Setter", StringComparison.Ordinal))
            {
                continue;
            }

            object property = GetProperty(setterBase, "Property");
            if (string.Equals(GetProperty(property, "Name").ToString(), dependencyPropertyName, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidOperationException($"Expected {description} to include a '{dependencyPropertyName}' setter.");
    }

    private static void AssertStyleHasTriggerSetter(
        object style,
        string triggerPropertyName,
        string triggerValue,
        string setterPropertyName,
        string description)
    {
        object triggers = GetProperty(style, "Triggers");
        if (triggers is not IEnumerable enumerable)
        {
            throw new InvalidOperationException($"Expected {description} to expose enumerable triggers.");
        }

        foreach (object triggerBase in enumerable)
        {
            if (!string.Equals(triggerBase.GetType().FullName, "System.Windows.Trigger", StringComparison.Ordinal))
            {
                continue;
            }

            object triggerProperty = GetProperty(triggerBase, "Property");
            object value = GetProperty(triggerBase, "Value");
            if (!string.Equals(GetProperty(triggerProperty, "Name").ToString(), triggerPropertyName, StringComparison.Ordinal) ||
                !string.Equals(value.ToString(), triggerValue, StringComparison.Ordinal))
            {
                continue;
            }

            object setters = GetProperty(triggerBase, "Setters");
            if (setters is not IEnumerable setterEnumerable)
            {
                throw new InvalidOperationException($"Expected {description} to expose enumerable trigger setters.");
            }

            foreach (object setterBase in setterEnumerable)
            {
                if (!string.Equals(setterBase.GetType().FullName, "System.Windows.Setter", StringComparison.Ordinal))
                {
                    continue;
                }

                object setterProperty = GetProperty(setterBase, "Property");
                if (string.Equals(GetProperty(setterProperty, "Name").ToString(), setterPropertyName, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException(
            $"Expected {description} to include a '{setterPropertyName}' setter under {triggerPropertyName}={triggerValue}.");
    }

    private static void AssertType(object instance, string expectedFullName, string description)
    {
        if (!string.Equals(instance.GetType().FullName, expectedFullName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected {description} to be '{expectedFullName}', got '{instance.GetType().FullName}'.");
        }
    }

    private static void AssertPositiveSize(object size, string description)
    {
        double width = Convert.ToDouble(GetProperty(size, "Width"));
        double height = Convert.ToDouble(GetProperty(size, "Height"));
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be positive, got {width}x{height}.");
        }
    }

    private static void AssertAtLeast(int expectedMinimum, object actual, string description)
    {
        int actualValue = Convert.ToInt32(actual);
        if (actualValue < expectedMinimum)
        {
            throw new InvalidOperationException(
                $"Expected {description} to be at least {expectedMinimum}, got {actualValue}.");
        }
    }

    private static int CountRetainedCommands(object visual)
    {
        return CountRetainedCommands(visual, new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static int CountRetainedCommands(object visual, ISet<object> visited)
    {
        if (!visited.Add(visual))
        {
            return 0;
        }

        int count = GetRetainedCommandCount(visual);
        PropertyInfo? childrenProperty = visual.GetType().GetProperty(
            "Children",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (childrenProperty?.GetValue(visual) is IEnumerable children)
        {
            foreach (object? child in children)
            {
                if (child != null)
                {
                    count += CountRetainedCommands(child, visited);
                }
            }
        }

        return count;
    }

    private static int GetRetainedCommandCount(object visual)
    {
        PropertyInfo? contextProperty = visual.GetType().GetProperty(
            "Context",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? context = contextProperty?.GetValue(visual);
        if (context == null)
        {
            return 0;
        }

        PropertyInfo? commandsProperty = context.GetType().GetProperty(
            "Commands",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? commands = commandsProperty?.GetValue(context);
        if (commands is ICollection nonGenericCollection)
        {
            return nonGenericCollection.Count;
        }

        object? count = commands == null ? null : GetOptionalProperty(commands, "Count");
        return count == null ? 0 : Convert.ToInt32(count);
    }

    private static object? GetOptionalProperty(object instance, string propertyName)
    {
        return instance.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
    }

    private static void AssertSame(object expected, object actual, string description)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to reference the same object.");
        }
    }

    private static void AssertCollectionContainsSame(object collection, object expected, string description)
    {
        if (collection is IEnumerable items)
        {
            foreach (object? item in items)
            {
                if (ReferenceEquals(expected, item))
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException($"Expected {description} to be present in the collection.");
    }

    private static void AssertEqual(object? expected, object? actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be '{expected}', got '{actual}'.");
        }
    }

    private static string FindArtifactAssembly(string repoRoot, string assemblyName)
    {
        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", assemblyName);
        if (!Directory.Exists(artifactsRoot))
        {
            throw new DirectoryNotFoundException($"Artifacts directory was not found: {artifactsRoot}");
        }

        string[] candidates = Directory.GetFiles(
            artifactsRoot,
            $"{assemblyName}.dll",
            SearchOption.AllDirectories);

        string? selected = candidates
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return selected
            ?? throw new FileNotFoundException($"Could not locate a net10.0 {assemblyName}.dll artifact.", artifactsRoot);
    }

    private static string? TryFindArtifactAssembly(string repoRoot, AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
        {
            return null;
        }

        string artifactsRoot = Path.Combine(repoRoot, "artifacts", "bin", assemblyName.Name);
        if (!Directory.Exists(artifactsRoot))
        {
            return null;
        }

        return Directory
            .GetFiles(artifactsRoot, $"{assemblyName.Name}.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string FindOutputAssembly(string assemblyName)
    {
        string outputAssemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            $"{assemblyName}.dll");
        return File.Exists(outputAssemblyPath)
            ? outputAssemblyPath
            : throw new FileNotFoundException($"Could not locate {assemblyName}.dll beside the theme runtime harness.", outputAssemblyPath);
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            string marker = Path.Combine(
                directory.FullName,
                "src",
                "Microsoft.DotNet.Wpf",
                "src",
                "PresentationFramework",
                "PresentationFramework.csproj");

            if (File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the WPF repository root.");
    }

    public sealed class ThemeGridRow
    {
        public ThemeGridRow(string name, bool isActive)
        {
            Name = name;
            IsActive = isActive;
        }

        public string Name { get; set; }

        public bool IsActive { get; set; }
    }

    private sealed class WpfAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _repoRoot;
        private readonly string _presentationFrameworkPath;
        private readonly string _presentationCorePath;
        private readonly string _compilerHarnessPath;
        private readonly string _fluentThemePath;
        private readonly string _proGpuWpfPath;
        private readonly string _proGpuWpfInteropPath;
        private readonly AssemblyDependencyResolver _resolver;

        public WpfAssemblyLoadContext(
            string repoRoot,
            string presentationFrameworkPath,
            string presentationCorePath,
            string compilerHarnessPath,
            string fluentThemePath,
            string proGpuWpfPath,
            string proGpuWpfInteropPath)
            : base(isCollectible: true)
        {
            _repoRoot = repoRoot;
            _presentationFrameworkPath = presentationFrameworkPath;
            _presentationCorePath = presentationCorePath;
            _compilerHarnessPath = compilerHarnessPath;
            _fluentThemePath = fluentThemePath;
            _proGpuWpfPath = proGpuWpfPath;
            _proGpuWpfInteropPath = proGpuWpfInteropPath;
            _resolver = new AssemblyDependencyResolver(fluentThemePath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (string.Equals(assemblyName.Name, CompilerHarnessAssemblyName, StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_compilerHarnessPath);
            }

            if (string.Equals(assemblyName.Name, FluentThemeAssemblyName, StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_fluentThemePath);
            }

            if (string.Equals(assemblyName.Name, ProGpuWpfAssemblyName, StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_proGpuWpfPath);
            }

            if (string.Equals(assemblyName.Name, ProGpuWpfInteropAssemblyName, StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_proGpuWpfInteropPath);
            }

            if (string.Equals(assemblyName.Name, "PresentationFramework", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationFrameworkPath);
            }

            if (string.Equals(assemblyName.Name, "PresentationCore", StringComparison.Ordinal))
            {
                return LoadFromAssemblyPath(_presentationCorePath);
            }

            string? artifactAssemblyPath = TryFindArtifactAssembly(_repoRoot, assemblyName);
            if (artifactAssemblyPath != null)
            {
                return LoadFromAssemblyPath(artifactAssemblyPath);
            }

            string outputAssemblyPath = Path.Combine(
                AppContext.BaseDirectory,
                $"{assemblyName.Name}.dll");
            if (File.Exists(outputAssemblyPath))
            {
                return LoadFromAssemblyPath(outputAssemblyPath);
            }

            string? resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return resolvedPath == null ? null : LoadFromAssemblyPath(resolvedPath);
        }
    }
}

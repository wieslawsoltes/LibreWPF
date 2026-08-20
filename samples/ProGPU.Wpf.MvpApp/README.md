# LibreWPF MVP App

This is the first runnable no-source-change MVP app for the custom LibreWPF SDK path. It uses regular WPF XAML and code-behind with:

- `Project Sdk="LibreWPF.Sdk/0.1.0-preview.43"`
- `TargetFramework=net10.0-windows`, matching the normal WPF project shape
- normal `App.xaml` / `MainWindow.xaml` with compiled startup/exit handlers, `ShutdownMode`, `Application.Properties`, and compiled merged resource dictionaries
- WPF app resources, assembly `;component` merged dictionaries, `Themes/Generic.xaml` default-style lookup, scoped implicit styles, local dynamic-resource invalidation, `App.config` settings through `ConfigurationManager`, `Application.LoadComponent`, runtime namescope mutation, loose `XamlReader`/`XamlWriter`, dispatcher invoke/post operations, app-defined `MarkupExtension`, app-defined `TypeConverter`, `x:Shared`, Freezable brush lifecycle, `ComponentResourceKey`, localization metadata, `AccessText`, `Label.Target` access-key metadata, `AutomationProperties`, automation peers/providers, `ObjectDataProvider`, `XmlDataProvider`, `x:Array`, `CompositeCollection`, `CollectionContainer`, `x:Null`, `DynamicResource` invalidation, `SystemParameters`, `SystemColors`, `SystemFonts`, WPF `Resource` pack streams, copied `Content` streams, site-of-origin streams, `DrawingImage`, `Image`, `ImageBrush`, `BlurEffect`, `DropShadowEffect`, menus, context menus, secondary `Window` XAML/show/close lifecycle, modal `ShowDialog`, portable `MessageBox.Show`, common file/folder dialogs, bindings, `PriorityBinding`, `FallbackValue`, `TargetNullValue`, `Binding.TargetUpdated`, `Binding.SourceUpdated`, `RelativeSource`, routed commands, stock `ApplicationCommands`, stock `NavigationCommands`, `CommandManager.RequerySuggested`, custom routed events, routed-event class handlers, key and mouse input bindings, value converters, `MultiBinding`, `CollectionViewSource` sorting/grouping/filtering, `GroupStyle.HeaderTemplate`, `AlternationCount`, `ItemStringFormat`, `SelectedValuePath`, multi-selection, `TabControl`, `TreeViewItem` expansion/selection events, `VirtualizingPanel` metadata, `VirtualizingStackPanel`, `GroupBox`, `Expander`, `ScrollViewer`, `ToolBar`, `ToolTip`, `Popup`, `ToggleButton`, `RadioButton`, `RepeatButton`, `Thumb` drag routed events, `Calendar`, `DatePicker`, `FocusManager`, `KeyboardNavigation`, `DockPanel`, `WrapPanel`, `UniformGrid`, `Canvas`, WPF shape controls, `GridSplitter`, `Viewbox`, explicit and implicit `DataTemplate`s, `DataTemplateSelector`, `ItemContainerStyle`, `ItemContainerStyleSelector`, `ItemsPanelTemplate`, implicit and `BasedOn` styles, property/data style triggers, `MultiTrigger`, `MultiDataTrigger`, `EventSetter`, compiled `ControlTemplate` styles, `VisualStateManager`, `ValidationRule`, `IDataErrorInfo`, `INotifyDataErrorInfo`, `BindingGroup`, `Validation.ErrorTemplate`, `EventTrigger`/`BeginStoryboard` animations, reusable `UserControl`, custom `DependencyProperty` binding, inherited attached properties, coercion callbacks, `AddOwner`, metadata overrides, `SetCurrentValue`, `Frame`/`Page` navigation with command-routed journal back/forward, `PasswordBox`, `RichTextBox`, `TextRange`, `EditingCommands`, `ApplicationCommands.Copy`, `ApplicationCommands.Cut`, `ApplicationCommands.Paste`, `ApplicationCommands.Undo`, `ApplicationCommands.Redo`, `DataObject`, `Clipboard`, `Hyperlink.RequestNavigate`, `FlowDocumentScrollViewer`, `FlowDocumentPageViewer`, `FlowDocumentReader`, `ListView`/`GridView`, list/table/tree controls, and a basic `FlowDocument`
- no app-side ProGPU APIs

Build and launch the SDK-produced apphost from the repository root:

```bash
./eng/run-progpu-wpf-mvp.sh
```

Run the same app through a bounded `Application.Run()` validation that opens via `StartupUri`, validates the WPF object graph, application manager state, startup event resources/properties, and shuts down automatically:

```bash
PROGPU_WPF_MVP_RUN_VALIDATE=1 ./eng/run-progpu-wpf-mvp.sh
```

Run the native apphost long enough to verify the live ProGPU/Silk.NET swapchain covers the declared `760x560` WPF window before terminating the probe:

```bash
PROGPU_WPF_MVP_LIVE_VALIDATE=1 ./eng/run-progpu-wpf-mvp.sh
```

If the local `0.1.0-preview.43` LibreWPF packages are stale or missing, rebuild the SDK package feed first:

```bash
PROGPU_WPF_MVP_REBUILD_PACKAGES=1 ./eng/run-progpu-wpf-mvp.sh
```

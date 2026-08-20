# LibreWPF Paid Xceed Toolkit + DataGrid

This sample exercises the commercial Xceed Toolkit Plus and Xceed DataGrid packages through the custom `LibreWPF.Sdk` project shape. The app project stays a normal SDK-switched WPF app:

- `Project Sdk="LibreWPF.Sdk/0.1.0-preview.43"`
- `UseWPF`
- normal compiled `App.xaml` / `MainWindow.xaml`
- normal Xceed package references
- no app-side ProGPU APIs

The project references the paid Toolkit/AvalonDock packages directly plus the separate paid DataGrid product:

- `Xceed.Wpf.Toolkit` `5.2.26322.8434`
- `Xceed.Wpf.AvalonDock` `5.2.26322.8434`
- `Xceed.Wpf.AvalonDock.Themes.Windows10` `5.2.26322.8434`
- `Xceed.Wpf.Themes.Windows10` `5.2.26322.8434`
- `Xceed.Wpf.Toolkit.Themes.MaterialDesign` `5.2.26322.8434`
- `Xceed.Products.Wpf.DataGrid.Full` `7.3.26322.8481`

The sample also pins patched direct versions of transitive packages pulled by the paid Xceed dependency graph so restore validation does not rely on vulnerable legacy transitive versions:

- `System.Data.SqlClient` `4.9.1`
- `System.Drawing.Common` `10.0.10`

The direct Toolkit references are intentional. The complete Toolkit metapackage also brings the Toolkit-era `Xceed.Wpf.DataGrid.Toolkit` assembly, which collides with the separate `Xceed.Wpf.DataGrid` product's `DataGridControl` type. This sample keeps the paid DataGrid surface on the 7.3 DataGrid product and uses Toolkit Plus/AvalonDock packages for the Toolkit side.

Runtime licensing is loaded only from environment variables:

- `XCEED_TOOLKIT_LICENSE_KEY`
- `XCEED_DATAGRID_LICENSE_KEY`

Do not put license values in this repository. `App.xaml.cs` sets `Xceed.Wpf.Toolkit.Licenser.LicenseKey`, `Xceed.Wpf.Themes.Windows10.Licenser.LicenseKey`, and `Xceed.Wpf.DataGrid.Licenser.LicenseKey` before constructing any paid controls.

Run:

```bash
./eng/run-progpu-wpf-xceed-paid.sh
```

Optional export output:

```bash
PROGPU_WPF_XCEED_PAID_EXPORT_DIR=/tmp/progpu-wpf-xceed-paid ./eng/run-progpu-wpf-xceed-paid.sh
```

Validation:

```bash
PROGPU_WPF_XCEED_PAID_VALIDATE=1 ./eng/run-progpu-wpf-xceed-paid.sh
PROGPU_WPF_XCEED_PAID_RUN_VALIDATE=1 ./eng/run-progpu-wpf-xceed-paid.sh
PROGPU_WPF_XCEED_PAID_LIVE_VALIDATE=1 ./eng/run-progpu-wpf-xceed-paid.sh
```

On macOS, the run script also imports `XCEED_TOOLKIT_LICENSE_KEY` and `XCEED_DATAGRID_LICENSE_KEY` from `launchctl getenv` when they are not already present in the current shell. The script never prints or writes the license values.

When the license env vars are missing, `PROGPU_WPF_XCEED_PAID_VALIDATE=1` still validates that the paid packages restore and the expected Toolkit Plus/DataGrid/Views3D/theme assemblies load. Set `PROGPU_WPF_XCEED_PAID_REQUIRE_LICENSE=1` to make validation fail unless both license variables are present.

`PROGPU_WPF_XCEED_PAID_RUN_VALIDATE=1` is a loaded-window validation and fails fast if license variables are still unavailable after shell and `launchctl` lookup, instead of opening the missing-license window and waiting for user input. `PROGPU_WPF_XCEED_PAID_LIVE_VALIDATE=1` runs that same loaded-window validation through the apphost and additionally checks the ProGPU render-surface geometry line for logical size, physical pixels, DPI, and full-target viewport coverage.

The MVP window hosts an AvalonDock layout with a Toolkit Plus Material-control pane and paid `Xceed.Wpf.DataGrid.DataGridControl` documents backed by 100,000 rows. The DataGrid lane now exercises explicit `xcdg:Column` definitions, `DataGridCollectionViewSource`, `DataGridVirtualizingQueryableCollectionViewSource`, explicit item-property metadata, `DataGridUnboundItemProperty`/`UnboundColumn` computed priority values, active-row filtering, category grouping, updated-date sorting, statistical functions, `FilterRow`, `StatRow`/`StatCell`, `TableView`, `TableflowView`, fixed headers/footers, merged headers, `SearchControl`/`SearchText`, `ColumnChooserControl`, `ShowInColumnChooser`, `AllowColumnChooser`, `ColumnChooserSortOrder`, visible-column toggling through `ColumnBase.Visible`, editable `ReadOnly=False` grids, `EditTriggers`, mutable `IDataErrorInfo` rows, `CellEditorDisplayConditions`, `Office2007BlueTheme` from ThemePack 1, auto-created detail descriptions, an explicit `DetailConfiguration`, row selection, `BringItemIntoView(...)` navigation, virtual view `MoveCurrentToPosition(...)` navigation, `ExportToCsv(...)`, `ExportToExcel(...)`, in-memory `SaveUserSettings(...)`/`LoadUserSettings(...)`, and package surface checks for Views3D `CardflowView3D`, column chooser/context-menu, cell editor/validation, virtualization, export, settings, theme-pack, and Workbooks types. The virtualizing document uses bounded `PageSize` and `MaxRealizedItemCount` settings over the same queryable 100k model for performance evaluation. The CSV/Excel buttons use Xceed's own export APIs and write to `PROGPU_WPF_XCEED_PAID_EXPORT_DIR` or a temp directory.

`PROGPU_WPF_XCEED_PAID_RUN_VALIDATE=1` and `PROGPU_WPF_XCEED_PAID_LIVE_VALIDATE=1` drive the loaded window through the same command handlers exposed by the toolbar: add row, select last row, save/load settings, hide/show the Status column, switch Tableflow/Cardflow/TableView, run editable-grid validation, assert WPF internal `VisualClip` viewport clips for the Toolkit pane and all paid DataGrid documents, verify paid `DataRow` realization stays bounded during initial layout and large-scroll passes, enforce a 10-second budget for each 100k-row large-scroll/layout pass, and perform large-scroll offset and clip checks on the 100k-row DataGrid surfaces. This keeps the paid Xceed smoke focused on normal WPF/Xceed managers while ProGPU validates rendering, invalidation, clipping, input, render-surface sizing, and GPU-backed hit testing under real command traffic.

WPF remains responsible for the managed Xceed control tree, binding, collection views, data virtualization, editing, validation, unbound item-property query values, filtering/grouping/sorting/stats/details, search, merged-header metadata, column chooser/visible-column policy, settings persistence, export policy, and docking state. ProGPU owns windowing, input, invalidation, clipping, image/layer texture trimming, shaders, and final WebGPU rendering.

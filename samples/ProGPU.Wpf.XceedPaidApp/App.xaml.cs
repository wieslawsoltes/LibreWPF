using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.ProGPU;
using System.Windows.Threading;
using DataGridLicenser = Xceed.Wpf.DataGrid.Licenser;
using ToolkitLicenser = Xceed.Wpf.Toolkit.Licenser;
using Windows10ThemeLicenser = Xceed.Wpf.Themes.Windows10.Licenser;

namespace ProGPU.Wpf.XceedPaidApp;

public partial class App : Application
{
    private const string LiveValidationEnvironmentVariable = "PROGPU_WPF_XCEED_PAID_LIVE_VALIDATE";
    private const string LiveValidationStatusPathEnvironmentVariable = "PROGPU_WPF_XCEED_PAID_LIVE_VALIDATE_STATUS_PATH";
    private const int MaxRunValidationAttempts = 40;

    internal static int StartupEventCount { get; private set; }

    internal static int ExitEventCount { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var licenseStatus = XceedPaidLicenseBootstrap.ConfigureFromEnvironment();
        bool runValidate = Environment.GetEnvironmentVariable("PROGPU_WPF_XCEED_PAID_RUN_VALIDATE") == "1";
        bool liveValidate = Environment.GetEnvironmentVariable(LiveValidationEnvironmentVariable) == "1";

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_XCEED_PAID_VALIDATE") == "1")
        {
            XceedPaidSelfTest.ValidatePackageSurface(licenseStatus);
            if (!licenseStatus.IsConfigured &&
                Environment.GetEnvironmentVariable("PROGPU_WPF_XCEED_PAID_REQUIRE_LICENSE") != "1")
            {
                Console.WriteLine($"ProGPU WPF paid Xceed package surface validation succeeded; runtime license validation skipped: {licenseStatus.DescribePublic()}.");
                Shutdown();
                return;
            }

            var window = new MainWindow(licenseStatus);
            XceedPaidSelfTest.Validate(window);
            Shutdown();
            Console.WriteLine("ProGPU WPF paid Xceed validation succeeded.");
            return;
        }

        base.OnStartup(e);

        if (!licenseStatus.IsConfigured)
        {
            if (runValidate || liveValidate)
            {
                Console.Error.WriteLine($"ProGPU WPF paid Xceed Application.Run validation requires license variables: {licenseStatus.DescribePublic()}.");
                Shutdown(1);
                return;
            }

            MainWindow = CreateMissingLicenseWindow(licenseStatus);
            MainWindow.Show();
            return;
        }

        var mainWindow = new MainWindow(licenseStatus);
        MainWindow = mainWindow;
        mainWindow.Show();

        if (runValidate || liveValidate)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                new Action(ValidateRunningApplication));
        }
    }

    private void OnAppStartup(object sender, StartupEventArgs e)
    {
        StartupEventCount++;
        Properties["XceedPaidStartupArgumentCount"] = e.Args.Length;
    }

    private void OnAppExit(object sender, ExitEventArgs e)
    {
        ExitEventCount++;
    }

    private static void ValidateRunningApplication()
    {
        try
        {
            bool liveValidate = Environment.GetEnvironmentVariable(LiveValidationEnvironmentVariable) == "1";
            var window = Current.MainWindow as MainWindow
                ?? Current.Windows.OfType<MainWindow>().FirstOrDefault()
                ?? throw new InvalidOperationException("Expected paid Xceed MainWindow.");
            if (!IsProGpuNativeTargetReady(window))
            {
                var attempt = Current.Properties["XceedPaidRunValidationAttempt"] is int currentAttempt
                    ? currentAttempt
                    : 0;
                if (attempt < MaxRunValidationAttempts)
                {
                    Current.Properties["XceedPaidRunValidationAttempt"] = attempt + 1;
                    Current.Dispatcher.BeginInvoke(
                        DispatcherPriority.ApplicationIdle,
                        new Action(ValidateRunningApplication));
                    return;
                }
            }

            XceedPaidSelfTest.Validate(window, expectLoaded: true);
            if (liveValidate)
            {
                string geometryStatus = XceedPaidSelfTest.ValidateRenderSurfaceGeometry(
                    window,
                    requireFullViewport: true);
                string successStatus = $"ProGPU WPF paid Xceed live geometry validation succeeded: {geometryStatus}.";
                string detailStatus = "ProGPU WPF paid Xceed live geometry validation details: loaded-window commands, scroll clips, bounded DataGrid rows, large-scroll performance budget, and GPU hit testing updated.";
                Console.WriteLine(successStatus);
                Console.WriteLine(detailStatus);
                WriteLiveValidationStatus($"{successStatus}{Environment.NewLine}{detailStatus}{Environment.NewLine}");
                Console.Out.Flush();
            }
            else
            {
                Console.WriteLine("ProGPU WPF paid Xceed Application.Run validation succeeded.");
            }

            Current.Shutdown();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Current.Shutdown(1);
        }
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

    private static bool IsProGpuNativeTargetReady(MainWindow window)
    {
        if (!ProGpuWpfDiagnostics.TryGetWindowHost(window, out var host) || host is null)
        {
            return false;
        }

        host.DoEvents();
        return host.CompositionTarget != null;
    }

    private static Window CreateMissingLicenseWindow(XceedPaidLicenseStatus licenseStatus)
    {
        return new Window
        {
            Title = "ProGPU WPF Paid Xceed",
            Width = 760,
            Height = 360,
            Background = Brushes.White,
            Content = new Border
            {
                Padding = new Thickness(24),
                Child = new TextBlock
                {
                    Text = "Set XCEED_TOOLKIT_LICENSE_KEY and XCEED_DATAGRID_LICENSE_KEY to run the paid Toolkit/DataGrid Showcase sample.\n\n" +
                           licenseStatus.DescribePublic(),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 16
                }
            }
        };
    }
}

internal sealed record XceedPaidLicenseStatus(
    bool ToolkitConfigured,
    bool DataGridConfigured,
    string? Failure)
{
    internal bool IsConfigured => ToolkitConfigured && DataGridConfigured && Failure is null;

    internal string DescribePublic()
    {
        if (Failure is not null)
        {
            return Failure;
        }

        return $"Toolkit license env: {(ToolkitConfigured ? "present" : "missing")}; DataGrid license env: {(DataGridConfigured ? "present" : "missing")}.";
    }
}

internal static class XceedPaidLicenseBootstrap
{
    internal const string ToolkitLicenseEnvironmentVariable = "XCEED_TOOLKIT_LICENSE_KEY";
    internal const string DataGridLicenseEnvironmentVariable = "XCEED_DATAGRID_LICENSE_KEY";

    internal static XceedPaidLicenseStatus ConfigureFromEnvironment()
    {
        var toolkitKey = Environment.GetEnvironmentVariable(ToolkitLicenseEnvironmentVariable);
        var dataGridKey = Environment.GetEnvironmentVariable(DataGridLicenseEnvironmentVariable);
        var toolkitConfigured = !string.IsNullOrWhiteSpace(toolkitKey);
        var dataGridConfigured = !string.IsNullOrWhiteSpace(dataGridKey);

        if (!toolkitConfigured || !dataGridConfigured)
        {
            return new XceedPaidLicenseStatus(
                toolkitConfigured,
                dataGridConfigured,
                $"Missing paid Xceed license environment variable(s): {DescribeMissing(toolkitConfigured, dataGridConfigured)}.");
        }

        try
        {
            ToolkitLicenser.LicenseKey = toolkitKey;
            Windows10ThemeLicenser.LicenseKey = toolkitKey;
            DataGridLicenser.LicenseKey = dataGridKey;
            return new XceedPaidLicenseStatus(true, true, null);
        }
        catch (Exception ex)
        {
            return new XceedPaidLicenseStatus(
                toolkitConfigured,
                dataGridConfigured,
                $"Failed to configure Xceed licenses from environment variables: {ex.GetBaseException().Message}");
        }
    }

    private static string DescribeMissing(bool toolkitConfigured, bool dataGridConfigured)
    {
        if (!toolkitConfigured && !dataGridConfigured)
        {
            return $"{ToolkitLicenseEnvironmentVariable}, {DataGridLicenseEnvironmentVariable}";
        }

        return toolkitConfigured
            ? DataGridLicenseEnvironmentVariable
            : ToolkitLicenseEnvironmentVariable;
    }
}

internal static class XceedPaidSelfTest
{
    private const int GpuOwnerBufferCapacity = 64;

    internal static void ValidatePackageSurface(XceedPaidLicenseStatus licenseStatus)
    {
        AssertType<Xceed.Wpf.Toolkit.MaterialButton>("Toolkit Plus MaterialButton");
        AssertType<Xceed.Wpf.Toolkit.MaterialTextField>("Toolkit Plus MaterialTextField");
        AssertType<Xceed.Wpf.Toolkit.MaterialSlider>("Toolkit Plus MaterialSlider");
        AssertType<Xceed.Wpf.Toolkit.MaterialSwitch>("Toolkit Plus MaterialSwitch");
        AssertType<Xceed.Wpf.Themes.Windows10.Windows10ResourceDictionary>("Toolkit Plus Windows10 theme resource dictionary");
        AssertType<Xceed.Wpf.DataGrid.DataGridControl>("Xceed DataGridControl");
        AssertType<Xceed.Wpf.DataGrid.DataGridCollectionViewSource>("Xceed DataGridCollectionViewSource");
        AssertType<Xceed.Wpf.DataGrid.DataGridVirtualizingCollectionView>("Xceed DataGrid virtualizing collection view");
        AssertType<Xceed.Wpf.DataGrid.DataGridVirtualizingQueryableCollectionViewSource>("Xceed DataGrid virtualizing queryable collection view source");
        AssertType<Xceed.Wpf.DataGrid.DataGridVirtualizingPanel>("Xceed DataGrid virtualizing panel");
        AssertType<Xceed.Wpf.DataGrid.Views.TableView>("Xceed DataGrid TableView");
        AssertType<Xceed.Wpf.DataGrid.Column>("Xceed DataGrid Column");
        AssertType<Xceed.Wpf.DataGrid.DataGridGroupDescription>("Xceed DataGrid group description");
        AssertType<Xceed.Wpf.DataGrid.DataGridItemProperty>("Xceed DataGrid item property");
        AssertType<Xceed.Wpf.DataGrid.DataGridUnboundItemProperty>("Xceed DataGrid unbound item property");
        AssertType<Xceed.Wpf.DataGrid.UnboundColumn>("Xceed DataGrid unbound column");
        AssertType<Xceed.Wpf.DataGrid.CellEditor>("Xceed DataGrid cell editor");
        AssertType<Xceed.Wpf.DataGrid.CellEditorSelector>("Xceed DataGrid cell editor selector");
        AssertType<Xceed.Wpf.DataGrid.CellValidationContext>("Xceed DataGrid cell validation context");
        AssertType<Xceed.Wpf.DataGrid.CellValidationError>("Xceed DataGrid cell validation error");
        AssertType<Xceed.Wpf.DataGrid.RowValidationError>("Xceed DataGrid row validation error");
        AssertType<Xceed.Wpf.DataGrid.ValidationRules.CellValidationRule>("Xceed DataGrid cell validation rule");
        AssertType<Xceed.Wpf.DataGrid.DetailConfiguration>("Xceed DataGrid detail configuration");
        AssertType<Xceed.Wpf.DataGrid.FilterRow>("Xceed DataGrid filter row");
        AssertType<Xceed.Wpf.DataGrid.MergedHeader>("Xceed DataGrid merged header");
        AssertType<Xceed.Wpf.DataGrid.MergedColumn>("Xceed DataGrid merged column");
        AssertType<Xceed.Wpf.DataGrid.MergedColumnManagerRow>("Xceed DataGrid merged-column manager row");
        AssertType<Xceed.Wpf.DataGrid.ColumnChooserControl>("Xceed DataGrid column chooser control");
        AssertType<Xceed.Wpf.DataGrid.ColumnChooserContextMenu>("Xceed DataGrid column chooser context menu");
        AssertType<Xceed.Wpf.DataGrid.SearchControl>("Xceed DataGrid search control");
        AssertType<Xceed.Wpf.DataGrid.StatRow>("Xceed DataGrid stat row");
        AssertType<Xceed.Wpf.DataGrid.StatCell>("Xceed DataGrid stat cell");
        AssertType<Xceed.Wpf.DataGrid.Stats.CountFunction>("Xceed DataGrid count stat function");
        AssertType<Xceed.Wpf.DataGrid.Stats.AverageFunction>("Xceed DataGrid average stat function");
        AssertType<Xceed.Wpf.DataGrid.Export.CsvExporter>("Xceed DataGrid CSV exporter");
        AssertType<Xceed.Wpf.DataGrid.Export.ExcelExporter>("Xceed DataGrid Excel exporter");
        AssertType<Xceed.Wpf.DataGrid.Export.HtmlClipboardExporter>("Xceed DataGrid HTML clipboard exporter");
        AssertType<Xceed.Wpf.DataGrid.Settings.SettingsRepository>("Xceed DataGrid settings repository");
        AssertType<Xceed.Wpf.DataGrid.ThemePack.Office2007BlueTheme>("Xceed DataGrid ThemePack Office2007BlueTheme");
        AssertType<Xceed.Wpf.DataGrid.Views.TableflowView>("Xceed DataGrid TableflowView");
        AssertType<Xceed.Wpf.DataGrid.Views.TreeGridflowView>("Xceed DataGrid TreeGridflowView");
        AssertType<Xceed.Wpf.DataGrid.Views.CardflowView3D>("Xceed DataGrid Views3D CardflowView3D");
        AssertType<Xceed.Wpf.DataGrid.Views.ElementalBlackTheme>("Xceed DataGrid Views3D ElementalBlackTheme");
        AssertType<Xceed.Wpf.DataGrid.Workbooks.WorkbooksExporter>("Xceed DataGrid Workbooks exporter");
        AssertType<Xceed.Wpf.AvalonDock.Themes.Windows10Theme>("Xceed AvalonDock Windows10 theme");
        AssertType<Xceed.Wpf.AvalonDock.Themes.Windows10.Windows10ResourceDictionary>("Xceed AvalonDock Windows10 resource dictionary");
        AssertDataGridControlApiSurface();
        AssertColumnChooserApiSurface();

        if (Environment.GetEnvironmentVariable("PROGPU_WPF_XCEED_PAID_REQUIRE_LICENSE") == "1" &&
            !licenseStatus.IsConfigured)
        {
            throw new InvalidOperationException(licenseStatus.DescribePublic());
        }
    }

    internal static void Validate(MainWindow window, bool expectLoaded = false)
    {
        ValidatePackageSurface(window.LicenseStatus);
        if (!window.LicenseStatus.IsConfigured)
        {
            throw new InvalidOperationException(window.LicenseStatus.DescribePublic());
        }

        var viewModel = window.ViewModel;
        var rowsView = window.PaidRowsViewSource;
        var virtualRowsView = window.VirtualPaidRowsViewSource;
        AssertEqual(100_000, viewModel.RowCount, "paid DataGrid row count");
        AssertEqual(128, viewModel.EditableRows.Count, "paid editable DataGrid row count");
        AssertEqual(66_667, viewModel.ActiveRowCount, "paid DataGrid active filtered row count");
        if (!expectLoaded)
        {
            AssertEqual(viewModel.Rows[0].Id, viewModel.SelectedRow.Id, "paid DataGrid initial selected row id");
            AssertEqual(viewModel.EditableRows[0].Id, viewModel.SelectedEditableRow.Id, "paid editable DataGrid initial selected row id");
        }
        else
        {
            AssertEqual(true, viewModel.Rows.Any(item => item.Id == viewModel.SelectedRow.Id), "paid DataGrid loaded selected row source membership");
            AssertEqual(true, viewModel.EditableRows.Any(item => item.Id == viewModel.SelectedEditableRow.Id), "paid editable DataGrid loaded selected row source membership");
        }
        AssertEqual(100_000, viewModel.VirtualRows.Count(), "paid DataGrid virtual queryable row count");
        AssertEqual(true, viewModel.Rows[0].Details.Count >= 2, "paid DataGrid lazy detail row data");
        AssertEqual(Xceed.Wpf.DataGrid.AutoFilterMode.And, rowsView.AutoFilterMode, "paid DataGrid auto-filter mode");
        AssertEqual(Xceed.Wpf.DataGrid.FilterCriteriaMode.And, rowsView.FilterCriteriaMode, "paid DataGrid filter criteria mode");
        AssertEqual(false, rowsView.AutoCreateItemProperties, "paid DataGrid explicit item properties");
        AssertEqual(false, rowsView.DefaultCalculateDistinctValues, "paid DataGrid distinct value calculation");
        AssertEqual(true, rowsView.AutoCreateDetailDescriptions, "paid DataGrid auto detail descriptions");
        AssertEqual(9, rowsView.ItemProperties.Count, "paid DataGrid item-property metadata");
        var priorityProperty = rowsView.ItemProperties
            .Cast<Xceed.Wpf.DataGrid.DataGridItemPropertyBase>()
            .Single(itemProperty => itemProperty.Name == "PriorityBand");
        AssertEqual(true, priorityProperty is Xceed.Wpf.DataGrid.DataGridUnboundItemProperty, "paid DataGrid priority unbound property");
        AssertEqual("Low", priorityProperty.GetValue(viewModel.Rows[0]), "paid DataGrid low priority unbound value");
        AssertEqual("Critical", MainWindow.GetPriorityBand(viewModel.Rows[4]), "paid DataGrid critical priority helper value");
        AssertEqual(true, window.PriorityBandQueryCount > 0, "paid DataGrid unbound priority query event");
        AssertEqual(1, rowsView.GroupDescriptions.Count, "paid DataGrid group description count");
        AssertEqual(1, rowsView.SortDescriptions.Count, "paid DataGrid sort description count");
        AssertEqual(2, rowsView.StatFunctions.Count, "paid DataGrid stat function count");
        AssertEqual(256, virtualRowsView.PageSize, "paid DataGrid virtual page size");
        AssertEqual(1024, virtualRowsView.MaxRealizedItemCount, "paid DataGrid virtual realized-item cache");
        AssertEqual(Xceed.Wpf.DataGrid.CommitMode.EditCommitted, virtualRowsView.CommitMode, "paid DataGrid virtual commit mode");
        AssertEqual(false, window.PaidDataGrid.AutoCreateColumns, "paid DataGrid auto columns");
        AssertEqual(true, window.PaidDataGrid.ReadOnly, "paid DataGrid read-only state");
        AssertEqual(true, window.PaidDataGrid.AutoCreateDetailConfigurations, "paid DataGrid auto detail configurations");
        AssertEqual(1, window.PaidDataGrid.DetailConfigurations.Count, "paid DataGrid explicit detail configuration");
        AssertEqual(1, window.PaidDataGrid.MergedHeaders.Count, "paid DataGrid merged header count");
        AssertEqual(true, window.PaidDataGrid.ClipboardExporters.Count >= 3, "paid DataGrid clipboard exporters");
        AssertEqual(Xceed.Wpf.DataGrid.ItemScrollingBehavior.Immediate, window.PaidDataGrid.ItemScrollingBehavior, "paid DataGrid scroll behavior");
        AssertEqual(Xceed.Wpf.DataGrid.NavigationBehavior.RowOnly, window.PaidDataGrid.NavigationBehavior, "paid DataGrid navigation behavior");
        AssertEqual(9, window.PaidDataGrid.Columns.Count, "paid DataGrid explicit columns");
        AssertEqual(9, window.PaidDataGrid.VisibleColumns.Count, "paid DataGrid visible column count");
        AssertEqual(true, ReferenceEquals(window.PaidDataGrid.Columns, window.PaidColumnChooser.Columns), "paid DataGrid column chooser column source");
        AssertEqual(true, window.PaidTableView.AllowColumnChooser, "paid DataGrid table view column chooser");
        AssertEqual(Xceed.Wpf.DataGrid.Views.ColumnChooserSortOrder.TitleAscending, window.PaidTableView.ColumnChooserSortOrder, "paid DataGrid column chooser sort order");
        var idColumn = MainWindow.FindPaidColumn(window.PaidDataGrid, "Id");
        var priorityColumn = MainWindow.FindPaidColumn(window.PaidDataGrid, "PriorityBand");
        var statusColumn = MainWindow.FindPaidColumn(window.PaidDataGrid, "Status");
        AssertEqual(true, priorityColumn is Xceed.Wpf.DataGrid.UnboundColumn, "paid DataGrid priority unbound column");
        AssertEqual(false, idColumn.ShowInColumnChooser, "paid DataGrid fixed Id column chooser visibility");
        AssertEqual(true, statusColumn.ShowInColumnChooser, "paid DataGrid Status column chooser visibility");
        statusColumn.Visible = false;
        AssertEqual(8, window.PaidDataGrid.VisibleColumns.Count, "paid DataGrid hidden Status visible column count");
        statusColumn.Visible = true;
        AssertEqual(9, window.PaidDataGrid.VisibleColumns.Count, "paid DataGrid restored Status visible column count");
        AssertEqual(false, window.VirtualPaidDataGrid.AutoCreateColumns, "paid virtual DataGrid auto columns");
        AssertEqual(true, window.VirtualPaidDataGrid.ReadOnly, "paid virtual DataGrid read-only state");
        AssertEqual(Xceed.Wpf.DataGrid.ItemScrollingBehavior.Deferred, window.VirtualPaidDataGrid.ItemScrollingBehavior, "paid virtual DataGrid scroll behavior");
        AssertEqual(7, window.VirtualPaidDataGrid.Columns.Count, "paid virtual DataGrid explicit columns");
        AssertEqual(false, window.EditablePaidDataGrid.AutoCreateColumns, "paid editable DataGrid auto columns");
        AssertEqual(false, window.EditablePaidDataGrid.ReadOnly, "paid editable DataGrid read-only state");
        AssertEqual(Xceed.Wpf.DataGrid.EditTriggers.CellIsCurrent, window.EditablePaidDataGrid.EditTriggers, "paid editable DataGrid edit trigger");
        AssertEqual(5, window.EditablePaidDataGrid.Columns.Count, "paid editable DataGrid explicit columns");
        AssertEqual(true, MainWindow.FindPaidColumn(window.EditablePaidDataGrid, "Id").ReadOnly, "paid editable DataGrid id read-only column");
        AssertEqual(true, MainWindow.FindPaidColumn(window.EditablePaidDataGrid, "Score").CellEditorDisplayConditions == Xceed.Wpf.DataGrid.CellEditorDisplayConditions.CellIsCurrent, "paid editable DataGrid score editor condition");
        var editableRow = viewModel.EditableRows[1];
        editableRow.Score = 125;
        AssertEqual("Score must stay between 0 and 100.", editableRow[nameof(PaidEditableGridItem.Score)], "paid editable DataGrid score validation");
        editableRow.Score = 75;
        AssertEqual(string.Empty, editableRow[nameof(PaidEditableGridItem.Score)], "paid editable DataGrid score validation reset");
        AssertEqual(true, window.PaidDataGrid.View is Xceed.Wpf.DataGrid.Views.TableView, "paid DataGrid table view");
        AssertEqual(true, window.PaidTableView.Theme is Xceed.Wpf.DataGrid.ThemePack.Office2007BlueTheme, "paid DataGrid ThemePack table view theme");
        AssertEqual(4, window.PaidTableView.FixedHeaders.Count, "paid DataGrid fixed header row count");
        AssertEqual(1, window.PaidTableView.FixedFooters.Count, "paid DataGrid fixed footer row count");
        AssertEqual(true, window.PaidSearchControl is Xceed.Wpf.DataGrid.SearchControl, "paid DataGrid search control");
        AssertEqual("ProGPU", viewModel.FilterText, "paid Toolkit filter view-model value");
        AssertEqual("ProGPU", viewModel.SearchText, "paid DataGrid search view-model binding");

        if (expectLoaded)
        {
            window.PaidDataGridDocument.IsSelected = true;
            window.PaidDataGridDocument.IsActive = true;
            window.PaidDataGrid.UpdateLayout();
            AssertEqual("ProGPU", window.PaidDataGrid.SearchText, "paid DataGrid loaded search text binding");
            AssertEqual(viewModel.SelectedRow, window.PaidDataGrid.SelectedItem, "paid DataGrid loaded selected item");
            window.PaidDataGrid.BringItemIntoView(viewModel.Rows[viewModel.Rows.Count - 1]);
            window.PaidDataGrid.UpdateLayout();
            window.EditableDataGridDocument.IsSelected = true;
            window.EditableDataGridDocument.IsActive = true;
            window.EditablePaidDataGrid.UpdateLayout();

            if (window.PaidDataGrid.ActualWidth <= 0 ||
                window.PaidDataGrid.ActualHeight <= 0 ||
                window.EditablePaidDataGrid.ActualWidth <= 0 ||
                window.EditablePaidDataGrid.ActualHeight <= 0)
            {
                throw new InvalidOperationException("Expected paid Xceed DataGrid documents to participate in loaded layout.");
            }

            AssertEqual(true, window.MaterialActionsSwitch.IsChecked, "paid Toolkit loaded MaterialSwitch binding");
            AssertEqual("ProGPU", window.MaterialFilterTextField.Text, "paid Toolkit loaded MaterialTextField binding");
            window.ExercisePaidDataGridRuntimeCommands();
            window.ValidatePaidScrollClipState();
            ValidateProGpuDiagnostics(window);
        }
    }

    private static void ValidateProGpuDiagnostics(MainWindow window)
    {
        if (!ProGpuWpfDiagnostics.TryGetWindowHost(window, out var host) || host is null)
        {
            throw new InvalidOperationException("Expected paid Xceed window to be attached to a ProGPU WPF host.");
        }

        if (host.PortablePresentationSource is null)
        {
            throw new InvalidOperationException("Expected paid Xceed ProGPU host to expose a portable presentation source.");
        }

        window.PaidDataGridDocument.IsSelected = true;
        window.PaidDataGridDocument.IsActive = true;
        window.PaidDataGrid.UpdateLayout();
        host.DoEvents();
        ValidateRenderSurfaceGeometry(window);

        if (!TryFindGpuPointOwnersUnder(host, window, window.PaidDataGrid, out var pointOwners, out var hitPoint, out var pointDiagnostics))
        {
            throw new InvalidOperationException(
                $"Expected paid Xceed DataGrid point to resolve GPU hit-test owners. {pointDiagnostics}");
        }

        if (!ContainsOwnerUnder(window.PaidDataGrid, pointOwners))
        {
            throw new InvalidOperationException(
                $"Expected paid Xceed DataGrid point GPU owners at {hitPoint.X:0.#},{hitPoint.Y:0.#} to include the DataGrid subtree; owners: {DescribeOwners(pointOwners)}.");
        }

        var topLeft = window.PaidDataGrid.TranslatePoint(new Point(8, 8), window);
        var bottomRight = window.PaidDataGrid.TranslatePoint(
            new Point(Math.Max(9, window.PaidDataGrid.ActualWidth - 8), Math.Max(9, window.PaidDataGrid.ActualHeight - 8)),
            window);
        object?[] boundsOwnerBuffer = ArrayPool<object?>.Shared.Rent(GpuOwnerBufferCapacity);
        try
        {
            if (!ProGpuWpfDiagnostics.TryQueryHitTestBoundsOwners(
                    window,
                    topLeft.X,
                    topLeft.Y,
                    bottomRight.X,
                    bottomRight.Y,
                    boundsOwnerBuffer,
                    out int boundsOwnerCount) ||
                boundsOwnerCount == 0)
            {
                throw new InvalidOperationException("Expected paid Xceed DataGrid bounds to resolve GPU hit-test owners.");
            }

            var boundsOwners = boundsOwnerBuffer.AsSpan(0, boundsOwnerCount);
            if (!ContainsOwnerUnder(window.PaidDataGrid, boundsOwners))
            {
                throw new InvalidOperationException(
                    $"Expected paid Xceed DataGrid bounds GPU owners to include the DataGrid subtree; owners: {DescribeOwners(boundsOwners)}.");
            }
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(boundsOwnerBuffer, clearArray: true);
        }

        ValidateGpuHitTestCache(window, "paid Xceed DataGrid loaded render");

        window.VirtualDataGridDocument.IsSelected = true;
        window.VirtualDataGridDocument.IsActive = true;
        window.BringVirtualPaidDataGridItemIntoView(50_000);
        window.VirtualPaidDataGrid.UpdateLayout();
        host.DoEvents();
        if (window.VirtualPaidDataGrid.ActualWidth <= 0 || window.VirtualPaidDataGrid.ActualHeight <= 0)
        {
            throw new InvalidOperationException("Expected paid Xceed virtual DataGrid document to participate in loaded layout.");
        }

        ValidateGpuHitTestCache(window, "paid Xceed virtual DataGrid loaded render");
    }

    internal static string ValidateRenderSurfaceGeometry(MainWindow window, bool requireFullViewport = false)
    {
        if (!ProGpuWpfDiagnostics.TryGetRenderSurfaceGeometry(window, out var geometry))
        {
            throw new InvalidOperationException("Expected paid Xceed validation to resolve ProGPU render-surface geometry.");
        }

        if (geometry.LogicalWidth == 0 ||
            geometry.LogicalHeight == 0 ||
            geometry.PixelWidth == 0 ||
            geometry.PixelHeight == 0 ||
            geometry.ViewportWidth == 0 ||
            geometry.ViewportHeight == 0 ||
            geometry.DpiScaleX <= 0 ||
            geometry.DpiScaleY <= 0 ||
            geometry.DpiScale <= 0)
        {
            throw new InvalidOperationException(
                $"Expected paid Xceed render-surface geometry to be nonzero; logical={geometry.LogicalWidth}x{geometry.LogicalHeight}, pixels={geometry.PixelWidth}x{geometry.PixelHeight}, viewport={geometry.ViewportX},{geometry.ViewportY},{geometry.ViewportWidth}x{geometry.ViewportHeight}, dpi={geometry.DpiScaleX:0.###}x{geometry.DpiScaleY:0.###}.");
        }

        if (geometry.PixelWidth < geometry.LogicalWidth ||
            geometry.PixelHeight < geometry.LogicalHeight)
        {
            throw new InvalidOperationException(
                $"Expected paid Xceed render-surface pixels to cover logical content, but got logical {geometry.LogicalWidth}x{geometry.LogicalHeight} and pixels {geometry.PixelWidth}x{geometry.PixelHeight}.");
        }

        if (requireFullViewport &&
            (geometry.ViewportX != 0 ||
             geometry.ViewportY != 0 ||
             geometry.ViewportWidth != geometry.PixelWidth ||
             geometry.ViewportHeight != geometry.PixelHeight))
        {
            throw new InvalidOperationException(
                $"Expected paid Xceed render-surface viewport to use the full physical target, but got viewport {geometry.ViewportWidth}x{geometry.ViewportHeight}@{geometry.ViewportX},{geometry.ViewportY} for pixels {geometry.PixelWidth}x{geometry.PixelHeight}.");
        }

        return $"logical {geometry.LogicalWidth}x{geometry.LogicalHeight}, pixels {geometry.PixelWidth}x{geometry.PixelHeight}, viewport {geometry.ViewportWidth}x{geometry.ViewportHeight}@{geometry.ViewportX},{geometry.ViewportY}, dpi {geometry.DpiScale:0.###}";
    }

    private static void ValidateGpuHitTestCache(MainWindow window, string description)
    {
        if (!ProGpuWpfDiagnostics.TryGetGpuHitTestCacheSnapshot(window, out var cache))
        {
            throw new InvalidOperationException($"Expected {description} to expose ProGPU hit-test cache diagnostics.");
        }

        if (!cache.HasIndex ||
            !cache.HasDeviceIndex ||
            cache.PrimitiveCount < 16 ||
            cache.NodeCount == 0 ||
            cache.OwnerCount < 4)
        {
            throw new InvalidOperationException(
                $"Expected {description} to populate a GPU hit-test index; hasIndex={cache.HasIndex}, hasDeviceIndex={cache.HasDeviceIndex}, primitives={cache.PrimitiveCount}, nodes={cache.NodeCount}, primitiveIndices={cache.PrimitiveIndexCount}, pathSegments={cache.PathSegmentCount}, owners={cache.OwnerCount}.");
        }
    }

    private static Point GetElementCenter(FrameworkElement element, UIElement root)
    {
        return element.TranslatePoint(
            new Point(Math.Max(1, element.ActualWidth) / 2.0, Math.Max(1, element.ActualHeight) / 2.0),
            root);
    }

    private static bool TryFindGpuPointOwnersUnder(
        ProGpuWpfWindowHost host,
        MainWindow window,
        FrameworkElement element,
        out object?[] owners,
        out Point point,
        out string diagnostics)
    {
        owners = Array.Empty<object?>();
        point = default;
        diagnostics = "No GPU point probes were executed.";

        var width = Math.Max(1, element.ActualWidth);
        var height = Math.Max(1, element.ActualHeight);
        object?[] bestOwners = Array.Empty<object?>();
        Point bestPoint = default;
        string bestDiagnostics = string.Empty;
        object?[] ownerBuffer = ArrayPool<object?>.Shared.Rent(GpuOwnerBufferCapacity);
        ReadOnlySpan<double> probes = [0.12, 0.25, 0.5, 0.75, 0.88];
        try
        {
            foreach (var yFraction in probes)
            {
                foreach (var xFraction in probes)
                {
                    var candidate = element.TranslatePoint(new Point(width * xFraction, height * yFraction), window);
                    var candidateDiagnostics = QueryGpuPointOwners(host, candidate, ownerBuffer, out int candidateOwnerCount);
                    diagnostics = candidateDiagnostics;
                    var candidateOwners = ownerBuffer.AsSpan(0, candidateOwnerCount);
                    if (candidateOwnerCount > 0)
                    {
                        bestOwners = CopyOwners(candidateOwners);
                        bestPoint = candidate;
                        bestDiagnostics = candidateDiagnostics;
                    }

                    if (candidateOwnerCount > 0 && ContainsOwnerUnder(element, candidateOwners))
                    {
                        owners = CopyOwners(candidateOwners);
                        point = candidate;
                        diagnostics = candidateDiagnostics;
                        return true;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<object?>.Shared.Return(ownerBuffer, clearArray: true);
        }

        if (bestOwners.Length > 0)
        {
            owners = bestOwners;
            point = bestPoint;
            diagnostics = $"Closest mapped GPU owners at {bestPoint.X:0.#},{bestPoint.Y:0.#}: {DescribeOwners(bestOwners)}. {bestDiagnostics}";
        }
        else if (!string.IsNullOrWhiteSpace(bestDiagnostics))
        {
            diagnostics = bestDiagnostics;
        }

        return false;
    }

    private static string QueryGpuPointOwners(ProGpuWpfWindowHost host, Point point, Span<object?> owners, out int ownerCount)
    {
        ownerCount = 0;
        if (!ProGpuWpfDiagnostics.TryHitTestOwners(host, point.X, point.Y, owners, out ownerCount))
        {
            return $"GPU point query at {point.X:0.#},{point.Y:0.#} did not execute. {DescribeGpuHitTestCache(host)}";
        }

        return $"GPU point query at {point.X:0.#},{point.Y:0.#}: mappedOwners={ownerCount}. {DescribeGpuHitTestCache(host)}";
    }

    private static object?[] CopyOwners(ReadOnlySpan<object?> owners)
    {
        if (owners.Length == 0)
        {
            return Array.Empty<object?>();
        }

        var copy = new object?[owners.Length];
        owners.CopyTo(copy);
        return copy;
    }

    private static string DescribeGpuHitTestCache(ProGpuWpfWindowHost host)
    {
        if (!ProGpuWpfDiagnostics.TryGetGpuHitTestCacheSnapshot(host, out var cache))
        {
            return "cache=<unavailable>.";
        }

        return $"cache={cache.HasIndex}, deviceIndex={cache.HasDeviceIndex}, primitives={cache.PrimitiveCount}, nodes={cache.NodeCount}, primitiveIndices={cache.PrimitiveIndexCount}, pathSegments={cache.PathSegmentCount}, ownerMap={cache.OwnerCount}.";
    }

    private static bool ContainsOwnerUnder(DependencyObject root, IEnumerable<object?> owners)
    {
        return owners.OfType<DependencyObject>().Any(owner => IsSelfOrDescendantOf(owner, root));
    }

    private static bool ContainsOwnerUnder(DependencyObject root, ReadOnlySpan<object?> owners)
    {
        foreach (var owner in owners)
        {
            if (owner is DependencyObject dependencyObject &&
                IsSelfOrDescendantOf(dependencyObject, root))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSelfOrDescendantOf(DependencyObject owner, DependencyObject root)
    {
        for (DependencyObject? current = owner; current != null; current = GetParent(current))
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        return VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
    }

    private static string DescribeOwners(IEnumerable<object?> owners)
    {
        return string.Join(
            ", ",
            owners.Select(DescribeOwner).Take(12));
    }

    private static string DescribeOwners(ReadOnlySpan<object?> owners)
    {
        var builder = new System.Text.StringBuilder();
        int count = Math.Min(owners.Length, 12);
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(DescribeOwner(owners[i]));
        }

        return builder.ToString();
    }

    private static string DescribeOwner(object? owner)
    {
        return owner switch
        {
            null => "<null>",
            FrameworkElement element when !string.IsNullOrEmpty(element.Name) => $"FrameworkElement:{element.Name}",
            FrameworkContentElement element when !string.IsNullOrEmpty(element.Name) => $"FrameworkContentElement:{element.Name}",
            DependencyObject => "DependencyObject",
            _ => owner.ToString() ?? "<owner>"
        };
    }

    private static void AssertType<T>(string description)
    {
        if (typeof(T).FullName is null)
        {
            throw new InvalidOperationException($"Expected {description} type to load.");
        }
    }

    private static void AssertDataGridControlApiSurface()
    {
        Action<Xceed.Wpf.DataGrid.DataGridControl, System.IO.Stream> exportToCsv =
            static (grid, stream) => grid.ExportToCsv(stream);
        Action<Xceed.Wpf.DataGrid.DataGridControl, System.IO.Stream> exportToExcel =
            static (grid, stream) => grid.ExportToExcel(stream);
        Action<Xceed.Wpf.DataGrid.DataGridControl, System.IO.Stream, Size> exportToXps =
            static (grid, stream, pageSize) => grid.ExportToXps(stream, pageSize);
        Action<Xceed.Wpf.DataGrid.DataGridControl, Xceed.Wpf.DataGrid.Settings.SettingsRepository, Xceed.Wpf.DataGrid.Settings.UserSettings> saveSettings =
            static (grid, repository, settings) => grid.SaveUserSettings(repository, settings);
        Action<Xceed.Wpf.DataGrid.DataGridControl, Xceed.Wpf.DataGrid.Settings.SettingsRepository, Xceed.Wpf.DataGrid.Settings.UserSettings> loadSettings =
            static (grid, repository, settings) => grid.LoadUserSettings(repository, settings);

        GC.KeepAlive(exportToCsv);
        GC.KeepAlive(exportToExcel);
        GC.KeepAlive(exportToXps);
        GC.KeepAlive(saveSettings);
        GC.KeepAlive(loadSettings);
    }

    private static void AssertColumnChooserApiSurface()
    {
        Func<Xceed.Wpf.DataGrid.ColumnChooserControl, object?> columns =
            static control => control.Columns;
        Func<Xceed.Wpf.DataGrid.ColumnChooserControl, object?> visibleColumnsSectionTitle =
            static control => control.VisibleColumnsSectionTitle;
        Func<Xceed.Wpf.DataGrid.ColumnChooserControl, object?> hiddenColumnsSectionTitle =
            static control => control.HiddenColumnsSectionTitle;

        GC.KeepAlive(columns);
        GC.KeepAlive(visibleColumnsSectionTitle);
        GC.KeepAlive(hiddenColumnsSectionTitle);
    }

    private static void AssertEqual<T>(T expected, T actual, string description)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {description} to be {expected}, got {actual}.");
        }
    }
}

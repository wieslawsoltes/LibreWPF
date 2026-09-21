using System;
using System.IO;
using System.Linq;
using Forms = System.Windows.Forms;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class PortableWinFormsControlCompatibilityTests
{
    [Fact]
    public void SplitContainerPublishesPanelsAsChildren()
    {
        var splitContainer = new Forms.SplitContainer
        {
            Orientation = Forms.Orientation.Vertical,
            SplitterDistance = 140
        };
        var treeView = new Forms.TreeView();
        var propertyGrid = new Forms.PropertyGrid();

        splitContainer.Panel1.Controls.Add(treeView);
        splitContainer.Panel2.Controls.Add(propertyGrid);

        Assert.Contains(splitContainer.Panel1, splitContainer.Controls);
        Assert.Contains(splitContainer.Panel2, splitContainer.Controls);
        Assert.Same(splitContainer, splitContainer.Panel1.Parent);
        Assert.Same(splitContainer, splitContainer.Panel2.Parent);
        Assert.Same(splitContainer.Panel1, treeView.Parent);
        Assert.Same(splitContainer.Panel2, propertyGrid.Parent);
    }

    [Fact]
    public void TabControlSynchronizesControlsAndTabPages()
    {
        var tabControl = new Forms.TabControl();
        var first = new Forms.TabPage("First");
        var second = new Forms.TabPage("Second");

        tabControl.Controls.Add(first);
        tabControl.TabPages.Add(second);

        Assert.Equal(2, tabControl.TabPages.Count);
        Assert.Contains(first, tabControl.TabPages);
        Assert.Contains(second, tabControl.Controls);
        Assert.Same(first, tabControl.SelectedTab);

        tabControl.SelectedTab = second;
        Assert.Equal(1, tabControl.SelectedIndex);

        tabControl.Controls.Remove(second);
        Assert.DoesNotContain(second, tabControl.TabPages);
    }

    [Fact]
    public void DataGridViewRowsAddValuesAndRaiseRowEvents()
    {
        var dataGridView = new Forms.DataGridView();
        dataGridView.Columns.AddRange(new Forms.DataGridViewColumn[]
        {
            new Forms.DataGridViewTextBoxColumn { HeaderText = "Name", Width = 200 },
            new Forms.DataGridViewTextBoxColumn { HeaderText = "Value", Width = 400 }
        });
        int rowsAdded = 0;
        int rowsRemoved = 0;
        int lastAddedIndex = -1;
        dataGridView.RowsAdded += (_, e) =>
        {
            rowsAdded += e.RowCount;
            lastAddedIndex = e.RowIndex;
        };
        dataGridView.RowsRemoved += (_, e) => rowsRemoved += e.RowCount;

        int index = dataGridView.Rows.Add("Configuration", "Debug");

        Assert.Equal(0, index);
        Assert.Equal(1, rowsAdded);
        Assert.Equal(0, lastAddedIndex);
        Assert.Equal("Configuration", dataGridView.Rows[0].Cells[0].Value);
        Assert.Equal("Debug", dataGridView.Rows[0].Cells[1].Value);
        Assert.Equal(200, dataGridView.Columns[0].Width);
        Assert.Equal(400, dataGridView.Columns[1].Width);

        dataGridView.Rows.RemoveAt(0);

        Assert.Equal(1, rowsRemoved);
        Assert.Empty(dataGridView.Rows);
    }

    [Fact]
    public void ListViewSelectionSynchronizesSelectedItemsAndRaisesChange()
    {
        var listView = new Forms.ListView
        {
            MultiSelect = false,
            View = Forms.View.Details
        };
        listView.Columns.Add("Name", 120, Forms.HorizontalAlignment.Left);
        Forms.ListViewItem first = listView.Items.Add("First");
        Forms.ListViewItem second = listView.Items.Add("Second");
        int selectedIndexChanged = 0;
        listView.SelectedIndexChanged += (_, _) => selectedIndexChanged++;

        first.Selected = true;

        Assert.True(first.Selected);
        Assert.Contains(first, listView.SelectedItems);
        Assert.Equal(1, selectedIndexChanged);

        second.Selected = true;

        Assert.False(first.Selected);
        Assert.True(second.Selected);
        Assert.DoesNotContain(first, listView.SelectedItems);
        Assert.Contains(second, listView.SelectedItems);
        Assert.Equal(2, selectedIndexChanged);
        Assert.Same(second, listView.GetItemAt(4, 42));

        listView.SelectedItems.Clear();

        Assert.False(second.Selected);
        Assert.Empty(listView.SelectedItems);
        Assert.Equal(3, selectedIndexChanged);
    }

    [Fact]
    public void ListViewItemActivationSelectsHitItemAndRaisesActivate()
    {
        var listView = new Forms.ListView
        {
            MultiSelect = false,
            View = Forms.View.Details
        };
        listView.Columns.Add("Name", 120, Forms.HorizontalAlignment.Left);
        Forms.ListViewItem first = listView.Items.Add("First");
        Forms.ListViewItem second = listView.Items.Add("Second");
        int activated = 0;
        listView.ItemActivate += (_, _) => activated++;

        Assert.True(listView.TryActivateItemAt(4, 42));

        Assert.False(first.Selected);
        Assert.True(second.Selected);
        Assert.Contains(second, listView.SelectedItems);
        Assert.Equal(1, activated);

        Assert.False(listView.TryActivateItemAt(4, 120));
        Assert.Equal(1, activated);
    }

    [Fact]
    public void ListViewHeaderHitRaisesColumnClick()
    {
        var listView = new Forms.ListView
        {
            View = Forms.View.Details
        };
        listView.Columns.Add("Name", 50, Forms.HorizontalAlignment.Left);
        listView.Columns.Add("Value", 70, Forms.HorizontalAlignment.Left);
        int clickedColumn = -1;
        listView.ColumnClick += (_, e) => clickedColumn = e.Column;

        Assert.True(listView.TryRaiseColumnClickAt(55, 10));

        Assert.Equal(1, clickedColumn);

        listView.HeaderStyle = Forms.ColumnHeaderStyle.Nonclickable;

        Assert.False(listView.TryRaiseColumnClickAt(10, 10));
        Assert.Equal(1, clickedColumn);
    }

    [Fact]
    public void ListViewSortUsesAssignedItemSorter()
    {
        var listView = new Forms.ListView
        {
            View = Forms.View.Details,
            ListViewItemSorter = new ListViewTextComparer()
        };
        listView.Columns.Add("Name", 120, Forms.HorizontalAlignment.Left);
        listView.Items.Add("beta");
        listView.Items.Add("alpha");

        listView.Sort();

        Assert.Equal("alpha", listView.Items[0].Text);
        Assert.Equal("beta", listView.Items[1].Text);
    }

    [Fact]
    public void ListViewCheckBoxesPublishCheckedCollectionsAndItemCheck()
    {
        var listView = new Forms.ListView
        {
            CheckBoxes = true,
            View = Forms.View.Details
        };
        listView.Columns.Add("Name", 120, Forms.HorizontalAlignment.Left);
        Forms.ListViewItem first = listView.Items.Add("alpha");
        Forms.ListViewItem second = listView.Items.Add("beta");
        int checkedEvents = 0;
        listView.ItemCheck += (_, e) =>
        {
            checkedEvents++;
            if (e.Index == 1)
            {
                e.NewValue = Forms.CheckState.Checked;
            }
        };

        first.Checked = true;

        Assert.True(first.Checked);
        Assert.Equal(new[] { 0 }, listView.CheckedIndices.Cast<int>().ToArray());
        Assert.Same(first, Assert.Single(listView.CheckedItems));
        Assert.Equal(1, checkedEvents);

        Assert.True(listView.TryToggleItemCheckAt(10, 42));

        Assert.True(second.Checked);
        Assert.True(second.Selected);
        Assert.Equal(new[] { 0, 1 }, listView.CheckedIndices.Cast<int>().ToArray());
        Assert.Equal(2, checkedEvents);

        Assert.False(listView.TryToggleItemCheckAt(40, 42));
        Assert.True(second.Checked);
    }

    [Fact]
    public void TextBoxBaseTextInputReplacesSelectionAndRaisesTextChanged()
    {
        var textBox = new Forms.TextBox
        {
            Text = "abcd"
        };
        int textChanged = 0;
        textBox.TextChanged += (_, _) => textChanged++;

        textBox.Select(1, 2);
        textBox.ApplyTextInput("XY");

        Assert.Equal("aXYd", textBox.Text);
        Assert.Equal(3, textBox.SelectionStart);
        Assert.Equal(0, textBox.SelectionLength);
        Assert.Equal(1, textChanged);

        textBox.RaiseKeyDown(new Forms.KeyEventArgs(Forms.Keys.Back));

        Assert.Equal("aXd", textBox.Text);
        Assert.Equal(2, textBox.SelectionStart);
        Assert.Equal(0, textBox.SelectionLength);
        Assert.Equal(2, textChanged);

        textBox.Select(1, 1);
        textBox.RaiseKeyDown(new Forms.KeyEventArgs(Forms.Keys.Delete));

        Assert.Equal("ad", textBox.Text);
        Assert.Equal(1, textBox.SelectionStart);
        Assert.Equal(0, textBox.SelectionLength);
        Assert.Equal(3, textChanged);

        textBox.AppendText("!");

        Assert.Equal("ad!", textBox.Text);
        Assert.Equal(3, textBox.SelectionStart);
        Assert.Equal(0, textBox.SelectionLength);
        Assert.Equal(4, textChanged);
    }

    [Fact]
    public void TextBoxBaseKeyRaisersUseInheritedControlEvents()
    {
        var textBox = new Forms.TextBox();
        Forms.Keys observedKey = Forms.Keys.None;
        char observedChar = '\0';
        textBox.KeyDown += (_, e) => observedKey = e.KeyCode;
        textBox.KeyPress += (_, e) => observedChar = e.KeyChar;

        textBox.RaiseKeyDown(new Forms.KeyEventArgs(Forms.Keys.A));
        textBox.RaiseKeyPress(new Forms.KeyPressEventArgs('a'));

        Assert.Equal(Forms.Keys.A, observedKey);
        Assert.Equal('a', observedChar);
    }

    [Fact]
    public void CheckedListBoxPublishesCheckedStateCollectionsAndItemCheck()
    {
        var checkedListBox = new Forms.CheckedListBox();
        checkedListBox.Items.Add("alpha");
        checkedListBox.Items.Add("beta");
        checkedListBox.Items.Add("gamma");
        int checkedEvents = 0;
        checkedListBox.ItemCheck += (_, e) =>
        {
            checkedEvents++;
            if (e.Index == 2)
            {
                e.NewValue = Forms.CheckState.Indeterminate;
            }
        };

        checkedListBox.SetItemCheckState(1, Forms.CheckState.Checked);

        Assert.True(checkedListBox.GetItemChecked(1));
        Assert.Equal(Forms.CheckState.Checked, checkedListBox.GetItemCheckState(1));
        Assert.Equal(new[] { 1 }, checkedListBox.CheckedIndices.Cast<int>().ToArray());
        Assert.Equal(new object[] { "beta" }, checkedListBox.CheckedItems.Cast<object>().ToArray());
        Assert.Equal(1, checkedEvents);

        checkedListBox.SetItemChecked(2, true);

        Assert.True(checkedListBox.GetItemChecked(2));
        Assert.Equal(Forms.CheckState.Indeterminate, checkedListBox.GetItemCheckState(2));
        Assert.Equal(new[] { 1, 2 }, checkedListBox.CheckedIndices.Cast<int>().ToArray());
        Assert.Equal(2, checkedEvents);

        int secondRowY = Math.Max(1, checkedListBox.Font.Height) + 1;

        Assert.False(checkedListBox.TryToggleItemAt(40, secondRowY));

        checkedListBox.CheckOnClick = true;

        Assert.True(checkedListBox.TryToggleItemAt(40, secondRowY));
        Assert.False(checkedListBox.GetItemChecked(1));
        Assert.Equal(new[] { 2 }, checkedListBox.CheckedIndices.Cast<int>().ToArray());
    }

    [Fact]
    public void TreeViewSelectionRaisesCancelableEvents()
    {
        var treeView = new Forms.TreeView();
        var root = new Forms.TreeNode("Root");
        var child = new Forms.TreeNode("Child");
        root.Nodes.Add(child);
        treeView.Nodes.Add(root);
        int beforeSelect = 0;
        int afterSelect = 0;
        treeView.BeforeSelect += (_, e) =>
        {
            beforeSelect++;
            if (ReferenceEquals(e.Node, child))
            {
                e.Cancel = true;
            }
        };
        treeView.AfterSelect += (_, _) => afterSelect++;

        treeView.SelectedNode = root;
        treeView.SelectedNode = child;

        Assert.Same(root, treeView.SelectedNode);
        Assert.Equal(2, beforeSelect);
        Assert.Equal(1, afterSelect);
    }

    [Fact]
    public void TreeNodeExpandCollapseRaisesTreeViewEvents()
    {
        var treeView = new Forms.TreeView();
        var root = new Forms.TreeNode("Root");
        root.Nodes.Add(new Forms.TreeNode("Child"));
        treeView.Nodes.Add(root);
        int beforeExpand = 0;
        int afterExpand = 0;
        int beforeCollapse = 0;
        int afterCollapse = 0;
        treeView.BeforeExpand += (_, _) => beforeExpand++;
        treeView.AfterExpand += (_, _) => afterExpand++;
        treeView.BeforeCollapse += (_, _) => beforeCollapse++;
        treeView.AfterCollapse += (_, _) => afterCollapse++;

        root.Expand();
        root.Collapse();

        Assert.False(root.IsExpanded);
        Assert.Equal(1, beforeExpand);
        Assert.Equal(1, afterExpand);
        Assert.Equal(1, beforeCollapse);
        Assert.Equal(1, afterCollapse);
    }

    [Fact]
    public void TreeNodeRemoveDetachesRootNodeAndClearsSelection()
    {
        var treeView = new Forms.TreeView();
        var root = new Forms.TreeNode("Root");
        var child = new Forms.TreeNode("Child");
        root.Nodes.Add(child);
        treeView.Nodes.Add(root);
        treeView.SelectedNode = child;

        root.Remove();

        Assert.Empty(treeView.Nodes);
        Assert.Null(treeView.SelectedNode);
        Assert.Null(root.Parent);
        Assert.Null(root.TreeView);
        Assert.Null(child.TreeView);
    }

    [Fact]
    public void ImageListSupportsKeyedLookupAndTreeNodeImageKeys()
    {
        using var first = new System.Drawing.Bitmap(1, 1);
        using var second = new System.Drawing.Bitmap(1, 1);
        var imageList = new Forms.ImageList();

        imageList.Images.Add("XmlTextTreeNodeImage", first);
        imageList.Images.Add("XmlTextTreeNodeGhostImage", second);

        Assert.Equal(2, imageList.Images.Count);
        Assert.True(imageList.Images.ContainsKey("xmltexttreenodeimage"));
        Assert.Equal(1, imageList.Images.IndexOfKey("XmlTextTreeNodeGhostImage"));
        Assert.Same(second, imageList.Images["XmlTextTreeNodeGhostImage"]);
        Assert.Contains("XmlTextTreeNodeImage", imageList.Images.Keys.Cast<string>());

        var node = new Forms.TreeNode("text")
        {
            ImageIndex = 4,
            SelectedImageIndex = 5,
            ImageKey = "XmlTextTreeNodeImage",
            SelectedImageKey = "XmlTextTreeNodeGhostImage"
        };

        Assert.Equal("XmlTextTreeNodeImage", node.ImageKey);
        Assert.Equal("XmlTextTreeNodeGhostImage", node.SelectedImageKey);
        Assert.Equal(-1, node.ImageIndex);
        Assert.Equal(-1, node.SelectedImageIndex);

        imageList.Images.RemoveByKey("XmlTextTreeNodeImage");

        Assert.False(imageList.Images.ContainsKey("XmlTextTreeNodeImage"));
        Assert.Equal(1, imageList.Images.Count);
    }

    [Fact]
    public void WindowsFormsHostTreeViewRendererUsesImageListTypedPath()
    {
        string source = File.ReadAllText(FindRepoPath("src", "LibreWPF.WinFormsCompat", "WindowsFormsIntegration", "WindowsFormsHost.cs"));

        Assert.Contains("TryGetTreeNodeImageSource", source, StringComparison.Ordinal);
        Assert.Contains("Forms.ImageList? imageList = treeView.ImageList;", source, StringComparison.Ordinal);
        Assert.Contains("DrawingImage? keyedImage = imageList.Images[key];", source, StringComparison.Ordinal);
        Assert.Contains("drawingContext.DrawImage(imageSource", source, StringComparison.Ordinal);
        Assert.Contains("new WriteableBitmap(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null)", source, StringComparison.Ordinal);
        Assert.Contains("bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppPArgb)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsFormsHostTreeViewRendererUsesTypedOwnerDrawPath()
    {
        string formsSource = File.ReadAllText(FindRepoPath("src", "LibreWPF.WinFormsCompat", "System.Windows.Forms", "WinFormsCompatTypes.cs"));
        string hostSource = File.ReadAllText(FindRepoPath("src", "LibreWPF.WinFormsCompat", "WindowsFormsIntegration", "WindowsFormsHost.cs"));

        Assert.Contains("public void RaiseDrawNode(DrawTreeNodeEventArgs e)", formsSource, StringComparison.Ordinal);
        Assert.Contains("OnDrawNode(e);", formsSource, StringComparison.Ordinal);
        Assert.Contains("TryRenderTreeNodeOwnerDraw", hostSource, StringComparison.Ordinal);
        Assert.Contains("Forms.TreeNodeStates state = GetTreeNodeState(treeView, node);", hostSource, StringComparison.Ordinal);
        Assert.Contains("DrawingGraphics.FromImage(bitmap)", hostSource, StringComparison.Ordinal);
        Assert.Contains("graphics.TranslateTransform(0, -eventBounds.Y);", hostSource, StringComparison.Ordinal);
        Assert.Contains("treeView.RaiseDrawNode(eventArgs);", hostSource, StringComparison.Ordinal);
        Assert.Contains("drawDefault = eventArgs.DrawDefault;", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsFormsHostComboBoxRendererUsesTypedPopupAndOwnerDrawPath()
    {
        string formsSource = File.ReadAllText(FindRepoPath("src", "LibreWPF.WinFormsCompat", "System.Windows.Forms", "WinFormsCompatTypes.cs"));
        string hostSource = File.ReadAllText(FindRepoPath("src", "LibreWPF.WinFormsCompat", "WindowsFormsIntegration", "WindowsFormsHost.cs"));

        Assert.Contains("public void RaiseDrawItem(DrawItemEventArgs e)", formsSource, StringComparison.Ordinal);
        Assert.Contains("public void RaiseMeasureItem(MeasureItemEventArgs e)", formsSource, StringComparison.Ordinal);
        Assert.Contains("TryShowComboBoxDropDown", hostSource, StringComparison.Ordinal);
        Assert.Contains("target is Forms.ComboBox comboBox", hostSource, StringComparison.Ordinal);
        Assert.Contains("comboBox.DroppedDown = true;", hostSource, StringComparison.Ordinal);
        Assert.Contains("comboBox.SelectedIndex = itemIndex;", hostSource, StringComparison.Ordinal);
        Assert.Contains("TryRenderListItemOwnerDraw", hostSource, StringComparison.Ordinal);
        Assert.Contains("Forms.DrawItemEventArgs eventArgs = new(graphics, listBox.Font, drawBounds, index, state);", hostSource, StringComparison.Ordinal);
        Assert.Contains("listBox.RaiseDrawItem(eventArgs);", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Reflection", hostSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProperty(", hostSource, StringComparison.Ordinal);
    }

    [Fact]
    public void MenuBaseDefersMenuModePushUntilPortablePopupHasPresentationSource()
    {
        string menuBaseSource = File.ReadAllText(FindRepoPath(
            "src",
            "Microsoft.DotNet.Wpf",
            "src",
            "PresentationFramework",
            "System",
            "Windows",
            "Controls",
            "Primitives",
            "MenuBase.cs"));

        Assert.Contains("_pushedMenuMode = PresentationSource.CriticalFromVisual(this);", menuBaseSource, StringComparison.Ordinal);
        Assert.Contains("if (_pushedMenuMode == null)", menuBaseSource, StringComparison.Ordinal);
        Assert.Contains("Portable popup sources can be attached after Opened is raised.", menuBaseSource, StringComparison.Ordinal);
        Assert.Contains("InputManager.UnsecureCurrent.PushMenuMode(_pushedMenuMode);", menuBaseSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DrawItemEventArgsDrawsBackgroundAndFocusRectangle()
    {
        using var bitmap = new System.Drawing.Bitmap(8, 8);
        using System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap);
        var args = new Forms.DrawItemEventArgs(
            graphics,
            Forms.Control.DefaultFont,
            new System.Drawing.Rectangle(0, 0, 8, 8),
            0,
            Forms.DrawItemState.Selected | Forms.DrawItemState.Focus);

        args.DrawBackground();
        int background = System.Drawing.SystemColors.Highlight.ToArgb();
        Assert.Equal(background, bitmap.GetPixel(4, 4).ToArgb());
        args.DrawFocusRectangle();

        Assert.Equal(background, bitmap.GetPixel(4, 4).ToArgb());
        System.Drawing.Color focusPixel = bitmap.GetPixel(1, 1);
        System.Drawing.Color foreground = System.Drawing.SystemColors.WindowText;
        System.Drawing.Color backgroundColor = System.Drawing.SystemColors.Highlight;
        Assert.NotEqual(background, focusPixel.ToArgb());
        Assert.Equal(255, focusPixel.A);
        Assert.InRange(focusPixel.R, Math.Min(foreground.R, backgroundColor.R), Math.Max(foreground.R, backgroundColor.R));
        Assert.InRange(focusPixel.G, Math.Min(foreground.G, backgroundColor.G), Math.Max(foreground.G, backgroundColor.G));
        Assert.InRange(focusPixel.B, Math.Min(foreground.B, backgroundColor.B), Math.Max(foreground.B, backgroundColor.B));
    }

    private sealed class ListViewTextComparer : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            return string.Compare(
                ((Forms.ListViewItem?)x)?.Text,
                ((Forms.ListViewItem?)y)?.Text,
                StringComparison.Ordinal);
        }
    }

    private static string FindRepoPath(params string[] pathSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathSegments).ToArray());

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repo file '{Path.Combine(pathSegments)}' from the test output directory.");
    }
}

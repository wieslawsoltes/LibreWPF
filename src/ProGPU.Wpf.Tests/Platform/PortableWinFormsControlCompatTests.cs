using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class PortableWinFormsControlCompatTests
{
    [Fact]
    public void PictureBoxSupportsDesignerInitialization()
    {
        var pictureBox = new PictureBox();

        var initializer = Assert.IsAssignableFrom<ISupportInitialize>(pictureBox);
        initializer.BeginInit();
        pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        initializer.EndInit();

        Assert.Equal(PictureBoxSizeMode.Zoom, pictureBox.SizeMode);
    }

    [Fact]
    public void ErrorProviderSupportsDesignerGeneratedErrorState()
    {
        using var container = new Container();
        using var textBox = new TextBox();
        using var provider = new ErrorProvider(container);

        var initializer = Assert.IsAssignableFrom<ISupportInitialize>(provider);
        initializer.BeginInit();
        provider.BlinkStyle = ErrorBlinkStyle.NeverBlink;
        provider.SetIconAlignment(textBox, ErrorIconAlignment.MiddleLeft);
        provider.SetIconPadding(textBox, 6);
        provider.SetError(textBox, "Name is required.");
        initializer.EndInit();

        Assert.Contains(provider, container.Components.Cast<object>());
        Assert.True(((IExtenderProvider)provider).CanExtend(textBox));
        Assert.Equal(ErrorBlinkStyle.NeverBlink, provider.BlinkStyle);
        Assert.Equal(ErrorIconAlignment.MiddleLeft, provider.GetIconAlignment(textBox));
        Assert.Equal(6, provider.GetIconPadding(textBox));
        Assert.Equal("Name is required.", provider.GetError(textBox));

        provider.SetError(textBox, string.Empty);

        Assert.Equal(string.Empty, provider.GetError(textBox));
    }

    [Fact]
    public void ListBoxSupportsSortedMultiSelection()
    {
        var listBox = new ListBox
        {
            SelectionMode = SelectionMode.MultiExtended,
            Sorted = true
        };

        listBox.Items.Add("zeta");
        listBox.Items.Add("alpha");
        listBox.Items.Add("gamma");
        listBox.SetSelected(0, true);
        listBox.SetSelected(2, true);

        Assert.Equal("alpha", listBox.Items[0]);
        Assert.Equal("zeta", listBox.Items[2]);
        Assert.Equal(0, listBox.SelectedIndex);
        Assert.Equal(new[] { "alpha", "zeta" }, listBox.SelectedItems.Cast<string>().ToArray());
    }

    [Fact]
    public void XmlEditorWinFormsSurfaceApiIsAvailable()
    {
        var parent = new Panel();
        var first = new TextBox { Text = "first" };
        var second = new PropertyGrid();
        parent.Controls.Add(first);
        parent.Controls.Add(second);

        first.BringToFront();
        Assert.Same(first, parent.Controls[parent.Controls.Count - 1]);

        first.SendToBack();
        Assert.Same(first, parent.Controls[0]);

        first.Clear();
        Assert.Equal(string.Empty, first.Text);

        var split = new SplitContainer { SplitterWidth = 8 };
        Assert.Equal(8, split.SplitterWidth);

        var grid = new DataGridView
        {
            MultiSelect = false,
            ShowEditingIcon = false
        };
        Assert.False(grid.MultiSelect);
        Assert.False(grid.ShowEditingIcon);

        var item = new ListViewItem(new[] { "match", "value" }, 3);
        Assert.Equal(3, item.ImageIndex);
        Assert.Equal("match", item.Text);
        Assert.Equal("value", item.SubItems[1].Text);

        var textItem = new ListViewItem("single", 2);
        Assert.Equal(2, textItem.ImageIndex);
        Assert.Equal("single", textItem.Text);

        var root = new TreeNode("root");
        var child = new TreeNode("child");
        root.Nodes.Add(child);
        root.ExpandAll();
        Assert.True(root.IsExpanded);
        Assert.True(child.IsExpanded);

        root.Collapse(false);
        Assert.False(root.IsExpanded);
        Assert.False(child.IsExpanded);
    }
}

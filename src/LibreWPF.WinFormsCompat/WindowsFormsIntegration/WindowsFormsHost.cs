using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ProGPU.Backend;
using ProGPU.Wpf.Interop;
using Forms = System.Windows.Forms;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingImage = System.Drawing.Image;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;

namespace System.Windows.Forms.Integration;

[DefaultEvent("ChildChanged")]
[DesignerCategory("code")]
[ContentProperty("Child")]
public class WindowsFormsHost : FrameworkElement
{
    private static readonly object s_registeredHostsGate = new();
    private static readonly List<WeakReference<WindowsFormsHost>> s_registeredHosts = new();

    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize));

    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(SystemFonts.MessageFontStyle));

    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(SystemFonts.MessageFontWeight));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush));

    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(default(Thickness)));

    public static readonly DependencyProperty TabIndexProperty =
        DependencyProperty.Register(nameof(TabIndex), typeof(int), typeof(WindowsFormsHost), new FrameworkPropertyMetadata(0));

    private Forms.Control? _child;
    private Forms.Control? _focusedControl;
    private WpfContextMenu? _activeContextMenu;
    private Forms.ContextMenuStrip? _activeContextMenuStrip;
    private readonly ConditionalWeakTable<DrawingImage, CachedImageSource> _imageSourceCache = new();

    public event EventHandler<ChildChangedEventArgs>? ChildChanged;

    public event EventHandler<LayoutExceptionEventArgs>? LayoutError;

    static WindowsFormsHost()
    {
        Forms.ContextMenuStrip.ShowRequested += OnContextMenuStripShowRequested;
    }

    public WindowsFormsHost()
    {
        RegisterHost(this);
    }

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public Forms.Control? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value))
            {
                return;
            }

            Forms.Control? previous = _child;
            if (_child != null)
            {
                UnsubscribeInvalidationTree(_child);
            }

            _child = value;
            _focusedControl = null;
            if (_child != null)
            {
                _child.CreateControl();
                SubscribeInvalidationTree(_child);
            }

            ChildChanged?.Invoke(this, new ChildChangedEventArgs(previous));
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    [Bindable(true)]
    [Category("Behavior")]
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    public PropertyMap PropertyMap { get; } = new();

    [Bindable(true)]
    [Category("Behavior")]
    public int TabIndex
    {
        get => (int)GetValue(TabIndexProperty);
        set => SetValue(TabIndexProperty, value);
    }

    public static void EnableWindowsFormsInterop()
    {
    }

    public virtual bool TabInto(System.Windows.Input.TraversalRequest request)
    {
        Focus();
        return true;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_child == null)
        {
            return default;
        }

        double width = double.IsInfinity(availableSize.Width) ? _child.Size.Width : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? _child.Size.Height : availableSize.Height;
        if (width <= 0)
        {
            width = 120;
        }

        if (height <= 0)
        {
            height = 80;
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_child != null)
        {
            LayoutControlTree(_child, new Rect(0, 0, finalSize.Width, finalSize.Height));
        }

        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_child == null)
        {
            return;
        }

        RenderControl(drawingContext, _child, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_child == null)
        {
            return;
        }

        Point hostPoint = e.GetPosition(this);
        Forms.Control? target = FindControlAt(_child, hostPoint, out Point localPoint);
        if (target == null)
        {
            return;
        }

        Focus();
        target.Focus();
        _focusedControl = target;
        var mouseEventArgs = new Forms.MouseEventArgs(MapMouseButton(e.ChangedButton), e.ClickCount, ToWinFormsCoordinate(localPoint.X), ToWinFormsCoordinate(localPoint.Y), 0);
        target.RaiseMouseDown(mouseEventArgs);
        ApplyDefaultSelection(target, localPoint);

        if (e.ChangedButton == MouseButton.Left
            && target is Forms.ComboBox comboBox
            && TryShowComboBoxDropDown(comboBox))
        {
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right && TryShowContextMenu(target, localPoint))
        {
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_child == null)
        {
            return;
        }

        Point hostPoint = e.GetPosition(this);
        Forms.Control? target = FindControlAt(_child, hostPoint, out Point localPoint);
        if (target == null)
        {
            return;
        }

        var mouseEventArgs = new Forms.MouseEventArgs(MapMouseButton(e.ChangedButton), e.ClickCount, ToWinFormsCoordinate(localPoint.X), ToWinFormsCoordinate(localPoint.Y), 0);
        target.RaiseMouseUp(mouseEventArgs);
        target.RaiseMouseClick(mouseEventArgs);
        if (e.ChangedButton == MouseButton.Left && ApplyDefaultHeaderClick(target, localPoint))
        {
            e.Handled = true;
            return;
        }

        if (e.ClickCount >= 2)
        {
            target.RaiseMouseDoubleClick(mouseEventArgs);
            ApplyDefaultActivation(target, localPoint);
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        Forms.Control? target = GetFocusedControl();
        if (target == null)
        {
            return;
        }

        Forms.Keys keyData = MapKey(e.Key, Keyboard.Modifiers);
        if (keyData == Forms.Keys.None)
        {
            return;
        }

        var keyEventArgs = new Forms.KeyEventArgs(keyData);
        target.RaiseKeyDown(keyEventArgs);

        if (!keyEventArgs.Handled && (keyEventArgs.KeyCode == Forms.Keys.Enter || keyEventArgs.KeyCode == Forms.Keys.Return))
        {
            var keyPressEventArgs = new Forms.KeyPressEventArgs('\r');
            target.RaiseKeyPress(keyPressEventArgs);
            if (target is Forms.ListView listView && listView.SelectedItems.Count > 0)
            {
                listView.RaiseItemActivate();
                keyPressEventArgs.Handled = true;
            }

            keyEventArgs.Handled = keyPressEventArgs.Handled;
        }

        e.Handled = keyEventArgs.Handled || keyEventArgs.SuppressKeyPress;
    }

    protected override void OnKeyUp(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyUp(e);
        Forms.Control? target = GetFocusedControl();
        if (target == null)
        {
            return;
        }

        Forms.Keys keyData = MapKey(e.Key, Keyboard.Modifiers);
        if (keyData == Forms.Keys.None)
        {
            return;
        }

        var keyEventArgs = new Forms.KeyEventArgs(keyData);
        target.RaiseKeyUp(keyEventArgs);
        e.Handled = keyEventArgs.Handled || keyEventArgs.SuppressKeyPress;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        Forms.Control? target = GetFocusedControl();
        if (target == null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        bool handled = false;
        foreach (char ch in e.Text)
        {
            var keyPressEventArgs = new Forms.KeyPressEventArgs(ch);
            target.RaiseKeyPress(keyPressEventArgs);
            handled |= keyPressEventArgs.Handled;
            if (!keyPressEventArgs.Handled && target is Forms.TextBoxBase textBoxBase)
            {
                textBoxBase.ApplyTextInput(ch.ToString(CultureInfo.InvariantCulture));
                handled = true;
            }
        }

        e.Handled = handled;
    }

    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new WindowsFormsHostAutomationPeer(this);
    }

    private static bool TryShowContextMenu(Forms.Control target, Point localPoint)
    {
        Forms.ContextMenuStrip? contextMenuStrip = FindContextMenuStrip(target);
        if (contextMenuStrip == null)
        {
            return false;
        }

        contextMenuStrip.Show(target, new System.Drawing.Point(ToWinFormsCoordinate(localPoint.X), ToWinFormsCoordinate(localPoint.Y)));
        return contextMenuStrip.Visible;
    }

    private bool ShowContextMenuStrip(Forms.ContextMenuStrip contextMenuStrip, Point hostPoint)
    {
        var contextMenu = new WpfContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = hostPoint.X,
            VerticalOffset = hostPoint.Y
        };

        CloseActiveContextMenu();
        foreach (Forms.ToolStripItem item in contextMenuStrip.Items)
        {
            if (CreateContextMenuItem(contextMenuStrip, item) is object menuItem)
            {
                contextMenu.Items.Add(menuItem);
            }
        }

        bool closingFromWpf = false;
        bool closingFromStrip = false;
        EventHandler stripClosed = null!;
        stripClosed = delegate
        {
            if (!closingFromWpf && contextMenu.IsOpen)
            {
                closingFromStrip = true;
                contextMenu.IsOpen = false;
                closingFromStrip = false;
            }

            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
                _activeContextMenuStrip = null;
            }

            contextMenuStrip.Closed -= stripClosed;
        };
        contextMenuStrip.Closed += stripClosed;

        contextMenu.Closed += delegate
        {
            contextMenuStrip.Closed -= stripClosed;
            if (!closingFromStrip)
            {
                closingFromWpf = true;
                contextMenuStrip.Close();
                closingFromWpf = false;
            }

            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
                _activeContextMenuStrip = null;
            }
        };

        _activeContextMenu = contextMenu;
        _activeContextMenuStrip = contextMenuStrip;
        contextMenu.IsOpen = true;
        return true;
    }

    private static void OnContextMenuStripShowRequested(object? sender, Forms.ContextMenuStripShowRequestedEventArgs e)
    {
        List<WindowsFormsHost> hosts = GetRegisteredHosts();
        foreach (WindowsFormsHost host in hosts)
        {
            if (host.TryShowContextMenuStripForControl(e.ContextMenuStrip, e.Control, e.Position))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private static List<WindowsFormsHost> GetRegisteredHosts()
    {
        var hosts = new List<WindowsFormsHost>();
        lock (s_registeredHostsGate)
        {
            for (int i = s_registeredHosts.Count - 1; i >= 0; i--)
            {
                if (s_registeredHosts[i].TryGetTarget(out WindowsFormsHost? host))
                {
                    hosts.Add(host);
                }
                else
                {
                    s_registeredHosts.RemoveAt(i);
                }
            }
        }

        return hosts;
    }

    private static void RegisterHost(WindowsFormsHost host)
    {
        lock (s_registeredHostsGate)
        {
            for (int i = s_registeredHosts.Count - 1; i >= 0; i--)
            {
                if (!s_registeredHosts[i].TryGetTarget(out WindowsFormsHost? existing))
                {
                    s_registeredHosts.RemoveAt(i);
                    continue;
                }

                if (ReferenceEquals(existing, host))
                {
                    return;
                }
            }

            s_registeredHosts.Add(new WeakReference<WindowsFormsHost>(host));
        }
    }

    private bool TryShowContextMenuStripForControl(Forms.ContextMenuStrip contextMenuStrip, Forms.Control control, System.Drawing.Point position)
    {
        if (_child == null || !TryGetHostPoint(_child, control, position, out Point hostPoint))
        {
            return false;
        }

        return ShowContextMenuStrip(contextMenuStrip, hostPoint);
    }

    private bool TryShowComboBoxDropDown(Forms.ComboBox comboBox)
    {
        if (_child == null
            || comboBox.Items.Count == 0
            || !TryGetHostPoint(_child, comboBox, new System.Drawing.Point(0, comboBox.Height), out Point hostPoint))
        {
            return false;
        }

        var contextMenu = new WpfContextMenu
        {
            PlacementTarget = this,
            Placement = PlacementMode.RelativePoint,
            HorizontalOffset = hostPoint.X,
            VerticalOffset = hostPoint.Y,
            MinWidth = Math.Max(0, comboBox.Width)
        };

        CloseActiveContextMenu();
        for (int i = 0; i < comboBox.Items.Count; i++)
        {
            int itemIndex = i;
            var menuItem = new WpfMenuItem
            {
                Header = comboBox.Items[i]?.ToString() ?? string.Empty,
                IsCheckable = true,
                IsChecked = itemIndex == comboBox.SelectedIndex
            };
            menuItem.Click += delegate
            {
                comboBox.SelectedIndex = itemIndex;
                contextMenu.IsOpen = false;
            };
            contextMenu.Items.Add(menuItem);
        }

        contextMenu.Closed += delegate
        {
            comboBox.DroppedDown = false;
            if (ReferenceEquals(_activeContextMenu, contextMenu))
            {
                _activeContextMenu = null;
                _activeContextMenuStrip = null;
            }
        };

        _activeContextMenu = contextMenu;
        _activeContextMenuStrip = null;
        comboBox.DroppedDown = true;
        contextMenu.IsOpen = true;
        return true;
    }

    private static bool TryGetHostPoint(Forms.Control root, Forms.Control source, System.Drawing.Point sourcePoint, out Point hostPoint)
    {
        double x = sourcePoint.X;
        double y = sourcePoint.Y;
        for (Forms.Control? current = source; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                hostPoint = new Point(x, y);
                return true;
            }

            x += current.Left;
            y += current.Top;
        }

        hostPoint = default;
        return false;
    }

    private void CloseActiveContextMenu()
    {
        if (_activeContextMenu != null)
        {
            _activeContextMenu.IsOpen = false;
        }
        else
        {
            _activeContextMenuStrip?.Close();
        }

        _activeContextMenu = null;
        _activeContextMenuStrip = null;
    }

    private Forms.Control? GetFocusedControl()
    {
        if (_focusedControl != null && _child != null && IsControlInTree(_child, _focusedControl))
        {
            return _focusedControl;
        }

        return _child;
    }

    private static bool IsControlInTree(Forms.Control root, Forms.Control target)
    {
        if (ReferenceEquals(root, target))
        {
            return true;
        }

        foreach (Forms.Control child in root.Controls)
        {
            if (IsControlInTree(child, target))
            {
                return true;
            }
        }

        return false;
    }

    private static Forms.ContextMenuStrip? FindContextMenuStrip(Forms.Control control)
    {
        for (Forms.Control? current = control; current != null; current = current.Parent)
        {
            if (current.ContextMenuStrip != null)
            {
                return current.ContextMenuStrip;
            }
        }

        return null;
    }

    private static object? CreateContextMenuItem(Forms.ContextMenuStrip owner, Forms.ToolStripItem item)
    {
        if (!item.Visible || !item.Available)
        {
            return null;
        }

        if (item is Forms.ToolStripSeparator)
        {
            return new WpfSeparator();
        }

        string text = string.IsNullOrEmpty(item.Text) ? item.Name : item.Text;
        var menuItem = new WpfMenuItem
        {
            Header = text,
            IsEnabled = item.Enabled
        };

        if (item is Forms.ToolStripMenuItem toolStripMenuItem)
        {
            menuItem.IsCheckable = toolStripMenuItem.CheckOnClick;
            menuItem.IsChecked = toolStripMenuItem.Checked;
            foreach (Forms.ToolStripItem child in toolStripMenuItem.DropDownItems)
            {
                if (CreateContextMenuItem(owner, child) is object childItem)
                {
                    menuItem.Items.Add(childItem);
                }
            }
        }

        if (menuItem.Items.Count == 0)
        {
            menuItem.Click += delegate
            {
                if (item is Forms.ToolStripMenuItem clickedMenuItem && clickedMenuItem.CheckOnClick)
                {
                    clickedMenuItem.Checked = !clickedMenuItem.Checked;
                    menuItem.IsChecked = clickedMenuItem.Checked;
                }

                item.PerformClick();
                owner.Close();
            };
        }

        return menuItem;
    }

    private static void ApplyDefaultSelection(Forms.Control target, Point localPoint)
    {
        if (target is Forms.CheckedListBox checkedListBox)
        {
            int x = ToWinFormsCoordinate(localPoint.X);
            int y = ToWinFormsCoordinate(localPoint.Y);
            int index = checkedListBox.IndexFromPoint(new System.Drawing.Point(x, y));
            if (index >= 0 && index < checkedListBox.Items.Count)
            {
                checkedListBox.SelectedIndex = index;
                checkedListBox.TryToggleItemAt(x, y);
            }
        }
        else if (target is Forms.ComboBox)
        {
        }
        else if (target is Forms.ListBox listBox)
        {
            int index = listBox.IndexFromPoint(new System.Drawing.Point(ToWinFormsCoordinate(localPoint.X), ToWinFormsCoordinate(localPoint.Y)));
            if (index >= 0 && index < listBox.Items.Count)
            {
                listBox.SelectedIndex = index;
            }
        }
        else if (target is Forms.TreeView treeView)
        {
            Forms.TreeNode? node = treeView.GetNodeAt(ToWinFormsCoordinate(localPoint.X), ToWinFormsCoordinate(localPoint.Y));
            if (node != null)
            {
                treeView.SelectedNode = node;
                treeView.Invalidate();
            }
        }
        else if (target is Forms.ListView listView)
        {
            int x = ToWinFormsCoordinate(localPoint.X);
            int y = ToWinFormsCoordinate(localPoint.Y);
            if (listView.TryToggleItemCheckAt(x, y))
            {
                return;
            }

            Forms.ListViewItem? item = listView.GetItemAt(x, y);
            if (item != null)
            {
                item.Selected = true;
                listView.Invalidate();
            }
        }
    }

    private static void ApplyDefaultActivation(Forms.Control target, Point localPoint)
    {
        if (target is Forms.ListView listView)
        {
            listView.TryActivateItemAt(
                ToWinFormsCoordinate(localPoint.X),
                ToWinFormsCoordinate(localPoint.Y));
        }
    }

    private static bool ApplyDefaultHeaderClick(Forms.Control target, Point localPoint)
    {
        return target is Forms.ListView listView
            && listView.TryRaiseColumnClickAt(
                ToWinFormsCoordinate(localPoint.X),
                ToWinFormsCoordinate(localPoint.Y));
    }

    private static Forms.Control? FindControlAt(Forms.Control root, Point hostPoint, out Point localPoint)
    {
        return FindControlAt(root, new Point(0, 0), hostPoint, out localPoint);
    }

    private static Forms.Control? FindControlAt(Forms.Control control, Point parentOrigin, Point hostPoint, out Point localPoint)
    {
        Point origin = new(parentOrigin.X + control.Left, parentOrigin.Y + control.Top);
        Rect bounds = new(origin.X, origin.Y, control.Width, control.Height);
        if (!control.Visible || !bounds.Contains(hostPoint))
        {
            localPoint = default;
            return null;
        }

        for (int i = control.Controls.Count - 1; i >= 0; i--)
        {
            Forms.Control child = control.Controls[i];
            Forms.Control? result = FindControlAt(child, origin, hostPoint, out localPoint);
            if (result != null)
            {
                return result;
            }
        }

        localPoint = new Point(hostPoint.X - origin.X, hostPoint.Y - origin.Y);
        return control;
    }

    private static Forms.MouseButtons MapMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => Forms.MouseButtons.Left,
            MouseButton.Right => Forms.MouseButtons.Right,
            MouseButton.Middle => Forms.MouseButtons.Middle,
            _ => Forms.MouseButtons.None
        };
    }

    private static int ToWinFormsCoordinate(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return (int)Math.Round(value);
    }

    private static Forms.Keys MapKey(System.Windows.Input.Key key, ModifierKeys modifiers)
    {
        Forms.Keys keyData = key switch
        {
            System.Windows.Input.Key.Enter => Forms.Keys.Enter,
            System.Windows.Input.Key.Tab => Forms.Keys.Tab,
            System.Windows.Input.Key.Escape => Forms.Keys.Escape,
            System.Windows.Input.Key.Space => Forms.Keys.Space,
            System.Windows.Input.Key.Back => Forms.Keys.Back,
            System.Windows.Input.Key.Delete => Forms.Keys.Delete,
            System.Windows.Input.Key.Home => Forms.Keys.Home,
            System.Windows.Input.Key.End => Forms.Keys.End,
            System.Windows.Input.Key.PageUp => Forms.Keys.PageUp,
            System.Windows.Input.Key.PageDown => Forms.Keys.PageDown,
            System.Windows.Input.Key.Up => Forms.Keys.Up,
            System.Windows.Input.Key.Down => Forms.Keys.Down,
            System.Windows.Input.Key.F2 => Forms.Keys.F2,
            >= System.Windows.Input.Key.A and <= System.Windows.Input.Key.Z
                => (Forms.Keys)((int)Forms.Keys.A + ((int)key - (int)System.Windows.Input.Key.A)),
            >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9
                => (Forms.Keys)((int)Forms.Keys.D0 + ((int)key - (int)System.Windows.Input.Key.D0)),
            _ => Forms.Keys.None
        };

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            keyData |= Forms.Keys.Control;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            keyData |= Forms.Keys.Shift;
        }

        return keyData;
    }

    private void OnChildInvalidated(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void SubscribeInvalidationTree(Forms.Control control)
    {
        control.Invalidated += OnChildInvalidated;
        foreach (Forms.Control child in control.Controls)
        {
            SubscribeInvalidationTree(child);
        }
    }

    private void UnsubscribeInvalidationTree(Forms.Control control)
    {
        control.Invalidated -= OnChildInvalidated;
        foreach (Forms.Control child in control.Controls)
        {
            UnsubscribeInvalidationTree(child);
        }
    }

    private static void LayoutControlTree(Forms.Control control, Rect bounds)
    {
        int width = Math.Max(0, (int)Math.Round(bounds.Width));
        int height = Math.Max(0, (int)Math.Round(bounds.Height));
        control.Location = new System.Drawing.Point((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y));
        control.Size = new System.Drawing.Size(width, height);

        if (control.Controls.Count == 0)
        {
            return;
        }

        if (control is Forms.SplitContainer splitContainer)
        {
            LayoutSplitContainer(splitContainer, width, height);
            return;
        }

        if (control is Forms.TabControl tabControl)
        {
            LayoutTabControl(tabControl, width, height);
            return;
        }

        int top = 0;
        int bottom = height;
        var fillControls = new List<Forms.Control>();
        foreach (Forms.Control child in control.Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            switch (child.Dock)
            {
                case Forms.DockStyle.Top:
                    int topHeight = GetPreferredHeight(child);
                    LayoutControlTree(child, new Rect(0, top, width, topHeight));
                    top += topHeight;
                    break;
                case Forms.DockStyle.Bottom:
                    int bottomHeight = GetPreferredHeight(child);
                    bottom -= bottomHeight;
                    LayoutControlTree(child, new Rect(0, bottom, width, bottomHeight));
                    break;
                case Forms.DockStyle.Left:
                    int leftWidth = GetPreferredWidth(child);
                    LayoutControlTree(child, new Rect(0, top, leftWidth, Math.Max(0, bottom - top)));
                    break;
                case Forms.DockStyle.Right:
                    int rightWidth = GetPreferredWidth(child);
                    LayoutControlTree(child, new Rect(Math.Max(0, width - rightWidth), top, rightWidth, Math.Max(0, bottom - top)));
                    break;
                case Forms.DockStyle.Fill:
                    fillControls.Add(child);
                    break;
                default:
                    LayoutControlTree(child, new Rect(child.Left, child.Top, child.Width, child.Height));
                    break;
            }
        }

        foreach (Forms.Control child in fillControls)
        {
            LayoutControlTree(child, new Rect(0, top, width, Math.Max(0, bottom - top)));
        }
    }

    private static void LayoutSplitContainer(Forms.SplitContainer splitContainer, int width, int height)
    {
        const int splitterSize = 4;
        if (splitContainer.Orientation == Forms.Orientation.Horizontal)
        {
            int available = Math.Max(0, height - splitterSize);
            int distance = splitContainer.SplitterDistance > 0 ? splitContainer.SplitterDistance : available / 2;
            distance = Math.Clamp(distance, 0, available);
            LayoutControlTree(splitContainer.Panel1, new Rect(0, 0, width, distance));
            LayoutControlTree(splitContainer.Panel2, new Rect(0, distance + splitterSize, width, Math.Max(0, height - distance - splitterSize)));
        }
        else
        {
            int available = Math.Max(0, width - splitterSize);
            int distance = splitContainer.SplitterDistance > 0 ? splitContainer.SplitterDistance : available / 2;
            distance = Math.Clamp(distance, 0, available);
            LayoutControlTree(splitContainer.Panel1, new Rect(0, 0, distance, height));
            LayoutControlTree(splitContainer.Panel2, new Rect(distance + splitterSize, 0, Math.Max(0, width - distance - splitterSize), height));
        }
    }

    private static void LayoutTabControl(Forms.TabControl tabControl, int width, int height)
    {
        const int tabHeaderHeight = 24;
        int contentTop = Math.Min(tabHeaderHeight, height);
        int contentWidth = Math.Max(0, width - 4);
        int contentHeight = Math.Max(0, height - contentTop - 2);

        foreach (Forms.TabPage page in tabControl.TabPages)
        {
            LayoutControlTree(page, new Rect(2, contentTop, contentWidth, contentHeight));
        }
    }

    private static int GetPreferredHeight(Forms.Control control)
    {
        if (control.Height > 0)
        {
            return control.Height;
        }

        return control is Forms.ToolStrip ? 24 : 20;
    }

    private static int GetPreferredWidth(Forms.Control control)
    {
        if (control.Width > 0)
        {
            return control.Width;
        }

        return 120;
    }

    private void RenderControl(DrawingContext drawingContext, Forms.Control control, Rect bounds)
    {
        if (!control.Visible || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        drawingContext.PushClip(new RectangleGeometry(bounds));
        try
        {
            Brush background = CreateBrush(control.BackColor, Background ?? SystemColors.ControlBrush);
            Brush foreground = CreateBrush(control.ForeColor, Foreground);
            drawingContext.DrawRectangle(background, null, bounds);

            if (control is Forms.TreeView treeView)
            {
                RenderTreeView(drawingContext, treeView, bounds, foreground);
                return;
            }

            if (control is Forms.TabControl tabControl)
            {
                RenderTabControl(drawingContext, tabControl, bounds, foreground);
                return;
            }

            if (control is Forms.SplitContainer splitContainer)
            {
                RenderSplitContainer(drawingContext, splitContainer, bounds);
            }
            else if (control is Forms.PropertyGrid propertyGrid)
            {
                RenderPropertyGrid(drawingContext, propertyGrid, bounds, foreground);
            }
            else if (control is Forms.DataGridView dataGridView)
            {
                RenderDataGridView(drawingContext, dataGridView, bounds, foreground);
            }
            else if (control is Forms.ComboBox comboBox)
            {
                RenderComboBox(drawingContext, comboBox, bounds, foreground);
            }
            else if (control is Forms.CheckedListBox checkedListBox)
            {
                RenderListBox(drawingContext, checkedListBox, bounds, foreground, true);
            }
            else if (control is Forms.ListBox listBox)
            {
                RenderListBox(drawingContext, listBox, bounds, foreground, false);
            }
            else if (control is Forms.ListView listView)
            {
                RenderListView(drawingContext, listView, bounds, foreground);
            }
            else if (control is Forms.ToolStrip toolStrip)
            {
                RenderToolStrip(drawingContext, toolStrip, bounds, foreground);
            }
            else if (control is Forms.TabPage)
            {
                drawingContext.DrawRectangle(SystemColors.ControlBrush, null, bounds);
            }
            else if (!string.IsNullOrEmpty(control.Text))
            {
                DrawText(drawingContext, control.Text, new Point(bounds.X + 4, bounds.Y + 3), foreground, 12);
            }

            foreach (Forms.Control child in control.Controls)
            {
                Rect childBounds = new(bounds.X + child.Left, bounds.Y + child.Top, child.Width, child.Height);
                RenderControl(drawingContext, child, childBounds);
            }
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    private void RenderSplitContainer(DrawingContext drawingContext, Forms.SplitContainer splitContainer, Rect bounds)
    {
        const double splitterSize = 4;
        Pen splitterPen = new(SystemColors.ControlDarkBrush, 1);
        if (splitContainer.Orientation == Forms.Orientation.Horizontal)
        {
            double splitterTop = bounds.Y + splitContainer.Panel1.Height;
            drawingContext.DrawRectangle(SystemColors.ControlBrush, splitterPen, new Rect(bounds.X, splitterTop, bounds.Width, splitterSize));
        }
        else
        {
            double splitterLeft = bounds.X + splitContainer.Panel1.Width;
            drawingContext.DrawRectangle(SystemColors.ControlBrush, splitterPen, new Rect(splitterLeft, bounds.Y, splitterSize, bounds.Height));
        }
    }

    private void RenderTabControl(DrawingContext drawingContext, Forms.TabControl tabControl, Rect bounds, Brush foreground)
    {
        const double tabHeaderHeight = 24;
        drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), bounds);

        double x = bounds.X + 2;
        for (int i = 0; i < tabControl.TabPages.Count; i++)
        {
            Forms.TabPage page = tabControl.TabPages[i];
            string text = string.IsNullOrEmpty(page.Text) ? page.Name : page.Text;
            double width = Math.Max(56, MeasureText(text, 12) + 18);
            Rect tabBounds = new(x, bounds.Y + 2, width, tabHeaderHeight - 2);
            bool selected = i == tabControl.SelectedIndex;
            drawingContext.DrawRectangle(
                selected ? SystemColors.WindowBrush : SystemColors.ControlBrush,
                new Pen(SystemColors.ControlDarkBrush, 1),
                tabBounds);
            DrawTextInBounds(drawingContext, text, new Rect(tabBounds.X + 8, tabBounds.Y + 4, Math.Max(0, tabBounds.Width - 16), tabBounds.Height - 6), foreground, 12);
            x += width - 1;
            if (x > bounds.Right)
            {
                break;
            }
        }

        Rect contentBounds = new(bounds.X + 1, bounds.Y + tabHeaderHeight, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Height - tabHeaderHeight - 1));
        drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlDarkBrush, 1), contentBounds);

        Forms.TabPage? selectedPage = tabControl.SelectedTab;
        if (selectedPage != null)
        {
            Rect pageBounds = new(bounds.X + selectedPage.Left, bounds.Y + selectedPage.Top, selectedPage.Width, selectedPage.Height);
            RenderControl(drawingContext, selectedPage, pageBounds);
        }
    }

    private void RenderComboBox(DrawingContext drawingContext, Forms.ComboBox comboBox, Rect bounds, Brush foreground)
    {
        Pen borderPen = new(SystemColors.ControlDarkBrush, 1);
        drawingContext.DrawRectangle(SystemColors.WindowBrush, borderPen, bounds);

        string text = comboBox.SelectedItem?.ToString() ?? comboBox.Text;
        Rect textBounds = new(bounds.X + 5, bounds.Y + 2, Math.Max(0, bounds.Width - 24), Math.Max(0, bounds.Height - 4));
        ImageSource? ownerDrawSource = null;
        bool ownerDrawn = comboBox.SelectedIndex >= 0
            && comboBox.DrawMode != Forms.DrawMode.Normal
            && TryRenderListItemOwnerDraw(drawingContext, comboBox, comboBox.SelectedIndex, bounds, textBounds, out ownerDrawSource);
        if (ownerDrawn)
        {
            if (ownerDrawSource != null)
            {
                drawingContext.DrawImage(ownerDrawSource, textBounds);
            }
        }
        else
        {
            DrawTextInBounds(drawingContext, text, textBounds, comboBox.Enabled ? foreground : SystemColors.GrayTextBrush, 12);
        }

        Rect buttonBounds = new(Math.Max(bounds.X, bounds.Right - 18), bounds.Y + 1, 17, Math.Max(0, bounds.Height - 2));
        drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), buttonBounds);
        Point p1 = new(buttonBounds.X + 5, buttonBounds.Y + Math.Max(6, buttonBounds.Height / 2 - 1));
        Point p2 = new(buttonBounds.X + 12, p1.Y);
        Point p3 = new(buttonBounds.X + 8.5, p1.Y + 4);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(p1, true, true);
            context.LineTo(p2, true, false);
            context.LineTo(p3, true, false);
        }
        drawingContext.DrawGeometry(SystemColors.ControlTextBrush, null, geometry);
    }

    private void RenderListBox(DrawingContext drawingContext, Forms.ListBox listBox, Rect bounds, Brush foreground, bool checkedItems)
    {
        DrawBorder(drawingContext, listBox.BorderStyle, bounds);
        const double lineHeight = 18;
        double y = bounds.Y + 2;
        for (int i = 0; i < listBox.Items.Count && y < bounds.Bottom; i++)
        {
            Rect rowBounds = new(bounds.X + 1, y, Math.Max(0, bounds.Width - 2), lineHeight);
            bool selected = i == listBox.SelectedIndex;
            if (selected)
            {
                drawingContext.DrawRectangle(SystemColors.HighlightBrush, null, rowBounds);
            }

            double textX = rowBounds.X + 4;
            if (checkedItems && listBox is Forms.CheckedListBox checkedListBox)
            {
                Rect checkBounds = new(rowBounds.X + 4, rowBounds.Y + 3, 12, 12);
                drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlDarkBrush, 1), checkBounds);
                if (checkedListBox.GetItemChecked(i))
                {
                    DrawText(drawingContext, "x", new Point(checkBounds.X + 2, checkBounds.Y - 1), SystemColors.ControlTextBrush, 11);
                }

                textX += 18;
            }

            Rect itemTextBounds = new(textX, rowBounds.Y + 1, Math.Max(0, rowBounds.Right - textX - 2), lineHeight - 2);
            ImageSource? ownerDrawSource = null;
            bool ownerDrawn = listBox.DrawMode != Forms.DrawMode.Normal
                && TryRenderListItemOwnerDraw(drawingContext, listBox, i, bounds, itemTextBounds, out ownerDrawSource);
            if (ownerDrawn)
            {
                if (ownerDrawSource != null)
                {
                    drawingContext.DrawImage(ownerDrawSource, itemTextBounds);
                }
            }
            else
            {
                string text = listBox.Items[i]?.ToString() ?? string.Empty;
                DrawTextInBounds(
                    drawingContext,
                    text,
                    itemTextBounds,
                    selected ? SystemColors.HighlightTextBrush : foreground,
                    12);
            }
            y += lineHeight;
        }
    }

    private static bool TryRenderListItemOwnerDraw(
        DrawingContext drawingContext,
        Forms.ListBox listBox,
        int index,
        Rect controlBounds,
        Rect itemBounds,
        out ImageSource? imageSource)
    {
        imageSource = null;
        int bitmapWidth = Math.Max(1, (int)Math.Ceiling(itemBounds.Width));
        int bitmapHeight = Math.Max(1, (int)Math.Ceiling(itemBounds.Height));
        DrawingRectangle drawBounds = new(
            (int)Math.Round(itemBounds.X),
            (int)Math.Round(itemBounds.Y),
            bitmapWidth,
            bitmapHeight);

        Forms.DrawItemState state = Forms.DrawItemState.None;
        if (index == listBox.SelectedIndex)
        {
            state |= Forms.DrawItemState.Selected;
        }

        if (!listBox.Enabled)
        {
            state |= Forms.DrawItemState.Disabled;
        }

        drawingContext.PushClip(new RectangleGeometry(itemBounds));
        try
        {
            if (TryGetNativeDrawingContext(drawingContext, out var nativeContext, out Matrix4x4 outerTransform))
            {
                Matrix4x4 clientTransform = Matrix4x4.CreateTranslation(
                    (float)controlBounds.X, (float)controlBounds.Y, 0f) * outerTransform;
                DrawingRectangle clientBounds = new(
                    (int)Math.Round(itemBounds.X - controlBounds.X),
                    (int)Math.Round(itemBounds.Y - controlBounds.Y),
                    bitmapWidth,
                    bitmapHeight);
                using DrawingGraphics nativeGraphics = DrawingGraphics.FromProGpuDrawingContext(
                    nativeContext, clientTransform);
                listBox.RaiseDrawItem(new Forms.DrawItemEventArgs(
                    nativeGraphics, listBox.Font, clientBounds, index, state));
                return true;
            }

            using DrawingBitmap bitmap = new(bitmapWidth, bitmapHeight, DrawingPixelFormat.Format32bppPArgb);
            using DrawingGraphics graphics = DrawingGraphics.FromImage(bitmap);
            graphics.Clear(DrawingColor.Transparent);
            graphics.TranslateTransform(-drawBounds.X, -drawBounds.Y);

            Forms.DrawItemEventArgs eventArgs = new(graphics, listBox.Font, drawBounds, index, state);
            listBox.RaiseDrawItem(eventArgs);
            imageSource = CreateImageSource(bitmap);
            return imageSource != null;
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    private static bool TryGetNativeDrawingContext(
        DrawingContext drawingContext,
        out ProGPU.Scene.DrawingContext nativeContext,
        out Matrix4x4 outerTransform)
    {
        nativeContext = null!;
        outerTransform = Matrix4x4.Identity;
        if (drawingContext is IPortableNativeDrawingContextStateSource nativeContextStateSource
            && nativeContextStateSource.TryGetPortableNativeDrawingContextState(out var portableState)
            && ProGPU.Scene.ProGpuDrawingContextState.TryCreate(
                portableState.NativeDrawingContext,
                portableState.Transform,
                out ProGPU.Scene.ProGpuDrawingContextState state))
        {
            nativeContext = state.DrawingContext;
            outerTransform = state.OuterTransform;
            return true;
        }

        if (drawingContext is not IPortableNativeDrawingContextSource nativeContextSource
            || !nativeContextSource.TryGetPortableNativeDrawingContext(out object? nativeContextObject)
            || nativeContextObject is not ProGPU.Scene.DrawingContext resolvedContext)
        {
            return false;
        }

        nativeContext = resolvedContext;
        return true;
    }

    private void RenderPropertyGrid(DrawingContext drawingContext, Forms.PropertyGrid propertyGrid, Rect bounds, Brush foreground)
    {
        DrawBorder(drawingContext, Forms.BorderStyle.Fixed3D, bounds);

        double y = bounds.Y + 1;
        if (propertyGrid.ToolbarVisible)
        {
            Rect toolbarBounds = new(bounds.X + 1, y, Math.Max(0, bounds.Width - 2), 22);
            drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), toolbarBounds);
            string selectionText = propertyGrid.SelectedObject?.GetType().Name ?? "Properties";
            if (propertyGrid.SelectedObjectCount > 1)
            {
                selectionText += " (" + propertyGrid.SelectedObjectCount.ToString(CultureInfo.CurrentCulture) + ")";
            }

            DrawTextInBounds(drawingContext, selectionText, new Rect(toolbarBounds.X + 5, toolbarBounds.Y + 3, Math.Max(0, toolbarBounds.Width - 10), 17), foreground, 12);
            y = toolbarBounds.Bottom;
        }

        double helpHeight = propertyGrid.HelpVisible ? Math.Min(42, Math.Max(0, bounds.Height * 0.18)) : 0;
        double rowsBottom = Math.Max(y, bounds.Bottom - helpHeight);
        double nameWidth = Math.Max(70, Math.Min(bounds.Width * 0.48, bounds.Width - 80));
        const double rowHeight = 18;

        foreach (Forms.PropertyGridDisplayRow row in propertyGrid.DisplayRows)
        {
            if (y + rowHeight > rowsBottom)
            {
                break;
            }

            Rect rowBounds = new(bounds.X + 1, y, Math.Max(0, bounds.Width - 2), rowHeight);
            if (row.IsCategory)
            {
                drawingContext.DrawRectangle(SystemColors.ControlLightBrush, new Pen(SystemColors.ControlDarkBrush, 1), rowBounds);
                DrawTextInBounds(drawingContext, row.Label, new Rect(rowBounds.X + 4, rowBounds.Y + 1, Math.Max(0, rowBounds.Width - 8), rowHeight - 2), foreground, 12);
            }
            else
            {
                drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlLightBrush, 1), rowBounds);
                Rect nameBounds = new(rowBounds.X + 4, rowBounds.Y + 1, Math.Max(0, nameWidth - 6), rowHeight - 2);
                Rect valueBounds = new(rowBounds.X + nameWidth + 4, rowBounds.Y + 1, Math.Max(0, rowBounds.Width - nameWidth - 8), rowHeight - 2);
                drawingContext.DrawLine(new Pen(SystemColors.ControlLightBrush, 1), new Point(rowBounds.X + nameWidth, rowBounds.Y), new Point(rowBounds.X + nameWidth, rowBounds.Bottom));
                DrawTextInBounds(drawingContext, row.Label, nameBounds, foreground, 12);
                DrawTextInBounds(drawingContext, row.ValueText, valueBounds, foreground, 12);
            }

            y += rowHeight;
        }

        if (helpHeight > 0)
        {
            Rect helpBounds = new(bounds.X + 1, rowsBottom, Math.Max(0, bounds.Width - 2), Math.Max(0, bounds.Bottom - rowsBottom - 1));
            drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), helpBounds);
            string helpText = propertyGrid.SelectedGridItem?.PropertyDescriptor?.Description ?? string.Empty;
            if (string.IsNullOrEmpty(helpText))
            {
                helpText = propertyGrid.SelectedGridItem?.Label ?? string.Empty;
            }

            DrawTextInBounds(drawingContext, helpText, new Rect(helpBounds.X + 5, helpBounds.Y + 4, Math.Max(0, helpBounds.Width - 10), Math.Max(0, helpBounds.Height - 8)), foreground, 11);
        }
    }

    private void RenderDataGridView(DrawingContext drawingContext, Forms.DataGridView dataGridView, Rect bounds, Brush foreground)
    {
        DrawBorder(drawingContext, Forms.BorderStyle.Fixed3D, bounds);

        double rowHeaderWidth = Math.Max(0, Math.Min(bounds.Width * 0.35, dataGridView.RowHeadersWidth));
        const double headerHeight = 22;
        const double rowHeight = 20;
        double x = bounds.X + 1 + rowHeaderWidth;
        double y = bounds.Y + 1;
        double bodyRight = bounds.Right - 1;

        if (rowHeaderWidth > 0)
        {
            drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), new Rect(bounds.X + 1, y, rowHeaderWidth, headerHeight));
        }

        foreach (Forms.DataGridViewColumn column in dataGridView.Columns)
        {
            double columnWidth = GetDataGridViewColumnWidth(dataGridView, column, bounds.Width - rowHeaderWidth - 2);
            Rect headerBounds = new(x, y, columnWidth, headerHeight);
            drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), headerBounds);
            string header = string.IsNullOrEmpty(column.HeaderText) ? column.Name : column.HeaderText;
            DrawTextInBounds(drawingContext, header, new Rect(headerBounds.X + 4, headerBounds.Y + 3, Math.Max(0, headerBounds.Width - 8), headerHeight - 4), foreground, 12);
            x += columnWidth;
            if (x > bodyRight)
            {
                break;
            }
        }

        y += headerHeight;
        for (int rowIndex = 0; rowIndex < dataGridView.Rows.Count && y + rowHeight <= bounds.Bottom - 1; rowIndex++)
        {
            Forms.DataGridViewRow row = dataGridView.Rows[rowIndex];
            if (rowHeaderWidth > 0)
            {
                Rect rowHeaderBounds = new(bounds.X + 1, y, rowHeaderWidth, rowHeight);
                drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlLightBrush, 1), rowHeaderBounds);
                DrawTextInBounds(drawingContext, (rowIndex + 1).ToString(CultureInfo.CurrentCulture), new Rect(rowHeaderBounds.X + 3, rowHeaderBounds.Y + 2, Math.Max(0, rowHeaderBounds.Width - 6), rowHeight - 4), foreground, 11);
            }

            x = bounds.X + 1 + rowHeaderWidth;
            for (int columnIndex = 0; columnIndex < dataGridView.Columns.Count && x < bodyRight; columnIndex++)
            {
                Forms.DataGridViewColumn column = dataGridView.Columns[columnIndex];
                double columnWidth = GetDataGridViewColumnWidth(dataGridView, column, bounds.Width - rowHeaderWidth - 2);
                Rect cellBounds = new(x, y, columnWidth, rowHeight);
                bool current = ReferenceEquals(dataGridView.CurrentCell, columnIndex < row.Cells.Count ? row.Cells[columnIndex] : null);
                drawingContext.DrawRectangle(current ? SystemColors.HighlightBrush : SystemColors.WindowBrush, new Pen(SystemColors.ControlLightBrush, 1), cellBounds);
                string text = columnIndex < row.Cells.Count ? Convert.ToString(row.Cells[columnIndex].Value, CultureInfo.CurrentCulture) ?? string.Empty : string.Empty;
                DrawTextInBounds(drawingContext, text, new Rect(cellBounds.X + 4, cellBounds.Y + 2, Math.Max(0, cellBounds.Width - 8), rowHeight - 4), current ? SystemColors.HighlightTextBrush : foreground, 12);
                x += columnWidth;
            }

            y += rowHeight;
        }
    }

    private static double GetDataGridViewColumnWidth(Forms.DataGridView dataGridView, Forms.DataGridViewColumn column, double availableWidth)
    {
        if (column.AutoSizeMode == Forms.DataGridViewAutoSizeColumnMode.Fill && dataGridView.Columns.Count > 0)
        {
            return Math.Max(40, availableWidth / dataGridView.Columns.Count);
        }

        return Math.Max(40, column.Width > 0 ? column.Width : 100);
    }

    private void RenderListView(DrawingContext drawingContext, Forms.ListView listView, Rect bounds, Brush foreground)
    {
        DrawBorder(drawingContext, listView.BorderStyle, bounds);

        double y = bounds.Y + 1;
        const double headerHeight = 20;
        const double rowHeight = 18;
        bool showDetails = listView.View == Forms.View.Details || listView.Columns.Count > 0;

        if (showDetails && listView.HeaderStyle != Forms.ColumnHeaderStyle.None)
        {
            double x = bounds.X + 1;
            drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), new Rect(bounds.X + 1, y, Math.Max(0, bounds.Width - 2), headerHeight));
            foreach (Forms.ColumnHeader column in listView.Columns)
            {
                double width = column.Width > 0 ? column.Width : 120;
                Rect headerBounds = new(x, y, width, headerHeight);
                drawingContext.DrawLine(new Pen(SystemColors.ControlDarkBrush, 1), new Point(headerBounds.Right, headerBounds.Y), new Point(headerBounds.Right, headerBounds.Bottom));
                DrawTextInBounds(drawingContext, column.Text, new Rect(headerBounds.X + 4, headerBounds.Y + 3, Math.Max(0, headerBounds.Width - 8), headerHeight - 4), foreground, 12);
                x += width;
                if (x > bounds.Right)
                {
                    break;
                }
            }

            y += headerHeight;
        }

        foreach (Forms.ListViewItem item in listView.Items)
        {
            if (y + rowHeight > bounds.Bottom)
            {
                break;
            }

            Rect rowBounds = new(bounds.X + 1, y, Math.Max(0, bounds.Width - 2), rowHeight);
            bool selected = item.Selected || listView.SelectedItems.Contains(item);
            if (selected)
            {
                drawingContext.DrawRectangle(SystemColors.HighlightBrush, null, rowBounds);
            }
            else if (listView.GridLines)
            {
                drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlLightBrush, 1), rowBounds);
            }

            if (listView.CheckBoxes)
            {
                Rect checkBounds = new(rowBounds.X + 4, rowBounds.Y + 3, 12, 12);
                drawingContext.DrawRectangle(SystemColors.WindowBrush, new Pen(SystemColors.ControlDarkBrush, 1), checkBounds);
                if (item.Checked)
                {
                    DrawText(drawingContext, "x", new Point(checkBounds.X + 2, checkBounds.Y - 1), SystemColors.ControlTextBrush, 11);
                }
            }

            if (showDetails)
            {
                double x = rowBounds.X;
                int columnCount = Math.Max(listView.Columns.Count, item.SubItems.Count);
                for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
                {
                    double width = columnIndex < listView.Columns.Count && listView.Columns[columnIndex].Width > 0
                        ? listView.Columns[columnIndex].Width
                        : 120;
                    string text = columnIndex < item.SubItems.Count ? item.SubItems[columnIndex].Text : string.Empty;
                    if (columnIndex == 0 && string.IsNullOrEmpty(text))
                    {
                        text = item.Text;
                    }

                    double textInset = columnIndex == 0 && listView.CheckBoxes ? 22 : 4;
                    Rect cellBounds = new(x + textInset, rowBounds.Y + 1, Math.Max(0, width - textInset - 4), rowHeight - 2);
                    DrawTextInBounds(drawingContext, text, cellBounds, selected ? SystemColors.HighlightTextBrush : foreground, 12);
                    x += width;
                    if (x > bounds.Right)
                    {
                        break;
                    }
                }
            }
            else
            {
                double textInset = listView.CheckBoxes ? 22 : 4;
                DrawTextInBounds(drawingContext, item.Text, new Rect(rowBounds.X + textInset, rowBounds.Y + 1, Math.Max(0, rowBounds.Width - textInset - 4), rowHeight - 2), selected ? SystemColors.HighlightTextBrush : foreground, 12);
            }

            y += rowHeight;
        }
    }

    private void RenderToolStrip(DrawingContext drawingContext, Forms.ToolStrip toolStrip, Rect bounds, Brush foreground)
    {
        drawingContext.DrawRectangle(SystemColors.ControlBrush, new Pen(SystemColors.ControlDarkBrush, 1), bounds);
        double x = bounds.X + 4;
        foreach (Forms.ToolStripItem item in toolStrip.Items)
        {
            if (!item.Visible || !item.Available)
            {
                continue;
            }

            if (item is Forms.ToolStripSeparator)
            {
                drawingContext.DrawLine(new Pen(SystemColors.ControlDarkBrush, 1), new Point(x + 3, bounds.Y + 4), new Point(x + 3, bounds.Bottom - 4));
                x += 8;
                continue;
            }

            string text = string.IsNullOrEmpty(item.Text) ? item.Name : item.Text;
            double itemWidth = Math.Max(item.Width > 0 ? item.Width : 0, MeasureText(text, 12) + 14);
            Rect itemBounds = new(x, bounds.Y + 2, itemWidth, Math.Max(0, bounds.Height - 4));
            if (item.Selected)
            {
                drawingContext.DrawRectangle(SystemColors.HighlightBrush, null, itemBounds);
            }

            DrawText(drawingContext, text, new Point(itemBounds.X + 6, itemBounds.Y + 3), item.Enabled ? foreground : SystemColors.GrayTextBrush, 12);
            x += itemWidth + 2;
        }
    }

    private void RenderTreeView(DrawingContext drawingContext, Forms.TreeView treeView, Rect bounds, Brush foreground)
    {
        DrawBorder(drawingContext, treeView.BorderStyle, bounds);

        double y = bounds.Y + 3;
        foreach (Forms.TreeNode node in treeView.Nodes)
        {
            y = RenderTreeNode(drawingContext, treeView, node, bounds, 0, y, foreground);
            if (y > bounds.Bottom)
            {
                break;
            }
        }
    }

    private double RenderTreeNode(DrawingContext drawingContext, Forms.TreeView treeView, Forms.TreeNode node, Rect bounds, int depth, double y, Brush foreground)
    {
        if (!node.IsVisible)
        {
            return y;
        }

        const double lineHeight = 18;
        Forms.TreeNodeStates state = GetTreeNodeState(treeView, node);
        double x = bounds.X + 4 + depth * 14;
        Rect rowBounds = new(bounds.X + 1, y, Math.Max(0, bounds.Width - 2), lineHeight);
        DrawingRectangle ownerAllBounds = CreateTreeNodeBounds(bounds, x, y, bounds.Right - x, lineHeight);

        if (ReferenceEquals(treeView.SelectedNode, node))
        {
            drawingContext.DrawRectangle(SystemColors.HighlightBrush, null, rowBounds);
        }

        ImageSource? ownerDrawAllSource = null;
        bool ownerDrawAllDefault = true;
        if (treeView.DrawMode == Forms.TreeViewDrawMode.OwnerDrawAll)
        {
            node.Bounds = ownerAllBounds;
            TryRenderTreeNodeOwnerDraw(treeView, node, bounds, ownerAllBounds, lineHeight, state, out ownerDrawAllSource, out ownerDrawAllDefault);
        }

        if (treeView.DrawMode != Forms.TreeViewDrawMode.OwnerDrawAll || ownerDrawAllDefault)
        {
            if (node.Nodes.Count > 0)
            {
                DrawText(drawingContext, node.IsExpanded ? "-" : "+", new Point(x, y + 1), foreground, 12);
                x += 12;
            }
            else
            {
                x += 12;
            }

            if (TryGetTreeNodeImageSource(treeView, node, out ImageSource? imageSource))
            {
                const double imageSize = 16;
                drawingContext.DrawImage(imageSource, new Rect(x, y + 1, imageSize, imageSize));
                x += imageSize + 3;
            }

            DrawingRectangle textBounds = CreateTreeNodeBounds(bounds, x, y, bounds.Right - x, lineHeight);
            node.Bounds = textBounds;

            ImageSource? ownerDrawTextSource = null;
            bool ownerDrawTextDefault = true;
            if (treeView.DrawMode == Forms.TreeViewDrawMode.OwnerDrawText)
            {
                TryRenderTreeNodeOwnerDraw(treeView, node, bounds, textBounds, lineHeight, state, out ownerDrawTextSource, out ownerDrawTextDefault);
            }

            if (treeView.DrawMode != Forms.TreeViewDrawMode.OwnerDrawText || ownerDrawTextDefault)
            {
                DrawText(drawingContext, node.Text, new Point(x, y + 1), ReferenceEquals(treeView.SelectedNode, node) ? SystemColors.HighlightTextBrush : foreground, 12);
            }

            if (ownerDrawTextSource != null)
            {
                drawingContext.DrawImage(ownerDrawTextSource, new Rect(bounds.X, y, ownerDrawTextSource.Width, ownerDrawTextSource.Height));
            }
        }

        if (ownerDrawAllSource != null)
        {
            drawingContext.DrawImage(ownerDrawAllSource, new Rect(bounds.X, y, ownerDrawAllSource.Width, ownerDrawAllSource.Height));
        }

        y += lineHeight;

        if (node.IsExpanded)
        {
            foreach (Forms.TreeNode child in node.Nodes)
            {
                y = RenderTreeNode(drawingContext, treeView, child, bounds, depth + 1, y, foreground);
                if (y > bounds.Bottom)
                {
                    break;
                }
            }
        }

        return y;
    }

    private static DrawingRectangle CreateTreeNodeBounds(Rect treeBounds, double x, double y, double width, double height)
    {
        return new DrawingRectangle(
            (int)Math.Round(x - treeBounds.X),
            (int)Math.Round(y - treeBounds.Y),
            Math.Max(0, (int)Math.Round(width)),
            Math.Max(0, (int)Math.Round(height)));
    }

    private static Forms.TreeNodeStates GetTreeNodeState(Forms.TreeView treeView, Forms.TreeNode node)
    {
        Forms.TreeNodeStates state = Forms.TreeNodeStates.Default;
        if (node.Checked)
        {
            state |= Forms.TreeNodeStates.Checked;
        }

        if (ReferenceEquals(treeView.SelectedNode, node))
        {
            state |= Forms.TreeNodeStates.Selected;
            if (treeView.Focused || treeView.ContainsFocus)
            {
                state |= Forms.TreeNodeStates.Focused;
            }
        }

        return state;
    }

    private static bool TryRenderTreeNodeOwnerDraw(
        Forms.TreeView treeView,
        Forms.TreeNode node,
        Rect treeBounds,
        DrawingRectangle eventBounds,
        double lineHeight,
        Forms.TreeNodeStates state,
        out ImageSource? imageSource,
        out bool drawDefault)
    {
        imageSource = null;
        drawDefault = true;

        int bitmapWidth = Math.Max(1, (int)Math.Ceiling(treeBounds.Width));
        int bitmapHeight = Math.Max(1, (int)Math.Ceiling(lineHeight));
        using DrawingBitmap bitmap = new(bitmapWidth, bitmapHeight, DrawingPixelFormat.Format32bppPArgb);
        using DrawingGraphics graphics = DrawingGraphics.FromImage(bitmap);
        graphics.Clear(DrawingColor.Transparent);
        graphics.TranslateTransform(0, -eventBounds.Y);

        Forms.DrawTreeNodeEventArgs eventArgs = new(graphics, node, eventBounds)
        {
            State = state
        };
        treeView.RaiseDrawNode(eventArgs);
        drawDefault = eventArgs.DrawDefault;
        imageSource = CreateImageSource(bitmap);
        return imageSource != null;
    }

    private bool TryGetTreeNodeImageSource(Forms.TreeView treeView, Forms.TreeNode node, out ImageSource? imageSource)
    {
        imageSource = null;
        Forms.ImageList? imageList = treeView.ImageList;
        if (imageList == null || imageList.Images.Count == 0)
        {
            return false;
        }

        DrawingImage? image = TryGetTreeNodeImage(treeView, node, imageList);
        if (image == null)
        {
            return false;
        }

        CachedImageSource cached = _imageSourceCache.GetValue(image, static key => new CachedImageSource(CreateImageSource(key)));
        imageSource = cached.Source;
        return imageSource != null;
    }

    private static DrawingImage? TryGetTreeNodeImage(Forms.TreeView treeView, Forms.TreeNode node, Forms.ImageList imageList)
    {
        bool selected = ReferenceEquals(treeView.SelectedNode, node);
        string key = selected ? node.SelectedImageKey : node.ImageKey;
        if (!string.IsNullOrEmpty(key))
        {
            DrawingImage? keyedImage = imageList.Images[key];
            if (keyedImage != null)
            {
                return keyedImage;
            }
        }

        int index = selected ? node.SelectedImageIndex : node.ImageIndex;
        if (index < 0)
        {
            index = selected ? treeView.SelectedImageIndex : treeView.ImageIndex;
        }

        return index >= 0 && index < imageList.Images.Count ? imageList.Images[index] : null;
    }

    private static ImageSource? CreateImageSource(DrawingImage image)
    {
        if (image is IProGpuTextureSource textureSource)
        {
            return PortableNativeImageSourceFactory.Create(
                new ProGpuDrawingImageSource(image, textureSource));
        }

        return CreatePixelImageSource(image);
    }

    private static WriteableBitmap? CreatePixelImageSource(DrawingImage image)
    {
        DrawingBitmap? bitmap = image as DrawingBitmap;
        bool ownsBitmap = false;
        if (bitmap == null)
        {
            bitmap = new DrawingBitmap(image);
            ownsBitmap = true;
        }

        try
        {
            var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppPArgb);
            try
            {
                int byteCount = Math.Abs(data.Stride) * data.Height;
                byte[] pixels = new byte[byteCount];
                Marshal.Copy(data.Scan0, pixels, 0, byteCount);

                var source = new WriteableBitmap(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null);
                GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    source.WritePixels(new Int32Rect(0, 0, bitmap.Width, bitmap.Height), handle.AddrOfPinnedObject(), byteCount, data.Stride);
                }
                finally
                {
                    handle.Free();
                }

                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
        finally
        {
            if (ownsBitmap)
            {
                bitmap.Dispose();
            }
        }
    }

    private sealed class ProGpuDrawingImageSource : IPortableNativeImageSource
    {
        private readonly DrawingImage _image;
        private readonly IProGpuTextureSource _textureSource;

        public ProGpuDrawingImageSource(DrawingImage image, IProGpuTextureSource textureSource)
        {
            _image = image;
            _textureSource = textureSource;
        }

        public int PixelWidth => _image.Width;

        public int PixelHeight => _image.Height;

        public bool TryGetPortableNativeImage(out object? nativeImage)
        {
            if (_textureSource.TryGetGpuTexture(out GpuTexture texture))
            {
                nativeImage = texture;
                return true;
            }

            nativeImage = null;
            return false;
        }
    }

    private sealed class CachedImageSource
    {
        public CachedImageSource(ImageSource? source)
        {
            Source = source;
        }

        public ImageSource? Source { get; }
    }

    private static void DrawBorder(DrawingContext drawingContext, Forms.BorderStyle borderStyle, Rect bounds)
    {
        if (borderStyle != Forms.BorderStyle.None)
        {
            drawingContext.DrawRectangle(null, new Pen(SystemColors.ControlDarkBrush, 1), bounds);
        }
    }

    private Brush CreateBrush(System.Drawing.Color color, Brush fallback)
    {
        if (color.IsEmpty)
        {
            return fallback;
        }

        return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
    }

    private void DrawText(DrawingContext drawingContext, string text, Point origin, Brush brush, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        FormattedText formatted = CreateFormattedText(text, brush, fontSize);
        drawingContext.DrawText(formatted, origin);
    }

    private void DrawTextInBounds(DrawingContext drawingContext, string text, Rect bounds, Brush brush, double fontSize)
    {
        if (string.IsNullOrEmpty(text) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        drawingContext.PushClip(new RectangleGeometry(bounds));
        try
        {
            DrawText(drawingContext, text, new Point(bounds.X, bounds.Y), brush, fontSize);
        }
        finally
        {
            drawingContext.Pop();
        }
    }

    private double MeasureText(string text, double fontSize)
    {
        return string.IsNullOrEmpty(text) ? 0 : CreateFormattedText(text, Brushes.Black, fontSize).WidthIncludingTrailingWhitespace;
    }

    private FormattedText CreateFormattedText(string text, Brush brush, double fontSize)
    {
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal),
            fontSize,
            brush,
            pixelsPerDip);
    }
}

public sealed class WindowsFormsHostAutomationPeer : FrameworkElementAutomationPeer
{
    public WindowsFormsHostAutomationPeer(WindowsFormsHost owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.Pane;
    }

    protected override string GetClassNameCore()
    {
        return nameof(WindowsFormsHost);
    }
}

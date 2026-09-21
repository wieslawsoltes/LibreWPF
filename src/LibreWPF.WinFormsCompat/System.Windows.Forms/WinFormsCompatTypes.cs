using System.ComponentModel;
using System.Drawing;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Linq;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace System.Windows.Forms;

public interface IWin32Window
{
    IntPtr Handle { get; }
}

public delegate void MethodInvoker();

public class Control : Component, IWin32Window, ISynchronizeInvoke
{
    private static long s_nextHandle = 0x10000;
    private static readonly object s_handleSync = new();
    private static readonly Dictionary<IntPtr, Control> s_controlsByHandle = new();

    private sealed class ImmediateAsyncResult : IAsyncResult
    {
        public ImmediateAsyncResult(object? asyncState, object? result)
        {
            AsyncState = asyncState;
            Result = result;
        }

        public object? AsyncState { get; }

        public WaitHandle AsyncWaitHandle => new ManualResetEvent(true);

        public bool CompletedSynchronously => true;

        public bool IsCompleted => true;

        public object? Result { get; }
    }

    private bool _isHandleCreated;
    private IntPtr _handle;
    private Point _location;
    private Size _size;
    private bool _visible = true;

    public static bool CheckForIllegalCrossThreadCalls { get; set; }

    public event EventHandler? GotFocus;
    public event EventHandler? LostFocus;
    public event EventHandler? SizeChanged;
    public event EventHandler? Resize;
    public event EventHandler? Enter;
    public event EventHandler? Leave;
    public event EventHandler? Click;
    public event EventHandler? DoubleClick;
    public event MouseEventHandler? MouseClick;
    public event MouseEventHandler? MouseDown;
    public event EventHandler? MouseLeave;
    public event MouseEventHandler? MouseMove;
    public event MouseEventHandler? MouseUp;
    public event MouseEventHandler? MouseWheel;
    public event MouseEventHandler? MouseDoubleClick;
    public event KeyEventHandler? KeyDown;
    public event KeyEventHandler? KeyUp;
    public event KeyPressEventHandler? KeyPress;
    public event PreviewKeyDownEventHandler? PreviewKeyDown;
    public event PaintEventHandler? Paint;
    public event EventHandler? TextChanged;
    public event EventHandler? VisibleChanged;
    public event EventHandler? LocationChanged;
    public event EventHandler? HandleCreated;
    public event EventHandler? Invalidated;
    public event CancelEventHandler? Validating;
    public event EventHandler? Validated;
    public event DragEventHandler? DragDrop;
    public event DragEventHandler? DragEnter;
    public event EventHandler? DragLeave;
    public event DragEventHandler? DragOver;

    public virtual bool AutoSize { get; set; }

    public bool AllowDrop { get; set; }

    public AnchorStyles Anchor { get; set; } = AnchorStyles.Top | AnchorStyles.Left;

    public Color BackColor { get; set; } = SystemColors.Control;

    public Image? BackgroundImage { get; set; }

    public virtual Rectangle Bounds
    {
        get => new(Location, Size);
        set
        {
            Location = value.Location;
            Size = value.Size;
        }
    }

    public virtual bool Capture { get; set; }

    public virtual Rectangle ClientRectangle => new(Point.Empty, Size);

    public ControlCollection Controls { get; }

    public ControlBindingsCollection DataBindings { get; }

    public virtual Rectangle DisplayRectangle => ClientRectangle;

    public DockStyle Dock { get; set; }

    public virtual bool Enabled { get; set; } = true;

    public virtual bool Focused { get; }

    public virtual bool ContainsFocus => Focused || Controls.Any(static control => control.ContainsFocus);

    public Font Font { get; set; } = SystemFonts.DefaultFont;

    public static Font DefaultFont => SystemFonts.DefaultFont;

    public Cursor Cursor { get; set; } = Cursors.Default;

    public Color ForeColor { get; set; } = SystemColors.ControlText;

    public IntPtr Handle
    {
        get => EnsureHandle();
        protected set
        {
            _handle = value;
            if (_handle != IntPtr.Zero && !_isHandleCreated)
            {
                OnHandleCreated(EventArgs.Empty);
            }
        }
    }

    public virtual int Height
    {
        get => Size.Height;
        set => Size = new Size(Size.Width, value);
    }

    public virtual int Left
    {
        get => Location.X;
        set => Location = new Point(value, Location.Y);
    }

    public virtual Point Location
    {
        get => _location;
        set
        {
            if (_location == value)
            {
                return;
            }

            _location = value;
            LocationChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
            Parent?.Invalidate();
        }
    }

    public Padding Margin { get; set; } = Padding.Empty;

    public Padding Padding { get; set; } = Padding.Empty;

    public DockPaddingEdges DockPadding { get; } = new();

    public Size MaximumSize { get; set; }

    public Size MinimumSize { get; set; }

    public string Name { get; set; } = string.Empty;

    public Control? Parent { get; internal set; }

    public RightToLeft RightToLeft { get; set; }

    public int Right => Left + Width;

    public bool ResizeRedraw { get; set; }

    public virtual Size Size
    {
        get => _size;
        set
        {
            if (_size == value)
            {
                return;
            }

            _size = value;
            OnResize(EventArgs.Empty);
            Invalidate();
            Parent?.Invalidate();
        }
    }

    public virtual Size ClientSize
    {
        get => Size;
        set => Size = value;
    }

    public object? Tag { get; set; }

    public int TabIndex { get; set; }

    public bool TabStop { get; set; } = true;

    private string _text = string.Empty;

    public virtual string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            OnTextChanged(EventArgs.Empty);
        }
    }

    public ContextMenuStrip? ContextMenuStrip { get; set; }

    public virtual int Top
    {
        get => Location.Y;
        set => Location = new Point(Location.X, value);
    }

    public virtual bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value)
            {
                return;
            }

            _visible = value;
            OnVisibleChanged(EventArgs.Empty);
            Parent?.Invalidate();
        }
    }

    public virtual int Width
    {
        get => Size.Width;
        set => Size = new Size(value, Size.Height);
    }

    public bool IsDisposed { get; private set; }

    public virtual bool IsHandleCreated => _isHandleCreated || _handle != IntPtr.Zero;

    public static Keys ModifierKeys { get; set; }

    public bool InvokeRequired => false;

    public Control()
    {
        Controls = new ControlCollection(this);
        DataBindings = new ControlBindingsCollection(this);
    }

    public DragDropEffects DoDragDrop(object data, DragDropEffects allowedEffects)
    {
        return allowedEffects;
    }

    public virtual bool Focus()
    {
        GotFocus?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public virtual void Select()
    {
        Focus();
    }

    public void BringToFront()
    {
        if (Parent == null)
        {
            return;
        }

        Parent.Controls.SetChildIndex(this, Parent.Controls.Count - 1);
    }

    public void SendToBack()
    {
        Parent?.Controls.SetChildIndex(this, 0);
    }

    public void RaiseMouseDown(MouseEventArgs e)
    {
        OnMouseDown(e);
    }

    public void RaiseMouseUp(MouseEventArgs e)
    {
        OnMouseUp(e);
    }

    public void RaiseMouseClick(MouseEventArgs e)
    {
        OnMouseClick(e);
    }

    public void RaiseMouseDoubleClick(MouseEventArgs e)
    {
        OnMouseDoubleClick(e);
        OnDoubleClick(EventArgs.Empty);
    }

    public void RaiseKeyDown(KeyEventArgs e)
    {
        OnKeyDown(e);
    }

    public void RaiseKeyUp(KeyEventArgs e)
    {
        OnKeyUp(e);
    }

    public void RaiseKeyPress(KeyPressEventArgs e)
    {
        OnKeyPress(e);
    }

    public IAsyncResult BeginInvoke(Delegate method, params object?[]? args)
    {
        object? result = Invoke(method, args);
        return new ImmediateAsyncResult(null, result);
    }

    public object? EndInvoke(IAsyncResult result)
    {
        return result is ImmediateAsyncResult immediate ? immediate.Result : result.AsyncState;
    }

    public object? Invoke(Delegate method)
    {
        return Invoke(method, null);
    }

    public object? Invoke(Delegate method, params object?[]? args)
    {
        return method.DynamicInvoke(args);
    }

    public virtual void CreateControl()
    {
        _ = EnsureHandle();
        foreach (Control child in Controls)
        {
            child.CreateControl();
        }
    }

    public virtual Graphics CreateGraphics()
    {
        return Graphics.FromHwnd(Handle);
    }

    public static Control? FromChildHandle(IntPtr handle)
    {
        lock (s_handleSync)
        {
            return s_controlsByHandle.GetValueOrDefault(handle);
        }
    }

    public bool Contains(Control control)
    {
        foreach (Control child in Controls)
        {
            if (ReferenceEquals(child, control) || child.Contains(control))
            {
                return true;
            }
        }

        return false;
    }

    public virtual void Invalidate()
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Invalidate(Rectangle rc)
    {
        Invalidate();
    }

    public Point PointToClient(Point p)
    {
        return new Point(p.X - Left, p.Y - Top);
    }

    public Point PointToScreen(Point p)
    {
        return new Point(p.X + Left, p.Y + Top);
    }

    public virtual void Refresh()
    {
        Invalidate();
    }

    public virtual void Show()
    {
        Visible = true;
    }

    public virtual void Hide()
    {
        Visible = false;
    }

    public virtual void SuspendLayout()
    {
    }

    public virtual void ResumeLayout(bool performLayout)
    {
        if (performLayout)
        {
            PerformLayout();
        }
    }

    public virtual void ResumeLayout()
    {
        ResumeLayout(true);
    }

    public virtual void PerformLayout()
    {
    }

    public virtual void SetBounds(int x, int y, int width, int height)
    {
        Bounds = new Rectangle(x, y, width, height);
    }

    protected virtual bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        return false;
    }

    protected virtual void OnDragDrop(DragEventArgs e)
    {
        DragDrop?.Invoke(this, e);
    }

    protected virtual void OnDragEnter(DragEventArgs e)
    {
        DragEnter?.Invoke(this, e);
    }

    protected virtual void OnDragLeave(EventArgs e)
    {
        DragLeave?.Invoke(this, e);
    }

    protected virtual void OnDragOver(DragEventArgs e)
    {
        DragOver?.Invoke(this, e);
    }

    protected virtual void OnHandleCreated(EventArgs e)
    {
        _isHandleCreated = true;
        HandleCreated?.Invoke(this, e);
    }

    private IntPtr EnsureHandle()
    {
        if (_handle == IntPtr.Zero)
        {
            _handle = new IntPtr(Interlocked.Increment(ref s_nextHandle));
            lock (s_handleSync)
            {
                s_controlsByHandle[_handle] = this;
            }
            if (!_isHandleCreated)
            {
                OnHandleCreated(EventArgs.Empty);
            }
        }

        return _handle;
    }

    protected virtual void OnMouseClick(MouseEventArgs e)
    {
        MouseClick?.Invoke(this, e);
        OnClick(EventArgs.Empty);
    }

    protected virtual void OnClick(EventArgs e)
    {
        Click?.Invoke(this, e);
    }

    protected virtual void OnDoubleClick(EventArgs e)
    {
        DoubleClick?.Invoke(this, e);
    }

    protected virtual void OnMouseDown(MouseEventArgs e)
    {
        MouseDown?.Invoke(this, e);
    }

    protected virtual void OnMouseLeave(EventArgs e)
    {
        MouseLeave?.Invoke(this, e);
    }

    protected virtual void OnMouseMove(MouseEventArgs e)
    {
        MouseMove?.Invoke(this, e);
    }

    protected virtual void OnMouseUp(MouseEventArgs e)
    {
        MouseUp?.Invoke(this, e);
    }

    protected virtual void OnMouseWheel(MouseEventArgs e)
    {
        MouseWheel?.Invoke(this, e);
    }

    protected virtual void OnMouseDoubleClick(MouseEventArgs e)
    {
        MouseDoubleClick?.Invoke(this, e);
    }

    protected virtual void OnKeyPress(KeyPressEventArgs e)
    {
        KeyPress?.Invoke(this, e);
    }

    protected virtual void OnEnter(EventArgs e)
    {
        Enter?.Invoke(this, e);
    }

    protected virtual void OnKeyDown(KeyEventArgs e)
    {
        KeyDown?.Invoke(this, e);
    }

    protected virtual void OnKeyUp(KeyEventArgs e)
    {
        KeyUp?.Invoke(this, e);
    }

    protected virtual void OnTextChanged(EventArgs e)
    {
        TextChanged?.Invoke(this, e);
    }

    protected virtual void OnPaint(PaintEventArgs e)
    {
        Paint?.Invoke(this, e);
    }

    protected virtual void OnVisibleChanged(EventArgs e)
    {
        VisibleChanged?.Invoke(this, e);
    }

    protected virtual void OnPaintBackground(PaintEventArgs e)
    {
    }

    protected virtual void OnLostFocus(EventArgs e)
    {
        LostFocus?.Invoke(this, e);
        var validating = new CancelEventArgs();
        OnValidating(validating);
        if (!validating.Cancel)
        {
            OnValidated(EventArgs.Empty);
        }

        OnLeave(e);
    }

    protected virtual void OnLeave(EventArgs e)
    {
        Leave?.Invoke(this, e);
    }

    protected virtual void OnValidating(CancelEventArgs e)
    {
        Validating?.Invoke(this, e);
    }

    protected virtual void OnValidated(EventArgs e)
    {
        Validated?.Invoke(this, e);
    }

    protected virtual void OnSizeChanged(EventArgs e)
    {
        SizeChanged?.Invoke(this, e);
    }

    protected virtual void OnPreviewKeyDown(PreviewKeyDownEventArgs e)
    {
        PreviewKeyDown?.Invoke(this, e);
    }

    protected virtual void OnResize(EventArgs e)
    {
        Resize?.Invoke(this, e);
        OnSizeChanged(e);
    }

    protected void SetStyle(ControlStyles flag, bool value)
    {
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        if (_handle != IntPtr.Zero)
        {
            lock (s_handleSync)
            {
                s_controlsByHandle.Remove(_handle);
            }
        }

        base.Dispose(disposing);
    }

    public class ControlCollection : Collection<Control>
    {
        private readonly Control _owner;

        public ControlCollection(Control owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, Control item)
        {
            item.Parent = _owner;
            base.InsertItem(index, item);
            if (_owner is TabControl tabControl && item is TabPage tabPage)
            {
                tabControl.RegisterControlTabPage(tabPage, index);
            }

            if (_owner.IsHandleCreated)
            {
                item.CreateControl();
            }

            _owner.Invalidate();
        }

        protected override void RemoveItem(int index)
        {
            Control control = this[index];
            control.Parent = null;
            base.RemoveItem(index);
            if (_owner is TabControl tabControl && control is TabPage tabPage)
            {
                tabControl.UnregisterControlTabPage(tabPage);
            }

            _owner.Invalidate();
        }

        protected override void ClearItems()
        {
            TabPage[]? tabPages = _owner is TabControl
                ? this.OfType<TabPage>().ToArray()
                : null;
            foreach (Control control in this)
            {
                control.Parent = null;
            }

            base.ClearItems();
            if (_owner is TabControl tabControl && tabPages != null)
            {
                tabControl.UnregisterControlTabPages(tabPages);
            }

            _owner.Invalidate();
        }

        public void SetChildIndex(Control child, int newIndex)
        {
            int oldIndex = IndexOf(child);
            if (oldIndex < 0)
            {
                return;
            }

            RemoveAt(oldIndex);
            Insert(Math.Clamp(newIndex, 0, Count), child);
        }

        public void AddRange(Control[] controls)
        {
            foreach (Control control in controls)
            {
                Add(control);
            }
        }
    }
}

public class ScrollableControl : Control
{
}

public class ContainerControl : ScrollableControl
{
    public Control? ActiveControl { get; set; }

    public SizeF AutoScaleDimensions { get; set; }

    public AutoScaleMode AutoScaleMode { get; set; }
}

public class Form : ContainerControl
{
    public event CancelEventHandler? Closing;
    public event FormClosingEventHandler? FormClosing;
    public event EventHandler? Closed;
    public event FormClosedEventHandler? FormClosed;
    public event EventHandler? Shown;

    public IButtonControl? AcceptButton { get; set; }

    public IButtonControl? CancelButton { get; set; }

    public Size ClientSize
    {
        get => Size;
        set => Size = value;
    }

    public DialogResult DialogResult { get; set; }

    public FormBorderStyle FormBorderStyle { get; set; }

    public bool MaximizeBox { get; set; } = true;

    public bool MinimizeBox { get; set; } = true;

    public bool ControlBox { get; set; } = true;

    public bool ShowIcon { get; set; } = true;

    public bool ShowInTaskbar { get; set; } = true;

    public Icon? Icon { get; set; }

    public Form? Owner { get; set; }

    public bool KeyPreview { get; set; }

    public bool RightToLeftLayout { get; set; }

    public FormStartPosition StartPosition { get; set; }

    public FormWindowState WindowState { get; set; }

    public DialogResult ShowDialog()
    {
        OnShown(EventArgs.Empty);
        return DialogResult;
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }

    public void Close()
    {
        var closing = new CancelEventArgs();
        OnClosing(closing);
        if (!closing.Cancel)
        {
            FormClosing?.Invoke(this, new FormClosingEventArgs(CloseReason.UserClosing, false));
            OnClosed(EventArgs.Empty);
            OnFormClosed(new FormClosedEventArgs(CloseReason.UserClosing));
        }
    }

    protected virtual void OnClosing(CancelEventArgs e)
    {
        Closing?.Invoke(this, e);
    }

    protected virtual void OnClosed(EventArgs e)
    {
        Closed?.Invoke(this, e);
    }

    protected virtual void OnFormClosed(FormClosedEventArgs e)
    {
        FormClosed?.Invoke(this, e);
    }

    protected virtual void OnShown(EventArgs e)
    {
        Shown?.Invoke(this, e);
    }
}

public class UserControl : ContainerControl
{
}

public class Panel : ScrollableControl
{
    public BorderStyle BorderStyle { get; set; }
}

public class SplitterPanel : Panel
{
}

public class ToolStripPanel : Panel
{
    public void Join(ToolStrip toolStrip)
    {
        if (!Controls.Contains(toolStrip))
        {
            Controls.Add(toolStrip);
        }
    }
}

public class ToolStripContentPanel : Panel
{
}

public class ToolStripContainer : ContainerControl
{
    public ToolStripContainer()
    {
        TopToolStripPanel = new ToolStripPanel();
        BottomToolStripPanel = new ToolStripPanel();
        LeftToolStripPanel = new ToolStripPanel();
        RightToolStripPanel = new ToolStripPanel();
        ContentPanel = new ToolStripContentPanel();

        Controls.Add(TopToolStripPanel);
        Controls.Add(BottomToolStripPanel);
        Controls.Add(LeftToolStripPanel);
        Controls.Add(RightToolStripPanel);
        Controls.Add(ContentPanel);
    }

    public ToolStripPanel TopToolStripPanel { get; }

    public ToolStripPanel BottomToolStripPanel { get; }

    public ToolStripPanel LeftToolStripPanel { get; }

    public ToolStripPanel RightToolStripPanel { get; }

    public ToolStripContentPanel ContentPanel { get; }
}

public class SplitContainer : ContainerControl
{
    public SplitContainer()
    {
        Controls.Add(Panel1);
        Controls.Add(Panel2);
    }

    public Orientation Orientation { get; set; }

    public SplitterPanel Panel1 { get; } = new();

    public SplitterPanel Panel2 { get; } = new();

    public int SplitterDistance { get; set; }

    public int SplitterWidth { get; set; } = 4;
}

public class Splitter : Control
{
    public int MinExtra { get; set; } = 25;

    public int MinSize { get; set; } = 25;
}

public class ButtonBase : Control
{
    public FlatStyle FlatStyle { get; set; }

    public Image? Image { get; set; }

    public ContentAlignment ImageAlign { get; set; }

    public ContentAlignment TextAlign { get; set; } = ContentAlignment.MiddleCenter;

    public bool UseCompatibleTextRendering { get; set; }

    public bool UseVisualStyleBackColor { get; set; }
}

public interface IButtonControl
{
    DialogResult DialogResult { get; set; }

    void NotifyDefault(bool value);

    void PerformClick();
}

public class Button : ButtonBase, IButtonControl
{
    public DialogResult DialogResult { get; set; }

    public void NotifyDefault(bool value)
    {
    }

    public void PerformClick()
    {
        OnClick(EventArgs.Empty);
    }
}

public class Label : Control
{
    public ContentAlignment TextAlign { get; set; } = ContentAlignment.TopLeft;

    public BorderStyle BorderStyle { get; set; }

    public FlatStyle FlatStyle { get; set; }

    public bool UseCompatibleTextRendering { get; set; }

    public bool UseMnemonic { get; set; } = true;
}

public class CheckBox : ButtonBase
{
    private CheckState _checkState;

    public event EventHandler? CheckedChanged;

    public Appearance Appearance { get; set; }

    public bool Checked
    {
        get => _checkState == CheckState.Checked;
        set => CheckState = value ? CheckState.Checked : CheckState.Unchecked;
    }

    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            if (_checkState == value)
            {
                return;
            }

            _checkState = value;
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public class RadioButton : CheckBox
{
}

public class GroupBox : Control
{
}

public class TextBoxBase : Control
{
    private string _selectedText = string.Empty;

    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;

    public bool Multiline { get; set; }

    public bool ReadOnly { get; set; }

    public int TextLength => Text.Length;

    public string SelectedText
    {
        get => _selectedText;
        set => ReplaceSelection(value ?? string.Empty);
    }

    public virtual bool CanUndo { get; set; }

    public bool WordWrap { get; set; } = true;

    public ScrollBars ScrollBars { get; set; }

    public string[] Lines
    {
        get => Text.Split('\n');
        set => Text = value != null ? string.Join('\n', value) : string.Empty;
    }

    public int SelectionLength { get; set; }

    public int SelectionStart { get; set; }

    public virtual void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Select(TextLength, 0);
        ReplaceSelection(text);
    }

    public void Select(int start, int length)
    {
        SelectionStart = Math.Clamp(start, 0, TextLength);
        SelectionLength = Math.Clamp(length, 0, TextLength - SelectionStart);
        _selectedText = SelectionLength > 0
            ? Text.Substring(SelectionStart, SelectionLength)
            : string.Empty;
    }

    public void SelectAll()
    {
        Select(0, TextLength);
    }

    public virtual void Cut()
    {
        if (!ReadOnly && SelectionLength > 0)
        {
            Clipboard.SetText(SelectedText);
            ReplaceSelection(string.Empty);
        }
    }

    public virtual void Copy()
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            Clipboard.SetText(SelectedText);
        }
    }

    public virtual void Paste()
    {
        if (!ReadOnly && Clipboard.ContainsText())
        {
            ReplaceSelection(Clipboard.GetText());
        }
    }

    public virtual void Undo()
    {
    }

    public virtual void ScrollToCaret()
    {
    }

    public virtual void Clear()
    {
        Text = string.Empty;
        SelectionStart = 0;
        SelectionLength = 0;
        _selectedText = string.Empty;
    }

    public virtual void ApplyTextInput(string text)
    {
        if (!ReadOnly)
        {
            ReplaceSelection(text ?? string.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || ReadOnly)
        {
            return;
        }

        if (e.KeyCode == Keys.Back && SelectionStart > 0 && SelectionLength == 0)
        {
            Select(SelectionStart - 1, 1);
            ReplaceSelection(string.Empty);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Delete && SelectionStart < TextLength)
        {
            if (SelectionLength == 0)
            {
                Select(SelectionStart, 1);
            }

            ReplaceSelection(string.Empty);
            e.Handled = true;
        }
    }

    private void ReplaceSelection(string replacement)
    {
        if (ReadOnly)
        {
            return;
        }

        int start = Math.Clamp(SelectionStart, 0, TextLength);
        int length = Math.Clamp(SelectionLength, 0, TextLength - start);
        Text = Text.Remove(start, length).Insert(start, replacement);
        SelectionStart = start + replacement.Length;
        SelectionLength = 0;
        _selectedText = string.Empty;
    }
}

public class TextBox : TextBoxBase
{
    public char PasswordChar { get; set; }
}

public class RichTextBox : TextBoxBase
{
}

public class ScrollBar : Control
{
    private int _value;

    public event ScrollEventHandler? Scroll;

    public int LargeChange { get; set; } = 10;

    public int Maximum { get; set; } = 100;

    public int Minimum { get; set; }

    public int SmallChange { get; set; } = 1;

    public int Value
    {
        get => _value;
        set
        {
            int oldValue = _value;
            _value = Math.Max(Minimum, Math.Min(Maximum, value));
            if (oldValue != _value)
            {
                Scroll?.Invoke(this, new ScrollEventArgs(ScrollEventType.ThumbPosition, oldValue, _value));
            }
        }
    }
}

public class VScrollBar : ScrollBar
{
}

public class ListBox : Control
{
    private readonly List<int> _selectedIndices = new();
    public event DrawItemEventHandler? DrawItem;
    public event MeasureItemEventHandler? MeasureItem;
    public event EventHandler? SelectedIndexChanged;

    private int _selectedIndex = -1;

    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;

    public DrawMode DrawMode { get; set; }

    public bool FormattingEnabled { get; set; }

    public bool IntegralHeight { get; set; } = true;

    public ListBox()
    {
        Items = new ListBoxObjectCollection(this);
        SelectedItems = new SelectedObjectCollection(this);
    }

    public ListBoxObjectCollection Items { get; }

    public SelectedObjectCollection SelectedItems { get; }

    public SelectionMode SelectionMode { get; set; } = SelectionMode.One;

    public bool Sorted { get; set; }

    public virtual int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value)
            {
                return;
            }

            if (value < -1 || value >= Items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetSelectedCore(value, true, clearExisting: true);
            OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    public object? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    public int IndexFromPoint(Point p)
    {
        return Items.Count == 0 ? -1 : Math.Max(0, Math.Min(Items.Count - 1, p.Y / Math.Max(1, Font.Height)));
    }

    public sealed class ListBoxObjectCollection : Collection<object>
    {
        private readonly ListBox _owner;

        internal ListBoxObjectCollection(ListBox owner)
        {
            _owner = owner;
        }

        public new int Add(object item)
        {
            if (!_owner.Sorted)
            {
                base.Add(item);
                return Count - 1;
            }

            string text = item?.ToString() ?? string.Empty;
            int index = 0;
            while (index < Count && string.Compare(this[index]?.ToString(), text, StringComparison.CurrentCulture) <= 0)
            {
                index++;
            }

            Insert(index, item);
            return index;
        }
    }

    public sealed class SelectedObjectCollection : IReadOnlyList<object>, ICollection
    {
        private readonly ListBox _owner;

        internal SelectedObjectCollection(ListBox owner)
        {
            _owner = owner;
        }

        public int Count => _owner._selectedIndices.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public object this[int index] => _owner.Items[_owner._selectedIndices[index]];

        public bool Contains(object item)
        {
            return Snapshot().Contains(item);
        }

        public void CopyTo(Array array, int index)
        {
            ((ICollection)Snapshot()).CopyTo(array, index);
        }

        public IEnumerator<object> GetEnumerator()
        {
            return Snapshot().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private List<object> Snapshot()
        {
            var items = new List<object>(_owner._selectedIndices.Count);
            foreach (int index in _owner._selectedIndices)
            {
                if (index >= 0 && index < _owner.Items.Count)
                {
                    items.Add(_owner.Items[index]);
                }
            }

            return items;
        }
    }

    public void SetSelected(int index, bool value)
    {
        ValidateItemIndex(index);
        SetSelectedCore(index, value, clearExisting: value && SelectionMode == SelectionMode.One);
        OnSelectedIndexChanged(EventArgs.Empty);
    }

    public bool GetSelected(int index)
    {
        ValidateItemIndex(index);
        return _selectedIndices.Contains(index);
    }

    private void SetSelectedCore(int index, bool value, bool clearExisting)
    {
        if (clearExisting)
        {
            _selectedIndices.Clear();
        }

        if (index < 0)
        {
            _selectedIndex = -1;
            return;
        }

        if (value)
        {
            if (!_selectedIndices.Contains(index))
            {
                _selectedIndices.Add(index);
                _selectedIndices.Sort();
            }
        }
        else
        {
            _selectedIndices.Remove(index);
        }

        _selectedIndex = _selectedIndices.Count > 0 ? _selectedIndices[0] : -1;
    }

    private void ValidateItemIndex(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    protected virtual void OnDrawItem(DrawItemEventArgs e)
    {
        DrawItem?.Invoke(this, e);
    }

    public void RaiseDrawItem(DrawItemEventArgs e)
    {
        OnDrawItem(e);
    }

    protected virtual void OnMeasureItem(MeasureItemEventArgs e)
    {
        MeasureItem?.Invoke(this, e);
    }

    public void RaiseMeasureItem(MeasureItemEventArgs e)
    {
        OnMeasureItem(e);
    }

    protected virtual void OnSelectedIndexChanged(EventArgs e)
    {
        SelectedIndexChanged?.Invoke(this, e);
    }
}

public class CheckedListBox : ListBox
{
    private readonly Dictionary<int, CheckState> _checkedStates = new();

    public CheckedListBox()
    {
        CheckedIndices = new CheckedIndexCollection(this);
        CheckedItems = new CheckedItemCollection(this);
    }

    public event ItemCheckEventHandler? ItemCheck;

    public bool CheckOnClick { get; set; }

    public CheckedIndexCollection CheckedIndices { get; }

    public CheckedItemCollection CheckedItems { get; }

    public void SetItemChecked(int index, bool value)
    {
        SetItemCheckState(index, value ? CheckState.Checked : CheckState.Unchecked);
    }

    public bool GetItemChecked(int index)
    {
        return GetItemCheckState(index) != CheckState.Unchecked;
    }

    public void SetItemCheckState(int index, CheckState value)
    {
        ValidateItemIndex(index);
        CheckState oldValue = GetItemCheckState(index);
        if (oldValue == value)
        {
            return;
        }

        var eventArgs = new ItemCheckEventArgs(index, value, oldValue);
        OnItemCheck(eventArgs);
        if (eventArgs.NewValue == CheckState.Unchecked)
        {
            _checkedStates.Remove(index);
        }
        else
        {
            _checkedStates[index] = eventArgs.NewValue;
        }

        Invalidate();
    }

    public CheckState GetItemCheckState(int index)
    {
        ValidateItemIndex(index);
        return _checkedStates.TryGetValue(index, out CheckState value)
            ? value
            : CheckState.Unchecked;
    }

    public bool TryToggleItemAt(int x, int y)
    {
        int index = IndexFromPoint(new Point(x, y));
        if (index < 0 || index >= Items.Count)
        {
            return false;
        }

        if (!CheckOnClick && x > 22)
        {
            return false;
        }

        SetItemCheckState(index, GetItemChecked(index) ? CheckState.Unchecked : CheckState.Checked);
        return true;
    }

    protected virtual void OnItemCheck(ItemCheckEventArgs e)
    {
        ItemCheck?.Invoke(this, e);
    }

    private void ValidateItemIndex(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public sealed class CheckedIndexCollection : IReadOnlyList<int>, ICollection
    {
        private readonly CheckedListBox _owner;

        internal CheckedIndexCollection(CheckedListBox owner)
        {
            _owner = owner;
        }

        public int Count => _owner._checkedStates.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public int this[int index] => Snapshot()[index];

        public bool Contains(int index)
        {
            return _owner._checkedStates.ContainsKey(index);
        }

        public int IndexOf(int index)
        {
            return Snapshot().IndexOf(index);
        }

        public void CopyTo(Array array, int index)
        {
            ((ICollection)Snapshot()).CopyTo(array, index);
        }

        public IEnumerator<int> GetEnumerator()
        {
            return Snapshot().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private List<int> Snapshot()
        {
            return _owner._checkedStates.Keys.OrderBy(static value => value).ToList();
        }
    }

    public sealed class CheckedItemCollection : IReadOnlyList<object>, ICollection
    {
        private readonly CheckedListBox _owner;

        internal CheckedItemCollection(CheckedListBox owner)
        {
            _owner = owner;
        }

        public int Count => _owner._checkedStates.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public object this[int index] => Snapshot()[index];

        public bool Contains(object item)
        {
            return Snapshot().Contains(item);
        }

        public int IndexOf(object item)
        {
            return Snapshot().IndexOf(item);
        }

        public void CopyTo(Array array, int index)
        {
            ((ICollection)Snapshot()).CopyTo(array, index);
        }

        public IEnumerator<object> GetEnumerator()
        {
            return Snapshot().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private List<object> Snapshot()
        {
            var items = new List<object>(_owner._checkedStates.Count);
            foreach (int index in _owner._checkedStates.Keys.OrderBy(static value => value))
            {
                if (index >= 0 && index < _owner.Items.Count)
                {
                    items.Add(_owner.Items[index]);
                }
            }

            return items;
        }
    }
}

public class ComboBox : ListBox
{
    private bool _droppedDown;

    public AutoCompleteMode AutoCompleteMode { get; set; }

    public AutoCompleteSource AutoCompleteSource { get; set; }

    public bool DroppedDown
    {
        get => _droppedDown;
        set
        {
            if (_droppedDown == value)
            {
                return;
            }

            _droppedDown = value;
            Invalidate();
            Parent?.Invalidate();
        }
    }

    public ComboBoxStyle DropDownStyle { get; set; }

    public bool FormattingEnabled { get; set; }

    public bool Sorted { get; set; }

    public int SelectionLength { get; set; }

    public string SelectedText { get; set; } = string.Empty;

    public override int SelectedIndex
    {
        get => base.SelectedIndex;
        set
        {
            base.SelectedIndex = value;
        }
    }

    public void SelectAll()
    {
        SelectedText = Text;
        SelectionLength = Text.Length;
    }
}

public class TabControl : Control
{
    private int _selectedIndex = -1;
    private bool _syncingControls;
    private bool _syncingTabPages;

    public TabControl()
    {
        TabPages = new TabPageCollection(this);
    }

    public TabPageCollection TabPages { get; }

    public bool RightToLeftLayout { get; set; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= TabPages.Count)
            {
                _selectedIndex = -1;
                return;
            }

            _selectedIndex = value;
        }
    }

    public TabPage? SelectedTab
    {
        get => SelectedIndex >= 0 && SelectedIndex < TabPages.Count ? TabPages[SelectedIndex] : null;
        set => SelectedIndex = value != null ? TabPages.IndexOf(value) : -1;
    }

    internal void RegisterControlTabPage(TabPage page, int controlIndex)
    {
        if (_syncingTabPages || TabPages.Contains(page))
        {
            return;
        }

        _syncingControls = true;
        try
        {
            TabPages.Insert(Math.Clamp(controlIndex, 0, TabPages.Count), page);
        }
        finally
        {
            _syncingControls = false;
        }
    }

    internal void UnregisterControlTabPage(TabPage page)
    {
        if (_syncingTabPages || !TabPages.Contains(page))
        {
            return;
        }

        _syncingControls = true;
        try
        {
            TabPages.Remove(page);
        }
        finally
        {
            _syncingControls = false;
        }
    }

    internal void UnregisterControlTabPages(IEnumerable<TabPage> pages)
    {
        foreach (TabPage page in pages)
        {
            UnregisterControlTabPage(page);
        }
    }

    public sealed class TabPageCollection : Collection<TabPage>
    {
        private readonly TabControl _owner;

        internal TabPageCollection(TabControl owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, TabPage item)
        {
            base.InsertItem(index, item);
            if (!_owner._syncingControls && !_owner.Controls.Contains(item))
            {
                _owner._syncingTabPages = true;
                try
                {
                    _owner.Controls.Add(item);
                    _owner.Controls.SetChildIndex(item, index);
                }
                finally
                {
                    _owner._syncingTabPages = false;
                }
            }

            if (_owner.SelectedIndex == -1)
            {
                _owner.SelectedIndex = 0;
            }

            _owner.Invalidate();
        }

        protected override void RemoveItem(int index)
        {
            TabPage page = this[index];
            base.RemoveItem(index);
            if (!_owner._syncingControls)
            {
                _owner._syncingTabPages = true;
                try
                {
                    _owner.Controls.Remove(page);
                }
                finally
                {
                    _owner._syncingTabPages = false;
                }
            }

            if (_owner.SelectedIndex >= Count)
            {
                _owner.SelectedIndex = Count - 1;
            }

            _owner.Invalidate();
        }

        protected override void ClearItems()
        {
            if (!_owner._syncingControls)
            {
                _owner._syncingTabPages = true;
                try
                {
                    foreach (TabPage page in this)
                    {
                        _owner.Controls.Remove(page);
                    }
                }
                finally
                {
                    _owner._syncingTabPages = false;
                }
            }

            base.ClearItems();
            _owner.SelectedIndex = -1;
            _owner.Invalidate();
        }

        public void AddRange(TabPage[] pages)
        {
            foreach (TabPage page in pages)
            {
                Add(page);
            }
        }
    }
}

public class TabPage : Panel
{
    public TabPage()
    {
    }

    public TabPage(string text)
    {
        Text = text;
    }

    public string ToolTipText { get; set; } = string.Empty;

    public bool UseVisualStyleBackColor { get; set; }
}

public class DataGridView : Control, ISupportInitialize
{
    public event DataGridViewCellEventHandler? CellValueChanged;
    public event DataGridViewDataErrorEventHandler? DataError;
    public event DataGridViewEditingControlShowingEventHandler? EditingControlShowing;
    public event DataGridViewRowsAddedEventHandler? RowsAdded;
    public event DataGridViewRowsRemovedEventHandler? RowsRemoved;

    public bool AllowUserToAddRows { get; set; } = true;

    public bool AllowUserToDeleteRows { get; set; } = true;

    public bool AllowUserToResizeRows { get; set; } = true;

    public DataGridViewColumnHeadersHeightSizeMode ColumnHeadersHeightSizeMode { get; set; }

    public bool MultiSelect { get; set; } = true;

    public bool ShowEditingIcon { get; set; } = true;

    public DataGridViewColumnCollection Columns { get; }

    public DataGridViewRowCollection Rows { get; }

    public DataGridViewEditMode EditMode { get; set; }

    public DataGridViewCell? CurrentCell { get; set; }

    public int RowHeadersWidth { get; set; } = 41;

    public DataGridViewRowHeadersWidthSizeMode RowHeadersWidthSizeMode { get; set; }

    public DataGridView()
    {
        Columns = new DataGridViewColumnCollection(this);
        Rows = new DataGridViewRowCollection(this);
    }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }

    public bool EndEdit()
    {
        if (CurrentCell != null)
        {
            OnCellValueChanged(new DataGridViewCellEventArgs(CurrentCell.ColumnIndex, CurrentCell.RowIndex));
        }

        return true;
    }

    internal void EnsureRowCells(DataGridViewRow row)
    {
        while (row.Cells.Count < Columns.Count)
        {
            row.Cells.Add(CreateCellForColumn(Columns[row.Cells.Count]));
        }
    }

    internal DataGridViewCell CreateCellForColumn(DataGridViewColumn column)
    {
        return column.CreateCell();
    }

    internal void OnCellValueChanged(DataGridViewCellEventArgs e)
    {
        CellValueChanged?.Invoke(this, e);
    }

    internal void OnDataError(DataGridViewDataErrorEventArgs e)
    {
        DataError?.Invoke(this, e);
    }

    internal void OnEditingControlShowing(DataGridViewEditingControlShowingEventArgs e)
    {
        EditingControlShowing?.Invoke(this, e);
    }

    internal void OnRowsAdded(DataGridViewRowsAddedEventArgs e)
    {
        RowsAdded?.Invoke(this, e);
        Invalidate();
    }

    internal void OnRowsRemoved(DataGridViewRowsRemovedEventArgs e)
    {
        RowsRemoved?.Invoke(this, e);
        Invalidate();
    }

    public sealed class DataGridViewColumnCollection : Collection<DataGridViewColumn>
    {
        private readonly DataGridView _owner;

        internal DataGridViewColumnCollection(DataGridView owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, DataGridViewColumn item)
        {
            item.SetOwner(_owner, index);
            base.InsertItem(index, item);
            Reindex();
            foreach (DataGridViewRow row in _owner.Rows)
            {
                _owner.EnsureRowCells(row);
            }
        }

        protected override void RemoveItem(int index)
        {
            this[index].SetOwner(null, -1);
            base.RemoveItem(index);
            Reindex();
        }

        protected override void ClearItems()
        {
            foreach (DataGridViewColumn column in this)
            {
                column.SetOwner(null, -1);
            }

            base.ClearItems();
        }

        public void AddRange(DataGridViewColumn[] columns)
        {
            foreach (DataGridViewColumn column in columns)
            {
                Add(column);
            }
        }

        private void Reindex()
        {
            for (int i = 0; i < Count; i++)
            {
                this[i].SetOwner(_owner, i);
            }
        }
    }

    public sealed class DataGridViewRowCollection : Collection<DataGridViewRow>
    {
        private readonly DataGridView _owner;

        internal DataGridViewRowCollection(DataGridView owner)
        {
            _owner = owner;
        }

        public new int Add(DataGridViewRow row)
        {
            base.Add(row);
            return Count - 1;
        }

        public int Add()
        {
            return Add(new DataGridViewRow());
        }

        public int Add(params object?[] values)
        {
            var row = new DataGridViewRow();
            for (int i = 0; i < values.Length; i++)
            {
                DataGridViewCell cell = i < _owner.Columns.Count
                    ? _owner.CreateCellForColumn(_owner.Columns[i])
                    : new DataGridViewTextBoxCell();
                cell.Value = values[i];
                row.Cells.Add(cell);
            }

            return Add(row);
        }

        protected override void InsertItem(int index, DataGridViewRow item)
        {
            item.SetOwner(_owner, index);
            _owner.EnsureRowCells(item);
            base.InsertItem(index, item);
            Reindex();
            _owner.OnRowsAdded(new DataGridViewRowsAddedEventArgs(index, 1));
        }

        protected override void RemoveItem(int index)
        {
            this[index].SetOwner(null, -1);
            base.RemoveItem(index);
            Reindex();
            _owner.OnRowsRemoved(new DataGridViewRowsRemovedEventArgs(index, 1));
        }

        protected override void ClearItems()
        {
            int count = Count;
            foreach (DataGridViewRow row in this)
            {
                row.SetOwner(null, -1);
            }

            base.ClearItems();
            if (count > 0)
            {
                _owner.OnRowsRemoved(new DataGridViewRowsRemovedEventArgs(0, count));
            }
        }

        private void Reindex()
        {
            for (int i = 0; i < Count; i++)
            {
                this[i].SetOwner(_owner, i);
            }
        }
    }
}

public class DataGridViewColumn : Component
{
    private DataGridView? _owner;

    public DataGridViewAutoSizeColumnMode AutoSizeMode { get; set; }

    public string HeaderText { get; set; } = string.Empty;

    public int Index { get; private set; } = -1;

    public string Name { get; set; } = string.Empty;

    public bool ReadOnly { get; set; }

    public object? Tag { get; set; }

    public int Width { get; set; } = 100;

    internal virtual DataGridViewCell CreateCell()
    {
        return new DataGridViewTextBoxCell();
    }

    internal void SetOwner(DataGridView? owner, int index)
    {
        _owner = owner;
        Index = index;
    }
}

public class DataGridViewTextBoxColumn : DataGridViewColumn
{
    internal override DataGridViewCell CreateCell()
    {
        return new DataGridViewTextBoxCell();
    }
}

public class DataGridViewComboBoxColumn : DataGridViewColumn
{
    internal override DataGridViewCell CreateCell()
    {
        return new DataGridViewComboBoxCell();
    }
}

public class DataGridViewRow
{
    private DataGridView? _owner;

    public DataGridViewCellCollection Cells { get; }

    public DataGridView? DataGridView => _owner;

    public int Index { get; private set; } = -1;

    public object? Tag { get; set; }

    public DataGridViewRow()
    {
        Cells = new DataGridViewCellCollection(this);
    }

    internal void SetOwner(DataGridView? owner, int index)
    {
        _owner = owner;
        Index = index;
        for (int i = 0; i < Cells.Count; i++)
        {
            Cells[i].SetOwner(owner, this, index, i);
        }
    }
}

public sealed class DataGridViewCellCollection : Collection<DataGridViewCell>
{
    private readonly DataGridViewRow _owner;

    internal DataGridViewCellCollection(DataGridViewRow owner)
    {
        _owner = owner;
    }

    protected override void InsertItem(int index, DataGridViewCell item)
    {
        item.SetOwner(_owner.DataGridView, _owner, _owner.Index, index);
        base.InsertItem(index, item);
        Reindex();
    }

    protected override void RemoveItem(int index)
    {
        this[index].SetOwner(null, null, -1, -1);
        base.RemoveItem(index);
        Reindex();
    }

    protected override void ClearItems()
    {
        foreach (DataGridViewCell cell in this)
        {
            cell.SetOwner(null, null, -1, -1);
        }

        base.ClearItems();
    }

    private void Reindex()
    {
        for (int i = 0; i < Count; i++)
        {
            this[i].SetOwner(_owner.DataGridView, _owner, _owner.Index, i);
        }
    }
}

public class DataGridViewCell
{
    public int ColumnIndex { get; private set; } = -1;

    public DataGridView? DataGridView { get; private set; }

    public DataGridViewRow? OwningRow { get; private set; }

    public int RowIndex { get; private set; } = -1;

    public object? Value { get; set; }

    internal void SetOwner(DataGridView? dataGridView, DataGridViewRow? row, int rowIndex, int columnIndex)
    {
        DataGridView = dataGridView;
        OwningRow = row;
        RowIndex = rowIndex;
        ColumnIndex = columnIndex;
    }
}

public class DataGridViewTextBoxCell : DataGridViewCell
{
}

public class DataGridViewComboBoxCell : DataGridViewCell
{
    public IList Items { get; } = new ArrayList();
}

public class TableLayoutPanel : Panel
{
}

public class FlowLayoutPanel : Panel
{
}

public class PictureBox : Control, ISupportInitialize
{
    public BorderStyle BorderStyle { get; set; }

    public Image? Image { get; set; }

    public PictureBoxSizeMode SizeMode { get; set; }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }
}

public class ProgressBar : Control
{
    public int Maximum { get; set; } = 100;

    public int Minimum { get; set; }

    public int Step { get; set; } = 10;

    public ProgressBarStyle Style { get; set; }

    public int Value { get; set; }

    public bool RightToLeftLayout { get; set; }

    public void PerformStep()
    {
        Value = Math.Max(Minimum, Math.Min(Maximum, Value + Step));
    }
}

public class NumericUpDown : Control
{
    public decimal DecimalPlaces { get; set; }

    public decimal Increment { get; set; } = 1;

    public decimal Maximum { get; set; } = 100;

    public decimal Minimum { get; set; }

    public decimal Value { get; set; }
}

public class TrackBar : Control
{
    public int Maximum { get; set; } = 10;

    public int Minimum { get; set; }

    public int TickFrequency { get; set; } = 1;

    public int Value { get; set; }

    public bool RightToLeftLayout { get; set; }
}

public class LinkLabel : Label
{
    public event LinkLabelLinkClickedEventHandler? LinkClicked;

    protected virtual void OnLinkClicked(LinkLabelLinkClickedEventArgs e)
    {
        LinkClicked?.Invoke(this, e);
    }
}

public class PropertyGrid : Control
{
    public event PropertyValueChangedEventHandler? PropertyValueChanged;
    public event SelectedGridItemChangedEventHandler? SelectedGridItemChanged;
    public event EventHandler? SelectedObjectsChanged;

    private readonly List<PropertyGridDisplayRow> _displayRows = new();
    private object? _selectedObject;
    private object?[]? _selectedObjects;
    private PropertySort _propertySort = PropertySort.CategorizedAlphabetical;

    public object? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (ReferenceEquals(_selectedObject, value) && _selectedObjects == null)
            {
                return;
            }

            _selectedObject = value;
            _selectedObjects = null;
            RebuildDisplayRows();
            SelectedObjectsChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public object?[]? SelectedObjects
    {
        get => _selectedObjects;
        set
        {
            _selectedObjects = value;
            _selectedObject = value != null && value.Length > 0 ? value[0] : null;
            RebuildDisplayRows();
            SelectedObjectsChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public GridItem? SelectedGridItem { get; set; }

    public bool HelpVisible { get; set; } = true;

    public PropertySort PropertySort
    {
        get => _propertySort;
        set
        {
            if (_propertySort == value)
            {
                return;
            }

            _propertySort = value;
            RebuildDisplayRows();
            Invalidate();
        }
    }

    public PropertyTabCollection PropertyTabs { get; } = new();

    public bool ToolbarVisible { get; set; } = true;

    public IReadOnlyList<PropertyGridDisplayRow> DisplayRows => _displayRows;

    public int SelectedObjectCount => _selectedObjects?.Length ?? (_selectedObject != null ? 1 : 0);

    public void ResetSelectedProperty()
    {
    }

    public override void Refresh()
    {
        RebuildDisplayRows();
        base.Refresh();
    }

    protected virtual void OnPropertyValueChanged(PropertyValueChangedEventArgs e)
    {
        PropertyValueChanged?.Invoke(this, e);
    }

    protected virtual void OnSelectedGridItemChanged(SelectedGridItemChangedEventArgs e)
    {
        SelectedGridItemChanged?.Invoke(this, e);
    }

    private void RebuildDisplayRows()
    {
        _displayRows.Clear();
        if (_selectedObject == null)
        {
            SelectedGridItem = null;
            return;
        }

        var descriptors = TypeDescriptor.GetProperties(_selectedObject)
            .Cast<PropertyDescriptor>()
            .Where(static descriptor => descriptor.IsBrowsable)
            .ToList();

        if (_propertySort == PropertySort.Alphabetical || _propertySort == PropertySort.CategorizedAlphabetical)
        {
            descriptors.Sort(static (x, y) => string.Compare(x.DisplayName, y.DisplayName, StringComparison.CurrentCultureIgnoreCase));
        }

        string? lastCategory = null;
        bool showCategories = _propertySort == PropertySort.Categorized || _propertySort == PropertySort.CategorizedAlphabetical;
        foreach (PropertyDescriptor descriptor in descriptors)
        {
            string category = descriptor.Category ?? string.Empty;
            if (showCategories && !string.Equals(lastCategory, category, StringComparison.Ordinal))
            {
                _displayRows.Add(PropertyGridDisplayRow.CreateCategory(category));
                lastCategory = category;
            }

            object? value = null;
            bool valueAvailable = true;
            try
            {
                value = descriptor.GetValue(_selectedObject);
            }
            catch
            {
                valueAvailable = false;
            }

            _displayRows.Add(PropertyGridDisplayRow.CreateProperty(
                descriptor.DisplayName,
                valueAvailable ? FormatPropertyValue(value) : string.Empty,
                descriptor.Description ?? string.Empty,
                category,
                descriptor));
        }

        PropertyGridDisplayRow? firstProperty = _displayRows.FirstOrDefault(static row => !row.IsCategory);
        SelectedGridItem = firstProperty != null
            ? new GridItem
            {
                Label = firstProperty.Label,
                Value = firstProperty.ValueText,
                PropertyDescriptor = firstProperty.PropertyDescriptor
            }
            : null;
    }

    private static string FormatPropertyValue(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is string text)
        {
            return text;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }
}

public sealed class PropertyTabCollection : Collection<Type>
{
    public void AddTabType(Type propertyTabType, System.ComponentModel.PropertyTabScope tabScope)
    {
        Add(propertyTabType);
    }
}

public sealed class PropertyGridDisplayRow
{
    private PropertyGridDisplayRow(
        bool isCategory,
        string label,
        string valueText,
        string description,
        string category,
        PropertyDescriptor? propertyDescriptor)
    {
        IsCategory = isCategory;
        Label = label;
        ValueText = valueText;
        Description = description;
        Category = category;
        PropertyDescriptor = propertyDescriptor;
    }

    public bool IsCategory { get; }

    public string Label { get; }

    public string ValueText { get; }

    public string Description { get; }

    public string Category { get; }

    public PropertyDescriptor? PropertyDescriptor { get; }

    public static PropertyGridDisplayRow CreateCategory(string category)
    {
        return new PropertyGridDisplayRow(true, string.IsNullOrEmpty(category) ? "Misc" : category, string.Empty, string.Empty, category, null);
    }

    public static PropertyGridDisplayRow CreateProperty(
        string label,
        string valueText,
        string description,
        string category,
        PropertyDescriptor propertyDescriptor)
    {
        return new PropertyGridDisplayRow(false, label, valueText, description, category, propertyDescriptor);
    }
}

public class WebBrowser : Control
{
    public event EventHandler? CanGoBackChanged;
    public event EventHandler? CanGoForwardChanged;
    public event EventHandler? DocumentTitleChanged;
    public event WebBrowserNavigatingEventHandler? Navigating;
    public event WebBrowserNavigatedEventHandler? Navigated;
    public event WebBrowserDocumentCompletedEventHandler? DocumentCompleted;
    public event EventHandler? StatusTextChanged;

    private Uri? _url;
    private string _documentTitle = string.Empty;
    private string _statusText = string.Empty;

    protected object ActiveXInstance { get; } = new object();

    public Uri? Url
    {
        get => _url;
        set
        {
            if (value != null)
            {
                var navigating = new WebBrowserNavigatingEventArgs(value);
                Navigating?.Invoke(this, navigating);
                if (navigating.Cancel)
                {
                    return;
                }
            }

            _url = value;
            if (value != null)
            {
                Navigated?.Invoke(this, new WebBrowserNavigatedEventArgs(value));
                DocumentCompleted?.Invoke(this, new WebBrowserDocumentCompletedEventArgs(value));
            }
        }
    }

    public string DocumentTitle
    {
        get => _documentTitle;
        set
        {
            if (_documentTitle == value)
            {
                return;
            }

            _documentTitle = value;
            DocumentTitleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            StatusTextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool CanGoBack { get; set; }

    public bool CanGoForward { get; set; }

    public void Navigate(string urlString)
    {
        Url = new Uri(urlString);
    }

    public void Navigate(Uri url)
    {
        Url = url;
    }

    public void GoBack()
    {
    }

    public void GoForward()
    {
    }

    public void Stop()
    {
    }

    public void Refresh(WebBrowserRefreshOption opt)
    {
        Refresh();
    }

    protected virtual void CreateSink()
    {
    }

    protected virtual void DetachSink()
    {
    }
}

public class ToolStrip : Control
{
    public ToolStripItemCollection Items { get; } = new();

    public bool CanOverflow { get; set; } = true;

    public ToolStripGripStyle GripStyle { get; set; }

    public bool ShowItemToolTips { get; set; } = true;

    public bool Stretch { get; set; } = true;
}

public class MenuStrip : ToolStrip
{
}

public class StatusStrip : ToolStrip
{
}

public class ContextMenuStrip : ToolStrip
{
    public static event EventHandler<ContextMenuStripShowRequestedEventArgs>? ShowRequested;

    public ContextMenuStrip()
    {
    }

    public ContextMenuStrip(IContainer container)
    {
        container?.Add(this);
    }

    public event CancelEventHandler? Opening;
    public event EventHandler? Opened;
    public event EventHandler? Closed;

    public Control? SourceControl { get; private set; }

    public Point ShowPosition { get; private set; }

    public void Show(Control control, Point position)
    {
        SourceControl = control;
        ShowPosition = position;

        var opening = new CancelEventArgs();
        Opening?.Invoke(this, opening);
        if (!opening.Cancel)
        {
            Visible = true;
            Opened?.Invoke(this, EventArgs.Empty);
            var requested = new ContextMenuStripShowRequestedEventArgs(this, control, position);
            ShowRequested?.Invoke(this, requested);
        }
    }

    public void Close()
    {
        if (Visible)
        {
            Visible = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class ContextMenuStripShowRequestedEventArgs : EventArgs
{
    public ContextMenuStripShowRequestedEventArgs(ContextMenuStrip contextMenuStrip, Control control, Point position)
    {
        ContextMenuStrip = contextMenuStrip;
        Control = control;
        Position = position;
    }

    public ContextMenuStrip ContextMenuStrip { get; }

    public Control Control { get; }

    public Point Position { get; }

    public bool Handled { get; set; }
}

public class ToolStripDropDown : ToolStrip
{
}

public class ToolStripItem : Component
{
    public event EventHandler? Click;
    public event EventHandler? TextChanged;

    public bool Available { get; set; } = true;

    public bool AutoSize { get; set; } = true;

    public ToolStripItemDisplayStyle DisplayStyle { get; set; } = ToolStripItemDisplayStyle.ImageAndText;

    public virtual bool Enabled { get; set; } = true;

    public Image? Image { get; set; }

    public ToolStripItemImageScaling ImageScaling { get; set; } = ToolStripItemImageScaling.SizeToFit;

    public string? ImageKey { get; set; }

    public Color ImageTransparentColor { get; set; } = Color.Empty;

    public string Name { get; set; } = string.Empty;

    public RightToLeft RightToLeft { get; set; }

    public Size Size { get; set; }

    public object? Tag { get; set; }

    public bool Selected { get; set; }

    public string ToolTipText { get; set; } = string.Empty;

    public virtual bool Visible { get; set; } = true;

    public int Width { get; set; }

    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            TextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public virtual void PerformClick()
    {
        OnClick(EventArgs.Empty);
    }

    public virtual bool Focus()
    {
        return true;
    }

    protected virtual void OnClick(EventArgs e)
    {
        Click?.Invoke(this, e);
    }
}

public class ToolStripMenuItem : ToolStripItem
{
    private bool _checked;
    private CheckState _checkState;

    public ToolStripMenuItem()
    {
    }

    public ToolStripMenuItem(string text)
    {
        Text = text;
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            _checkState = value ? CheckState.Checked : CheckState.Unchecked;
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            _checkState = value;
            Checked = value == CheckState.Checked;
        }
    }

    public bool CheckOnClick { get; set; }

    public ToolStripDropDown DropDown { get; } = new();

    public ToolStripItemCollection DropDownItems { get; } = new();

    public string ShortcutKeyDisplayString { get; set; } = string.Empty;

    public Keys ShortcutKeys { get; set; }

    public void ShowDropDown()
    {
        OnDropDownShow(EventArgs.Empty);
        DropDown.Visible = true;
    }

    protected virtual void OnDropDownShow(EventArgs e)
    {
    }
}

public class ToolStripButton : ToolStripItem
{
    public bool Checked { get; set; }

    public bool CheckOnClick { get; set; }
}

public class ToolStripDropDownButton : ToolStripItem
{
    public ToolStripDropDown DropDown { get; } = new();

    public ToolStripItemCollection DropDownItems { get; } = new();

    public void ShowDropDown()
    {
        OnDropDownShow(EventArgs.Empty);
        DropDown.Visible = true;
    }

    protected virtual void OnDropDownShow(EventArgs e)
    {
    }
}

public class ToolStripSplitButton : ToolStripDropDownButton
{
    public event EventHandler? ButtonClick;

    public void PerformButtonClick()
    {
        OnButtonClick(EventArgs.Empty);
    }

    protected virtual void OnButtonClick(EventArgs e)
    {
        ButtonClick?.Invoke(this, e);
    }
}

public class ToolStripLabel : ToolStripItem
{
}

public class ToolStripControlHost : ToolStripItem
{
    public ToolStripControlHost(Control control)
    {
        Control = control;
    }

    public Control Control { get; }
}

public class ToolStripComboBox : ToolStripItem
{
    public event KeyEventHandler? KeyDown;
    public event EventHandler? SelectedIndexChanged;

    public ComboBox ComboBox { get; } = new();

    public AutoCompleteMode AutoCompleteMode
    {
        get => ComboBox.AutoCompleteMode;
        set => ComboBox.AutoCompleteMode = value;
    }

    public AutoCompleteSource AutoCompleteSource
    {
        get => ComboBox.AutoCompleteSource;
        set => ComboBox.AutoCompleteSource = value;
    }

    public FlatStyle FlatStyle { get; set; }

    public ListBox.ListBoxObjectCollection Items => ComboBox.Items;

    public int SelectedIndex
    {
        get => ComboBox.SelectedIndex;
        set
        {
            if (ComboBox.SelectedIndex == value)
            {
                return;
            }

            ComboBox.SelectedIndex = value;
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem => ComboBox.SelectedItem;

    protected virtual void OnKeyDown(KeyEventArgs e)
    {
        KeyDown?.Invoke(this, e);
    }
}

public class ToolStripProgressBar : ToolStripControlHost
{
    public ToolStripProgressBar()
        : base(new ProgressBar())
    {
    }

    private ProgressBar ProgressBar => (ProgressBar)Control;

    public int Maximum
    {
        get => ProgressBar.Maximum;
        set => ProgressBar.Maximum = value;
    }

    public int Minimum
    {
        get => ProgressBar.Minimum;
        set => ProgressBar.Minimum = value;
    }

    public ProgressBarStyle Style
    {
        get => ProgressBar.Style;
        set => ProgressBar.Style = value;
    }

    public int Step
    {
        get => ProgressBar.Step;
        set => ProgressBar.Step = value;
    }

    public int Value
    {
        get => ProgressBar.Value;
        set => ProgressBar.Value = value;
    }

    public void PerformStep()
    {
        ProgressBar.PerformStep();
    }
}

public class ToolStripTextBox : ToolStripControlHost
{
    public ToolStripTextBox()
        : base(new TextBox())
    {
    }

    private TextBox TextBox => (TextBox)Control;

    public override bool Enabled
    {
        get => TextBox.Enabled;
        set => TextBox.Enabled = value;
    }

    public override bool Visible
    {
        get => TextBox.Visible;
        set => TextBox.Visible = value;
    }

    public string? SelectedText
    {
        get => TextBox.SelectedText;
        set => TextBox.SelectedText = value;
    }

    public int SelectionLength
    {
        get => TextBox.SelectionLength;
        set => TextBox.SelectionLength = value;
    }

    public int SelectionStart
    {
        get => TextBox.SelectionStart;
        set => TextBox.SelectionStart = value;
    }

    public override bool Focus()
    {
        return TextBox.Focus();
    }
}

public class ToolStripSeparator : ToolStripItem
{
}

public class ToolStripItemCollection : Collection<ToolStripItem>
{
    public ToolStripItem Add(string text)
    {
        var item = new ToolStripMenuItem(text);
        Add(item);
        return item;
    }

    public void AddRange(ToolStripItem[] items)
    {
        foreach (ToolStripItem item in items)
        {
            Add(item);
        }
    }

    public ToolStripItem Add(string text, Image? image, EventHandler? onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            Image = image
        };
        if (onClick != null)
        {
            item.Click += onClick;
        }

        Add(item);
        return item;
    }
}

public abstract class FileDialog : Component
{
    public bool AddExtension { get; set; } = true;

    public bool CheckFileExists { get; set; }

    public bool CheckPathExists { get; set; } = true;

    public string DefaultExt { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Filter { get; set; } = string.Empty;

    public int FilterIndex { get; set; } = 1;

    public string InitialDirectory { get; set; } = string.Empty;

    public bool RestoreDirectory { get; set; }

    public string Title { get; set; } = string.Empty;

    protected virtual string DialogKind => "OpenFile";

    protected virtual bool AllowMultipleSelection => false;

    public virtual DialogResult ShowDialog()
    {
        PortableFileDialogResult? result = PortableWinFormsDialogService.ShowFileDialog(
            DialogKind,
            Title,
            InitialDirectory,
            suggestedItemName: FileName,
            defaultExtension: DefaultExt,
            Filter,
            FilterIndex,
            AllowMultipleSelection);
        if (result == null || result.SelectedPathCount == 0 || string.IsNullOrEmpty(result.SelectedPath))
        {
            return DialogResult.Cancel;
        }

        SetSelectedPaths(result.SelectedPaths);
        return DialogResult.OK;
    }

    public virtual DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }

    protected virtual void SetSelectedPaths(ReadOnlySpan<string> selectedPaths)
    {
        FileName = selectedPaths[0];
    }
}

public class OpenFileDialog : FileDialog
{
    protected override string DialogKind => "OpenFile";

    protected override bool AllowMultipleSelection => Multiselect;

    public bool Multiselect { get; set; }

    public string[] FileNames { get; set; } = Array.Empty<string>();

    protected override void SetSelectedPaths(ReadOnlySpan<string> selectedPaths)
    {
        int selectedPathCount = Multiselect ? selectedPaths.Length : 1;
        FileNames = selectedPaths[..selectedPathCount].ToArray();
        FileName = FileNames[0];
    }
}

public class SaveFileDialog : FileDialog
{
    protected override string DialogKind => "SaveFile";

    public bool OverwritePrompt { get; set; } = true;
}

public class PrintDialog : Component
{
    public bool AllowSomePages { get; set; }

    public PrintDocument? Document { get; set; }

    public DialogResult ShowDialog()
    {
        return DialogResult.Cancel;
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }
}

public class PrintPreviewDialog : Form
{
    public PrintDocument? Document { get; set; }

    public bool TopMost { get; set; }

    public void Show(IWin32Window owner)
    {
        Show();
    }
}

public class FolderBrowserDialog : Component
{
    public string Description { get; set; } = string.Empty;

    public Environment.SpecialFolder RootFolder { get; set; } = Environment.SpecialFolder.Desktop;

    public string SelectedPath { get; set; } = string.Empty;

    public bool ShowNewFolderButton { get; set; } = true;

    public DialogResult ShowDialog()
    {
        PortableFileDialogResult? result = PortableWinFormsDialogService.ShowFileDialog(
            "PickFolder",
            Description,
            SelectedPath,
            suggestedItemName: string.Empty,
            defaultExtension: string.Empty,
            filter: string.Empty,
            filterIndex: 1);
        if (result == null || string.IsNullOrEmpty(result.SelectedPath))
        {
            return DialogResult.Cancel;
        }

        SelectedPath = result.SelectedPath;
        return DialogResult.OK;
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }
}

public class ColorDialog : Component
{
    public Color Color { get; set; }

    public int[] CustomColors { get; set; } = Array.Empty<int>();

    public DialogResult ShowDialog()
    {
        return RunDialog(IntPtr.Zero) ? DialogResult.OK : DialogResult.Cancel;
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }

    protected virtual bool RunDialog(IntPtr hwndOwner)
    {
        int? selectedArgb = PortableWinFormsColorDialogService.ShowColorDialog(Color.ToArgb(), CustomColors);
        if (!selectedArgb.HasValue)
        {
            return false;
        }

        Color = Color.FromArgb(selectedArgb.Value);
        return true;
    }
}

public sealed class ToolTip : Component
{
    private readonly Dictionary<Control, string> _toolTips = new();

    public bool Active { get; set; } = true;

    public void SetToolTip(Control control, string? caption)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (string.IsNullOrEmpty(caption))
        {
            _toolTips.Remove(control);
        }
        else
        {
            _toolTips[control] = caption;
        }
    }

    public string GetToolTip(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _toolTips.TryGetValue(control, out string? value) ? value : string.Empty;
    }
}

public sealed class ErrorProvider : Component, IExtenderProvider, ISupportInitialize
{
    private readonly Dictionary<Control, string> _errors = new();
    private readonly Dictionary<Control, ErrorIconAlignment> _iconAlignments = new();
    private readonly Dictionary<Control, int> _iconPadding = new();

    public ErrorProvider()
    {
    }

    public ErrorProvider(ContainerControl parentControl)
    {
        ContainerControl = parentControl;
    }

    public ErrorProvider(IContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Add(this);
    }

    public int BlinkRate { get; set; } = 250;

    public ErrorBlinkStyle BlinkStyle { get; set; } = ErrorBlinkStyle.BlinkIfDifferentError;

    public ContainerControl? ContainerControl { get; set; }

    public string? DataMember { get; set; }

    public object? DataSource { get; set; }

    public Icon? Icon { get; set; }

    public RightToLeft RightToLeft { get; set; } = RightToLeft.Inherit;

    public bool CanExtend(object extendee)
    {
        return extendee is Control;
    }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }

    public string GetError(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _errors.TryGetValue(control, out string? value) ? value : string.Empty;
    }

    public ErrorIconAlignment GetIconAlignment(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _iconAlignments.TryGetValue(control, out ErrorIconAlignment value) ? value : ErrorIconAlignment.MiddleRight;
    }

    public int GetIconPadding(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return _iconPadding.TryGetValue(control, out int value) ? value : 0;
    }

    public void SetError(Control control, string? value)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (string.IsNullOrEmpty(value))
        {
            _errors.Remove(control);
        }
        else
        {
            _errors[control] = value;
        }
    }

    public void SetIconAlignment(Control control, ErrorIconAlignment value)
    {
        ArgumentNullException.ThrowIfNull(control);
        _iconAlignments[control] = value;
    }

    public void SetIconPadding(Control control, int padding)
    {
        ArgumentNullException.ThrowIfNull(control);
        _iconPadding[control] = padding;
    }

    public void Clear()
    {
        _errors.Clear();
    }
}

public class AxHost : Control
{
    public sealed class ConnectionPointCookie
    {
        public ConnectionPointCookie(object? source, object? sink, Type eventInterface)
        {
            EventInterface = eventInterface;
        }

        public Type EventInterface { get; }

        public void Disconnect()
        {
        }
    }
}

public class FontDialog : Component
{
    public Font? Font { get; set; } = SystemFonts.DefaultFont;

    public bool ShowEffects { get; set; } = true;

    public bool ShowColor { get; set; }

    public int MinSize { get; set; }

    public int MaxSize { get; set; }

    public DialogResult ShowDialog()
    {
        return RunDialog(IntPtr.Zero) ? DialogResult.OK : DialogResult.Cancel;
    }

    public DialogResult ShowDialog(IWin32Window owner)
    {
        return ShowDialog();
    }

    protected virtual bool RunDialog(IntPtr hwndOwner)
    {
        Font initialFont = Font ?? SystemFonts.DefaultFont;
        var request = new PortableFontDialogRequest(
            initialFont.Name,
            initialFont.Size,
            (int)initialFont.Style,
            initialFont.Unit.ToString(),
            ShowEffects,
            ShowColor,
            MinSize,
            MaxSize);

        PortableFontDialogResult? result = PortableWinFormsFontDialogService.ShowFontDialog(request);
        if (result == null)
        {
            return false;
        }

        Font = new Font(
            result.FamilyName,
            result.Size,
            (FontStyle)result.Style,
            ParseGraphicsUnit(result.Unit),
            initialFont.GdiCharSet,
            initialFont.GdiVerticalFont);
        return true;
    }

    private static GraphicsUnit ParseGraphicsUnit(string unit)
    {
        return Enum.TryParse(unit, ignoreCase: true, out GraphicsUnit parsed)
            ? parsed
            : GraphicsUnit.Point;
    }
}

public class Timer : Component
{
    public event EventHandler? Tick;

    public bool Enabled { get; set; }

    public int Interval { get; set; } = 100;

    public void Start()
    {
        Enabled = true;
    }

    public void Stop()
    {
        Enabled = false;
    }

    public void RaiseTick()
    {
        if (Enabled)
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }
}

public class ListView : Control
{
    private const int HeaderHeight = 20;
    private const int RowHeight = 18;
    private bool _syncingCheckedItems;
    private bool _syncingSelection;

    public event LabelEditEventHandler? AfterLabelEdit;
    public event ColumnClickEventHandler? ColumnClick;
    public event ItemCheckEventHandler? ItemCheck;
    public event EventHandler? ItemActivate;
    public event EventHandler? SelectedIndexChanged;

    public ColumnHeaderCollection Columns { get; } = new();

    public ListViewGroupCollection Groups { get; } = new();

    public ListViewItemCollection Items { get; }

    public CheckedListViewItemCollection CheckedItems { get; }

    public CheckedIndexCollection CheckedIndices { get; }

    public SelectedListViewItemCollection SelectedItems { get; }

    public IComparer? ListViewItemSorter { get; set; }

    public ImageList? LargeImageList { get; set; }

    public ImageList? SmallImageList { get; set; }

    public bool AllowColumnReorder { get; set; }

    public ListViewAlignment Alignment { get; set; }

    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;

    public bool HideSelection { get; set; } = true;

    public bool CheckBoxes { get; set; }

    public bool FullRowSelect { get; set; }

    public bool GridLines { get; set; }

    public ColumnHeaderStyle HeaderStyle { get; set; } = ColumnHeaderStyle.Clickable;

    public bool LabelEdit { get; set; }

    public bool MultiSelect { get; set; } = true;

    public SortOrder Sorting { get; set; }

    public bool UseCompatibleStateImageBehavior { get; set; }

    public View View { get; set; }

    public ListView()
    {
        Items = new ListViewItemCollection(this);
        CheckedItems = new CheckedListViewItemCollection(this);
        CheckedIndices = new CheckedIndexCollection(this);
        SelectedItems = new SelectedListViewItemCollection(this);
    }

    public void BeginUpdate()
    {
    }

    public void EndUpdate()
    {
    }

    public ListViewItem? GetItemAt(int x, int y)
    {
        if (x < 0 || y < 0)
        {
            return null;
        }

        bool showDetails = View == View.Details || Columns.Count > 0;
        int itemY = showDetails && HeaderStyle != ColumnHeaderStyle.None ? y - HeaderHeight : y;
        if (itemY < 0)
        {
            return null;
        }

        int index = itemY / RowHeight;
        return index >= 0 && index < Items.Count ? Items[index] : null;
    }

    public void Sort()
    {
        if (ListViewItemSorter != null)
        {
            Items.Sort(ListViewItemSorter);
        }
    }

    protected virtual void OnColumnClick(ColumnClickEventArgs e)
    {
        ColumnClick?.Invoke(this, e);
    }

    protected virtual void OnItemCheck(ItemCheckEventArgs e)
    {
        ItemCheck?.Invoke(this, e);
    }

    public void RaiseColumnClick(int column)
    {
        if (column < 0 || column >= Columns.Count)
        {
            return;
        }

        OnColumnClick(new ColumnClickEventArgs(column));
        Invalidate();
    }

    public bool TryRaiseColumnClickAt(int x, int y)
    {
        if (x < 0 || y < 0 || y >= HeaderHeight || HeaderStyle != ColumnHeaderStyle.Clickable)
        {
            return false;
        }

        if (View != View.Details && Columns.Count == 0)
        {
            return false;
        }

        int currentX = 0;
        for (int columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            int width = Columns[columnIndex].Width > 0 ? Columns[columnIndex].Width : 120;
            if (x >= currentX && x < currentX + width)
            {
                RaiseColumnClick(columnIndex);
                return true;
            }

            currentX += width;
        }

        return false;
    }

    protected virtual void OnItemActivate(EventArgs e)
    {
        ItemActivate?.Invoke(this, e);
    }

    protected virtual void OnAfterLabelEdit(LabelEditEventArgs e)
    {
        AfterLabelEdit?.Invoke(this, e);
    }

    protected virtual void OnSelectedIndexChanged(EventArgs e)
    {
        SelectedIndexChanged?.Invoke(this, e);
    }

    internal void SetItemSelected(ListViewItem item, bool selected, bool raiseEvent)
    {
        if (item.Owner != null && !ReferenceEquals(item.Owner, this))
        {
            return;
        }

        if (item.SelectedCore == selected)
        {
            return;
        }

        bool changed = false;
        if (selected && !MultiSelect)
        {
            foreach (ListViewItem selectedItem in SelectedItems.ToArray())
            {
                if (!ReferenceEquals(selectedItem, item))
                {
                    SetItemSelected(selectedItem, false, false);
                    changed = true;
                }
            }
        }

        item.SetSelectedCore(selected);
        _syncingSelection = true;
        try
        {
            if (selected)
            {
                if (!SelectedItems.Contains(item))
                {
                    SelectedItems.Add(item);
                }
            }
            else
            {
                SelectedItems.Remove(item);
            }
        }
        finally
        {
            _syncingSelection = false;
        }

        changed = true;
        Invalidate();
        if (changed && raiseEvent)
        {
            OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    internal void SetItemChecked(ListViewItem item, bool value, bool raiseEvent)
    {
        if (item.Owner != null && !ReferenceEquals(item.Owner, this))
        {
            return;
        }

        if (item.CheckedCore == value)
        {
            return;
        }

        bool finalValue = value;
        if (raiseEvent && item.Owner != null)
        {
            int index = Items.IndexOf(item);
            if (index >= 0)
            {
                var eventArgs = new ItemCheckEventArgs(
                    index,
                    value ? CheckState.Checked : CheckState.Unchecked,
                    item.CheckedCore ? CheckState.Checked : CheckState.Unchecked);
                OnItemCheck(eventArgs);
                finalValue = eventArgs.NewValue != CheckState.Unchecked;
            }
        }

        if (item.CheckedCore == finalValue)
        {
            return;
        }

        item.SetCheckedCore(finalValue);
        _syncingCheckedItems = true;
        try
        {
            if (finalValue)
            {
                if (!CheckedItems.Contains(item))
                {
                    CheckedItems.Add(item);
                }
            }
            else
            {
                CheckedItems.Remove(item);
            }
        }
        finally
        {
            _syncingCheckedItems = false;
        }

        Invalidate();
    }

    public void RaiseItemActivate()
    {
        OnItemActivate(EventArgs.Empty);
    }

    public bool TryActivateItemAt(int x, int y)
    {
        ListViewItem? item = GetItemAt(x, y);
        if (item == null)
        {
            return false;
        }

        if (!item.Selected)
        {
            item.Selected = true;
        }

        RaiseItemActivate();
        return true;
    }

    public bool TryToggleItemCheckAt(int x, int y)
    {
        if (!CheckBoxes || x < 0 || x > 24)
        {
            return false;
        }

        ListViewItem? item = GetItemAt(x, y);
        if (item == null)
        {
            return false;
        }

        if (!item.Selected)
        {
            item.Selected = true;
        }

        item.Checked = !item.Checked;
        return true;
    }

    internal void AttachItem(ListViewItem item)
    {
        item.SetOwner(this);
        if (item.CheckedCore && !CheckedItems.Contains(item))
        {
            _syncingCheckedItems = true;
            try
            {
                CheckedItems.Add(item);
            }
            finally
            {
                _syncingCheckedItems = false;
            }
        }

        if (item.SelectedCore && !SelectedItems.Contains(item))
        {
            _syncingSelection = true;
            try
            {
                SelectedItems.Add(item);
            }
            finally
            {
                _syncingSelection = false;
            }
        }
    }

    internal void DetachItem(ListViewItem item)
    {
        if (SelectedItems.Contains(item))
        {
            _syncingSelection = true;
            try
            {
                SelectedItems.Remove(item);
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        if (CheckedItems.Contains(item))
        {
            _syncingCheckedItems = true;
            try
            {
                CheckedItems.Remove(item);
            }
            finally
            {
                _syncingCheckedItems = false;
            }
        }

        item.SetSelectedCore(false);
        item.SetOwner(null);
    }

    public sealed class ColumnHeaderCollection : Collection<ColumnHeader>
    {
        public ColumnHeader Add(string text, int width, HorizontalAlignment textAlign)
        {
            var columnHeader = new ColumnHeader
            {
                Text = text,
                Width = width,
                TextAlign = textAlign
            };
            Add(columnHeader);
            return columnHeader;
        }

        public void AddRange(ColumnHeader[] values)
        {
            foreach (ColumnHeader value in values)
            {
                Add(value);
            }
        }
    }

    public sealed class ListViewGroupCollection : Collection<ListViewGroup>
    {
        public ListViewGroup? this[string key]
        {
            get
            {
                foreach (ListViewGroup group in this)
                {
                    if (string.Equals(group.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return group;
                    }
                }

                return null;
            }
        }

        public void AddRange(ListViewGroup[] groups)
        {
            foreach (ListViewGroup group in groups)
            {
                Add(group);
            }
        }
    }

    public sealed class ListViewItemCollection : Collection<ListViewItem>
    {
        private readonly ListView _owner;

        internal ListViewItemCollection(ListView owner)
        {
            _owner = owner;
        }

        public new ListViewItem Add(ListViewItem item)
        {
            base.Add(item);
            return item;
        }

        public ListViewItem Add(string text)
        {
            var item = new ListViewItem(text);
            Add(item);
            return item;
        }

        public ListViewItem Add(string text, int imageIndex)
        {
            var item = new ListViewItem(text)
            {
                ImageIndex = imageIndex
            };
            Add(item);
            return item;
        }

        public void AddRange(ListViewItem[] values)
        {
            foreach (ListViewItem value in values)
            {
                Add(value);
            }
        }

        internal void Sort(IComparer comparer)
        {
            var items = new List<ListViewItem>(this);
            items.Sort((x, y) => comparer.Compare(x, y));
            ClearItems();
            foreach (var item in items)
            {
                Add(item);
            }
        }

        protected override void InsertItem(int index, ListViewItem item)
        {
            base.InsertItem(index, item);
            _owner.AttachItem(item);
            _owner.Invalidate();
        }

        protected override void RemoveItem(int index)
        {
            ListViewItem item = this[index];
            _owner.DetachItem(item);
            base.RemoveItem(index);
            _owner.Invalidate();
        }

        protected override void ClearItems()
        {
            foreach (ListViewItem item in this)
            {
                _owner.DetachItem(item);
            }

            base.ClearItems();
            _owner.Invalidate();
        }
    }

    public sealed class SelectedListViewItemCollection : Collection<ListViewItem>
    {
        private readonly ListView _owner;

        internal SelectedListViewItemCollection(ListView owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, ListViewItem item)
        {
            if (_owner._syncingSelection)
            {
                base.InsertItem(index, item);
                return;
            }

            _owner.SetItemSelected(item, true, true);
        }

        protected override void RemoveItem(int index)
        {
            if (_owner._syncingSelection)
            {
                base.RemoveItem(index);
                return;
            }

            _owner.SetItemSelected(this[index], false, true);
        }

        protected override void ClearItems()
        {
            if (_owner._syncingSelection)
            {
                base.ClearItems();
                return;
            }

            foreach (ListViewItem item in this.ToArray())
            {
                _owner.SetItemSelected(item, false, false);
            }

            _owner.OnSelectedIndexChanged(EventArgs.Empty);
        }
    }

    public sealed class CheckedListViewItemCollection : Collection<ListViewItem>
    {
        private readonly ListView _owner;

        internal CheckedListViewItemCollection(ListView owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, ListViewItem item)
        {
            if (_owner._syncingCheckedItems)
            {
                base.InsertItem(index, item);
                return;
            }

            _owner.SetItemChecked(item, true, true);
        }

        protected override void RemoveItem(int index)
        {
            if (_owner._syncingCheckedItems)
            {
                base.RemoveItem(index);
                return;
            }

            _owner.SetItemChecked(this[index], false, true);
        }

        protected override void ClearItems()
        {
            if (_owner._syncingCheckedItems)
            {
                base.ClearItems();
                return;
            }

            foreach (ListViewItem item in this.ToArray())
            {
                _owner.SetItemChecked(item, false, false);
            }
        }
    }

    public sealed class CheckedIndexCollection : IReadOnlyList<int>, ICollection
    {
        private readonly ListView _owner;

        internal CheckedIndexCollection(ListView owner)
        {
            _owner = owner;
        }

        public int Count => _owner.CheckedItems.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public int this[int index] => Snapshot()[index];

        public bool Contains(int index)
        {
            return index >= 0
                && index < _owner.Items.Count
                && _owner.Items[index].Checked;
        }

        public int IndexOf(int index)
        {
            return Snapshot().IndexOf(index);
        }

        public void CopyTo(Array array, int index)
        {
            ((ICollection)Snapshot()).CopyTo(array, index);
        }

        public IEnumerator<int> GetEnumerator()
        {
            return Snapshot().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private List<int> Snapshot()
        {
            var indices = new List<int>();
            for (int i = 0; i < _owner.Items.Count; i++)
            {
                if (_owner.Items[i].Checked)
                {
                    indices.Add(i);
                }
            }

            return indices;
        }
    }
}

public class TreeView : Control
{
    private TreeNode? _selectedNode;

    public event TreeViewCancelEventHandler? BeforeExpand;
    public event TreeViewEventHandler? AfterExpand;
    public event TreeViewCancelEventHandler? BeforeCollapse;
    public event TreeViewEventHandler? AfterCollapse;
    public event TreeViewCancelEventHandler? BeforeSelect;
    public event TreeViewEventHandler? AfterCheck;
    public event TreeViewEventHandler? AfterSelect;
    public event NodeLabelEditEventHandler? AfterLabelEdit;
    public event DrawTreeNodeEventHandler? DrawNode;
    public event ItemDragEventHandler? ItemDrag;

    public TreeNodeCollection Nodes { get; }

    public static new Font DefaultFont => SystemFonts.DefaultFont;

    public TreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value))
            {
                return;
            }

            if (value != null && !ReferenceEquals(value.TreeView, this))
            {
                return;
            }

            if (value != null)
            {
                var cancelEventArgs = new TreeViewCancelEventArgs(value, false, TreeViewAction.Unknown);
                OnBeforeSelect(cancelEventArgs);
                if (cancelEventArgs.Cancel)
                {
                    return;
                }
            }

            _selectedNode = value;
            Invalidate();
            if (value != null)
            {
                OnAfterSelect(new TreeViewEventArgs(value, TreeViewAction.Unknown));
            }
        }
    }

    public bool Sorted { get; set; }

    public IComparer? TreeViewNodeSorter { get; set; }

    public ImageList? ImageList { get; set; }

    public BorderStyle BorderStyle { get; set; } = BorderStyle.Fixed3D;

    public int ImageIndex { get; set; } = -1;

    public int SelectedImageIndex { get; set; } = -1;

    public bool LabelEdit { get; set; }

    public bool HideSelection { get; set; } = true;

    public TreeViewDrawMode DrawMode { get; set; }

    public TreeView()
    {
        Nodes = new TreeNodeCollection(this, null);
    }

    public virtual TreeNode? GetNodeAt(int x, int y)
    {
        foreach (TreeNode node in Nodes)
        {
            if (TryGetNodeAt(node, x, y, out TreeNode? match))
            {
                return match;
            }
        }

        return null;
    }

    public virtual TreeNode? GetNodeAt(Point pt)
    {
        return GetNodeAt(pt.X, pt.Y);
    }

    public virtual void BeginUpdate()
    {
    }

    public virtual void EndUpdate()
    {
    }

    public void ExpandAll()
    {
        foreach (TreeNode node in Nodes)
        {
            node.Expand();
        }
    }

    public void CollapseAll()
    {
        foreach (TreeNode node in Nodes)
        {
            node.Collapse();
        }
    }

    public new virtual void Sort()
    {
        if (TreeViewNodeSorter == null)
        {
            return;
        }

        Nodes.Sort(TreeViewNodeSorter);
    }

    internal void AttachNode(TreeNode node)
    {
        node.SetTreeView(this);
    }

    protected virtual void OnBeforeExpand(TreeViewCancelEventArgs e)
    {
        BeforeExpand?.Invoke(this, e);
    }

    protected virtual void OnAfterExpand(TreeViewEventArgs e)
    {
        AfterExpand?.Invoke(this, e);
    }

    protected virtual void OnBeforeCollapse(TreeViewCancelEventArgs e)
    {
        BeforeCollapse?.Invoke(this, e);
    }

    protected virtual void OnAfterCollapse(TreeViewEventArgs e)
    {
        AfterCollapse?.Invoke(this, e);
    }

    protected virtual void OnBeforeSelect(TreeViewCancelEventArgs e)
    {
        BeforeSelect?.Invoke(this, e);
    }

    protected virtual void OnAfterCheck(TreeViewEventArgs e)
    {
        AfterCheck?.Invoke(this, e);
    }

    protected virtual void OnAfterSelect(TreeViewEventArgs e)
    {
        AfterSelect?.Invoke(this, e);
    }

    protected virtual void OnAfterLabelEdit(NodeLabelEditEventArgs e)
    {
        AfterLabelEdit?.Invoke(this, e);
    }

    protected virtual void OnDrawNode(DrawTreeNodeEventArgs e)
    {
        DrawNode?.Invoke(this, e);
    }

    public void RaiseDrawNode(DrawTreeNodeEventArgs e)
    {
        OnDrawNode(e);
    }

    protected virtual void OnItemDrag(ItemDragEventArgs e)
    {
        ItemDrag?.Invoke(this, e);
    }

    internal bool RaiseBeforeExpand(TreeViewCancelEventArgs e)
    {
        OnBeforeExpand(e);
        return !e.Cancel;
    }

    internal void RaiseAfterExpand(TreeViewEventArgs e)
    {
        OnAfterExpand(e);
    }

    internal bool RaiseBeforeCollapse(TreeViewCancelEventArgs e)
    {
        OnBeforeCollapse(e);
        return !e.Cancel;
    }

    internal void RaiseAfterCollapse(TreeViewEventArgs e)
    {
        OnAfterCollapse(e);
    }

    internal void ClearSelectionForRemovedNode(TreeNode node)
    {
        if (_selectedNode == null || !IsNodeOrDescendant(node, _selectedNode))
        {
            return;
        }

        _selectedNode = null;
        Invalidate();
    }

    private static bool IsNodeOrDescendant(TreeNode root, TreeNode candidate)
    {
        for (TreeNode? current = candidate; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetNodeAt(TreeNode node, int x, int y, out TreeNode? match)
    {
        if (node.IsVisible && node.Bounds.Contains(x, y))
        {
            match = node;
            return true;
        }

        if (node.IsExpanded)
        {
            foreach (TreeNode child in node.Nodes)
            {
                if (TryGetNodeAt(child, x, y, out match))
                {
                    return true;
                }
            }
        }

        match = null;
        return false;
    }
}

public class TreeNode
{
    private TreeView? _treeView;
    private string _imageKey = string.Empty;
    private string _selectedImageKey = string.Empty;

    public TreeNode()
    {
        Nodes = new TreeNodeCollection(null, this);
    }

    public TreeNode(string text)
        : this()
    {
        Text = text;
    }

    public TreeNode(string text, int imageIndex, int selectedImageIndex)
        : this(text)
    {
        ImageIndex = imageIndex;
        SelectedImageIndex = selectedImageIndex;
    }

    public bool Checked { get; set; }

    public bool IsEditing { get; private set; }

    public bool IsExpanded { get; private set; }

    public bool IsVisible { get; set; } = true;

    public int ImageIndex { get; set; } = -1;

    public string ImageKey
    {
        get => _imageKey;
        set
        {
            _imageKey = value ?? string.Empty;
            if (_imageKey.Length > 0)
            {
                ImageIndex = -1;
            }

            TreeView?.Invalidate();
        }
    }

    public int SelectedImageIndex { get; set; } = -1;

    public string SelectedImageKey
    {
        get => _selectedImageKey;
        set
        {
            _selectedImageKey = value ?? string.Empty;
            if (_selectedImageKey.Length > 0)
            {
                SelectedImageIndex = -1;
            }

            TreeView?.Invalidate();
        }
    }

    public ContextMenuStrip? ContextMenuStrip { get; set; }

    public TreeNodeCollection Nodes { get; }

    public string Name { get; set; } = string.Empty;

    public TreeNode? Parent { get; internal set; }

    public string Text { get; set; } = string.Empty;

    public object? Tag { get; set; }

    public TreeView? TreeView => _treeView ?? Parent?.TreeView;

    public Rectangle Bounds { get; set; }

    public string FullPath => Parent != null && !string.IsNullOrEmpty(Parent.FullPath)
        ? Parent.FullPath + "\\" + Text
        : Text;

    public void BeginEdit()
    {
        IsEditing = true;
    }

    public void EndEdit(bool cancel)
    {
        IsEditing = false;
    }

    public void EnsureVisible()
    {
    }

    public void Expand()
    {
        if (IsExpanded)
        {
            return;
        }

        TreeView? treeView = TreeView;
        if (treeView != null)
        {
            var e = new TreeViewCancelEventArgs(this, false, TreeViewAction.Expand);
            if (!treeView.RaiseBeforeExpand(e))
            {
                return;
            }
        }

        IsExpanded = true;
        treeView?.Invalidate();
        treeView?.RaiseAfterExpand(new TreeViewEventArgs(this, TreeViewAction.Expand));
    }

    public void ExpandAll()
    {
        Expand();
        foreach (TreeNode node in Nodes)
        {
            node.ExpandAll();
        }
    }

    public void Collapse()
    {
        if (!IsExpanded)
        {
            return;
        }

        TreeView? treeView = TreeView;
        if (treeView != null)
        {
            var e = new TreeViewCancelEventArgs(this, false, TreeViewAction.Collapse);
            if (!treeView.RaiseBeforeCollapse(e))
            {
                return;
            }
        }

        IsExpanded = false;
        treeView?.Invalidate();
        treeView?.RaiseAfterCollapse(new TreeViewEventArgs(this, TreeViewAction.Collapse));
    }

    public void Collapse(bool ignoreChildren)
    {
        if (!ignoreChildren)
        {
            foreach (TreeNode node in Nodes)
            {
                node.Collapse(false);
            }
        }

        Collapse();
    }

    public void Toggle()
    {
        if (IsExpanded)
        {
            Collapse();
        }
        else
        {
            Expand();
        }
    }

    public void Remove()
    {
        if (Parent != null)
        {
            Parent.Nodes.Remove(this);
            return;
        }

        TreeView?.Nodes.Remove(this);
    }

    internal void SetTreeView(TreeView? treeView)
    {
        _treeView = treeView;
        foreach (TreeNode node in Nodes)
        {
            node.SetTreeView(treeView);
        }
    }
}

public class TreeNodeCollection : Collection<TreeNode>
{
    private readonly TreeView? _treeView;
    private readonly TreeNode? _parent;

    internal TreeNodeCollection(TreeView? treeView, TreeNode? parent)
    {
        _treeView = treeView;
        _parent = parent;
    }

    public new int Add(TreeNode node)
    {
        base.Add(node);
        return Count - 1;
    }

    public TreeNode Add(string text)
    {
        var node = new TreeNode(text);
        Add(node);
        return node;
    }

    public void AddRange(TreeNode[] nodes)
    {
        foreach (TreeNode node in nodes)
        {
            Add(node);
        }
    }

    public void CopyTo(TreeNode[] array, int arrayIndex)
    {
        for (int i = 0; i < Count; i++)
        {
            array[arrayIndex + i] = this[i];
        }
    }

    internal void Sort(IComparer comparer)
    {
        var nodes = new List<TreeNode>(this);
        nodes.Sort((x, y) => comparer.Compare(x, y));
        ClearItems();
        foreach (TreeNode node in nodes)
        {
            Add(node);
        }
    }

    protected override void InsertItem(int index, TreeNode item)
    {
        item.Parent = _parent;
        item.SetTreeView(_treeView ?? _parent?.TreeView);
        base.InsertItem(index, item);
        item.TreeView?.Invalidate();
    }

    protected override void RemoveItem(int index)
    {
        TreeNode item = this[index];
        TreeView? treeView = item.TreeView;
        treeView?.ClearSelectionForRemovedNode(item);
        item.Parent = null;
        item.SetTreeView(null);
        base.RemoveItem(index);
        treeView?.Invalidate();
    }

    protected override void ClearItems()
    {
        TreeView? treeView = _treeView ?? _parent?.TreeView;
        foreach (TreeNode node in this)
        {
            treeView?.ClearSelectionForRemovedNode(node);
            node.Parent = null;
            node.SetTreeView(null);
        }

        base.ClearItems();
        treeView?.Invalidate();
    }
}

public class ColumnHeader
{
    public string? ImageKey { get; set; }

    public string Text { get; set; } = string.Empty;

    public HorizontalAlignment TextAlign { get; set; }

    public int Width { get; set; }
}

public class ListViewGroup
{
    public ListViewGroup()
    {
    }

    public ListViewGroup(string header, HorizontalAlignment headerAlignment)
    {
        Header = header;
        HeaderAlignment = headerAlignment;
    }

    public ListViewGroup(string key, string headerText)
    {
        Name = key;
        Header = headerText;
    }

    public string Header { get; set; } = string.Empty;

    public HorizontalAlignment HeaderAlignment { get; set; }

    public string Name { get; set; } = string.Empty;
}

public class ListViewItem
{
    private bool _checked;
    private bool _isEditing;
    private bool _selected;
    private ListView? _owner;

    public ListViewItem()
    {
    }

    public ListViewItem(string text)
    {
        Text = text;
        SubItems.Add(new ListViewSubItem(this, text));
    }

    public ListViewItem(string text, int imageIndex)
        : this(text)
    {
        ImageIndex = imageIndex;
    }

    public ListViewItem(string[] items)
    {
        if (items.Length > 0)
        {
            Text = items[0];
        }

        foreach (string item in items)
        {
            SubItems.Add(new ListViewSubItem(this, item));
        }
    }

    public ListViewItem(string[] items, int imageIndex)
        : this(items)
    {
        ImageIndex = imageIndex;
    }

    public ListViewSubItemCollection SubItems { get; } = new();

    public string Text { get; set; } = string.Empty;

    public object? Tag { get; set; }

    public int ImageIndex { get; set; } = -1;

    public ListViewGroup? Group { get; set; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_owner != null)
            {
                _owner.SetItemSelected(this, value, true);
            }
            else
            {
                _selected = value;
            }
        }
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_owner != null)
            {
                _owner.SetItemChecked(this, value, true);
            }
            else
            {
                _checked = value;
            }
        }
    }

    internal ListView? Owner => _owner;

    internal bool CheckedCore => _checked;

    internal bool SelectedCore => _selected;

    public void BeginEdit()
    {
        _isEditing = true;
    }

    internal void SetOwner(ListView? owner)
    {
        _owner = owner;
    }

    internal void SetCheckedCore(bool value)
    {
        _checked = value;
    }

    internal void SetSelectedCore(bool selected)
    {
        _selected = selected;
    }

    public sealed class ListViewSubItem
    {
        public ListViewSubItem()
        {
        }

        public ListViewSubItem(ListViewItem? owner, string text)
        {
            Owner = owner;
            Text = text;
        }

        public ListViewItem? Owner { get; }

        public string Text { get; set; } = string.Empty;
    }

    public sealed class ListViewSubItemCollection : Collection<ListViewSubItem>
    {
        public int Add(string text)
        {
            Add(new ListViewSubItem(null!, text));
            return Count - 1;
        }
    }
}

public sealed class ImageList : Component
{
    public ImageList()
    {
    }

    public ImageList(IContainer container)
    {
        container?.Add(this);
    }

    public ImageCollection Images { get; } = new();

    public ColorDepth ColorDepth { get; set; }

    public ImageListStreamer? ImageStream { get; set; }

    public Size ImageSize { get; set; } = new(16, 16);

    public Color TransparentColor { get; set; } = Color.Transparent;

    public sealed class ImageCollection
    {
        private readonly List<Image> _images = new();
        private readonly List<string> _keys = new();

        public int Count => _images.Count;

        public bool Empty => _images.Count == 0;

        public ICollection Keys => _keys.AsReadOnly();

        public Image this[int index]
        {
            get => _images[index];
            set => _images[index] = value;
        }

        public Image? this[string key]
        {
            get
            {
                int index = IndexOfKey(key);
                return index >= 0 ? _images[index] : null;
            }
        }

        public int Add(Icon icon)
        {
            return Add(icon.ToBitmap());
        }

        public int Add(Image image)
        {
            _images.Add(image);
            _keys.Add(string.Empty);
            return _images.Count - 1;
        }

        public void Add(string key, Icon icon)
        {
            Add(key, icon.ToBitmap());
        }

        public void Add(string key, Image image)
        {
            int index = Add(image);
            SetKeyName(index, key);
        }

        public void AddRange(Image[] images)
        {
            if (images == null)
            {
                return;
            }

            foreach (Image image in images)
            {
                Add(image);
            }
        }

        public void Clear()
        {
            _images.Clear();
            _keys.Clear();
        }

        public bool ContainsKey(string key)
        {
            return IndexOfKey(key) >= 0;
        }

        public int IndexOf(Image image)
        {
            return _images.IndexOf(image);
        }

        public int IndexOfKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return -1;
            }

            for (int i = 0; i < _keys.Count; i++)
            {
                if (string.Equals(_keys[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        public void Remove(Image image)
        {
            int index = IndexOf(image);
            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _images.Count)
            {
                return;
            }

            _images.RemoveAt(index);
            _keys.RemoveAt(index);
        }

        public void RemoveByKey(string key)
        {
            int index = IndexOfKey(key);
            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        public void SetKeyName(int index, string name)
        {
            if (index < 0 || index >= _keys.Count)
            {
                return;
            }

            _keys[index] = name ?? string.Empty;
        }
    }
}

public sealed class ImageListStreamer
{
}

public class NativeWindow
{
    public IntPtr Handle { get; private set; }

    public void AssignHandle(IntPtr handle)
    {
        Handle = handle;
    }

    public void ReleaseHandle()
    {
        Handle = IntPtr.Zero;
    }

    protected virtual void WndProc(ref Message m)
    {
    }

    protected virtual void OnThreadException(Exception e)
    {
    }
}

public interface IDataObject
{
    object? GetData(string format);

    object? GetData(Type format);

    object? GetData(string format, bool autoConvert);

    bool GetDataPresent(string format);

    bool GetDataPresent(Type format);

    bool GetDataPresent(string format, bool autoConvert);
}

public class DataObject : IDataObject
{
    private readonly Dictionary<string, object?> _data = new(StringComparer.Ordinal);

    public DataObject()
    {
    }

    public DataObject(object data)
    {
        SetData(data);
    }

    public DataObject(string format, object? data)
    {
        SetData(format, data);
    }

    public object? GetData(string format)
    {
        return _data.TryGetValue(format, out var value) ? value : null;
    }

    public object? GetData(Type format)
    {
        return GetData(format.FullName ?? format.Name);
    }

    public object? GetData(string format, bool autoConvert)
    {
        return GetData(format);
    }

    public bool GetDataPresent(string format)
    {
        return _data.ContainsKey(format);
    }

    public bool GetDataPresent(Type format)
    {
        return GetDataPresent(format.FullName ?? format.Name);
    }

    public bool GetDataPresent(string format, bool autoConvert)
    {
        return GetDataPresent(format);
    }

    public void SetData(string format, object? data)
    {
        _data[format] = data;
    }

    public void SetData(Type format, object? data)
    {
        SetData(format.FullName ?? format.Name, data);
    }

    public void SetData(object? data)
    {
        if (data != null)
        {
            if (data is string text)
            {
                SetData(DataFormats.Text, text);
                SetData(DataFormats.UnicodeText, text);
                SetData(DataFormats.StringFormat, text);
            }

            SetData(data.GetType(), data);
        }
    }
}

public static class DataFormats
{
    public const string FileDrop = "FileDrop";
    public const string StringFormat = "String";
    public const string Text = "Text";
    public const string UnicodeText = "UnicodeText";
}

public static class Clipboard
{
    public static void Clear()
    {
        PortableWinFormsClipboardService.Clear();
    }

    public static void SetDataObject(object data)
    {
        PortableWinFormsClipboardService.SetDataObject(data);
    }

    public static IDataObject? GetDataObject()
    {
        return PortableWinFormsClipboardService.GetDataObject();
    }

    public static void SetText(string text)
    {
        PortableWinFormsClipboardService.SetText(text);
    }

    public static string GetText()
    {
        return PortableWinFormsClipboardService.GetText();
    }

    public static bool ContainsText()
    {
        return PortableWinFormsClipboardService.ContainsText();
    }
}

public static class Cursors
{
    public static Cursor Default { get; } = new Cursor();
    public static Cursor WaitCursor { get; } = new Cursor();
}

public static class Application
{
    private static readonly List<IMessageFilter> s_messageFilters = new();

    public static string StartupPath => AppContext.BaseDirectory;

    public static event EventHandler? Idle;
    public static event ThreadExceptionEventHandler? ThreadException;

    public static void OnThreadException(Exception e)
    {
        ThreadException?.Invoke(typeof(Application), new ThreadExceptionEventArgs(e));
    }

    public static ApartmentState OleRequired()
    {
        return ApartmentState.STA;
    }

    public static void SetCompatibleTextRenderingDefault(bool defaultValue)
    {
    }

    public static void AddMessageFilter(IMessageFilter value)
    {
        if (!s_messageFilters.Contains(value))
        {
            s_messageFilters.Add(value);
        }
    }

    public static void RemoveMessageFilter(IMessageFilter value)
    {
        s_messageFilters.Remove(value);
    }

    public static bool FilterMessage(ref Message message)
    {
        foreach (IMessageFilter filter in s_messageFilters.ToArray())
        {
            if (filter.PreFilterMessage(ref message))
            {
                return true;
            }
        }

        return false;
    }

    public static void RaiseIdle(EventArgs e)
    {
        Idle?.Invoke(typeof(Application), e);
    }

    public static void Run()
    {
    }

    public static void Run(Form mainForm)
    {
        mainForm.Show();
    }

    public static void ExitThread()
    {
    }
}

public static class MessageBox
{
    public static DialogResult Show(string text)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(string text, string caption)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options)
    {
        return DialogResult.OK;
    }

    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options)
    {
        return DialogResult.OK;
    }
}

public static class ControlPaint
{
    public static void DrawBorder3D(Graphics graphics, Rectangle rectangle, Border3DStyle style)
    {
    }

    public static Color Light(Color baseColor)
    {
        return Color.FromArgb(
            baseColor.A,
            Math.Min(255, baseColor.R + ((255 - baseColor.R) / 2)),
            Math.Min(255, baseColor.G + ((255 - baseColor.G) / 2)),
            Math.Min(255, baseColor.B + ((255 - baseColor.B) / 2)));
    }
}

public static class SystemInformation
{
    public static BootMode BootMode => BootMode.Normal;

    public static Size DragSize => new(4, 4);

    public static Font MenuFont => SystemFonts.MenuFont;

    public static int MouseWheelScrollLines => 3;

    public static bool TerminalServerSession => false;

    public static int VerticalScrollBarWidth => 17;
}

public delegate void ColumnClickEventHandler(object sender, ColumnClickEventArgs e);

public delegate void LabelEditEventHandler(object sender, LabelEditEventArgs e);

public delegate void DragEventHandler(object sender, DragEventArgs e);

public delegate void MouseEventHandler(object sender, MouseEventArgs e);

public delegate void PaintEventHandler(object sender, PaintEventArgs e);

public delegate void DrawItemEventHandler(object sender, DrawItemEventArgs e);

public delegate void MeasureItemEventHandler(object sender, MeasureItemEventArgs e);

public delegate void ItemCheckEventHandler(object sender, ItemCheckEventArgs e);

public delegate void ScrollEventHandler(object sender, ScrollEventArgs e);

public delegate void KeyPressEventHandler(object sender, KeyPressEventArgs e);

public delegate void KeyEventHandler(object sender, KeyEventArgs e);

public delegate void PreviewKeyDownEventHandler(object sender, PreviewKeyDownEventArgs e);

public delegate void FormClosingEventHandler(object sender, FormClosingEventArgs e);

public delegate void FormClosedEventHandler(object sender, FormClosedEventArgs e);

public delegate void WebBrowserNavigatedEventHandler(object sender, WebBrowserNavigatedEventArgs e);

public delegate void WebBrowserDocumentCompletedEventHandler(object sender, WebBrowserDocumentCompletedEventArgs e);

public delegate void WebBrowserNavigatingEventHandler(object sender, WebBrowserNavigatingEventArgs e);

public delegate void PropertyValueChangedEventHandler(object sender, PropertyValueChangedEventArgs e);

public delegate void SelectedGridItemChangedEventHandler(object sender, SelectedGridItemChangedEventArgs e);

public delegate void LinkLabelLinkClickedEventHandler(object sender, LinkLabelLinkClickedEventArgs e);

public delegate void DataGridViewCellEventHandler(object sender, DataGridViewCellEventArgs e);

public delegate void DataGridViewDataErrorEventHandler(object sender, DataGridViewDataErrorEventArgs e);

public interface IMessageFilter
{
    bool PreFilterMessage(ref Message m);
}

public delegate void DataGridViewEditingControlShowingEventHandler(object sender, DataGridViewEditingControlShowingEventArgs e);

public delegate void DataGridViewRowsAddedEventHandler(object sender, DataGridViewRowsAddedEventArgs e);

public delegate void DataGridViewRowsRemovedEventHandler(object sender, DataGridViewRowsRemovedEventArgs e);

public delegate void TreeViewCancelEventHandler(object sender, TreeViewCancelEventArgs e);

public delegate void TreeViewEventHandler(object sender, TreeViewEventArgs e);

public delegate void NodeLabelEditEventHandler(object sender, NodeLabelEditEventArgs e);

public delegate void DrawTreeNodeEventHandler(object sender, DrawTreeNodeEventArgs e);

public delegate void ItemDragEventHandler(object sender, ItemDragEventArgs e);

public enum ImeMode
{
    NoControl = 0,
    On = 1,
    Off = 2,
    Disable = 3
}

public enum DialogResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Abort = 3,
    Retry = 4,
    Ignore = 5,
    Yes = 6,
    No = 7
}

public enum MessageBoxButtons
{
    OK = 0,
    OKCancel = 1,
    AbortRetryIgnore = 2,
    YesNoCancel = 3,
    YesNo = 4,
    RetryCancel = 5
}

public enum MessageBoxIcon
{
    None = 0,
    Hand = 16,
    Question = 32,
    Exclamation = 48,
    Asterisk = 64,
    Error = Hand,
    Warning = Exclamation,
    Information = Asterisk
}

public enum MessageBoxDefaultButton
{
    Button1 = 0,
    Button2 = 256,
    Button3 = 512
}

[Flags]
public enum MessageBoxOptions
{
    None = 0,
    DefaultDesktopOnly = 0x20000,
    RightAlign = 0x80000,
    RtlReading = 0x100000,
    ServiceNotification = 0x200000
}

[Flags]
public enum Keys
{
    None = 0,
    Return = 13,
    Enter = Return,
    Tab = 9,
    Escape = 27,
    Space = 32,
    PageUp = 33,
    PageDown = 34,
    End = 35,
    Home = 36,
    Left = 37,
    Right = 39,
    Back = 8,
    Backspace = Back,
    Delete = 46,
    D0 = 48,
    D1 = 49,
    D2 = 50,
    D3 = 51,
    D4 = 52,
    D5 = 53,
    D6 = 54,
    D7 = 55,
    D8 = 56,
    D9 = 57,
    A = 65,
    B = 66,
    C = 67,
    D = 68,
    E = 69,
    F = 70,
    G = 71,
    H = 72,
    I = 73,
    J = 74,
    K = 75,
    L = 76,
    M = 77,
    N = 78,
    O = 79,
    P = 80,
    Q = 81,
    R = 82,
    S = 83,
    T = 84,
    U = 85,
    V = 86,
    W = 87,
    X = 88,
    Y = 89,
    Z = 90,
    F2 = 113,
    Up = 38,
    Down = 40,
    Shift = 0x10000,
    Control = 0x20000
}

public struct Message
{
    public IntPtr HWnd;
    public int Msg;
    public IntPtr WParam;
    public IntPtr LParam;
    public IntPtr Result;
}

public enum Border3DStyle
{
    RaisedInner = 4,
    Sunken = 2
}

public enum BorderStyle
{
    None = 0,
    FixedSingle = 1,
    Fixed3D = 2
}

public enum ErrorBlinkStyle
{
    BlinkIfDifferentError = 0,
    AlwaysBlink = 1,
    NeverBlink = 2
}

public enum ErrorIconAlignment
{
    TopLeft = 0,
    TopRight = 1,
    MiddleLeft = 2,
    MiddleRight = 3,
    BottomLeft = 4,
    BottomRight = 5
}

public class AmbientProperties
{
    public Color BackColor { get; set; } = SystemColors.Control;
    public Cursor? Cursor { get; set; }
    public Font Font { get; set; } = SystemFonts.DefaultFont;
    public Color ForeColor { get; set; } = SystemColors.ControlText;
}

public sealed class ControlBindingsCollection : Collection<Binding>
{
    public ControlBindingsCollection(Control control)
    {
        Control = control;
    }

    public Control Control { get; }

    public Binding Add(string propertyName, object dataSource, string dataMember)
    {
        var binding = new Binding(propertyName, dataSource, dataMember);
        Add(binding);
        return binding;
    }
}

public sealed class Binding
{
    public Binding(string propertyName, object dataSource, string dataMember)
    {
        PropertyName = propertyName;
        DataSource = dataSource;
        DataMember = dataMember;
    }

    public object DataSource { get; }

    public string DataMember { get; }

    public string PropertyName { get; }
}

[Flags]
public enum AnchorStyles
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8
}

public enum Appearance
{
    Normal = 0,
    Button = 1
}

public enum AutoCompleteMode
{
    None = 0,
    Suggest = 1,
    Append = 2,
    SuggestAppend = 3
}

public enum AutoCompleteSource
{
    None = 0,
    HistoryList = 1,
    ListItems = 256,
    AllUrl = 6
}

public enum ToolStripItemDisplayStyle
{
    None = 0,
    Text = 1,
    Image = 2,
    ImageAndText = 3
}

public enum ToolStripItemImageScaling
{
    None = 0,
    SizeToFit = 1
}

public enum ToolStripGripStyle
{
    Hidden = 0,
    Visible = 1
}

public enum WebBrowserRefreshOption
{
    Normal = 0,
    IfExpired = 1,
    Completely = 3
}

public enum View
{
    LargeIcon = 0,
    Details = 1,
    SmallIcon = 2,
    List = 3,
    Tile = 4
}

public enum SortOrder
{
    None = 0,
    Ascending = 1,
    Descending = 2
}

public enum ProgressBarStyle
{
    Blocks = 0,
    Continuous = 1,
    Marquee = 2
}

public enum ScrollBars
{
    None = 0,
    Horizontal = 1,
    Vertical = 2,
    Both = 3
}

public enum RightToLeft
{
    No = 0,
    Yes = 1,
    Inherit = 2
}

public enum BootMode
{
    Normal = 0,
    FailSafe = 1,
    FailSafeWithNetwork = 2
}

public enum PictureBoxSizeMode
{
    Normal = 0,
    StretchImage = 1,
    AutoSize = 2,
    CenterImage = 3,
    Zoom = 4
}

public enum AutoScaleMode
{
    None = 0,
    Font = 1,
    Dpi = 2,
    Inherit = 3
}

public enum CheckState
{
    Unchecked = 0,
    Checked = 1,
    Indeterminate = 2
}

public enum SelectionMode
{
    None = 0,
    One = 1,
    MultiSimple = 2,
    MultiExtended = 3
}

public enum ComboBoxStyle
{
    DropDown = 1,
    Simple = 0,
    DropDownList = 2
}

public enum DrawMode
{
    Normal = 0,
    OwnerDrawFixed = 1,
    OwnerDrawVariable = 2
}

[Flags]
public enum DrawItemState
{
    None = 0,
    Selected = 1,
    Grayed = 2,
    Disabled = 4,
    Checked = 8,
    Focus = 16,
    Default = 32,
    HotLight = 64,
    Inactive = 128,
    NoAccelerator = 256,
    NoFocusRect = 512,
    ComboBoxEdit = 4096
}

[Flags]
public enum ControlStyles
{
    UserPaint = 0x2,
    AllPaintingInWmPaint = 0x2000,
    OptimizedDoubleBuffer = 0x20000,
    CacheText = 0x4000
}

public enum DockStyle
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 3,
    Right = 4,
    Fill = 5
}

public enum FlatStyle
{
    Flat = 0,
    Popup = 1,
    Standard = 2,
    System = 3
}

public enum FormBorderStyle
{
    None = 0,
    FixedSingle = 1,
    Fixed3D = 2,
    FixedDialog = 3,
    Sizable = 4,
    FixedToolWindow = 5,
    SizableToolWindow = 6
}

public enum FormStartPosition
{
    Manual = 0,
    CenterScreen = 1,
    WindowsDefaultLocation = 2,
    WindowsDefaultBounds = 3,
    CenterParent = 4
}

public enum FormWindowState
{
    Normal = 0,
    Minimized = 1,
    Maximized = 2
}

public enum HorizontalAlignment
{
    Left = 0,
    Right = 1,
    Center = 2
}

public enum ColumnHeaderStyle
{
    None = 0,
    Nonclickable = 1,
    Clickable = 2
}

public enum ListViewAlignment
{
    Default = 0,
    Left = 1,
    Top = 2,
    SnapToGrid = 5
}

public enum PropertySort
{
    NoSort = 0,
    Alphabetical = 1,
    Categorized = 2,
    CategorizedAlphabetical = 3
}

public enum DataGridViewAutoSizeColumnMode
{
    NotSet = 0,
    None = 1,
    ColumnHeader = 2,
    AllCellsExceptHeader = 4,
    AllCells = 6,
    DisplayedCellsExceptHeader = 8,
    DisplayedCells = 10,
    Fill = 16
}

public enum DataGridViewColumnHeadersHeightSizeMode
{
    EnableResizing = 0,
    DisableResizing = 1,
    AutoSize = 2
}

public enum DataGridViewRowHeadersWidthSizeMode
{
    EnableResizing = 0,
    DisableResizing = 1,
    AutoSizeToAllHeaders = 2,
    AutoSizeToDisplayedHeaders = 3,
    AutoSizeToFirstHeader = 4
}

public enum DataGridViewEditMode
{
    EditOnEnter = 0,
    EditOnKeystroke = 1,
    EditOnKeystrokeOrF2 = 2,
    EditOnF2 = 3,
    EditProgrammatically = 4
}

public enum CloseReason
{
    None = 0,
    WindowsShutDown = 1,
    MdiFormClosing = 2,
    UserClosing = 3,
    TaskManagerClosing = 4,
    FormOwnerClosing = 5,
    ApplicationExitCall = 6
}

public enum Orientation
{
    Horizontal = 0,
    Vertical = 1
}

[Flags]
public enum DragDropEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
    Scroll = unchecked((int)0x80000000),
    All = Copy | Move | Link | Scroll
}

public enum MouseButtons
{
    None = 0,
    Left = 0x100000,
    Right = 0x200000,
    Middle = 0x400000
}

public enum ScrollEventType
{
    SmallDecrement,
    SmallIncrement,
    LargeDecrement,
    LargeIncrement,
    ThumbPosition,
    ThumbTrack,
    First,
    Last,
    EndScroll
}

public enum ColorDepth
{
    Depth8Bit = 8,
    Depth16Bit = 16,
    Depth24Bit = 24,
    Depth32Bit = 32
}

public enum TreeViewAction
{
    Unknown = 0,
    ByKeyboard = 1,
    ByMouse = 2,
    Collapse = 3,
    Expand = 4
}

public enum TreeViewDrawMode
{
    Normal = 0,
    OwnerDrawText = 1,
    OwnerDrawAll = 2
}

[Flags]
public enum TreeNodeStates
{
    Default = 0,
    Checked = 1,
    Selected = 2,
    Grayed = 4,
    Hot = 8,
    Focused = 16
}

public sealed class ColumnClickEventArgs : EventArgs
{
    public ColumnClickEventArgs(int column)
    {
        Column = column;
    }

    public int Column { get; }
}

public sealed class ItemCheckEventArgs : EventArgs
{
    public ItemCheckEventArgs(int index, CheckState newValue, CheckState currentValue)
    {
        Index = index;
        NewValue = newValue;
        CurrentValue = currentValue;
    }

    public int Index { get; }

    public CheckState NewValue { get; set; }

    public CheckState CurrentValue { get; }
}

public class LabelEditEventArgs : EventArgs
{
    public LabelEditEventArgs(int item, string? label)
    {
        Item = item;
        Label = label;
    }

    public bool CancelEdit { get; set; }

    public int Item { get; }

    public string? Label { get; }
}

public class DragEventArgs : EventArgs
{
    public DragEventArgs(IDataObject data, int keyState, int x, int y, DragDropEffects allowedEffect, DragDropEffects effect)
    {
        Data = data;
        KeyState = keyState;
        X = x;
        Y = y;
        AllowedEffect = allowedEffect;
        Effect = effect;
    }

    public DragDropEffects AllowedEffect { get; }

    public IDataObject Data { get; }

    public DragDropEffects Effect { get; set; }

    public int KeyState { get; }

    public int X { get; }

    public int Y { get; }
}

public class MouseEventArgs : EventArgs
{
    public MouseEventArgs(MouseButtons button, int clicks, int x, int y, int delta)
    {
        Button = button;
        Clicks = clicks;
        X = x;
        Y = y;
        Delta = delta;
    }

    public MouseButtons Button { get; }

    public int Clicks { get; }

    public int Delta { get; }

    public Point Location => new(X, Y);

    public int X { get; }

    public int Y { get; }
}

public class KeyPressEventArgs : EventArgs
{
    public KeyPressEventArgs(char keyChar)
    {
        KeyChar = keyChar;
    }

    public bool Handled { get; set; }

    public char KeyChar { get; set; }
}

public class KeyEventArgs : EventArgs
{
    public KeyEventArgs(Keys keyData)
    {
        KeyData = keyData;
    }

    public bool Handled { get; set; }

    public bool SuppressKeyPress { get; set; }

    public Keys KeyData { get; }

    public Keys KeyCode => KeyData & ~(Keys.Control | Keys.Shift);

    public Keys Modifiers => KeyData & (Keys.Control | Keys.Shift);
}

public class FormClosingEventArgs : CancelEventArgs
{
    public FormClosingEventArgs(CloseReason closeReason, bool cancel)
        : base(cancel)
    {
        CloseReason = closeReason;
    }

    public CloseReason CloseReason { get; }
}

public class FormClosedEventArgs : EventArgs
{
    public FormClosedEventArgs(CloseReason closeReason)
    {
        CloseReason = closeReason;
    }

    public CloseReason CloseReason { get; }
}

public class WebBrowserNavigatedEventArgs : EventArgs
{
    public WebBrowserNavigatedEventArgs(Uri url)
    {
        Url = url;
    }

    public Uri Url { get; }
}

public class WebBrowserDocumentCompletedEventArgs : EventArgs
{
    public WebBrowserDocumentCompletedEventArgs(Uri url)
    {
        Url = url;
    }

    public Uri Url { get; }
}

public class WebBrowserNavigatingEventArgs : CancelEventArgs
{
    public WebBrowserNavigatingEventArgs(Uri url)
    {
        Url = url;
    }

    public Uri Url { get; }

    public string TargetFrameName { get; set; } = string.Empty;
}

public class PropertyValueChangedEventArgs : EventArgs
{
    public PropertyValueChangedEventArgs(GridItem? changedItem, object? oldValue)
    {
        ChangedItem = changedItem;
        OldValue = oldValue;
    }

    public GridItem? ChangedItem { get; }

    public object? OldValue { get; }
}

public class SelectedGridItemChangedEventArgs : EventArgs
{
    public SelectedGridItemChangedEventArgs(GridItem? oldSelection, GridItem? newSelection)
    {
        OldSelection = oldSelection;
        NewSelection = newSelection;
    }

    public GridItem? NewSelection { get; }

    public GridItem? OldSelection { get; }
}

public class DataGridViewCellEventArgs : EventArgs
{
    public DataGridViewCellEventArgs(int columnIndex, int rowIndex)
    {
        ColumnIndex = columnIndex;
        RowIndex = rowIndex;
    }

    public int ColumnIndex { get; }

    public int RowIndex { get; }
}

public class DataGridViewDataErrorEventArgs : DataGridViewCellEventArgs
{
    public DataGridViewDataErrorEventArgs(Exception? exception, int columnIndex, int rowIndex)
        : base(columnIndex, rowIndex)
    {
        Exception = exception;
    }

    public Exception? Exception { get; }

    public bool ThrowException { get; set; }
}

public class DataGridViewEditingControlShowingEventArgs : EventArgs
{
    public DataGridViewEditingControlShowingEventArgs(Control control)
    {
        Control = control;
    }

    public Control Control { get; }
}

public class DataGridViewRowsAddedEventArgs : EventArgs
{
    public DataGridViewRowsAddedEventArgs(int rowIndex, int rowCount)
    {
        RowIndex = rowIndex;
        RowCount = rowCount;
    }

    public int RowIndex { get; }

    public int RowCount { get; }
}

public class DataGridViewRowsRemovedEventArgs : EventArgs
{
    public DataGridViewRowsRemovedEventArgs(int rowIndex, int rowCount)
    {
        RowIndex = rowIndex;
        RowCount = rowCount;
    }

    public int RowIndex { get; }

    public int RowCount { get; }
}

public class LinkLabelLinkClickedEventArgs : EventArgs
{
    public LinkLabelLinkClickedEventArgs(object? link)
    {
        Link = link;
    }

    public object? Link { get; }
}

public class GridItem
{
    public string Label { get; set; } = string.Empty;

    public object? Value { get; set; }

    public PropertyDescriptor? PropertyDescriptor { get; set; }
}

public class PaintEventArgs : EventArgs
{
    public PaintEventArgs(Graphics graphics, Rectangle clipRectangle)
    {
        Graphics = graphics;
        ClipRectangle = clipRectangle;
    }

    public Rectangle ClipRectangle { get; }

    public Graphics Graphics { get; }
}

public class DrawItemEventArgs : EventArgs
{
    public DrawItemEventArgs(Graphics graphics, Font font, Rectangle bounds, int index, DrawItemState state)
    {
        Graphics = graphics;
        Font = font;
        Bounds = bounds;
        Index = index;
        State = state;
    }

    public Rectangle Bounds { get; }

    public Font Font { get; }

    public Graphics Graphics { get; }

    public int Index { get; }

    public DrawItemState State { get; }

    public void DrawBackground()
    {
        Brush background = (State & DrawItemState.Selected) != 0
            ? SystemBrushes.Highlight
            : (State & DrawItemState.Disabled) != 0
                ? SystemBrushes.Control
                : SystemBrushes.Window;

        Graphics.FillRectangle(background, Bounds);
    }

    public void DrawFocusRectangle()
    {
        if ((State & DrawItemState.Focus) == 0 || (State & DrawItemState.NoFocusRect) != 0)
        {
            return;
        }

        Rectangle focusBounds = Bounds;
        if (focusBounds.Width <= 1 || focusBounds.Height <= 1)
        {
            return;
        }

        focusBounds.Inflate(-1, -1);
        Graphics.DrawRectangle(SystemPens.WindowText, focusBounds);
    }
}

public readonly struct Padding
{
    public static Padding Empty { get; } = new Padding(0);

    public Padding(int all)
    {
        Left = all;
        Top = all;
        Right = all;
        Bottom = all;
    }

    public Padding(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Left { get; }

    public int Top { get; }

    public int Right { get; }

    public int Bottom { get; }
}

public sealed class DockPaddingEdges
{
    public int All
    {
        set
        {
            Left = value;
            Top = value;
            Right = value;
            Bottom = value;
        }
    }

    public int Left { get; set; }

    public int Top { get; set; }

    public int Right { get; set; }

    public int Bottom { get; set; }
}

public class MeasureItemEventArgs : EventArgs
{
    public MeasureItemEventArgs(Graphics graphics, int index)
    {
        Graphics = graphics;
        Index = index;
    }

    public Graphics Graphics { get; }

    public int Index { get; }

    public int ItemHeight { get; set; }

    public int ItemWidth { get; set; }
}

public class TreeViewCancelEventArgs : CancelEventArgs
{
    public TreeViewCancelEventArgs(TreeNode? node, bool cancel, TreeViewAction action)
        : base(cancel)
    {
        Node = node;
        Action = action;
    }

    public TreeViewAction Action { get; }

    public TreeNode? Node { get; }
}

public class TreeViewEventArgs : EventArgs
{
    public TreeViewEventArgs(TreeNode? node)
        : this(node, TreeViewAction.Unknown)
    {
    }

    public TreeViewEventArgs(TreeNode? node, TreeViewAction action)
    {
        Node = node;
        Action = action;
    }

    public TreeViewAction Action { get; }

    public TreeNode? Node { get; }
}

public class NodeLabelEditEventArgs : EventArgs
{
    public NodeLabelEditEventArgs(TreeNode? node, string? label)
    {
        Node = node;
        Label = label;
    }

    public bool CancelEdit { get; set; }

    public string? Label { get; }

    public TreeNode? Node { get; }
}

public class DrawTreeNodeEventArgs : EventArgs
{
    public DrawTreeNodeEventArgs(Graphics graphics, TreeNode? node, Rectangle bounds)
    {
        Graphics = graphics;
        Node = node;
        Bounds = bounds;
    }

    public Rectangle Bounds { get; }

    public Graphics Graphics { get; }

    public TreeNode? Node { get; }

    public bool DrawDefault { get; set; }

    public TreeNodeStates State { get; set; }
}

public class ItemDragEventArgs : EventArgs
{
    public ItemDragEventArgs(MouseButtons button, object? item)
    {
        Button = button;
        Item = item;
    }

    public MouseButtons Button { get; }

    public object? Item { get; }
}

public class PreviewKeyDownEventArgs : EventArgs
{
    public PreviewKeyDownEventArgs(Keys keyData)
    {
        KeyData = keyData;
    }

    public Keys KeyData { get; }

    public Keys KeyCode => KeyData & ~(Keys.Control | Keys.Shift);

    public Keys Modifiers => KeyData & (Keys.Control | Keys.Shift);
}

public class ScrollEventArgs : EventArgs
{
    public ScrollEventArgs(ScrollEventType type, int oldValue, int newValue)
    {
        Type = type;
        OldValue = oldValue;
        NewValue = newValue;
    }

    public int NewValue { get; }

    public int OldValue { get; }

    public ScrollEventType Type { get; }
}

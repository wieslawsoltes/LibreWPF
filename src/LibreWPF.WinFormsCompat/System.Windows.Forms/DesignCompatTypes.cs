using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Reflection;

namespace System.Drawing.Design
{
    public sealed class CategoryNameCollection : ReadOnlyCollection<string>
    {
        public CategoryNameCollection(string[] value)
            : base(value)
        {
        }
    }

    public interface IPropertyValueUIService
    {
        event EventHandler? PropertyUIValueItemsChanged;

        void AddPropertyValueUIHandler(PropertyValueUIHandler newHandler);

        PropertyValueUIItem[] GetPropertyUIValueItems(ITypeDescriptorContext context, PropertyDescriptor propDesc);

        void NotifyPropertyValueUIItemsChanged();

        void RemovePropertyValueUIHandler(PropertyValueUIHandler newHandler);
    }

    public interface IToolboxService
    {
        CategoryNameCollection CategoryNames { get; }

        string? SelectedCategory { get; set; }

        void AddCreator(ToolboxItemCreatorCallback creator, string format);

        void AddCreator(ToolboxItemCreatorCallback creator, string format, IDesignerHost host);

        void AddLinkedToolboxItem(ToolboxItem toolboxItem, string category, IDesignerHost host);

        void AddLinkedToolboxItem(ToolboxItem toolboxItem, IDesignerHost host);

        void AddToolboxItem(ToolboxItem toolboxItem);

        void AddToolboxItem(ToolboxItem toolboxItem, string category);

        ToolboxItem DeserializeToolboxItem(object serializedObject);

        ToolboxItem DeserializeToolboxItem(object serializedObject, IDesignerHost host);

        ToolboxItem? GetSelectedToolboxItem();

        ToolboxItem? GetSelectedToolboxItem(IDesignerHost host);

        ToolboxItemCollection GetToolboxItems();

        ToolboxItemCollection GetToolboxItems(string category);

        ToolboxItemCollection GetToolboxItems(string category, IDesignerHost host);

        ToolboxItemCollection GetToolboxItems(IDesignerHost host);

        bool IsSupported(object serializedObject, IDesignerHost host);

        bool IsToolboxItem(object serializedObject);

        bool IsToolboxItem(object serializedObject, IDesignerHost host);

        void Refresh();

        void RemoveCreator(string format);

        void RemoveCreator(string format, IDesignerHost host);

        void RemoveToolboxItem(ToolboxItem toolboxItem);

        void RemoveToolboxItem(ToolboxItem toolboxItem, string category);

        void SelectedToolboxItemUsed();

        object SerializeToolboxItem(ToolboxItem toolboxItem);

        bool SetCursor();

        void SetSelectedToolboxItem(ToolboxItem toolboxItem);

        event EventHandler? SelectedCategoryChanged;

        event EventHandler? SelectedCategoryChanging;
    }

    public sealed class PaintValueEventArgs : EventArgs
    {
        public PaintValueEventArgs(ITypeDescriptorContext? context, object? value, Graphics graphics, Rectangle bounds)
        {
            Context = context;
            Value = value;
            Graphics = graphics;
            Bounds = bounds;
        }

        public Rectangle Bounds { get; }

        public ITypeDescriptorContext? Context { get; }

        public Graphics Graphics { get; }

        public object? Value { get; }
    }

    public delegate void PropertyValueUIHandler(ITypeDescriptorContext context, PropertyDescriptor propDesc, ArrayList valueUIItemList);

    public delegate void PropertyValueUIItemInvokeHandler(ITypeDescriptorContext context, PropertyDescriptor descriptor, PropertyValueUIItem invokedItem);

    public sealed class PropertyValueUIItem
    {
        public PropertyValueUIItem(Image image, PropertyValueUIItemInvokeHandler handler, string tooltip)
        {
            Image = image;
            InvokeHandler = handler;
            ToolTip = tooltip;
        }

        public Image Image { get; }

        public PropertyValueUIItemInvokeHandler InvokeHandler { get; }

        public string ToolTip { get; }

        public void Invoke(ITypeDescriptorContext context, PropertyDescriptor descriptor)
        {
            InvokeHandler(context, descriptor, this);
        }
    }

    public class ToolboxItem
    {
        public ToolboxItem()
        {
        }

        public ToolboxItem(Type toolType)
        {
            TypeName = toolType.AssemblyQualifiedName ?? toolType.FullName ?? toolType.Name;
            DisplayName = toolType.Name;
        }

        public AssemblyName? AssemblyName { get; set; }

        public Bitmap Bitmap { get; set; } = new(1, 1);

        public string Company { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public ICollection Filter { get; set; } = Array.Empty<object>();

        public bool IsTransient { get; set; }

        public IDictionary Properties { get; } = new Hashtable();

        public string? TypeName { get; set; }

        public virtual void Initialize(Type? type)
        {
            if (type is null)
            {
                return;
            }

            TypeName = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
            AssemblyName = type.Assembly.GetName();
            DisplayName = type.Name;
        }

        public IComponent[] CreateComponents()
        {
            return CreateComponentsCore(null!);
        }

        public IComponent[] CreateComponents(IDesignerHost host)
        {
            return CreateComponentsCore(host);
        }

        public IComponent[] CreateComponents(IDesignerHost host, IDictionary defaultValues)
        {
            return CreateComponentsCore(host, defaultValues);
        }

        protected virtual IComponent[] CreateComponentsCore(IDesignerHost host)
        {
            return Array.Empty<IComponent>();
        }

        protected virtual IComponent[] CreateComponentsCore(IDesignerHost host, IDictionary defaultValues)
        {
            return CreateComponentsCore(host);
        }
    }

    public sealed class ToolboxItemCollection : ReadOnlyCollection<ToolboxItem>
    {
        public ToolboxItemCollection(ToolboxItem[] value)
            : base(value)
        {
        }
    }

    public delegate ToolboxItem? ToolboxItemCreatorCallback(object serializedObject, string format);

    public enum UITypeEditorEditStyle
    {
        None = 1,
        Modal = 2,
        DropDown = 3
    }

    public class UITypeEditor
    {
        public virtual bool IsDropDownResizable => false;

        protected static string CreateFilterEntry(UITypeEditor editor)
        {
            return editor switch
            {
                IconEditor => "Icon files (*.ico)|*.ico|All files (*.*)|*.*",
                ImageEditor => "Image files (*.bmp;*.gif;*.jpg;*.jpeg;*.png)|*.bmp;*.gif;*.jpg;*.jpeg;*.png|All files (*.*)|*.*",
                _ => "All files (*.*)|*.*"
            };
        }

        public virtual object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
        {
            return value;
        }

        public virtual UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context)
        {
            return UITypeEditorEditStyle.None;
        }

        public virtual bool GetPaintValueSupported(ITypeDescriptorContext? context)
        {
            return false;
        }

        public virtual void PaintValue(PaintValueEventArgs e)
        {
        }
    }

    public class ImageEditor : UITypeEditor
    {
    }

    public class IconEditor : UITypeEditor
    {
    }
}

namespace System.Windows.Forms.Design
{
    public interface IUIService
    {
        IDictionary Styles { get; }

        bool CanShowComponentEditor(object component);

        IWin32Window? GetDialogOwnerWindow();

        void SetUIDirty();

        bool ShowComponentEditor(object component, IWin32Window parent);

        DialogResult ShowDialog(Form form);

        void ShowError(Exception ex);

        void ShowError(Exception ex, string message);

        void ShowError(string message);

        void ShowMessage(string message);

        void ShowMessage(string message, string caption);

        DialogResult ShowMessage(string message, string caption, MessageBoxButtons buttons);

        bool ShowToolWindow(Guid toolWindow);
    }

    public interface IWindowsFormsEditorService
    {
        void CloseDropDown();

        void DropDownControl(Control control);

        DialogResult ShowDialog(Form dialog);
    }

    public class DesignerOptions
    {
        public virtual bool EnableInSituEditing { get; }

        public virtual Size GridSize { get; }

        public virtual bool ObjectBoundSmartTagAutoShow { get; }

        public virtual bool ShowGrid { get; }

        public virtual bool SnapToGrid { get; }

        public virtual bool UseOptimizedCodeGeneration { get; }

        public virtual bool UseSmartTags { get; }

        public virtual bool UseSnapLines { get; }
    }

    public class WindowsFormsDesignerOptionService : DesignerOptionService
    {
        private bool _optionsPopulated;

        protected override void PopulateOptionCollection(DesignerOptionCollection options)
        {
            if (_optionsPopulated)
            {
                return;
            }

            _optionsPopulated = true;
            CreateOptionCollection(options, "WindowsFormsDesigner", this);
        }

        public Size GridSize { get; set; }

        public bool EnableInSituEditing { get; set; }

        public bool ObjectBoundSmartTagAutoShow { get; set; }

        public bool UseOptimizedCodeGeneration { get; set; }

        public bool UseSmartTags { get; set; }

        public bool UseSnapLines { get; set; }

        public bool ShowGrid { get; set; }

        public bool SnapToGrid { get; set; }
    }
}

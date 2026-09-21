using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Globalization;
using System.Resources;
using System.Windows.Forms;

namespace System.ComponentModel.Design.Serialization
{
    public enum CodeDomLocalizationModel
    {
        None = 0,
        PropertyAssignment = 1,
        PropertyReflection = 2
    }

    public class CodeDomLocalizationProvider : IDesignerSerializationProvider
    {
        public CodeDomLocalizationProvider(IDesignerLoaderHost host, CodeDomLocalizationModel model)
        {
            Host = host;
            Model = model;
        }

        public IDesignerLoaderHost Host { get; }

        public CodeDomLocalizationModel Model { get; }

        public object? GetSerializer(IDesignerSerializationManager manager, object? currentSerializer, Type objectType, Type serializerType)
        {
            return null;
        }
    }

    public abstract class CodeDomDesignerLoader : DesignerLoader
    {
        private bool _loading;
        private IDesignerLoaderHost? _host;
        private CodeStatement[] _preservedEventStatements = Array.Empty<CodeStatement>();

        public override bool Loading => base.Loading || _loading;

        protected abstract CodeDomProvider? CodeDomProvider { get; }

        protected abstract ITypeResolutionService? TypeResolutionService { get; }

        public override void BeginLoad(IDesignerLoaderHost host)
        {
            _loading = true;
            _host = host;
            try
            {
                Initialize();
                CodeCompileUnit unit = Parse();
                _preservedEventStatements = CaptureEventStatements(unit);
                var deserializer = new PortableCodeDomDesignSurfaceDeserializer(host, TypeResolutionService);
                object[] errors = deserializer.Load(unit);
                SeedEventBindingService(host, _preservedEventStatements);
                bool successful = errors.Length == 0;
                host.EndLoad(deserializer.RootComponentClassName, successful, errors);
                OnEndLoad(successful, errors);
            }
            catch (Exception ex)
            {
                object[] errors = { ex };
                host.EndLoad(typeof(Panel).FullName!, false, errors);
                OnEndLoad(false, errors);
            }
            finally
            {
                _loading = false;
            }
        }

        public override void Dispose()
        {
            _host = null;
        }

        public override void Flush()
        {
            if (_host is null)
                return;

            List<object> errors = new();
            try
            {
                if (GetService(typeof(IDesignerSerializationManager)) is not IDesignerSerializationManager manager)
                {
                    errors.Add(new InvalidOperationException("IDesignerSerializationManager is not available."));
                }
                else
                {
                    PerformFlush(manager);
                    var serializer = new PortableCodeDomDesignSurfaceSerializer(_host, manager);
                    CodeCompileUnit unit = serializer.Serialize();
                    if (GetService(typeof(IEventBindingService)) is not EventBindingService)
                        AppendEventStatements(unit, _preservedEventStatements);
                    Write(unit);
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }

            if (errors.Count > 0)
            {
                ReportFlushErrors(errors);
            }
        }

        protected object? GetService(Type serviceType)
        {
            return _host?.GetService(serviceType);
        }

        protected virtual void Initialize()
        {
        }

        protected virtual bool IsReloadNeeded()
        {
            return false;
        }

        protected virtual void OnComponentRename(object component, string? oldName, string? newName)
        {
        }

        protected virtual void OnEndLoad(bool successful, ICollection errors)
        {
        }

        protected virtual CodeCompileUnit Parse()
        {
            return new CodeCompileUnit();
        }

        protected virtual void PerformFlush(IDesignerSerializationManager manager)
        {
        }

        protected virtual void ReportFlushErrors(ICollection errors)
        {
        }

        protected abstract void Write(CodeCompileUnit unit);

        private static CodeStatement[] CaptureEventStatements(CodeCompileUnit unit)
        {
            CodeMemberMethod? initializeComponent = FindInitializeComponent(unit);
            if (initializeComponent is null)
                return Array.Empty<CodeStatement>();

            List<CodeStatement> statements = new();
            foreach (CodeStatement statement in initializeComponent.Statements)
            {
                if (statement is CodeAttachEventStatement or CodeRemoveEventStatement)
                    statements.Add(statement);
            }

            return statements.ToArray();
        }

        private static void SeedEventBindingService(IDesignerLoaderHost host, CodeStatement[] statements)
        {
            if (statements.Length == 0)
                return;
            if (host.GetService(typeof(IEventBindingService)) is not EventBindingService eventBindingService)
                return;

            foreach (CodeStatement statement in statements)
            {
                switch (statement)
                {
                    case CodeAttachEventStatement attach:
                        ApplyEventStatement(host, eventBindingService, attach.Event, attach.Listener, clear: false);
                        break;
                    case CodeRemoveEventStatement remove:
                        ApplyEventStatement(host, eventBindingService, remove.Event, remove.Listener, clear: true);
                        break;
                }
            }
        }

        private static void ApplyEventStatement(
            IDesignerLoaderHost host,
            EventBindingService eventBindingService,
            CodeEventReferenceExpression eventReference,
            CodeExpression listener,
            bool clear)
        {
            IComponent? target = ResolveEventTarget(host, eventReference);
            if (target is null)
                return;

            string? methodName = clear ? null : TryGetListenerMethodName(listener);
            if (!clear && string.IsNullOrWhiteSpace(methodName))
                return;

            eventBindingService.SetEventMethodName(target, eventReference.EventName, methodName);
        }

        private static IComponent? ResolveEventTarget(IDesignerLoaderHost host, CodeEventReferenceExpression eventReference)
        {
            if (eventReference.TargetObject is CodeThisReferenceExpression)
                return (host.GetService(typeof(IDesignerHost)) as IDesignerHost)?.RootComponent;

            string? targetName = TryGetNamedTarget(eventReference.TargetObject);
            if (string.IsNullOrWhiteSpace(targetName))
                return null;

            return (host.GetService(typeof(IDesignerSerializationManager)) as IDesignerSerializationManager)
                ?.GetInstance(targetName) as IComponent;
        }

        private static string? TryGetNamedTarget(CodeExpression expression)
        {
            return expression switch
            {
                CodeFieldReferenceExpression field
                    when field.TargetObject is CodeThisReferenceExpression => field.FieldName,
                CodeVariableReferenceExpression variable => variable.VariableName,
                _ => null
            };
        }

        private static string? TryGetListenerMethodName(CodeExpression listener)
        {
            return listener is CodeDelegateCreateExpression delegateCreate
                && delegateCreate.TargetObject is CodeThisReferenceExpression
                    ? delegateCreate.MethodName
                    : null;
        }

        private static void AppendEventStatements(CodeCompileUnit unit, CodeStatement[] statements)
        {
            if (statements.Length == 0)
                return;

            CodeMemberMethod? initializeComponent = FindInitializeComponent(unit);
            if (initializeComponent is null)
                return;

            for (int i = 0; i < statements.Length; i++)
            {
                initializeComponent.Statements.Add(statements[i]);
            }
        }

        private static CodeMemberMethod? FindInitializeComponent(CodeCompileUnit unit)
        {
            foreach (CodeNamespace codeNamespace in unit.Namespaces)
            {
                foreach (CodeTypeDeclaration codeClass in codeNamespace.Types)
                {
                    foreach (CodeTypeMember member in codeClass.Members)
                    {
                        if (member is CodeMemberMethod method
                            && string.Equals(method.Name, "InitializeComponent", StringComparison.Ordinal))
                        {
                            return method;
                        }
                    }
                }
            }

            return null;
        }
    }

    internal sealed class PortableCodeDomDesignSurfaceSerializer
    {
        private static readonly HashSet<string> s_skippedProperties = new(StringComparer.Ordinal)
        {
            nameof(Component.Site),
            nameof(Control.Controls),
            nameof(Control.Parent),
            nameof(Control.ContextMenuStrip)
        };

        private readonly IDesignerLoaderHost _host;
        private readonly IDesignerSerializationManager _serializationManager;
        private readonly Dictionary<IComponent, string> _names = new();
        private readonly CodeDomLocalizationModel _localizationModel;

        public PortableCodeDomDesignSurfaceSerializer(
            IDesignerLoaderHost host,
            IDesignerSerializationManager serializationManager)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _serializationManager = serializationManager ?? throw new ArgumentNullException(nameof(serializationManager));
            _localizationModel = serializationManager is PortableDesignerHost portableHost
                ? portableHost.GetLocalizationModel()
                : CodeDomLocalizationModel.None;
        }

        public CodeCompileUnit Serialize()
        {
            var designerHost = _host.GetService(typeof(IDesignerHost)) as IDesignerHost;
            IComponent rootComponent = designerHost?.RootComponent ?? new Panel();
            IContainer? container = designerHost?.Container;
            CaptureComponentNames(container, rootComponent);

            SplitQualifiedName(GetRootClassName(rootComponent), out string namespaceName, out string className);

            var unit = new CodeCompileUnit();
            var codeNamespace = new CodeNamespace(namespaceName);
            codeNamespace.Imports.Add(new CodeNamespaceImport("System"));
            codeNamespace.Imports.Add(new CodeNamespaceImport("System.Drawing"));
            codeNamespace.Imports.Add(new CodeNamespaceImport("System.Windows.Forms"));
            unit.Namespaces.Add(codeNamespace);

            var codeClass = new CodeTypeDeclaration(className)
            {
                IsClass = true,
                IsPartial = true
            };
            codeClass.BaseTypes.Add(new CodeTypeReference(rootComponent.GetType().FullName ?? rootComponent.GetType().Name));
            codeNamespace.Types.Add(codeClass);

            foreach (IComponent component in GetSerializableComponents(container, rootComponent))
            {
                codeClass.Members.Add(new CodeMemberField(
                    new CodeTypeReference(component.GetType().FullName ?? component.GetType().Name),
                    _names[component])
                {
                    Attributes = MemberAttributes.Private
                });
            }

            var initializeComponent = new CodeMemberMethod
            {
                Name = "InitializeComponent",
                Attributes = MemberAttributes.Private
            };
            codeClass.Members.Add(initializeComponent);

            CodeVariableReferenceExpression? resourcesVariable = null;
            if (_localizationModel == CodeDomLocalizationModel.PropertyReflection)
            {
                resourcesVariable = new CodeVariableReferenceExpression("resources");
                initializeComponent.Statements.Add(new CodeVariableDeclarationStatement(
                    typeof(ComponentResourceManager).FullName!,
                    "resources",
                    new CodeObjectCreateExpression(
                        typeof(ComponentResourceManager).FullName!,
                        new CodeTypeOfExpression(GetRootClassName(rootComponent)))));
            }

            foreach (IComponent component in GetSerializableComponents(container, rootComponent))
            {
                initializeComponent.Statements.Add(new CodeAssignStatement(
                    CreateComponentExpression(component, rootComponent),
                    new CodeObjectCreateExpression(component.GetType().FullName ?? component.GetType().Name)));
            }

            if (resourcesVariable is not null)
            {
                SerializeApplyResources(initializeComponent, resourcesVariable, rootComponent, rootComponent, "$this");
                foreach (IComponent component in GetSerializableComponents(container, rootComponent))
                {
                    SerializeApplyResources(initializeComponent, resourcesVariable, component, rootComponent, _names[component]);
                }
            }

            SerializeProperties(initializeComponent, rootComponent, rootComponent);
            foreach (IComponent component in GetSerializableComponents(container, rootComponent))
            {
                SerializeProperties(initializeComponent, component, rootComponent);
            }

            if (rootComponent is Control rootControl)
            {
                SerializeControlChildren(initializeComponent, rootControl, rootComponent);
            }

            SerializeEvents(initializeComponent, rootComponent);

            return unit;
        }

        private string GetRootClassName(IComponent rootComponent)
        {
            if (_host is PortableDesignerHost portableHost
                && !string.IsNullOrWhiteSpace(portableHost.RootComponentClassName))
            {
                return portableHost.RootComponentClassName;
            }

            return rootComponent.GetType().FullName ?? rootComponent.GetType().Name;
        }

        private void CaptureComponentNames(IContainer? container, IComponent rootComponent)
        {
            if (!string.IsNullOrWhiteSpace(_serializationManager.GetName(rootComponent)))
            {
                _names[rootComponent] = _serializationManager.GetName(rootComponent)!;
            }

            if (container is null)
                return;

            foreach (IComponent component in container.Components)
            {
                string? name = _serializationManager.GetName(component);
                if (string.IsNullOrWhiteSpace(name))
                    name = component.Site?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    _names[component] = name!;
            }
        }

        private IEnumerable<IComponent> GetSerializableComponents(IContainer? container, IComponent rootComponent)
        {
            if (container is null)
                yield break;

            foreach (IComponent component in container.Components)
            {
                if (ReferenceEquals(component, rootComponent))
                    continue;
                if (!_names.ContainsKey(component))
                    continue;
                yield return component;
            }
        }

        private void SerializeProperties(CodeMemberMethod initializeComponent, IComponent component, IComponent rootComponent)
        {
            var target = CreateComponentExpression(component, rootComponent);
            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(component);
            foreach (PropertyDescriptor descriptor in properties)
            {
                if (descriptor.IsReadOnly
                    || !descriptor.IsBrowsable
                    || s_skippedProperties.Contains(descriptor.Name)
                    || !CanSerializePropertyType(descriptor.PropertyType))
                {
                    continue;
                }

                object? value;
                try
                {
                    if (!descriptor.ShouldSerializeValue(component)
                        && !ShouldAlwaysSerialize(component, descriptor))
                    {
                        continue;
                    }

                    value = descriptor.GetValue(component);
                }
                catch
                {
                    continue;
                }

                CodeExpression? valueExpression = CreateValueExpression(value, descriptor.PropertyType);
                if (valueExpression is null)
                    continue;

                initializeComponent.Statements.Add(new CodeAssignStatement(
                    new CodePropertyReferenceExpression(target, descriptor.Name),
                    valueExpression));
            }
        }

        private void SerializeApplyResources(
            CodeMemberMethod initializeComponent,
            CodeExpression resourcesExpression,
            IComponent component,
            IComponent rootComponent,
            string resourceName)
        {
            initializeComponent.Statements.Add(new CodeExpressionStatement(new CodeMethodInvokeExpression(
                resourcesExpression,
                nameof(ComponentResourceManager.ApplyResources),
                CreateComponentExpression(component, rootComponent),
                new CodePrimitiveExpression(resourceName))));
        }

        private static bool ShouldAlwaysSerialize(IComponent component, PropertyDescriptor descriptor)
        {
            return component is Control && string.Equals(descriptor.Name, nameof(Control.Name), StringComparison.Ordinal);
        }

        private void SerializeControlChildren(CodeMemberMethod initializeComponent, Control parent, IComponent rootComponent)
        {
            foreach (Control child in parent.Controls)
            {
                if (_names.ContainsKey(child)
                    && TryCreateComponentExpression(parent, rootComponent, out CodeExpression? parentExpression))
                {
                    initializeComponent.Statements.Add(new CodeExpressionStatement(
                        new CodeMethodInvokeExpression(
                            new CodePropertyReferenceExpression(parentExpression, nameof(Control.Controls)),
                            "Add",
                            CreateComponentExpression(child, rootComponent))));
                }

                SerializeControlChildren(initializeComponent, child, rootComponent);
            }
        }

        private void SerializeEvents(CodeMemberMethod initializeComponent, IComponent rootComponent)
        {
            if (_host.GetService(typeof(IEventBindingService)) is not EventBindingService eventBindingService)
                return;

            foreach (PortableEventBinding binding in eventBindingService.GetEventBindings())
            {
                if (!TryCreateComponentExpression(binding.Component, rootComponent, out CodeExpression? targetExpression))
                    continue;

                initializeComponent.Statements.Add(new CodeAttachEventStatement(
                    new CodeEventReferenceExpression(targetExpression, binding.Event.Name),
                    new CodeDelegateCreateExpression(
                        new CodeTypeReference(binding.Event.EventType ?? typeof(EventHandler)),
                        new CodeThisReferenceExpression(),
                        binding.MethodName)));
            }
        }

        private CodeExpression CreateComponentExpression(IComponent component, IComponent rootComponent)
        {
            if (TryCreateComponentExpression(component, rootComponent, out CodeExpression? expression))
                return expression!;

            return new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), _names[component]);
        }

        private bool TryCreateComponentExpression(IComponent component, IComponent rootComponent, out CodeExpression? expression)
        {
            if (ReferenceEquals(component, rootComponent))
            {
                expression = new CodeThisReferenceExpression();
                return true;
            }

            if (_names.TryGetValue(component, out string? name))
            {
                expression = new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), name);
                return true;
            }

            if (component is Control control
                && control.Parent is ToolStripContainer toolStripContainer
                && TryCreateToolStripContainerPanelExpression(toolStripContainer, control, rootComponent, out expression))
            {
                return true;
            }

            expression = null;
            return false;
        }

        private bool TryCreateToolStripContainerPanelExpression(
            ToolStripContainer owner,
            Control panel,
            IComponent rootComponent,
            out CodeExpression? expression)
        {
            string? propertyName = null;
            if (ReferenceEquals(panel, owner.TopToolStripPanel))
                propertyName = nameof(ToolStripContainer.TopToolStripPanel);
            else if (ReferenceEquals(panel, owner.BottomToolStripPanel))
                propertyName = nameof(ToolStripContainer.BottomToolStripPanel);
            else if (ReferenceEquals(panel, owner.LeftToolStripPanel))
                propertyName = nameof(ToolStripContainer.LeftToolStripPanel);
            else if (ReferenceEquals(panel, owner.RightToolStripPanel))
                propertyName = nameof(ToolStripContainer.RightToolStripPanel);
            else if (ReferenceEquals(panel, owner.ContentPanel))
                propertyName = nameof(ToolStripContainer.ContentPanel);

            if (propertyName is null || !TryCreateComponentExpression(owner, rootComponent, out CodeExpression? ownerExpression))
            {
                expression = null;
                return false;
            }

            expression = new CodePropertyReferenceExpression(ownerExpression, propertyName);
            return true;
        }

        private static bool CanSerializePropertyType(Type propertyType)
        {
            Type type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            return type == typeof(string)
                || type == typeof(bool)
                || type == typeof(byte)
                || type == typeof(short)
                || type == typeof(int)
                || type == typeof(long)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(decimal)
                || type.IsEnum
                || type == typeof(System.Drawing.Point)
                || type == typeof(System.Drawing.Size)
                || type == typeof(System.Drawing.SizeF)
                || type == typeof(System.Drawing.Rectangle)
                || type == typeof(System.Drawing.Color);
        }

        private static CodeExpression? CreateValueExpression(object? value, Type propertyType)
        {
            if (value is null)
                return new CodePrimitiveExpression(null);

            Type type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (type == typeof(string)
                || type == typeof(bool)
                || type == typeof(byte)
                || type == typeof(short)
                || type == typeof(int)
                || type == typeof(long)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(decimal))
            {
                return new CodePrimitiveExpression(value);
            }

            if (type.IsEnum)
            {
                return new CodeFieldReferenceExpression(
                    new CodeTypeReferenceExpression(type.FullName ?? type.Name),
                    value.ToString());
            }

            if (value is System.Drawing.Point point)
            {
                return new CodeObjectCreateExpression(
                    typeof(System.Drawing.Point).FullName!,
                    new CodePrimitiveExpression(point.X),
                    new CodePrimitiveExpression(point.Y));
            }

            if (value is System.Drawing.Size size)
            {
                return new CodeObjectCreateExpression(
                    typeof(System.Drawing.Size).FullName!,
                    new CodePrimitiveExpression(size.Width),
                    new CodePrimitiveExpression(size.Height));
            }

            if (value is System.Drawing.SizeF sizeF)
            {
                return new CodeObjectCreateExpression(
                    typeof(System.Drawing.SizeF).FullName!,
                    new CodePrimitiveExpression(sizeF.Width),
                    new CodePrimitiveExpression(sizeF.Height));
            }

            if (value is System.Drawing.Rectangle rectangle)
            {
                return new CodeObjectCreateExpression(
                    typeof(System.Drawing.Rectangle).FullName!,
                    new CodePrimitiveExpression(rectangle.X),
                    new CodePrimitiveExpression(rectangle.Y),
                    new CodePrimitiveExpression(rectangle.Width),
                    new CodePrimitiveExpression(rectangle.Height));
            }

            if (value is System.Drawing.Color color)
            {
                return new CodeMethodInvokeExpression(
                    new CodeTypeReferenceExpression(typeof(System.Drawing.Color).FullName!),
                    nameof(System.Drawing.Color.FromArgb),
                    new CodePrimitiveExpression(color.ToArgb()));
            }

            return null;
        }

        private static void SplitQualifiedName(string qualifiedName, out string namespaceName, out string className)
        {
            int lastDot = qualifiedName.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == qualifiedName.Length - 1)
            {
                namespaceName = string.Empty;
                className = qualifiedName;
                return;
            }

            namespaceName = qualifiedName[..lastDot];
            className = qualifiedName[(lastDot + 1)..];
        }
    }

    internal static class PortableWinFormsTypeResolver
    {
        private static readonly Type[] s_knownTypes =
        {
            typeof(System.Drawing.Point),
            typeof(System.Drawing.Size),
            typeof(System.Drawing.SizeF),
            typeof(System.Drawing.Rectangle),
            typeof(System.Drawing.Color),
            typeof(System.Drawing.ContentAlignment),
            typeof(System.ComponentModel.ComponentResourceManager),
            typeof(System.ComponentModel.Container),
            typeof(System.ComponentModel.ISupportInitialize),
            typeof(Control),
            typeof(UserControl),
            typeof(Form),
            typeof(Panel),
            typeof(SplitterPanel),
            typeof(ToolStripPanel),
            typeof(ToolStripContentPanel),
            typeof(ToolStripContainer),
            typeof(SplitContainer),
            typeof(Button),
            typeof(Label),
            typeof(CheckBox),
            typeof(RadioButton),
            typeof(GroupBox),
            typeof(TextBox),
            typeof(RichTextBox),
            typeof(ComboBox),
            typeof(ListBox),
            typeof(CheckedListBox),
            typeof(TabControl),
            typeof(TabPage),
            typeof(DataGridView),
            typeof(DataGridViewTextBoxColumn),
            typeof(DataGridViewComboBoxColumn),
            typeof(PictureBox),
            typeof(ProgressBar),
            typeof(NumericUpDown),
            typeof(TrackBar),
            typeof(PropertyGrid),
            typeof(WebBrowser),
            typeof(ToolStrip),
            typeof(MenuStrip),
            typeof(StatusStrip),
            typeof(ContextMenuStrip),
            typeof(ToolStripDropDown),
            typeof(ToolStripMenuItem),
            typeof(ToolStripButton),
            typeof(ToolStripDropDownButton),
            typeof(ToolStripSplitButton),
            typeof(ToolStripLabel),
            typeof(ToolStripProgressBar),
            typeof(ToolStripTextBox),
            typeof(ToolStripSeparator),
            typeof(ListView),
            typeof(ColumnHeader),
            typeof(ListViewGroup),
            typeof(ListViewItem),
            typeof(TreeView),
            typeof(TreeNode),
            typeof(ImageList),
            typeof(ImageListStreamer),
            typeof(DockStyle),
            typeof(Orientation),
            typeof(HorizontalAlignment),
            typeof(View),
            typeof(SortOrder),
            typeof(BorderStyle),
            typeof(AutoScaleMode),
            typeof(CheckState),
            typeof(FlatStyle),
            typeof(ColumnHeaderStyle),
            typeof(ToolStripGripStyle),
            typeof(ToolStripItemDisplayStyle),
            typeof(ToolStripItemImageScaling),
            typeof(ColorDepth),
            typeof(PictureBoxSizeMode),
            typeof(ProgressBarStyle)
        };

        public static Type? Resolve(ITypeResolutionService? typeResolutionService, CodeTypeReference typeReference)
        {
            ArgumentNullException.ThrowIfNull(typeReference);
            return Resolve(typeResolutionService, typeReference.BaseType);
        }

        public static Type? Resolve(ITypeResolutionService? typeResolutionService, string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            string normalizedTypeName = NormalizeTypeName(typeName);
            Type? resolved = typeResolutionService?.GetType(typeName, false);
            if (resolved is null && !string.Equals(normalizedTypeName, typeName, StringComparison.Ordinal))
                resolved = typeResolutionService?.GetType(normalizedTypeName, false);
            if (resolved is not null)
                return resolved;

            resolved = Type.GetType(typeName, false);
            if (resolved is null && !string.Equals(normalizedTypeName, typeName, StringComparison.Ordinal))
                resolved = Type.GetType(normalizedTypeName, false);
            if (resolved is not null)
                return resolved;

            for (int i = 0; i < s_knownTypes.Length; i++)
            {
                Type candidate = s_knownTypes[i];
                if (string.Equals(candidate.FullName, typeName, StringComparison.Ordinal)
                    || string.Equals(candidate.Name, typeName, StringComparison.Ordinal)
                    || string.Equals(candidate.FullName, normalizedTypeName, StringComparison.Ordinal)
                    || string.Equals(candidate.Name, normalizedTypeName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string NormalizeTypeName(string typeName)
        {
            int assemblySeparator = typeName.IndexOf(',');
            return assemblySeparator > 0
                ? typeName[..assemblySeparator].Trim()
                : typeName;
        }
    }

    internal sealed class PortableCodeDomDesignSurfaceDeserializer
    {
        private readonly IDesignerLoaderHost _host;
        private readonly ITypeResolutionService? _typeResolutionService;
        private readonly IDesignerSerializationManager? _serializationManager;
        private readonly Dictionary<string, object?> _locals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Type> _fieldTypes = new(StringComparer.Ordinal);
        private readonly List<object> _errors = new();
        private IComponent? _rootComponent;

        public PortableCodeDomDesignSurfaceDeserializer(IDesignerLoaderHost host, ITypeResolutionService? typeResolutionService)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _typeResolutionService = typeResolutionService;
            _serializationManager = host.GetService(typeof(IDesignerSerializationManager)) as IDesignerSerializationManager;
        }

        public string RootComponentClassName { get; private set; } = typeof(Panel).FullName!;

        public object[] Load(CodeCompileUnit unit)
        {
            ArgumentNullException.ThrowIfNull(unit);

            try
            {
                CodeTypeDeclaration? codeClass = FindFirstType(unit);
                if (codeClass is null)
                    return Array.Empty<object>();

                RootComponentClassName = GetQualifiedTypeName(unit, codeClass);
                Type rootType = ResolveRootType(codeClass) ?? typeof(Panel);
                string rootName = string.IsNullOrEmpty(codeClass.Name) ? "Root" : codeClass.Name;
                _rootComponent = _host.CreateComponent(rootType, rootName);
                _serializationManager?.SetName(_rootComponent, rootName);
                _locals["this"] = _rootComponent;
                _locals["base"] = _rootComponent;

                foreach (CodeTypeMember member in codeClass.Members)
                {
                    if (member is CodeMemberField field)
                    {
                        Type? fieldType = PortableWinFormsTypeResolver.Resolve(_typeResolutionService, field.Type);
                        if (fieldType is not null && !string.IsNullOrEmpty(field.Name))
                            _fieldTypes[field.Name] = fieldType;
                    }
                }

                CodeMemberMethod? initializeComponent = FindInitializeComponent(codeClass);
                if (initializeComponent is not null)
                {
                    ExecuteStatements(initializeComponent.Statements);
                }
            }
            catch (Exception ex)
            {
                _errors.Add(ex);
            }

            return _errors.ToArray();
        }

        private static CodeTypeDeclaration? FindFirstType(CodeCompileUnit unit)
        {
            foreach (CodeNamespace codeNamespace in unit.Namespaces)
            {
                foreach (CodeTypeDeclaration codeClass in codeNamespace.Types)
                {
                    return codeClass;
                }
            }

            return null;
        }

        private static CodeMemberMethod? FindInitializeComponent(CodeTypeDeclaration codeClass)
        {
            foreach (CodeTypeMember member in codeClass.Members)
            {
                if (member is CodeMemberMethod method
                    && string.Equals(method.Name, "InitializeComponent", StringComparison.Ordinal))
                {
                    return method;
                }
            }

            return null;
        }

        private static string GetQualifiedTypeName(CodeCompileUnit unit, CodeTypeDeclaration codeClass)
        {
            foreach (CodeNamespace codeNamespace in unit.Namespaces)
            {
                if (codeNamespace.Types.Contains(codeClass))
                {
                    return string.IsNullOrEmpty(codeNamespace.Name)
                        ? codeClass.Name
                        : codeNamespace.Name + "." + codeClass.Name;
                }
            }

            return codeClass.Name;
        }

        private Type? ResolveRootType(CodeTypeDeclaration codeClass)
        {
            foreach (CodeTypeReference baseType in codeClass.BaseTypes)
            {
                Type? type = PortableWinFormsTypeResolver.Resolve(_typeResolutionService, baseType);
                if (type is not null && typeof(IComponent).IsAssignableFrom(type))
                    return type;
            }

            return null;
        }

        private void ExecuteStatements(CodeStatementCollection statements)
        {
            foreach (CodeStatement statement in statements)
            {
                ExecuteStatement(statement);
            }
        }

        private void ExecuteStatement(CodeStatement statement)
        {
            try
            {
                switch (statement)
                {
                    case CodeVariableDeclarationStatement variable:
                        ExecuteVariableDeclaration(variable);
                        break;
                    case CodeAssignStatement assignment:
                        ExecuteAssignment(assignment);
                        break;
                    case CodeExpressionStatement expressionStatement:
                        _ = EvaluateExpression(expressionStatement.Expression);
                        break;
                    case CodeAttachEventStatement:
                    case CodeRemoveEventStatement:
                        break;
                }
            }
            catch (Exception ex)
            {
                _errors.Add(ex);
            }
        }

        private void ExecuteVariableDeclaration(CodeVariableDeclarationStatement variable)
        {
            if (!string.IsNullOrEmpty(variable.Name))
            {
                Type? variableType = PortableWinFormsTypeResolver.Resolve(_typeResolutionService, variable.Type);
                if (variableType is not null)
                    _fieldTypes[variable.Name] = variableType;
            }

            object? value = variable.InitExpression is not null
                ? EvaluateExpression(variable.InitExpression, variable.Name)
                : null;
            _locals[variable.Name] = value;
            RegisterNamedInstance(variable.Name, value);
        }

        private void ExecuteAssignment(CodeAssignStatement assignment)
        {
            if (assignment.Left is CodeFieldReferenceExpression fieldReference)
            {
                string? fieldName = TryGetThisFieldName(fieldReference);
                if (!string.IsNullOrEmpty(fieldName))
                {
                    object? value = EvaluateExpression(assignment.Right, fieldName);
                    _locals[fieldName] = value;
                    RegisterNamedInstance(fieldName, value);
                    return;
                }
            }

            object? right = EvaluateExpression(assignment.Right);
            if (assignment.Left is CodePropertyReferenceExpression propertyReference)
            {
                object? target = EvaluateExpression(propertyReference.TargetObject);
                SetPublicProperty(target, propertyReference.PropertyName, right);
            }
        }

        private object? EvaluateExpression(CodeExpression expression, string? preferredName = null)
        {
            switch (expression)
            {
                case CodePrimitiveExpression primitive:
                    return primitive.Value;
                case CodeThisReferenceExpression:
                    return _rootComponent;
                case CodeBaseReferenceExpression:
                    return _rootComponent;
                case CodeVariableReferenceExpression variable:
                    return TryGetNamedInstance(variable.VariableName);
                case CodeFieldReferenceExpression field:
                    return EvaluateFieldReference(field);
                case CodePropertyReferenceExpression property:
                    return GetPublicProperty(EvaluateExpression(property.TargetObject), property.PropertyName);
                case CodeObjectCreateExpression create:
                    return CreateObject(create, preferredName);
                case CodeArrayCreateExpression array:
                    return CreateArray(array);
                case CodeCastExpression cast:
                    return EvaluateExpression(cast.Expression, preferredName);
                case CodeTypeOfExpression typeOf:
                    return PortableWinFormsTypeResolver.Resolve(_typeResolutionService, typeOf.Type)
                        ?? _rootComponent?.GetType()
                        ?? typeof(Control);
                case CodeMethodInvokeExpression methodInvoke:
                    return InvokeMethod(methodInvoke);
                case CodeTypeReferenceExpression typeReference:
                    return PortableWinFormsTypeResolver.Resolve(_typeResolutionService, typeReference.Type);
                case CodeDelegateCreateExpression:
                    return null;
                default:
                    return null;
            }
        }

        private object? EvaluateFieldReference(CodeFieldReferenceExpression field)
        {
            if (field.TargetObject is CodeTypeReferenceExpression typeReference)
            {
                Type? type = PortableWinFormsTypeResolver.Resolve(_typeResolutionService, typeReference.Type);
                if (type?.IsEnum == true && Enum.TryParse(type, field.FieldName, out object? enumValue))
                    return enumValue;

                if (type == typeof(System.Drawing.Color))
                    return GetPublicProperty(type, field.FieldName);
            }

            string? fieldName = TryGetThisFieldName(field);
            if (!string.IsNullOrEmpty(fieldName))
                return TryGetNamedInstance(fieldName);

            return null;
        }

        private static string? TryGetThisFieldName(CodeFieldReferenceExpression field)
        {
            return field.TargetObject is CodeThisReferenceExpression or CodeBaseReferenceExpression
                ? field.FieldName
                : null;
        }

        private object? CreateObject(CodeObjectCreateExpression create, string? preferredName)
        {
            Type? type = PortableWinFormsTypeResolver.Resolve(_typeResolutionService, create.CreateType);
            if (type is null)
                return null;

            object?[] arguments = EvaluateArguments(create.Parameters);
            ICollection argumentCollection = arguments;
            bool addToContainer = typeof(IComponent).IsAssignableFrom(type)
                && !HasContainerConstructorArgument(arguments);

            object? instance = _serializationManager?.CreateInstance(type, argumentCollection, preferredName, addToContainer)
                ?? Activator.CreateInstance(type, arguments);
            RegisterNamedInstance(preferredName, instance);
            return instance;
        }

        private static bool HasContainerConstructorArgument(object?[] arguments)
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] is IContainer)
                    return true;
            }

            return false;
        }

        private Array CreateArray(CodeArrayCreateExpression arrayExpression)
        {
            Type elementType = PortableWinFormsTypeResolver.Resolve(_typeResolutionService, arrayExpression.CreateType)
                ?? typeof(object);
            int count = arrayExpression.Initializers.Count;
            Array array = Array.CreateInstance(elementType, count);
            for (int i = 0; i < count; i++)
            {
                object? value = EvaluateExpression(arrayExpression.Initializers[i]);
                array.SetValue(CoerceValue(value, elementType), i);
            }

            return array;
        }

        private object? InvokeMethod(CodeMethodInvokeExpression methodInvoke)
        {
            object? target = EvaluateExpression(methodInvoke.Method.TargetObject);
            string methodName = methodInvoke.Method.MethodName;
            object?[] arguments = EvaluateArguments(methodInvoke.Parameters);

            if (target is ComponentResourceManager resourceManager)
            {
                if (string.Equals(methodName, nameof(System.ComponentModel.ComponentResourceManager.GetObject), StringComparison.Ordinal)
                    && arguments.Length > 0
                    && arguments[0] is string objectName)
                {
                    try
                    {
                        return resourceManager.GetObject(objectName, CultureInfo.CurrentUICulture);
                    }
                    catch
                    {
                        return null;
                    }
                }

                if (string.Equals(methodName, nameof(System.ComponentModel.ComponentResourceManager.ApplyResources), StringComparison.Ordinal)
                    && arguments.Length >= 2
                    && arguments[0] is object resourceTarget
                    && arguments[1] is string resourceName)
                {
                    try
                    {
                        resourceManager.ApplyResources(resourceTarget, resourceName, CultureInfo.CurrentUICulture);
                    }
                    catch
                    {
                    }

                    ApplyResourceEntries(resourceManager, resourceTarget, resourceName);
                    return null;
                }
            }

            if (target is ResourceManager generalResourceManager)
            {
                if (string.Equals(methodName, nameof(ResourceManager.GetString), StringComparison.Ordinal)
                    && arguments.Length > 0
                    && arguments[0] is string stringName)
                {
                    try
                    {
                        return generalResourceManager.GetString(stringName, CultureInfo.CurrentUICulture);
                    }
                    catch
                    {
                        return null;
                    }
                }

                if (string.Equals(methodName, nameof(ResourceManager.GetStream), StringComparison.Ordinal)
                    && arguments.Length > 0
                    && arguments[0] is string streamName)
                {
                    try
                    {
                        return generalResourceManager.GetStream(streamName, CultureInfo.CurrentUICulture);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            if (target is ISupportInitialize supportInitialize)
            {
                if (string.Equals(methodName, nameof(ISupportInitialize.BeginInit), StringComparison.Ordinal))
                {
                    supportInitialize.BeginInit();
                    return null;
                }

                if (string.Equals(methodName, nameof(ISupportInitialize.EndInit), StringComparison.Ordinal))
                {
                    supportInitialize.EndInit();
                    return null;
                }
            }

            if (target is Control control)
            {
                if (string.Equals(methodName, nameof(Control.SuspendLayout), StringComparison.Ordinal))
                {
                    control.SuspendLayout();
                    return null;
                }

                if (string.Equals(methodName, nameof(Control.ResumeLayout), StringComparison.Ordinal))
                {
                    if (arguments.Length > 0 && arguments[0] is bool performLayout)
                        control.ResumeLayout(performLayout);
                    else
                        control.ResumeLayout();
                    return null;
                }

                if (string.Equals(methodName, nameof(Control.PerformLayout), StringComparison.Ordinal))
                {
                    control.PerformLayout();
                    return null;
                }
            }

            InvokeCollectionMethod(target, methodName, arguments);
            return null;
        }

        private static void ApplyResourceEntries(ResourceManager resourceManager, object target, string resourceName)
        {
            ResourceSet? resourceSet;
            try
            {
                resourceSet = resourceManager.GetResourceSet(CultureInfo.CurrentUICulture, createIfNotExists: true, tryParents: true);
            }
            catch
            {
                return;
            }

            if (resourceSet is null)
                return;

            string prefix = resourceName + ".";
            foreach (DictionaryEntry entry in resourceSet)
            {
                if (entry.Key is not string key || !key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                string propertyName = key[prefix.Length..];
                if (propertyName.Length == 0 || propertyName.Contains('.', StringComparison.Ordinal))
                    continue;

                SetPublicProperty(target, propertyName, entry.Value);
            }
        }

        private static void InvokeCollectionMethod(object? target, string methodName, object?[] arguments)
        {
            if (target is null)
                return;

            if (string.Equals(methodName, "AddRange", StringComparison.Ordinal) && arguments.Length > 0)
            {
                switch (target)
                {
                    case Control.ControlCollection controls when arguments[0] is Control[] controlArray:
                        controls.AddRange(controlArray);
                        return;
                    case ToolStripItemCollection items when arguments[0] is ToolStripItem[] itemArray:
                        items.AddRange(itemArray);
                        return;
                    case ListView.ColumnHeaderCollection columns when arguments[0] is ColumnHeader[] columnArray:
                        columns.AddRange(columnArray);
                        return;
                    case ListView.ListViewGroupCollection groups when arguments[0] is ListViewGroup[] groupArray:
                        groups.AddRange(groupArray);
                        return;
                    case ListView.ListViewItemCollection listItems when arguments[0] is ListViewItem[] listItemArray:
                        listItems.AddRange(listItemArray);
                        return;
                    case TreeNodeCollection treeNodes when arguments[0] is TreeNode[] treeNodeArray:
                        treeNodes.AddRange(treeNodeArray);
                        return;
                    case ImageList.ImageCollection images when arguments[0] is System.Drawing.Image[] imageArray:
                        images.AddRange(imageArray);
                        return;
                }
            }

            if (string.Equals(methodName, "Add", StringComparison.Ordinal) && arguments.Length > 0)
            {
                switch (target)
                {
                    case Control.ControlCollection controls when arguments[0] is Control control:
                        controls.Add(control);
                        return;
                    case ToolStripItemCollection items when arguments[0] is ToolStripItem item:
                        items.Add(item);
                        return;
                    case ListView.ColumnHeaderCollection columns when arguments[0] is ColumnHeader column:
                        columns.Add(column);
                        return;
                    case ListView.ListViewGroupCollection groups when arguments[0] is ListViewGroup group:
                        groups.Add(group);
                        return;
                    case ListView.ListViewItemCollection listItems when arguments[0] is ListViewItem listItem:
                        listItems.Add(listItem);
                        return;
                    case TreeNodeCollection treeNodes when arguments[0] is TreeNode treeNode:
                        treeNodes.Add(treeNode);
                        return;
                    case IList list:
                        list.Add(arguments[0]);
                        return;
                }
            }

            if (target is ImageList.ImageCollection imageCollection
                && string.Equals(methodName, "SetKeyName", StringComparison.Ordinal)
                && arguments.Length >= 2
                && arguments[0] is int imageIndex
                && arguments[1] is string keyName)
            {
                imageCollection.SetKeyName(imageIndex, keyName);
            }
        }

        private object?[] EvaluateArguments(CodeExpressionCollection expressions)
        {
            object?[] arguments = new object?[expressions.Count];
            for (int i = 0; i < expressions.Count; i++)
            {
                arguments[i] = EvaluateExpression(expressions[i]);
            }

            return arguments;
        }

        private object? TryGetNamedInstance(string name)
        {
            if (_locals.TryGetValue(name, out object? local))
                return local;

            object? instance = _serializationManager?.GetInstance(name);
            if (instance is not null)
                return instance;

            return _host.Container.Components[name];
        }

        private void RegisterNamedInstance(string? name, object? instance)
        {
            if (string.IsNullOrEmpty(name) || instance is null)
                return;

            _locals[name] = instance;
            _serializationManager?.SetName(instance, name);
            if (instance is Control control)
                control.Name = name;
            else if (instance is ToolStripItem toolStripItem)
                toolStripItem.Name = name;
        }

        private static object? GetPublicProperty(object? target, string propertyName)
        {
            if (target is null || string.IsNullOrEmpty(propertyName))
                return null;

            PropertyDescriptor? descriptor = TypeDescriptor.GetProperties(target).Find(propertyName, false);
            if (descriptor is null)
                return null;

            try
            {
                return descriptor.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private static void SetPublicProperty(object? target, string propertyName, object? value)
        {
            if (target is null || string.IsNullOrEmpty(propertyName))
                return;

            PropertyDescriptor? descriptor = TypeDescriptor.GetProperties(target).Find(propertyName, false);
            if (descriptor is null || descriptor.IsReadOnly)
                return;

            object? coerced = CoerceValue(value, descriptor.PropertyType);
            if (coerced is null && descriptor.PropertyType.IsValueType && Nullable.GetUnderlyingType(descriptor.PropertyType) is null)
                return;

            descriptor.SetValue(target, coerced);
        }

        private static object? CoerceValue(object? value, Type targetType)
        {
            if (value is null)
                return null;

            Type nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (nonNullableTarget.IsInstanceOfType(value))
                return value;

            if (nonNullableTarget.IsEnum)
            {
                if (value is string text)
                    return Enum.Parse(nonNullableTarget, text);
                return Enum.ToObject(nonNullableTarget, value);
            }

            try
            {
                return Convert.ChangeType(value, nonNullableTarget, CultureInfo.InvariantCulture);
            }
            catch
            {
                return value;
            }
        }
    }

    public class CodeDomSerializer
    {
        public virtual object? Deserialize(IDesignerSerializationManager manager, object codeObject)
        {
            return null;
        }

        protected virtual object? DeserializeInstance(IDesignerSerializationManager manager, Type type, object[] parameters, string name, bool addToContainer)
        {
            return null;
        }

        public virtual string GetTargetComponentName(CodeStatement statement, CodeExpression expression, Type targetType)
        {
            return string.Empty;
        }

        public virtual object? Serialize(IDesignerSerializationManager manager, object value)
        {
            return null;
        }

        public virtual object? SerializeAbsolute(IDesignerSerializationManager manager, object value)
        {
            return Serialize(manager, value);
        }

        public virtual CodeStatementCollection SerializeMember(IDesignerSerializationManager manager, object owningObject, MemberDescriptor member)
        {
            return new CodeStatementCollection();
        }

        public virtual CodeStatementCollection SerializeMemberAbsolute(IDesignerSerializationManager manager, object owningObject, MemberDescriptor member)
        {
            return SerializeMember(manager, owningObject, member);
        }

        protected CodeExpression? SerializeToExpression(IDesignerSerializationManager manager, object value)
        {
            return null;
        }
    }

    public class MemberCodeDomSerializer
    {
        public virtual void Serialize(IDesignerSerializationManager manager, object value, MemberDescriptor descriptor, CodeStatementCollection statements)
        {
        }

        public virtual bool ShouldSerialize(IDesignerSerializationManager manager, object value, MemberDescriptor descriptor)
        {
            return false;
        }

        protected CodeExpression? SerializeToExpression(IDesignerSerializationManager manager, object value)
        {
            return null;
        }
    }

    public sealed class ExceptionCollection : Exception
    {
        public ExceptionCollection(ICollection exceptions)
        {
            Exceptions = exceptions;
        }

        public ICollection Exceptions { get; }
    }
}

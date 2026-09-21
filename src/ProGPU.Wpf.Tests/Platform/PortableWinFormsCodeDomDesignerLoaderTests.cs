using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.ComponentModel.Design.Serialization;
using System.Linq;
using System.Reflection;
using Xunit;
using Forms = System.Windows.Forms;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class PortableWinFormsCodeDomDesignerLoaderTests
{
    [Fact]
    public void BeginLoadReplaysCodeDomControlTree()
    {
        var surface = new DesignSurface();

        surface.BeginLoad(new TestCodeDomDesignerLoader());

        Assert.Empty(surface.LoadErrors);
        Assert.True(surface.IsLoaded);
        var root = Assert.IsType<Forms.UserControl>(surface.View);
        Assert.Equal("SampleControl", root.Name);
        Assert.Equal(new System.Drawing.Size(320, 200), root.Size);

        var panel = Assert.IsType<Forms.Panel>(Assert.Single(root.Controls));
        Assert.Equal("panel1", panel.Name);
        Assert.Equal(Forms.DockStyle.Fill, panel.Dock);

        var button = Assert.IsType<Forms.Button>(Assert.Single(panel.Controls));
        Assert.Equal("button1", button.Name);
        Assert.Equal("Run", button.Text);
        Assert.Equal(new System.Drawing.Point(5, 6), button.Location);

        var host = Assert.IsAssignableFrom<IDesignerHost>(surface.GetService(typeof(IDesignerHost)));
        Assert.Equal(3, host.Container.Components.Count);

        var serializationManager = Assert.IsAssignableFrom<IDesignerSerializationManager>(
            surface.GetService(typeof(IDesignerSerializationManager)));
        Assert.Same(button, serializationManager.GetInstance("button1"));
    }

    [Fact]
    public void FlushSerializesChangedControlProperty()
    {
        var loader = new TestCodeDomDesignerLoader();
        var surface = new DesignSurface();

        surface.BeginLoad(loader);

        var root = Assert.IsType<Forms.UserControl>(surface.View);
        var panel = Assert.IsType<Forms.Panel>(Assert.Single(root.Controls));
        var button = Assert.IsType<Forms.Button>(Assert.Single(panel.Controls));
        button.Text = "Saved";

        surface.Flush();

        Assert.NotNull(loader.WrittenUnit);
        var textAssignment = FindAssignment(loader.WrittenUnit!, "button1", nameof(Forms.Control.Text));
        var primitive = Assert.IsType<CodePrimitiveExpression>(textAssignment.Right);
        Assert.Equal("Saved", primitive.Value);
    }

    [Fact]
    public void FlushPreservesExistingEventHookups()
    {
        var loader = new TestCodeDomDesignerLoader();
        var surface = new DesignSurface();

        surface.BeginLoad(loader);
        surface.Flush();

        Assert.NotNull(loader.WrittenUnit);
        var eventStatement = FindEventAttach(
            loader.WrittenUnit!,
            "button1",
            nameof(Forms.Control.Click),
            "button1_Click");
        Assert.NotNull(eventStatement);
    }

    [Fact]
    public void FlushSerializesDesignerEditedEventHookups()
    {
        var loader = new TestCodeDomDesignerLoader();
        var surface = new DesignSurface();

        surface.BeginLoad(loader);

        var root = Assert.IsType<Forms.UserControl>(surface.View);
        var panel = Assert.IsType<Forms.Panel>(Assert.Single(root.Controls));
        var button = Assert.IsType<Forms.Button>(Assert.Single(panel.Controls));
        var eventBindingService = Assert.IsAssignableFrom<IEventBindingService>(
            surface.GetService(typeof(IEventBindingService)));
        EventDescriptor click = Assert.IsAssignableFrom<EventDescriptor>(
            TypeDescriptor.GetEvents(button)[nameof(Forms.Control.Click)]);
        PropertyDescriptor clickProperty = eventBindingService.GetEventProperty(click);

        Assert.Equal("button1_Click", clickProperty.GetValue(button));

        clickProperty.SetValue(button, "button1_CustomClick");
        surface.Flush();

        Assert.NotNull(loader.WrittenUnit);
        Assert.Null(FindEventAttach(
            loader.WrittenUnit!,
            "button1",
            nameof(Forms.Control.Click),
            "button1_Click"));
        Assert.NotNull(FindEventAttach(
            loader.WrittenUnit!,
            "button1",
            nameof(Forms.Control.Click),
            "button1_CustomClick"));
    }

    [Fact]
    public void EventBindingShowCodeCreatesAndSerializesHandler()
    {
        ServiceContainer services = new();
        NavigatingEventBindingService eventBindingService = new(services);
        services.AddService(typeof(IEventBindingService), eventBindingService);
        var loader = new TestCodeDomDesignerLoader();
        var surface = new DesignSurface(services);

        surface.BeginLoad(loader);

        var root = Assert.IsType<Forms.UserControl>(surface.View);
        var panel = Assert.IsType<Forms.Panel>(Assert.Single(root.Controls));
        var bindingService = Assert.IsAssignableFrom<IEventBindingService>(
            surface.GetService(typeof(IEventBindingService)));
        EventDescriptor click = Assert.IsAssignableFrom<EventDescriptor>(
            TypeDescriptor.GetEvents(panel)[nameof(Forms.Control.Click)]);

        Assert.True(bindingService.ShowCode(panel, click));
        Assert.Equal("panel1_Click", eventBindingService.ShownMethodName);
        Assert.Equal("panel1_Click", bindingService.GetEventProperty(click).GetValue(panel));

        surface.Flush();

        Assert.NotNull(loader.WrittenUnit);
        Assert.NotNull(FindEventAttach(
            loader.WrittenUnit!,
            "panel1",
            nameof(Forms.Control.Click),
            "panel1_Click"));
    }

    [Fact]
    public void DesignSurfaceUsesProvidedEventBindingService()
    {
        ServiceContainer services = new();
        TestEventBindingService eventBindingService = new(services);
        services.AddService(typeof(IEventBindingService), eventBindingService);
        var surface = new DesignSurface(services);

        surface.BeginLoad(new TestCodeDomDesignerLoader());

        Assert.Same(eventBindingService, surface.GetService(typeof(IEventBindingService)));
    }

    [Fact]
    public void HostCreateComponentAssignsSerializableName()
    {
        var loader = new TestCodeDomDesignerLoader();
        var surface = new DesignSurface();

        surface.BeginLoad(loader);

        var root = Assert.IsType<Forms.UserControl>(surface.View);
        var panel = Assert.IsType<Forms.Panel>(Assert.Single(root.Controls));
        var host = Assert.IsAssignableFrom<IDesignerHost>(surface.GetService(typeof(IDesignerHost)));
        var serializationManager = Assert.IsAssignableFrom<IDesignerSerializationManager>(
            surface.GetService(typeof(IDesignerSerializationManager)));

        var label = Assert.IsType<Forms.Label>(host.CreateComponent(typeof(Forms.Label)));
        label.Text = "Generated";
        panel.Controls.Add(label);

        Assert.Equal("label1", label.Site?.Name);
        Assert.Same(label, serializationManager.GetInstance("label1"));

        surface.Flush();

        Assert.NotNull(loader.WrittenUnit);
        Assert.NotNull(FindField(loader.WrittenUnit!, "label1"));
        Assert.NotNull(FindControlsAddInvocation(loader.WrittenUnit!, "panel1", "label1"));
        var textAssignment = FindAssignment(loader.WrittenUnit!, "label1", nameof(Forms.Control.Text));
        var primitive = Assert.IsType<CodePrimitiveExpression>(textAssignment.Right);
        Assert.Equal("Generated", primitive.Value);
    }

    [Fact]
    public void HostDestroyComponentDetachesControlAndEventBindings()
    {
        var loader = new TestCodeDomDesignerLoader();
        var surface = new DesignSurface();

        surface.BeginLoad(loader);

        var root = Assert.IsType<Forms.UserControl>(surface.View);
        var panel = Assert.IsType<Forms.Panel>(Assert.Single(root.Controls));
        var button = Assert.IsType<Forms.Button>(Assert.Single(panel.Controls));
        var host = Assert.IsAssignableFrom<IDesignerHost>(surface.GetService(typeof(IDesignerHost)));
        var serializationManager = Assert.IsAssignableFrom<IDesignerSerializationManager>(
            surface.GetService(typeof(IDesignerSerializationManager)));

        host.DestroyComponent(button);

        Assert.DoesNotContain(button, panel.Controls);
        Assert.DoesNotContain(button, host.Container.Components.Cast<IComponent>());
        Assert.Null(serializationManager.GetInstance("button1"));

        surface.Flush();

        Assert.NotNull(loader.WrittenUnit);
        Assert.Null(FindField(loader.WrittenUnit!, "button1"));
        Assert.Null(FindControlsAddInvocation(loader.WrittenUnit!, "panel1", "button1"));
        Assert.Null(FindEventAttach(
            loader.WrittenUnit!,
            "button1",
            nameof(Forms.Control.Click),
            "button1_Click"));
    }

    [Fact]
    public void FlushSerializesNamedChildrenInsideToolStripContainerPanels()
    {
        var loader = new ToolStripContainerCodeDomDesignerLoader();
        var surface = new DesignSurface();

        surface.BeginLoad(loader);

        var root = Assert.IsType<Forms.UserControl>(surface.View);
        var container = Assert.IsType<Forms.ToolStripContainer>(Assert.Single(root.Controls));
        var toolStrip = Assert.IsType<Forms.ToolStrip>(Assert.Single(container.BottomToolStripPanel.Controls));
        toolStrip.Text = "Updated strip";

        surface.Flush();

        Assert.NotNull(loader.WrittenUnit);
        var addStatement = FindPanelControlsAddInvocation(
            loader.WrittenUnit!,
            "tscMain",
            nameof(Forms.ToolStripContainer.BottomToolStripPanel),
            "toolStrip1");
        Assert.NotNull(addStatement);
    }

    [Fact]
    public void BeginLoadAppliesComponentResourceManagerResources()
    {
        ComponentResourceManager resourceManager = new(typeof(LocalizableWinFormsResourceTarget));
        Forms.Button probeButton = new();
        resourceManager.ApplyResources(probeButton, "button1");
        Assert.Equal("Localized button", probeButton.Text);

        var surface = new DesignSurface();

        surface.BeginLoad(new ResourceApplyCodeDomDesignerLoader());

        Assert.Empty(surface.LoadErrors);
        Assert.True(surface.IsLoaded);
        var root = Assert.IsType<Forms.UserControl>(surface.View);
        var button = Assert.IsType<Forms.Button>(Assert.Single(root.Controls));
        Assert.Equal("Localized button", button.Text);
        Assert.Equal("Localized root", root.Text);
    }

    [Fact]
    public void FlushEmitsApplyResourcesForReflectionLocalizationModel()
    {
        var loader = new ReflectionLocalizedCodeDomDesignerLoader();
        var surface = new DesignSurface();

        surface.BeginLoad(loader);
        surface.Flush();

        Assert.NotNull(loader.WrittenUnit);
        Assert.NotNull(FindVariableDeclaration(loader.WrittenUnit!, "resources"));
        Assert.NotNull(FindApplyResources(loader.WrittenUnit!, null, "$this"));
        Assert.NotNull(FindApplyResources(loader.WrittenUnit!, "button1", "button1"));
    }

    private static CodeAssignStatement FindAssignment(CodeCompileUnit unit, string fieldName, string propertyName)
    {
        return unit.Namespaces
            .Cast<CodeNamespace>()
            .SelectMany(codeNamespace => codeNamespace.Types.Cast<CodeTypeDeclaration>())
            .SelectMany(type => type.Members.OfType<CodeMemberMethod>())
            .Where(method => method.Name == "InitializeComponent")
            .SelectMany(method => method.Statements.OfType<CodeAssignStatement>())
            .Single(statement =>
                statement.Left is CodePropertyReferenceExpression property
                && property.PropertyName == propertyName
                && property.TargetObject is CodeFieldReferenceExpression field
                && field.TargetObject is CodeThisReferenceExpression
                && field.FieldName == fieldName);
    }

    private static CodeMemberField? FindField(CodeCompileUnit unit, string fieldName)
    {
        return unit.Namespaces
            .Cast<CodeNamespace>()
            .SelectMany(codeNamespace => codeNamespace.Types.Cast<CodeTypeDeclaration>())
            .SelectMany(type => type.Members.OfType<CodeMemberField>())
            .SingleOrDefault(field => field.Name == fieldName);
    }

    private static CodeExpressionStatement? FindControlsAddInvocation(
        CodeCompileUnit unit,
        string parentFieldName,
        string childFieldName)
    {
        return unit.Namespaces
            .Cast<CodeNamespace>()
            .SelectMany(codeNamespace => codeNamespace.Types.Cast<CodeTypeDeclaration>())
            .SelectMany(type => type.Members.OfType<CodeMemberMethod>())
            .Where(method => method.Name == "InitializeComponent")
            .SelectMany(method => method.Statements.OfType<CodeExpressionStatement>())
            .SingleOrDefault(statement =>
                statement.Expression is CodeMethodInvokeExpression invoke
                && invoke.Method.MethodName == "Add"
                && invoke.Parameters.Count == 1
                && invoke.Parameters[0] is CodeFieldReferenceExpression childField
                && childField.TargetObject is CodeThisReferenceExpression
                && childField.FieldName == childFieldName
                && invoke.Method.TargetObject is CodePropertyReferenceExpression controlsProperty
                && controlsProperty.PropertyName == nameof(Forms.Control.Controls)
                && controlsProperty.TargetObject is CodeFieldReferenceExpression parentField
                && parentField.TargetObject is CodeThisReferenceExpression
                && parentField.FieldName == parentFieldName);
    }

    private static CodeExpressionStatement? FindPanelControlsAddInvocation(
        CodeCompileUnit unit,
        string ownerFieldName,
        string panelPropertyName,
        string childFieldName)
    {
        return unit.Namespaces
            .Cast<CodeNamespace>()
            .SelectMany(codeNamespace => codeNamespace.Types.Cast<CodeTypeDeclaration>())
            .SelectMany(type => type.Members.OfType<CodeMemberMethod>())
            .Where(method => method.Name == "InitializeComponent")
            .SelectMany(method => method.Statements.OfType<CodeExpressionStatement>())
            .SingleOrDefault(statement =>
                statement.Expression is CodeMethodInvokeExpression invoke
                && invoke.Method.MethodName == "Add"
                && invoke.Parameters.Count == 1
                && invoke.Parameters[0] is CodeFieldReferenceExpression childField
                && childField.TargetObject is CodeThisReferenceExpression
                && childField.FieldName == childFieldName
                && invoke.Method.TargetObject is CodePropertyReferenceExpression controlsProperty
                && controlsProperty.PropertyName == nameof(Forms.Control.Controls)
                && controlsProperty.TargetObject is CodePropertyReferenceExpression panelProperty
                && panelProperty.PropertyName == panelPropertyName
                && panelProperty.TargetObject is CodeFieldReferenceExpression ownerField
                && ownerField.TargetObject is CodeThisReferenceExpression
                && ownerField.FieldName == ownerFieldName);
    }

    private static CodeVariableDeclarationStatement? FindVariableDeclaration(CodeCompileUnit unit, string variableName)
    {
        return unit.Namespaces
            .Cast<CodeNamespace>()
            .SelectMany(codeNamespace => codeNamespace.Types.Cast<CodeTypeDeclaration>())
            .SelectMany(type => type.Members.OfType<CodeMemberMethod>())
            .Where(method => method.Name == "InitializeComponent")
            .SelectMany(method => method.Statements.OfType<CodeVariableDeclarationStatement>())
            .SingleOrDefault(statement =>
                statement.Name == variableName
                && statement.Type.BaseType == typeof(ComponentResourceManager).FullName);
    }

    private static CodeExpressionStatement? FindApplyResources(
        CodeCompileUnit unit,
        string? fieldName,
        string resourceName)
    {
        return unit.Namespaces
            .Cast<CodeNamespace>()
            .SelectMany(codeNamespace => codeNamespace.Types.Cast<CodeTypeDeclaration>())
            .SelectMany(type => type.Members.OfType<CodeMemberMethod>())
            .Where(method => method.Name == "InitializeComponent")
            .SelectMany(method => method.Statements.OfType<CodeExpressionStatement>())
            .SingleOrDefault(statement =>
                statement.Expression is CodeMethodInvokeExpression invoke
                && invoke.Method.MethodName == nameof(ComponentResourceManager.ApplyResources)
                && invoke.Method.TargetObject is CodeVariableReferenceExpression variable
                && variable.VariableName == "resources"
                && invoke.Parameters.Count == 2
                && MatchesApplyResourcesTarget(invoke.Parameters[0], fieldName)
                && invoke.Parameters[1] is CodePrimitiveExpression primitive
                && Equals(primitive.Value, resourceName));
    }

    private static bool MatchesApplyResourcesTarget(CodeExpression expression, string? fieldName)
    {
        if (fieldName is null)
            return expression is CodeThisReferenceExpression;

        return expression is CodeFieldReferenceExpression field
            && field.TargetObject is CodeThisReferenceExpression
            && field.FieldName == fieldName;
    }

    private static CodeAttachEventStatement? FindEventAttach(
        CodeCompileUnit unit,
        string fieldName,
        string eventName,
        string methodName)
    {
        return unit.Namespaces
            .Cast<CodeNamespace>()
            .SelectMany(codeNamespace => codeNamespace.Types.Cast<CodeTypeDeclaration>())
            .SelectMany(type => type.Members.OfType<CodeMemberMethod>())
            .Where(method => method.Name == "InitializeComponent")
            .SelectMany(method => method.Statements.OfType<CodeAttachEventStatement>())
            .SingleOrDefault(statement =>
                statement.Event.EventName == eventName
                && statement.Event.TargetObject is CodeFieldReferenceExpression field
                && field.TargetObject is CodeThisReferenceExpression
                && field.FieldName == fieldName
                && statement.Listener is CodeDelegateCreateExpression listener
                && listener.TargetObject is CodeThisReferenceExpression
                && listener.MethodName == methodName);
    }

    private class TestCodeDomDesignerLoader : CodeDomDesignerLoader
    {
        public CodeCompileUnit? WrittenUnit { get; private set; }

        protected override CodeDomProvider? CodeDomProvider => null;

        protected override ITypeResolutionService? TypeResolutionService { get; } = new ResourceTypeResolutionService();

        protected override CodeCompileUnit Parse()
        {
            CodeTypeDeclaration codeClass = new("SampleControl");
            codeClass.BaseTypes.Add(typeof(Forms.UserControl).FullName!);
            codeClass.Members.Add(new CodeMemberField(typeof(Forms.Panel), "panel1"));
            codeClass.Members.Add(new CodeMemberField(typeof(Forms.Button), "button1"));
            codeClass.Members.Add(CreateInitializeComponent());

            CodeNamespace codeNamespace = new("PortableDesignerSmoke");
            codeNamespace.Types.Add(codeClass);

            CodeCompileUnit unit = new();
            unit.Namespaces.Add(codeNamespace);
            return unit;
        }

        protected override void Write(CodeCompileUnit unit)
        {
            WrittenUnit = unit;
        }

        private static CodeMemberMethod CreateInitializeComponent()
        {
            CodeMemberMethod method = new()
            {
                Name = "InitializeComponent"
            };

            CodeThisReferenceExpression @this = new();
            CodeFieldReferenceExpression panelField = new(@this, "panel1");
            CodeFieldReferenceExpression buttonField = new(@this, "button1");

            method.Statements.Add(new CodeAssignStatement(panelField, new CodeObjectCreateExpression(typeof(Forms.Panel))));
            method.Statements.Add(new CodeAssignStatement(buttonField, new CodeObjectCreateExpression(typeof(Forms.Button))));
            method.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(@this, nameof(Forms.Control.Name)),
                new CodePrimitiveExpression("SampleControl")));
            method.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(@this, nameof(Forms.Control.Size)),
                new CodeObjectCreateExpression(typeof(System.Drawing.Size), new CodePrimitiveExpression(320), new CodePrimitiveExpression(200))));
            method.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(panelField, nameof(Forms.Control.Dock)),
                new CodeFieldReferenceExpression(new CodeTypeReferenceExpression(typeof(Forms.DockStyle)), nameof(Forms.DockStyle.Fill))));
            method.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(buttonField, nameof(Forms.Control.Text)),
                new CodePrimitiveExpression("Run")));
            method.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(buttonField, nameof(Forms.Control.Location)),
                new CodeObjectCreateExpression(typeof(System.Drawing.Point), new CodePrimitiveExpression(5), new CodePrimitiveExpression(6))));
            method.Statements.Add(new CodeAttachEventStatement(
                new CodeEventReferenceExpression(buttonField, nameof(Forms.Control.Click)),
                new CodeDelegateCreateExpression(
                    new CodeTypeReference(typeof(System.EventHandler)),
                    @this,
                    "button1_Click")));
            method.Statements.Add(new CodeExpressionStatement(new CodeMethodInvokeExpression(
                new CodePropertyReferenceExpression(panelField, nameof(Forms.Control.Controls)),
                "Add",
                buttonField)));
            method.Statements.Add(new CodeExpressionStatement(new CodeMethodInvokeExpression(
                new CodePropertyReferenceExpression(@this, nameof(Forms.Control.Controls)),
                "Add",
                panelField)));

            return method;
        }
    }

    private sealed class ReflectionLocalizedCodeDomDesignerLoader : TestCodeDomDesignerLoader
    {
        protected override void Initialize()
        {
            var host = Assert.IsAssignableFrom<IDesignerLoaderHost>(GetService(typeof(IDesignerLoaderHost)));
            var manager = Assert.IsAssignableFrom<IDesignerSerializationManager>(
                GetService(typeof(IDesignerSerializationManager)));
            manager.AddSerializationProvider(new CodeDomLocalizationProvider(
                host,
                CodeDomLocalizationModel.PropertyReflection));
        }
    }

    private sealed class ResourceApplyCodeDomDesignerLoader : CodeDomDesignerLoader
    {
        protected override CodeDomProvider? CodeDomProvider => null;

        protected override ITypeResolutionService? TypeResolutionService { get; } = new ResourceTypeResolutionService();

        protected override CodeCompileUnit Parse()
        {
            CodeTypeDeclaration codeClass = new("ResourceApplyControl");
            codeClass.BaseTypes.Add(typeof(Forms.UserControl).FullName!);
            codeClass.Members.Add(new CodeMemberField(typeof(Forms.Button), "button1"));
            codeClass.Members.Add(CreateInitializeComponent());

            CodeNamespace codeNamespace = new("PortableDesignerSmoke");
            codeNamespace.Types.Add(codeClass);

            CodeCompileUnit unit = new();
            unit.Namespaces.Add(codeNamespace);
            return unit;
        }

        protected override void Write(CodeCompileUnit unit)
        {
        }

        private static CodeMemberMethod CreateInitializeComponent()
        {
            CodeMemberMethod method = new()
            {
                Name = "InitializeComponent"
            };

            CodeThisReferenceExpression @this = new();
            CodeVariableReferenceExpression resources = new("resources");
            CodeFieldReferenceExpression buttonField = new(@this, "button1");

            method.Statements.Add(new CodeVariableDeclarationStatement(
                typeof(ComponentResourceManager),
                "resources",
                new CodeObjectCreateExpression(
                    typeof(ComponentResourceManager),
                    new CodeTypeOfExpression(typeof(LocalizableWinFormsResourceTarget).FullName!))));
            method.Statements.Add(new CodeAssignStatement(
                buttonField,
                new CodeObjectCreateExpression(typeof(Forms.Button))));
            method.Statements.Add(new CodeExpressionStatement(new CodeMethodInvokeExpression(
                resources,
                nameof(ComponentResourceManager.ApplyResources),
                buttonField,
                new CodePrimitiveExpression("button1"))));
            method.Statements.Add(new CodeExpressionStatement(new CodeMethodInvokeExpression(
                resources,
                nameof(ComponentResourceManager.ApplyResources),
                @this,
                new CodePrimitiveExpression("$this"))));
            method.Statements.Add(new CodeExpressionStatement(new CodeMethodInvokeExpression(
                new CodePropertyReferenceExpression(@this, nameof(Forms.Control.Controls)),
                "Add",
                buttonField)));

            return method;
        }
    }

    private sealed class ResourceTypeResolutionService : ITypeResolutionService
    {
        public Assembly? GetAssembly(AssemblyName name)
        {
            return GetAssembly(name, throwOnError: false);
        }

        public Assembly? GetAssembly(AssemblyName name, bool throwOnError)
        {
            try
            {
                return Assembly.Load(name);
            }
            catch
            {
                if (throwOnError)
                    throw;
                return null;
            }
        }

        public string? GetPathOfAssembly(AssemblyName name)
        {
            return null;
        }

        public Type? GetType(string name)
        {
            return GetType(name, throwOnError: false, ignoreCase: false);
        }

        public Type? GetType(string name, bool throwOnError)
        {
            return GetType(name, throwOnError, ignoreCase: false);
        }

        public Type? GetType(string name, bool throwOnError, bool ignoreCase)
        {
            StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            Type resourceType = typeof(LocalizableWinFormsResourceTarget);
            if (string.Equals(name, resourceType.FullName, comparison) || string.Equals(name, resourceType.Name, comparison))
                return resourceType;

            Type? resolved = Type.GetType(name, throwOnError: false, ignoreCase: ignoreCase);
            if (resolved is null && throwOnError)
                throw new TypeLoadException(name);

            return resolved;
        }

        public void ReferenceAssembly(AssemblyName name)
        {
        }
    }

    private sealed class TestEventBindingService : EventBindingService
    {
        public TestEventBindingService(IServiceProvider provider)
            : base(provider)
        {
        }

        protected override string CreateUniqueMethodName(IComponent component, EventDescriptor e)
        {
            return "Test_" + e.Name;
        }

        protected override ICollection GetCompatibleMethods(EventDescriptor e)
        {
            return Array.Empty<string>();
        }

        protected override bool ShowCode()
        {
            return false;
        }

        protected override bool ShowCode(int lineNumber)
        {
            return false;
        }

        protected override bool ShowCode(IComponent component, EventDescriptor e, string methodName)
        {
            return false;
        }
    }

    private sealed class NavigatingEventBindingService : EventBindingService
    {
        public NavigatingEventBindingService(IServiceProvider provider)
            : base(provider)
        {
        }

        public string? ShownMethodName { get; private set; }

        protected override string CreateUniqueMethodName(IComponent component, EventDescriptor e)
        {
            return component.Site?.Name + "_" + e.Name;
        }

        protected override ICollection GetCompatibleMethods(EventDescriptor e)
        {
            return Array.Empty<string>();
        }

        protected override bool ShowCode()
        {
            return true;
        }

        protected override bool ShowCode(int lineNumber)
        {
            return true;
        }

        protected override bool ShowCode(IComponent component, EventDescriptor e, string methodName)
        {
            ShownMethodName = methodName;
            return true;
        }
    }

    private sealed class ToolStripContainerCodeDomDesignerLoader : CodeDomDesignerLoader
    {
        public CodeCompileUnit? WrittenUnit { get; private set; }

        protected override CodeDomProvider? CodeDomProvider => null;

        protected override ITypeResolutionService? TypeResolutionService => null;

        protected override CodeCompileUnit Parse()
        {
            CodeTypeDeclaration codeClass = new("SampleControl");
            codeClass.BaseTypes.Add(typeof(Forms.UserControl).FullName!);
            codeClass.Members.Add(new CodeMemberField(typeof(Forms.ToolStripContainer), "tscMain"));
            codeClass.Members.Add(new CodeMemberField(typeof(Forms.ToolStrip), "toolStrip1"));
            codeClass.Members.Add(CreateInitializeComponent());

            CodeNamespace codeNamespace = new("PortableDesignerSmoke");
            codeNamespace.Types.Add(codeClass);

            CodeCompileUnit unit = new();
            unit.Namespaces.Add(codeNamespace);
            return unit;
        }

        protected override void Write(CodeCompileUnit unit)
        {
            WrittenUnit = unit;
        }

        private static CodeMemberMethod CreateInitializeComponent()
        {
            CodeMemberMethod method = new()
            {
                Name = "InitializeComponent"
            };

            CodeThisReferenceExpression @this = new();
            CodeFieldReferenceExpression containerField = new(@this, "tscMain");
            CodeFieldReferenceExpression toolStripField = new(@this, "toolStrip1");

            method.Statements.Add(new CodeAssignStatement(
                containerField,
                new CodeObjectCreateExpression(typeof(Forms.ToolStripContainer))));
            method.Statements.Add(new CodeAssignStatement(
                toolStripField,
                new CodeObjectCreateExpression(typeof(Forms.ToolStrip))));
            method.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(containerField, nameof(Forms.Control.Text)),
                new CodePrimitiveExpression("Container")));
            method.Statements.Add(new CodeAssignStatement(
                new CodePropertyReferenceExpression(toolStripField, nameof(Forms.Control.Text)),
                new CodePrimitiveExpression("Strip")));
            method.Statements.Add(new CodeExpressionStatement(new CodeMethodInvokeExpression(
                new CodePropertyReferenceExpression(
                    new CodePropertyReferenceExpression(containerField, nameof(Forms.ToolStripContainer.BottomToolStripPanel)),
                    nameof(Forms.Control.Controls)),
                "Add",
                toolStripField)));
            method.Statements.Add(new CodeExpressionStatement(new CodeMethodInvokeExpression(
                new CodePropertyReferenceExpression(@this, nameof(Forms.Control.Controls)),
                "Add",
                containerField)));

            return method;
        }
    }
}

internal sealed class LocalizableWinFormsResourceTarget
{
}

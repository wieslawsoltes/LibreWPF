using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Windows.Forms;

namespace System.ComponentModel.Design
{
    public class DesignSurface : IServiceProvider, IDisposable
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly Collection<Exception> _loadErrors = new();
        private PortableDesignerHost? _host;
        private DesignerLoader? _loader;

        public DesignSurface()
        {
        }

        public DesignSurface(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public event EventHandler? Flushed;
        public event EventHandler<LoadedEventArgs>? Loaded;
        public event EventHandler? Loading;
        public event EventHandler? Unloaded;
        public event EventHandler? Unloading;

        public DesignSurfaceManager? Manager { get; internal set; }

        public bool IsLoaded { get; private set; }

        public ICollection LoadErrors => _loadErrors;

        public object View { get; private set; } = new Panel();

        public void BeginLoad(DesignerLoader loader)
        {
            ArgumentNullException.ThrowIfNull(loader);

            Loading?.Invoke(this, EventArgs.Empty);
            _loadErrors.Clear();
            _loader = loader;

            _host = new PortableDesignerHost(_serviceProvider);
            try
            {
                loader.BeginLoad(_host);

                if (!_host.LoadCompleted)
                {
                    _host.EndLoad(_host.RootComponentClassName, true, Array.Empty<object>());
                }

                _host.EnsureRootComponent();

                foreach (object? error in _host.LoadErrors)
                {
                    if (error is Exception exception)
                    {
                        _loadErrors.Add(exception);
                    }
                    else if (error is not null)
                    {
                        _loadErrors.Add(new InvalidOperationException(error.ToString()));
                    }
                }

                IsLoaded = _host.LoadSucceeded && _loadErrors.Count == 0;
                View = _host.RootComponent as Control ?? new Panel();
            }
            catch (Exception ex)
            {
                _loadErrors.Add(ex);
                IsLoaded = false;
            }

            Loaded?.Invoke(this, new LoadedEventArgs(IsLoaded, _loadErrors));
        }

        public void Dispose()
        {
            Unloading?.Invoke(this, EventArgs.Empty);
            _loader?.Dispose();
            _loader = null;
            Unloaded?.Invoke(this, EventArgs.Empty);
        }

        public void Flush()
        {
            _loader?.Flush();
            Flushed?.Invoke(this, EventArgs.Empty);
        }

        public object? GetService(Type serviceType)
        {
            if (_host is not null)
            {
                object? service = _host.GetService(serviceType);
                if (service is not null)
                    return service;
            }

            return _serviceProvider?.GetService(serviceType);
        }
    }

    internal sealed class PortableDesignerHost : IDesignerLoaderHost, IDesignerLoaderHost2, IDesignerSerializationManager, IComponentChangeService, ISelectionService, INameCreationService
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly Container _container = new();
        private readonly Dictionary<string, object> _instances = new(StringComparer.Ordinal);
        private readonly Dictionary<object, string> _names = new();
        private readonly Dictionary<Type, object> _services = new();
        private readonly Dictionary<Type, ServiceCreatorCallback> _serviceCreators = new();
        private readonly List<IDesignerSerializationProvider> _serializationProviders = new();
        private readonly List<object> _selection = new();
        private readonly PortableEventBindingService _eventBindingService;
        private int _transactionDepth;
        private string _transactionDescription = string.Empty;

        public PortableDesignerHost(IServiceProvider? serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _eventBindingService = new PortableEventBindingService(this);
        }

        public event EventHandler? Activated;
        public event EventHandler? Deactivated;
        public event EventHandler? LoadComplete;
        public event DesignerTransactionCloseEventHandler? TransactionClosed;
        public event DesignerTransactionCloseEventHandler? TransactionClosing;
        public event EventHandler? TransactionOpened;
        public event EventHandler? TransactionOpening;
        public event ComponentEventHandler? ComponentAdded;
        public event ComponentEventHandler? ComponentAdding;
        public event ComponentChangedEventHandler? ComponentChanged;
        public event ComponentChangingEventHandler? ComponentChanging;
        public event ComponentEventHandler? ComponentRemoved;
        public event ComponentEventHandler? ComponentRemoving;
        public event ComponentRenameEventHandler? ComponentRename;
        public event EventHandler? SelectionChanged;
        public event EventHandler? SelectionChanging;
        public event ResolveNameEventHandler? ResolveName;
        public event EventHandler? SerializationComplete;

        public bool LoadCompleted { get; private set; }

        public ICollection LoadErrors { get; private set; } = Array.Empty<object>();

        public bool LoadSucceeded { get; private set; }

        public bool CanReloadWithErrors { get; set; }

        public bool IgnoreErrorsDuringReload { get; set; }

        public IContainer Container => _container;

        public bool InTransaction => _transactionDepth > 0;

        public bool Loading => !LoadCompleted;

        public IComponent? RootComponent { get; private set; }

        public string RootComponentClassName { get; private set; } = typeof(Panel).FullName!;

        public string TransactionDescription => _transactionDescription;

        public ContextStack Context { get; } = new();

        public PropertyDescriptorCollection Properties => PropertyDescriptorCollection.Empty;

        public object? PrimarySelection => _selection.Count > 0 ? _selection[0] : RootComponent;

        public int SelectionCount => _selection.Count;

        public void EnsureRootComponent()
        {
            if (RootComponent is not null)
                return;

            RootComponent = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.White
            };
            _container.Add(RootComponent, "Root");
            SetName(RootComponent, "Root");
            SetSelectedComponents(new object[] { RootComponent });
        }

        public void Activate()
        {
            Activated?.Invoke(this, EventArgs.Empty);
        }

        public IComponent CreateComponent(Type componentClass)
        {
            return CreateComponent(componentClass, null);
        }

        public IComponent CreateComponent(Type componentClass, string? name)
        {
            ArgumentNullException.ThrowIfNull(componentClass);
            object? instance = Activator.CreateInstance(componentClass);
            if (instance is not IComponent component)
                throw new InvalidOperationException(componentClass.FullName + " is not a component.");

            string? componentName = string.IsNullOrWhiteSpace(name)
                ? CreateName(_container, componentClass)
                : name;
            ValidateName(componentName);

            ComponentAdding?.Invoke(this, new ComponentEventArgs(component));
            _container.Add(component, componentName);
            SetName(component, componentName);
            if (RootComponent is null)
            {
                RootComponent = component;
                RootComponentClassName = componentClass.FullName ?? componentClass.Name;
            }

            ComponentAdded?.Invoke(this, new ComponentEventArgs(component));
            return component;
        }

        public DesignerTransaction CreateTransaction()
        {
            return CreateTransaction(string.Empty);
        }

        public DesignerTransaction CreateTransaction(string description)
        {
            _transactionDepth++;
            _transactionDescription = description ?? string.Empty;
            TransactionOpening?.Invoke(this, EventArgs.Empty);
            TransactionOpened?.Invoke(this, EventArgs.Empty);
            return new PortableDesignerTransaction(_transactionDescription, CloseTransaction);
        }

        public void DestroyComponent(IComponent component)
        {
            if (component is null)
                return;

            ComponentRemoving?.Invoke(this, new ComponentEventArgs(component));
            if (component is Control control && control.Parent is not null)
            {
                control.Parent.Controls.Remove(control);
            }

            _container.Remove(component);
            RemoveName(component);
            _eventBindingService.RemoveComponent(component);
            if (ReferenceEquals(RootComponent, component))
            {
                RootComponent = null;
            }

            component.Dispose();
            ComponentRemoved?.Invoke(this, new ComponentEventArgs(component));
        }

        public void EndLoad(string baseClassName, bool successful, ICollection? errorCollection)
        {
            RootComponentClassName = string.IsNullOrEmpty(baseClassName) ? RootComponentClassName : baseClassName;
            LoadSucceeded = successful;
            LoadErrors = errorCollection ?? Array.Empty<object>();
            LoadCompleted = true;
            LoadComplete?.Invoke(this, EventArgs.Empty);
        }

        public IDesigner? GetDesigner(IComponent component)
        {
            return null;
        }

        public Type? GetType(string typeName)
        {
            if (_serviceProvider?.GetService(typeof(ITypeResolutionService)) is ITypeResolutionService resolver)
            {
                Type? resolved = resolver.GetType(typeName, false);
                if (resolved is not null)
                    return resolved;
            }

            return PortableWinFormsTypeResolver.Resolve(
                _serviceProvider?.GetService(typeof(ITypeResolutionService)) as ITypeResolutionService,
                typeName);
        }

        public void Reload()
        {
        }

        public object? GetService(Type serviceType)
        {
            if (_services.TryGetValue(serviceType, out object? service))
                return service;
            if (_serviceCreators.TryGetValue(serviceType, out ServiceCreatorCallback? creator))
            {
                service = creator(this, serviceType);
                if (service is not null)
                    _services[serviceType] = service;
                return service;
            }

            if (serviceType == typeof(IDesignerHost) || serviceType == typeof(IDesignerLoaderHost) || serviceType == typeof(IDesignerLoaderHost2))
                return this;
            if (serviceType == typeof(IContainer))
                return _container;
            if (serviceType == typeof(IComponentChangeService))
                return this;
            if (serviceType == typeof(ISelectionService))
                return this;
            if (serviceType == typeof(IDesignerSerializationManager))
                return this;
            if (serviceType == typeof(IEventBindingService))
                return _serviceProvider?.GetService(serviceType) ?? _eventBindingService;
            if (serviceType == typeof(INameCreationService))
                return _serviceProvider?.GetService(serviceType) ?? this;

            return _serviceProvider?.GetService(serviceType);
        }

        public void AddService(Type serviceType, ServiceCreatorCallback callback)
        {
            AddService(serviceType, callback, false);
        }

        public void AddService(Type serviceType, ServiceCreatorCallback callback, bool promote)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            ArgumentNullException.ThrowIfNull(callback);

            if (promote && _serviceProvider is IServiceContainer parent)
            {
                parent.AddService(serviceType, callback, true);
                return;
            }

            _serviceCreators[serviceType] = callback;
            _services.Remove(serviceType);
        }

        public void AddService(Type serviceType, object serviceInstance)
        {
            AddService(serviceType, serviceInstance, false);
        }

        public void AddService(Type serviceType, object serviceInstance, bool promote)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            ArgumentNullException.ThrowIfNull(serviceInstance);

            if (promote && _serviceProvider is IServiceContainer parent)
            {
                parent.AddService(serviceType, serviceInstance, true);
                return;
            }

            _services[serviceType] = serviceInstance;
            _serviceCreators.Remove(serviceType);
        }

        public void RemoveService(Type serviceType)
        {
            RemoveService(serviceType, false);
        }

        public void RemoveService(Type serviceType, bool promote)
        {
            ArgumentNullException.ThrowIfNull(serviceType);

            if (promote && _serviceProvider is IServiceContainer parent)
            {
                parent.RemoveService(serviceType, true);
                return;
            }

            _services.Remove(serviceType);
            _serviceCreators.Remove(serviceType);
        }

        public void AddSerializationProvider(IDesignerSerializationProvider provider)
        {
            if (provider is not null && !_serializationProviders.Contains(provider))
                _serializationProviders.Add(provider);
        }

        public object? CreateInstance(Type type, ICollection? arguments, string? name, bool addToContainer)
        {
            object?[]? argumentArray = null;
            if (arguments is not null)
            {
                argumentArray = new object?[arguments.Count];
                arguments.CopyTo(argumentArray, 0);
            }

            object? instance = Activator.CreateInstance(type, argumentArray ?? Array.Empty<object>());
            if (instance is null)
                return null;

            if (!string.IsNullOrEmpty(name))
                SetName(instance, name);
            if (addToContainer && instance is IComponent component)
                _container.Add(component, name);
            return instance;
        }

        public object? GetInstance(string name)
        {
            if (name is not null && _instances.TryGetValue(name, out object? instance))
                return instance;

            ResolveNameEventArgs args = new(name);
            ResolveName?.Invoke(this, args);
            return args.Value;
        }

        public string? GetName(object value)
        {
            return value is not null && _names.TryGetValue(value, out string? name) ? name : null;
        }

        public object? GetSerializer(Type objectType, Type serializerType)
        {
            for (int i = _serializationProviders.Count - 1; i >= 0; i--)
            {
                object? serializer = _serializationProviders[i].GetSerializer(this, null, objectType, serializerType);
                if (serializer is not null)
                    return serializer;
            }

            return null;
        }

        internal CodeDomLocalizationModel GetLocalizationModel()
        {
            for (int i = _serializationProviders.Count - 1; i >= 0; i--)
            {
                if (_serializationProviders[i] is CodeDomLocalizationProvider localizationProvider)
                    return localizationProvider.Model;
            }

            return CodeDomLocalizationModel.None;
        }

        Type? IDesignerSerializationManager.GetType(string typeName)
        {
            return GetType(typeName);
        }

        public void RemoveSerializationProvider(IDesignerSerializationProvider provider)
        {
            _serializationProviders.Remove(provider);
        }

        public void ReportError(object errorInformation)
        {
            if (errorInformation is Exception exception)
            {
                LoadErrors = new object[] { exception };
            }
            else if (errorInformation is not null)
            {
                LoadErrors = new object[] { new InvalidOperationException(errorInformation.ToString()) };
            }
        }

        public void SetName(object instance, string name)
        {
            if (instance is null || string.IsNullOrEmpty(name))
                return;

            _instances[name] = instance;
            _names[instance] = name;
            if (instance is IComponent component && component.Site is not null)
                component.Site.Name = name;
        }

        private void RemoveName(object instance)
        {
            if (instance is null)
                return;

            if (_names.Remove(instance, out string? name))
            {
                _instances.Remove(name);
            }
        }

        public string CreateName(IContainer container, Type dataType)
        {
            ArgumentNullException.ThrowIfNull(dataType);

            string baseName = dataType.Name;
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "component";
            baseName = char.ToLowerInvariant(baseName[0]) + baseName[1..];

            int index = 1;
            string candidate;
            do
            {
                candidate = baseName + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                index++;
            }
            while (!IsNameAvailable(container, candidate));

            return candidate;
        }

        public bool IsValidName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                !(char.IsLetter(name[0]) || name[0] == '_'))
            {
                return false;
            }

            for (int i = 1; i < name.Length; i++)
            {
                char ch = name[i];
                if (!char.IsLetterOrDigit(ch) && ch != '_')
                    return false;
            }

            return true;
        }

        public void ValidateName(string name)
        {
            if (!IsValidName(name))
                throw new ArgumentException("Invalid component name '" + name + "'.", nameof(name));
        }

        private bool IsNameAvailable(IContainer? container, string name)
        {
            if (_instances.ContainsKey(name))
                return false;

            return container?.Components[name] is null;
        }

        public void OnComponentChanged(object component, MemberDescriptor? member, object? oldValue, object? newValue)
        {
            ComponentChanged?.Invoke(this, new ComponentChangedEventArgs(component, member, oldValue, newValue));
        }

        public void OnComponentChanging(object component, MemberDescriptor? member)
        {
            ComponentChanging?.Invoke(this, new ComponentChangingEventArgs(component, member));
        }

        public bool GetComponentSelected(object component)
        {
            return _selection.Contains(component);
        }

        public ICollection GetSelectedComponents()
        {
            return _selection.ToArray();
        }

        public void SetSelectedComponents(ICollection components)
        {
            SetSelectedComponents(components, SelectionTypes.Replace);
        }

        public void SetSelectedComponents(ICollection components, SelectionTypes selectionType)
        {
            SelectionChanging?.Invoke(this, EventArgs.Empty);
            if ((selectionType & SelectionTypes.Replace) == SelectionTypes.Replace)
                _selection.Clear();
            foreach (object? component in components)
            {
                if (component is not null && !_selection.Contains(component))
                    _selection.Add(component);
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CloseTransaction(bool commit)
        {
            if (_transactionDepth > 0)
                _transactionDepth--;
            TransactionClosing?.Invoke(this, new DesignerTransactionCloseEventArgs(commit));
            TransactionClosed?.Invoke(this, new DesignerTransactionCloseEventArgs(commit));
            if (_transactionDepth == 0)
                _transactionDescription = string.Empty;
        }

        private sealed class PortableDesignerTransaction : DesignerTransaction
        {
            private readonly Action<bool> _close;

            public PortableDesignerTransaction(string description, Action<bool> close)
                : base(description)
            {
                _close = close;
            }

            protected override void OnCancel()
            {
                _close(false);
            }

            protected override void OnCommit()
            {
                _close(true);
            }
        }
    }

    public class DesignSurfaceManager : IServiceProvider, IDisposable
    {
        private readonly List<DesignSurface> _surfaces = new();

        public DesignSurface? ActiveDesignSurface { get; set; }

        public DesignSurface CreateDesignSurface(IServiceProvider serviceProvider)
        {
            var surface = new DesignSurface(serviceProvider)
            {
                Manager = this
            };
            _surfaces.Add(surface);
            return surface;
        }

        public void Dispose()
        {
            foreach (DesignSurface surface in _surfaces.ToArray())
            {
                surface.Dispose();
            }

            _surfaces.Clear();
            ActiveDesignSurface = null;
        }

        public object? GetService(Type serviceType)
        {
            return ActiveDesignSurface?.GetService(serviceType);
        }
    }

    public sealed class LoadedEventArgs : EventArgs
    {
        public LoadedEventArgs(bool hasSucceeded, ICollection? errors)
        {
            HasSucceeded = hasSucceeded;
            Errors = errors ?? Array.Empty<object>();
        }

        public ICollection Errors { get; }

        public bool HasSucceeded { get; }
    }

    public abstract class EventBindingService : IEventBindingService
    {
        private readonly IServiceProvider _provider;
        private readonly Dictionary<IComponent, Dictionary<string, string>> _eventMethods = new();

        protected EventBindingService(IServiceProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        protected abstract string CreateUniqueMethodName(IComponent component, EventDescriptor e);

        protected virtual void FreeMethod(IComponent component, EventDescriptor e, string methodName)
        {
        }

        protected abstract ICollection GetCompatibleMethods(EventDescriptor e);

        protected object? GetService(Type serviceType)
        {
            return _provider.GetService(serviceType);
        }

        protected abstract bool ShowCode();

        protected abstract bool ShowCode(int lineNumber);

        protected abstract bool ShowCode(IComponent component, EventDescriptor e, string methodName);

        protected virtual void UseMethod(IComponent component, EventDescriptor e, string methodName)
        {
        }

        protected virtual void ValidateMethodName(string methodName)
        {
        }

        string IEventBindingService.CreateUniqueMethodName(IComponent component, EventDescriptor e)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(e);
            return CreateUniqueMethodName(component, e);
        }

        ICollection IEventBindingService.GetCompatibleMethods(EventDescriptor e)
        {
            ArgumentNullException.ThrowIfNull(e);
            return GetCompatibleMethods(e);
        }

        EventDescriptor? IEventBindingService.GetEvent(PropertyDescriptor property)
        {
            return property is EventPropertyDescriptor eventProperty ? eventProperty.Event : null;
        }

        PropertyDescriptorCollection IEventBindingService.GetEventProperties(EventDescriptorCollection events)
        {
            ArgumentNullException.ThrowIfNull(events);
            PropertyDescriptor[] properties = new PropertyDescriptor[events.Count];
            for (int i = 0; i < events.Count; i++)
            {
                properties[i] = new EventPropertyDescriptor(events[i]!, this);
            }

            return new PropertyDescriptorCollection(properties);
        }

        PropertyDescriptor IEventBindingService.GetEventProperty(EventDescriptor e)
        {
            ArgumentNullException.ThrowIfNull(e);
            return new EventPropertyDescriptor(e, this);
        }

        bool IEventBindingService.ShowCode()
        {
            return ShowCode();
        }

        bool IEventBindingService.ShowCode(int lineNumber)
        {
            return ShowCode(lineNumber);
        }

        bool IEventBindingService.ShowCode(IComponent component, EventDescriptor e)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(e);

            string? methodName = GetEventMethodName(component, e);
            bool createdMethodBinding = false;
            if (string.IsNullOrWhiteSpace(methodName))
            {
                methodName = CreateUniqueMethodName(component, e);
                if (string.IsNullOrWhiteSpace(methodName))
                    return false;

                SetEventMethodName(component, e, methodName);
                createdMethodBinding = true;
            }

            bool shown = ShowCode(component, e, methodName);
            if (!shown && createdMethodBinding)
                SetEventMethodName(component, e, null);

            return shown;
        }

        private string? GetEventMethodName(IComponent component, EventDescriptor e)
        {
            return _eventMethods.TryGetValue(component, out Dictionary<string, string>? componentEvents)
                && componentEvents.TryGetValue(e.Name, out string? methodName)
                    ? methodName
                    : null;
        }

        internal PortableEventBinding[] GetEventBindings()
        {
            List<PortableEventBinding> bindings = new();
            foreach (KeyValuePair<IComponent, Dictionary<string, string>> componentEntry in _eventMethods)
            {
                EventDescriptorCollection events = TypeDescriptor.GetEvents(componentEntry.Key);
                foreach (KeyValuePair<string, string> eventEntry in componentEntry.Value)
                {
                    EventDescriptor? eventDescriptor = events.Find(eventEntry.Key, false);
                    if (eventDescriptor is not null && !string.IsNullOrWhiteSpace(eventEntry.Value))
                    {
                        bindings.Add(new PortableEventBinding(componentEntry.Key, eventDescriptor, eventEntry.Value));
                    }
                }
            }

            return bindings.ToArray();
        }

        internal void SetEventMethodName(IComponent component, string eventName, string? methodName)
        {
            if (string.IsNullOrWhiteSpace(eventName))
                return;

            EventDescriptor? eventDescriptor = TypeDescriptor.GetEvents(component).Find(eventName, false);
            if (eventDescriptor is not null)
                SetEventMethodName(component, eventDescriptor, methodName);
        }

        internal void RemoveComponent(IComponent component)
        {
            _eventMethods.Remove(component);
        }

        private void SetEventMethodName(IComponent component, EventDescriptor e, string? methodName)
        {
            string? oldMethodName = GetEventMethodName(component, e);
            if (string.Equals(oldMethodName, methodName, StringComparison.Ordinal))
                return;

            if (!string.IsNullOrEmpty(oldMethodName))
                FreeMethod(component, e, oldMethodName);

            if (string.IsNullOrEmpty(methodName))
            {
                if (_eventMethods.TryGetValue(component, out Dictionary<string, string>? componentEvents))
                {
                    componentEvents.Remove(e.Name);
                    if (componentEvents.Count == 0)
                        _eventMethods.Remove(component);
                }
                return;
            }

            ValidateMethodName(methodName);
            if (!_eventMethods.TryGetValue(component, out Dictionary<string, string>? events))
            {
                events = new Dictionary<string, string>(StringComparer.Ordinal);
                _eventMethods.Add(component, events);
            }

            events[e.Name] = methodName;
            UseMethod(component, e, methodName);
        }

        private sealed class EventPropertyDescriptor : PropertyDescriptor
        {
            private readonly EventBindingService _owner;

            public EventPropertyDescriptor(EventDescriptor @event, EventBindingService owner)
                : base(@event.Name, null)
            {
                Event = @event;
                _owner = owner;
            }

            public EventDescriptor Event { get; }

            public override Type ComponentType => Event.ComponentType!;

            public override bool IsReadOnly => false;

            public override Type PropertyType => typeof(string);

            public override bool CanResetValue(object component)
            {
                return component is IComponent target && _owner.GetEventMethodName(target, Event) is not null;
            }

            public override object? GetValue(object? component)
            {
                return component is IComponent target ? _owner.GetEventMethodName(target, Event) : null;
            }

            public override void ResetValue(object component)
            {
                if (component is IComponent target)
                    _owner.SetEventMethodName(target, Event, null);
            }

            public override void SetValue(object? component, object? value)
            {
                if (component is IComponent target)
                    _owner.SetEventMethodName(target, Event, value as string);
            }

            public override bool ShouldSerializeValue(object component)
            {
                return CanResetValue(component);
            }
        }
    }

    internal readonly struct PortableEventBinding
    {
        public PortableEventBinding(IComponent component, EventDescriptor eventDescriptor, string methodName)
        {
            Component = component;
            Event = eventDescriptor;
            MethodName = methodName;
        }

        public IComponent Component { get; }

        public EventDescriptor Event { get; }

        public string MethodName { get; }
    }

    internal sealed class PortableEventBindingService : EventBindingService
    {
        public PortableEventBindingService(IServiceProvider provider)
            : base(provider)
        {
        }

        protected override string CreateUniqueMethodName(IComponent component, EventDescriptor e)
        {
            string? componentName = component.Site?.Name;
            if (string.IsNullOrWhiteSpace(componentName))
                componentName = component.GetType().Name;

            return componentName + "_" + e.Name;
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

        protected override void ValidateMethodName(string methodName)
        {
            if (string.IsNullOrWhiteSpace(methodName))
                throw new ArgumentException("Method name must not be empty.", nameof(methodName));
        }
    }

    public class MenuCommandService : IMenuCommandService
    {
        private readonly Dictionary<CommandID, MenuCommand> _commands = new();
        private readonly IServiceProvider? _serviceProvider;
        private readonly DesignerVerbCollection _verbs = new();

        public MenuCommandService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public virtual DesignerVerbCollection Verbs => _verbs;

        public virtual void AddCommand(MenuCommand command)
        {
            ArgumentNullException.ThrowIfNull(command);
            _commands[command.CommandID] = command;
            if (command is DesignerVerb verb && !_verbs.Contains(verb))
            {
                _verbs.Add(verb);
            }
        }

        public virtual void AddVerb(DesignerVerb verb)
        {
            ArgumentNullException.ThrowIfNull(verb);
            _verbs.Add(verb);
            AddCommand(verb);
        }

        public virtual MenuCommand? FindCommand(CommandID commandID)
        {
            _commands.TryGetValue(commandID, out MenuCommand? command);
            return command;
        }

        public virtual bool GlobalInvoke(CommandID commandID)
        {
            MenuCommand? command = FindCommand(commandID);
            if (command is null || !command.Enabled)
            {
                return false;
            }

            command.Invoke();
            return true;
        }

        public virtual void RemoveCommand(MenuCommand command)
        {
            if (command is null)
            {
                return;
            }

            _commands.Remove(command.CommandID);
        }

        public virtual void RemoveVerb(DesignerVerb verb)
        {
            if (verb is null)
            {
                return;
            }

            _verbs.Remove(verb);
            RemoveCommand(verb);
        }

        public virtual void ShowContextMenu(CommandID menuID, int x, int y)
        {
        }

        protected object? GetService(Type serviceType)
        {
            return _serviceProvider?.GetService(serviceType);
        }
    }

    public abstract class UndoEngine : IDisposable
    {
        protected UndoEngine(IServiceProvider provider)
        {
            Provider = provider;
        }

        protected IServiceProvider Provider { get; }

        public virtual bool Enabled
        {
            get => true;
            set { }
        }

        protected abstract void AddUndoUnit(UndoUnit unit);

        public virtual void Dispose()
        {
        }

        protected object? GetRequiredService(Type serviceType)
        {
            return Provider.GetService(serviceType);
        }

        public abstract class UndoUnit
        {
            protected UndoUnit(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public abstract void Undo();
        }
    }
}

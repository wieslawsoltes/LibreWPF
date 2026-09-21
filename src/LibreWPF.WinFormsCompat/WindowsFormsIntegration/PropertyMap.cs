using System.Collections;
using System.Collections.Generic;

namespace System.Windows.Forms.Integration;

public delegate void PropertyTranslator(object host, string propertyName, object value);

public sealed class ChildChangedEventArgs : EventArgs
{
    public ChildChangedEventArgs(object? previousChild)
    {
        PreviousChild = previousChild;
    }

    public object? PreviousChild { get; }
}

public class IntegrationExceptionEventArgs : EventArgs
{
    public IntegrationExceptionEventArgs(bool throwException, Exception? exception)
    {
        ThrowException = throwException;
        Exception = exception;
    }

    public Exception? Exception { get; }

    public bool ThrowException { get; set; }
}

public sealed class LayoutExceptionEventArgs : IntegrationExceptionEventArgs
{
    public LayoutExceptionEventArgs(Exception? exception)
        : base(false, exception)
    {
    }
}

public sealed class PropertyMappingExceptionEventArgs : IntegrationExceptionEventArgs
{
    public PropertyMappingExceptionEventArgs(Exception? exception, string propertyName, object? propertyValue)
        : base(false, exception)
    {
        PropertyName = propertyName;
        PropertyValue = propertyValue;
    }

    public string PropertyName { get; }

    public object? PropertyValue { get; }
}

public class PropertyMap
{
    private readonly Dictionary<string, PropertyTranslator> _translators = new(StringComparer.Ordinal);
    private readonly object? _sourceObject;

    public PropertyMap()
    {
    }

    public PropertyMap(object source)
    {
        _sourceObject = source;
    }

    public event EventHandler<PropertyMappingExceptionEventArgs>? PropertyMappingError;

    public PropertyTranslator this[string propertyName]
    {
        get => _translators[propertyName];
        set => _translators[propertyName] = value;
    }

    public ICollection Keys => _translators.Keys;

    public ICollection Values => _translators.Values;

    protected object? SourceObject => _sourceObject;

    public void Add(string propertyName, PropertyTranslator translator)
    {
        _translators.Add(propertyName, translator);
    }

    public void Apply(string propertyName)
    {
        if (_sourceObject is null || !_translators.TryGetValue(propertyName, out PropertyTranslator? translator))
        {
            return;
        }

        try
        {
            translator(_sourceObject, propertyName, null!);
        }
        catch (Exception ex)
        {
            PropertyMappingError?.Invoke(this, new PropertyMappingExceptionEventArgs(ex, propertyName, null));
        }
    }

    public void ApplyAll()
    {
        foreach (string key in _translators.Keys)
        {
            Apply(key);
        }
    }

    public void Clear()
    {
        _translators.Clear();
    }

    public bool Contains(string propertyName)
    {
        return _translators.ContainsKey(propertyName);
    }

    public void Remove(string propertyName)
    {
        _translators.Remove(propertyName);
    }

    public void Reset(string propertyName)
    {
        _translators.Remove(propertyName);
    }

    public void ResetAll()
    {
        _translators.Clear();
    }
}

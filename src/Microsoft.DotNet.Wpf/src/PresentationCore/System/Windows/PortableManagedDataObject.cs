// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Text.Json;

namespace System.Windows;

internal sealed class PortableManagedDataObject : ITypedDataObject
{
    private readonly Dictionary<string, Entry> _data = new Dictionary<string, Entry>(StringComparer.Ordinal);

    public object? GetData(string format) => GetData(format, autoConvert: true);

    public object? GetData(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return GetData(format.FullName ?? format.Name, autoConvert: true);
    }

    public object? GetData(string format, bool autoConvert)
    {
        ArgumentException.ThrowIfNullOrEmpty(format);
        return TryGetEntry(format, autoConvert, out Entry entry) ? entry.Data : null;
    }

    public bool GetDataPresent(string format) => GetDataPresent(format, autoConvert: true);

    public bool GetDataPresent(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return GetDataPresent(format.FullName ?? format.Name, autoConvert: true);
    }

    public bool GetDataPresent(string format, bool autoConvert)
    {
        ArgumentException.ThrowIfNullOrEmpty(format);
        return TryGetEntry(format, autoConvert, out _);
    }

    public string[] GetFormats() => GetFormats(autoConvert: true);

    public string[] GetFormats(bool autoConvert)
    {
        string[] formats = new string[_data.Count];
        _data.Keys.CopyTo(formats, 0);
        return formats;
    }

    public void SetData(object? data)
    {
        SetData(data?.GetType().FullName ?? DataFormats.Serializable, data, autoConvert: true);
    }

    public void SetData(string format, object? data)
    {
        SetData(format, data, autoConvert: true);
    }

    public void SetData(Type format, object? data)
    {
        ArgumentNullException.ThrowIfNull(format);
        SetData(format.FullName ?? format.Name, data, autoConvert: true);
    }

    public void SetData(string format, object? data, bool autoConvert)
    {
        ArgumentException.ThrowIfNullOrEmpty(format);
        _data[format] = new Entry(data, autoConvert);
    }

    internal void SetDataAsJson<T>(string format, T data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        string json = JsonSerializer.Serialize(data);
        Type payloadType = typeof(T);
        var payload = new JsonPayload(
            json,
            TypeName.Parse(payloadType.AssemblyQualifiedName ?? payloadType.FullName ?? payloadType.Name));
        _data[format] = new Entry(data, autoConvert: false, payload);
    }

    public bool TryGetData<T>([NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        return TryGetData(typeof(T).FullName ?? typeof(T).Name, autoConvert: true, out data);
    }

    public bool TryGetData<T>(
        string format,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        return TryGetData(format, autoConvert: true, out data);
    }

    public bool TryGetData<T>(
        string format,
        bool autoConvert,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        data = default;
        if (!TryGetEntry(format, autoConvert, out Entry entry))
        {
            return false;
        }

        if (entry.Payload is JsonPayload payload)
        {
            return TryDeserializeJsonPayload(payload, resolver: null, out data);
        }

        if (entry.Data is T typed)
        {
            data = typed;
            return true;
        }

        return false;
    }

    public bool TryGetData<T>(
        string format,
        Func<TypeName, Type?> resolver,
        bool autoConvert,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        data = default;
        if (!TryGetEntry(format, autoConvert, out Entry entry))
        {
            return false;
        }

        if (entry.Payload is JsonPayload payload)
        {
            return TryDeserializeJsonPayload(payload, resolver, out data);
        }

        if (entry.Data is T typed)
        {
            data = typed;
            return true;
        }

        return false;
    }

    private bool TryGetEntry(string format, bool autoConvert, out Entry entry)
    {
        if (_data.TryGetValue(format, out entry))
        {
            return true;
        }

        if (!autoConvert)
        {
            entry = default;
            return false;
        }

        if (IsTextFormat(format)
            && _data.TryGetValue(DataFormats.UnicodeText, out entry)
            && entry.AutoConvert)
        {
            return true;
        }

        entry = default;
        return false;
    }

    private static bool IsTextFormat(string format)
    {
        return format == DataFormats.Text
            || format == DataFormats.UnicodeText
            || format == DataFormats.StringFormat;
    }

    private static bool TryDeserializeJsonPayload<T>(
        JsonPayload payload,
        Func<TypeName, Type?>? resolver,
        [NotNullWhen(true), MaybeNullWhen(false)] out T data)
    {
        data = default;

        Type? targetType = resolver is null
            ? typeof(T)
            : resolver(payload.TypeName);
        if (targetType is null || !typeof(T).IsAssignableFrom(targetType))
        {
            return false;
        }

        try
        {
            object? value = targetType == typeof(T)
                ? JsonSerializer.Deserialize<T>(payload.Json)
                : JsonSerializer.Deserialize(payload.Json, targetType);
            if (value is not T typed)
            {
                return false;
            }

            data = typed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private readonly struct Entry
    {
        internal Entry(object? data, bool autoConvert, JsonPayload? payload = null)
        {
            Data = data;
            AutoConvert = autoConvert;
            Payload = payload;
        }

        internal object? Data { get; }

        internal bool AutoConvert { get; }

        internal JsonPayload? Payload { get; }
    }

    private sealed class JsonPayload
    {
        internal JsonPayload(string json, TypeName typeName)
        {
            Json = json;
            TypeName = typeName;
        }

        internal string Json { get; }

        internal TypeName TypeName { get; }
    }
}

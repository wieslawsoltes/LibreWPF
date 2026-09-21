using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace System.Windows.Media.ProGPU.Composition.Mil;

internal sealed class WpfBoundedWeakValueCache<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly ConcurrentDictionary<TKey, Entry> _entries;
    private readonly int _capacity;
    private readonly object _trimLock = new();
    private long _accessClock;

    public WpfBoundedWeakValueCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
        _entries = new ConcurrentDictionary<TKey, Entry>(
            comparer ?? EqualityComparer<TKey>.Default);
    }

    internal int Count => _entries.Count;

    internal int Capacity => _capacity;

    public bool TryGetValue(TKey key, out TValue? value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (entry.Value.TryGetTarget(out value))
            {
                Interlocked.Exchange(ref entry.LastAccess, NextAccess());
                return true;
            }

            ((ICollection<KeyValuePair<TKey, Entry>>)_entries).Remove(
                new KeyValuePair<TKey, Entry>(key, entry));
        }

        value = null;
        return false;
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        while (true)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                if (existing.Value.TryGetTarget(out var cachedValue))
                {
                    Interlocked.Exchange(ref existing.LastAccess, NextAccess());
                    return cachedValue;
                }

                var replacementValue = valueFactory(key);
                var replacement = CreateEntry(replacementValue);
                if (_entries.TryUpdate(key, replacement, existing))
                {
                    TrimToCapacity();
                    return replacementValue;
                }

                continue;
            }

            var newValue = valueFactory(key);
            if (_entries.TryAdd(key, CreateEntry(newValue)))
            {
                TrimToCapacity();
                return newValue;
            }
        }
    }

    private Entry CreateEntry(TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Entry(value, NextAccess());
    }

    private long NextAccess()
    {
        return Interlocked.Increment(ref _accessClock);
    }

    private void TrimToCapacity()
    {
        if (_entries.Count <= _capacity)
        {
            return;
        }

        lock (_trimLock)
        {
            foreach (var pair in _entries)
            {
                if (pair.Value.Value.TryGetTarget(out _))
                {
                    continue;
                }

                ((ICollection<KeyValuePair<TKey, Entry>>)_entries).Remove(pair);
            }

            while (_entries.Count > _capacity)
            {
                KeyValuePair<TKey, Entry>? oldest = null;
                foreach (var pair in _entries)
                {
                    if (oldest is null ||
                        Volatile.Read(ref pair.Value.LastAccess) <
                        Volatile.Read(ref oldest.Value.Value.LastAccess))
                    {
                        oldest = pair;
                    }
                }

                if (oldest is null ||
                    !((ICollection<KeyValuePair<TKey, Entry>>)_entries).Remove(oldest.Value))
                {
                    break;
                }
            }
        }
    }

    private sealed class Entry
    {
        public Entry(TValue value, long lastAccess)
        {
            Value = new WeakReference<TValue>(value);
            LastAccess = lastAccess;
        }

        public WeakReference<TValue> Value { get; }

        public long LastAccess;
    }
}

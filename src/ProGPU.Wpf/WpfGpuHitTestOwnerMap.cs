using System;
using System.Collections.Generic;

namespace System.Windows.Media.ProGPU;

public sealed class WpfGpuHitTestOwnerMap
{
    private readonly Dictionary<object, int> _idsByOwner = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, object> _ownersById = new();
    private int _nextId = 1;

    public int Count => _ownersById.Count;

    public void Clear()
    {
        _idsByOwner.Clear();
        _ownersById.Clear();
        _nextId = 1;
    }

    public int GetOrCreateId(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (_idsByOwner.TryGetValue(owner, out int id))
        {
            return id;
        }

        id = _nextId++;
        _idsByOwner.Add(owner, id);
        _ownersById.Add(id, owner);
        return id;
    }

    public bool TryGetOwner(int id, out object? owner)
    {
        return _ownersById.TryGetValue(id, out owner);
    }
}

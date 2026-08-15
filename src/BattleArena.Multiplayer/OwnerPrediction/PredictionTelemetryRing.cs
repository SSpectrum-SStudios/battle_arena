using System.Collections;

namespace BattleArena.Multiplayer.OwnerPrediction;

/// <summary>
/// A fixed-capacity, oldest-to-newest diagnostic buffer. Adding after capacity
/// is reached overwrites exactly the oldest sample. The ring is intentionally
/// not thread-safe; one prediction owner must serialize its telemetry writes.
/// </summary>
public sealed class PredictionTelemetryRing<T> : IReadOnlyList<T>
{
    private readonly T[] _items;
    private int _nextWriteIndex;
    private int _version;

    public PredictionTelemetryRing(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Prediction telemetry capacity must be positive.");
        }

        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count { get; private set; }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _items[ToStorageIndex(index)];
        }
    }

    /// <summary>Adds one sample without allocating or changing capacity.</summary>
    public void Add(T item)
    {
        _items[_nextWriteIndex] = item;
        _nextWriteIndex = (_nextWriteIndex + 1) % Capacity;
        if (Count < Capacity)
        {
            Count++;
        }

        _version = unchecked(_version + 1);
    }

    /// <summary>
    /// Copies the retained samples oldest-to-newest into caller-owned storage.
    /// </summary>
    public int CopyTo(Span<T> destination)
    {
        if (destination.Length < Count)
        {
            throw new ArgumentException(
                "The destination cannot hold every retained telemetry sample.",
                nameof(destination));
        }

        for (var index = 0; index < Count; index++)
        {
            destination[index] = this[index];
        }

        return Count;
    }

    public void Clear()
    {
        Array.Clear(_items);
        Count = 0;
        _nextWriteIndex = 0;
        _version = unchecked(_version + 1);
    }

    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int ToStorageIndex(int chronologicalIndex)
    {
        var oldestIndex = Count == Capacity ? _nextWriteIndex : 0;
        return (oldestIndex + chronologicalIndex) % Capacity;
    }

    public struct Enumerator : IEnumerator<T>
    {
        private readonly PredictionTelemetryRing<T> _ring;
        private readonly int _version;
        private int _index;

        internal Enumerator(PredictionTelemetryRing<T> ring)
        {
            _ring = ring;
            _version = ring._version;
            _index = -1;
            Current = default!;
        }

        public T Current { get; private set; }

        object? IEnumerator.Current
        {
            get
            {
                ThrowIfChanged();
                if (_index < 0 || _index >= _ring.Count)
                {
                    throw new InvalidOperationException(
                        "The enumerator is not positioned on a telemetry sample.");
                }

                return Current;
            }
        }

        public bool MoveNext()
        {
            ThrowIfChanged();
            var nextIndex = _index + 1;
            if (nextIndex >= _ring.Count)
            {
                _index = _ring.Count;
                Current = default!;
                return false;
            }

            _index = nextIndex;
            Current = _ring[nextIndex];
            return true;
        }

        public void Reset()
        {
            ThrowIfChanged();
            _index = -1;
            Current = default!;
        }

        public void Dispose()
        {
        }

        private readonly void ThrowIfChanged()
        {
            if (_version != _ring._version)
            {
                throw new InvalidOperationException(
                    "Prediction telemetry changed during enumeration.");
            }
        }
    }
}

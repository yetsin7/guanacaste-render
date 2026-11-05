namespace Guanacaste3D.Core.Import;

/// <summary>
/// Append-only growable array. Unlike <see cref="List{T}"/> it exposes its backing span and trims on
/// <see cref="ToArray"/>, which keeps peak memory low when millions of vertices are read from a file.
/// </summary>
internal sealed class ArrayBuilder<T>(int capacity = 16)
{
    private T[] _items = new T[Math.Max(4, capacity)];

    public int Count { get; private set; }

    public ref T this[int index] => ref _items[index];

    public void Add(T item)
    {
        if (Count == _items.Length) Array.Resize(ref _items, (int)Math.Min(int.MaxValue / 2L, _items.Length * 2L));
        _items[Count++] = item;
    }

    public void AddRange(ReadOnlySpan<T> items)
    {
        foreach (var item in items) Add(item);
    }

    public ReadOnlySpan<T> AsSpan() => _items.AsSpan(0, Count);

    public T[] ToArray() => AsSpan().ToArray();

    public void Clear() => Count = 0;
}

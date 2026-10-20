using System.Collections;
using System.Collections.Generic;

namespace ModernWestern.UEArchitecture
{
    /// <summary>
    /// A read-only view of a framework list (players, subsystems, PlayerStarts). Unlike IReadOnlyList, looping over it
    /// with foreach allocates nothing, so it is safe in Update.
    /// </summary>
    public readonly struct ListView<T> : IReadOnlyList<T>
    {
        private static readonly List<T> Empty = new();

        private readonly List<T> _list;

        internal ListView(List<T> list)
        {
            _list = list;
        }

        public int Count => _list?.Count ?? 0;

        public T this[int index] => (_list ?? Empty)[index];

        public bool Contains(T item)
        {
            return _list != null && _list.Contains(item);
        }

        public int IndexOf(T item)
        {
            return _list?.IndexOf(item) ?? -1;
        }

        public List<T>.Enumerator GetEnumerator()
        {
            return (_list ?? Empty).GetEnumerator();
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}

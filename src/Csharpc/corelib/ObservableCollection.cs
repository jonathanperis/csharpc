// System.Collections.ObjectModel.ObservableCollection<T>, ReadOnlyObservableCollection<T> and
// System.Collections.Specialized.NotifyCollectionChangedEventArgs: port of dotnet/runtime's
// System.ObjectModel sources (release/11.0, MIT). The corelib shadows that assembly's
// PropertyChangedEventArgs, so its other types live here too.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.ObjectModel.ObservableCollection`1")]
    public class ObservableCollectionImpl<T> : Collection<T>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        private SimpleMonitor _monitor;
        private int _blockReentrancyCount;

        public ObservableCollectionImpl() { }

        public ObservableCollectionImpl(IEnumerable<T> collection) : base(new List<T>(collection ?? throw new ArgumentNullException("collection"))) { }

        public ObservableCollectionImpl(List<T> list) : base(new List<T>(list ?? throw new ArgumentNullException("list"))) { }

        public void Move(int oldIndex, int newIndex) => MoveItem(oldIndex, newIndex);

        event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
        {
            add => PropertyChanged += value;
            remove => PropertyChanged -= value;
        }

        // Explicit backing fields: csharpc doesn't lower field-like events of generic classes yet.
        private NotifyCollectionChangedEventHandler _collectionChanged;
        private PropertyChangedEventHandler _propertyChanged;

        public virtual event NotifyCollectionChangedEventHandler CollectionChanged
        {
            add => _collectionChanged += value;
            remove => _collectionChanged -= value;
        }

        protected override void ClearItems()
        {
            CheckReentrancy();
            base.ClearItems();
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionReset();
        }

        protected override void RemoveItem(int index)
        {
            CheckReentrancy();
            T removedItem = this[index];
            base.RemoveItem(index);
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionChanged(NotifyCollectionChangedAction.Remove, removedItem, index);
        }

        protected override void InsertItem(int index, T item)
        {
            CheckReentrancy();
            base.InsertItem(index, item);
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionChanged(NotifyCollectionChangedAction.Add, item, index);
        }

        protected override void SetItem(int index, T item)
        {
            CheckReentrancy();
            T originalItem = this[index];
            base.SetItem(index, item);
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, originalItem, index));
        }

        protected virtual void MoveItem(int oldIndex, int newIndex)
        {
            CheckReentrancy();
            T removedItem = this[oldIndex];
            base.RemoveItem(oldIndex);
            base.InsertItem(newIndex, removedItem);
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, removedItem, newIndex, oldIndex));
        }

        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) => _propertyChanged?.Invoke(this, e);

        protected virtual event PropertyChangedEventHandler PropertyChanged
        {
            add => _propertyChanged += value;
            remove => _propertyChanged -= value;
        }

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            NotifyCollectionChangedEventHandler handler = _collectionChanged;
            if (handler != null)
            {
                _blockReentrancyCount++;
                try { handler(this, e); }
                finally { _blockReentrancyCount--; }
            }
        }

        protected IDisposable BlockReentrancy()
        {
            _blockReentrancyCount++;
            return _monitor ??= new SimpleMonitor(this);
        }

        protected void CheckReentrancy()
        {
            if (_blockReentrancyCount > 0)
            {
                // Changes are fine with a single listener: only later listeners would see stale event args.
                NotifyCollectionChangedEventHandler handler = _collectionChanged;
                if (handler != null && handler.GetInvocationList().Length != 1)
                    throw new InvalidOperationException("Cannot change ObservableCollection during a CollectionChanged event.");
            }
        }

        private void OnCountPropertyChanged() => OnPropertyChanged(ObservableEventArgsCache.CountPropertyChanged);
        private void OnIndexerPropertyChanged() => OnPropertyChanged(ObservableEventArgsCache.IndexerPropertyChanged);

        private void OnCollectionChanged(NotifyCollectionChangedAction action, object item, int index) =>
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(action, item, index));

        private void OnCollectionReset() => OnCollectionChanged(ObservableEventArgsCache.ResetCollectionChanged);

        private sealed class SimpleMonitor : IDisposable
        {
            internal ObservableCollectionImpl<T> _collection;
            public SimpleMonitor(ObservableCollectionImpl<T> collection) { _collection = collection; }
            public void Dispose() => _collection._blockReentrancyCount--;
        }
    }

    internal static class ObservableEventArgsCache
    {
        private static PropertyChangedEventArgs s_count, s_indexer;
        private static NotifyCollectionChangedEventArgs s_reset;
        internal static PropertyChangedEventArgs CountPropertyChanged => s_count ??= new PropertyChangedEventArgs("Count");
        internal static PropertyChangedEventArgs IndexerPropertyChanged => s_indexer ??= new PropertyChangedEventArgs("Item[]");
        internal static NotifyCollectionChangedEventArgs ResetCollectionChanged => s_reset ??= new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
    }

    [Shadows("System.Collections.ObjectModel.ReadOnlyObservableCollection`1")]
    public class ReadOnlyObservableCollectionImpl<T> : ReadOnlyCollection<T>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        public ReadOnlyObservableCollectionImpl(ObservableCollection<T> list) : base(list)
        {
            ((INotifyCollectionChanged)list).CollectionChanged += HandleCollectionChanged;
            ((INotifyPropertyChanged)list).PropertyChanged += HandlePropertyChanged;
        }

        private static ReadOnlyObservableCollectionImpl<T> s_empty;
        public static ReadOnlyObservableCollectionImpl<T> Empty => s_empty ??= new ReadOnlyObservableCollectionImpl<T>(new ObservableCollection<T>());

        event NotifyCollectionChangedEventHandler INotifyCollectionChanged.CollectionChanged
        {
            add => CollectionChanged += value;
            remove => CollectionChanged -= value;
        }

        private NotifyCollectionChangedEventHandler _collectionChanged;
        private PropertyChangedEventHandler _propertyChanged;

        protected virtual event NotifyCollectionChangedEventHandler CollectionChanged
        {
            add => _collectionChanged += value;
            remove => _collectionChanged -= value;
        }

        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs args) => _collectionChanged?.Invoke(this, args);

        event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
        {
            add => PropertyChanged += value;
            remove => PropertyChanged -= value;
        }

        protected virtual event PropertyChangedEventHandler PropertyChanged
        {
            add => _propertyChanged += value;
            remove => _propertyChanged -= value;
        }

        protected virtual void OnPropertyChanged(PropertyChangedEventArgs args) => _propertyChanged?.Invoke(this, args);

        private void HandleCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => OnCollectionChanged(e);
        private void HandlePropertyChanged(object sender, PropertyChangedEventArgs e) => OnPropertyChanged(e);
    }

    [Shadows("System.Collections.Specialized.NotifyCollectionChangedEventArgs")]
    public class NotifyCollectionChangedEventArgsImpl : EventArgs
    {
        private readonly NotifyCollectionChangedAction _action;
        private readonly IList _newItems;
        private readonly IList _oldItems;
        private readonly int _newStartingIndex = -1;
        private readonly int _oldStartingIndex = -1;

        private static ArgumentException WrongAction(NotifyCollectionChangedAction expected) =>
            new ArgumentException("Constructor supports only the '" + expected + "' action.", "action");
        private static ArgumentException ResetAddOrRemove() =>
            new ArgumentException("Constructor only supports either a Reset, Add, or Remove action.", "action");

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action)
        {
            if (action != NotifyCollectionChangedAction.Reset) throw WrongAction(NotifyCollectionChangedAction.Reset);
            _action = action;
        }

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action, object changedItem) : this(action, changedItem, -1) { }

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action, object changedItem, int index)
        {
            switch (action)
            {
                case NotifyCollectionChangedAction.Reset:
                    if (changedItem != null) throw new ArgumentException("Reset action must be initialized with no changed items.", "action");
                    if (index != -1) throw new ArgumentException("Reset action must be initialized with index -1.", "action");
                    break;
                case NotifyCollectionChangedAction.Add:
                    _newItems = new SingleItemReadOnlyList(changedItem);
                    _newStartingIndex = index;
                    break;
                case NotifyCollectionChangedAction.Remove:
                    _oldItems = new SingleItemReadOnlyList(changedItem);
                    _oldStartingIndex = index;
                    break;
                default:
                    throw ResetAddOrRemove();
            }
            _action = action;
        }

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action, IList changedItems) : this(action, changedItems, -1) { }

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action, IList changedItems, int startingIndex)
        {
            switch (action)
            {
                case NotifyCollectionChangedAction.Reset:
                    if (changedItems != null) throw new ArgumentException("Reset action must be initialized with no changed items.", "action");
                    if (startingIndex != -1) throw new ArgumentException("Reset action must be initialized with index -1.", "action");
                    break;
                case NotifyCollectionChangedAction.Add:
                case NotifyCollectionChangedAction.Remove:
                    ArgumentNullException.ThrowIfNull(changedItems);
                    ArgumentOutOfRangeException.ThrowIfLessThan(startingIndex, -1);
                    if (action == NotifyCollectionChangedAction.Add)
                    {
                        _newItems = new ReadOnlyItemList(changedItems);
                        _newStartingIndex = startingIndex;
                    }
                    else
                    {
                        _oldItems = new ReadOnlyItemList(changedItems);
                        _oldStartingIndex = startingIndex;
                    }
                    break;
                default:
                    throw ResetAddOrRemove();
            }
            _action = action;
        }

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action, object newItem, object oldItem) : this(action, newItem, oldItem, -1) { }

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action, object newItem, object oldItem, int index)
        {
            if (action != NotifyCollectionChangedAction.Replace) throw WrongAction(NotifyCollectionChangedAction.Replace);
            _action = action;
            _newItems = new SingleItemReadOnlyList(newItem);
            _oldItems = new SingleItemReadOnlyList(oldItem);
            _newStartingIndex = _oldStartingIndex = index;
        }

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action, IList newItems, IList oldItems) : this(action, newItems, oldItems, -1) { }

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action, IList newItems, IList oldItems, int startingIndex)
        {
            if (action != NotifyCollectionChangedAction.Replace) throw WrongAction(NotifyCollectionChangedAction.Replace);
            ArgumentNullException.ThrowIfNull(newItems);
            ArgumentNullException.ThrowIfNull(oldItems);
            _action = action;
            _newItems = new ReadOnlyItemList(newItems);
            _oldItems = new ReadOnlyItemList(oldItems);
            _newStartingIndex = _oldStartingIndex = startingIndex;
        }

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action, object changedItem, int index, int oldIndex)
        {
            if (action != NotifyCollectionChangedAction.Move) throw WrongAction(NotifyCollectionChangedAction.Move);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            _action = action;
            _newItems = _oldItems = new SingleItemReadOnlyList(changedItem);
            _newStartingIndex = index;
            _oldStartingIndex = oldIndex;
        }

        public NotifyCollectionChangedEventArgsImpl(NotifyCollectionChangedAction action, IList changedItems, int index, int oldIndex)
        {
            if (action != NotifyCollectionChangedAction.Move) throw WrongAction(NotifyCollectionChangedAction.Move);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            _action = action;
            _newItems = _oldItems = changedItems != null ? new ReadOnlyItemList(changedItems) : null;
            _newStartingIndex = index;
            _oldStartingIndex = oldIndex;
        }

        public NotifyCollectionChangedAction Action => _action;
        public IList NewItems => _newItems;
        public IList OldItems => _oldItems;
        public int NewStartingIndex => _newStartingIndex;
        public int OldStartingIndex => _oldStartingIndex;
    }

    internal sealed class ReadOnlyItemList : IList
    {
        private readonly IList _list;
        internal ReadOnlyItemList(IList list) { _list = list; }
        private static NotSupportedException ReadOnly() => new NotSupportedException("Collection is read-only.");

        public int Count => _list.Count;
        public bool IsReadOnly => true;
        public bool IsFixedSize => true;
        public bool IsSynchronized => _list.IsSynchronized;
        public object this[int index]
        {
            get => _list[index];
            set => throw ReadOnly();
        }
        public object SyncRoot => _list.SyncRoot;
        public int Add(object value) => throw ReadOnly();
        public void Clear() => throw ReadOnly();
        public bool Contains(object value) => _list.Contains(value);
        public void CopyTo(Array array, int index) => _list.CopyTo(array, index);
        public IEnumerator GetEnumerator() => _list.GetEnumerator();
        public int IndexOf(object value) => _list.IndexOf(value);
        public void Insert(int index, object value) => throw ReadOnly();
        public void Remove(object value) => throw ReadOnly();
        public void RemoveAt(int index) => throw ReadOnly();
    }

    internal sealed class SingleItemReadOnlyList : IList
    {
        private readonly object _item;
        public SingleItemReadOnlyList(object item) { _item = item; }
        private static NotSupportedException ReadOnly() => new NotSupportedException("Collection is read-only.");

        public object this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNotEqual(index, 0);
                return _item;
            }
            set => throw ReadOnly();
        }
        public bool IsFixedSize => true;
        public bool IsReadOnly => true;
        public int Count => 1;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public IEnumerator GetEnumerator()
        {
            yield return _item;
        }
        public bool Contains(object value) => _item is null ? value is null : _item.Equals(value);
        public int IndexOf(object value) => Contains(value) ? 0 : -1;
        public void CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);
            if (array.Rank != 1) throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", "array");
            if (array.GetLowerBound(0) != 0) throw new ArgumentException("The lower bound of target array must be zero.", "array");
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            if (array.Length - index < 1) throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.");
            array.SetValue(_item, index);
        }
        public int Add(object value) => throw ReadOnly();
        public void Clear() => throw ReadOnly();
        public void Insert(int index, object value) => throw ReadOnly();
        public void Remove(object value) => throw ReadOnly();
        public void RemoveAt(int index) => throw ReadOnly();
    }
}

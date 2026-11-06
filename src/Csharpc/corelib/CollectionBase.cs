// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to csharpc from dotnet/runtime (release/11.0), src/libraries/System.Collections.NonGeneric/src/System/
// Collections/CollectionBase.cs; see THIRD-PARTY-NOTICES.md.
// System.Collections.CollectionBase over the corelib's ArrayList: the On* hooks run around every change as in .NET.
// X509CertificateCollection (docs/adr/0022-tls.md) and the other pre-generics collections derive from it.
#nullable disable
using System;
using System.Collections;

namespace CsharpcCoreLib
{
    [Shadows("System.Collections.CollectionBase")]
    public abstract class CollectionBaseImpl : IList
    {
        private readonly ArrayList _list;

        protected CollectionBaseImpl() { _list = new ArrayList(); }

        protected CollectionBaseImpl(int capacity) { _list = new ArrayList(capacity); }

        protected ArrayList InnerList => _list;

        protected IList List => (IList)this;

        public int Capacity
        {
            get => InnerList.Capacity;
            set => InnerList.Capacity = value;
        }

        public int Count => _list.Count;

        public void Clear()
        {
            OnClear();
            InnerList.Clear();
            OnClearComplete();
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
            object temp = InnerList[index];
            OnValidate(temp);
            OnRemove(index, temp);
            InnerList.RemoveAt(index);
            try
            {
                OnRemoveComplete(index, temp);
            }
            catch
            {
                InnerList.Insert(index, temp);
                throw;
            }
        }

        bool IList.IsReadOnly => InnerList.IsReadOnly;
        bool IList.IsFixedSize => InnerList.IsFixedSize;
        bool ICollection.IsSynchronized => InnerList.IsSynchronized;
        object ICollection.SyncRoot => InnerList.SyncRoot;
        void ICollection.CopyTo(Array array, int index) => InnerList.CopyTo(array, index);

        object IList.this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                    throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
                return InnerList[index];
            }
            set
            {
                if (index < 0 || index >= Count)
                    throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection.");
                OnValidate(value);
                object temp = InnerList[index];
                OnSet(index, temp, value);
                InnerList[index] = value;
                try
                {
                    OnSetComplete(index, temp, value);
                }
                catch
                {
                    InnerList[index] = temp;
                    throw;
                }
            }
        }

        bool IList.Contains(object value) => InnerList.Contains(value);

        int IList.Add(object value)
        {
            OnValidate(value);
            OnInsert(InnerList.Count, value);
            int index = InnerList.Add(value);
            try
            {
                OnInsertComplete(index, value);
            }
            catch
            {
                InnerList.RemoveAt(index);
                throw;
            }
            return index;
        }

        void IList.Remove(object value)
        {
            OnValidate(value);
            int index = InnerList.IndexOf(value);
            if (index < 0) throw new ArgumentException("Cannot remove the specified item because it was not found in the specified Collection.");
            OnRemove(index, value);
            InnerList.RemoveAt(index);
            try
            {
                OnRemoveComplete(index, value);
            }
            catch
            {
                InnerList.Insert(index, value);
                throw;
            }
        }

        int IList.IndexOf(object value) => InnerList.IndexOf(value);

        void IList.Insert(int index, object value)
        {
            if (index < 0 || index > Count)
                throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            OnValidate(value);
            OnInsert(index, value);
            InnerList.Insert(index, value);
            try
            {
                OnInsertComplete(index, value);
            }
            catch
            {
                InnerList.RemoveAt(index);
                throw;
            }
        }

        public IEnumerator GetEnumerator() => InnerList.GetEnumerator();

        protected virtual void OnSet(int index, object oldValue, object newValue) { }
        protected virtual void OnInsert(int index, object value) { }
        protected virtual void OnClear() { }
        protected virtual void OnRemove(int index, object value) { }

        protected virtual void OnValidate(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
        }

        protected virtual void OnSetComplete(int index, object oldValue, object newValue) { }
        protected virtual void OnInsertComplete(int index, object value) { }
        protected virtual void OnClearComplete() { }
        protected virtual void OnRemoveComplete(int index, object value) { }
    }
}

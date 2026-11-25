// Exception.Data: .NET's ListDictionaryInternal (insertion-ordered, non-null keys), created on first use.
// csrt_Exception has no slot for it (src/runtime/csrt.h), so the dictionaries live in a
// ConditionalWeakTable keyed by the exception: only programs that read Data pay for it.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace CsharpcCoreLib
{
    internal static class ExceptionData
    {
        private static readonly ConditionalWeakTable<Exception, ListDictionaryInternal> s_data = new();
        private static readonly ConditionalWeakTable<Exception, StringBox> s_helpLink = new(), s_source = new();

        private sealed class StringBox { public string Value; }

        public static string GetHelpLink(Exception exception) => s_helpLink.TryGetValue(exception, out StringBox box) ? box.Value : null;
        public static void SetHelpLink(Exception exception, string value) => s_helpLink.GetOrCreateValue(exception).Value = value;

        /// <summary>
        /// Exception.Source: what was set, else null for an exception that was never thrown. .NET names the assembly of the method
        /// that threw an exception otherwise; csharpc doesn't know it yet, so that asks for an explicit error.
        /// </summary>
        public static string GetSource(Exception exception)
        {
            if (s_source.TryGetValue(exception, out StringBox box)) return box.Value;
            if (exception.StackTrace == null) return null;
            throw new NotSupportedException("Exception.Source of an exception that was thrown is not supported by csharpc yet (it names the assembly of the throwing method).");
        }

        /// <summary>Exception.Source read by the framework's own IL: what was set, else null (never the throwing assembly's name, which is not known).</summary>
        public static string GetSourceFramework(Exception exception) => s_source.TryGetValue(exception, out StringBox box) ? box.Value : null;

        public static void SetSource(Exception exception, string value) => s_source.GetOrCreateValue(exception).Value = value;

        public static IDictionary Get(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return s_data.GetValue(exception, static _ => new ListDictionaryInternal());
        }

        // The library exceptions whose GetObjectData adds nothing to Exception's.
        private static readonly HashSet<string> s_plainLibraryExceptions = new()
        {
            "System.Exception", "System.SystemException", "System.ApplicationException", "System.InvalidOperationException",
            "System.NotSupportedException", "System.NotImplementedException", "System.FormatException",
        };

        /// <summary>A call of GetObjectData through the exception's type that no override of the program took: the base implementation.</summary>
        public static void GetObjectDataBase(Exception exception, SerializationInfo info) => GetObjectData(exception, info);

        /// <summary>
        /// Exception.GetObjectData: the entries .NET's base implementation writes. Exceptions whose library ancestor overrides it
        /// with entries of its own (ArgumentException's ParamName, the file exceptions' FileName...) are an explicit error, not a
        /// shorter list.
        /// </summary>
        public static void GetObjectData(Exception exception, SerializationInfo info)
        {
            ArgumentNullException.ThrowIfNull(info);
            bool known = false;
            for (Type t = exception.GetType(); t != null; t = t.BaseType)
            {
                if (s_plainLibraryExceptions.Contains(t.FullName ?? "")) { known = true; break; }
                if (t.FullName != null && t.FullName.StartsWith("System.", StringComparison.Ordinal))
                    throw new NotSupportedException($"Exception.GetObjectData of '{t.FullName}' is not supported by csharpc yet (the entries of its own GetObjectData).");
            }
            if (!known) throw new NotSupportedException("Exception.GetObjectData of this exception is not supported by csharpc yet.");
            string message = exception.Message;
            // No message given to Exception or a user exception: .NET stores null, and Message answers this text (the library
            // exceptions put their own default text in the field).
            if (message == $"Exception of type '{exception.GetType()}' was thrown.") message = null;
            s_data.TryGetValue(exception, out ListDictionaryInternal data);
            info.AddValue("ClassName", exception.GetType().ToString(), typeof(string));
            info.AddValue("Message", message, typeof(string));
            info.AddValue("Data", data, typeof(IDictionary));
            info.AddValue("InnerException", exception.InnerException, typeof(Exception));
            info.AddValue("HelpURL", GetHelpLink(exception), typeof(string));
            info.AddValue("StackTraceString", exception.StackTrace, typeof(string));
            info.AddValue("RemoteStackTraceString", null, typeof(string));
            info.AddValue("RemoteStackIndex", 0, typeof(int));
            info.AddValue("ExceptionMethod", null, typeof(string));
            info.AddValue("HResult", exception.HResult);
            info.AddValue("Source", GetSource(exception), typeof(string));
            info.AddValue("WatsonBuckets", null, typeof(byte[]));
        }
    }

    internal sealed class ListDictionaryInternal : IDictionary
    {
        private readonly List<object> _keys = new();
        private readonly List<object> _values = new();

        public object this[object key]
        {
            get
            {
                ArgumentNullException.ThrowIfNull(key);
                int i = IndexOf(key);
                return i < 0 ? null : _values[i];
            }
            set
            {
                ArgumentNullException.ThrowIfNull(key);
                int i = IndexOf(key);
                if (i < 0) { _keys.Add(key); _values.Add(value); }
                else _values[i] = value;
            }
        }

        private int IndexOf(object key)
        {
            for (int i = 0; i < _keys.Count; i++)
                if (_keys[i].Equals(key)) return i;
            return -1;
        }

        public int Count => _keys.Count;
        public ICollection Keys => _keys.ToArray();
        public ICollection Values => _values.ToArray();
        public bool IsReadOnly => false;
        public bool IsFixedSize => false;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        public void Add(object key, object value)
        {
            ArgumentNullException.ThrowIfNull(key);
            if (IndexOf(key) >= 0)
                throw new ArgumentException("An item with the same key has already been added. Key: " + key);
            _keys.Add(key);
            _values.Add(value);
        }

        public void Clear() { _keys.Clear(); _values.Clear(); }

        public bool Contains(object key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return IndexOf(key) >= 0;
        }

        public void Remove(object key)
        {
            ArgumentNullException.ThrowIfNull(key);
            int i = IndexOf(key);
            if (i >= 0) { _keys.RemoveAt(i); _values.RemoveAt(i); }
        }

        public void CopyTo(Array array, int index)
        {
            ArgumentNullException.ThrowIfNull(array);
            for (int i = 0; i < _keys.Count; i++) array.SetValue(new DictionaryEntry(_keys[i], _values[i]), index + i);
        }

        public IDictionaryEnumerator GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        private sealed class Enumerator : IDictionaryEnumerator
        {
            private readonly ListDictionaryInternal _owner;
            private int _index = -1;
            public Enumerator(ListDictionaryInternal owner) { _owner = owner; }
            public bool MoveNext() => ++_index < _owner._keys.Count;
            public void Reset() => _index = -1;
            public DictionaryEntry Entry => new DictionaryEntry(_owner._keys[_index], _owner._values[_index]);
            public object Key => _owner._keys[_index];
            public object Value => _owner._values[_index];
            public object Current => Entry;
        }
    }
}

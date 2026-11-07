// ResourceSet (what ResourceManager.GetResourceSet returns: .NET's RuntimeResourceSet), the streams
// ResourceManager.GetStream / GetObject hand out for Stream entries, and UnmanagedMemoryStreamWrapper.
// The sets are views of the compiled resource table (Resources.cs, ModuleCodeGen.Resources.cs): the
// neutral culture's entries by name, resolved the way .NET's ResourceReader/RuntimeResourceSet do it
// (values boxed once and cached, except byte arrays and streams, which are created on every read; strings
// are the table's text). The constructors of ResourceSet over a file, a stream or an IResourceReader are
// not provided: a program that calls them is rejected at compile time.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Resources;
using System.Runtime.InteropServices;

namespace CsharpcCoreLib
{
    [Shadows("System.Resources.ResourceSet")]
    public class ResourceSetImpl : IDisposable, IEnumerable
    {
        private readonly ResourceManagerImpl _owner;
        private readonly string _baseName;
        private Dictionary<string, string> _raw;            // null once disposed
        private Dictionary<string, object> _resolved = new Dictionary<string, object>();

        internal ResourceSetImpl(ResourceManagerImpl owner, string baseName, Dictionary<string, string> raw)
        {
            _owner = owner;
            _baseName = baseName;
            _raw = raw;
        }

        private Dictionary<string, string> Raw => _raw ?? throw new ObjectDisposedException(null, "Cannot access a closed resource set.");

        public virtual void Close() => Dispose(true);

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            _raw = null;
            lock (_resolved) _resolved.Clear();
        }

        public virtual Type GetDefaultReader() => typeof(ResourceReader);

        public virtual Type GetDefaultWriter() => typeof(ResourceWriter);

        public virtual IDictionaryEnumerator GetEnumerator() => new ResourceEnumerator(this, Raw);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private string Find(Dictionary<string, string> raw, string name, bool ignoreCase, out string key)
        {
            if (raw.TryGetValue(name, out string value)) { key = name; return value; }
            if (ignoreCase)
                foreach (KeyValuePair<string, string> entry in raw)
                    if (string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase)) { key = entry.Key; return entry.Value; }
            key = null;
            return null;
        }

        public virtual string GetString(string name) => GetString(name, false);

        public virtual string GetString(string name, bool ignoreCase)
        {
            Dictionary<string, string> raw = Raw;
            if (name == null) throw new ArgumentNullException("key");
            string value = Find(raw, name, ignoreCase, out string key);
            if (value == null) return null;
            if (!ResourceManagerImpl.IsNonString(value)) return value;
            // A value an earlier GetObject boxed is what .NET's cache holds: reading it as a string is a bad cast.
            if (ResourceManagerImpl.IsTypeCoded(value))
            {
                lock (_resolved)
                    if (_resolved.TryGetValue(key, out object boxed) && boxed != null)
                        throw new InvalidCastException("Unable to cast object of type '" + boxed.GetType().FullName + "' to type 'System.String'.");
                if (ResourceManagerImpl.TypeCodeName(value) is "Null") return null;
            }
            throw new InvalidOperationException("Resource was of type '" + ResourceManagerImpl.NonStringTypeDescription(value) + "' instead of String - call GetObject instead.");
        }

        public virtual object GetObject(string name) => GetObject(name, false);

        public virtual object GetObject(string name, bool ignoreCase)
        {
            Dictionary<string, string> raw = Raw;
            if (name == null) throw new ArgumentNullException("key");
            string value = Find(raw, name, ignoreCase, out string key);
            return value == null ? null : Resolve(key, value);
        }

        internal object Resolve(string key, string value)
        {
            if (!ResourceManagerImpl.IsNonString(value)) return value;
            lock (_resolved)
                if (_resolved.TryGetValue(key, out object known)) return known;
            object created = ResourceManagerImpl.ObjectOf(_baseName, key, value);
            // A value stored by type code is boxed once (byte arrays and streams are made again on every read); an object
            // DeserializingResourceReader creates from a preserialized type is made again each time.
            if (ResourceManagerImpl.IsTypeCoded(value) && !(created is byte[]) && !(created is Stream)) lock (_resolved) _resolved[key] = created;
            return created;
        }

        /// <summary>ResourceReader.ResourceEnumerator: the entries in the table's order, values created when read.</summary>
        [Shadows("System.Resources.ResourceReader+ResourceEnumerator")]
        internal sealed class ResourceEnumerator : IDictionaryEnumerator
        {
            private readonly ResourceSetImpl _set;
            private readonly KeyValuePair<string, string>[] _entries;
            private int _index = -1;
            private bool _finished;

            internal ResourceEnumerator(ResourceSetImpl set, Dictionary<string, string> raw)
            {
                _set = set;
                _entries = new KeyValuePair<string, string>[raw.Count];
                int i = 0;
                foreach (KeyValuePair<string, string> entry in raw) _entries[i++] = entry;
            }

            public bool MoveNext()
            {
                if (_set._raw == null) throw new ObjectDisposedException(null, "Cannot access a closed resource set.");
                if (_index == _entries.Length) return false;
                _index++;
                if (_index == _entries.Length) { _finished = true; return false; }
                return true;
            }

            private KeyValuePair<string, string> Current
            {
                get
                {
                    if (_finished || _index == _entries.Length) throw new InvalidOperationException("Enumeration already finished.");
                    if (_index < 0) throw new InvalidOperationException("Enumeration has not started. Call MoveNext.");
                    return _entries[_index];
                }
            }

            public object Key => Current.Key;
            // The reader's enumerator creates the value anew each time and doesn't fill the set's cache.
            public object Value => ResourceManagerImpl.IsNonString(Current.Value) ? ResourceManagerImpl.ObjectOf(_set._baseName, Current.Key, Current.Value) : Current.Value;
            public DictionaryEntry Entry => new DictionaryEntry(Key, Value);
            object IEnumerator.Current => Entry;

            public void Reset()
            {
                if (_set._raw == null) throw new InvalidOperationException("Cannot access a closed resource set.");
                _index = -1;
                _finished = false;
            }
        }
    }

    /// <summary>What ResourceManager.GetResourceSet creates; the name is .NET's, as GetType() reports it.</summary>
    [Shadows("System.Resources.RuntimeResourceSet")]
    internal sealed class RuntimeResourceSetImpl : ResourceSetImpl
    {
        internal RuntimeResourceSetImpl(ResourceManagerImpl owner, string baseName, Dictionary<string, string> raw) : base(owner, baseName, raw) { }
    }

    /// <summary>The Stream ResourceManager.GetObject returns for a Stream entry: a wrapper of the UnmanagedMemoryStream (its own position).</summary>
    [Shadows("System.IO.UnmanagedMemoryStreamWrapper")]
    internal sealed class UnmanagedMemoryStreamWrapperImpl : Stream
    {
        private readonly UnmanagedMemoryStream _stream;

        internal UnmanagedMemoryStreamWrapperImpl(UnmanagedMemoryStream stream) { _stream = stream; }

        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => _stream.CanSeek;
        public override bool CanWrite => _stream.CanWrite;
        public override long Length => _stream.Length;
        public override long Position { get => _stream.Position; set => _stream.Position = value; }
        public override void Flush() => _stream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _stream.Read(buffer);
        public override int ReadByte() => _stream.ReadByte();
        public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);
        public override void SetLength(long value) => _stream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
            if (disposing) _stream.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>The bytes behind the UnmanagedMemoryStreams of Stream entries: native copies that live as long as the program (as the assembly image's would).</summary>
    internal static unsafe class ResourceStreams
    {
        private static readonly Dictionary<string, IntPtr> s_memory = new Dictionary<string, IntPtr>();

        /// <summary>A new read-only stream over the bytes `text` (base64) encodes; the same text shares one copy.</summary>
        internal static UnmanagedMemoryStream Open(string text)
        {
            byte[] bytes = Convert.FromBase64String(text);
            IntPtr memory;
            lock (s_memory)
            {
                if (!s_memory.TryGetValue(text, out memory))
                {
                    memory = Marshal.AllocHGlobal(bytes.Length == 0 ? 1 : bytes.Length);
                    Marshal.Copy(bytes, 0, memory, bytes.Length);
                    s_memory[text] = memory;
                }
            }
            return new UnmanagedMemoryStream((byte*)memory, bytes.Length);
        }
    }
}

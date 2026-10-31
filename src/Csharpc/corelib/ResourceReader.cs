// System.Resources.ResourceReader over a .resources stream (the format of dotnet/runtime's
// ResourceReader.cs / RuntimeResourceSet, version 2 — what resgen and the SDK write): the keys in the
// file's (name-hash) order, GetResourceData's type name and raw bytes, and the values of the primitive
// type codes. Values of serialized user types (ResourceTypeCode.StartOfUserTypes and above) are an
// explicit NotSupportedException, as BinaryFormatter is gone from .NET too. csharpc's own front end reads
// the IL assemblies' string resources with it (Frontend/Il/IlAssemblySet.cs).
#nullable disable
using System;
using System.Collections;
using System.IO;
using System.Resources;
using System.Text;

namespace CsharpcCoreLib
{
    [Shadows("System.Resources.ResourceReader")]
    public sealed class ResourceReaderImpl : IResourceReader
    {
        private const int MagicNumber = unchecked((int)0xBEEFCACE);
        private const int StartOfUserTypes = 0x40;
        private static readonly string[] s_typeCodeNames =
        {
            "ResourceTypeCode.Null", "ResourceTypeCode.String", "ResourceTypeCode.Boolean", "ResourceTypeCode.Char",
            "ResourceTypeCode.Byte", "ResourceTypeCode.SByte", "ResourceTypeCode.Int16", "ResourceTypeCode.UInt16",
            "ResourceTypeCode.Int32", "ResourceTypeCode.UInt32", "ResourceTypeCode.Int64", "ResourceTypeCode.UInt64",
            "ResourceTypeCode.Single", "ResourceTypeCode.Double", "ResourceTypeCode.Decimal", "ResourceTypeCode.DateTime",
            "ResourceTypeCode.TimeSpan",
        };

        private Stream _stream;
        private BinaryReader _reader;
        private string[] _types;
        private int[] _namePositions;
        private long _nameSectionOffset, _dataSectionOffset;

        public ResourceReaderImpl(string fileName) : this(new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read)) { }

        public ResourceReaderImpl(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanRead) throw new ArgumentException("Stream was not readable.");
            _stream = stream;
            _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            ReadHeaders();
        }

        private void ReadHeaders()
        {
            // ResourceManager header: magic, header version, then (version > 1) bytes to skip, else reader and set types.
            if (_reader.ReadInt32() != MagicNumber) throw new ArgumentException("Stream is not a valid resource file.");
            int managerVersion = _reader.ReadInt32();
            int skip = _reader.ReadInt32();
            if (managerVersion > 1) _stream.Seek(skip, SeekOrigin.Current);
            else { _reader.ReadString(); _reader.ReadString(); }
            // RuntimeResourceSet header.
            int version = _reader.ReadInt32();
            if (version != 2) throw new NotSupportedException("ResourceReader of a version " + version + " .resources file is not supported by csharpc yet.");
            int resources = _reader.ReadInt32();
            if (resources < 0) throw new BadImageFormatException("Corrupt .resources file. Resource count is negative.");
            int typeCount = _reader.ReadInt32();
            if (typeCount < 0) throw new BadImageFormatException("Corrupt .resources file. Type count is negative.");
            _types = new string[typeCount];
            for (int i = 0; i < typeCount; i++) _types[i] = _reader.ReadString();
            // Padding to an 8-byte boundary ("PAD" bytes), then the name hashes and positions.
            long position = _stream.Position;
            int align = (int)(position & 7);
            if (align != 0) _stream.Seek(8 - align, SeekOrigin.Current);
            _stream.Seek(4L * resources, SeekOrigin.Current); // name hashes
            _namePositions = new int[resources];
            for (int i = 0; i < resources; i++) _namePositions[i] = _reader.ReadInt32();
            _dataSectionOffset = _reader.ReadInt32();
            _nameSectionOffset = _stream.Position;
        }

        /// <summary>The name and data offset of resource <paramref name="index"/> (in the file's name-hash order).</summary>
        private (string Name, int DataOffset) Entry(int index)
        {
            _stream.Seek(_nameSectionOffset + _namePositions[index], SeekOrigin.Begin);
            int byteLength = _reader.Read7BitEncodedInt();
            byte[] bytes = _reader.ReadBytes(byteLength);
            int dataOffset = _reader.ReadInt32();
            return (Encoding.Unicode.GetString(bytes), dataOffset);
        }

        private void EnsureOpen()
        {
            if (_reader == null) throw new InvalidOperationException("ResourceReader is closed.");
        }

        public void GetResourceData(string resourceName, out string resourceType, out byte[] resourceData)
        {
            ArgumentNullException.ThrowIfNull(resourceName);
            EnsureOpen();
            // The data of a resource runs to the next data offset in the file (or the end of the stream).
            long start = -1, end = _stream.Length;
            var offsets = new long[_namePositions.Length];
            for (int i = 0; i < _namePositions.Length; i++)
            {
                var (name, dataOffset) = Entry(i);
                offsets[i] = _dataSectionOffset + dataOffset;
                if (name == resourceName) start = offsets[i];
            }
            if (start < 0) throw new ArgumentException("Resource '" + resourceName + "' was not found.", "resourceName");
            foreach (long offset in offsets) if (offset > start && offset < end) end = offset;
            _stream.Seek(start, SeekOrigin.Begin);
            resourceType = TypeName(_reader.Read7BitEncodedInt());
            resourceData = _reader.ReadBytes((int)(end - _stream.Position));
        }

        private const int ByteArrayCode = 0x20, StreamCode = 0x21;

        /// <summary>GetResourceData's type: "ResourceTypeCode.X" for the built-in codes, the type table's name for user types.</summary>
        private string TypeName(int typeCode)
        {
            if (typeCode >= 0 && typeCode < s_typeCodeNames.Length) return s_typeCodeNames[typeCode];
            if (typeCode == ByteArrayCode) return "ResourceTypeCode.ByteArray";
            if (typeCode == StreamCode) return "ResourceTypeCode.Stream";
            if (typeCode >= StartOfUserTypes && typeCode - StartOfUserTypes < _types.Length) return _types[typeCode - StartOfUserTypes];
            throw new BadImageFormatException("Corrupt .resources file. The specified type doesn't exist.");
        }

        /// <summary>The value of a primitive-typed resource at the reader's position (its type code already read).</summary>
        private object ReadValue(int typeCode, string name)
        {
            switch (typeCode)
            {
                case 0: return null;
                case 1: return _reader.ReadString();
                case 2: return _reader.ReadBoolean();
                case 3: return (char)_reader.ReadUInt16();
                case 4: return _reader.ReadByte();
                case 5: return _reader.ReadSByte();
                case 6: return _reader.ReadInt16();
                case 7: return _reader.ReadUInt16();
                case 8: return _reader.ReadInt32();
                case 9: return _reader.ReadUInt32();
                case 10: return _reader.ReadInt64();
                case 11: return _reader.ReadUInt64();
                case 12: return _reader.ReadSingle();
                case 13: return _reader.ReadDouble();
                case 15: return DateTime.FromBinary(_reader.ReadInt64());
                case 16: return new TimeSpan(_reader.ReadInt64());
                case ByteArrayCode: return _reader.ReadBytes(_reader.ReadInt32());
            }
            throw new NotSupportedException("The value of resource '" + name + "' (" + TypeName(typeCode)
                + ") is not supported by csharpc yet; GetResourceData returns its bytes.");
        }

        public IDictionaryEnumerator GetEnumerator()
        {
            EnsureOpen();
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Close() => Dispose();

        public void Dispose()
        {
            _reader?.Dispose();
            _reader = null;
            _stream = null;
        }

        private sealed class Enumerator : IDictionaryEnumerator
        {
            private readonly ResourceReaderImpl _owner;
            private int _index = -1;

            internal Enumerator(ResourceReaderImpl owner) { _owner = owner; }

            public bool MoveNext()
            {
                _owner.EnsureOpen();
                if (_index >= _owner._namePositions.Length - 1) { _index = _owner._namePositions.Length; return false; }
                _index++;
                return true;
            }

            private void EnsureCurrent()
            {
                if (_index < 0 || _index >= _owner._namePositions.Length) throw new InvalidOperationException("Enumeration has either not started or has already finished.");
                _owner.EnsureOpen();
            }

            public object Key { get { EnsureCurrent(); return _owner.Entry(_index).Name; } }

            public object Value
            {
                get
                {
                    EnsureCurrent();
                    var (name, dataOffset) = _owner.Entry(_index);
                    _owner._stream.Seek(_owner._dataSectionOffset + dataOffset, SeekOrigin.Begin);
                    return _owner.ReadValue(_owner._reader.Read7BitEncodedInt(), name);
                }
            }

            public DictionaryEntry Entry => new DictionaryEntry(Key, Value);
            public object Current => Entry;
            public void Reset() { _index = -1; }
        }
    }
}

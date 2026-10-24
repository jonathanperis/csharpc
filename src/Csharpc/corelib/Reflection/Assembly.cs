// System.Reflection.Assembly / AssemblyName and System.Version: what Type.Assembly,
// Assembly.GetExecutingAssembly() and GetEntryAssembly() hand out. csharpc compiles a closed world,
// so an assembly is just its identity: the full name, computed at compile time
// (ModuleCodeGen.TypeInfo.cs: the program's own assemblies, IL assemblies, and the shared framework's
// implementation assemblies reached through type forwards, e.g. System.Private.CoreLib).
#nullable disable
using System;
using System.Reflection;
using System.Threading;

namespace CsharpcCoreLib
{
    [Shadows("System.Reflection.Assembly")]
    public class AssemblyImpl
    {
        private readonly string _fullName;
        private AssemblyImpl _next;
        private static AssemblyImpl s_first;

        private AssemblyImpl(string fullName) { _fullName = fullName; }

        /// <summary>The one Assembly object for a full name, so == and ReferenceEquals hold as in .NET.</summary>
        internal static Assembly ForName(string fullName)
        {
            AssemblyImpl created = null;
            while (true)
            {
                var head = Volatile.Read(ref s_first);
                for (var a = head; a != null; a = a._next)
                    if (a._fullName == fullName) return (Assembly)(object)a;
                created ??= new AssemblyImpl(fullName);
                created._next = head;
                if (Interlocked.CompareExchange(ref s_first, created, head) == head) return (Assembly)(object)created;
            }
        }

        /// <summary>The identity compiled-in attribute lookups dispatch on (FunctionCodeGen.AssemblyAttributes.cs).</summary>
        internal static string NameOf(Assembly assembly) => ((AssemblyImpl)(object)assembly)._fullName;

        public virtual string FullName => _fullName;
        public virtual bool IsDynamic => false;
        public virtual AssemblyName GetName() => AssemblyNameImpl.FromFullName(_fullName);
        public override string ToString() => _fullName;
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();

        public static bool operator ==(AssemblyImpl left, AssemblyImpl right) => ReferenceEquals(left, right);
        public static bool operator !=(AssemblyImpl left, AssemblyImpl right) => !ReferenceEquals(left, right);
    }

    [Shadows("System.Reflection.AssemblyName")]
    public sealed class AssemblyNameImpl
    {
        private string _fullName;
        private string _name;
        private Version _version;
        private string _culture;
        private byte[] _publicKeyToken;

        private AssemblyNameImpl() { }

        /// <summary>
        /// From a display name csharpc produced itself ("Name, Version=a.b.c.d, Culture=neutral,
        /// PublicKeyToken=..."): no quoting or escaping to handle.
        /// </summary>
        internal static AssemblyName FromFullName(string fullName)
        {
            var result = new AssemblyNameImpl { _fullName = fullName, _culture = "" };
            var parts = fullName.Split(',');
            result._name = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var part = parts[i].Trim();
                var eq = part.IndexOf('=');
                var key = part.Substring(0, eq);
                var value = part.Substring(eq + 1);
                if (key == "Version")
                {
                    var n = value.Split('.');
                    result._version = new Version(int.Parse(n[0]), int.Parse(n[1]), int.Parse(n[2]), int.Parse(n[3]));
                }
                else if (key == "Culture") result._culture = value == "neutral" ? "" : value;
                else if (key == "PublicKeyToken")
                {
                    var token = value == "null" ? new byte[0] : new byte[value.Length / 2];
                    for (var k = 0; k < token.Length; k++) token[k] = (byte)((HexValue(value[2 * k]) << 4) | HexValue(value[2 * k + 1]));
                    result._publicKeyToken = token;
                }
            }
            return (AssemblyName)(object)result;
        }

        private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

        public string Name => _name;
        public Version Version => _version;
        public string CultureName => _culture;
        public string FullName => _fullName;
        public byte[] GetPublicKeyToken()
        {
            if (_publicKeyToken == null) return null;
            var copy = new byte[_publicKeyToken.Length];
            for (var i = 0; i < copy.Length; i++) copy[i] = _publicKeyToken[i];
            return copy;
        }
        public override string ToString() => _fullName;
    }

    /// <summary>System.Version (dotnet/runtime's semantics; parsing is not implemented yet).</summary>
    [Shadows("System.Version")]
    public sealed class VersionImpl : IComparable, IComparable<Version>, IEquatable<Version>
    {
        private readonly int _Major;
        private readonly int _Minor;
        private readonly int _Build;
        private readonly int _Revision;

        public VersionImpl(int major, int minor, int build, int revision)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(major, nameof(major));
            ArgumentOutOfRangeException.ThrowIfNegative(minor, nameof(minor));
            ArgumentOutOfRangeException.ThrowIfNegative(build, nameof(build));
            ArgumentOutOfRangeException.ThrowIfNegative(revision, nameof(revision));
            _Major = major; _Minor = minor; _Build = build; _Revision = revision;
        }

        public VersionImpl(int major, int minor, int build)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(major, nameof(major));
            ArgumentOutOfRangeException.ThrowIfNegative(minor, nameof(minor));
            ArgumentOutOfRangeException.ThrowIfNegative(build, nameof(build));
            _Major = major; _Minor = minor; _Build = build; _Revision = -1;
        }

        public VersionImpl(int major, int minor)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(major, nameof(major));
            ArgumentOutOfRangeException.ThrowIfNegative(minor, nameof(minor));
            _Major = major; _Minor = minor; _Build = -1; _Revision = -1;
        }

        public VersionImpl() { _Build = -1; _Revision = -1; }

        private static VersionImpl Of(Version v) => (VersionImpl)(object)v;

        public int Major => _Major;
        public int Minor => _Minor;
        public int Build => _Build;
        public int Revision => _Revision;
        public short MajorRevision => (short)(_Revision >> 16);
        public short MinorRevision => (short)(_Revision & 0xFFFF);

        private VersionImpl(VersionImpl version)
        {
            _Major = version._Major; _Minor = version._Minor; _Build = version._Build; _Revision = version._Revision;
        }

        public object Clone() => new VersionImpl(this);

        public int CompareTo(object version)
        {
            if (version == null) return 1;
            if (version is Version v) return CompareTo(v);
            throw new ArgumentException("Object must be of type Version.", nameof(version));
        }

        public int CompareTo(Version value)
        {
            if (value is null) return 1;
            var other = Of(value);
            if (ReferenceEquals(other, this)) return 0;
            return _Major != other._Major ? (_Major > other._Major ? 1 : -1) :
                _Minor != other._Minor ? (_Minor > other._Minor ? 1 : -1) :
                _Build != other._Build ? (_Build > other._Build ? 1 : -1) :
                _Revision != other._Revision ? (_Revision > other._Revision ? 1 : -1) :
                0;
        }

        public override bool Equals(object obj) => Equals(obj as Version);

        public bool Equals(Version obj)
        {
            var other = Of(obj);
            return ReferenceEquals(other, this) || (other is not null && _Major == other._Major && _Minor == other._Minor
                && _Build == other._Build && _Revision == other._Revision);
        }

        public override int GetHashCode()
        {
            int accumulator = 0;
            accumulator |= (_Major & 0x0000000F) << 28;
            accumulator |= (_Minor & 0x000000FF) << 20;
            accumulator |= (_Build & 0x000000FF) << 12;
            accumulator |= (_Revision & 0x00000FFF);
            return accumulator;
        }

        private int DefaultFormatFieldCount => _Build == -1 ? 2 : _Revision == -1 ? 3 : 4;

        public override string ToString() => ToString(DefaultFormatFieldCount);

        public string ToString(int fieldCount)
        {
            switch (fieldCount)
            {
                case 0: return "";
                case 1: return _Major.ToString();
                case 2: return _Major.ToString() + "." + _Minor.ToString();
                default:
                    if ((uint)fieldCount > 4)
                        throw new ArgumentException("Argument must be between 0 and 4.", nameof(fieldCount));
                    if (_Build == -1)
                        throw new ArgumentException("Argument must be between 0 and 2.", nameof(fieldCount));
                    if (fieldCount == 3)
                        return _Major.ToString() + "." + _Minor.ToString() + "." + _Build.ToString();
                    if (_Revision == -1)
                        throw new ArgumentException("Argument must be between 0 and 3.", nameof(fieldCount));
                    return _Major.ToString() + "." + _Minor.ToString() + "." + _Build.ToString() + "." + _Revision.ToString();
            }
        }

        public static bool operator ==(VersionImpl v1, VersionImpl v2) => v2 is null ? v1 is null : ReferenceEquals(v2, v1) || v2.Equals((Version)(object)v1);
        public static bool operator !=(VersionImpl v1, VersionImpl v2) => !(v1 == v2);

        public static bool operator <(VersionImpl v1, VersionImpl v2)
        {
            if (v1 is null) return v2 is not null;
            return v1.CompareTo((Version)(object)v2) < 0;
        }

        public static bool operator <=(VersionImpl v1, VersionImpl v2)
        {
            if (v1 is null) return true;
            return v1.CompareTo((Version)(object)v2) <= 0;
        }

        public static bool operator >(VersionImpl v1, VersionImpl v2) => v2 < v1;
        public static bool operator >=(VersionImpl v1, VersionImpl v2) => v2 <= v1;
    }
}

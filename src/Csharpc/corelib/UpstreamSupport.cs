// Contract members that CoreLib IL taken from the framework (tools/il-first/slicer, issue #191 part B) calls and that csharpc serves itself.
// Nothing here is reached unless a slice assembly is referenced (CSHARPC_UPSTREAM_MAP).
#nullable disable
using System;
using System.Resources;

namespace CsharpcCoreLib
{
    /// <summary>
    /// IInternalStringEqualityComparer / NonRandomizedStringEqualityComparer, the internal comparer family Dictionary and HashSet
    /// consult for string keys: the fast unseeded hash first, the Marvin-seeded one (string.GetHashCode) after many collisions, so that
    /// colliding keys chosen by an attacker cost a rehash, not a linear scan per insert. csharpc serves the family itself because upstream's
    /// reaches Marvin's OrdinalIgnoreCase casing through ICU and GenericEqualityComparer, which csharpc's EqualityComparer does not have.
    /// </summary>
    public interface UpstreamInternalStringEqualityComparer
    {
        System.Collections.Generic.IEqualityComparer<string> GetUnderlyingEqualityComparer();

        static System.Collections.Generic.IEqualityComparer<string> GetUnderlyingEqualityComparer(System.Collections.Generic.IEqualityComparer<string> comparer) =>
            comparer is UpstreamInternalStringEqualityComparer wrapped ? wrapped.GetUnderlyingEqualityComparer() : comparer;
    }

    /// <summary>
    /// What Dictionary and HashSet hold for string keys with the default, Ordinal or OrdinalIgnoreCase comparer (.NET's
    /// NonRandomizedStringEqualityComparer): the same comparison, the fast unseeded hash until an operation sees too many collisions, and the
    /// alternate-key (ReadOnlySpan&lt;char&gt;) side that AlternateLookup requires of the collection's comparer. The collections' Comparer property
    /// gives the comparer they were created with back. The wrapper of the default comparer is a singleton, so the default-comparer type guard in the
    /// code generator (FunctionCodeGen.DefaultComparers) can recognize it and compare and hash strings inline. The Ordinal and OrdinalIgnoreCase
    /// wrappers are made by those comparers (StringComparerImpl.CreateNonRandomizedWrapper): a program that never creates them does not carry them.
    /// </summary>
    public class UpstreamNonRandomizedStringEqualityComparer : System.Collections.Generic.IEqualityComparer<string>,
        System.Collections.Generic.IAlternateEqualityComparer<ReadOnlySpan<char>, string>, UpstreamInternalStringEqualityComparer
    {
        private readonly System.Collections.Generic.IEqualityComparer<string> _underlying;

        private UpstreamNonRandomizedStringEqualityComparer(System.Collections.Generic.IEqualityComparer<string> underlying) { _underlying = underlying; }

        private static UpstreamNonRandomizedStringEqualityComparer s_default;

        internal static UpstreamNonRandomizedStringEqualityComparer ForOrdinal(System.Collections.Generic.IEqualityComparer<string> ordinal) => new(ordinal);
        internal static UpstreamNonRandomizedStringEqualityComparer ForOrdinalIgnoreCase(System.Collections.Generic.IEqualityComparer<string> ordinalIgnoreCase) =>
            new IgnoreCase(ordinalIgnoreCase);

        /// <summary>The guard's operand: the wrapper of the default comparer, as an object.</summary>
        public static object WrappedDefault() => s_default ??= new(System.Collections.Generic.EqualityComparer<string>.Default);

        /// <summary>
        /// string.GetNonRandomizedHashCode (dotnet/runtime String.Comparison.cs), for the guard's inline path and the wrapper: two lanes over the chars
        /// two at a time. An odd tail reads a zero where upstream reads the terminator, which csharpc's strings do not guarantee.
        /// </summary>
        public static unsafe int HashDefault(string value)
        {
            fixed (char* chars = CoreIntrinsics.StringChars(value))
            {
                uint hash1 = (5381 << 16) + 5381;
                uint hash2 = hash1;
                uint* pair = (uint*)chars;
                int length = value.Length;
                while (length >= 4)
                {
                    hash1 = (((hash1 << 5) | (hash1 >> 27)) + hash1) ^ System.Runtime.CompilerServices.Unsafe.ReadUnaligned<uint>(pair);
                    hash2 = (((hash2 << 5) | (hash2 >> 27)) + hash2) ^ System.Runtime.CompilerServices.Unsafe.ReadUnaligned<uint>(pair + 1);
                    pair += 2;
                    length -= 4;
                }
                if (length == 3)
                {
                    hash1 = (((hash1 << 5) | (hash1 >> 27)) + hash1) ^ System.Runtime.CompilerServices.Unsafe.ReadUnaligned<uint>(pair);
                    hash2 = (((hash2 << 5) | (hash2 >> 27)) + hash2) ^ chars[value.Length - 1];
                }
                else if (length > 0)
                {
                    uint last = length == 2 ? System.Runtime.CompilerServices.Unsafe.ReadUnaligned<uint>(pair) : chars[value.Length - 1];
                    hash2 = (((hash2 << 5) | (hash2 >> 27)) + hash2) ^ last;
                }
                return (int)(hash1 + hash2 * 1566083941);
            }
        }

        // NonRandomizedStringEqualityComparer.GetStringComparer
        public static System.Collections.Generic.IEqualityComparer<string> GetStringComparer(object comparer) =>
            ReferenceEquals(comparer, System.Collections.Generic.EqualityComparer<string>.Default) ? (UpstreamNonRandomizedStringEqualityComparer)WrappedDefault()
            : (comparer as StringComparerImpl)?.CreateNonRandomizedWrapper();

        public virtual bool Equals(string x, string y) => string.Equals(x, y);
        public virtual int GetHashCode(string obj) => HashDefault(obj);

        // Span keys hash as the string with those chars, so either finds the other's entries.
        public virtual bool Equals(ReadOnlySpan<char> alternate, string other) => other != null && alternate.SequenceEqual(other.AsSpan());
        public virtual int GetHashCode(ReadOnlySpan<char> alternate) => NonRandomizedStringHash.Hash(alternate);
        public string Create(ReadOnlySpan<char> alternate) => new string(alternate);

        public System.Collections.Generic.IEqualityComparer<string> GetUnderlyingEqualityComparer() => _underlying;

        /// <summary>After too many collisions: the same equality with the seeded hash (string.GetHashCode is Marvin with a per-process random seed).</summary>
        public UpstreamRandomizedStringEqualityComparer GetRandomizedEqualityComparer() => new(_underlying);

        private sealed class IgnoreCase(System.Collections.Generic.IEqualityComparer<string> underlying) : UpstreamNonRandomizedStringEqualityComparer(underlying)
        {
            public override bool Equals(string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
            public override int GetHashCode(string obj) => NonRandomizedStringHash.HashOrdinalIgnoreCase(obj);
            public override bool Equals(ReadOnlySpan<char> alternate, string other) => other != null && alternate.Equals(other.AsSpan(), StringComparison.OrdinalIgnoreCase);
            public override int GetHashCode(ReadOnlySpan<char> alternate) => NonRandomizedStringHash.HashOrdinalIgnoreCase(alternate);
        }
    }

    /// <summary>
    /// string.GetNonRandomizedHashCode / GetNonRandomizedHashCodeOrdinalIgnoreCase (dotnet/runtime String.Comparison.cs) for span keys and the
    /// OrdinalIgnoreCase wrapper: two lanes over the chars two at a time, so a span hashes like the string with those chars. Bucket order of the
    /// collections served from IL depends on it.
    /// </summary>
    internal static class NonRandomizedStringHash
    {
        private const uint NormalizeToLowercase = 0x0020_0020u;

        // Chars are read two at a time (one uint); an odd tail reads a zero in place of upstream's terminator.
        private static uint Pair(ReadOnlySpan<char> s, int i) => (uint)s[i] | (i + 1 < s.Length ? (uint)s[i + 1] << 16 : 0u);
        private static uint Rol5(uint v) => (v << 5) | (v >> 27);
        private static bool AllAscii(uint value) => (value & ~0x007F_007Fu) == 0;

        internal static int Hash(ReadOnlySpan<char> s)
        {
            uint hash1 = (5381 << 16) + 5381;
            uint hash2 = hash1;
            int length = s.Length, i = 0;
            while (length > 2)
            {
                length -= 4;
                hash1 = (Rol5(hash1) + hash1) ^ Pair(s, i);
                hash2 = (Rol5(hash2) + hash2) ^ Pair(s, i + 2);
                i += 4;
            }
            if (length > 0) hash2 = (Rol5(hash2) + hash2) ^ Pair(s, i);
            return (int)(hash1 + (hash2 * 1566083941));
        }

        internal static int HashOrdinalIgnoreCase(string s) => HashOrdinalIgnoreCase(s.AsSpan());

        internal static int HashOrdinalIgnoreCase(ReadOnlySpan<char> s)
        {
            uint hash1 = (5381 << 16) + 5381;
            uint hash2 = hash1;
            int length = s.Length, i = 0;
            while (length > 2)
            {
                uint p0 = Pair(s, i), p1 = Pair(s, i + 2);
                if (!AllAscii(p0 | p1)) return HashOrdinalIgnoreCaseSlow(hash1, hash2, s.Slice(i));
                length -= 4;
                hash1 = (Rol5(hash1) + hash1) ^ (p0 | NormalizeToLowercase);
                hash2 = (Rol5(hash2) + hash2) ^ (p1 | NormalizeToLowercase);
                i += 4;
            }
            if (length > 0)
            {
                uint p0 = Pair(s, i);
                if (!AllAscii(p0)) return HashOrdinalIgnoreCaseSlow(hash1, hash2, s.Slice(i));
                hash2 = (Rol5(hash2) + hash2) ^ (p0 | NormalizeToLowercase);
            }
            return (int)(hash1 + (hash2 * 1566083941));
        }

        // The rest upper-cased (Ordinal.ToUpperOrdinal), then the same loop.
        private static int HashOrdinalIgnoreCaseSlow(uint hash1, uint hash2, ReadOnlySpan<char> rest)
        {
            string upper = OrdinalCasing.ToUpperOrdinal(new string(rest));
            int length = upper.Length, i = 0;
            ReadOnlySpan<char> u = upper.AsSpan();
            while (length > 2)
            {
                length -= 4;
                hash1 = (Rol5(hash1) + hash1) ^ (Pair(u, i) | NormalizeToLowercase);
                hash2 = (Rol5(hash2) + hash2) ^ (Pair(u, i + 2) | NormalizeToLowercase);
                i += 4;
            }
            if (length > 0) hash2 = (Rol5(hash2) + hash2) ^ (Pair(u, i) | NormalizeToLowercase);
            return (int)(hash1 + (hash2 * 1566083941));
        }
    }

    /// <summary>
    /// RandomizedStringEqualityComparer: the equality of the comparer it replaces (default, Ordinal or OrdinalIgnoreCase) with a hash no input
    /// can be prepared against: the comparer's own hash, which for strings is Marvin, seeded from the OS random number generator when the process
    /// starts (runtime/strings.c; OrdinalIgnoreCase hashes the upper-cased string).
    /// </summary>
    public sealed class UpstreamRandomizedStringEqualityComparer : System.Collections.Generic.IEqualityComparer<string>,
        System.Collections.Generic.IAlternateEqualityComparer<ReadOnlySpan<char>, string>, UpstreamInternalStringEqualityComparer
    {
        private readonly System.Collections.Generic.IEqualityComparer<string> _underlying;
        private readonly System.Collections.Generic.IAlternateEqualityComparer<ReadOnlySpan<char>, string> _alternate;

        internal UpstreamRandomizedStringEqualityComparer(System.Collections.Generic.IEqualityComparer<string> underlying)
        {
            _underlying = underlying;
            _alternate = underlying as System.Collections.Generic.IAlternateEqualityComparer<ReadOnlySpan<char>, string>;
        }

        public static UpstreamRandomizedStringEqualityComparer Create(System.Collections.Generic.IEqualityComparer<string> underlyingComparer, bool ignoreCase) =>
            new(underlyingComparer);

        public bool Equals(string x, string y) => _underlying.Equals(x, y);
        public int GetHashCode(string obj) => _underlying.GetHashCode(obj);

        // (the default comparer takes no span keys: it is the ordinal comparison and the string's own hash)
        public bool Equals(ReadOnlySpan<char> alternate, string other) =>
            _alternate is not null ? _alternate.Equals(alternate, other) : other != null && alternate.SequenceEqual(other.AsSpan());
        public int GetHashCode(ReadOnlySpan<char> alternate) =>
            _alternate is not null ? _alternate.GetHashCode(alternate) : new string(alternate).GetHashCode();
        public string Create(ReadOnlySpan<char> alternate) => new string(alternate);

        public System.Collections.Generic.IEqualityComparer<string> GetUnderlyingEqualityComparer() => _underlying;
    }

    public static unsafe class UpstreamSupport
    {
        /// <summary>
        /// Interop.GetRandomBytes, Random's default seed: OS entropy (getentropy, rand_s, WASI random_get) through the runtime's csrt_os_random,
        /// the same source string hashing is seeded from; the program stops if the OS cannot provide it, as CoreCLR fails fast.
        /// </summary>
        public static void GetRandomBytes(byte* buffer, int length)
        {
            if (length > 0) CoreIntrinsics.csrt_os_random(buffer, (nuint)length);
        }

        private static ResourceManager s_strings;

        /// <summary>
        /// SR.InternalGetResourceString: the text of a System.Private.CoreLib resource key. CoreLib's own lookup bootstraps its
        /// ResourceManager/RuntimeResourceSet by hand to survive recursion during start-up; csharpc registers the embedded
        /// .resources of every IL assembly at compile time, so the ordinary ResourceManager finds them.
        /// </summary>
        public static string GetResourceString(string key)
        {
            if (key.Length == 0) return key;
            var manager = s_strings ??= new ResourceManager("System.Private.CoreLib.Strings", typeof(object).Assembly);
            return manager.GetString(key) ?? key;
        }

        /// <summary>
        /// SR.GetUsingResourceKeysSwitchValue: AppContext's "System.Resources.UseSystemResourceKeys", false as in the shared framework's runtimeconfig
        /// and under NativeAOT (ILLink's substitution of CoreLib's SR). Without it SR's static constructor reads the switch through AppContext and
        /// its dictionaries: about twenty functions in every program that reaches an SR text.
        /// </summary>
        public static bool GetUsingResourceKeys() => false;

        /// <summary>
        /// ThrowHelper.GetKeyNotFoundException(key): SR.Format(Arg_KeyNotFoundWithKey, key), that is string.Format with the current culture,
        /// which formats the key through IFormattable (ToString(null, culture)) and otherwise ToString(). Written out so that a program that
        /// reads a Dictionary does not carry the composite-format parser and every number formatter for the sake of an exception message.
        /// </summary>
        public static System.Collections.Generic.KeyNotFoundException GetKeyNotFoundException(object key) =>
            new System.Collections.Generic.KeyNotFoundException("The given key '" + KeyText(key) + "' was not present in the dictionary.");

        /// <summary>ThrowHelper.GetAddingDuplicateWithKeyArgumentException(key), formatted as GetKeyNotFoundException's message is.</summary>
        public static ArgumentException GetAddingDuplicateWithKeyArgumentException(object key) =>
            new ArgumentException("An item with the same key has already been added. Key: " + KeyText(key));

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        public static void ThrowKeyNotFoundException<T>(T key) =>
            throw new System.Collections.Generic.KeyNotFoundException("The given key '" + KeyText(key) + "' was not present in the dictionary.");

        [System.Diagnostics.CodeAnalysis.DoesNotReturn]
        public static void ThrowAddingDuplicateWithKeyArgumentException<T>(T key) =>
            throw new ArgumentException("An item with the same key has already been added. Key: " + KeyText(key));

        // string.Format("{0}", key): for the types a collection is usually keyed by, ToString() is what it calls (IFormattable.ToString(null, current
        // culture) is ToString() for them); the specialization per T leaves the general path out of programs that never need it.
        private static string KeyText<T>(T key)
        {
            if (typeof(T) == typeof(string)) return (string)(object)key ?? "";
            if (typeof(T) == typeof(int)) return ((int)(object)key).ToString();
            if (typeof(T) == typeof(long)) return ((long)(object)key).ToString();
            if (typeof(T) == typeof(uint)) return ((uint)(object)key).ToString();
            if (typeof(T) == typeof(ulong)) return ((ulong)(object)key).ToString();
            if (typeof(T) == typeof(char)) return ((char)(object)key).ToString();
            return KeyText((object)key);
        }

        private static string KeyText(object key) =>
            key as string ?? (key is IFormattable formattable ? formattable.ToString(null, System.Globalization.CultureInfo.CurrentCulture) : key?.ToString()) ?? "";
    }
}

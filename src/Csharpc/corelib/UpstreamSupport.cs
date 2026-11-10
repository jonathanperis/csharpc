// Contract members that CoreLib IL taken from the framework (tools/il-first/slicer, issue #191 part B) calls and that csharpc serves itself.
// Nothing here is reached unless a slice assembly is referenced (CSHARPC_UPSTREAM_MAP).
#nullable disable
using System;
using System.Resources;

namespace CsharpcCoreLib
{
    /// <summary>
    /// IInternalStringEqualityComparer / NonRandomizedStringEqualityComparer, the internal comparer family Dictionary and HashSet
    /// consult for string keys (randomized hashing after many collisions; Marvin, OrdinalIgnoreCase casing over ICU). csharpc's
    /// string keys use the default comparer, which hashes the same characters every run, so the contract is "no special comparer".
    /// </summary>
    public interface UpstreamInternalStringEqualityComparer
    {
        System.Collections.Generic.IEqualityComparer<string> GetUnderlyingEqualityComparer();

        static System.Collections.Generic.IEqualityComparer<string> GetUnderlyingEqualityComparer(System.Collections.Generic.IEqualityComparer<string> comparer) =>
            comparer is UpstreamInternalStringEqualityComparer wrapped ? wrapped.GetUnderlyingEqualityComparer() : comparer;
    }

    /// <summary>
    /// What Dictionary and HashSet hold for string keys and the default comparer (.NET's WrappedAroundDefaultComparer): the same ordinal
    /// comparison, plus the alternate-key (ReadOnlySpan&lt;char&gt;) side that AlternateLookup requires of the collection's comparer.
    /// The collections' Comparer property gives the default comparer back. A singleton, so the default-comparer type guard in the code
    /// generator (FunctionCodeGen.DefaultComparers) can recognize it and compare strings inline.
    /// </summary>
    public sealed class UpstreamWrappedDefaultStringComparer : System.Collections.Generic.IEqualityComparer<string>,
        System.Collections.Generic.IAlternateEqualityComparer<ReadOnlySpan<char>, string>, UpstreamInternalStringEqualityComparer
    {
        public static readonly UpstreamWrappedDefaultStringComparer Instance = new();

        public bool Equals(string x, string y) => string.Equals(x, y);
        public int GetHashCode(string obj) => obj is null ? 0 : obj.GetHashCode();
        public bool Equals(ReadOnlySpan<char> alternate, string other) => other != null && alternate.SequenceEqual(other.AsSpan());
        public int GetHashCode(ReadOnlySpan<char> alternate) => new string(alternate).GetHashCode();
        public string Create(ReadOnlySpan<char> alternate) => new string(alternate);
        public System.Collections.Generic.IEqualityComparer<string> GetUnderlyingEqualityComparer() => System.Collections.Generic.EqualityComparer<string>.Default;
        // The guard's operand: the singleton, as an object.
        public static object Wrapped() => Instance;
    }

    public sealed class UpstreamNonRandomizedStringEqualityComparer
    {
        // Only the default comparer is wrapped; StringComparer.Ordinal / OrdinalIgnoreCase already take span keys, and the randomized
        // fallback after many collisions upstream is not used (hash flooding stays as it is with the corelib's collections today).
        public static System.Collections.Generic.IEqualityComparer<string> GetStringComparer(object comparer) =>
            comparer == (object)System.Collections.Generic.EqualityComparer<string>.Default ? UpstreamWrappedDefaultStringComparer.Instance : null;
        // Upstream returns RandomizedStringEqualityComparer, redirected to this class as well (never created: GetStringComparer is null).
        public UpstreamNonRandomizedStringEqualityComparer GetRandomizedEqualityComparer() => null;
        public System.Collections.Generic.IEqualityComparer<string> GetUnderlyingEqualityComparer() => null; // OrderedDictionary.Resize, after a type test no object passes
        public static UpstreamNonRandomizedStringEqualityComparer Create(System.Collections.Generic.IEqualityComparer<string> underlyingComparer, bool ignoreCase) => null;
    }

    public static class UpstreamSupport
    {
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

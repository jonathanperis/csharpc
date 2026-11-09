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

        static System.Collections.Generic.IEqualityComparer<string> GetUnderlyingEqualityComparer(System.Collections.Generic.IEqualityComparer<string> comparer) => comparer;
    }

    public sealed class UpstreamNonRandomizedStringEqualityComparer
    {
        public static System.Collections.Generic.IEqualityComparer<string> GetStringComparer(object comparer) => null;
        // Upstream returns RandomizedStringEqualityComparer, redirected to this class as well (never created: GetStringComparer is null).
        public UpstreamNonRandomizedStringEqualityComparer GetRandomizedEqualityComparer() => null;
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
    }
}

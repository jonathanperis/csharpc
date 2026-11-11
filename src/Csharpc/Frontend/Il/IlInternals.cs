using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Csharpc.Frontend;

/// <summary>Access to the internals of a program given as IL, for the code csharpc generates next to it (ResourceConverters).</summary>
public static class IlInternals
{
    /// <summary>
    /// Lets the compilation named <paramref name="toAssembly"/> see the program's internals, as an
    /// [InternalsVisibleTo] in its metadata would. Roslyn has no public way to add one to a metadata
    /// reference; its PE assembly's IVT map is filled in through reflection before any compilation binds
    /// the reference (assembly symbols are cached per metadata). False when this Roslyn has another shape.
    /// </summary>
    public static bool GrantInternalsAccess(MetadataReference reference, string toAssembly)
    {
        try
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            // The metadata the compilation binds (GetMetadata() would hand out a copy with its own PE assembly).
            if (reference is not PortableExecutableReference pe
                || typeof(PortableExecutableReference).GetMethod("GetMetadataNoCopy", instance, Type.EmptyTypes)?.Invoke(pe, null) is not AssemblyMetadata metadata)
                return false;
            var peAssembly = typeof(AssemblyMetadata).GetMethod("GetAssembly", instance, Type.EmptyTypes)?.Invoke(metadata, null);
            if (peAssembly is null) return false;
            var type = peAssembly.GetType();
            // Builds the lazy map from the assembly's own attributes.
            if (type.GetMethod("GetInternalsVisibleToPublicKeys", instance, [typeof(string)])?.Invoke(peAssembly, [toAssembly]) is System.Collections.IEnumerable keys)
                foreach (var _ in keys) { }
            if (type.GetField("_lazyInternalsVisibleToMap", instance)?.GetValue(peAssembly) is not Dictionary<string, List<ImmutableArray<byte>>> map) return false;
            if (!map.TryGetValue(toAssembly, out var list)) map[toAssembly] = list = [];
            // An empty public key: the granted assembly is not strong-named (csharpc's compilation isn't).
            if (!list.Any(k => k.IsEmpty)) list.Add(ImmutableArray<byte>.Empty);
            return true;
        }
        catch (Exception e) when (e is TargetInvocationException or MemberAccessException or InvalidCastException or ArgumentException)
        {
            return false;
        }
    }
}

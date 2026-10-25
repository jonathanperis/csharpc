using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;

namespace Csharpc.Frontend;

/// <summary>
/// Programs bind against the reference pack, where System.Runtime & co. define the core types. A
/// framework assembly compiled from IL may instead name them in System.Private.CoreLib, the
/// implementation (System.Diagnostics.TraceSource's Trace.WriteLine(object)), and Roslyn then rejects
/// a call into it with CS0012 ("'Object' is defined in an assembly that is not referenced"). This
/// builds the missing piece: an assembly with System.Private.CoreLib's identity that only forwards each
/// of its public types to the reference assembly defining it — the shared framework's System.Runtime
/// forwards the other way — so both names unify to the reference pack's types (or the corelib's, for
/// the few public CoreLib types the pack leaves out).
/// </summary>
internal static class CoreLibFacade
{
    private const string Name = "System.Private.CoreLib";
    private static MetadataReference? s_facade;
    private static readonly object s_lock = new();

    /// <summary>The facade when one of the IL references names System.Private.CoreLib (null otherwise or without a shared framework).</summary>
    public static MetadataReference? For(IEnumerable<MetadataReference> ilReferences, IEnumerable<MetadataReference> referencePack)
    {
        if (!ilReferences.OfType<PortableExecutableReference>().Any(r => r.FilePath is { } path && ReferencesCoreLib(path))) return null;
        lock (s_lock)
        {
            if (s_facade is not null) return s_facade;
            if (SharedFramework.Directory is not { } dir || !File.Exists(Path.Combine(dir, Name + ".dll"))) return null;
            // The reference pack first; then the corelib, for public CoreLib types the pack doesn't have
            // (System.Diagnostics.DebugProvider) that the corelib defines under the same name.
            var paths = referencePack.OfType<PortableExecutableReference>().Select(r => r.FilePath).OfType<string>().ToList();
            if (CoreLibAssembly.Current is { } corelib) paths.Add(corelib.Path);
            return s_facade = Build(Path.Combine(dir, Name + ".dll"), paths);
        }
    }

    private static bool ReferencesCoreLib(string path)
    {
        try
        {
            using var pe = new PEReader(File.OpenRead(path));
            var md = pe.GetMetadataReader();
            return md.AssemblyReferences.Any(h => md.StringComparer.Equals(md.GetAssemblyReference(h).Name, Name));
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or InvalidOperationException) { return false; }
    }

    private static MetadataReference Build(string coreLibPath, IReadOnlyList<string> referencePaths)
    {
        // Which reference assembly defines each public top-level type (the first one, if several do).
        var definedIn = new Dictionary<(string Ns, string Name), int>();
        var references = new List<(string Name, Version Version, byte[] PublicKey, string Culture)>();
        foreach (var path in referencePaths)
        {
            using var pe = new PEReader(File.OpenRead(path));
            var md = pe.GetMetadataReader();
            if (!md.IsAssembly) continue;
            var assembly = md.GetAssemblyDefinition();
            int index = references.Count;
            references.Add((md.GetString(assembly.Name), assembly.Version, md.GetBlobBytes(assembly.PublicKey), md.GetString(assembly.Culture)));
            foreach (var th in md.TypeDefinitions)
            {
                var t = md.GetTypeDefinition(th);
                if (!t.GetDeclaringType().IsNil || (t.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public) continue;
                definedIn.TryAdd((md.GetString(t.Namespace), md.GetString(t.Name)), index);
            }
        }

        using var coreLib = new PEReader(File.OpenRead(coreLibPath));
        var cmd = coreLib.GetMetadataReader();
        var definition = cmd.GetAssemblyDefinition();

        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(Name + ".dll"), metadata.GetOrAddGuid(new Guid("6e1d5b5a-2f43-4c1c-9a0e-0c5a1f0e7a11")), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(Name), definition.Version, default,
            metadata.GetOrAddBlob(cmd.GetBlobBytes(definition.PublicKey)), AssemblyFlags.PublicKey, AssemblyHashAlgorithm.Sha1);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var referenceHandles = new Dictionary<int, AssemblyReferenceHandle>();
        AssemblyReferenceHandle ReferenceFor(int index)
        {
            if (referenceHandles.TryGetValue(index, out var handle)) return handle;
            var (name, version, publicKey, culture) = references[index];
            return referenceHandles[index] = metadata.AddAssemblyReference(metadata.GetOrAddString(name), version,
                culture.Length == 0 ? default : metadata.GetOrAddString(culture), metadata.GetOrAddBlob(publicKey),
                publicKey.Length > 0 ? AssemblyFlags.PublicKey : 0, default);
        }
        const TypeAttributes forwarder = (TypeAttributes)0x00200000; // tdForwarder (ECMA-335 II.23.1.15)
        foreach (var th in cmd.TypeDefinitions)
        {
            var t = cmd.GetTypeDefinition(th);
            if (!t.GetDeclaringType().IsNil || (t.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public) continue;
            var ns = cmd.GetString(t.Namespace);
            var name = cmd.GetString(t.Name);
            if (!definedIn.TryGetValue((ns, name), out var index)) continue;
            metadata.AddExportedType(forwarder, metadata.GetOrAddString(ns), metadata.GetOrAddString(name), ReferenceFor(index), 0);
        }

        var builder = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata), new BlobBuilder(), flags: CorFlags.ILOnly, deterministicIdProvider: _ => new BlobContentId(Guid.Empty, 0));
        var blob = new BlobBuilder();
        builder.Serialize(blob);
        return MetadataReference.CreateFromImage(ImmutableArray.Create(blob.ToArray()));
    }
}

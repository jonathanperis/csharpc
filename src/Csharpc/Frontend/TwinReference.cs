using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;

namespace Csharpc.Frontend;

/// <summary>
/// Framework assemblies that define types of their own and also forward others to System.Private.CoreLib (System.Collections: Stack&lt;T&gt;
/// is its own, List&lt;T&gt; is CoreLib's) cannot replace their reference-pack twin as it is: Roslyn would follow the forward of List&lt;T&gt;
/// to the CoreLib facade (CoreLibFacade), which forwards it to the reference assembly that defines it, the implementation again.
/// The way out is a second copy of the twin under another identity. The reference assembly's Assembly row names it "System.Collections";
/// pointing that row at the tail of the same string ("Collections") renames the copy without touching a heap. The facade forwards the
/// CoreLib types to the copy; the copy's other types, which the implementation defines too, are made non-public (the visibility bits of
/// their TypeDef rows) so they do not clash with the implementation's, and the program, the implementation and every other IL assembly
/// meet one definition of List&lt;T&gt;.
/// </summary>
internal static class TwinReference
{
    /// <summary>The assemblies taken as IL with a renamed twin. Others that forward twin types stay facades until they are measured the same way.</summary>
    private static readonly string[] SupportedAssemblies = ["System.Collections", "System.Collections.Concurrent", "System.Memory"];

    public static bool Supported(string assemblyName) => SupportedAssemblies.Contains(assemblyName, StringComparer.OrdinalIgnoreCase);

    /// <summary>The reference assembly a renamed copy stands for ("Collections" is "System.Collections"), else the name itself.</summary>
    public static string OriginalName(string name) =>
        SupportedAssemblies.FirstOrDefault(a => CopyName(a).Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;

    /// <summary>The assembly a twin copy takes its name from: "System.Collections" is "Collections", "System.Collections.Concurrent" is "Collections.Concurrent".</summary>
    public static string CopyName(string assemblyName) => assemblyName[(assemblyName.IndexOf('.') + 1)..];

    /// <summary>
    /// For each IL reference that replaces a reference-pack assembly and forwards types to System.Private.*, a renamed copy of the
    /// reference assembly (empty without a pack or an IL reference of that kind).
    /// </summary>
    public static IReadOnlyList<PortableExecutableReference> For(IEnumerable<MetadataReference> ilReferences, IEnumerable<MetadataReference> referencePack)
    {
        var result = new List<PortableExecutableReference>();
        foreach (var il in ilReferences.OfType<PortableExecutableReference>())
        {
            if (il.FilePath is not { } implementation) continue;
            var name = Path.GetFileNameWithoutExtension(implementation);
            if (!Supported(name) || !ForwardsToPrivate(implementation)) continue;
            var twin = referencePack.OfType<PortableExecutableReference>().FirstOrDefault(r =>
                r.FilePath is { } p && Path.GetFileNameWithoutExtension(p).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (twin?.FilePath is { } twinPath && name.Contains('.') && Copy(twinPath, name, ForwardedToPrivate(implementation)) is { } copy)
                result.Add(MetadataReference.CreateFromFile(copy));
        }
        return result;
    }

    private static bool ForwardsToPrivate(string implementation) => ForwardedToPrivate(implementation).Count > 0;

    /// <summary>The types (namespace.name) the implementation forwards to System.Private.*.</summary>
    private static HashSet<string> ForwardedToPrivate(string implementation)
    {
        var forwarded = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var pe = new PEReader(File.OpenRead(implementation));
            var md = pe.GetMetadataReader();
            foreach (var h in md.ExportedTypes)
            {
                var e = md.GetExportedType(h);
                if (e.Implementation is { Kind: HandleKind.AssemblyReference } i
                    && md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)i).Name).StartsWith("System.Private.", StringComparison.Ordinal))
                    forwarded.Add(md.GetString(e.Namespace) + "." + md.GetString(e.Name));
            }
        }
        catch (Exception e) when (e is IOException or BadImageFormatException or InvalidOperationException) { }
        return forwarded;
    }

    /// <summary>The renamed copy of the reference assembly, written once to the csharpc cache directory (named by its content).</summary>
    private static string? Copy(string twinPath, string name, HashSet<string> keepPublic)
    {
        try
        {
            var image = File.ReadAllBytes(twinPath);
            var copyName = CopyName(name);
            var hash = Convert.ToHexString(SHA256.HashData(image))[..16];
            var cacheRoot = Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            var directory = Path.Combine(cacheRoot, "csharpc", "twin-" + hash + "-" + keepPublic.Count);
            var path = Path.Combine(directory, copyName + ".dll");
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(directory);
            var temp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllBytes(temp, Rename(image, copyName, keepPublic));
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException) { return null; }
    }

    /// <summary>
    /// The image with its Assembly row's Name pointing at <paramref name="newName"/>, which must be a tail of the current name (a suffix of a
    /// string in the #Strings heap is itself a string: the heap holds null-terminated UTF-8).
    /// </summary>
    internal static byte[] Rename(byte[] image, string newName, HashSet<string> keepPublic)
    {
        using var pe = new PEReader(ImmutableArray.Create(image));
        var md = pe.GetMetadataReader();
        var current = md.GetString(md.GetAssemblyDefinition().Name);
        if (!current.EndsWith("." + newName, StringComparison.Ordinal) && current != newName)
            throw new InvalidOperationException($"'{newName}' is not a tail of the assembly name '{current}'");
        var metadataStart = pe.PEHeaders.MetadataStartOffset;
        var row = metadataStart + md.GetTableMetadataOffset(TableIndex.Assembly);
        // Row: HashAlgId (4), version (8), flags (4), PublicKey (blob index), Name and Culture (string indexes); the sizes of the two
        // kinds of index are 2 or 4 bytes and tell apart by the row size.
        (int blob, int str) = md.GetTableRowSize(TableIndex.Assembly) switch
        {
            22 => (2, 2), 24 => (4, 2), 26 => (2, 4), 28 => (4, 4),
            var other => throw new InvalidOperationException($"unexpected Assembly row size {other}"),
        };
        var nameColumn = row + 16 + blob;
        var oldOffset = 0;
        for (var i = 0; i < str; i++) oldOffset |= image[nameColumn + i] << (8 * i);
        var newOffset = oldOffset + (current.Length - newName.Length); // ASCII names: one byte per char
        var patched = (byte[])image.Clone();
        for (var i = 0; i < str; i++) patched[nameColumn + i] = (byte)(newOffset >> (8 * i));
        // The top-level types the implementation does not forward stay in the copy as non-public ones (their nested types with them): TypeDef rows start with their 4-byte flags.
        var typeRows = metadataStart + md.GetTableMetadataOffset(TableIndex.TypeDef);
        var typeRowSize = md.GetTableRowSize(TableIndex.TypeDef);
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            if (!type.GetDeclaringType().IsNil || md.GetString(type.Name) == "<Module>" || keepPublic.Contains(md.GetString(type.Namespace) + "." + md.GetString(type.Name))) continue;
            var flags = typeRows + (MetadataTokens.GetRowNumber(handle) - 1) * typeRowSize;
            patched[flags] &= unchecked((byte)~0x07); // TypeAttributes.VisibilityMask: 0 is NotPublic
        }
        return patched;
    }
}

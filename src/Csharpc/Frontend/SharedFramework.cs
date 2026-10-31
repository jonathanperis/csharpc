using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace Csharpc.Frontend;

/// <summary>
/// The implementation assemblies of the .NET shared framework (shared/Microsoft.NETCore.App/&lt;ver&gt;),
/// read with System.Reflection.Metadata. User code binds against reference assemblies, where e.g.
/// List&lt;T&gt; lives in System.Collections.dll; at run time .NET resolves it through type forwards to
/// System.Private.CoreLib, and that is what Type.Assembly reports. Following the same forwards at
/// compile time gives csharpc the identical answer (and works when csharpc itself runs as NativeAOT).
/// </summary>
public static class SharedFramework
{
    private static readonly Lock s_lock = new();
    private static readonly Dictionary<string, MetadataReader?> s_readers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string, string, string), string?> s_cache = [];
    private static string? s_directory;
    private static bool s_searched;

    /// <summary>Override with CSHARPC_SHARED_FRAMEWORK=&lt;directory containing System.Private.CoreLib.dll&gt;.</summary>
    public static string? Directory
    {
        get
        {
            lock (s_lock)
            {
                if (!s_searched) { s_directory = Find(); s_searched = true; }
                return s_directory;
            }
        }
    }

    private static string? s_productVersion;

    /// <summary>
    /// The runtime's product version: System.Private.CoreLib's [AssemblyInformationalVersion] up to the
    /// commit ("11.0.0-rc.2.26504.105+abc" → "11.0.0-rc.2.26504.105"), which .NET compiles into
    /// Environment.Version and RuntimeInformation.FrameworkDescription; null without a shared framework.
    /// </summary>
    public static string? ProductVersion
    {
        get
        {
            lock (s_lock)
                if (s_productVersion is not null) return s_productVersion;
            if (Directory is not { } dir) return null;
            string? version = null;
            try
            {
                using var pe = new PEReader(File.OpenRead(Path.Combine(dir, "System.Private.CoreLib.dll")));
                var md = pe.GetMetadataReader();
                foreach (var h in md.GetAssemblyDefinition().GetCustomAttributes())
                {
                    var attribute = md.GetCustomAttribute(h);
                    var type = attribute.Constructor.Kind switch
                    {
                        HandleKind.MethodDefinition => md.GetString(md.GetTypeDefinition(md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()).Name),
                        HandleKind.MemberReference when md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent is { Kind: HandleKind.TypeReference } parent
                            => md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name),
                        _ => null,
                    };
                    if (type != "AssemblyInformationalVersionAttribute") continue;
                    var blob = md.GetBlobReader(attribute.Value);
                    if (blob.ReadUInt16() == 1) version = blob.ReadSerializedString(); // after the prolog
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException) { }
            // Fallback: the framework directory is named after the version.
            version ??= Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
            if (version.IndexOf('+') is var plus and >= 0) version = version[..plus];
            lock (s_lock) return s_productVersion = version;
        }
    }

    /// <summary>
    /// Another shared framework of the same installation (Microsoft.AspNetCore.App): the directory next to
    /// <see cref="Directory"/> with the same version, else that framework's newest 11.x; null if absent.
    /// </summary>
    public static string? DirectoryOf(string framework)
    {
        if (Directory is not { } netcore) return null;
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(netcore)));
        if (root is null) return null;
        var frameworkRoot = Path.Combine(root, framework);
        var same = Path.Combine(frameworkRoot, Path.GetFileName(Path.TrimEndingDirectorySeparator(netcore)));
        if (System.IO.Directory.Exists(same)) return same;
        if (!System.IO.Directory.Exists(frameworkRoot)) return null;
        return System.IO.Directory.EnumerateDirectories(frameworkRoot)
            .Where(d => Path.GetFileName(d).StartsWith("11.", StringComparison.Ordinal))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Assembly.FullName of the assembly that defines the top-level type <paramref name="ns"/>.<paramref name="name"/>
    /// (metadata name, e.g. "List`1"), starting from the reference assembly Roslyn bound it in and
    /// following type forwards; null when it can't be found.
    /// </summary>
    public static string? DefiningAssembly(string assemblyName, string ns, string name)
    {
        lock (s_lock)
        {
            if (s_cache.TryGetValue((assemblyName, ns, name), out var known)) return known;
            return s_cache[(assemblyName, ns, name)] = Resolve(assemblyName, ns, name, depth: 0);
        }
    }

    private static string? Resolve(string assemblyName, string ns, string name, int depth)
    {
        if (depth > 8 || Reader(assemblyName) is not { } reader) return null;
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (type.GetDeclaringType().IsNil && reader.StringComparer.Equals(type.Name, name) && reader.StringComparer.Equals(type.Namespace, ns))
                return reader.GetAssemblyDefinition().GetAssemblyName().FullName;
        }
        foreach (var handle in reader.ExportedTypes)
        {
            var exported = reader.GetExportedType(handle);
            if (!reader.StringComparer.Equals(exported.Name, name) || !reader.StringComparer.Equals(exported.Namespace, ns)) continue;
            if (exported.Implementation.Kind != HandleKind.AssemblyReference) return null;
            var target = reader.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
            return Resolve(reader.GetString(target.Name), ns, name, depth + 1);
        }
        return null;
    }

    private static readonly Dictionary<(string, string, string), string[]?> s_fieldOrder = [];

    /// <summary>
    /// Field names of the top-level type <paramref name="ns"/>.<paramref name="name"/> in declaration order,
    /// read from the implementation assembly that defines it (following type forwards from
    /// <paramref name="assemblyName"/>); null when it can't be found. Reference assemblies list members
    /// alphabetically, but .NET resolves enum aliases (TypeAttributes.NotPublic = AnsiClass = 0) by the
    /// implementation's order.
    /// </summary>
    public static string[]? FieldOrder(string assemblyName, string ns, string name)
    {
        lock (s_lock)
        {
            if (s_fieldOrder.TryGetValue((assemblyName, ns, name), out var known)) return known;
            return s_fieldOrder[(assemblyName, ns, name)] = FindFields(assemblyName, ns, name, depth: 0);
        }
    }

    private static string[]? FindFields(string assemblyName, string ns, string name, int depth)
    {
        if (depth > 8 || Reader(assemblyName) is not { } reader) return null;
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (type.GetDeclaringType().IsNil && reader.StringComparer.Equals(type.Name, name) && reader.StringComparer.Equals(type.Namespace, ns))
                return [.. type.GetFields().Select(f => reader.GetString(reader.GetFieldDefinition(f).Name))];
        }
        foreach (var handle in reader.ExportedTypes)
        {
            var exported = reader.GetExportedType(handle);
            if (!reader.StringComparer.Equals(exported.Name, name) || !reader.StringComparer.Equals(exported.Namespace, ns)) continue;
            if (exported.Implementation.Kind != HandleKind.AssemblyReference) return null;
            var target = reader.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
            return FindFields(reader.GetString(target.Name), ns, name, depth + 1);
        }
        return null;
    }

    private static readonly Dictionary<(string, string, string), string[]?> s_interfaceOrder = [];

    /// <summary>
    /// The interfaces of the top-level type <paramref name="ns"/>.<paramref name="name"/> as the implementation
    /// assembly lists them ("System.Collections.Generic.IEnumerable`1", generic definitions), following type
    /// forwards; null when it can't be found. Reference assemblies sort them alphabetically, but
    /// Type.GetInterfaces reports the implementation's order.
    /// </summary>
    public static string[]? InterfaceOrder(string assemblyName, string ns, string name)
    {
        lock (s_lock)
        {
            if (s_interfaceOrder.TryGetValue((assemblyName, ns, name), out var known)) return known;
            return s_interfaceOrder[(assemblyName, ns, name)] = FindInterfaces(assemblyName, ns, name, depth: 0);
        }
    }

    private static string[]? FindInterfaces(string assemblyName, string ns, string name, int depth)
    {
        if (depth > 8 || Reader(assemblyName) is not { } reader) return null;
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (!type.GetDeclaringType().IsNil || !reader.StringComparer.Equals(type.Name, name) || !reader.StringComparer.Equals(type.Namespace, ns)) continue;
            var names = new List<string>();
            foreach (var impl in type.GetInterfaceImplementations())
                if (TypeName(reader, reader.GetInterfaceImplementation(impl).Interface) is { } itf) names.Add(itf);
            return [.. names];
        }
        foreach (var handle in reader.ExportedTypes)
        {
            var exported = reader.GetExportedType(handle);
            if (!reader.StringComparer.Equals(exported.Name, name) || !reader.StringComparer.Equals(exported.Namespace, ns)) continue;
            if (exported.Implementation.Kind != HandleKind.AssemblyReference) return null;
            var target = reader.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
            return FindInterfaces(reader.GetString(target.Name), ns, name, depth + 1);
        }
        return null;
    }

    private static readonly Dictionary<(string, string), TypeInterfaces?> s_interfaceSignatures = [];

    /// <summary>
    /// The interfaces the implementation assembly declares for <paramref name="fullName"/> ("Ns.Name`1",
    /// "Ns.Outer+Inner"), in metadata order, with their type arguments, following type forwards from
    /// <paramref name="assemblyName"/>; null when it can't be found. Unlike the reference assemblies,
    /// this includes interfaces internal to the framework (int's IBinaryIntegerParseAndFormatInfo&lt;int&gt;),
    /// which Type.GetInterfaces reports too.
    /// </summary>
    public static TypeInterfaces? InterfacesOf(string assemblyName, string fullName)
    {
        lock (s_lock)
        {
            if (s_interfaceSignatures.TryGetValue((assemblyName, fullName), out var known)) return known;
            return s_interfaceSignatures[(assemblyName, fullName)] = FindInterfaceSignatures(assemblyName, fullName, depth: 0);
        }
    }

    private static TypeInterfaces? FindInterfaceSignatures(string assemblyName, string fullName, int depth)
    {
        if (depth > 8 || Reader(assemblyName) is not { } reader) return null;
        var names = fullName.Split('+');
        var dot = names[0].LastIndexOf('.');
        var (ns, name) = dot < 0 ? ("", names[0]) : (names[0][..dot], names[0][(dot + 1)..]);
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if (!type.GetDeclaringType().IsNil || !reader.StringComparer.Equals(type.Name, name) || !reader.StringComparer.Equals(type.Namespace, ns)) continue;
            for (var i = 1; i < names.Length; i++)
            {
                var nested = type.GetNestedTypes().Select(reader.GetTypeDefinition).Where(n => reader.StringComparer.Equals(n.Name, names[i])).ToList();
                if (nested.Count != 1) return null;
                type = nested[0];
            }
            var provider = new SignatureProvider();
            var interfaces = new List<MetadataTypeSig>();
            foreach (var impl in type.GetInterfaceImplementations())
            {
                var itf = reader.GetInterfaceImplementation(impl).Interface;
                interfaces.Add(itf.Kind switch
                {
                    HandleKind.TypeDefinition => provider.GetTypeFromDefinition(reader, (TypeDefinitionHandle)itf, 0),
                    HandleKind.TypeReference => provider.GetTypeFromReference(reader, (TypeReferenceHandle)itf, 0),
                    HandleKind.TypeSpecification => provider.GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)itf, 0),
                    _ => new MetadataUnsupported(),
                });
            }
            var variances = type.GetGenericParameters().Select(p => (int)(reader.GetGenericParameter(p).Attributes & GenericParameterAttributes.VarianceMask)).ToList();
            return new TypeInterfaces(interfaces, assemblyName, variances);
        }
        if (names.Length > 1) return null; // a nested type is forwarded with its declaring type
        foreach (var handle in reader.ExportedTypes)
        {
            var exported = reader.GetExportedType(handle);
            if (!reader.StringComparer.Equals(exported.Name, name) || !reader.StringComparer.Equals(exported.Namespace, ns)) continue;
            if (exported.Implementation.Kind != HandleKind.AssemblyReference) return null;
            var target = reader.GetAssemblyReference((AssemblyReferenceHandle)exported.Implementation);
            return FindInterfaceSignatures(reader.GetString(target.Name), fullName, depth + 1);
        }
        return null;
    }

    /// <summary>Decodes metadata type signatures into <see cref="MetadataTypeSig"/> trees.</summary>
    private sealed class SignatureProvider : ISignatureTypeProvider<MetadataTypeSig, object?>
    {
        public MetadataTypeSig GetPrimitiveType(PrimitiveTypeCode typeCode) => new MetadataPrimitive(typeCode);

        public MetadataTypeSig GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            var declaring = type.GetDeclaringType();
            return new MetadataNamed(reader.GetString(type.Namespace), reader.GetString(type.Name),
                declaring.IsNil ? null : (MetadataNamed)GetTypeFromDefinition(reader, declaring, 0), []);
        }

        public MetadataTypeSig GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            var declaring = type.ResolutionScope.Kind == HandleKind.TypeReference
                ? (MetadataNamed)GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, 0) : null;
            return new MetadataNamed(reader.GetString(type.Namespace), reader.GetString(type.Name), declaring, []);
        }

        public MetadataTypeSig GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public MetadataTypeSig GetGenericInstantiation(MetadataTypeSig genericType, System.Collections.Immutable.ImmutableArray<MetadataTypeSig> typeArguments) =>
            genericType is MetadataNamed named ? named with { Arguments = typeArguments } : new MetadataUnsupported();

        public MetadataTypeSig GetGenericTypeParameter(object? genericContext, int index) => new MetadataGenericParameter(index);
        public MetadataTypeSig GetGenericMethodParameter(object? genericContext, int index) => new MetadataUnsupported();
        public MetadataTypeSig GetSZArrayType(MetadataTypeSig elementType) => new MetadataArray(elementType, 0);
        public MetadataTypeSig GetArrayType(MetadataTypeSig elementType, ArrayShape shape) => new MetadataArray(elementType, shape.Rank);
        public MetadataTypeSig GetPointerType(MetadataTypeSig elementType) => new MetadataPointer(elementType);
        public MetadataTypeSig GetByReferenceType(MetadataTypeSig elementType) => new MetadataUnsupported();
        public MetadataTypeSig GetFunctionPointerType(MethodSignature<MetadataTypeSig> signature) => new MetadataUnsupported();
        public MetadataTypeSig GetModifiedType(MetadataTypeSig modifier, MetadataTypeSig unmodifiedType, bool isRequired) => unmodifiedType;
        public MetadataTypeSig GetPinnedType(MetadataTypeSig elementType) => new MetadataUnsupported();
    }

    /// <summary>"Namespace.Name" of a TypeDef/TypeRef, or of the generic definition of a TypeSpec instantiation.</summary>
    private static string? TypeName(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                var def = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                return reader.GetString(def.Namespace) + "." + reader.GetString(def.Name);
            case HandleKind.TypeReference:
                var reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                return reader.GetString(reference.Namespace) + "." + reader.GetString(reference.Name);
            case HandleKind.TypeSpecification:
                var blob = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
                if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return null;
                blob.ReadSignatureTypeCode(); // CLASS or VALUETYPE
                return TypeName(reader, blob.ReadTypeHandle());
            default:
                return null;
        }
    }

    private static MetadataReader? Reader(string assemblyName)
    {
        if (s_readers.TryGetValue(assemblyName, out var cached)) return cached;
        MetadataReader? reader = null;
        if (Directory is { } dir && Path.Combine(dir, assemblyName + ".dll") is var path && File.Exists(path))
        {
            // Kept open for the compiler's lifetime: a handful of small framework assemblies.
            var pe = new PEReader(File.OpenRead(path));
            if (pe.HasMetadata) reader = pe.GetMetadataReader();
        }
        return s_readers[assemblyName] = reader;
    }

    private static string? Find()
    {
        if (Environment.GetEnvironmentVariable("CSHARPC_SHARED_FRAMEWORK") is { Length: > 0 } explicitDir) return explicitDir;
        // Running on the JIT: the framework csharpc itself runs on is .NET 11's.
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        if (runtimeDir.Length > 0 && File.Exists(Path.Combine(runtimeDir, "System.Private.CoreLib.dll"))
            && Path.GetFileName(Path.TrimEndingDirectorySeparator(runtimeDir)).StartsWith("11.", StringComparison.Ordinal))
            return runtimeDir;
        IEnumerable<string?> roots =
        [
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet11"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"),
            "/usr/share/dotnet", "/usr/lib/dotnet", "/usr/local/share/dotnet",
            Environment.GetEnvironmentVariable("ProgramFiles") is { } pf ? Path.Combine(pf, "dotnet") : null,
        ];
        return roots.Where(r => r is not null && System.IO.Directory.Exists(Path.Combine(r, "shared", "Microsoft.NETCore.App")))
            .SelectMany(r => System.IO.Directory.EnumerateDirectories(Path.Combine(r!, "shared", "Microsoft.NETCore.App")))
            .Where(d => Path.GetFileName(d).StartsWith("11.", StringComparison.Ordinal) && File.Exists(Path.Combine(d, "System.Private.CoreLib.dll")))
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

/// <summary>A type as a metadata signature names it (SharedFramework.InterfacesOf).</summary>
public abstract record MetadataTypeSig;

/// <summary>A named type: "Ns.Name`n" (nested: its declaring type's), with type arguments when instantiated.</summary>
public sealed record MetadataNamed(string Namespace, string Name, MetadataNamed? Declaring, IReadOnlyList<MetadataTypeSig> Arguments) : MetadataTypeSig
{
    public string FullName => Declaring is { } d ? d.FullName + "+" + Name : Namespace.Length > 0 ? Namespace + "." + Name : Name;
}

/// <summary>The type's own generic parameter #Index (outer types' parameters first, as metadata numbers them).</summary>
public sealed record MetadataGenericParameter(int Index) : MetadataTypeSig;
public sealed record MetadataPrimitive(PrimitiveTypeCode Code) : MetadataTypeSig;
/// <summary>An array; Rank 0: one-dimensional, zero-based.</summary>
public sealed record MetadataArray(MetadataTypeSig Element, int Rank) : MetadataTypeSig;
public sealed record MetadataPointer(MetadataTypeSig Element) : MetadataTypeSig;
/// <summary>Something csharpc doesn't map (by-refs, function pointers, method generic parameters).</summary>
public sealed record MetadataUnsupported : MetadataTypeSig;

/// <summary>A type's declared interfaces (metadata order), the assembly defining it, and its generic parameters' variance (1 out, 2 in).</summary>
public sealed record TypeInterfaces(IReadOnlyList<MetadataTypeSig> Interfaces, string Assembly, IReadOnlyList<int> Variances);

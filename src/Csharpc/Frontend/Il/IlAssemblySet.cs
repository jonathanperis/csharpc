using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;

namespace Csharpc.Frontend.Il;

/// <summary>
/// The assemblies csharpc compiles from IL (docs/adr/0010-il-frontend.md): DLLs referenced without
/// sources (`-r`, project &lt;Reference&gt; items). Roslyn binds the program against them as ordinary
/// metadata references; this class connects Roslyn's PE symbols to the metadata they came from, so
/// codegen can ask for a method's IL body and resolve the tokens inside it back to Roslyn symbols.
/// </summary>
public sealed class IlAssemblySet : IDisposable
{
    private readonly List<IlAssembly> _assemblies = [];
    private readonly Dictionary<IAssemblySymbol, IlAssembly> _bySymbol = new(SymbolEqualityComparer.Default);

    public IReadOnlyList<IlAssembly> Assemblies => _assemblies;

    /// <summary>The metadata references Roslyn binds against (one per assembly).</summary>
    public ImmutableArray<MetadataReference> References => [.. _assemblies.Select(a => a.Reference)];

    public static IlAssemblySet Empty { get; } = new();

    public bool IsEmpty => _assemblies.Count == 0;

    /// <summary>
    /// Assemblies other than the precompiled corelib (ADR 0015): libraries the program references, whose
    /// failures become stubs and whose types join class-hierarchy analysis (ADR 0010).
    /// </summary>
    public bool HasLibraries => _assemblies.Any(a => !a.IsCoreLib);

    /// <summary>True for symbols of the precompiled corelib assembly.</summary>
    public bool IsCoreLib(ISymbol? symbol) => OwnerOf(symbol) is { IsCoreLib: true };

    /// <summary>Opens and validates the assemblies; throws <see cref="InvalidOperationException"/> with a user-facing message.</summary>
    public static IlAssemblySet Load(IEnumerable<string> paths)
    {
        var set = new IlAssemblySet();
        foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal))
        {
            if (!File.Exists(path)) throw new InvalidOperationException($"referenced assembly '{path}' not found");
            var bytes = File.ReadAllBytes(path);
            var pe = new PEReader(ImmutableArray.Create(bytes));
            if (!pe.HasMetadata) throw new InvalidOperationException($"'{path}' is not a .NET assembly");
            // ReadyToRun images (the shared framework) carry native code next to complete IL (ILLibrary);
            // C++/CLI mixed-mode assemblies have native method bodies instead.
            if (pe.PEHeaders.CorHeader is { } cor && (cor.Flags & (CorFlags.ILOnly | CorFlags.ILLibrary)) == 0)
                throw new InvalidOperationException($"'{path}' contains native code (mixed-mode / C++/CLI assembly); not supported by csharpc");
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) throw new InvalidOperationException($"'{path}' is a module, not an assembly");
            if (IsReferenceAssembly(reader))
                throw new InvalidOperationException($"'{path}' is a reference assembly (no method bodies); reference the implementation assembly instead");
            var reference = MetadataReference.CreateFromImage(bytes, filePath: path);
            set._assemblies.Add(new IlAssembly(path, pe, reader, reference));
        }
        return set;
    }

    private static bool IsReferenceAssembly(MetadataReader reader)
    {
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
            if (parent.Kind == HandleKind.TypeReference
                && reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name) == "ReferenceAssemblyAttribute")
                return true;
        }
        return false;
    }

    /// <summary>Connects every assembly to its symbol in the compilation codegen uses.</summary>
    public void Bind(Compilation compilation)
    {
        _bySymbol.Clear();
        var referenced = new Dictionary<string, IAssemblySymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var asm in compilation.SourceModule.ReferencedAssemblySymbols) referenced.TryAdd(asm.Identity.Name, asm);
        foreach (var assembly in _assemblies)
        {
            var symbol = compilation.GetAssemblyOrModuleSymbol(assembly.Reference) as IAssemblySymbol
                ?? throw new InvalidOperationException($"'{assembly.Path}' was not loaded by the compilation");
            assembly.Bind(compilation, symbol, referenced);
            _bySymbol[symbol] = assembly;
        }
    }

    /// <summary>The IL assembly a symbol is defined in, or null for source/library symbols.</summary>
    public IlAssembly? OwnerOf(ISymbol? symbol) =>
        symbol?.ContainingAssembly is { } asm && _bySymbol.TryGetValue(asm, out var owner) ? owner : null;

    public bool Contains(IAssemblySymbol? assembly) => assembly is not null && _bySymbol.ContainsKey(assembly);

    public void Dispose()
    {
        foreach (var a in _assemblies) a.Dispose();
    }
}

/// <summary>A type in a signature: Roslyn type plus the signature-only shapes Roslyn has no type for.</summary>
public sealed record IlType(ITypeSymbol Type, bool IsByRef = false, bool IsPinned = false)
{
    public override string ToString() => (IsPinned ? "pinned " : "") + Type.ToDisplayString() + (IsByRef ? "&" : "");
}

/// <summary>Generic parameters visible in a signature: !n (the type's, outermost first) and !!n (the method's).</summary>
public readonly record struct IlGenericContext(ImmutableArray<ITypeSymbol> TypeParameters, ImmutableArray<ITypeSymbol> MethodParameters)
{
    /// <summary>The context inside a definition: its containing types' parameters and its own.</summary>
    public static IlGenericContext For(ISymbol member)
    {
        var method = member as IMethodSymbol;
        var type = member as INamedTypeSymbol ?? member.ContainingType;
        return new IlGenericContext(TypeParametersOf(type?.OriginalDefinition),
            method is null ? [] : [.. method.OriginalDefinition.TypeParameters]);
    }

    public static ImmutableArray<ITypeSymbol> TypeParametersOf(INamedTypeSymbol? type)
    {
        if (type is null) return [];
        var chain = new List<INamedTypeSymbol>();
        for (var t = type; t is not null; t = t.ContainingType) chain.Add(t);
        chain.Reverse();
        return [.. chain.SelectMany(t => t.TypeParameters)];
    }
}

/// <summary>Raised for metadata or IL that csharpc cannot (or will not) interpret.</summary>
public sealed class IlException(string message) : Exception(message);

/// <summary>One assembly compiled from IL: its PE image, metadata, and the token ↔ symbol mapping.</summary>
public sealed class IlAssembly : IDisposable, ISignatureTypeProvider<IlType, IlGenericContext>
{
    public string Path { get; }
    public PEReader Pe { get; }
    public MetadataReader Reader { get; }
    public MetadataReference Reference { get; }
    public IAssemblySymbol Symbol { get; private set; } = null!;
    public Compilation Compilation { get; private set; } = null!;
    public string Name => Reader.GetString(Reader.GetAssemblyDefinition().Name);

    /// <summary>The precompiled corelib (ADR 0015): lowered from IL, but with the source corelib's policies.</summary>
    public bool IsCoreLib { get; }

    /// <summary>Decoded bodies by generic definition (IlMethodBody.For).</summary>
    internal Dictionary<IMethodSymbol, IlMethodBody> DecodedBodies { get; } = new(SymbolEqualityComparer.Default);

    private Dictionary<string, IAssemblySymbol> _referenced = [];
    private Dictionary<int, ISymbol>? _byToken;
    private readonly Dictionary<EntityHandle, INamedTypeSymbol> _typeRefs = [];
    private readonly Dictionary<(EntityHandle, IlGenericContext), ISymbol> _members = [];

    internal IlAssembly(string path, PEReader pe, MetadataReader reader, MetadataReference reference)
    {
        Path = path;
        Pe = pe;
        Reader = reader;
        Reference = reference;
        IsCoreLib = Name == CoreLibAssembly.Name;
    }

    internal void Bind(Compilation compilation, IAssemblySymbol symbol, Dictionary<string, IAssemblySymbol> referenced)
    {
        Compilation = compilation;
        Symbol = symbol;
        _referenced = referenced;
        _byToken = null;
        _typeRefs.Clear();
        _members.Clear();
        DecodedBodies.Clear();
    }

    /// <summary>
    /// The string entries of the assembly's embedded `.resources` (System.Resources.ResourceReader
    /// format): (base name, key, value), the base name being the manifest resource name without
    /// ".resources" — what `new ResourceManager(typeof(X))` / `("Ns.Resources", assembly)` look up.
    /// Other value types (streams, byte arrays, serialized objects) are not included.
    /// </summary>
    public IEnumerable<(string BaseName, string Key, string Value)> ReadStringResources()
    {
        if (Pe.PEHeaders.CorHeader is not { } cor || cor.ResourcesDirectory.Size == 0) yield break;
        var directory = Pe.GetSectionData(cor.ResourcesDirectory.RelativeVirtualAddress);
        foreach (var handle in Reader.ManifestResources)
        {
            var resource = Reader.GetManifestResource(handle);
            if (!resource.Implementation.IsNil) continue; // linked or in another assembly
            var name = Reader.GetString(resource.Name);
            if (!name.EndsWith(".resources", StringComparison.Ordinal)) continue;
            var block = directory.GetReader((int)resource.Offset, directory.Length - (int)resource.Offset);
            var bytes = block.ReadBytes(block.ReadInt32());
            List<(string, string, string)> entries = [];
            try
            {
                using var reader = new System.Resources.ResourceReader(new MemoryStream(bytes));
                var e = reader.GetEnumerator();
                while (e.MoveNext())
                {
                    var key = (string)e.Key;
                    reader.GetResourceData(key, out var type, out var data);
                    if (type != "ResourceTypeCode.String") continue;
                    using var value = new BinaryReader(new MemoryStream(data));
                    entries.Add((name[..^".resources".Length], key, value.ReadString()));
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException or ArgumentException or FormatException)
            {
                throw new IlException($"embedded resource '{name}' of {Name}.dll is not in the .resources format: {ex.Message}");
            }
            foreach (var entry in entries) yield return entry;
        }
    }

    public void Dispose() => Pe.Dispose();

    // ======================================================================================
    // Definitions: token -> symbol
    // ======================================================================================

    private Dictionary<int, ISymbol> ByToken => _byToken ??= IndexTokens();

    private Dictionary<int, ISymbol> IndexTokens()
    {
        var index = new Dictionary<int, ISymbol>();
        void AddType(INamedTypeSymbol type)
        {
            if (type.MetadataToken != 0) index[type.MetadataToken] = type;
            foreach (var member in type.GetMembers())
            {
                if (member is INamedTypeSymbol nested) AddType(nested);
                else if (member.MetadataToken != 0) index.TryAdd(member.MetadataToken, member);
            }
        }
        void AddNamespace(INamespaceSymbol ns)
        {
            foreach (var member in ns.GetMembers())
                if (member is INamespaceSymbol child) AddNamespace(child);
                else if (member is INamedTypeSymbol type) AddType(type);
        }
        AddNamespace(Symbol.GlobalNamespace);
        return index;
    }

    public ISymbol? Definition(EntityHandle handle) => ByToken.GetValueOrDefault(MetadataTokens.GetToken(handle));

    public TypeDefinitionHandle TypeHandle(INamedTypeSymbol type) =>
        MetadataTokens.TypeDefinitionHandle(MetadataTokens.GetRowNumber(MetadataTokens.EntityHandle(type.OriginalDefinition.MetadataToken)));

    public TypeDefinition TypeDefinitionOf(INamedTypeSymbol type) => Reader.GetTypeDefinition(TypeHandle(type));

    public MethodDefinition MethodDefinitionOf(IMethodSymbol method) =>
        Reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(MetadataTokens.GetRowNumber(MetadataTokens.EntityHandle(method.OriginalDefinition.MetadataToken))));

    public FieldDefinition FieldDefinitionOf(IFieldSymbol field) =>
        Reader.GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(MetadataTokens.GetRowNumber(MetadataTokens.EntityHandle(field.OriginalDefinition.MetadataToken))));

    /// <summary>The IL body of a method defined in this assembly (null: abstract, extern, runtime-implemented).</summary>
    public MethodBodyBlock? Body(IMethodSymbol method)
    {
        if (method.OriginalDefinition.MetadataToken == 0) throw new IlException($"'{method}' has no metadata definition (synthesized by Roslyn)");
        var definition = MethodDefinitionOf(method);
        return definition.RelativeVirtualAddress == 0 ? null : Pe.GetMethodBody(definition.RelativeVirtualAddress);
    }

    /// <summary>The initial data of a field with an RVA (array initializers, UTF-8 / span literals).</summary>
    public byte[] FieldData(IFieldSymbol field, int size)
    {
        var definition = FieldDefinitionOf(field);
        var rva = definition.GetRelativeVirtualAddress();
        if (rva == 0) throw new IlException($"field '{field}' has no initial data (RVA)");
        var block = Pe.GetSectionData(rva);
        if (block.Length < size) throw new IlException($"field '{field}': its RVA data is shorter than {size} bytes");
        return block.GetContent(0, size).ToArray();
    }

    /// <summary>
    /// Roslyn hides a field-like event's backing field (same name as the event) from its PE symbols;
    /// IL accessors still read and write it. The event symbol stands for the field (its storage slot).
    /// </summary>
    private IEventSymbol? EventOfBackingField(FieldDefinitionHandle handle)
    {
        var field = Reader.GetFieldDefinition(handle);
        var name = Reader.GetString(field.Name);
        return Definition(field.GetDeclaringType()) is INamedTypeSymbol owner
            ? owner.GetMembers(name).OfType<IEventSymbol>().FirstOrDefault()
            : null;
    }

    /// <summary>Does the event have a backing field (a field-like event compiled from C#)?</summary>
    public bool HasBackingField(IEventSymbol e)
    {
        var type = TypeDefinitionOf(e.ContainingType);
        foreach (var handle in type.GetFields())
            if (Reader.StringComparer.Equals(Reader.GetFieldDefinition(handle).Name, e.Name)) return true;
        return false;
    }

    /// <summary>Explicit class size (.size) of a type, 0 if none.</summary>
    public int ClassSize(INamedTypeSymbol type) => TypeDefinitionOf(type).GetLayout().Size;

    public string UserString(int token) => Reader.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF));

    // ======================================================================================
    // Tokens in method bodies -> symbols
    // ======================================================================================

    public ITypeSymbol ResolveType(int token, IlGenericContext context) => ResolveType(MetadataTokens.EntityHandle(token), context).Type;

    public IlType ResolveType(EntityHandle handle, IlGenericContext context)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return new IlType(Definition(handle) as INamedTypeSymbol
                    ?? throw new IlException($"type definition 0x{MetadataTokens.GetToken(handle):X8} has no symbol"));
            case HandleKind.TypeReference:
                return new IlType(ResolveTypeReference((TypeReferenceHandle)handle));
            case HandleKind.TypeSpecification:
                return Reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, context);
            default:
                throw new IlException($"token 0x{MetadataTokens.GetToken(handle):X8} is not a type");
        }
    }

    private INamedTypeSymbol ResolveTypeReference(TypeReferenceHandle handle)
    {
        if (_typeRefs.TryGetValue(handle, out var cached)) return cached;
        var reference = Reader.GetTypeReference(handle);
        var name = Reader.GetString(reference.Name);
        var ns = Reader.GetString(reference.Namespace);
        var full = ns.Length > 0 ? ns + "." + name : name;
        INamedTypeSymbol? result;
        switch (reference.ResolutionScope.Kind)
        {
            case HandleKind.TypeReference:
                var outer = ResolveTypeReference((TypeReferenceHandle)reference.ResolutionScope);
                result = outer.GetTypeMembers().FirstOrDefault(t => t.MetadataName == name);
                break;
            case HandleKind.AssemblyReference:
                var assemblyName = Reader.GetString(Reader.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name);
                if (!_referenced.TryGetValue(assemblyName, out var assembly))
                    throw new IlException($"'{full}' is defined in assembly '{assemblyName}', which '{Name}' references but csharpc was not given (pass it with -r)");
                result = assembly.GetTypeByMetadataName(full) ?? assembly.ResolveForwardedType(full) ?? Compilation.GetTypeByMetadataName(full);
                break;
            case HandleKind.ModuleDefinition:
                result = Symbol.GetTypeByMetadataName(full);
                break;
            default:
                throw new IlException($"type reference '{full}' with a {reference.ResolutionScope.Kind} scope (multi-module assembly)");
        }
        return _typeRefs[handle] = result ?? throw new IlException($"type '{full}' referenced by '{Name}' was not found");
    }

    /// <summary>A field, method or constructor token (FieldDef, MethodDef, MemberRef, MethodSpec).</summary>
    public ISymbol ResolveMember(int token, IlGenericContext context)
    {
        var handle = MetadataTokens.EntityHandle(token);
        if (_members.TryGetValue((handle, context), out var cached)) return cached;
        ISymbol result = handle.Kind switch
        {
            HandleKind.MethodDefinition or HandleKind.FieldDefinition =>
                Definition(handle) ?? (handle.Kind == HandleKind.FieldDefinition ? EventOfBackingField((FieldDefinitionHandle)handle) : null)
                ?? throw new IlException($"member 0x{token:X8} has no symbol (unsupported signature?)"),
            HandleKind.MemberReference => ResolveMemberReference((MemberReferenceHandle)handle, context),
            HandleKind.MethodSpecification => ResolveMethodSpecification((MethodSpecificationHandle)handle, context),
            _ => throw new IlException($"token 0x{token:X8} is not a member"),
        };
        return _members[(handle, context)] = result;
    }

    private IMethodSymbol ResolveMethodSpecification(MethodSpecificationHandle handle, IlGenericContext context)
    {
        var spec = Reader.GetMethodSpecification(handle);
        var method = ResolveMember(MetadataTokens.GetToken(spec.Method), context) as IMethodSymbol
            ?? throw new IlException("method specification of a non-method");
        var arguments = spec.DecodeSignature(this, context);
        if (arguments.Any(a => a.IsByRef)) throw new IlException($"by-ref type argument for '{method}'");
        return method.Construct([.. arguments.Select(a => a.Type)]);
    }

    /// <summary>
    /// A method of an array type itself (a MemberRef whose parent is an array TypeSpec): the runtime-provided
    /// constructor, Get, Set and Address of rectangular arrays, which have no symbol in Roslyn.
    /// </summary>
    public bool TryArrayAccessor(int token, IlGenericContext context, out IArrayTypeSymbol array, out string name, out int parameterCount)
    {
        array = null!;
        name = "";
        parameterCount = 0;
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind != HandleKind.MemberReference) return false;
        var reference = Reader.GetMemberReference((MemberReferenceHandle)handle);
        if (reference.Parent.Kind != HandleKind.TypeSpecification || reference.GetKind() != MemberReferenceKind.Method) return false;
        if (ResolveType(reference.Parent, context).Type is not IArrayTypeSymbol arrayType) return false;
        array = arrayType;
        name = Reader.GetString(reference.Name);
        parameterCount = reference.DecodeMethodSignature(this, context).ParameterTypes.Length;
        return true;
    }

    private ISymbol ResolveMemberReference(MemberReferenceHandle handle, IlGenericContext context)
    {
        var reference = Reader.GetMemberReference(handle);
        var name = Reader.GetString(reference.Name);
        var parent = reference.Parent.Kind switch
        {
            HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification => ResolveType(reference.Parent, context).Type,
            HandleKind.MethodDefinition => throw new IlException($"vararg call site of '{name}'"),
            _ => throw new IlException($"member reference '{name}' with a {reference.Parent.Kind} parent"),
        };
        if (parent is IArrayTypeSymbol array)
            throw new IlException($"multi-dimensional array accessor '{array}::{name}'");
        if (parent is not INamedTypeSymbol owner) throw new IlException($"member '{name}' of '{parent}'");
        var definition = owner.OriginalDefinition;
        var ownerContext = new IlGenericContext(IlGenericContext.TypeParametersOf(definition), []);

        if (reference.GetKind() == MemberReferenceKind.Field)
        {
            var fieldType = reference.DecodeFieldSignature(this, ownerContext);
            for (INamedTypeSymbol? t = definition; t is not null; t = t.BaseType?.OriginalDefinition)
            {
                var field = t.GetMembers(name).OfType<IFieldSymbol>().FirstOrDefault(f => SameType(f.Type, fieldType.Type))
                    ?? t.GetMembers(name).OfType<IFieldSymbol>().FirstOrDefault();
                if (field is not null) return OnConstructed(owner, field);
            }
            throw new IlException($"field '{owner}.{name}' not found");
        }

        var blobReader = Reader.GetBlobReader(reference.Signature);
        var header = Reader.GetBlobReader(reference.Signature).ReadSignatureHeader();
        if (header.CallingConvention == SignatureCallingConvention.VarArgs) throw new IlException($"vararg method '{owner}.{name}'");
        var arity = header.IsGeneric ? PeekGenericArity(blobReader) : 0;
        for (INamedTypeSymbol? t = definition; t is not null; t = t.BaseType?.OriginalDefinition)
        {
            var typeContext = new IlGenericContext(IlGenericContext.TypeParametersOf(t), []);
            foreach (var candidate in t.GetMembers(name).OfType<IMethodSymbol>())
            {
                if (candidate.Arity != arity || candidate.IsStatic == header.IsInstance) continue;
                var signature = reference.DecodeMethodSignature(this, typeContext with { MethodParameters = [.. candidate.TypeParameters] });
                if (Matches(candidate, signature)) return OnConstructed(owner, candidate);
            }
        }
        throw new IlException($"method '{owner}.{name}' with the referenced signature not found");
    }

    private static int PeekGenericArity(BlobReader blob)
    {
        blob.ReadSignatureHeader();
        return blob.ReadCompressedInteger();
    }

    private static bool Matches(IMethodSymbol candidate, MethodSignature<IlType> signature)
    {
        if (candidate.Parameters.Length != signature.ParameterTypes.Length) return false;
        for (var i = 0; i < signature.ParameterTypes.Length; i++)
        {
            var p = candidate.Parameters[i];
            var s = signature.ParameterTypes[i];
            if ((p.RefKind != RefKind.None) != s.IsByRef || !SameType(p.Type, s.Type)) return false;
        }
        var returnsByRef = candidate.RefKind != RefKind.None;
        if (candidate.MethodKind == MethodKind.Constructor) return signature.ReturnType.Type.SpecialType == SpecialType.System_Void;
        return returnsByRef == signature.ReturnType.IsByRef && SameType(candidate.ReturnType, signature.ReturnType.Type);
    }

    /// <summary>The member of a (possibly constructed) type that corresponds to a definition's member.</summary>
    private static ISymbol OnConstructed(INamedTypeSymbol owner, ISymbol definitionMember)
    {
        // An inherited member found on a base: look it up on the corresponding constructed base type.
        for (INamedTypeSymbol? t = owner; t is not null; t = t.BaseType)
        {
            if (!SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, definitionMember.ContainingType.OriginalDefinition)) continue;
            if (SymbolEqualityComparer.Default.Equals(t, t.OriginalDefinition)) return definitionMember;
            return t.GetMembers(definitionMember.Name).First(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, definitionMember.OriginalDefinition));
        }
        return definitionMember;
    }

    /// <summary>Type identity as metadata sees it: no tuple names, dynamic is object, nint is IntPtr.</summary>
    public static bool SameType(ITypeSymbol a, ITypeSymbol b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.TypeKind == TypeKind.Dynamic) return b.TypeKind == TypeKind.Dynamic || b.SpecialType == SpecialType.System_Object;
        if (b.TypeKind == TypeKind.Dynamic) return a.SpecialType == SpecialType.System_Object;
        switch (a)
        {
            case IArrayTypeSymbol aa:
                return b is IArrayTypeSymbol ba && aa.Rank == ba.Rank && SameType(aa.ElementType, ba.ElementType);
            case IPointerTypeSymbol ap:
                return b is IPointerTypeSymbol bp && SameType(ap.PointedAtType, bp.PointedAtType);
            case IFunctionPointerTypeSymbol:
                return b is IFunctionPointerTypeSymbol;
            case ITypeParameterSymbol:
                return SymbolEqualityComparer.Default.Equals(a, b);
            case INamedTypeSymbol an when b is INamedTypeSymbol bn:
                if (an.SpecialType != SpecialType.None || bn.SpecialType != SpecialType.None) return an.SpecialType == bn.SpecialType;
                if (!SymbolEqualityComparer.Default.Equals(an.OriginalDefinition, bn.OriginalDefinition)) return false;
                if (an.ContainingType is { } ac && bn.ContainingType is { } bc && !SameType(ac, bc)) return false;
                var at = an.TypeArguments;
                var bt = bn.TypeArguments;
                if (at.Length != bt.Length) return false;
                for (var i = 0; i < at.Length; i++)
                    if (!SameType(at[i], bt[i])) return false;
                return true;
            default:
                return SymbolEqualityComparer.Default.Equals(a, b);
        }
    }

    // ======================================================================================
    // ISignatureTypeProvider: signatures -> Roslyn types
    // ======================================================================================

    public IlType GetPrimitiveType(PrimitiveTypeCode typeCode) => new(Compilation.GetSpecialType(typeCode switch
    {
        PrimitiveTypeCode.Boolean => SpecialType.System_Boolean,
        PrimitiveTypeCode.Byte => SpecialType.System_Byte,
        PrimitiveTypeCode.SByte => SpecialType.System_SByte,
        PrimitiveTypeCode.Char => SpecialType.System_Char,
        PrimitiveTypeCode.Int16 => SpecialType.System_Int16,
        PrimitiveTypeCode.UInt16 => SpecialType.System_UInt16,
        PrimitiveTypeCode.Int32 => SpecialType.System_Int32,
        PrimitiveTypeCode.UInt32 => SpecialType.System_UInt32,
        PrimitiveTypeCode.Int64 => SpecialType.System_Int64,
        PrimitiveTypeCode.UInt64 => SpecialType.System_UInt64,
        PrimitiveTypeCode.Single => SpecialType.System_Single,
        PrimitiveTypeCode.Double => SpecialType.System_Double,
        PrimitiveTypeCode.IntPtr => SpecialType.System_IntPtr,
        PrimitiveTypeCode.UIntPtr => SpecialType.System_UIntPtr,
        PrimitiveTypeCode.Object => SpecialType.System_Object,
        PrimitiveTypeCode.String => SpecialType.System_String,
        PrimitiveTypeCode.Void => SpecialType.System_Void,
        PrimitiveTypeCode.TypedReference => SpecialType.System_TypedReference,
        _ => throw new IlException($"primitive type {typeCode}"),
    }));

    public IlType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        ResolveType(handle, default);

    public IlType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        new(ResolveTypeReference(handle));

    public IlType GetTypeFromSpecification(MetadataReader reader, IlGenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public IlType GetSZArrayType(IlType elementType) => new(Compilation.CreateArrayTypeSymbol(NoByRef(elementType)));

    public IlType GetArrayType(IlType elementType, ArrayShape shape) => new(Compilation.CreateArrayTypeSymbol(NoByRef(elementType), shape.Rank));

    public IlType GetByReferenceType(IlType elementType) => elementType with { IsByRef = true };

    public IlType GetPointerType(IlType elementType) => new(Compilation.CreatePointerTypeSymbol(NoByRef(elementType)));

    public IlType GetPinnedType(IlType elementType) => elementType with { IsPinned = true };

    public IlType GetModifiedType(IlType modifier, IlType unmodifiedType, bool isRequired) => unmodifiedType;

    public IlType GetGenericInstantiation(IlType genericType, ImmutableArray<IlType> typeArguments) =>
        new(Construct((INamedTypeSymbol)genericType.Type, [.. typeArguments.Select(NoByRef)]));

    public IlType GetGenericTypeParameter(IlGenericContext genericContext, int index) =>
        index < genericContext.TypeParameters.Length ? new(genericContext.TypeParameters[index])
            : throw new IlException($"type parameter !{index} out of range");

    public IlType GetGenericMethodParameter(IlGenericContext genericContext, int index) =>
        index < genericContext.MethodParameters.Length ? new(genericContext.MethodParameters[index])
            : throw new IlException($"method type parameter !!{index} out of range");

    public IlType GetFunctionPointerType(MethodSignature<IlType> signature) =>
        new(Compilation.CreateFunctionPointerTypeSymbol(
            signature.ReturnType.Type, signature.ReturnType.IsByRef ? RefKind.Ref : RefKind.None,
            [.. signature.ParameterTypes.Select(p => p.Type)], [.. signature.ParameterTypes.Select(p => p.IsByRef ? RefKind.Ref : RefKind.None)],
            signature.Header.CallingConvention, []));

    private static ITypeSymbol NoByRef(IlType type) =>
        type.IsByRef ? throw new IlException($"by-ref type '{type}' used as a type argument or element") : type.Type;

    /// <summary>
    /// Metadata instantiates a nested generic with all type arguments at once (Outer`1/Inner`1&lt;A, B&gt;);
    /// Roslyn constructs each level of the containing chain with its own arguments.
    /// </summary>
    private static INamedTypeSymbol Construct(INamedTypeSymbol definition, ITypeSymbol[] arguments)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var t = definition.OriginalDefinition; t is not null; t = t.ContainingType) chain.Add(t);
        chain.Reverse();
        if (chain.Sum(t => t.Arity) != arguments.Length)
            throw new IlException($"'{definition}' instantiated with {arguments.Length} type argument(s)");
        INamedTypeSymbol? current = null;
        var next = 0;
        foreach (var level in chain)
        {
            var levelDefinition = current is null ? level : current.GetTypeMembers(level.Name, level.Arity).First();
            current = level.Arity == 0 ? levelDefinition : levelDefinition.Construct(arguments[next..(next + level.Arity)]);
            next += level.Arity;
        }
        return current!;
    }

    /// <summary>Local variable types of a body (by-ref / pinned shapes kept).</summary>
    public ImmutableArray<IlType> DecodeLocals(MethodBodyBlock body, IlGenericContext context) =>
        body.LocalSignature.IsNil ? [] : Reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(this, context);

    /// <summary>The signature of a `calli` (a StandAloneSig token).</summary>
    public MethodSignature<IlType> DecodeCallSiteSignature(int token, IlGenericContext context) =>
        Reader.GetStandaloneSignature((StandaloneSignatureHandle)MetadataTokens.EntityHandle(token)).DecodeMethodSignature(this, context);

    /// <summary>The definition's own type attributes (layout kind, beforefieldinit, ...).</summary>
    public TypeAttributes AttributesOf(INamedTypeSymbol type) => TypeDefinitionOf(type).Attributes;
}
